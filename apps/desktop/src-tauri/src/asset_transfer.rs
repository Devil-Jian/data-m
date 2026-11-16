//! S5: atomic, idempotent logical transfers of existing asset links.
//! No CAS path is accepted; the service never writes Blob bytes or Replicates.
use crate::project_tree::{database_path, with_database_at, TreeError};
use rusqlite::{params, Connection, OptionalExtension, TransactionBehavior};
use serde::{Deserialize, Serialize};
use std::{collections::HashSet, path::Path};

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AssetScope {
    assay_id: String,
    stage: String,
    group_id: Option<String>,
    replicate_id: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct TransferSource {
    asset_link_id: String,
    asset_id: String,
    asset_version_id: String,
    assay_id: String,
    stage: String,
    group_id: Option<String>,
    replicate_id: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct TransferRequest {
    action: String,
    request_id: String,
    items: Vec<TransferSource>,
    target: AssetScope,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct TransferLink {
    source_link_id: String,
    result_link_id: String,
    asset_id: String,
    asset_version_id: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct TransferResult {
    request_id: String,
    action: String,
    target: AssetScope,
    links: Vec<TransferLink>,
}

struct CurrentLink {
    name: String,
    asset_id: String,
    version_id: String,
    scope: AssetScope,
}

fn failure(code: &'static str) -> TreeError {
    let message = match code {
        "INVALID_REQUEST" => "Choose 1 to 100 distinct, valid asset associations.",
        "INVALID_TARGET" => "Choose an active assay or an active group and sample in that assay.",
        "NOT_FOUND" => "The selected asset association no longer exists.",
        "STALE_SELECTION" => "The selected association or its version changed. Refresh and reselect.",
        "NAME_CONFLICT" => "The destination already contains one of these names.",
        "REQUEST_CONFLICT" => "This request ID belongs to a different operation.",
        _ => "The asset transfer could not be completed.",
    };
    TreeError::new(code, message)
}

fn valid_id(value: &str) -> bool {
    !value.is_empty()
        && value.len() <= 128
        && value
            .bytes()
            .all(|byte| byte.is_ascii_alphanumeric() || byte == b'-' || byte == b'_')
}

fn valid_scope(scope: &AssetScope) -> bool {
    valid_id(&scope.assay_id)
        && matches!(
            scope.stage.as_str(),
            "raw" | "processed" | "statistics" | "display"
        )
        && scope.group_id.as_deref().map_or(true, valid_id)
        && scope.replicate_id.as_deref().map_or(true, valid_id)
        && match scope.stage.as_str() {
            "raw" | "processed" => scope.group_id.is_some() && scope.replicate_id.is_some(),
            _ => scope.group_id.is_none() && scope.replicate_id.is_none(),
        }
}

fn validate(request: &TransferRequest) -> Result<(), TreeError> {
    if !matches!(request.action.as_str(), "copy" | "move")
        || !valid_id(&request.request_id)
        || !valid_scope(&request.target)
        || request.items.is_empty()
        || request.items.len() > 100
    {
        return Err(failure("INVALID_REQUEST"));
    }
    let mut seen = HashSet::new();
    for source in &request.items {
        let scope = AssetScope {
            assay_id: source.assay_id.clone(),
            stage: source.stage.clone(),
            group_id: source.group_id.clone(),
            replicate_id: source.replicate_id.clone(),
        };
        if !valid_id(&source.asset_link_id)
            || !valid_id(&source.asset_id)
            || !valid_id(&source.asset_version_id)
            || !valid_scope(&scope)
            || !seen.insert(source.asset_link_id.as_str())
        {
            return Err(failure("INVALID_REQUEST"));
        }
    }
    Ok(())
}

fn ensure_target(connection: &Connection, target: &AssetScope) -> Result<(), TreeError> {
    let assay_exists: bool = connection.query_row(
        "SELECT EXISTS(
             SELECT 1 FROM assays a JOIN themes t ON t.id=a.theme_id
             JOIN projects p ON p.id=t.project_id
             WHERE a.id=?1 AND a.deleted_at IS NULL AND t.deleted_at IS NULL
             AND p.deleted_at IS NULL)",
        [&target.assay_id],
        |row| row.get(0),
    )?;
    if !assay_exists {
        return Err(failure("INVALID_TARGET"));
    }
    if matches!(target.stage.as_str(), "raw" | "processed") {
        let valid: bool = connection.query_row(
            "SELECT EXISTS(
                 SELECT 1 FROM experiment_groups g
                 JOIN replicates r ON r.group_id=g.id
                 WHERE g.id=?1 AND r.id=?2 AND g.assay_id=?3
                 AND g.deleted_at IS NULL AND r.deleted_at IS NULL)",
            params![target.group_id, target.replicate_id, target.assay_id],
            |row| row.get(0),
        )?;
        if !valid {
            return Err(failure("INVALID_TARGET"));
        }
    }
    Ok(())
}

fn current(connection: &Connection, item: &TransferSource) -> Result<CurrentLink, TreeError> {
    let current = connection
        .query_row(
            "SELECT l.display_name,l.asset_id,v.id,l.assay_id,l.stage,l.group_id,l.replicate_id
             FROM asset_links l
             JOIN data_assets a ON a.id=l.asset_id
             JOIN asset_versions v ON v.asset_id=a.id
               AND v.version_number=(SELECT MAX(v2.version_number) FROM asset_versions v2 WHERE v2.asset_id=a.id)
             WHERE l.id=?1 AND l.deleted_at IS NULL AND a.deleted_at IS NULL",
            [&item.asset_link_id],
            |row| {
                Ok(CurrentLink {
                    name: row.get(0)?,
                    asset_id: row.get(1)?,
                    version_id: row.get(2)?,
                    scope: AssetScope {
                        assay_id: row.get(3)?,
                        stage: row.get(4)?,
                        group_id: row.get(5)?,
                        replicate_id: row.get(6)?,
                    },
                })
            },
        )
        .optional()?
        .ok_or_else(|| failure("NOT_FOUND"))?;
    let supplied_scope = AssetScope {
        assay_id: item.assay_id.clone(),
        stage: item.stage.clone(),
        group_id: item.group_id.clone(),
        replicate_id: item.replicate_id.clone(),
    };
    if current.asset_id != item.asset_id
        || current.version_id != item.asset_version_id
        || current.scope != supplied_scope
    {
        return Err(failure("STALE_SELECTION"));
    }
    // A soft-deleted parent or a legacy malformed scope cannot be moved.
    ensure_target(connection, &current.scope).map_err(|_| failure("STALE_SELECTION"))?;
    Ok(current)
}

fn destination_conflict(
    connection: &Connection,
    target: &AssetScope,
    name: &str,
) -> Result<bool, TreeError> {
    connection
        .query_row(
            "SELECT EXISTS(
                 SELECT 1 FROM asset_links
                 WHERE deleted_at IS NULL AND assay_id=?1 AND stage=?2
                   AND COALESCE(group_id,'')=COALESCE(?3,'')
                   AND COALESCE(replicate_id,'')=COALESCE(?4,'')
                   AND display_name=?5 COLLATE NOCASE)",
            params![
                target.assay_id,
                target.stage,
                target.group_id,
                target.replicate_id,
                name
            ],
            |row| row.get(0),
        )
        .map_err(Into::into)
}

fn execute_at(path: &Path, request: TransferRequest) -> Result<TransferResult, TreeError> {
    validate(&request)?;
    with_database_at(path, |connection| {
        let tx = connection.transaction_with_behavior(TransactionBehavior::Immediate)?;
        let payload = serde_json::to_string(&request).map_err(|_| failure("INVALID_REQUEST"))?;
        let previous: Option<String> = tx
            .query_row(
                "SELECT payload FROM s1_requests WHERE request_id=?1",
                [&request.request_id],
                |row| row.get(0),
            )
            .optional()?;
        let audit_id = format!("s5:{}", request.request_id);
        if let Some(previous) = previous {
            if previous != payload {
                return Err(failure("REQUEST_CONFLICT"));
            }
            let saved: String = tx
                .query_row(
                    "SELECT details_json FROM audit_events
                     WHERE id=?1 AND object_type='asset_link_batch'",
                    [&audit_id],
                    |row| row.get(0),
                )
                .optional()?
                .ok_or_else(|| failure("REQUEST_CONFLICT"))?;
            return serde_json::from_str(&saved).map_err(|_| failure("REQUEST_CONFLICT"));
        }

        ensure_target(&tx, &request.target)?;
        let mut sources = Vec::with_capacity(request.items.len());
        let mut names = HashSet::new();
        for item in &request.items {
            let source = current(&tx, item)?;
            if source.scope == request.target {
                return Err(if request.action == "copy" {
                    failure("NAME_CONFLICT")
                } else {
                    failure("INVALID_TARGET")
                });
            }
            // SQLite's NOCASE index is ASCII-only; apply the same preflight rule.
            if !names.insert(source.name.to_ascii_lowercase())
                || destination_conflict(&tx, &request.target, &source.name)?
            {
                return Err(failure("NAME_CONFLICT"));
            }
            sources.push(source);
        }

        let mut links = Vec::with_capacity(sources.len());
        for (item, source) in request.items.iter().zip(&sources) {
            let link_id = if request.action == "copy" {
                let id: String = tx.query_row(
                    "SELECT lower(hex(randomblob(16)))",
                    [],
                    |row| row.get(0),
                )?;
                tx.execute(
                    "INSERT INTO asset_links
                     (id,asset_id,assay_id,group_id,replicate_id,stage,display_name)
                     VALUES(?1,?2,?3,?4,?5,?6,?7)",
                    params![
                        id,
                        source.asset_id,
                        request.target.assay_id,
                        request.target.group_id,
                        request.target.replicate_id,
                        request.target.stage,
                        source.name
                    ],
                )?;
                id
            } else {
                tx.execute(
                    "UPDATE asset_links SET assay_id=?1,group_id=?2,replicate_id=?3,stage=?4
                     WHERE id=?5 AND deleted_at IS NULL",
                    params![
                        request.target.assay_id,
                        request.target.group_id,
                        request.target.replicate_id,
                        request.target.stage,
                        item.asset_link_id
                    ],
                )?;
                item.asset_link_id.clone()
            };
            links.push(TransferLink {
                source_link_id: item.asset_link_id.clone(),
                result_link_id: link_id,
                asset_id: source.asset_id.clone(),
                asset_version_id: source.version_id.clone(),
            });
        }
        let response = TransferResult {
            request_id: request.request_id.clone(),
            action: request.action.clone(),
            target: request.target.clone(),
            links,
        };
        let details = serde_json::to_string(&response).map_err(|_| failure("INVALID_REQUEST"))?;
        tx.execute(
            "INSERT INTO audit_events(id,action,object_type,object_id,details_json)
             VALUES(?1,?2,'asset_link_batch',?3,?4)",
            params![
                audit_id,
                format!("asset_link_{}", request.action),
                request.target.assay_id,
                details
            ],
        )?;
        tx.execute(
            "INSERT INTO s1_requests(request_id,payload) VALUES(?1,?2)",
            params![request.request_id, payload],
        )?;
        tx.commit()?;
        Ok(response)
    })
}

#[tauri::command]
pub fn asset_transfer<R: tauri::Runtime>(
    app: tauri::AppHandle<R>,
    request: TransferRequest,
) -> Result<TransferResult, TreeError> {
    execute_at(&database_path(&app)?, request)
}

#[cfg(test)]
#[path = "asset_transfer_tests.rs"]
mod tests;
