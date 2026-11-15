//! Assay-scoped group and biological replicate management in the controlled test workspace.
use crate::project_tree::{database_path, required, required_name, with_database_at, TreeError};
use rusqlite::{params, Connection, OptionalExtension, TransactionBehavior};
use serde::{Deserialize, Serialize};
use std::path::Path;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ExperimentRequest {
    action: String,
    assay_id: String,
    kind: Option<String>,
    id: Option<String>,
    parent_id: Option<String>,
    name: Option<String>,
    request_id: Option<String>,
}

#[derive(Debug, Serialize, PartialEq, Eq)]
pub struct ReplicateNode {
    id: String,
    label: String,
}

#[derive(Debug, Serialize, PartialEq, Eq)]
pub struct GroupNode {
    id: String,
    name: String,
    replicates: Vec<ReplicateNode>,
}

#[derive(Debug, Serialize, PartialEq, Eq)]
pub struct StageNode {
    id: &'static str,
    label: &'static str,
    scope: &'static str,
}

#[derive(Debug, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct ExperimentSnapshot {
    assay_id: String,
    groups: Vec<GroupNode>,
    stages: Vec<StageNode>,
    replicate_count: usize,
}

#[derive(Clone, Copy)]
enum Kind {
    Group,
    Replicate,
}

impl Kind {
    fn parse(value: Option<&str>) -> Result<Self, TreeError> {
        match value {
            Some("group") => Ok(Self::Group),
            Some("replicate") => Ok(Self::Replicate),
            _ => Err(TreeError::input()),
        }
    }
}

#[tauri::command]
pub fn experiment_tree<R: tauri::Runtime>(
    app: tauri::AppHandle<R>,
    request: ExperimentRequest,
) -> Result<ExperimentSnapshot, TreeError> {
    let path = database_path(&app)?;
    execute_at(&path, request)
}

fn validate(request: &ExperimentRequest) -> Result<(), TreeError> {
    required(Some(&request.assay_id), 128)?;
    if request.action == "list" {
        return if request.kind.is_none()
            && request.id.is_none()
            && request.parent_id.is_none()
            && request.name.is_none()
            && request.request_id.is_none()
        {
            Ok(())
        } else {
            Err(TreeError::input())
        };
    }
    let kind = Kind::parse(request.kind.as_deref())?;
    required(request.request_id.as_deref(), 128)?;
    match request.action.as_str() {
        "create" => {
            if request.id.is_some() {
                return Err(TreeError::input());
            }
            required_name(request.name.as_deref())?;
            match kind {
                Kind::Group if request.parent_id.is_some() => return Err(TreeError::input()),
                Kind::Replicate => { required(request.parent_id.as_deref(), 128)?; }
                Kind::Group => {}
            }
        }
        "rename" | "delete" => {
            required(request.id.as_deref(), 128)?;
            // Reparenting is outside this contract; never silently change relationships.
            if request.parent_id.is_some() {
                return Err(TreeError::input());
            }
            if request.action == "rename" {
                required_name(request.name.as_deref())?;
            } else if request.name.is_some() {
                return Err(TreeError::input());
            }
        }
        _ => return Err(TreeError::input()),
    }
    Ok(())
}

fn ensure_assay(connection: &Connection, assay: &str) -> Result<(), TreeError> {
    let exists: bool = connection.query_row(
        "SELECT EXISTS(SELECT 1 FROM assays a JOIN themes t ON t.id=a.theme_id JOIN projects p ON p.id=t.project_id WHERE a.id=?1 AND a.deleted_at IS NULL AND t.deleted_at IS NULL AND p.deleted_at IS NULL)",
        [assay], |row| row.get(0),
    )?;
    if exists {
        Ok(())
    } else {
        Err(TreeError::new("NOT_FOUND", "The selected assay no longer exists."))
    }
}

fn execute_at(path: &Path, request: ExperimentRequest) -> Result<ExperimentSnapshot, TreeError> {
    validate(&request)?;
    with_database_at(path, |connection| {
        if request.action != "list" {
            let tx = connection.transaction_with_behavior(TransactionBehavior::Immediate)?;
            ensure_assay(&tx, &request.assay_id)?;
            let token = required(request.request_id.as_deref(), 128)?;
            // Scope is part of the persisted payload. Reusing an S1 token, or using
            // a token for another assay or operation, cannot replay unrelated work.
            let payload = serde_json::to_string(&serde_json::json!({
                "scope": "experiment_tree", "request": &request
            })).map_err(|_| TreeError::input())?;
            let previous: Option<String> = tx.query_row(
                "SELECT payload FROM s1_requests WHERE request_id=?1", [token], |row| row.get(0),
            ).optional()?;
            match previous {
                Some(previous) if previous != payload => return Err(TreeError::new(
                    "REQUEST_CONFLICT", "This request ID was already used for a different operation.",
                )),
                Some(_) => {}
                None => {
                    mutate(&tx, &request)?;
                    tx.execute("INSERT INTO s1_requests(request_id,payload) VALUES(?1,?2)", params![token,payload])?;
                }
            }
            tx.commit()?;
        }
        // All nested rows and the count come from one consistent database snapshot
        // after the mutation commits, including when another process is writing.
        let read = connection.transaction()?;
        ensure_assay(&read, &request.assay_id)?;
        let snapshot = snapshot(&read, &request.assay_id)?;
        read.commit()?;
        Ok(snapshot)
    })
}

fn group_exists(connection: &Connection, assay: &str, id: &str) -> Result<bool, TreeError> {
    Ok(connection.query_row(
        "SELECT EXISTS(SELECT 1 FROM experiment_groups WHERE id=?1 AND assay_id=?2 AND deleted_at IS NULL)",
        params![id,assay], |row| row.get(0),
    )?)
}

fn mutate(connection: &Connection, request: &ExperimentRequest) -> Result<(), TreeError> {
    let kind = Kind::parse(request.kind.as_deref())?;
    let assay = &request.assay_id;
    if request.action == "create" {
        let name = required_name(request.name.as_deref())?.trim();
        match kind {
            Kind::Group => {
                connection.execute(
                    "INSERT INTO experiment_groups(id,assay_id,name) VALUES(lower(hex(randomblob(16))),?1,?2)",
                    params![assay,name],
                )?;
            }
            Kind::Replicate => {
                let parent = required(request.parent_id.as_deref(), 128)?;
                if !group_exists(connection, assay, parent)? {
                    return Err(TreeError::new("INVALID_PARENT", "Select an existing group in this assay."));
                }
                connection.execute(
                    "INSERT INTO replicates(id,group_id,label) VALUES(lower(hex(randomblob(16))),?1,?2)",
                    params![parent,name],
                )?;
            }
        }
        return Ok(());
    }
    let id = required(request.id.as_deref(), 128)?;
    let exists = match kind {
        Kind::Group => group_exists(connection, assay, id)?,
        Kind::Replicate => connection.query_row(
            "SELECT EXISTS(SELECT 1 FROM replicates r JOIN experiment_groups g ON g.id=r.group_id WHERE r.id=?1 AND g.assay_id=?2 AND r.deleted_at IS NULL AND g.deleted_at IS NULL)",
            params![id,assay], |row| row.get(0),
        )?,
    };
    if !exists {
        return Err(TreeError::new("NOT_FOUND", "The selected item no longer exists in this assay."));
    }
    if request.action == "rename" {
        let sql = match kind {
            Kind::Group => "UPDATE experiment_groups SET name=?1 WHERE id=?2 AND assay_id=?3",
            Kind::Replicate => "UPDATE replicates SET label=?1 WHERE id=?2 AND group_id IN (SELECT id FROM experiment_groups WHERE assay_id=?3)",
        };
        connection.execute(sql, params![required_name(request.name.as_deref())?.trim(),id,assay])?;
        return Ok(());
    }
    let guard = match kind {
        Kind::Group => "SELECT EXISTS(SELECT 1 FROM replicates WHERE group_id=?1 AND deleted_at IS NULL UNION ALL SELECT 1 FROM asset_links WHERE group_id=?1 AND deleted_at IS NULL)",
        Kind::Replicate => "SELECT EXISTS(SELECT 1 FROM asset_links WHERE replicate_id=?1 AND deleted_at IS NULL UNION ALL SELECT 1 FROM analysis_samples WHERE replicate_id=?1 UNION ALL SELECT 1 FROM representative_sources WHERE replicate_id=?1)",
    };
    if connection.query_row(guard, [id], |row| row.get::<_, bool>(0))? {
        return Err(TreeError::new("NODE_IN_USE", "Remove this item's children and associations before deleting it."));
    }
    let sql = match kind {
        Kind::Group => "UPDATE experiment_groups SET deleted_at=strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id=?1 AND assay_id=?2",
        Kind::Replicate => "UPDATE replicates SET deleted_at=strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id=?1 AND group_id IN (SELECT id FROM experiment_groups WHERE assay_id=?2)",
    };
    connection.execute(sql, params![id,assay])?;
    Ok(())
}

fn snapshot(connection: &Connection, assay: &str) -> Result<ExperimentSnapshot, TreeError> {
    let mut statement = connection.prepare(
        "SELECT id,name FROM experiment_groups WHERE assay_id=?1 AND deleted_at IS NULL ORDER BY sort_order,name,id",
    )?;
    let group_rows = statement.query_map([assay], |row| Ok((row.get::<_, String>(0)?,row.get::<_, String>(1)?)))?
        .collect::<rusqlite::Result<Vec<_>>>()?;
    let mut groups = Vec::with_capacity(group_rows.len());
    let mut replicate_count = 0;
    let mut samples = connection.prepare(
        "SELECT id,label FROM replicates WHERE group_id=?1 AND deleted_at IS NULL ORDER BY label,id",
    )?;
    for (id,name) in group_rows {
        let replicates = samples.query_map([&id], |row| Ok(ReplicateNode { id:row.get(0)?,label:row.get(1)? }))?
            .collect::<rusqlite::Result<Vec<_>>>()?;
        replicate_count += replicates.len();
        groups.push(GroupNode { id,name,replicates });
    }
    let stages = vec![
        StageNode { id:"raw",label:"Raw",scope:"replicate" },
        StageNode { id:"processed",label:"Processed",scope:"replicate" },
        StageNode { id:"statistics",label:"Statistics",scope:"assay" },
        StageNode { id:"display",label:"Display",scope:"assay" },
    ];
    Ok(ExperimentSnapshot { assay_id:assay.to_string(),groups,stages,replicate_count })
}

#[cfg(test)]
#[path = "experiment_tree_tests.rs"]
mod tests;
