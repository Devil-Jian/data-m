//! S6: transactional lifecycle of an AssetLink, not of Blob or AssetVersion.
use crate::project_tree::{database_path, with_database_at, TreeError};
use rusqlite::{params, Connection, OptionalExtension, TransactionBehavior};
use serde::{Deserialize, Serialize};
use std::{collections::{HashMap,HashSet}, path::Path};

#[derive(Debug,Clone,Serialize,Deserialize,PartialEq,Eq)]
#[serde(rename_all="camelCase",deny_unknown_fields)]
pub(super) struct LinkIdentity {
    pub asset_link_id:String, pub asset_id:String, pub asset_version_id:String,
    pub assay_id:String, pub stage:String, pub group_id:Option<String>, pub replicate_id:Option<String>,
}
#[derive(Debug,Clone,Serialize,Deserialize)]
#[serde(rename_all="camelCase",deny_unknown_fields)]
pub struct LifecycleRequest {
    action:String, request_id:String,
    item:Option<LinkIdentity>, items:Option<Vec<LinkIdentity>>, new_name:Option<String>,
}
#[derive(Debug,Clone,Serialize,Deserialize,PartialEq,Eq)]
#[serde(rename_all="camelCase")]
pub(super) struct LifecycleItem {
    pub asset_link_id:String, pub asset_id:String, pub asset_version_id:String,
    pub name:String, pub deleted_at:Option<String>,
}
#[derive(Debug,Clone,Serialize,Deserialize,PartialEq,Eq)]
#[serde(rename_all="camelCase")]
pub struct LifecycleResult {
    request_id:String, action:String, results:Vec<LifecycleItem>,
}
#[derive(Clone)]
struct Current {
    source:LinkIdentity, name:String, deleted_at:Option<String>,
}
pub(super) fn fail(code:&'static str)->TreeError {
    let message=match code {
        "INVALID_REQUEST"=>"Select one to one hundred asset associations in their original context.",
        "INVALID_NAME"=>"Use a valid Windows filename (no path characters, reserved names or trailing dots/spaces).",
        "NOT_FOUND"=>"The selected association no longer exists.",
        "STALE_SELECTION"=>"The link, asset, version or source location changed. Refresh and retry.",
        "INVALID_TARGET"=>"The original assay, group or sample is no longer active.",
        "NAME_CONFLICT"=>"An active file in this container already uses that name.",
        "REFERENCE_PROTECTED"=>"The last active association is protected by a paper version reference.",
        "ALREADY_DELETED"=>"This association was already recycled. Refresh the list.",
        "ALREADY_ACTIVE"=>"This association is already active. Refresh the recycle view.",
        "REQUEST_CONFLICT"=>"This request ID was used for a different operation.",
        _=>"The asset lifecycle operation could not be completed.",
    };
    TreeError::new(code,message)
}
pub(super) fn valid_id(value:&str)->bool {
    !value.is_empty()&&value.len()<=128&&value.bytes().all(|c|c.is_ascii_alphanumeric()||c==b'_'||c==b'-')
}
pub(super) fn valid_scope(s:&LinkIdentity)->bool {
    valid_id(&s.asset_link_id)&&valid_id(&s.asset_id)&&valid_id(&s.asset_version_id)
        &&valid_id(&s.assay_id)&&matches!(s.stage.as_str(),"raw"|"processed"|"statistics"|"display")
        &&s.group_id.as_deref().is_none_or(valid_id)&&s.replicate_id.as_deref().is_none_or(valid_id)
        &&if matches!(s.stage.as_str(),"raw"|"processed"){s.group_id.is_some()&&s.replicate_id.is_some()}
          else{s.group_id.is_none()&&s.replicate_id.is_none()}
}
fn combining_mark(ch:char)->bool {
    matches!(ch,
       '\u{0300}'..='\u{036f}'|'\u{1ab0}'..='\u{1aff}'|'\u{1dc0}'..='\u{1dff}'|
       '\u{20d0}'..='\u{20ff}'|'\u{fe20}'..='\u{fe2f}'|
       '\u{1100}'..='\u{11ff}'|'\u{a960}'..='\u{a97f}'|'\u{d7b0}'..='\u{d7ff}')
}
pub(super) fn valid_name(name:&str)->bool {
    if name.is_empty()||name.encode_utf16().count()>255||name.trim()!=name||name.ends_with('.')||
      name=="."||name==".."||name.chars().any(|ch|{
          ch.is_control()||combining_mark(ch)||
          matches!(ch,'/'|'\\'|':'|'*'|'?'|'"'|'<'|'>'|'|'|
              '\u{200b}'..='\u{200f}'|'\u{202a}'..='\u{202e}'|
              '\u{2060}'..='\u{206f}'|'\u{feff}'|'\u{0130}'|'\u{00df}'|'\u{1e9e}')
      }) {return false;}
    let root=name.split('.').next().unwrap_or("").to_uppercase();
    if matches!(root.as_str(),"CON"|"PRN"|"AUX"|"NUL") {return false;}
    // Windows also reserves COM¹..COM³ and LPT¹..LPT³.
    let mut chars=root.chars();
    if let (Some('C'),Some('O'),Some('M'),Some(d))=(chars.next(),chars.next(),chars.next(),chars.next()){
        if chars.next().is_none()&&matches!(d,'1'..='9'|'¹'|'²'|'³'){return false;}
    }
    let mut chars=root.chars();
    if let (Some('L'),Some('P'),Some('T'),Some(d))=(chars.next(),chars.next(),chars.next(),chars.next()){
        if chars.next().is_none()&&matches!(d,'1'..='9'|'¹'|'²'|'³'){return false;}
    }
    true
}
fn validate(req:&LifecycleRequest)->Result<Vec<LinkIdentity>,TreeError> {
    if !valid_id(&req.request_id){return Err(fail("INVALID_REQUEST"));}
    let sources=match req.action.as_str(){
        "rename" if req.items.is_none()&&req.item.is_some()&&req.new_name.as_deref().is_some_and(valid_name)=>
            vec![req.item.clone().unwrap()],
        "rename" if req.items.is_none()&&req.item.is_some()=>return Err(fail("INVALID_NAME")),
        "soft_delete"|"restore" if req.item.is_none()&&req.new_name.is_none()=>
            req.items.clone().ok_or_else(||fail("INVALID_REQUEST"))?,
        _=>return Err(fail("INVALID_REQUEST"))
    };
    if sources.is_empty()||sources.len()>100||sources.iter().any(|s|!valid_scope(s))||
       sources.iter().map(|s|s.asset_link_id.as_str()).collect::<HashSet<_>>().len()!=sources.len(){
        return Err(fail("INVALID_REQUEST"));
    }
    Ok(sources)
}
pub(super) fn active_target(c:&Connection,s:&LinkIdentity)->Result<bool,TreeError>{
    let assay:bool=c.query_row("SELECT EXISTS(SELECT 1 FROM assays a JOIN themes t ON t.id=a.theme_id
        JOIN projects p ON p.id=t.project_id WHERE a.id=?1 AND a.deleted_at IS NULL
        AND t.deleted_at IS NULL AND p.deleted_at IS NULL)",[&s.assay_id],|r|r.get(0))?;
    if !assay{return Ok(false);}
    if s.stage=="raw"||s.stage=="processed" {
        c.query_row("SELECT EXISTS(SELECT 1 FROM experiment_groups g JOIN replicates r ON r.group_id=g.id
            WHERE g.id=?1 AND r.id=?2 AND g.assay_id=?3 AND g.deleted_at IS NULL AND r.deleted_at IS NULL)",
            params![s.group_id,s.replicate_id,s.assay_id],|r|r.get(0)).map_err(Into::into)
    } else{Ok(true)}
}
fn current(c:&Connection,expected:&LinkIdentity,action:&str)->Result<Current,TreeError>{
    let row=c.query_row("SELECT l.asset_id,v.id,l.assay_id,l.stage,l.group_id,l.replicate_id,
        l.display_name,l.deleted_at FROM asset_links l JOIN data_assets a ON a.id=l.asset_id
        JOIN asset_versions v ON v.asset_id=a.id AND v.version_number=
        (SELECT MAX(x.version_number) FROM asset_versions x WHERE x.asset_id=a.id)
        WHERE l.id=?1 AND a.deleted_at IS NULL",
        [&expected.asset_link_id],|r|{
            Ok(Current{source:LinkIdentity{
                asset_link_id:expected.asset_link_id.clone(),asset_id:r.get(0)?,asset_version_id:r.get(1)?,
                assay_id:r.get(2)?,stage:r.get(3)?,group_id:r.get(4)?,replicate_id:r.get(5)?},
                name:r.get(6)?,deleted_at:r.get(7)?})
        }).optional()?.ok_or_else(||fail("NOT_FOUND"))?;
    if row.source!=*expected{return Err(fail("STALE_SELECTION"));}
    if !active_target(c,&row.source)?{return Err(fail("INVALID_TARGET"));}
    if action=="restore"&&row.deleted_at.is_none(){return Err(fail("ALREADY_ACTIVE"));}
    if action!="restore"&&row.deleted_at.is_some(){return Err(fail("ALREADY_DELETED"));}
    Ok(row)
}
pub(super) fn name_conflict(c:&Connection,s:&LinkIdentity,name:&str,except:&str)->Result<bool,TreeError>{
    let mut stmt=c.prepare("SELECT id,display_name FROM asset_links WHERE deleted_at IS NULL
        AND assay_id=?1 AND stage=?2 AND COALESCE(group_id,'')=COALESCE(?3,'')
        AND COALESCE(replicate_id,'')=COALESCE(?4,'')")?;
    let names=stmt.query_map(params![s.assay_id,s.stage,s.group_id,s.replicate_id],
        |r|Ok((r.get::<_,String>(0)?,r.get::<_,String>(1)?)))?;
    let key=name.to_lowercase();
    for row in names{
        let (id,other)=row?;
        if id!=except&&other.to_lowercase()==key{return Ok(true);}
    }
    Ok(false)
}
fn referenced(c:&Connection,asset_id:&str)->Result<bool,TreeError>{
    c.query_row("SELECT EXISTS(
        SELECT 1 FROM asset_versions v JOIN panel_asset_versions p ON p.asset_version_id=v.id WHERE v.asset_id=?1
        UNION ALL
        SELECT 1 FROM asset_versions v JOIN representative_sources r ON r.asset_version_id=v.id WHERE v.asset_id=?1
        )",[asset_id],|r|r.get(0)).map_err(Into::into)
}
pub(super) fn execute_at(path:&Path,req:LifecycleRequest)->Result<LifecycleResult,TreeError>{
    let sources=validate(&req)?;
    with_database_at(path,|c|{
        let tx=c.transaction_with_behavior(TransactionBehavior::Immediate)?;
        let payload=serde_json::to_string(&req).map_err(|_|fail("INVALID_REQUEST"))?;
        let previous:Option<String>=tx.query_row("SELECT payload FROM s1_requests WHERE request_id=?1",
            [&req.request_id],|r|r.get(0)).optional()?;
        let audit_id=format!("s6:{}",req.request_id);
        if let Some(previous)=previous {
            if previous!=payload{return Err(fail("REQUEST_CONFLICT"));}
            let response:Option<String>=tx.query_row("SELECT details_json FROM audit_events
                WHERE id=?1 AND object_type='asset_link_lifecycle'",
                [&audit_id],|r|r.get(0)).optional()?;
            return serde_json::from_str(response.as_deref().ok_or_else(||fail("REQUEST_CONFLICT"))?)
                .map_err(|_|fail("REQUEST_CONFLICT"));
        }
        let mut rows=Vec::with_capacity(sources.len());
        for s in &sources{rows.push(current(&tx,s,&req.action)?);}
        if req.action=="rename"||req.action=="restore" {
            for row in &rows{
                let name=if req.action=="rename"{req.new_name.as_deref().unwrap()}else{&row.name};
                if name_conflict(&tx,&row.source,name,&row.source.asset_link_id)?{
                    return Err(fail("NAME_CONFLICT"));
                }
            }
        }
        if req.action=="soft_delete" {
            let mut requested:HashMap<&str,i64>=HashMap::new();
            for row in &rows{*requested.entry(row.source.asset_id.as_str()).or_default()+=1;}
            for (asset,n) in requested{
                if referenced(&tx,asset)? {
                    let active:i64=tx.query_row("SELECT count(*) FROM asset_links WHERE asset_id=?1
                        AND deleted_at IS NULL",[asset],|r|r.get(0))?;
                    if active<=n{return Err(fail("REFERENCE_PROTECTED"));}
                }
            }
        }
        let mut result=Vec::with_capacity(rows.len());
        for row in &rows{
            match req.action.as_str(){
                "rename"=>{tx.execute("UPDATE asset_links SET display_name=?1 WHERE id=?2 AND deleted_at IS NULL",
                    params![req.new_name,row.source.asset_link_id])?;}
                "soft_delete"=>{tx.execute("UPDATE asset_links SET deleted_at=strftime('%Y-%m-%dT%H:%M:%fZ','now')
                    WHERE id=?1 AND deleted_at IS NULL",[&row.source.asset_link_id])?;}
                "restore"=>{tx.execute("UPDATE asset_links SET deleted_at=NULL WHERE id=?1 AND deleted_at IS NOT NULL",
                    [&row.source.asset_link_id])?;}
                _=>return Err(fail("INVALID_REQUEST"))
            }
            let (name,deleted_at):(String,Option<String>)=tx.query_row(
                "SELECT display_name,deleted_at FROM asset_links WHERE id=?1",
                [&row.source.asset_link_id],|r|Ok((r.get(0)?,r.get(1)?)))?;
            result.push(LifecycleItem{asset_link_id:row.source.asset_link_id.clone(),
                asset_id:row.source.asset_id.clone(),asset_version_id:row.source.asset_version_id.clone(),
                name,deleted_at});
        }
        let response=LifecycleResult{request_id:req.request_id.clone(),action:req.action.clone(),results:result};
        let data=serde_json::to_string(&response).map_err(|_|fail("INVALID_REQUEST"))?;
        tx.execute("INSERT INTO audit_events(id,action,object_type,object_id,details_json)
            VALUES(?1,?2,'asset_link_lifecycle',?3,?4)",
            params![audit_id,format!("asset_link_{}",req.action),req.request_id,data])?;
        tx.execute("INSERT INTO s1_requests(request_id,payload) VALUES(?1,?2)",
            params![req.request_id,payload])?;
        tx.commit()?;
        Ok(response)
    })
}
#[tauri::command]
pub fn asset_lifecycle<R:tauri::Runtime>(
    app:tauri::AppHandle<R>,request:LifecycleRequest
)->Result<LifecycleResult,TreeError>{
    execute_at(&database_path(&app)?,request)
}

#[cfg(test)]
#[path="asset_lifecycle_tests.rs"]
mod tests;
