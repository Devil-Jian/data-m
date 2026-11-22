//! S8: real assay-scoped, persisted statistical analyses. No statistical computation.
use crate::project_tree::{database_path, required, required_name, with_database_at, TreeError};
use rusqlite::{params, Connection, OptionalExtension, TransactionBehavior};
use serde::{Deserialize, Serialize};
use std::collections::HashSet;
use std::path::Path;

#[derive(Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AnalysisRequest {
    action: String,
    assay_id: String,
    analysis_id: Option<String>,
    name: Option<String>,
    notes: Option<String>,
    replicate_ids: Option<Vec<String>>,
    input_version_ids: Option<Vec<String>>,
    result_version_ids: Option<Vec<String>>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AnalysisSummary {
    id: String,
    name: String,
    notes: String,
    frozen_at: Option<String>,
    updated_at: String,
    included_n: i64,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct InclusionRow {
    replicate_id: String,
    group_id: String,
    group_name: String,
    label: String,
    included: bool,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct VersionChoice {
    id: String,
    asset_name: String,
    version_number: i64,
    deleted: bool,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AnalysisResponse {
    assay_id: String,
    selected_id: Option<String>,
    analyses: Vec<AnalysisSummary>,
    samples: Vec<InclusionRow>,
    available_versions: Vec<VersionChoice>,
    input_version_ids: Vec<String>,
    result_version_ids: Vec<String>,
}

#[tauri::command]
pub fn statistical_analysis<R: tauri::Runtime>(
    app: tauri::AppHandle<R>,
    request: AnalysisRequest,
) -> Result<AnalysisResponse, TreeError> {
    let path = database_path(&app)?;
    execute_at(&path, request)
}

fn validate(request: &AnalysisRequest) -> Result<(), TreeError> {
    required(Some(&request.assay_id), 128)?;
    match request.action.as_str() {
        "list" | "get" => {
            if request.action == "get" {
                required(request.analysis_id.as_deref(), 128)?;
            } else if request.analysis_id.is_some() {
                return Err(TreeError::input());
            }
            if request.name.is_some()
                || request.notes.is_some()
                || request.replicate_ids.is_some()
                || request.input_version_ids.is_some()
                || request.result_version_ids.is_some()
            {
                return Err(TreeError::input());
            }
        }
        "create" | "save" => {
            required_name(request.name.as_deref())?;
            if request.action == "save" {
                required(request.analysis_id.as_deref(), 128)?;
            } else if request.analysis_id.is_some() {
                return Err(TreeError::input());
            }
            let notes = request.notes.as_deref().unwrap_or("");
            if notes.chars().count() > 4000 || notes.chars().any(|c| c == '\0') {
                return Err(TreeError::input());
            }
            check_ids(request.replicate_ids.as_ref())?;
            check_ids(request.input_version_ids.as_ref())?;
            check_ids(request.result_version_ids.as_ref())?;
        }
        "freeze" | "revise" => {
            required(request.analysis_id.as_deref(), 128)?;
            if request.name.is_some()
                || request.notes.is_some()
                || request.replicate_ids.is_some()
                || request.input_version_ids.is_some()
                || request.result_version_ids.is_some()
            {
                return Err(TreeError::input());
            }
        }
        _ => return Err(TreeError::input()),
    }
    Ok(())
}
fn check_ids(ids: Option<&Vec<String>>) -> Result<(), TreeError> {
    let values = ids.ok_or_else(TreeError::input)?;
    if values.len() > 10000 {
        return Err(TreeError::input());
    }
    let mut unique = HashSet::new();
    for id in values {
        required(Some(id.as_str()), 128)?;
        if !unique.insert(id) {
            return Err(TreeError::new(
                "DUPLICATE_ID",
                "Duplicate selections are not allowed.",
            ));
        }
    }
    Ok(())
}
fn assay_valid(c: &Connection, assay: &str) -> Result<(), TreeError> {
    let exists: bool = c.query_row(
        "SELECT EXISTS(SELECT 1 FROM assays a JOIN themes t ON a.theme_id=t.id
           JOIN projects p ON t.project_id=p.id
           WHERE a.id=?1 AND a.deleted_at IS NULL AND t.deleted_at IS NULL
           AND p.deleted_at IS NULL)",
        [assay],
        |r| r.get(0),
    )?;
    if exists {
        Ok(())
    } else {
        Err(TreeError::new("NOT_FOUND", "The assay does not exist."))
    }
}
fn status(c: &Connection, assay: &str, id: &str) -> Result<bool, TreeError> {
    c.query_row(
        "SELECT frozen_at IS NOT NULL FROM statistical_analyses
                 WHERE id=?1 AND assay_id=?2",
        params![id, assay],
        |r| r.get(0),
    )
    .optional()?
    .ok_or_else(|| TreeError::new("NOT_FOUND", "The analysis was not found in this assay."))
}
fn audit(c: &Connection, id: &str, action: &str, details: &str) -> Result<(), TreeError> {
    c.execute(
        "INSERT INTO audit_events(id,action,object_type,object_id,details_json)
         VALUES(lower(hex(randomblob(16))),?1,'statistical_analysis',?2,?3)",
        params![action, id, details],
    )?;
    Ok(())
}
fn replace_selection(c: &Connection, id: &str, request: &AnalysisRequest) -> Result<(), TreeError> {
    c.execute("DELETE FROM analysis_samples WHERE analysis_id=?1", [id])?;
    c.execute(
        "DELETE FROM analysis_asset_versions WHERE analysis_id=?1",
        [id],
    )?;
    for replicate in request
        .replicate_ids
        .as_ref()
        .ok_or_else(TreeError::input)?
    {
        let active: bool = c.query_row(
            "SELECT EXISTS(SELECT 1 FROM replicates r JOIN experiment_groups g ON g.id=r.group_id
               WHERE r.id=?1 AND r.deleted_at IS NULL AND g.deleted_at IS NULL
                 AND g.assay_id=?2)",
            params![replicate, request.assay_id],
            |r| r.get(0),
        )?;
        if !active {
            return Err(TreeError::new(
                "INVALID_REPLICATE",
                "The sample is not active in this assay.",
            ));
        }
        c.execute(
            "INSERT INTO analysis_samples(analysis_id,replicate_id,included)
                   VALUES(?1,?2,1)",
            params![id, replicate],
        )?;
    }
    for (role, ids) in [
        (
            "input",
            request
                .input_version_ids
                .as_ref()
                .ok_or_else(TreeError::input)?,
        ),
        (
            "result",
            request
                .result_version_ids
                .as_ref()
                .ok_or_else(TreeError::input)?,
        ),
    ] {
        for version in ids {
            let linked: bool = c.query_row(
                "SELECT EXISTS(SELECT 1 FROM asset_versions v
                 JOIN asset_links l ON l.asset_id=v.asset_id
                 WHERE v.id=?1 AND l.assay_id=?2)",
                params![version, request.assay_id],
                |r| r.get(0),
            )?;
            if !linked {
                return Err(TreeError::new(
                    "INVALID_VERSION",
                    "Version has no historical link to this assay.",
                ));
            }
            c.execute(
                "INSERT INTO analysis_asset_versions(analysis_id,asset_version_id,role)
                       VALUES(?1,?2,?3)",
                params![id, version, role],
            )?;
        }
    }
    Ok(())
}
fn mutate(c: &Connection, request: &AnalysisRequest) -> Result<String, TreeError> {
    let assay = request.assay_id.as_str();
    match request.action.as_str() {
        "create" => {
            let id: String = c.query_row(
                "INSERT INTO statistical_analyses(id,assay_id,name,notes,updated_at)
                 VALUES(lower(hex(randomblob(16))),?1,?2,?3,strftime('%Y-%m-%dT%H:%M:%fZ','now'))
                 RETURNING id",
                params![
                    assay,
                    required_name(request.name.as_deref())?.trim(),
                    request.notes.as_deref().unwrap_or("")
                ],
                |r| r.get(0),
            )?;
            replace_selection(c, &id, request)?;
            audit(c, &id, "analysis_create", "{}")?;
            Ok(id)
        }
        "save" => {
            let id = required(request.analysis_id.as_deref(), 128)?;
            if status(c, assay, id)? {
                return Err(TreeError::new(
                    "FROZEN",
                    "Frozen analyses cannot be edited.",
                ));
            }
            c.execute(
                "UPDATE statistical_analyses SET name=?1,notes=?2,
                 updated_at=strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id=?3",
                params![
                    required_name(request.name.as_deref())?.trim(),
                    request.notes.as_deref().unwrap_or(""),
                    id
                ],
            )?;
            replace_selection(c, id, request)?;
            audit(c, id, "analysis_save", "{}")?;
            Ok(id.to_owned())
        }
        "freeze" => {
            let id = required(request.analysis_id.as_deref(), 128)?;
            if status(c, assay, id)? {
                return Err(TreeError::new("FROZEN", "Analysis is already frozen."));
            }
            c.execute(
                "UPDATE statistical_analyses SET frozen_at=strftime('%Y-%m-%dT%H:%M:%fZ','now'),
               updated_at=strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id=?1",
                [id],
            )?;
            audit(c, id, "analysis_freeze", "{}")?;
            Ok(id.to_owned())
        }
        "revise" => {
            let source = required(request.analysis_id.as_deref(), 128)?;
            if !status(c, assay, source)? {
                return Err(TreeError::new(
                    "NOT_FROZEN",
                    "Only frozen analyses can be revised.",
                ));
            }
            let id: String = c.query_row(
                "INSERT INTO statistical_analyses(id,assay_id,name,notes,updated_at)
                 SELECT lower(hex(randomblob(16))),assay_id,name || ' (revision)',notes,
                     strftime('%Y-%m-%dT%H:%M:%fZ','now')
                 FROM statistical_analyses WHERE id=?1 RETURNING id",
                [source],
                |r| r.get(0),
            )?;
            c.execute(
                "INSERT INTO analysis_samples(analysis_id,replicate_id,included)
                SELECT ?1,replicate_id,included FROM analysis_samples WHERE analysis_id=?2",
                params![id, source],
            )?;
            c.execute(
                "INSERT INTO analysis_asset_versions(analysis_id,asset_version_id,role)
                SELECT ?1,asset_version_id,role FROM analysis_asset_versions WHERE analysis_id=?2",
                params![id, source],
            )?;
            audit(
                c,
                &id,
                "analysis_revise",
                &serde_json::json!({"sourceId":source}).to_string(),
            )?;
            Ok(id)
        }
        _ => Err(TreeError::input()),
    }
}
fn snapshot(
    c: &Connection,
    assay: &str,
    selected: Option<String>,
) -> Result<AnalysisResponse, TreeError> {
    let mut stmt = c.prepare(
        "SELECT a.id,a.name,a.notes,a.frozen_at,a.updated_at,
          (SELECT count(*) FROM analysis_samples s WHERE s.analysis_id=a.id AND s.included=1)
          FROM statistical_analyses a WHERE a.assay_id=?1
          ORDER BY a.updated_at DESC,a.id",
    )?;
    let analyses: Vec<AnalysisSummary> = stmt
        .query_map([assay], |r| {
            Ok(AnalysisSummary {
                id: r.get(0)?,
                name: r.get(1)?,
                notes: r.get(2)?,
                frozen_at: r.get(3)?,
                updated_at: r.get(4)?,
                included_n: r.get(5)?,
            })
        })?
        .collect::<rusqlite::Result<_>>()?;
    let mut stmt = c.prepare(
        "SELECT r.id,g.id,g.name,r.label,
          CASE WHEN s.included=1 THEN 1 ELSE 0 END
         FROM replicates r JOIN experiment_groups g ON r.group_id=g.id
         LEFT JOIN analysis_samples s ON s.replicate_id=r.id AND s.analysis_id=?2
         WHERE g.assay_id=?1 AND g.deleted_at IS NULL AND r.deleted_at IS NULL
         ORDER BY g.sort_order,g.id,r.id",
    )?;
    let samples: Vec<InclusionRow> = stmt
        .query_map(params![assay, selected], |r| {
            Ok(InclusionRow {
                replicate_id: r.get(0)?,
                group_id: r.get(1)?,
                group_name: r.get(2)?,
                label: r.get(3)?,
                included: r.get(4)?,
            })
        })?
        .collect::<rusqlite::Result<_>>()?;
    let mut stmt = c.prepare(
        "SELECT DISTINCT v.id,d.name,v.version_number,
          NOT EXISTS(SELECT 1 FROM asset_links active
            WHERE active.asset_id=v.asset_id AND active.assay_id=?1 AND active.deleted_at IS NULL)
         FROM asset_versions v JOIN data_assets d ON d.id=v.asset_id
         JOIN asset_links l ON l.asset_id=v.asset_id
         WHERE l.assay_id=?1 ORDER BY d.name,v.version_number,v.id",
    )?;
    let available_versions: Vec<VersionChoice> = stmt
        .query_map([assay], |r| {
            Ok(VersionChoice {
                id: r.get(0)?,
                asset_name: r.get(1)?,
                version_number: r.get(2)?,
                deleted: r.get(3)?,
            })
        })?
        .collect::<rusqlite::Result<_>>()?;
    let mut input_version_ids = Vec::new();
    let mut result_version_ids = Vec::new();
    if let Some(ref id) = selected {
        let mut stmt=c.prepare("SELECT asset_version_id,role FROM analysis_asset_versions WHERE analysis_id=?1 ORDER BY role,asset_version_id")?;
        let rows = stmt.query_map([id], |r| {
            Ok((r.get::<_, String>(0)?, r.get::<_, String>(1)?))
        })?;
        for row in rows {
            let (version, role) = row?;
            if role == "input" {
                input_version_ids.push(version);
            } else {
                result_version_ids.push(version);
            }
        }
    }
    Ok(AnalysisResponse {
        assay_id: assay.to_owned(),
        selected_id: selected,
        analyses,
        samples,
        available_versions,
        input_version_ids,
        result_version_ids,
    })
}
fn execute_at(path: &Path, request: AnalysisRequest) -> Result<AnalysisResponse, TreeError> {
    validate(&request)?;
    with_database_at(path, |c| {
        assay_valid(c, &request.assay_id)?;
        let selected = if matches!(request.action.as_str(), "list" | "get") {
            if let Some(ref id) = request.analysis_id {
                status(c, &request.assay_id, id)?;
            }
            request.analysis_id.clone()
        } else {
            let tx = c.transaction_with_behavior(TransactionBehavior::Immediate)?;
            let id = mutate(&tx, &request)?;
            tx.commit()?;
            Some(id)
        };
        snapshot(c, &request.assay_id, selected)
    })
}

#[cfg(test)]
mod tests;
