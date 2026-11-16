//! S6 recycle-bin listing; only deleted AssetLinks, no CAS paths.
use crate::asset_lifecycle::{active_target,fail,name_conflict,valid_id,LinkIdentity};
use crate::project_tree::{database_path,with_database_at,TreeError};
use rusqlite::{params,Connection};
use serde::{Deserialize,Serialize};
use std::path::Path;

#[derive(Debug,Deserialize)]
#[serde(rename_all="camelCase",deny_unknown_fields)]
pub struct RecycleQuery{
    assay_id:String,stage:Option<String>,group_id:Option<String>,replicate_id:Option<String>,
    search:Option<String>,cursor:Option<String>,limit:Option<u32>
}
#[derive(Debug,Serialize)]
#[serde(rename_all="camelCase")]
pub struct RecycledRow{
    asset_link_id:String,asset_id:String,asset_version_id:String,
    assay_id:String,stage:String,group_id:Option<String>,replicate_id:Option<String>,
    name:String,deleted_at:String,sha256:String,referenced:bool,
    can_restore:bool,restore_block_reason:Option<String>,
}
#[derive(Debug,Serialize)]
#[serde(rename_all="camelCase")]
pub struct RecyclePage{items:Vec<RecycledRow>,total:i64,next_cursor:Option<String>}

fn validate(q:&RecycleQuery)->Result<(Option<String>,Option<String>,usize),TreeError>{
    if !valid_id(&q.assay_id)||
       q.stage.as_deref().is_some_and(|s|!matches!(s,"raw"|"processed"|"statistics"|"display"))||
       q.group_id.as_deref().is_some_and(|s|!valid_id(s))||
       q.replicate_id.as_deref().is_some_and(|s|!valid_id(s))||
       q.replicate_id.is_some()!=q.group_id.is_some()||
       q.search.as_deref().is_some_and(|s|s.len()>256||s.chars().any(char::is_control))||
       q.limit.is_some_and(|n|n==0||n>100) {
       return Err(fail("INVALID_REQUEST"));
    }
    let (time,id)=match q.cursor.as_deref(){
        None=>(None,None),
        Some(cursor)=>{
            let (time,id)=cursor.split_once('|').ok_or_else(||fail("INVALID_REQUEST"))?;
            if time.len()!=24||!time.ends_with('Z')||!time.bytes().all(|b|b.is_ascii_alphanumeric()||matches!(b,b'-'|b':'|b'.'))||!valid_id(id){
                return Err(fail("INVALID_REQUEST"));
            }
            (Some(time.to_string()),Some(id.to_string()))
        }
    };
    Ok((time,id,q.limit.unwrap_or(50) as usize))
}
pub(super) fn list_at(path:&Path,q:RecycleQuery)->Result<RecyclePage,TreeError>{
    let (time,id,limit)=validate(&q)?;
    with_database_at(path,|c|{
        let valid:bool=c.query_row("SELECT EXISTS(SELECT 1 FROM assays a JOIN themes t ON t.id=a.theme_id
           JOIN projects p ON p.id=t.project_id WHERE a.id=?1 AND a.deleted_at IS NULL
           AND t.deleted_at IS NULL AND p.deleted_at IS NULL)",[&q.assay_id],|r|r.get(0))?;
        if !valid{return Err(fail("INVALID_TARGET"));}
        let clause="l.assay_id=?1 AND l.deleted_at IS NOT NULL
           AND (?2 IS NULL OR l.stage=?2) AND (?3 IS NULL OR l.group_id=?3)
           AND (?4 IS NULL OR l.replicate_id=?4)
           AND (?5 IS NULL OR instr(lower(l.display_name),lower(?5))>0)";
        let total:i64=c.query_row(&format!("SELECT count(*) FROM asset_links l WHERE {clause}"),
            params![q.assay_id,q.stage,q.group_id,q.replicate_id,q.search],|r|r.get(0))?;
        let sql=format!("SELECT l.id,l.asset_id,v.id,l.assay_id,l.stage,l.group_id,l.replicate_id,
            l.display_name,l.deleted_at,b.sha256,
            EXISTS(SELECT 1 FROM asset_versions v2 JOIN panel_asset_versions p ON p.asset_version_id=v2.id
              WHERE v2.asset_id=l.asset_id
              UNION ALL SELECT 1 FROM asset_versions v3 JOIN representative_sources rs ON rs.asset_version_id=v3.id
              WHERE v3.asset_id=l.asset_id)
            FROM asset_links l JOIN data_assets a ON a.id=l.asset_id
            JOIN asset_versions v ON v.asset_id=a.id AND v.version_number=
             (SELECT MAX(x.version_number) FROM asset_versions x WHERE x.asset_id=a.id)
            JOIN blobs b ON b.sha256=v.blob_sha256 WHERE {clause}
            AND (?6 IS NULL OR l.deleted_at<?6 OR (l.deleted_at=?6 AND l.id<?7))
            ORDER BY l.deleted_at DESC,l.id DESC LIMIT ?8");
        let entries={
            let mut stmt=c.prepare(&sql)?;
            let rows=stmt.query_map(params![q.assay_id,q.stage,q.group_id,q.replicate_id,q.search,
                time,id,(limit+1) as i64],|r|{
                Ok(RecycledRow{asset_link_id:r.get(0)?,asset_id:r.get(1)?,asset_version_id:r.get(2)?,
                    assay_id:r.get(3)?,stage:r.get(4)?,group_id:r.get(5)?,replicate_id:r.get(6)?,
                    name:r.get(7)?,deleted_at:r.get(8)?,sha256:r.get(9)?,
                    referenced:r.get(10)?,can_restore:false,restore_block_reason:None})
            })?;
            rows.collect::<rusqlite::Result<Vec<_>>>()?
        };
        let has_more=entries.len()>limit;
        let mut items=entries.into_iter().take(limit).collect::<Vec<_>>();
        for item in &mut items {
            let scope=LinkIdentity{asset_link_id:item.asset_link_id.clone(),asset_id:item.asset_id.clone(),
              asset_version_id:item.asset_version_id.clone(),assay_id:item.assay_id.clone(),
              stage:item.stage.clone(),group_id:item.group_id.clone(),replicate_id:item.replicate_id.clone()};
            item.restore_block_reason=if !active_target(c,&scope)? {Some("INVALID_TARGET".into())}
              else if name_conflict(c,&scope,&item.name,&item.asset_link_id)? {Some("NAME_CONFLICT".into())}
              else {None};
            item.can_restore=item.restore_block_reason.is_none();
        }
        let next_cursor=if has_more {items.last().map(|x|format!("{}|{}",x.deleted_at,x.asset_link_id))}
            else{None};
        Ok(RecyclePage{items,total,next_cursor})
    })
}
#[tauri::command]
pub fn list_recycled_assets<R:tauri::Runtime>(
    app:tauri::AppHandle<R>,request:RecycleQuery
)->Result<RecyclePage,TreeError>{
    list_at(&database_path(&app)?,request)
}
