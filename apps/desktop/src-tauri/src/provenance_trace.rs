//! S10 read-only exact AssetVersion references. No inference from filenames.
//! Queries are bounded and explicitly scoped to the active project.
use crate::project_tree::{database_path, required, with_database_at, TreeError};
use rusqlite::{params, Connection};
use serde::{Deserialize, Serialize};
use std::path::Path;

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct TraceRequest {
    project_id: String,
    action: String,
    id: String,
    offset: Option<u32>,
    limit: Option<u32>,
}
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TraceEdge {
    paper_id: Option<String>,
    figure_id: Option<String>,
    panel_id: Option<String>,
    usage: Option<String>,
    analysis_id: Option<String>,
    analysis_role: Option<String>,
    frozen_at: Option<String>,
    asset_id: String,
    asset_version_id: String,
    version_number: i64,
    blob_sha256: String,
    size_bytes: i64,
    asset_deleted: bool,
}
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TraceResult {
    edges: Vec<TraceEdge>,
    total_count: i64,
    offset: u32,
    limit: u32,
    truncated: bool,
}
fn invalid_scope() -> TreeError {
    TreeError::new("NOT_FOUND", "The requested item does not belong to this project.")
}
fn execute(c: &Connection, r: TraceRequest) -> Result<TraceResult, TreeError> {
    required(Some(&r.project_id), 128)?;
    required(Some(&r.id), 128)?;
    let offset = r.offset.unwrap_or(0).min(1_000_000);
    let limit = r.limit.unwrap_or(50);
    if !(1..=200).contains(&limit) || !matches!(r.action.as_str(), "panel" | "asset" | "version") {
        return Err(TreeError::input());
    }
    let project_exists: bool = c.query_row(
        "SELECT EXISTS(SELECT 1 FROM projects WHERE id=?1 AND deleted_at IS NULL)",
        [&r.project_id], |row| row.get(0)
    )?;
    if !project_exists { return Err(invalid_scope()); }
    // Scope is established by a *historical* assay link, so soft-deletion
    // does not erase evidence. Exact version identity is never substituted.
    let scope_sql = match r.action.as_str() {
        "panel" => "SELECT EXISTS(SELECT 1 FROM panels x JOIN figures f ON f.id=x.figure_id JOIN papers p ON p.id=f.paper_id WHERE x.id=?1 AND p.project_id=?2 AND p.deleted_at IS NULL)",
        "asset" => "SELECT EXISTS(SELECT 1 FROM data_assets d JOIN asset_links l ON l.asset_id=d.id JOIN assays s ON s.id=l.assay_id JOIN themes t ON t.id=s.theme_id WHERE d.id=?1 AND t.project_id=?2)",
        _ => "SELECT EXISTS(SELECT 1 FROM asset_versions v JOIN asset_links l ON l.asset_id=v.asset_id JOIN assays s ON s.id=l.assay_id JOIN themes t ON t.id=s.theme_id WHERE v.id=?1 AND t.project_id=?2)",
    };
    let found: bool = c.query_row(scope_sql, params![r.id, r.project_id], |row| row.get(0))?;
    if !found { return Err(invalid_scope()); }

    // UNION ALL keeps independent paper usages and statistical input/result
    // links. Multiple AssetLinks never multiply an edge; there is no filename
    // or stage-based inference of an analysis relationship.
    let relation_sql = "
      SELECT p.id AS paper_id, f.id AS figure_id, x.id AS panel_id, pav.usage AS usage,
             NULL AS analysis_id, NULL AS analysis_role, NULL AS frozen_at,
             v.asset_id AS asset_id, v.id AS asset_version_id,
             v.version_number AS version_number, v.blob_sha256 AS blob_sha256,
             b.size_bytes AS size_bytes, d.deleted_at IS NOT NULL AS asset_deleted
      FROM panel_asset_versions pav
      JOIN panels x ON x.id=pav.panel_id
      JOIN figures f ON f.id=x.figure_id
      JOIN papers p ON p.id=f.paper_id
      JOIN asset_versions v ON v.id=pav.asset_version_id
      JOIN data_assets d ON d.id=v.asset_id
      JOIN blobs b ON b.sha256=v.blob_sha256
      WHERE p.project_id=?1 AND p.deleted_at IS NULL
      UNION ALL
      SELECT NULL, NULL, NULL, NULL, a.id, av.role, a.frozen_at,
             v.asset_id, v.id, v.version_number, v.blob_sha256, b.size_bytes,
             d.deleted_at IS NOT NULL
      FROM analysis_asset_versions av
      JOIN statistical_analyses a ON a.id=av.analysis_id
      JOIN assays s ON s.id=a.assay_id
      JOIN themes t ON t.id=s.theme_id
      JOIN asset_versions v ON v.id=av.asset_version_id
      JOIN data_assets d ON d.id=v.asset_id
      JOIN blobs b ON b.sha256=v.blob_sha256
      WHERE t.project_id=?1";
    let filter = match r.action.as_str() {
        "panel" => "panel_id=?2",
        "asset" => "asset_id=?2",
        _ => "asset_version_id=?2",
    };
    let from = format!(" FROM ({relation_sql}) WHERE {filter}");
    let total_count: i64 = c.query_row(
        &format!("SELECT count(*) {from}"), params![r.project_id, r.id],
        |row| row.get(0)
    )?;
    let query = format!(
        "SELECT * {from} ORDER BY asset_version_id, panel_id, usage, analysis_id, analysis_role LIMIT ?3 OFFSET ?4"
    );
    let mut stmt = c.prepare(&query)?;
    let edges = stmt.query_map(
        params![r.project_id, r.id, limit, offset],
        |row| Ok(TraceEdge {
            paper_id: row.get(0)?,
            figure_id: row.get(1)?,
            panel_id: row.get(2)?,
            usage: row.get(3)?,
            analysis_id: row.get(4)?,
            analysis_role: row.get(5)?,
            frozen_at: row.get(6)?,
            asset_id: row.get(7)?,
            asset_version_id: row.get(8)?,
            version_number: row.get(9)?,
            blob_sha256: row.get(10)?,
            size_bytes: row.get(11)?,
            asset_deleted: row.get(12)?,
        })
    )?.collect::<rusqlite::Result<Vec<_>>>()?;
    Ok(TraceResult {
        truncated: i64::from(offset) + (edges.len() as i64) < total_count,
        edges, total_count, offset, limit,
    })
}
pub(crate) fn execute_at(path: &Path, request: TraceRequest) -> Result<TraceResult, TreeError> {
    with_database_at(path, |c| execute(c, request))
}
#[tauri::command]
pub fn provenance_trace<R: tauri::Runtime>(
    app: tauri::AppHandle<R>, request: TraceRequest
) -> Result<TraceResult, TreeError> {
    execute_at(&database_path(&app)?, request)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn s10_exact_version_forward_reverse_and_frozen_snapshot() {
        let c = Connection::open_in_memory().unwrap();
        c.pragma_update(None, "foreign_keys", "ON").unwrap();
        c.execute_batch(crate::project_tree::MIGRATION).unwrap();
        c.execute_batch(include_str!("../migrations/0002_import_jobs.sql")).unwrap();
        c.execute_batch(include_str!("../migrations/0003_statistics.sql")).unwrap();
        let sha = "a".repeat(64);
        c.execute_batch(&format!("
          INSERT INTO projects(id,name) VALUES('p','Project');
          INSERT INTO themes(id,project_id,name) VALUES('t','p','Theme');
          INSERT INTO assays(id,theme_id,name) VALUES('a','t','Assay');
          INSERT INTO experiment_groups(id,assay_id,name) VALUES('g','a','Group');
          INSERT INTO replicates(id,group_id,label) VALUES('r','g','Sample');
          INSERT INTO blobs(sha256,size_bytes) VALUES('{sha}',3);
          INSERT INTO data_assets(id,name) VALUES('d','original.tif');
          INSERT INTO asset_versions(id,asset_id,version_number,blob_sha256,source_name)
            VALUES('v1','d',1,'{sha}','original.tif');
          INSERT INTO asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name)
            VALUES('l','d','a','g','r','raw','original.tif');
          INSERT INTO papers(id,project_id,title) VALUES('paper','p','Paper');
          INSERT INTO figures(id,paper_id,label) VALUES('figure','paper','Figure 1');
          INSERT INTO panels(id,figure_id,label) VALUES('panel','figure','A');
          INSERT INTO panel_asset_versions(panel_id,asset_version_id,usage)
            VALUES('panel','v1','display');
          INSERT INTO statistical_analyses(id,assay_id,name,notes,updated_at)
            VALUES('analysis','a','A','','2026-01-01');
          INSERT INTO analysis_samples(analysis_id,replicate_id,included)
            VALUES('analysis','r',1);
          INSERT INTO analysis_asset_versions(analysis_id,asset_version_id,role)
            VALUES('analysis','v1','input');
          UPDATE statistical_analyses SET frozen_at='2026-01-01' WHERE id='analysis';
          UPDATE asset_links SET deleted_at='2026-02-01' WHERE id='l';
        ")).unwrap();
        let req = |action: &str, id: &str| TraceRequest {
            project_id: "p".into(), action: action.into(), id: id.into(),
            offset: None, limit: Some(50),
        };
        let forward = execute(&c, req("panel", "panel")).unwrap();
        assert_eq!(forward.total_count, 1);
        assert_eq!(forward.edges[0].asset_version_id, "v1");
        let reverse = execute(&c, req("version", "v1")).unwrap();
        assert_eq!(reverse.total_count, 2);
        assert!(reverse.edges.iter().any(|e| e.panel_id.as_deref() == Some("panel")));
        assert!(reverse.edges.iter().any(|e| e.analysis_id.as_deref() == Some("analysis")
            && e.frozen_at.as_deref() == Some("2026-01-01")));
        assert_eq!(execute(&c, req("asset", "d")).unwrap().total_count, 2);
        assert_eq!(execute(&c, req("version", "other")).unwrap_err().code, "NOT_FOUND");
    }
}
