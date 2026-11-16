//! S4 read-only, context-qualified asset browsing. No paths or mutating commands.
use crate::project_tree::{database_path, with_database_at, TreeError};
use rusqlite::{params, Connection};
use serde::{Deserialize, Serialize};
use std::path::Path;

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AssetQuery {
    assay_id: String,
    stage: Option<String>,
    group_id: Option<String>,
    replicate_id: Option<String>,
    search: Option<String>,
    sort: Option<String>,
    cursor: Option<String>,
    limit: Option<u32>,
}
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AssetRow {
    asset_id: String,
    asset_link_id: String,
    asset_version_id: String,
    version_number: i64,
    name: String,
    stage: String,
    group_id: Option<String>,
    replicate_id: Option<String>,
    sha256: String,
    size_bytes: i64,
    referenced: bool,
}
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AssetPage {
    items: Vec<AssetRow>,
    total: i64,
    next_cursor: Option<String>,
}
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AssetDetailQuery {
    asset_link_id: String,
}
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AssetDetail {
    item: AssetRow,
}
fn input() -> TreeError {
    TreeError::input()
}
fn valid_id(id: &str) -> bool {
    !id.is_empty()
        && id.len() <= 128
        && id
            .bytes()
            .all(|b| b.is_ascii_alphanumeric() || b == b'_' || b == b'-')
}
fn clause(query: &AssetQuery) -> Result<(&'static str, &'static str, usize), TreeError> {
    if !valid_id(&query.assay_id)
        || query.group_id.as_deref().is_some_and(|s| !valid_id(s))
        || query.replicate_id.as_deref().is_some_and(|s| !valid_id(s))
        || query
            .stage
            .as_deref()
            .is_some_and(|s| !matches!(s, "raw" | "processed" | "statistics" | "display"))
        || query
            .search
            .as_ref()
            .is_some_and(|s| s.len() > 256 || s.chars().any(char::is_control))
    {
        return Err(input());
    }
    if query.replicate_id.is_some() && query.group_id.is_none() {
        return Err(input());
    }
    let sort = match query.sort.as_deref().unwrap_or("name") {
        "name" => "l.display_name COLLATE NOCASE ASC, l.id ASC",
        "newest" => "v.created_at DESC, l.id ASC",
        "size" => "b.size_bytes DESC, l.id ASC",
        _ => return Err(input()),
    };
    let limit = query.limit.unwrap_or(50) as usize;
    if limit == 0 || limit > 100 {
        return Err(input());
    }
    let scope = "l.assay_id=?1 AND l.deleted_at IS NULL AND a.deleted_at IS NULL AND (?2 IS NULL OR l.stage=?2) AND (?3 IS NULL OR l.group_id=?3) AND (?4 IS NULL OR l.replicate_id=?4) AND (?5 IS NULL OR instr(lower(l.display_name),lower(?5))>0)";
    Ok((scope, sort, limit))
}
fn decode_cursor(cursor: Option<&str>) -> Result<usize, TreeError> {
    match cursor {
        None => Ok(0),
        Some(s)
            if !s.is_empty()
                && s.len() <= 12
                && s.bytes().all(|b| b.is_ascii_digit())
                && !s.starts_with('0') =>
        {
            s.parse::<usize>().map_err(|_| input())
        }
        _ => Err(input()),
    }
}
fn sql_rows(
    connection: &Connection,
    predicate: &str,
    sort: &str,
    query: &AssetQuery,
    offset: usize,
    limit: usize,
) -> Result<Vec<AssetRow>, TreeError> {
    let sql=format!("SELECT a.id,l.id,v.id,v.version_number,l.display_name,l.stage,l.group_id,l.replicate_id,b.sha256,b.size_bytes,EXISTS(SELECT 1 FROM panel_asset_versions p WHERE p.asset_version_id=v.id) FROM asset_links l JOIN data_assets a ON a.id=l.asset_id JOIN asset_versions v ON v.asset_id=a.id AND v.version_number=(SELECT MAX(v2.version_number) FROM asset_versions v2 WHERE v2.asset_id=a.id) JOIN blobs b ON b.sha256=v.blob_sha256 WHERE {predicate} ORDER BY {sort} LIMIT ?6 OFFSET ?7");
    let mut stmt = connection.prepare(&sql)?;
    let rows = stmt
        .query_map(
            params![
                query.assay_id,
                query.stage,
                query.group_id,
                query.replicate_id,
                query.search,
                limit as i64,
                offset as i64
            ],
            |r| {
                Ok(AssetRow {
                    asset_id: r.get(0)?,
                    asset_link_id: r.get(1)?,
                    asset_version_id: r.get(2)?,
                    version_number: r.get(3)?,
                    name: r.get(4)?,
                    stage: r.get(5)?,
                    group_id: r.get(6)?,
                    replicate_id: r.get(7)?,
                    sha256: r.get(8)?,
                    size_bytes: r.get(9)?,
                    referenced: r.get::<_, i64>(10)? != 0,
                })
            },
        )?
        .collect::<rusqlite::Result<Vec<_>>>()?;
    Ok(rows)
}
fn list_at(path: &Path, request: AssetQuery) -> Result<AssetPage, TreeError> {
    let (predicate, sort, limit) = clause(&request)?;
    let offset = decode_cursor(request.cursor.as_deref())?;
    if offset > 1_000_000 {
        return Err(input());
    }
    with_database_at(path, |connection| {
        let exists: i64 = connection.query_row(
            "SELECT EXISTS(SELECT 1 FROM assays WHERE id=?1 AND deleted_at IS NULL)",
            [&request.assay_id],
            |r| r.get(0),
        )?;
        if exists == 0 {
            return Err(TreeError::new("NOT_FOUND", "Assay does not exist."));
        }
        let total:i64=connection.query_row(&format!("SELECT count(*) FROM asset_links l JOIN data_assets a ON a.id=l.asset_id WHERE {predicate}"),params![request.assay_id,request.stage,request.group_id,request.replicate_id,request.search],|r|r.get(0))?;
        let items = sql_rows(connection, predicate, sort, &request, offset, limit)?;
        let next = offset.checked_add(items.len()).ok_or_else(input)?;
        Ok(AssetPage {
            items,
            next_cursor: if (next as i64) < total {
                Some(next.to_string())
            } else {
                None
            },
            total,
        })
    })
}
fn detail_at(path: &Path, request: AssetDetailQuery) -> Result<AssetDetail, TreeError> {
    if !valid_id(&request.asset_link_id) {
        return Err(input());
    }
    with_database_at(path, |connection| {
        let assay: Option<String> = connection
            .query_row(
                "SELECT assay_id FROM asset_links WHERE id=?1 AND deleted_at IS NULL",
                [&request.asset_link_id],
                |r| r.get(0),
            )
            .optional()?;
        let assay =
            assay.ok_or_else(|| TreeError::new("NOT_FOUND", "Asset link does not exist."))?;
        let query = AssetQuery {
            assay_id: assay,
            stage: None,
            group_id: None,
            replicate_id: None,
            search: None,
            sort: None,
            cursor: None,
            limit: Some(100),
        };
        let predicate = "l.id=?1 AND l.deleted_at IS NULL AND a.deleted_at IS NULL";
        let sql="SELECT a.id,l.id,v.id,v.version_number,l.display_name,l.stage,l.group_id,l.replicate_id,b.sha256,b.size_bytes,EXISTS(SELECT 1 FROM panel_asset_versions p WHERE p.asset_version_id=v.id) FROM asset_links l JOIN data_assets a ON a.id=l.asset_id JOIN asset_versions v ON v.asset_id=a.id AND v.version_number=(SELECT MAX(v2.version_number) FROM asset_versions v2 WHERE v2.asset_id=a.id) JOIN blobs b ON b.sha256=v.blob_sha256 WHERE l.id=?1 AND l.deleted_at IS NULL AND a.deleted_at IS NULL";
        let _ = (query, predicate);
        let item = connection
            .query_row(sql, [request.asset_link_id], |r| {
                Ok(AssetRow {
                    asset_id: r.get(0)?,
                    asset_link_id: r.get(1)?,
                    asset_version_id: r.get(2)?,
                    version_number: r.get(3)?,
                    name: r.get(4)?,
                    stage: r.get(5)?,
                    group_id: r.get(6)?,
                    replicate_id: r.get(7)?,
                    sha256: r.get(8)?,
                    size_bytes: r.get(9)?,
                    referenced: r.get::<_, i64>(10)? != 0,
                })
            })
            .map_err(|_| TreeError::new("NOT_FOUND", "Asset link does not exist."))?;
        Ok(AssetDetail { item })
    })
}
use rusqlite::OptionalExtension;
#[tauri::command]
pub fn list_assets<R: tauri::Runtime>(
    app: tauri::AppHandle<R>,
    request: AssetQuery,
) -> Result<AssetPage, TreeError> {
    list_at(&database_path(&app)?, request)
}
#[tauri::command]
pub fn get_asset<R: tauri::Runtime>(
    app: tauri::AppHandle<R>,
    request: AssetDetailQuery,
) -> Result<AssetDetail, TreeError> {
    detail_at(&database_path(&app)?, request)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::project_tree::with_database_at;
    fn setup() -> (tempfile::TempDir, std::path::PathBuf) {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("assets.sqlite");
        with_database_at(&path,|c|{
            c.execute("INSERT INTO projects(id,name) VALUES('p','p')",[])?;
            c.execute("INSERT INTO themes(id,project_id,name) VALUES('t','p','t')",[])?;
            c.execute("INSERT INTO assays(id,theme_id,name) VALUES('a','t','a')",[])?;
            c.execute("INSERT INTO experiment_groups(id,assay_id,name) VALUES('g','a','g')",[])?;
            c.execute("INSERT INTO replicates(id,group_id,label) VALUES('r','g','r')",[])?;
            for n in 0..100 {
                let id=format!("asset{n:03}");let link=format!("link{n:03}");let version=format!("version{n:03}");
                let hash=format!("{n:064x}");
                c.execute("INSERT INTO blobs(sha256,size_bytes) VALUES(?1,?2)",params![hash,n])?;
                c.execute("INSERT INTO data_assets(id,name) VALUES(?1,?2)",params![id,id])?;
                c.execute("INSERT INTO asset_versions(id,asset_id,version_number,blob_sha256,source_name) VALUES(?1,?2,1,?3,?4)",params![version,id,hash,id])?;
                c.execute("INSERT INTO asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name) VALUES(?1,?2,'a','g','r','raw',?3)",params![link,id,format!("file{n:03}.png")])?;
            }
            Ok(())
        }).unwrap();
        (dir, path)
    }
    fn query() -> AssetQuery {
        AssetQuery {
            assay_id: "a".into(),
            stage: Some("raw".into()),
            group_id: Some("g".into()),
            replicate_id: Some("r".into()),
            search: None,
            sort: Some("name".into()),
            cursor: None,
            limit: Some(17),
        }
    }
    #[test]
    fn s4_100_files_stable_pagination_and_detail() {
        let (_dir, path) = setup();
        let mut q = query();
        let mut found = vec![];
        loop {
            let page = list_at(&path, q).unwrap();
            assert_eq!(page.total, 100);
            found.extend(page.items.into_iter().map(|a| a.asset_link_id));
            if let Some(next) = page.next_cursor {
                q.cursor = Some(next)
            } else {
                break;
            }
        }
        assert_eq!(found.len(), 100);
        let unique: std::collections::HashSet<_> = found.iter().collect();
        assert_eq!(unique.len(), 100);
        let detail = detail_at(
            &path,
            AssetDetailQuery {
                asset_link_id: found[0].clone(),
            },
        )
        .unwrap();
        assert_eq!(detail.item.version_number, 1);
    }
    #[test]
    fn s4_filters_bad_cursor_and_context_identity() {
        let (_dir, path) = setup();
        let mut q = query();
        q.search = Some("file009".into());
        assert_eq!(list_at(&path, q).unwrap().total, 1);
        let mut q = query();
        q.cursor = Some("0".into());
        assert!(list_at(&path, q).is_err());
        let mut q = query();
        q.sort = Some("injection".into());
        assert!(list_at(&path, q).is_err());
        with_database_at(&path,|c|{c.execute("INSERT INTO asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name) VALUES('other','asset000','a','g','r','processed','same.png')",[])?;Ok(())}).unwrap();
        let mut q = query();
        q.stage = None;
        assert_eq!(list_at(&path, q).unwrap().total, 101);
        assert_eq!(
            detail_at(
                &path,
                AssetDetailQuery {
                    asset_link_id: "other".into()
                }
            )
            .unwrap()
            .item
            .asset_id,
            "asset000"
        );
    }
}
