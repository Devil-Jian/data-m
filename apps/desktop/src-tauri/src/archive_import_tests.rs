use super::*;
use serde_json::{json,Value};
use tempfile::TempDir;

fn request(v:Value)->ArchiveRequest{serde_json::from_value(v).unwrap()}
fn fixtures()->(TempDir,PathBuf,PathBuf){
    let dir=tempfile::tempdir().unwrap();
    let repo=dir.path().join("database").join("project-tree.sqlite");
    let root=dir.path().join("科研项目A");
    fs::create_dir_all(root.join("氧化应激").join("ROS").join("Control")).unwrap();
    (dir,repo,root)
}
fn put(root:&Path,filename:&str,bytes:&[u8]){
    let path=root.join("氧化应激").join("ROS").join("Control").join(filename);
    fs::write(path,bytes).unwrap();
}
fn scan_task(repo:&Path,root:&Path)->ArchiveTask{
    execute(repo,request(json!({
        "action":"scan","rootPath":root.to_str().unwrap(),
        "layout":"project_theme_assay_group"
    }))).unwrap().current.unwrap()
}

#[test]
fn s13_names_decode_raw_processing_variants_and_experiment_scoped_display(){
    assert_eq!(parse_name("ROS01_raw.tif").0,"ROS01");
    assert_eq!(parse_name("ROS01_raw.tif").1,"raw");
    assert_eq!(parse_name("ROS01_process_crop.tif").1,"processed");
    assert_eq!(parse_name("ROS01_process_crop.tif").3,"crop");
    assert_eq!(parse_name("ROS01_processed_adjust.png").1,"processed");
    assert_eq!(parse_name("ROS01_show.png").1,"display");
    assert_eq!(parse_name("ROS_statistics.xlsx").1,"statistics");
    assert_eq!(parse_name("ROS01_quant.csv").1,"review");
    assert_eq!(parse_name("image_final.tif").1,"review");
}
#[test]
fn s13_scan_reads_only_original_files_and_persists_pageable_manifest(){
    let (_temp,repo,root)=fixtures();
    put(&root,"ROS01_raw.tif",b"RAW1");
    put(&root,"ROS01_process_crop.tif",b"CROP");
    put(&root,"ROS01_show.png",b"SHOW");
    put(&root,"ROS01_quant.csv",b"X1");
    let task=scan_task(&repo,&root);
    assert_eq!(task.total,4);
    assert_eq!(task.review,1);
    assert_eq!(task.status,"scanned");
    assert!(task.entries.iter().any(|e|e.stage=="processed"&&e.replicate_name=="ROS01"&&e.process_tag=="crop"));
    assert!(task.entries.iter().any(|e|e.stage=="display"&&e.replicate_name=="ROS01"));
    assert_eq!(task.entries.iter().find(|e|e.stage=="raw").unwrap().sha256,format!("{:x}",Sha256::digest(b"RAW1")));
    // Scanning metadata does not create a project, assay, asset link or CAS blob.
    with_database_at(&repo,|db|{
        for table in ["projects","assays","asset_links","blobs"]{
            let count:i64=db.query_row(&format!("SELECT count(*) FROM {table}"),[],|r|r.get(0))?;
            assert_eq!(count,0);
        }
        Ok(())
    }).unwrap();
    let reopened=execute(&repo,request(json!({"action":"get","taskId":task.id}))).unwrap().current.unwrap();
    assert_eq!(reopened.total,4);
    assert_eq!(reopened.review,1);
    assert_eq!(reopened.entries.len(),4);
}
#[test]
fn s13_review_then_preflight_never_creates_research_nodes(){
    let (_temp,repo,root)=fixtures();
    put(&root,"ROS01_raw.tif",b"ORIGINAL");
    put(&root,"ROS01_quant.csv",b"1,2,3");
    let task=scan_task(&repo,&root);
    let quant=task.entries.iter().find(|e|e.stage=="review").unwrap();
    let edited=execute(&repo,request(json!({
        "action":"edit","taskId":task.id,"entryId":quant.id,
        "decision":"import","stage":"statistics"
    }))).unwrap().current.unwrap();
    assert_eq!(edited.review,0);
    let prepared=execute(&repo,request(json!({"action":"prepare","taskId":task.id})))
        .unwrap().current.unwrap();
    assert_eq!(prepared.status,"prepared");
    assert_eq!(prepared.review,0);
    with_database_at(&repo,|db|{
        let count:i64=db.query_row("SELECT count(*) FROM projects",[],|r|r.get(0))?;
        assert_eq!(count,0);
        Ok(())
    }).unwrap();
}
#[test]
fn s13_source_change_promotes_review_and_refuses_unverified_start(){
    let (_temp,repo,root)=fixtures();
    put(&root,"ROS01_raw.tif",b"ORIGINAL");
    let task=scan_task(&repo,&root);
    put(&root,"ROS01_raw.tif",b"DIFFERENT_BYTES");
    let result=execute(&repo,request(json!({"action":"prepare","taskId":task.id})))
        .unwrap().current.unwrap();
    assert_eq!(result.status,"review");
    assert_eq!(result.review,1);
    assert_eq!(result.entries[0].issue,"SOURCE_CHANGED");
    assert!(execute(&repo,request(json!({"action":"resume","taskId":task.id}))).is_err());
}
#[test]
fn s13_explicit_skip_and_resume_do_not_synthesize_assets(){
    let (_temp,repo,root)=fixtures();
    put(&root,"unclassified_image.tif",b"unclassified");
    let task=scan_task(&repo,&root);
    assert_eq!(task.review,1);
    let entry=&task.entries[0];
    let result=execute(&repo,request(json!({
        "action":"edit","taskId":task.id,"entryId":entry.id,"decision":"skip"
    }))).unwrap().current.unwrap();
    assert_eq!(result.skipped,1);
    let prepared=execute(&repo,request(json!({"action":"prepare","taskId":task.id})))
        .unwrap().current.unwrap();
    assert_eq!(prepared.status,"prepared");
    let resumed=execute(&repo,request(json!({"action":"resume","taskId":task.id}))).unwrap().current.unwrap();
    assert_eq!(resumed.status,"running");
    let completed=execute(&repo,request(json!({"action":"reconcile","taskId":task.id})))
        .unwrap().current.unwrap();
    assert_eq!(completed.status,"completed");
    assert_eq!(completed.imported,0);
}
#[test]
fn s13_hard_conflict_requires_explicit_alias_or_skip(){
    let (_temp,repo,root)=fixtures();
    put(&root,"ROS01_raw.tif",b"new bytes");
    let task=scan_task(&repo,&root);
    let old_hash=format!("{:x}",Sha256::digest(b"old bytes"));
    with_database_at(&repo,|db|{
        db.execute_batch("INSERT INTO projects(id,name) VALUES('p','科研项目A');
          INSERT INTO themes(id,project_id,name) VALUES('t','p','氧化应激');
          INSERT INTO assays(id,theme_id,name) VALUES('a','t','ROS');
          INSERT INTO experiment_groups(id,assay_id,name) VALUES('g','a','Control');
          INSERT INTO replicates(id,group_id,label) VALUES('r','g','ROS01');")?;
        db.execute("INSERT INTO blobs(sha256,size_bytes) VALUES(?1,9)",[&old_hash])?;
        db.execute("INSERT INTO data_assets(id,name) VALUES('asset','ROS01_raw.tif')",[])?;
        db.execute("INSERT INTO asset_versions(id,asset_id,version_number,blob_sha256,source_name)
             VALUES('v','asset',1,?1,'ROS01_raw.tif')",[&old_hash])?;
        db.execute("INSERT INTO asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name)
             VALUES('link','asset','a','g','r','raw','ROS01_raw.tif')",[])?;
        Ok(())
    }).unwrap();
    let review=execute(&repo,request(json!({"action":"prepare","taskId":task.id})))
        .unwrap().current.unwrap();
    assert_eq!(review.status,"review");
    assert_eq!(review.entries[0].issue,"NAME_CONFLICT_DIFFERENT_CONTENT");
    let entry=&review.entries[0];
    execute(&repo,request(json!({"action":"edit","taskId":task.id,"entryId":entry.id,
        "decision":"import","alias":"ROS01_raw_imported_v2.tif"}))).unwrap();
    let prepared=execute(&repo,request(json!({"action":"prepare","taskId":task.id})))
        .unwrap().current.unwrap();
    assert_eq!(prepared.status,"prepared");
}
#[test]
fn s13_source_folder_cannot_be_repository_ancestor(){
    let (temp,repo,_root)=fixtures();
    assert!(execute(&repo,request(json!({
        "action":"scan","rootPath":temp.path().to_str().unwrap(),
        "layout":"project_theme_assay_group"
    }))).is_err());
}
#[test]
fn s13_rule_templates_persist_across_queue_reloads(){
    let (_temp,repo,_root)=fixtures();
    execute(&repo,request(json!({"action":"templateSave","templateName":"RAW naming",
      "layout":"project_theme_assay_group","fallbackGroup":"Control"}))).unwrap();
    let reopened=execute(&repo,request(json!({"action":"list"}))).unwrap();
    assert!(reopened.templates.iter().any(|t|t.name=="RAW naming"&&t.fallback_group=="Control"));
    execute(&repo,request(json!({"action":"templateDelete","templateName":"RAW naming"}))).unwrap();
    assert!(execute(&repo,request(json!({"action":"list"}))).unwrap().templates.is_empty());
}
#[cfg(unix)]
#[test]
fn s13_folder_symlinks_cannot_escort_scanner_outside_source(){
    use std::os::unix::fs::symlink;
    let (_temp,repo,root)=fixtures();
    let dir=root.join("氧化应激").join("ROS").join("Control");
    symlink("/tmp",&dir.join("external")).unwrap();
    assert!(execute(&repo,request(json!({"action":"scan",
      "rootPath":root.to_str().unwrap(),"layout":"project_theme_assay_group"
    }))).is_err());
}
