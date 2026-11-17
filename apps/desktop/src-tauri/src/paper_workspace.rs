//! S9 paper/figure/panel repository. Controlled disposable S1 workspace only.
use crate::project_tree::{database_path, required, required_name, with_database_at, TreeError};
use rusqlite::{params, Connection, OptionalExtension, TransactionBehavior};
use serde::{Deserialize, Serialize};
use std::path::Path;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct PaperRequest {
    action: String,
    project_id: String,
    kind: Option<String>,
    id: Option<String>,
    parent_id: Option<String>,
    name: Option<String>,
    version_id: Option<String>,
    version_ids: Option<Vec<String>>,
    replicate_id: Option<String>,
    status: Option<String>,
    notes: Option<String>,
    usage: Option<String>,
    order_ids: Option<Vec<String>>,
    request_id: Option<String>,
    candidate_query: Option<String>,
    candidate_stage: Option<String>,
    candidate_offset: Option<u32>,
    candidate_limit: Option<u32>,
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct PaperRow {
    id: String,
    title: String,
    status: String,
    notes: String,
    figures: Vec<FigureRow>,
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct FigureRow {
    id: String,
    label: String,
    panels: Vec<PanelRow>,
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct PanelRow {
    id: String,
    label: String,
    assets: Vec<PanelAsset>,
    representatives: Vec<Representative>,
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct PanelAsset {
    asset_version_id: String,
    usage: String,
    version_number: i64,
    name: String,
    sha256: String,
    stage: String,
    assay_id: String,
    replicate_id: Option<String>,
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Representative {
    replicate_id: String,
    asset_version_id: Option<String>,
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Candidate {
    asset_version_id: String,
    asset_id: String,
    version_number: i64,
    name: String,
    sha256: String,
    stage: String,
    assay_id: String,
    group_id: Option<String>,
    replicate_id: Option<String>,
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PaperSnapshot {
    papers: Vec<PaperRow>,
    candidates: Vec<Candidate>,
    candidate_total_count: i64,
    candidate_truncated: bool,
}

fn invalid() -> TreeError {
    TreeError::input()
}
fn conflict(message: &'static str) -> TreeError {
    TreeError::new("CONFLICT", message)
}
fn missing() -> TreeError {
    TreeError::new(
        "NOT_FOUND",
        "The requested record does not belong to this project.",
    )
}
fn id(value: Option<&str>) -> Result<&str, TreeError> {
    required(value, 128)
}
fn live_project(c: &Connection, project: &str) -> Result<(), TreeError> {
    let exists: bool = c.query_row(
        "SELECT EXISTS(SELECT 1 FROM projects WHERE id=?1 AND deleted_at IS NULL)",
        [project],
        |r| r.get(0),
    )?;
    if exists {
        Ok(())
    } else {
        Err(missing())
    }
}
fn owns(c: &Connection, project: &str, kind: &str, object: &str) -> Result<(), TreeError> {
    let query = match kind {
        "paper" => "SELECT EXISTS(SELECT 1 FROM papers p JOIN projects pr ON pr.id=p.project_id WHERE p.id=?1 AND pr.id=?2 AND pr.deleted_at IS NULL AND p.deleted_at IS NULL)",
        "figure" => "SELECT EXISTS(SELECT 1 FROM figures f JOIN papers p ON p.id=f.paper_id JOIN projects pr ON pr.id=p.project_id WHERE f.id=?1 AND pr.id=?2 AND pr.deleted_at IS NULL AND p.deleted_at IS NULL)",
        "panel" => "SELECT EXISTS(SELECT 1 FROM panels x JOIN figures f ON f.id=x.figure_id JOIN papers p ON p.id=f.paper_id JOIN projects pr ON pr.id=p.project_id WHERE x.id=?1 AND pr.id=?2 AND pr.deleted_at IS NULL AND p.deleted_at IS NULL)",
        _ => return Err(invalid())
    };
    if c.query_row(query, params![object, project], |r| r.get::<_, bool>(0))? {
        Ok(())
    } else {
        Err(missing())
    }
}
fn valid_version(c: &Connection, project: &str, version: &str) -> Result<String, TreeError> {
    c.query_row(
      "SELECT DISTINCT v.asset_id FROM asset_versions v JOIN data_assets a ON a.id=v.asset_id JOIN asset_links l ON l.asset_id=a.id JOIN assays s ON s.id=l.assay_id JOIN themes t ON t.id=s.theme_id JOIN projects p ON p.id=t.project_id WHERE v.id=?1 AND p.id=?2 AND p.deleted_at IS NULL AND t.deleted_at IS NULL AND s.deleted_at IS NULL AND l.deleted_at IS NULL AND a.deleted_at IS NULL LIMIT 1",
      params![version,project],|r|r.get(0)
    ).optional()?.ok_or_else(missing)
}
fn validate(r: &PaperRequest) -> Result<(), TreeError> {
    id(Some(&r.project_id))?;
    if r.action == "candidates" {
        if r.candidate_query
            .as_ref()
            .is_some_and(|q| q.chars().count() > 160 || q.chars().any(char::is_control))
            || r.candidate_stage.as_ref().is_some_and(|s| {
                !matches!(s.as_str(), "raw" | "processed" | "statistics" | "display")
            })
            || r.candidate_offset.unwrap_or(0) > 1_000_000
            || !(1..=200).contains(&r.candidate_limit.unwrap_or(100))
        {
            return Err(invalid());
        }
    } else if r.candidate_query.is_some()
        || r.candidate_stage.is_some()
        || r.candidate_offset.is_some()
        || r.candidate_limit.is_some()
    {
        return Err(invalid());
    }
    match r.action.as_str() {
        "list" | "candidates" => {
            if r.request_id.is_some() {
                return Err(invalid());
            }
        }
        "create"
        | "rename"
        | "delete"
        | "reorder"
        | "attach"
        | "detach"
        | "representative"
        | "clearRepresentative"
        | "metadata" => {
            id(r.request_id.as_deref())?;
        }
        _ => return Err(invalid()),
    };
    if matches!(
        r.action.as_str(),
        "create" | "rename" | "delete" | "reorder"
    ) {
        if !matches!(r.kind.as_deref(), Some("paper" | "figure" | "panel")) {
            return Err(invalid());
        }
    }
    if matches!(
        r.action.as_str(),
        "attach" | "detach" | "representative" | "clearRepresentative" | "metadata"
    ) {
        id(r.id.as_deref())?;
    }
    if r.action == "create" || r.action == "rename" {
        required_name(r.name.as_deref())?;
    }
    if r.action == "reorder" {
        let ids = r.order_ids.as_ref().ok_or_else(invalid)?;
        if ids.len() > 1000 || ids.is_empty() || ids.iter().any(|v| id(Some(v)).is_err()) {
            return Err(invalid());
        }
    }
    if matches!(r.action.as_str(), "attach" | "detach") {
        if r.action == "attach" && r.version_ids.is_some() {
            if r.version_id.is_some() {
                return Err(invalid());
            }
            let versions = r.version_ids.as_ref().ok_or_else(invalid)?;
            if versions.is_empty()
                || versions.len() > 100
                || versions.iter().any(|v| id(Some(v)).is_err())
            {
                return Err(invalid());
            }
            if versions
                .iter()
                .collect::<std::collections::HashSet<_>>()
                .len()
                != versions.len()
            {
                return Err(invalid());
            }
        } else {
            id(r.version_id.as_deref())?;
        }
        if !matches!(
            r.usage.as_deref(),
            Some("display" | "statistics" | "source" | "other")
        ) {
            return Err(invalid());
        }
    }
    if r.action == "representative" {
        id(r.replicate_id.as_deref())?;
        id(r.version_id.as_deref())?;
    }
    if r.action == "clearRepresentative" {
        id(r.replicate_id.as_deref())?;
    }
    if r.action == "metadata" {
        let st = r.status.as_deref().ok_or_else(invalid)?;
        let notes = r.notes.as_deref().ok_or_else(invalid)?;
        if st.len() > 80
            || st.trim().is_empty()
            || notes.len() > 4000
            || st.chars().any(char::is_control)
            || notes
                .chars()
                .any(|c| c.is_control() && c != '\n' && c != '\t')
        {
            return Err(invalid());
        }
    }
    Ok(())
}
fn mutate(c: &Connection, r: &PaperRequest) -> Result<(), TreeError> {
    let project = &r.project_id;
    match r.action.as_str() {
        "create" => {
            let kind = r.kind.as_deref().ok_or_else(invalid)?;
            let name = required_name(r.name.as_deref())?.trim();
            match kind {
                "paper" => {
                    if r.parent_id.is_some() {
                        return Err(invalid());
                    }
                    c.execute("INSERT INTO papers(id,project_id,title) VALUES(lower(hex(randomblob(16))),?1,?2)",params![project,name])?;
                }
                "figure" => {
                    let parent = id(r.parent_id.as_deref())?;
                    owns(c, project, "paper", parent)?;
                    c.execute("INSERT INTO figures(id,paper_id,label,sort_order) VALUES(lower(hex(randomblob(16))),?1,?2,(SELECT count(*) FROM figures WHERE paper_id=?1))",params![parent,name])?;
                }
                "panel" => {
                    let parent = id(r.parent_id.as_deref())?;
                    owns(c, project, "figure", parent)?;
                    c.execute("INSERT INTO panels(id,figure_id,label,sort_order) VALUES(lower(hex(randomblob(16))),?1,?2,(SELECT count(*) FROM panels WHERE figure_id=?1))",params![parent,name])?;
                }
                _ => return Err(invalid()),
            }
        }
        "rename" => {
            let kind = r.kind.as_deref().ok_or_else(invalid)?;
            let object = id(r.id.as_deref())?;
            owns(c, project, kind, object)?;
            let sql = match kind {
                "paper" => "UPDATE papers SET title=?1 WHERE id=?2",
                "figure" => "UPDATE figures SET label=?1 WHERE id=?2",
                "panel" => "UPDATE panels SET label=?1 WHERE id=?2",
                _ => return Err(invalid()),
            };
            c.execute(
                sql,
                params![required_name(r.name.as_deref())?.trim(), object],
            )?;
        }
        "delete" => {
            let kind = r.kind.as_deref().ok_or_else(invalid)?;
            let object = id(r.id.as_deref())?;
            owns(c, project, kind, object)?;
            let (guard,sql)=match kind {
      "paper"=>("SELECT EXISTS(SELECT 1 FROM figures WHERE paper_id=?1)","UPDATE papers SET deleted_at=strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id=?1"),
      "figure"=>("SELECT EXISTS(SELECT 1 FROM panels WHERE figure_id=?1)","DELETE FROM figures WHERE id=?1"),
      "panel"=>("SELECT EXISTS(SELECT 1 FROM panel_asset_versions WHERE panel_id=?1 UNION ALL SELECT 1 FROM representative_sources WHERE panel_id=?1)","DELETE FROM panels WHERE id=?1"),
      _=>return Err(invalid())};
            let used: bool = c.query_row(guard, [object], |row| row.get(0))?;
            if used {
                return Err(conflict(
                    "Remove dependent records or references before deleting.",
                ));
            }
            c.execute(sql, [object])?;
        }
        "reorder" => {
            let kind = r.kind.as_deref().ok_or_else(invalid)?;
            if kind == "paper" {
                return Err(invalid());
            }
            let parent = id(r.parent_id.as_deref())?;
            owns(
                c,
                project,
                if kind == "figure" { "paper" } else { "figure" },
                parent,
            )?;
            let (table, column) = if kind == "figure" {
                ("figures", "paper_id")
            } else {
                ("panels", "figure_id")
            };
            let ids = r.order_ids.as_ref().ok_or_else(invalid)?;
            let count: i64 = c.query_row(
                &format!("SELECT count(*) FROM {table} WHERE {column}=?1"),
                [parent],
                |row| row.get(0),
            )?;
            if count != ids.len() as i64 {
                return Err(conflict(
                    "Ordering must include every sibling exactly once.",
                ));
            }
            let mut seen = std::collections::HashSet::new();
            for value in ids {
                if !seen.insert(value) {
                    return Err(conflict("Duplicate ordering ID."));
                }
                let exists: bool = c.query_row(
                    &format!("SELECT EXISTS(SELECT 1 FROM {table} WHERE id=?1 AND {column}=?2)"),
                    params![value, parent],
                    |row| row.get(0),
                )?;
                if !exists {
                    return Err(conflict("Ordering contains an unrelated item."));
                }
            }
            for (i, value) in ids.iter().enumerate() {
                c.execute(
                    &format!("UPDATE {table} SET sort_order=?1 WHERE id=?2"),
                    params![i as i64, value],
                )?;
            }
        }
        "attach" | "detach" => {
            let panel = id(r.id.as_deref())?;
            owns(c, project, "panel", panel)?;
            let usage = r.usage.as_deref().ok_or_else(invalid)?;
            let versions = if let Some(versions) = &r.version_ids {
                versions.clone()
            } else {
                vec![id(r.version_id.as_deref())?.to_owned()]
            };
            // Validate all members before issuing any writes. The caller's IMMEDIATE
            // transaction means one invalid item cannot partially attach a selection.
            if r.action == "attach" {
                for version in &versions {
                    valid_version(c, project, version)?;
                }
                for version in &versions {
                    let exists:bool=c.query_row("SELECT EXISTS(SELECT 1 FROM panel_asset_versions WHERE panel_id=?1 AND asset_version_id=?2 AND usage=?3)",params![panel,version,usage],|row|row.get(0))?;
                    if exists {
                        return Err(conflict("An association already exists."));
                    }
                }
                for version in &versions {
                    c.execute("INSERT INTO panel_asset_versions(panel_id,asset_version_id,usage) VALUES(?1,?2,?3)",params![panel,version,usage])?;
                }
            } else {
                let used:bool=c.query_row("SELECT EXISTS(SELECT 1 FROM representative_sources WHERE panel_id=?1 AND asset_version_id=?2)",params![panel,versions[0]],|row|row.get(0))?;
                let another:bool=c.query_row("SELECT EXISTS(SELECT 1 FROM panel_asset_versions WHERE panel_id=?1 AND asset_version_id=?2 AND usage<>?3)",params![panel,versions[0],usage],|row|row.get(0))?;
                if used && !another {
                    return Err(conflict("Clear the representative source before detaching its last Panel reference."));
                }
                let count=c.execute("DELETE FROM panel_asset_versions WHERE panel_id=?1 AND asset_version_id=?2 AND usage=?3",params![panel,versions[0],usage])?;
                if count == 0 {
                    return Err(conflict("The association does not exist."));
                }
            }
        }
        "metadata" => {
            let paper = id(r.id.as_deref())?;
            owns(c, project, "paper", paper)?;
            let status = r.status.as_deref().ok_or_else(invalid)?;
            let notes = r.notes.as_deref().ok_or_else(invalid)?;
            let details = serde_json::json!({"status":status,"notes":notes}).to_string();
            c.execute("INSERT INTO audit_events(id,action,object_type,object_id,details_json) VALUES(lower(hex(randomblob(16))),'paper_metadata','paper',?1,?2)",params![paper,details])?;
        }
        "clearRepresentative" => {
            let panel = id(r.id.as_deref())?;
            owns(c, project, "panel", panel)?;
            let replicate = id(r.replicate_id.as_deref())?;
            let count = c.execute(
                "DELETE FROM representative_sources WHERE panel_id=?1 AND replicate_id=?2",
                params![panel, replicate],
            )?;
            if count == 0 {
                return Err(conflict("No representative source exists."));
            }
        }
        "representative" => {
            let panel = id(r.id.as_deref())?;
            owns(c, project, "panel", panel)?;
            let version = id(r.version_id.as_deref())?;
            let asset = valid_version(c, project, version)?;
            let replicate = id(r.replicate_id.as_deref())?;
            let valid: bool=c.query_row(
      "SELECT EXISTS(SELECT 1 FROM replicates r JOIN experiment_groups g ON g.id=r.group_id JOIN assays s ON s.id=g.assay_id JOIN themes t ON t.id=s.theme_id JOIN asset_links l ON l.replicate_id=r.id AND l.assay_id=s.id AND l.asset_id=?3 AND l.deleted_at IS NULL WHERE r.id=?1 AND t.project_id=?2 AND r.deleted_at IS NULL AND g.deleted_at IS NULL AND s.deleted_at IS NULL AND t.deleted_at IS NULL)",
      params![replicate,project,asset],|row|row.get(0))?;
            if !valid {
                return Err(conflict(
                    "The chosen version does not originate from this biological replicate.",
                ));
            }
            let linked:bool=c.query_row("SELECT EXISTS(SELECT 1 FROM panel_asset_versions WHERE panel_id=?1 AND asset_version_id=?2)",params![panel,version],|row|row.get(0))?;
            if !linked {
                return Err(conflict(
                    "Attach the fixed version to the Panel before selecting a representative.",
                ));
            }
            c.execute("INSERT INTO representative_sources(panel_id,replicate_id,asset_version_id) VALUES(?1,?2,?3) ON CONFLICT(panel_id,replicate_id) DO UPDATE SET asset_version_id=excluded.asset_version_id",params![panel,replicate,version])?;
        }
        _ => return Err(invalid()),
    }
    Ok(())
}
fn query(
    c: &Connection,
    project: &str,
    request: &PaperRequest,
) -> Result<PaperSnapshot, TreeError> {
    let mut papers = Vec::new();
    let mut ps = c.prepare(
        "SELECT id,title FROM papers WHERE project_id=?1 AND deleted_at IS NULL ORDER BY title,id",
    )?;
    let paper_rows = ps.query_map([project], |row| {
        Ok((row.get::<_, String>(0)?, row.get::<_, String>(1)?))
    })?;
    for p in paper_rows {
        let (paper_id, title) = p?;
        let mut figures = Vec::new();
        let mut fs =
            c.prepare("SELECT id,label FROM figures WHERE paper_id=?1 ORDER BY sort_order,id")?;
        for f in fs.query_map([&paper_id], |row| {
            Ok((row.get::<_, String>(0)?, row.get::<_, String>(1)?))
        })? {
            let (figure_id, label) = f?;
            let mut panels = Vec::new();
            let mut xs =
                c.prepare("SELECT id,label FROM panels WHERE figure_id=?1 ORDER BY sort_order,id")?;
            for x in xs.query_map([&figure_id], |row| {
                Ok((row.get::<_, String>(0)?, row.get::<_, String>(1)?))
            })? {
                let (panel_id, label) = x?;
                let mut asset_stmt=c.prepare("SELECT pav.asset_version_id,pav.usage,v.version_number,COALESCE((SELECT l.display_name FROM asset_links l JOIN assays a ON a.id=l.assay_id JOIN themes t ON t.id=a.theme_id WHERE l.asset_id=v.asset_id AND t.project_id=?2 AND l.deleted_at IS NULL ORDER BY l.id LIMIT 1),v.source_name),v.blob_sha256,COALESCE((SELECT l.stage FROM asset_links l JOIN assays a ON a.id=l.assay_id JOIN themes t ON t.id=a.theme_id WHERE l.asset_id=v.asset_id AND t.project_id=?2 AND l.deleted_at IS NULL ORDER BY l.id LIMIT 1),'unknown'),COALESCE((SELECT l.assay_id FROM asset_links l JOIN assays a ON a.id=l.assay_id JOIN themes t ON t.id=a.theme_id WHERE l.asset_id=v.asset_id AND t.project_id=?2 AND l.deleted_at IS NULL ORDER BY l.id LIMIT 1),''),(SELECT l.replicate_id FROM asset_links l JOIN assays a ON a.id=l.assay_id JOIN themes t ON t.id=a.theme_id WHERE l.asset_id=v.asset_id AND t.project_id=?2 AND l.deleted_at IS NULL ORDER BY l.id LIMIT 1) FROM panel_asset_versions pav JOIN asset_versions v ON v.id=pav.asset_version_id WHERE pav.panel_id=?1 ORDER BY pav.usage,pav.asset_version_id")?;
                let assets = asset_stmt
                    .query_map(params![panel_id, project], |row| {
                        Ok(PanelAsset {
                            asset_version_id: row.get(0)?,
                            usage: row.get(1)?,
                            version_number: row.get(2)?,
                            name: row.get(3)?,
                            sha256: row.get(4)?,
                            stage: row.get(5)?,
                            assay_id: row.get(6)?,
                            replicate_id: row.get(7)?,
                        })
                    })?
                    .collect::<rusqlite::Result<Vec<_>>>()?;
                let mut rep_stmt=c.prepare("SELECT replicate_id,asset_version_id FROM representative_sources WHERE panel_id=?1 ORDER BY replicate_id")?;
                let representatives = rep_stmt
                    .query_map([&panel_id], |row| {
                        Ok(Representative {
                            replicate_id: row.get(0)?,
                            asset_version_id: row.get(1)?,
                        })
                    })?
                    .collect::<rusqlite::Result<Vec<_>>>()?;
                panels.push(PanelRow {
                    id: panel_id,
                    label,
                    assets,
                    representatives,
                });
            }
            figures.push(FigureRow {
                id: figure_id,
                label,
                panels,
            });
        }
        let metadata:Option<String>=c.query_row("SELECT details_json FROM audit_events WHERE action='paper_metadata' AND object_type='paper' AND object_id=?1 ORDER BY rowid DESC LIMIT 1",[&paper_id],|row|row.get(0)).optional()?;
        let meta: serde_json::Value = metadata
            .as_deref()
            .and_then(|value| serde_json::from_str(value).ok())
            .unwrap_or_else(|| serde_json::json!({}));
        let status = meta
            .get("status")
            .and_then(|v| v.as_str())
            .unwrap_or("draft")
            .to_owned();
        let notes = meta
            .get("notes")
            .and_then(|v| v.as_str())
            .unwrap_or("")
            .to_owned();
        papers.push(PaperRow {
            id: paper_id,
            title,
            status,
            notes,
            figures,
        });
    }
    let mut candidates = Vec::new();
    let mut candidate_total_count = 0;
    let mut candidate_truncated = false;
    if request.action == "candidates" {
        let search = request.candidate_query.as_deref().unwrap_or("").trim();
        let pattern = format!("%{}%", search);
        let stage = request.candidate_stage.as_deref().unwrap_or("");
        let offset = request.candidate_offset.unwrap_or(0);
        let limit = request.candidate_limit.unwrap_or(100);
        // Run exactly the same SQL predicate for total count and page contents.
        // DISTINCT prevents multiple AssetLinks multiplying the same candidate.
        let source = "FROM asset_versions v
          JOIN data_assets da ON da.id=v.asset_id
          JOIN asset_links l ON l.asset_id=v.asset_id
          JOIN assays a ON a.id=l.assay_id
          JOIN themes t ON t.id=a.theme_id
          WHERE t.project_id=?1 AND t.deleted_at IS NULL
            AND a.deleted_at IS NULL AND l.deleted_at IS NULL
            AND da.deleted_at IS NULL
            AND (?2='' OR l.display_name LIKE ?3 OR v.id LIKE ?3 OR da.name LIKE ?3)
            AND (?4='' OR l.stage=?4)";
        let columns = "v.id,v.asset_id,v.version_number,l.display_name,v.blob_sha256,
                       l.stage,l.assay_id,l.group_id,l.replicate_id";
        let count_sql = format!("SELECT count(*) FROM (SELECT DISTINCT {columns} {source})");
        candidate_total_count = c.query_row(
            &count_sql,
            params![project, search, pattern, stage],
            |row| row.get(0),
        )?;
        let sql = format!(
            "SELECT DISTINCT {columns} {source}
          ORDER BY l.display_name COLLATE NOCASE,v.version_number DESC,
                   v.id,l.assay_id,l.group_id,l.replicate_id
          LIMIT ?5 OFFSET ?6"
        );
        let mut stmt = c.prepare(&sql)?;
        candidates = stmt
            .query_map(
                params![project, search, pattern, stage, limit, offset],
                |row| {
                    Ok(Candidate {
                        asset_version_id: row.get(0)?,
                        asset_id: row.get(1)?,
                        version_number: row.get(2)?,
                        name: row.get(3)?,
                        sha256: row.get(4)?,
                        stage: row.get(5)?,
                        assay_id: row.get(6)?,
                        group_id: row.get(7)?,
                        replicate_id: row.get(8)?,
                    })
                },
            )?
            .collect::<rusqlite::Result<Vec<_>>>()?;
        candidate_truncated = i64::from(offset) + (candidates.len() as i64) < candidate_total_count;
    }
    Ok(PaperSnapshot {
        papers,
        candidates,
        candidate_total_count,
        candidate_truncated,
    })
}
pub(super) fn execute_at(path: &Path, r: PaperRequest) -> Result<PaperSnapshot, TreeError> {
    validate(&r)?;
    with_database_at(path, |c| {
        live_project(c, &r.project_id)?;
        if r.action != "list" && r.action != "candidates" {
            let tx = c.transaction_with_behavior(TransactionBehavior::Immediate)?;
            live_project(&tx, &r.project_id)?;
            let token = id(r.request_id.as_deref())?;
            let payload =
                serde_json::to_string(&serde_json::json!({"scope":"paper_workspace","request":r}))
                    .map_err(|_| invalid())?;
            let previous: Option<String> = tx
                .query_row(
                    "SELECT payload FROM s1_requests WHERE request_id=?1",
                    [token],
                    |row| row.get(0),
                )
                .optional()?;
            match previous {
                Some(before) if before != payload => {
                    return Err(TreeError::new(
                        "REQUEST_CONFLICT",
                        "The request ID was already used.",
                    ))
                }
                Some(_) => {}
                None => {
                    mutate(&tx, &r)?;
                    tx.execute(
                        "INSERT INTO s1_requests(request_id,payload) VALUES(?1,?2)",
                        params![token, payload],
                    )?;
                    tx.execute("INSERT INTO audit_events(id,action,object_type,object_id,details_json) VALUES(lower(hex(randomblob(16))),?1,'paper_workspace',?2,?3)",params![r.action,r.id.as_deref().unwrap_or(&r.project_id),serde_json::to_string(&r).map_err(|_|invalid())?])?;
                }
            }
            tx.commit()?;
        }
        query(c, &r.project_id, &r)
    })
}
#[tauri::command]
pub fn paper_workspace<R: tauri::Runtime>(
    app: tauri::AppHandle<R>,
    request: PaperRequest,
) -> Result<PaperSnapshot, TreeError> {
    execute_at(&database_path(&app)?, request)
}
#[cfg(test)]
mod tests {
    use super::*;
    use crate::project_tree::with_database_at;
    use tempfile::tempdir;
    fn fixture() -> (tempfile::TempDir, std::path::PathBuf) {
        let dir = tempdir().unwrap();
        let path = dir.path().join("db.sqlite");
        with_database_at(&path,|c|{
    c.execute_batch("INSERT INTO projects(id,name) VALUES('p','Project'),('other','Other'); INSERT INTO themes(id,project_id,name) VALUES('t','p','Theme'),('t2','other','Other'); INSERT INTO assays(id,theme_id,name) VALUES('a','t','Assay'),('a2','t2','Other'); INSERT INTO experiment_groups(id,assay_id,name) VALUES('g','a','Group'); INSERT INTO replicates(id,group_id,label) VALUES('r','g','R'); INSERT INTO data_assets(id,name) VALUES('asset','raw.tif'); INSERT INTO blobs(sha256,size_bytes) VALUES('aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',1); INSERT INTO asset_versions(id,asset_id,version_number,blob_sha256,source_name) VALUES('v1','asset',1,'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa','raw.tif'); INSERT INTO asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name) VALUES('link','asset','a','g','r','raw','raw.tif');")?;Ok(())
  }).unwrap();
        (dir, path)
    }
    fn request(
        action: &str,
        kind: Option<&str>,
        id: Option<&str>,
        parent: Option<&str>,
        name: Option<&str>,
        token: Option<&str>,
    ) -> PaperRequest {
        PaperRequest {
            action: action.into(),
            project_id: "p".into(),
            kind: kind.map(str::to_owned),
            id: id.map(str::to_owned),
            parent_id: parent.map(str::to_owned),
            name: name.map(str::to_owned),
            version_id: None,
            version_ids: None,
            replicate_id: None,
            usage: None,
            status: None,
            notes: None,
            order_ids: None,
            request_id: token.map(str::to_owned),
            candidate_query: None,
            candidate_stage: None,
            candidate_offset: None,
            candidate_limit: None,
        }
    }
    #[test]
    fn s10_candidate_pages_are_server_filtered_and_counted_beyond_old_cap() {
        let (_dir, path) = fixture();
        with_database_at(&path,|c|{
            let tx=c.transaction()?;
            for n in 2..=605 {
                tx.execute(
                    "INSERT INTO asset_versions(id,asset_id,version_number,blob_sha256,source_name)
                     VALUES(?1,'asset',?2,'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa','raw.tif')",
                    params![format!("v{n}"),n],
                )?;
            }
            tx.commit()?;
            Ok(())
        }).unwrap();
        let mut req = request("candidates", None, None, None, None, None);
        req.candidate_offset = Some(0);
        req.candidate_limit = Some(100);
        let first = execute_at(&path, req.clone()).unwrap();
        assert_eq!(first.candidate_total_count, 605);
        assert_eq!(first.candidates.len(), 100);
        assert!(first.candidate_truncated);
        req.candidate_offset = Some(600);
        let tail = execute_at(&path, req.clone()).unwrap();
        assert_eq!(tail.candidate_total_count, 605);
        assert_eq!(tail.candidates.len(), 5);
        assert!(!tail.candidate_truncated);
        req.candidate_offset = Some(0);
        req.candidate_query = Some("v605".into());
        let filtered = execute_at(&path, req).unwrap();
        assert_eq!(filtered.candidate_total_count, 1);
        assert_eq!(filtered.candidates[0].asset_version_id, "v605");
    }
    #[test]
    fn s9_persistence_and_version_pin() {
        let (_d, path) = fixture();
        let p = execute_at(
            &path,
            request(
                "create",
                Some("paper"),
                None,
                None,
                Some("Paper"),
                Some("p1"),
            ),
        )
        .unwrap();
        let pid = p.papers[0].id.clone();
        let f = execute_at(
            &path,
            request(
                "create",
                Some("figure"),
                None,
                Some(&pid),
                Some("Figure 1"),
                Some("f1"),
            ),
        )
        .unwrap();
        let fid = f.papers[0].figures[0].id.clone();
        let x = execute_at(
            &path,
            request(
                "create",
                Some("panel"),
                None,
                Some(&fid),
                Some("A"),
                Some("x1"),
            ),
        )
        .unwrap();
        let xid = x.papers[0].figures[0].panels[0].id.clone();
        let mut a = request("attach", None, Some(&xid), None, None, Some("attach1"));
        a.version_id = Some("v1".into());
        a.usage = Some("source".into());
        execute_at(&path, a.clone()).unwrap();
        execute_at(&path, a).unwrap();
        with_database_at(&path,|c|{c.execute("INSERT INTO asset_versions(id,asset_id,version_number,blob_sha256,source_name) VALUES('v2','asset',2,'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa','v2.tif')",[])?; Ok(())}).unwrap();
        let loaded = execute_at(&path, request("list", None, None, None, None, None)).unwrap();
        assert_eq!(
            loaded.papers[0].figures[0].panels[0].assets[0].asset_version_id,
            "v1"
        );
        let mut rep = request("representative", None, Some(&xid), None, None, Some("rep1"));
        rep.version_id = Some("v1".into());
        rep.replicate_id = Some("r".into());
        execute_at(&path, rep).unwrap();
        let count = with_database_at(&path, |c| {
            Ok(c.query_row("SELECT count(*) FROM replicates", [], |r| {
                r.get::<_, i64>(0)
            })?)
        })
        .unwrap();
        assert_eq!(count, 1);
    }
    #[test]
    fn s9_rejects_cross_project_and_duplicates() {
        let (_d, path) = fixture();
        let p = execute_at(
            &path,
            request(
                "create",
                Some("paper"),
                None,
                None,
                Some("Study"),
                Some("p2"),
            ),
        )
        .unwrap();
        let pid = &p.papers[0].id;
        let f = execute_at(
            &path,
            request(
                "create",
                Some("figure"),
                None,
                Some(pid),
                Some("F"),
                Some("f2"),
            ),
        )
        .unwrap();
        let fid = &f.papers[0].figures[0].id;
        let x = execute_at(
            &path,
            request(
                "create",
                Some("panel"),
                None,
                Some(fid),
                Some("A"),
                Some("x2"),
            ),
        )
        .unwrap();
        let xid = &x.papers[0].figures[0].panels[0].id;
        let mut a = request("attach", None, Some(xid), None, None, Some("attach2"));
        a.version_id = Some("v1".into());
        a.usage = Some("display".into());
        execute_at(&path, a.clone()).unwrap();
        a.request_id = Some("attach3".into());
        assert!(execute_at(&path, a).is_err());
        let mut other = request(
            "rename",
            Some("panel"),
            Some(xid),
            None,
            Some("bad"),
            Some("bad1"),
        );
        other.project_id = "other".into();
        assert!(execute_at(&path, other).is_err());
    }
    #[test]
    fn s9_transactional_order_and_failed_permutation_rollback() {
        let (_d, path) = fixture();
        let p = execute_at(
            &path,
            request(
                "create",
                Some("paper"),
                None,
                None,
                Some("Order"),
                Some("ordpaper"),
            ),
        )
        .unwrap();
        let pid = &p.papers[0].id;
        let x = execute_at(
            &path,
            request(
                "create",
                Some("figure"),
                None,
                Some(pid),
                Some("F1"),
                Some("ord1"),
            ),
        )
        .unwrap();
        let first = x.papers[0].figures[0].id.clone();
        let x = execute_at(
            &path,
            request(
                "create",
                Some("figure"),
                None,
                Some(pid),
                Some("F2"),
                Some("ord2"),
            ),
        )
        .unwrap();
        let second = x.papers[0].figures[1].id.clone();
        let mut sort = request(
            "reorder",
            Some("figure"),
            None,
            Some(pid),
            None,
            Some("sortvalid"),
        );
        sort.order_ids = Some(vec![second.clone(), first.clone()]);
        execute_at(&path, sort).unwrap();
        let mut invalid = request(
            "reorder",
            Some("figure"),
            None,
            Some(pid),
            None,
            Some("sortinvalid"),
        );
        invalid.order_ids = Some(vec![first.clone(), first.clone()]);
        assert!(execute_at(&path, invalid).is_err());
        let out = execute_at(&path, request("list", None, None, None, None, None)).unwrap();
        assert_eq!(out.papers[0].figures[0].id, second);
        assert_eq!(out.papers[0].figures[1].id, first);
    }
    #[test]
    fn s9_atomic_bulk_attach_and_representative_guard() {
        let (_d, path) = fixture();
        let p = execute_at(
            &path,
            request(
                "create",
                Some("paper"),
                None,
                None,
                Some("Study"),
                Some("bulkpaper"),
            ),
        )
        .unwrap();
        let pid = &p.papers[0].id;
        let x = execute_at(
            &path,
            request(
                "create",
                Some("figure"),
                None,
                Some(pid),
                Some("F"),
                Some("bulkfig"),
            ),
        )
        .unwrap();
        let fid = &x.papers[0].figures[0].id;
        let x = execute_at(
            &path,
            request(
                "create",
                Some("panel"),
                None,
                Some(fid),
                Some("Panel"),
                Some("bulkpanel"),
            ),
        )
        .unwrap();
        let panel = &x.papers[0].figures[0].panels[0].id;
        let mut bulk = request("attach", None, Some(panel), None, None, Some("bulkfail"));
        bulk.version_ids = Some(vec!["v1".into(), "missing".into()]);
        bulk.usage = Some("source".into());
        assert!(execute_at(&path, bulk).is_err());
        let count = with_database_at(&path, |c| {
            Ok(
                c.query_row("SELECT count(*) FROM panel_asset_versions", [], |row| {
                    row.get::<_, i64>(0)
                })?,
            )
        })
        .unwrap();
        assert_eq!(count, 0);
        let mut valid = request("attach", None, Some(panel), None, None, Some("bulkok"));
        valid.version_ids = Some(vec!["v1".into()]);
        valid.usage = Some("source".into());
        execute_at(&path, valid).unwrap();
        let mut rep = request(
            "representative",
            None,
            Some(panel),
            None,
            None,
            Some("bulkrep"),
        );
        rep.replicate_id = Some("r".into());
        rep.version_id = Some("v1".into());
        execute_at(&path, rep).unwrap();
        let mut detach = request("detach", None, Some(panel), None, None, Some("bulkdetach"));
        detach.version_id = Some("v1".into());
        detach.usage = Some("source".into());
        assert!(execute_at(&path, detach.clone()).is_err());
        let mut clear = request(
            "clearRepresentative",
            None,
            Some(panel),
            None,
            None,
            Some("bulkclear"),
        );
        clear.replicate_id = Some("r".into());
        execute_at(&path, clear).unwrap();
        execute_at(&path, detach).unwrap();
        assert_eq!(
            with_database_at(&path, |c| Ok(c.query_row(
                "SELECT count(*) FROM asset_versions",
                [],
                |row| row.get::<_, i64>(0)
            )?))
            .unwrap(),
            1
        );
    }
    #[test]
    fn s9_rename_copy_links_do_not_change_version_or_n() {
        let (_d, path) = fixture();
        let p = execute_at(
            &path,
            request(
                "create",
                Some("paper"),
                None,
                None,
                Some("Paper"),
                Some("rp1"),
            ),
        )
        .unwrap();
        let x = execute_at(
            &path,
            request(
                "create",
                Some("figure"),
                None,
                Some(&p.papers[0].id),
                Some("F"),
                Some("rf1"),
            ),
        )
        .unwrap();
        let x = execute_at(
            &path,
            request(
                "create",
                Some("panel"),
                None,
                Some(&x.papers[0].figures[0].id),
                Some("Panel"),
                Some("rpanel"),
            ),
        )
        .unwrap();
        let panel = x.papers[0].figures[0].panels[0].id.clone();
        let mut a = request("attach", None, Some(&panel), None, None, Some("rattach"));
        a.usage = Some("display".into());
        a.version_id = Some("v1".into());
        execute_at(&path, a).unwrap();
        with_database_at(&path,|c|{
    c.execute("UPDATE asset_links SET display_name='renamed.tif' WHERE id='link'",[])?;
    c.execute("INSERT INTO asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name) VALUES('copy','asset','a','g','r','processed','copy.tif')",[])?;
    c.execute("UPDATE asset_links SET stage='display' WHERE id='copy'",[])?;
    Ok(())
  }).unwrap();
        let x = execute_at(&path, request("list", None, None, None, None, None)).unwrap();
        assert_eq!(
            x.papers[0].figures[0].panels[0].assets[0].asset_version_id,
            "v1"
        );
        let n = with_database_at(&path, |c| {
            Ok(c.query_row("SELECT count(*) FROM replicates", [], |row| {
                row.get::<_, i64>(0)
            })?)
        })
        .unwrap();
        assert_eq!(n, 1);
    }
    #[test]
    fn s9_paper_metadata_persists_in_existing_audit_schema() {
        let (_d, path) = fixture();
        let p = execute_at(
            &path,
            request(
                "create",
                Some("paper"),
                None,
                None,
                Some("Paper"),
                Some("mpaper"),
            ),
        )
        .unwrap();
        let mut metadata = request(
            "metadata",
            None,
            Some(&p.papers[0].id),
            None,
            None,
            Some("m1"),
        );
        metadata.status = Some("submitted".into());
        metadata.notes = Some("Revision 1".into());
        execute_at(&path, metadata).unwrap();
        let loaded = execute_at(&path, request("list", None, None, None, None, None)).unwrap();
        assert_eq!(loaded.papers[0].status, "submitted");
        assert_eq!(loaded.papers[0].notes, "Revision 1");
    }
    #[test]
    fn i1_paper_and_statistics_share_schema_v3_after_reopen() {
        let (_temp, path) = fixture();
        let paper = execute_at(
            &path,
            request(
                "create",
                Some("paper"),
                None,
                None,
                Some("I1 Paper"),
                Some("i1-paper-create"),
            ),
        )
        .unwrap();
        let id = paper.papers[0].id.clone();
        let tables = with_database_at(&path, |connection| {
            let version: i64 = connection.pragma_query_value(None, "user_version", |r| r.get(0))?;
            let analyses: i64 =
                connection.query_row("SELECT count(*) FROM statistical_analyses", [], |r| {
                    r.get(0)
                })?;
            let papers: i64 =
                connection.query_row("SELECT count(*) FROM papers WHERE id=?1", [&id], |r| {
                    r.get(0)
                })?;
            Ok((version, analyses, papers))
        })
        .unwrap();
        assert_eq!(tables.0, 3);
        assert_eq!(tables.1, 0);
        assert_eq!(tables.2, 1);
        let reopened = execute_at(&path, request("list", None, None, None, None, None)).unwrap();
        assert_eq!(reopened.papers[0].id, id);
    }
}
