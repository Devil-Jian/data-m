//! S13: immutable folder scan and durable user-reviewed import manifest.
//! The original scientific directory is never modified; a job plan contains
//! only canonical, regular files. S3 remains the sole CAS import writer.
use crate::project_tree::{database_path, with_database_at, TreeError};
use rusqlite::{params, Connection, OptionalExtension, TransactionBehavior};
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::{collections::{HashMap, HashSet}, fs::{self, File}, io::Read, path::{Component, Path, PathBuf}, time::UNIX_EPOCH};
use tauri::Manager;

const MAX_FILES: usize = 50_000;
const MAX_DEPTH: usize = 20;
const PAGE_SIZE: usize = 500;

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all="camelCase",deny_unknown_fields)]
pub struct ArchiveRequest {
    action:String,
    root_path:Option<String>,
    layout:Option<String>,
    task_id:Option<String>,
    target_project_id:Option<String>,
    target_theme_name:Option<String>,
    fallback_group:Option<String>,
    entry_id:Option<String>,
    decision:Option<String>,
    project_name:Option<String>,
    theme_name:Option<String>,
    assay_name:Option<String>,
    group_name:Option<String>,
    replicate_name:Option<String>,
    stage:Option<String>,
    alias:Option<String>,
    job_id:Option<String>,
    entry_ids:Option<Vec<String>>,
    offset:Option<usize>,
    template_name:Option<String>,
}
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all="camelCase")]
pub struct ArchiveEntry {
    id:String, relative_path:String, source_path:String, size_bytes:i64, sha256:String,
    project_name:String, theme_name:String, assay_name:String, group_name:String,
    replicate_name:String, stage:String, decision:String, alias:String, process_tag:String, issue:String,
    status:String, job_id:Option<String>,
}
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all="camelCase")]
pub struct ArchiveTask {
    id:String, root_name:String, layout:String, target_project_id:Option<String>,
    target_theme_name:String, fallback_group:String, status:String,
    total:usize, imported:usize, skipped:usize, review:usize, failed:usize, ready:usize,
    entries:Vec<ArchiveEntry>, next_offset:Option<usize>,
}
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all="camelCase")]
pub struct ArchiveTemplate {name:String,layout:String,target_theme_name:String,fallback_group:String}
#[derive(Debug, Serialize)]
#[serde(rename_all="camelCase")]
pub struct ArchiveResponse {
    current:Option<ArchiveTask>,tasks:Vec<ArchiveTask>,templates:Vec<ArchiveTemplate>,
}

fn invalid(code:&'static str)->TreeError{TreeError::new(code,code)}
fn required<'a>(value:Option<&'a str>)->Result<&'a str,TreeError>{
    value.filter(|s|!s.trim().is_empty()&&s.len()<=32767&& !s.contains('\0'))
        .ok_or_else(||invalid("INVALID_REQUEST"))
}
fn name(value:&str)->bool {
    !value.trim().is_empty() && value.chars().count()<=128
       && !value.chars().any(|c|c.is_control()||c=='/'||c=='\\')
}
fn layout_ok(value:&str)->bool{
    matches!(value,"project_theme_assay_group"|"theme_assay_group"|"assay_group")
}
fn stage_ok(value:&str)->bool{
    matches!(value,"raw"|"processed"|"statistics"|"display")
}
fn safe_regular(path:&Path)->Result<fs::Metadata,TreeError>{
    let m=fs::symlink_metadata(path).map_err(|_|invalid("SOURCE_UNAVAILABLE"))?;
    if m.file_type().is_symlink() || !m.is_file() {return Err(invalid("UNSAFE_PATH"))}
    #[cfg(windows)]
    {
        use std::os::windows::fs::MetadataExt;
        if m.file_attributes() & 0x400 != 0 {return Err(invalid("UNSAFE_PATH"))}
    }
    Ok(m)
}
fn safe_directory(path:&Path)->Result<(),TreeError>{
    let m=fs::symlink_metadata(path).map_err(|_|invalid("SOURCE_UNAVAILABLE"))?;
    if m.file_type().is_symlink() || !m.is_dir(){return Err(invalid("UNSAFE_PATH"))}
    #[cfg(windows)]
    {
        use std::os::windows::fs::MetadataExt;
        if m.file_attributes() & 0x400 != 0 {return Err(invalid("UNSAFE_PATH"))}
    }
    Ok(())
}
fn modified(m:&fs::Metadata)->Result<u128,TreeError>{
    Ok(m.modified().map_err(|_|invalid("SOURCE_UNAVAILABLE"))?
        .duration_since(UNIX_EPOCH).map_err(|_|invalid("SOURCE_UNAVAILABLE"))?.as_nanos())
}
fn file_hash(path:&Path)->Result<String,TreeError>{
    let mut file=File::open(path).map_err(|_|invalid("SOURCE_UNAVAILABLE"))?;
    let mut hash=Sha256::new();let mut buffer=[0u8;1024*1024];
    loop {
        let n=file.read(&mut buffer).map_err(|_|invalid("SOURCE_UNAVAILABLE"))?;
        if n==0{break}hash.update(&buffer[..n]);
    }
    Ok(format!("{:x}",hash.finalize()))
}
fn parse_name(file:&str)->(String,String,String,String){
    let stem=Path::new(file).file_stem().and_then(|x|x.to_str()).unwrap_or(file);
    let lower=stem.to_ascii_lowercase();
    let ext=Path::new(file).extension().and_then(|x|x.to_str())
        .unwrap_or("").to_ascii_lowercase();
    for (token,stage) in [("_raw","raw"),("-raw","raw"),
        ("_show","display"),("-show","display"),("_display","display"),
        ("_statistics","statistics"),("_stats","statistics"),
        ("_process","processed"),("-process","processed"),
        ("_processed","processed"),("-processed","processed"),
        ("_quant","processed"),("-quant","processed")]{
        if let Some(index)=lower.find(token){
            // Always require a boundary after the stage marker; process_* has variants.
            let remainder=&lower[index+token.len()..];
            if !(remainder.is_empty() || remainder.starts_with('_') || remainder.starts_with('-')){continue}
            if !name(&stem[..index]){continue}
            if token.contains("quant") && matches!(ext.as_str(),"csv"|"xlsx"|"xls"|"tsv"){
                return (stem[..index].into(),"review".into(),"quant result: choose processed or statistics".into(),String::new());
            }
            let tag=if token.contains("quant"){"quant".to_string()}
                else{remainder.trim_start_matches(['_','-']).to_string()};
            return (stem[..index].into(),stage.into(),String::new(),tag);
        }
    }
    (String::new(),"review".into(),"cannot determine stage or biological replicate".into(),String::new())
}
fn inferred(
    root:&Path,relative:&Path,layout:&str,theme_fallback:&str,group_fallback:&str,
)->(String,String,String,String,String,String,String){
    let parts:Vec<String>=relative.components().filter_map(|c|match c{
        Component::Normal(s)=>s.to_str().map(|x|x.to_string()),_=>None
    }).collect();
    let file=parts.last().cloned().unwrap_or_default();
    let folders=&parts[..parts.len().saturating_sub(1)];
    let root_name=root.file_name().and_then(|x|x.to_str()).unwrap_or("Imported");
    let (project,theme,assay,group,required_dirs): (String,String,String,String,usize)=match layout{
        "project_theme_assay_group"=>(
            root_name.into(),folders.first().cloned().unwrap_or_default(),
            folders.get(1).cloned().unwrap_or_default(),folders.get(2).cloned().unwrap_or_default(),2),
        "theme_assay_group"=>(
            String::new(),root_name.into(),
            folders.first().cloned().unwrap_or_default(),folders.get(1).cloned().unwrap_or_default(),1),
        "assay_group"=>(String::new(),theme_fallback.into(),root_name.into(),
            folders.first().cloned().unwrap_or_default(),0),
        _=>(String::new(),String::new(),String::new(),String::new(),99)
    };
    let (sample,stage,issue,_) = parse_name(&file);
    let group=if group.is_empty(){group_fallback.into()}else{group};
    let mut reason=issue;
    if folders.len()<required_dirs || !name(&theme) || !name(&assay){
        reason="missing project/theme/assay directory mapping".into();
    }
    if !name(&project) && layout=="project_theme_assay_group"{
        reason="invalid project directory name".into();
    }
    if (stage=="raw"||stage=="processed") && (!name(&group)||!name(&sample)){
        reason="raw/processed requires a confirmed group and replicate".into();
    }
    (project,theme,assay,group,sample,stage,reason)
}
fn scan_walk(root:&Path,dir:&Path,depth:usize,paths:&mut Vec<PathBuf>)->Result<(),TreeError>{
    if depth>MAX_DEPTH{return Err(invalid("DIRECTORY_TOO_DEEP"))}
    safe_directory(dir)?;
    let mut children=fs::read_dir(dir).map_err(|_|invalid("SOURCE_UNAVAILABLE"))?
        .map(|v|v.map(|x|x.path()).map_err(|_|invalid("SOURCE_UNAVAILABLE")))
        .collect::<Result<Vec<_>,_>>()?;
    children.sort();
    for child in children {
        let m=fs::symlink_metadata(&child).map_err(|_|invalid("SOURCE_UNAVAILABLE"))?;
        if m.file_type().is_symlink(){return Err(invalid("UNSAFE_PATH"))}
        #[cfg(windows)]
        {
            use std::os::windows::fs::MetadataExt;
            if m.file_attributes() & 0x400 != 0 {return Err(invalid("UNSAFE_PATH"))}
        }
        if m.is_dir(){scan_walk(root,&child,depth+1,paths)?;}
        else if m.is_file(){
            let actual=child.canonicalize().map_err(|_|invalid("SOURCE_UNAVAILABLE"))?;
            if !actual.starts_with(root){return Err(invalid("UNSAFE_PATH"))}
            paths.push(child);
            if paths.len()>MAX_FILES{return Err(invalid("TOO_MANY_FILES"))}
        }else{return Err(invalid("UNSAFE_PATH"))}
    }
    Ok(())
}
fn db(path:&Path)->Result<Connection,TreeError>{
    let parent=path.parent().ok_or_else(||invalid("UNSAFE_PATH"))?;
    if !parent.exists(){fs::create_dir_all(parent).map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;}
    safe_directory(parent)?;
    if path.exists(){safe_regular(path)?;}
    let con=Connection::open(path).map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    con.busy_timeout(std::time::Duration::from_secs(15)).map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    con.execute_batch(r#"
      PRAGMA foreign_keys=ON;
      CREATE TABLE IF NOT EXISTS s13_tasks(
        id TEXT PRIMARY KEY,root_path TEXT NOT NULL,root_name TEXT NOT NULL,
        layout TEXT NOT NULL,target_project_id TEXT,target_theme_name TEXT NOT NULL,
        fallback_group TEXT NOT NULL,status TEXT NOT NULL,
        created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
      ) STRICT;
      CREATE TABLE IF NOT EXISTS s13_entries(
        id TEXT PRIMARY KEY,task_id TEXT NOT NULL REFERENCES s13_tasks(id),
        ordinal INTEGER NOT NULL,relative_path TEXT NOT NULL,source_path TEXT NOT NULL,
        size_bytes INTEGER NOT NULL,mtime TEXT NOT NULL,sha256 TEXT NOT NULL,
        project_name TEXT NOT NULL,theme_name TEXT NOT NULL,assay_name TEXT NOT NULL,
        group_name TEXT NOT NULL,replicate_name TEXT NOT NULL,stage TEXT NOT NULL,
        decision TEXT NOT NULL,alias TEXT NOT NULL,process_tag TEXT NOT NULL DEFAULT '',
        issue TEXT NOT NULL,status TEXT NOT NULL DEFAULT 'queued',job_id TEXT,
        UNIQUE(task_id,ordinal),UNIQUE(task_id,relative_path)
      ) STRICT;
      CREATE INDEX IF NOT EXISTS s13_entries_task ON s13_entries(task_id,ordinal);
      CREATE TABLE IF NOT EXISTS s13_templates(
        name TEXT PRIMARY KEY,layout TEXT NOT NULL,target_theme_name TEXT NOT NULL,
        fallback_group TEXT NOT NULL
      ) STRICT;
    "#).map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    Ok(con)
}
fn task(con:&Connection,id:&str,offset:usize)->Result<ArchiveTask,TreeError>{
    let head=con.query_row(
       "SELECT root_name,layout,target_project_id,target_theme_name,fallback_group,status
        FROM s13_tasks WHERE id=?1",[id],|r|Ok((
        r.get::<_,String>(0)?,r.get::<_,String>(1)?,r.get::<_,Option<String>>(2)?,
        r.get::<_,String>(3)?,r.get::<_,String>(4)?,r.get::<_,String>(5)?
    ))).optional().map_err(|_|invalid("QUEUE_UNAVAILABLE"))?
    .ok_or_else(||invalid("TASK_NOT_FOUND"))?;
    let (total,imported,skipped,review,failed,ready):(i64,i64,i64,i64,i64,i64)=con.query_row(
       "SELECT count(*),
         COALESCE(sum(status='imported'),0),COALESCE(sum(decision='skip'),0),
         COALESCE(sum(decision='review'),0),COALESCE(sum(status='failed'),0),
         COALESCE(sum(decision='import' AND status!='imported'),0)
        FROM s13_entries WHERE task_id=?1",[id],|r|Ok((r.get(0)?,r.get(1)?,
        r.get(2)?,r.get(3)?,r.get(4)?,r.get(5)?))).map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    let mut stmt=con.prepare(
        "SELECT id,relative_path,source_path,size_bytes,sha256,project_name,theme_name,
        assay_name,group_name,replicate_name,stage,decision,alias,process_tag,issue,status,job_id
        FROM s13_entries WHERE task_id=?1 ORDER BY ordinal LIMIT ?2 OFFSET ?3"
    ).map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    let entries=stmt.query_map(params![id,PAGE_SIZE as i64,offset as i64],|r|Ok(ArchiveEntry{
        id:r.get(0)?,relative_path:r.get(1)?,source_path:r.get(2)?,
        size_bytes:r.get(3)?,sha256:r.get(4)?,project_name:r.get(5)?,
        theme_name:r.get(6)?,assay_name:r.get(7)?,group_name:r.get(8)?,
        replicate_name:r.get(9)?,stage:r.get(10)?,decision:r.get(11)?,
        alias:r.get(12)?,process_tag:r.get(13)?,issue:r.get(14)?,status:r.get(15)?,job_id:r.get(16)?,
    })).map_err(|_|invalid("QUEUE_UNAVAILABLE"))?
       .collect::<Result<Vec<_>,_>>().map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    Ok(ArchiveTask{id:id.into(),root_name:head.0,layout:head.1,
        target_project_id:head.2,target_theme_name:head.3,fallback_group:head.4,status:head.5,
        total:total as usize,imported:imported as usize,skipped:skipped as usize,
        review:review as usize,failed:failed as usize,ready:ready as usize,
        entries,next_offset:if offset+PAGE_SIZE<total as usize{Some(offset+PAGE_SIZE)}else{None}})
}
fn templates(con:&Connection)->Result<Vec<ArchiveTemplate>,TreeError>{
    let mut stmt=con.prepare("SELECT name,layout,target_theme_name,fallback_group FROM s13_templates ORDER BY name")
        .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    stmt.query_map([],|r|Ok(ArchiveTemplate{
        name:r.get(0)?,layout:r.get(1)?,target_theme_name:r.get(2)?,fallback_group:r.get(3)?
    })).map_err(|_|invalid("QUEUE_UNAVAILABLE"))?
        .collect::<Result<Vec<_>,_>>().map_err(|_|invalid("QUEUE_UNAVAILABLE"))
}
fn make_response(con:&Connection,id:Option<&str>,offset:usize)->Result<ArchiveResponse,TreeError>{
    let current=id.map(|id|task(con,id,offset)).transpose()?;
    let mut stmt=con.prepare("SELECT id FROM s13_tasks ORDER BY created_at DESC LIMIT 20")
        .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    let ids=stmt.query_map([],|r|r.get::<_,String>(0)).map_err(|_|invalid("QUEUE_UNAVAILABLE"))?
       .collect::<Result<Vec<_>,_>>().map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    let mut tasks=Vec::new();
    for id in ids {let mut t=task(con,&id,0)?;t.entries.clear();t.next_offset=None;tasks.push(t);}
    Ok(ArchiveResponse{current,tasks,templates:templates(con)?})
}

fn scan(con:&mut Connection,request:&ArchiveRequest,repo_path:&Path)->Result<String,TreeError>{
    let root=Path::new(required(request.root_path.as_deref())?);
    safe_directory(root)?;
    let root=root.canonicalize().map_err(|_|invalid("UNSAFE_PATH"))?;
    let repo_root=repo_path.parent().ok_or_else(||invalid("UNSAFE_PATH"))?
        .canonicalize().map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    // Source must not be a repository ancestor OR descendant.
    if root.starts_with(&repo_root) || repo_root.starts_with(&root){
        return Err(invalid("SOURCE_IN_REPOSITORY"));
    }
    let layout=required(request.layout.as_deref())?;
    if !layout_ok(layout){return Err(invalid("INVALID_LAYOUT"))}
    if let Some(project)=request.target_project_id.as_deref(){
        if !name(project){return Err(invalid("INVALID_REQUEST"))}
    }else if layout!="project_theme_assay_group"{return Err(invalid("TARGET_PROJECT_REQUIRED"))}
    let fallback=request.fallback_group.as_deref().unwrap_or("");
    let theme=request.target_theme_name.as_deref().unwrap_or("");
    if !fallback.is_empty()&&!name(fallback){return Err(invalid("INVALID_GROUP"))}
    if !theme.is_empty()&&!name(theme){return Err(invalid("INVALID_THEME"))}
    if layout=="assay_group" && theme.is_empty(){return Err(invalid("INVALID_THEME"))}
    let mut files=Vec::new();
    scan_walk(&root,&root,0,&mut files)?;
    if files.is_empty(){return Err(invalid("EMPTY_SOURCE"))}
    let root_name=root.file_name().and_then(|s|s.to_str())
        .ok_or_else(||invalid("UNSAFE_PATH"))?.to_string();
    let tx=con.transaction_with_behavior(TransactionBehavior::Immediate)
        .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    let task_id:String=tx.query_row("SELECT lower(hex(randomblob(16)))",[],|r|r.get(0))
        .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    tx.execute("INSERT INTO s13_tasks(id,root_path,root_name,layout,target_project_id,target_theme_name,fallback_group,status)
       VALUES(?1,?2,?3,?4,?5,?6,?7,'scanned')",
       params![task_id,root.to_string_lossy().as_ref(),root_name,layout,request.target_project_id,theme,fallback]
    ).map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    for (ordinal,path) in files.iter().enumerate(){
        let source=safe_regular(path)?;
        let relative=path.strip_prefix(&root).map_err(|_|invalid("UNSAFE_PATH"))?;
        let text=relative.to_str().ok_or_else(||invalid("UNSAFE_PATH"))?
            .replace('\\',"/");
        let (project,theme_name,assay,group,replicate,stage,issue)=
            inferred(&root,relative,layout,theme,fallback);
        let decision=if issue.is_empty(){"import"}else{"review"};
        let file_name=path.file_name().and_then(|x|x.to_str()).ok_or_else(||invalid("UNSAFE_PATH"))?;
        let (_,_,_,process_tag)=parse_name(file_name);
        let size=i64::try_from(source.len()).map_err(|_|invalid("SOURCE_TOO_LARGE"))?;
        let mtime=modified(&source)?.to_string();
        let sha=file_hash(path)?;
        let after=safe_regular(path)?;
        if after.len()!=source.len()||modified(&after)?.to_string()!=mtime{
            return Err(invalid("SOURCE_CHANGED"));
        }
        let id:String=tx.query_row("SELECT lower(hex(randomblob(16)))",[],|r|r.get(0))
            .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
        tx.execute("INSERT INTO s13_entries(id,task_id,ordinal,relative_path,source_path,size_bytes,mtime,sha256,
            project_name,theme_name,assay_name,group_name,replicate_name,stage,decision,alias,process_tag,issue)
            VALUES(?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?12,?13,?14,?15,?16,?17,?18)",
            params![id,task_id,ordinal as i64,text,path.to_string_lossy().as_ref(),size,mtime,sha,
                project,theme_name,assay,group,replicate,stage,decision,file_name,process_tag,issue]
        ).map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    }
    tx.commit().map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    Ok(task_id)
}

fn edit(con:&Connection,req:&ArchiveRequest)->Result<(),TreeError>{
    let id=required(req.task_id.as_deref())?;
    let entry=required(req.entry_id.as_deref())?;
    let state:String=con.query_row("SELECT status FROM s13_tasks WHERE id=?1",[id],|r|r.get(0))
        .optional().map_err(|_|invalid("QUEUE_UNAVAILABLE"))?
        .ok_or_else(||invalid("TASK_NOT_FOUND"))?;
    if state=="running"||state=="completed"{return Err(invalid("TASK_LOCKED"))}
    let current:Option<(String,String,String,String,String,String,String,String,String)>=con.query_row(
        "SELECT project_name,theme_name,assay_name,group_name,replicate_name,stage,decision,alias,issue
         FROM s13_entries WHERE task_id=?1 AND id=?2",
        params![id,entry],|r|Ok((r.get(0)?,r.get(1)?,r.get(2)?,r.get(3)?,r.get(4)?,
            r.get(5)?,r.get(6)?,r.get(7)?,r.get(8)?))
    ).optional().map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    let old=current.ok_or_else(||invalid("ENTRY_NOT_FOUND"))?;
    let (project,theme,assay,group,replicate,stage,decision,alias)=(
        req.project_name.as_deref().unwrap_or(&old.0),
        req.theme_name.as_deref().unwrap_or(&old.1),
        req.assay_name.as_deref().unwrap_or(&old.2),
        req.group_name.as_deref().unwrap_or(&old.3),
        req.replicate_name.as_deref().unwrap_or(&old.4),
        req.stage.as_deref().unwrap_or(&old.5),
        req.decision.as_deref().unwrap_or(&old.6),
        req.alias.as_deref().unwrap_or(&old.7));
    if !matches!(decision,"import"|"skip"|"review")
        || (decision!="skip" && (!name(alias)||!name(theme)||!name(assay)||!stage_ok(stage)
            || (stage=="raw"||stage=="processed")&&(!name(group)||!name(replicate))
            || (!project.is_empty()&&!name(project))))
        {return Err(invalid("INVALID_MAPPING"))}
    con.execute("UPDATE s13_entries SET project_name=?1,theme_name=?2,assay_name=?3,
      group_name=?4,replicate_name=?5,stage=?6,decision=?7,alias=?8,issue=?9
      WHERE id=?10 AND task_id=?11",
      params![project,theme,assay,group,replicate,stage,decision,
         alias,if decision=="review"{"manual review required"}else{""},entry,id]
    ).map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    con.execute("UPDATE s13_tasks SET status='scanned' WHERE id=?1",[id])
        .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    Ok(())
}


fn prepare(con:&mut Connection,repo:&Path,id:&str)->Result<(),TreeError>{
    let state:String=con.query_row("SELECT status FROM s13_tasks WHERE id=?1",[id],|r|r.get(0))
        .optional().map_err(|_|invalid("QUEUE_UNAVAILABLE"))?
        .ok_or_else(||invalid("TASK_NOT_FOUND"))?;
    if !matches!(state.as_str(),"scanned"|"review"|"prepared"|"paused"|"failed"){
        return Err(invalid("TASK_LOCKED"));
    }
    let mut stmt=con.prepare("SELECT id,source_path,size_bytes,mtime,sha256,
       project_name,theme_name,assay_name,group_name,replicate_name,stage,alias,decision
       FROM s13_entries WHERE task_id=?1 ORDER BY ordinal")
       .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    type Row=(String,String,i64,String,String,String,String,String,String,String,String,String,String);
    let rows:Vec<Row>=stmt.query_map([id],|r|Ok((
        r.get(0)?,r.get(1)?,r.get(2)?,r.get(3)?,r.get(4)?,
        r.get(5)?,r.get(6)?,r.get(7)?,r.get(8)?,r.get(9)?,
        r.get(10)?,r.get(11)?,r.get(12)?
    ))).map_err(|_|invalid("QUEUE_UNAVAILABLE"))?.collect::<Result<_,_>>()
        .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    drop(stmt);
    let mut changed=Vec::<(String,String,String)>::new();
    let mut seen=HashMap::<String,(String,String)>::new();
    let mut any_review=false;
    // Never mutate S1/S2/S3 while preparing: all other views remain consistent.
    with_database_at(repo,|db|{
        for (entry,path,size,mtime,sha,project,theme,assay,group,sample,stage,alias,decision) in &rows{
            if decision=="skip"{
                changed.push((entry.clone(),"skip".into(),"skipped by user".into()));
                continue;
            }
            if decision=="review"||!stage_ok(stage)||!name(theme)||!name(assay)||!name(alias)
               ||(stage=="raw"||stage=="processed")&&(!name(group)||!name(sample)){
                changed.push((entry.clone(),"review".into(),"unresolved directory or stage mapping".into()));
                any_review=true;continue;
            }
            let source=Path::new(path);
            let meta=safe_regular(source)?;
            if meta.len()!=*size as u64||modified(&meta)?.to_string()!=*mtime
                ||file_hash(source)?!=*sha {
                changed.push((entry.clone(),"review".into(),"SOURCE_CHANGED".into()));
                any_review=true;continue;
            }
            let target_project=con.query_row("SELECT target_project_id FROM s13_tasks WHERE id=?1",
                [id],|r|r.get::<_,Option<String>>(0))
                .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
            let key=format!("{}|{}|{}|{}|{}|{}|{}",
                target_project.as_deref().unwrap_or(project),theme,assay,group,sample,stage,
                alias.to_lowercase());
            if let Some((prior_hash,_))=seen.get(&key){
                if prior_hash==sha{
                    changed.push((entry.clone(),"skip".into(),"same staged target and SHA".into()));
                }else{
                    changed.push((entry.clone(),"review".into(),"DUPLICATE_TARGET_IN_PLAN".into()));
                    any_review=true;
                }
                continue;
            }
            seen.insert(key,(sha.clone(),entry.clone()));
            // Missing entity levels are created only by S1/S2 after explicit confirmation.
            // Existing names must match at every parent level and in the correct assay scope.
            let mut st=db.prepare("SELECT v.blob_sha256
                FROM asset_links l
                JOIN assays a ON a.id=l.assay_id AND a.deleted_at IS NULL
                JOIN themes t ON t.id=a.theme_id AND t.deleted_at IS NULL
                JOIN projects p ON p.id=t.project_id AND p.deleted_at IS NULL
                JOIN data_assets da ON da.id=l.asset_id
                JOIN asset_versions v ON v.asset_id=da.id
                LEFT JOIN experiment_groups g ON g.id=l.group_id
                LEFT JOIN replicates r ON r.id=l.replicate_id
                WHERE (?1 IS NULL OR p.id=?1)
                  AND (?1 IS NOT NULL OR p.name=?2 COLLATE NOCASE)
                  AND t.name=?3 COLLATE NOCASE AND a.name=?4 COLLATE NOCASE
                  AND (l.stage IN ('statistics','display') OR
                       (g.name=?5 COLLATE NOCASE AND r.label=?6 COLLATE NOCASE))
                  AND l.stage=?7 AND l.display_name=?8 COLLATE NOCASE
                  AND l.deleted_at IS NULL
                ORDER BY v.version_number DESC LIMIT 2")
                .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
            let matches=st.query_map(params![target_project,project,theme,assay,group,sample,stage,alias],
                |r|r.get::<_,String>(0)).map_err(|_|invalid("QUEUE_UNAVAILABLE"))?
                .collect::<Result<Vec<_>,_>>().map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
            if matches.is_empty(){changed.push((entry.clone(),"import".into(),String::new()));}
            else if matches.iter().any(|known|known==sha){
                changed.push((entry.clone(),"skip".into(),"already imported same SHA".into()));
            }else{
                changed.push((entry.clone(),"review".into(),"NAME_CONFLICT_DIFFERENT_CONTENT".into()));
                any_review=true;
            }
        }
        Ok(())
    })?;
    let tx=con.transaction_with_behavior(TransactionBehavior::Immediate)
        .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    for (entry,decision,issue) in &changed{
        tx.execute("UPDATE s13_entries SET decision=?1,issue=?2
             WHERE task_id=?3 AND id=?4",params![decision,issue,id,entry])
             .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    }
    tx.execute("UPDATE s13_tasks SET status=?1 WHERE id=?2",
        params![if any_review{"review"}else{"prepared"},id])
        .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    tx.commit().map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    Ok(())
}
fn bind_job(con:&mut Connection,id:&str,req:&ArchiveRequest)->Result<(),TreeError>{
    let job=required(req.job_id.as_deref())?;
    if job.len()>128||req.entry_ids.as_ref().map_or(true,|ids|ids.is_empty()||ids.len()>1000){
        return Err(invalid("INVALID_REQUEST"));
    }
    let task_status:String=con.query_row("SELECT status FROM s13_tasks WHERE id=?1",[id],|r|r.get(0))
        .optional().map_err(|_|invalid("QUEUE_UNAVAILABLE"))?
        .ok_or_else(||invalid("TASK_NOT_FOUND"))?;
    if !matches!(task_status.as_str(),"prepared"|"running"|"failed"|"paused"){
        return Err(invalid("TASK_NOT_READY"));
    }
    let ids=req.entry_ids.as_ref().ok_or_else(||invalid("INVALID_REQUEST"))?;
    let distinct:HashSet<&String>=ids.iter().collect();
    if distinct.len()!=ids.len(){return Err(invalid("INVALID_REQUEST"))}
    let tx=con.transaction_with_behavior(TransactionBehavior::Immediate)
        .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    for entry in ids{
        let changed=tx.execute(
            "UPDATE s13_entries SET job_id=?1,status='running'
             WHERE id=?2 AND task_id=?3 AND decision='import'
               AND status NOT IN ('imported','skipped') AND (job_id IS NULL OR job_id=?1)",
            params![job,entry,id]).map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
        if changed!=1{return Err(invalid("ENTRY_CONFLICT"))}
    }
    tx.execute("UPDATE s13_tasks SET status='running' WHERE id=?1",[id])
        .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    tx.commit().map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    Ok(())
}
fn reconcile(con:&mut Connection,repo:&Path,id:&str)->Result<(),TreeError>{
    let mut st=con.prepare("SELECT id,job_id,source_path,sha256,alias
        FROM s13_entries WHERE task_id=?1 AND job_id IS NOT NULL AND decision='import'")
        .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    let entries=st.query_map([id],|r|Ok((r.get::<_,String>(0)?,
        r.get::<_,String>(1)?,r.get::<_,String>(2)?,
        r.get::<_,String>(3)?,r.get::<_,String>(4)?)))
        .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?.collect::<Result<Vec<_>,_>>()
        .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    drop(st);
    let mut findings=Vec::<(String,String)>::new();
    with_database_at(repo,|db|{
        for (entry,job,path,hash,alias) in &entries{
            // Never trust a renderer claim of completion: verify both S3's recorded
            // SHA and its committed immutable version from the actual DB.
            let result=db.query_row("SELECT f.status,f.sha256,v.blob_sha256
                FROM import_files f
                LEFT JOIN asset_versions v ON v.id=f.version_id
                WHERE f.job_id=?1 AND f.source_path=?2 AND f.name=?3 LIMIT 1",
                params![job,path,alias],|r|Ok((
                    r.get::<_,String>(0)?,r.get::<_,Option<String>>(1)?,
                    r.get::<_,Option<String>>(2)?))
                )).optional().map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
            let state=match result{
                Some((status,Some(digest),Some(version))) if status=="committed"
                    && digest==*hash&&version==*hash =>"imported",
                Some((status,_,_)) if matches!(status.as_str(),"failed"|"cancelled"|"interrupted")=>"failed",
                Some(_)=>"running",
                None=>"failed"
            };
            findings.push((entry.clone(),state.into()));
        }
        Ok(())
    })?;
    let tx=con.transaction_with_behavior(TransactionBehavior::Immediate)
        .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    for (entry,state) in findings{
        tx.execute("UPDATE s13_entries SET status=?1 WHERE task_id=?2 AND id=?3
            AND status!='imported'",params![state,id,entry])
            .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    }
    let (left,failed):(i64,i64)=tx.query_row("SELECT
        COALESCE(sum(decision='import' AND status!='imported'),0),
        COALESCE(sum(decision='import' AND status='failed'),0)
        FROM s13_entries WHERE task_id=?1",[id],|r|Ok((r.get(0)?,r.get(1)?)))
        .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    let next=if left==0{"completed"}else if failed>0{"failed"}else{"running"};
    tx.execute("UPDATE s13_tasks SET status=?1 WHERE id=?2",
        params![next,id]).map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    tx.commit().map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
    Ok(())
}

/// Scans and plans are local-only, even before project creation.
fn execute(repo:&Path,req:ArchiveRequest)->Result<ArchiveResponse,TreeError>{
    let db_path=repo.with_file_name("s13-archive-queue.sqlite");
    let mut con=db(&db_path)?;
    let offset=req.offset.unwrap_or(0);
    if offset>MAX_FILES{return Err(invalid("INVALID_REQUEST"))}
    let id=match req.action.as_str(){
        "scan"=>Some(scan(&mut con,&req,repo)?),
        "list"=>None,
        "get"=>Some(required(req.task_id.as_deref())?.to_string()),
        "edit"=>{
            edit(&con,&req)?;
            Some(required(req.task_id.as_deref())?.to_string())
        },
        "prepare"=>{
            let id=required(req.task_id.as_deref())?;
            prepare(&mut con,repo,id)?;
            Some(id.to_string())
        },
        "bind"=>{
            let id=required(req.task_id.as_deref())?;
            bind_job(&mut con,id,&req)?;
            Some(id.to_string())
        },
        "reconcile"=>{
            let id=required(req.task_id.as_deref())?;
            reconcile(&mut con,repo,id)?;
            Some(id.to_string())
        },
        "pause"=>{
            let id=required(req.task_id.as_deref())?;
            con.execute("UPDATE s13_tasks SET status='paused' WHERE id=?1 AND status NOT IN ('completed','cancelled')",[id])
                .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
            Some(id.to_string())
        },
        "resume"=>{
            let id=required(req.task_id.as_deref())?;
            let status:String=con.query_row("SELECT status FROM s13_tasks WHERE id=?1",[id],|r|r.get(0))
                .optional().map_err(|_|invalid("QUEUE_UNAVAILABLE"))?
                .ok_or_else(||invalid("TASK_NOT_FOUND"))?;
            if !matches!(status.as_str(),"prepared"|"paused"|"failed"|"running"){
                return Err(invalid("TASK_NOT_READY"));
            }
            con.execute("UPDATE s13_tasks SET status='running' WHERE id=?1",[id])
                .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
            Some(id.to_string())
        },
        "templateSave"=>{
            let name=required(req.template_name.as_deref())?;
            let layout=required(req.layout.as_deref())?;
            if !self::name(name)||!layout_ok(layout){return Err(invalid("INVALID_REQUEST"))}
            let group=req.fallback_group.as_deref().unwrap_or("");
            let theme=req.target_theme_name.as_deref().unwrap_or("");
            if !group.is_empty()&&!self::name(group){return Err(invalid("INVALID_REQUEST"))}
            if !theme.is_empty()&&!self::name(theme){return Err(invalid("INVALID_REQUEST"))}
            con.execute("INSERT INTO s13_templates(name,layout,target_theme_name,fallback_group)
                VALUES(?1,?2,?3,?4) ON CONFLICT(name) DO UPDATE SET layout=excluded.layout,
                target_theme_name=excluded.target_theme_name,fallback_group=excluded.fallback_group",
                params![name,layout,theme,group]).map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
            None
        },
        "templateDelete"=>{
            let name=required(req.template_name.as_deref())?;
            con.execute("DELETE FROM s13_templates WHERE name=?1",[name])
                .map_err(|_|invalid("QUEUE_UNAVAILABLE"))?;
            None
        },
        _=>return Err(invalid("INVALID_REQUEST"))
    };
    make_response(&con,id.as_deref().or(req.task_id.as_deref()),offset)
}
#[tauri::command]
pub async fn archive_import<R:tauri::Runtime>(
    app:tauri::AppHandle<R>,request:ArchiveRequest
)->Result<ArchiveResponse,TreeError>{
    let repo=database_path(&app)?;
    tauri::async_runtime::spawn_blocking(move||execute(&repo,request))
        .await.map_err(|_|invalid("QUEUE_UNAVAILABLE"))?
}

/// A native folder chooser is useful when the user does not wish to drag/drop.
/// No user-provided command fragments are ever executed.
#[tauri::command]
pub async fn archive_pick_folder()->Result<Option<String>,TreeError>{
    #[cfg(windows)]
    {
        tauri::async_runtime::spawn_blocking(||{
            let script=r#"Add-Type -AssemblyName System.Windows.Forms; $d = New-Object System.Windows.Forms.FolderBrowserDialog; $d.Description = 'Select a research archive folder'; if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($d.SelectedPath) }"#;
            let output=std::process::Command::new("powershell.exe")
                .args(["-NoLogo","-NoProfile","-STA","-Command",script])
                .output().map_err(|_|invalid("PICKER_UNAVAILABLE"))?;
            if !output.status.success(){return Err(invalid("PICKER_UNAVAILABLE"))}
            let result=String::from_utf8(output.stdout).map_err(|_|invalid("PICKER_UNAVAILABLE"))?;
            let value=result.trim().to_string();
            if value.is_empty(){Ok(None)}else{safe_directory(Path::new(&value))?;Ok(Some(value))}
        }).await.map_err(|_|invalid("PICKER_UNAVAILABLE"))?
    }
    #[cfg(not(windows))]
    {Err(invalid("PICKER_UNAVAILABLE"))}
}

#[cfg(test)]
#[path = "archive_import_tests.rs"]
mod tests;
