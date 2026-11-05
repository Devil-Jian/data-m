use std::{
    collections::{BTreeMap, BTreeSet},
    fs,
    path::Path,
};

use chrono::{SecondsFormat, Utc};
use rusqlite::{OptionalExtension, params};
use serde::{Deserialize, Serialize};

use super::papers::Paper;
use super::{ArchiveError, ArchiveStore};

const MAX_FOLDER_FILES: usize = 20_000;
const MAX_DEPTH: usize = 12;

#[derive(Clone, Debug, Serialize, Deserialize, PartialEq, Eq)]
pub struct ScanEntry {
    pub relative_path: String,
    pub kind: String,
    pub role: String,
    pub figure_number: Option<u32>,
    pub panel_label: Option<String>,
    pub sequence: Option<u32>,
    pub issue: Option<String>,
    pub file_size: u64,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct FolderScan {
    pub folder: String,
    pub paper_title: String,
    pub manuscript: Option<String>,
    pub entries: Vec<ScanEntry>,
    pub warnings: Vec<String>,
    pub total_bytes: u64,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct PlannedFile {
    pub source_relative_path: String,
    pub logical_relative_path: String,
    pub role: String,
    pub figure_number: Option<u32>,
    pub panel_label: Option<String>,
    pub sequence: Option<u32>,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct ImportResult {
    pub paper: Paper,
    pub imported: usize,
    pub failed: Vec<String>,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct ArchiveIssue {
    pub severity: String,
    pub message: String,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct ArchiveCheck {
    pub paper_id: String,
    pub can_archive: bool,
    pub issues: Vec<ArchiveIssue>,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct OtherResource {
    pub data_id: String,
    pub original_filename: String,
    pub source_relative_path: String,
    pub sha256: String,
    pub file_size: u64,
}

fn valid_logical_path(value: &str) -> bool {
    !value.is_empty()
        && !value.contains('\\')
        && !value.contains(':')
        && !value.contains('|')
        && value.split('/').all(|piece| {
            !piece.is_empty() && piece != "." && piece != ".." && !piece.ends_with(' ')
        })
}

fn invalid(message: &str) -> ArchiveError {
    ArchiveError::InvalidInput(message.to_owned())
}

fn has_ext(filename: &str, list: &[&str]) -> bool {
    let ext = Path::new(filename)
        .extension()
        .and_then(|s| s.to_str())
        .unwrap_or_default();
    list.iter().any(|item| ext.eq_ignore_ascii_case(item))
}

fn parse_number_prefix(text: &str) -> Option<(u32, &str)> {
    let n = text.bytes().take_while(|b| b.is_ascii_digit()).count();
    if n == 0 {
        return None;
    }
    let number = text[..n].parse::<u32>().ok()?;
    if number == 0 {
        return None;
    }
    Some((number, &text[n..]))
}

fn parse_figure_name(stem: &str) -> Option<u32> {
    let lowered = stem.to_ascii_lowercase();
    let remainder = lowered
        .strip_prefix("figure")
        .or_else(|| lowered.strip_prefix("fig"))?
        .trim_start_matches(['-', '_', ' ']);
    let (number, rest) = parse_number_prefix(remainder)?;
    if rest.is_empty() || rest.starts_with(['-', '_', ' ', '.']) {
        Some(number)
    } else {
        None
    }
}

fn parse_panel_name(stem: &str) -> Option<(u32, String, Option<u32>)> {
    let (figure, remain) = parse_number_prefix(stem)?;
    let after = remain
        .strip_prefix('-')
        .or_else(|| remain.strip_prefix('_'))?;
    let first = after.chars().next()?;
    if !first.is_ascii_alphabetic() {
        return None;
    }
    let panel = first.to_ascii_uppercase().to_string();
    let tail = &after[first.len_utf8()..];
    // 1-A-1-..., 1-A1..., 2-a9.png (legacy).
    let indexed = tail
        .strip_prefix('-')
        .or_else(|| tail.strip_prefix('_'))
        .unwrap_or(tail);
    let sequence = parse_number_prefix(indexed).map(|(number, _)| number);
    Some((figure, panel, sequence))
}

fn parse_table_name(stem: &str) -> Option<u32> {
    let lowered = stem.to_ascii_lowercase();
    let stripped = lowered
        .strip_prefix("table")
        .or_else(|| lowered.strip_prefix('t'))?;
    let (number, tail) = parse_number_prefix(stripped.trim_start_matches(['-', '_', ' ']))?;
    if tail.is_empty() || tail.starts_with(['-', '_', ' ']) {
        Some(number)
    } else {
        None
    }
}

fn classify(relative: &str, size: u64) -> ScanEntry {
    let path = Path::new(relative);
    let name = path
        .file_name()
        .and_then(|f| f.to_str())
        .unwrap_or_default();
    let stem = path
        .file_stem()
        .and_then(|f| f.to_str())
        .unwrap_or_default();
    let parts: Vec<String> = path
        .components()
        .map(|c| c.as_os_str().to_string_lossy().to_ascii_lowercase())
        .collect();
    let top = parts.first().map(String::as_str).unwrap_or("");
    let mut item = ScanEntry {
        relative_path: relative.replace('\\', "/"),
        kind: "other".to_owned(),
        role: "other".to_owned(),
        figure_number: None,
        panel_label: None,
        sequence: None,
        issue: None,
        file_size: size,
    };

    if parts.len() == 1 {
        if has_ext(name, &["pdf", "docx"]) {
            item.kind = "manuscript".to_owned();
            item.role = "manuscript".to_owned();
        } else {
            item.issue = Some("根目录仅允许论文正文；其他文件将归入 Others".to_owned());
        }
        return item;
    }

    if top == "figure" || top == "figures" {
        if !has_ext(
            name,
            &[
                "png", "jpg", "jpeg", "tif", "tiff", "bmp", "webp", "czi", "nd2", "svs", "pdf",
            ],
        ) {
            item.issue = Some("Figure 目录中的文件不是常见图像格式，请检查归属".to_owned());
        }
        let second = parts.get(1).map(String::as_str).unwrap_or("");
        if matches!(second, "组合图" | "composite" | "combined" | "组图") {
            item.kind = "figure".to_owned();
            item.role = "composite".to_owned();
            item.figure_number = parse_figure_name(stem);
            if item.figure_number.is_none() {
                item.issue = Some("组合图缺少可识别的 Figure 编号".to_owned());
            }
        } else {
            item.kind = "panel".to_owned();
            item.role = if matches!(second, "子图" | "展示子图" | "display" | "panels") {
                "display"
            } else if matches!(second, "统计原始图" | "统计图" | "stat_images" | "stat") {
                "stat_image"
            } else {
                item.issue = Some("无法确定图片用于展示还是统计，暂按未区分原始图处理".to_owned());
                "unspecified"
            }
            .to_owned();
            if let Some((number, label, sequence)) = parse_panel_name(stem) {
                item.figure_number = Some(number);
                item.panel_label = Some(label);
                item.sequence = sequence;
            } else {
                item.issue =
                    Some("图片名称不包含 Figure-Panel 编号，需在处理工作台确认".to_owned());
            }
        }
    } else if top == "table" || top == "tables" {
        item.role = "statistics".to_owned();
        if let Some((figure, panel, sequence)) = parse_panel_name(stem) {
            item.kind = "panel".to_owned();
            item.figure_number = Some(figure);
            item.panel_label = Some(panel);
            item.sequence = sequence;
        } else if let Some(table_number) = parse_table_name(stem) {
            item.kind = "table".to_owned();
            item.figure_number = Some(table_number);
            if stem.to_ascii_lowercase().starts_with("table") {
                item.issue =
                    Some("旧式 tableN 编号可能指独立 Table 或 Figure 统计数据，请确认".to_owned());
            }
        } else {
            item.issue = Some("统计表文件名未标明 Figure-Panel 或独立 Table 编号".to_owned());
        }
    } else if top == "others" {
        item.kind = "other".to_owned();
    } else {
        item.issue = Some("不属于 Figure、Table、Others，文件暂归 Others".to_owned());
    }
    item
}

pub fn scan_paper_folder(root: impl AsRef<Path>) -> Result<FolderScan, ArchiveError> {
    let root = fs::canonicalize(root)?;
    if !root.is_dir() {
        return Err(invalid("请选择包含论文正文和 Figure/Table/Others 的文件夹"));
    }
    let paper_title = root
        .file_name()
        .and_then(|n| n.to_str())
        .unwrap_or("未命名论文")
        .to_owned();
    let mut paths = vec![(root.clone(), 0usize)];
    let mut entries = Vec::new();
    let mut total_bytes = 0u64;

    while let Some((directory, depth)) = paths.pop() {
        if depth > MAX_DEPTH {
            return Err(invalid("文件夹层级过深，超过安全扫描限制"));
        }
        for child in fs::read_dir(&directory)? {
            let child = child?;
            let meta = fs::symlink_metadata(child.path())?;
            if meta.file_type().is_symlink() {
                return Err(invalid(
                    "论文文件夹不能包含符号链接或目录联接，请先整理源文件",
                ));
            }
            if meta.is_dir() {
                paths.push((child.path(), depth + 1));
            } else if meta.is_file() {
                if entries.len() >= MAX_FOLDER_FILES {
                    return Err(invalid("论文文件过多，超过单次 20000 文件的安全限制"));
                }
                let rel = child
                    .path()
                    .strip_prefix(&root)
                    .map_err(|_| invalid("无法计算论文文件相对路径"))?
                    .to_string_lossy()
                    .replace('\\', "/");
                total_bytes = total_bytes
                    .checked_add(meta.len())
                    .ok_or_else(|| invalid("论文文件大小溢出"))?;
                entries.push(classify(&rel, meta.len()));
            }
        }
    }
    entries.sort_by(|a, b| {
        a.relative_path
            .to_lowercase()
            .cmp(&b.relative_path.to_lowercase())
    });
    let manuscripts: Vec<&ScanEntry> = entries
        .iter()
        .filter(|item| item.kind == "manuscript")
        .collect();
    let manuscript = manuscripts.first().map(|item| item.relative_path.clone());
    let mut warnings = Vec::new();
    if manuscripts.is_empty() {
        warnings.push("未找到根目录 PDF 或 DOCX 论文正文".to_owned());
    }
    if manuscripts.len() > 1 {
        warnings.push("检测到多个正文文件，默认选择按名称排序的第一个；请核实".to_owned());
    }
    let mut seen = BTreeSet::new();
    let mut seen_composites = BTreeSet::new();
    for entry in &entries {
        if entry.role == "composite" {
            if let Some(number) = entry.figure_number {
                if !seen_composites.insert(number) {
                    warnings.push(format!(
                        "{}：Figure {number} 存在多个最终组合图，请确认",
                        entry.relative_path
                    ));
                }
            }
        }
        if let Some(issue) = &entry.issue {
            warnings.push(format!("{}：{}", entry.relative_path, issue));
        }
        if let (Some(fig), Some(panel), Some(seq)) = (
            entry.figure_number,
            entry.panel_label.as_ref(),
            entry.sequence,
        ) {
            if entry.role == "display" || entry.role == "stat_image" {
                let identity = format!("{}-{}-{}-{}", fig, panel, seq, entry.role);
                if !seen.insert(identity) {
                    warnings.push(format!(
                        "{}：组图/Panel/内部序号重复，请确认文件是否属于同一组",
                        entry.relative_path
                    ));
                }
            }
        }
    }
    Ok(FolderScan {
        folder: root.to_string_lossy().into_owned(),
        paper_title,
        manuscript,
        entries,
        warnings,
        total_bytes,
    })
}

impl ArchiveStore {
    pub fn scan_folder(&self, root: &Path) -> Result<FolderScan, ArchiveError> {
        let canonical = fs::canonicalize(root)?;
        let storage = fs::canonicalize(&self.root)?;
        if canonical.starts_with(&storage) || storage.starts_with(&canonical) {
            return Err(invalid("导入源文件夹不能包含当前数据仓库，避免递归导入"));
        }
        scan_paper_folder(canonical)
    }

    pub fn ensure_paper_editable(&self, paper_id: &str) -> Result<(), ArchiveError> {
        let paper = self.get_paper(paper_id)?;
        if paper.archive_status == "archived" {
            return Err(invalid("论文已归档，需先进入修订模式"));
        }
        Ok(())
    }

    pub fn read_source_preview(
        &self,
        root: &Path,
        relative: &str,
    ) -> Result<Vec<u8>, ArchiveError> {
        let canonical_root = fs::canonicalize(root)?;
        let storage = fs::canonicalize(&self.root)?;
        if canonical_root.starts_with(&storage) || storage.starts_with(&canonical_root) {
            return Err(invalid("不能从当前数据仓库读取未归档的图片预览"));
        }
        if !valid_logical_path(relative) {
            return Err(invalid("预览路径不合法"));
        }
        let path = canonical_root.join(relative);
        let canonical_file = fs::canonicalize(&path)?;
        if !canonical_file.starts_with(&canonical_root) || !canonical_file.is_file() {
            return Err(invalid("预览路径超出论文文件夹"));
        }
        if !has_ext(relative, &["png", "jpg", "jpeg", "webp"]) {
            return Err(invalid("仅支持预览 PNG / JPEG / WebP 图片"));
        }
        let meta = fs::metadata(&canonical_file)?;
        if meta.len() > 16 * 1024 * 1024 {
            return Err(invalid("预览文件超过 16 MB，请在本地图片软件中查看"));
        }
        Ok(fs::read(canonical_file)?)
    }

    pub fn import_folder(&self, root: &Path) -> Result<ImportResult, ArchiveError> {
        self.import_folder_with_plan(root, Vec::new())
    }

    pub fn import_folder_with_plan(
        &self,
        root: &Path,
        edits: Vec<PlannedFile>,
    ) -> Result<ImportResult, ArchiveError> {
        let mut scan = self.scan_folder(root)?;
        if !edits.is_empty() {
            let mut changes = BTreeMap::new();
            for edit in edits {
                if !valid_logical_path(&edit.logical_relative_path) {
                    return Err(invalid("逻辑目录或文件名包含无效路径片段"));
                }
                if !matches!(
                    edit.role.as_str(),
                    "composite"
                        | "display"
                        | "stat_image"
                        | "statistics"
                        | "other"
                        | "unspecified"
                        | "manuscript"
                ) {
                    return Err(invalid("资源角色不合法"));
                }
                if edit.panel_label.as_ref().is_some_and(|label| {
                    label.len() != 1 || !label.bytes().all(|byte| byte.is_ascii_alphabetic())
                }) {
                    return Err(invalid("Panel 仅支持单个英文字母"));
                }
                if changes
                    .insert(edit.source_relative_path.clone(), edit)
                    .is_some()
                {
                    return Err(invalid("同一原始文件不能被修改两次"));
                }
            }
            if changes.len() != scan.entries.len()
                || !scan
                    .entries
                    .iter()
                    .all(|entry| changes.contains_key(&entry.relative_path))
            {
                return Err(invalid("文件列表已经变化，请重新扫描后再导入"));
            }
            let mut logical_names = BTreeSet::new();
            for entry in &mut scan.entries {
                let edit = changes
                    .remove(&entry.relative_path)
                    .expect("all paths verified");
                if !logical_names.insert(edit.logical_relative_path.to_lowercase()) {
                    return Err(invalid("整理后的目录存在重名文件，已阻止归档"));
                }
                let mut classified = classify(&edit.logical_relative_path, entry.file_size);
                classified.role = edit.role;
                classified.figure_number = edit.figure_number.or(classified.figure_number);
                classified.panel_label = edit.panel_label.or(classified.panel_label);
                classified.sequence = edit.sequence.or(classified.sequence);
                if classified.kind != "manuscript" {
                    classified.kind = match classified.role.as_str() {
                        "composite" => "figure",
                        "display" | "stat_image" | "statistics"
                            if classified.panel_label.is_some() =>
                        {
                            "panel"
                        }
                        "statistics" if classified.figure_number.is_some() => "table",
                        _ => "other",
                    }
                    .to_owned();
                }
                // Always import actual bytes from the original path; only mapping metadata changes.
                entry.kind = classified.kind;
                entry.role = classified.role;
                entry.figure_number = classified.figure_number;
                entry.panel_label = classified.panel_label;
                entry.sequence = classified.sequence;
                entry.issue = classified.issue;
                entry.relative_path =
                    format!("{}|{}", entry.relative_path, edit.logical_relative_path);
            }
        }
        self.import_scanned_folder(scan)
    }

    fn import_scanned_folder(&self, scan: FolderScan) -> Result<ImportResult, ArchiveError> {
        if scan.entries.is_empty() {
            return Err(invalid("文件夹中没有可归档的文件"));
        }
        // The initial import always creates a *draft*. An interrupted import remains editable.
        let paper = self.create_paper(&scan.paper_title, None)?;
        let mut figure_ids: BTreeMap<u32, String> = BTreeMap::new();
        let mut panel_ids: BTreeMap<(u32, String), String> = BTreeMap::new();
        let mut table_ids: BTreeMap<u32, String> = BTreeMap::new();
        let mut failed = Vec::new();
        let mut imported = 0usize;

        for entry in &scan.entries {
            let (source_rel, logical_rel) = entry
                .relative_path
                .split_once('|')
                .unwrap_or((&entry.relative_path, &entry.relative_path));
            let input = Path::new(&scan.folder).join(source_rel);
            let file = match self.import_file(&input) {
                Ok(file) => file,
                Err(e) => {
                    failed.push(format!("{}：{}", entry.relative_path, e));
                    continue;
                }
            };
            imported += 1;
            if entry.kind == "manuscript" {
                if scan.manuscript.as_deref() == Some(source_rel) {
                    if let Err(e) = self.set_imported_manuscript(
                        &paper.paper_id,
                        &file.sha256,
                        &file.original_filename,
                        &file.file_extension,
                    ) {
                        failed.push(format!("正文关联失败：{e}"));
                    }
                } else {
                    self.link_other(&paper.paper_id, &file.data_id, logical_rel)?;
                }
                continue;
            }
            let target = match entry.kind.as_str() {
                "figure" | "panel" => {
                    if let Some(fig_no) = entry.figure_number {
                        let figure_id = if let Some(id) = figure_ids.get(&fig_no) {
                            id.clone()
                        } else {
                            let figure = self.create_paper_item(
                                &paper.paper_id,
                                "figure",
                                &format!("Figure {fig_no}"),
                                None,
                            )?;
                            figure_ids.insert(fig_no, figure.item_id.clone());
                            figure.item_id
                        };
                        if entry.kind == "panel" {
                            if let Some(panel_name) = &entry.panel_label {
                                let key = (fig_no, panel_name.clone());
                                Some(if let Some(id) = panel_ids.get(&key) {
                                    id.clone()
                                } else {
                                    let new_item = self.create_paper_item(
                                        &paper.paper_id,
                                        "panel",
                                        &format!("Figure {fig_no}{panel_name}"),
                                        Some(&figure_id),
                                    )?;
                                    panel_ids.insert(key, new_item.item_id.clone());
                                    new_item.item_id
                                })
                            } else {
                                None
                            }
                        } else {
                            Some(figure_id)
                        }
                    } else {
                        None
                    }
                }
                "table" => {
                    if let Some(number) = entry.figure_number {
                        Some(if let Some(id) = table_ids.get(&number) {
                            id.clone()
                        } else {
                            let table = self.create_paper_item(
                                &paper.paper_id,
                                "table",
                                &format!("Table {number}"),
                                None,
                            )?;
                            table_ids.insert(number, table.item_id.clone());
                            table.item_id
                        })
                    } else {
                        None
                    }
                }
                _ => None,
            };
            if let Some(item_id) = target {
                let reference_id =
                    self.attach_data_to_item(&paper.paper_id, &item_id, &file.data_id)?;
                let connection = self
                    .connection
                    .lock()
                    .map_err(|_| ArchiveError::LockPoisoned)?;
                connection.execute(
                    "UPDATE data_references SET resource_role = ?1, source_relative_path = ?2 WHERE reference_id = ?3",
                    params![entry.role,logical_rel,reference_id],
                )?;
            } else {
                self.link_other(&paper.paper_id, &file.data_id, &entry.relative_path)?;
            }
        }
        Ok(ImportResult {
            paper: self.get_paper(&paper.paper_id)?,
            imported,
            failed,
        })
    }

    fn set_imported_manuscript(
        &self,
        paper_id: &str,
        sha: &str,
        filename: &str,
        extension: &str,
    ) -> Result<(), ArchiveError> {
        let connection = self
            .connection
            .lock()
            .map_err(|_| ArchiveError::LockPoisoned)?;
        connection.execute(
            "UPDATE papers SET manuscript_sha256=?1, manuscript_filename=?2,
             manuscript_extension=?3, updated_at=?4 WHERE paper_id=?5",
            params![
                sha,
                filename,
                extension,
                Utc::now().to_rfc3339_opts(SecondsFormat::Millis, true),
                paper_id
            ],
        )?;
        Ok(())
    }

    fn link_other(
        &self,
        paper_id: &str,
        data_id: &str,
        relative: &str,
    ) -> Result<(), ArchiveError> {
        let connection = self
            .connection
            .lock()
            .map_err(|_| ArchiveError::LockPoisoned)?;
        connection.execute(
            "INSERT OR IGNORE INTO paper_other_resources(paper_id,data_id,source_relative_path) VALUES (?1,?2,?3)",
            params![paper_id,data_id,relative],
        )?;
        Ok(())
    }

    pub fn list_other_resources(&self, paper_id: &str) -> Result<Vec<OtherResource>, ArchiveError> {
        let connection = self
            .connection
            .lock()
            .map_err(|_| ArchiveError::LockPoisoned)?;
        let mut statement = connection.prepare(
            "SELECT d.data_id,d.original_filename,o.source_relative_path,b.sha256,b.file_size
             FROM paper_other_resources o JOIN data_records d ON o.data_id=d.data_id
             JOIN blobs b ON b.sha256=d.blob_sha256
             WHERE o.paper_id=?1 ORDER BY o.source_relative_path",
        )?;
        let rows = statement.query_map(params![paper_id], |row| {
            let size: i64 = row.get(4)?;
            Ok(OtherResource {
                data_id: row.get(0)?,
                original_filename: row.get(1)?,
                source_relative_path: row.get(2)?,
                sha256: row.get(3)?,
                file_size: size as u64,
            })
        })?;
        rows.collect::<Result<Vec<_>, _>>()
            .map_err(ArchiveError::from)
    }

    pub fn update_resource(
        &self,
        paper_id: &str,
        reference_id: &str,
        target_item_id: &str,
        role: &str,
    ) -> Result<(), ArchiveError> {
        self.ensure_paper_editable(paper_id)?;
        if !matches!(
            role,
            "composite" | "display" | "stat_image" | "statistics" | "other" | "unspecified"
        ) {
            return Err(invalid("不支持的文件角色"));
        }
        let connection = self
            .connection
            .lock()
            .map_err(|_| ArchiveError::LockPoisoned)?;
        let changed = connection.execute(
            "UPDATE data_references SET item_id=?1,resource_role=?2
             WHERE reference_id=?3 AND EXISTS(
               SELECT 1 FROM paper_items i WHERE i.item_id=?1 AND i.paper_id=?4
             ) AND EXISTS(
               SELECT 1 FROM paper_items original
               WHERE original.item_id=data_references.item_id AND original.paper_id=?4
             )",
            params![target_item_id, role, reference_id, paper_id],
        )?;
        if changed != 1 {
            return Err(invalid("资源引用不存在或目标图表不属于该论文"));
        }
        Ok(())
    }

    pub fn move_other_to_item(
        &self,
        paper_id: &str,
        data_id: &str,
        item_id: &str,
        role: &str,
    ) -> Result<(), ArchiveError> {
        self.ensure_paper_editable(paper_id)?;
        let original = {
            let connection=self.connection.lock().map_err(|_|ArchiveError::LockPoisoned)?;
            connection.query_row(
                "SELECT source_relative_path FROM paper_other_resources WHERE paper_id=?1 AND data_id=?2",
                params![paper_id,data_id],|r|r.get::<_,String>(0)
            ).optional()?
        }.ok_or_else(||invalid("文件不属于该论文的待分类资源"))?;
        let reference = self.attach_data_to_item(paper_id, item_id, data_id)?;
        self.update_resource(paper_id, &reference, item_id, role)?;
        let connection = self
            .connection
            .lock()
            .map_err(|_| ArchiveError::LockPoisoned)?;
        connection.execute(
            "UPDATE data_references SET source_relative_path=?1 WHERE reference_id=?2",
            params![original, reference],
        )?;
        connection.execute(
            "DELETE FROM paper_other_resources WHERE paper_id=?1 AND data_id=?2",
            params![paper_id, data_id],
        )?;
        Ok(())
    }

    pub fn check_paper(&self, paper_id: &str) -> Result<ArchiveCheck, ArchiveError> {
        let paper = self.get_paper(paper_id)?;
        let items = self.list_paper_items(paper_id)?;
        let resources = self.list_paper_resources(paper_id)?;
        let others = self.list_other_resources(paper_id)?;
        let mut issues = Vec::new();
        if paper.manuscript_sha256.is_none() {
            issues.push(ArchiveIssue {
                severity: "error".into(),
                message: "缺少 PDF/DOCX 论文正文".into(),
            });
        }
        if items.is_empty() {
            issues.push(ArchiveIssue {
                severity: "warning".into(),
                message: "尚无 Figure/Panel/Table 图表对象".into(),
            });
        }
        if !others.is_empty() {
            issues.push(ArchiveIssue {
                severity: "warning".into(),
                message: format!("{} 个文件未匹配到图表，仍保存在 Others", others.len()),
            });
        }
        for item in &items {
            if !resources.iter().any(|r| r.item_id == item.item_id) && item.item_type != "figure" {
                issues.push(ArchiveIssue {
                    severity: "warning".into(),
                    message: format!("{} 尚未关联原始数据", item.label),
                });
            }
        }
        let connection = self
            .connection
            .lock()
            .map_err(|_| ArchiveError::LockPoisoned)?;
        let broken: i64 = connection.query_row(
            "SELECT COUNT(*) FROM data_references r
             JOIN paper_items i ON r.item_id=i.item_id
             LEFT JOIN data_records d ON d.data_id=r.data_id
             LEFT JOIN blobs b ON b.sha256=d.blob_sha256
             WHERE i.paper_id=?1 AND (d.data_id IS NULL OR b.sha256 IS NULL)",
            params![paper_id],
            |r| r.get(0),
        )?;
        if broken > 0 {
            issues.push(ArchiveIssue {
                severity: "error".into(),
                message: format!("发现 {broken} 条失效的资源引用"),
            });
        }
        drop(connection);
        // Verify original bytes, not merely file existence; do not silently archive corrupted data.
        let mut checksums = BTreeMap::<String, u64>::new();
        for resource in &resources {
            checksums.insert(resource.sha256.clone(), resource.file_size);
        }
        for resource in &others {
            checksums.insert(resource.sha256.clone(), resource.file_size);
        }
        if let Some(manuscript_sha) = &paper.manuscript_sha256 {
            let connection = self
                .connection
                .lock()
                .map_err(|_| ArchiveError::LockPoisoned)?;
            let size: Option<i64> = connection
                .query_row(
                    "SELECT file_size FROM blobs WHERE sha256 = ?1",
                    params![manuscript_sha],
                    |row| row.get(0),
                )
                .optional()?;
            if let Some(size) = size {
                if size >= 0 {
                    checksums.insert(manuscript_sha.clone(), size as u64);
                }
            } else {
                issues.push(ArchiveIssue {
                    severity: "error".into(),
                    message: "论文正文关联的 Blob 元数据丢失".into(),
                });
            }
        }
        for (sha, file_size) in checksums {
            let path = self
                .root
                .join("blobs")
                .join("sha256")
                .join(&sha[..2])
                .join(&sha);
            if let Err(reason) = super::store::verify_blob(&path, &sha, file_size) {
                issues.push(ArchiveIssue {
                    severity: "error".into(),
                    message: format!("文件 SHA-256 完整性校验失败：{} ({reason})", &sha[..12]),
                });
            }
        }
        let can_archive = issues.iter().all(|issue| issue.severity != "error");
        Ok(ArchiveCheck {
            paper_id: paper_id.to_owned(),
            can_archive,
            issues,
        })
    }

    pub fn finalize_paper(&self, paper_id: &str) -> Result<Paper, ArchiveError> {
        self.ensure_paper_editable(paper_id)?;
        let check = self.check_paper(paper_id)?;
        if !check.can_archive {
            return Err(invalid("归档检查存在必须处理的错误"));
        }
        let mut paper = self.get_paper(paper_id)?;
        let items = self.list_paper_items(paper_id)?;
        let resources = self.list_paper_resources(paper_id)?;
        let others = self.list_other_resources(paper_id)?;
        let now = Utc::now().to_rfc3339_opts(SecondsFormat::Millis, true);
        paper.archive_status = "archived".to_owned();
        paper.archive_revision += 1;
        paper.archived_at = Some(now.clone());
        paper.updated_at = now.clone();
        let snapshot = serde_json::json!({
            "paper":paper,"items":items,"resources":resources,"others":others
        })
        .to_string();
        let mut connection = self
            .connection
            .lock()
            .map_err(|_| ArchiveError::LockPoisoned)?;
        let tx = connection.transaction()?;
        let current_status: String = tx.query_row(
            "SELECT archive_status FROM papers WHERE paper_id=?1",
            params![paper_id],
            |row| row.get(0),
        )?;
        if current_status == "archived" {
            return Err(invalid("论文已归档"));
        }
        let revision: i64 = tx.query_row(
            "SELECT archive_revision+1 FROM papers WHERE paper_id=?1",
            params![paper_id],
            |r| r.get(0),
        )?;
        tx.execute(
            "INSERT INTO paper_archive_snapshots(paper_id,revision,snapshot_json,archived_at)
             VALUES (?1,?2,?3,?4)",
            params![paper_id, revision, snapshot, now],
        )?;
        tx.execute(
            "UPDATE papers SET archive_status='archived',archive_revision=?1,
             archived_at=?2,updated_at=?2 WHERE paper_id=?3",
            params![revision, now, paper_id],
        )?;
        tx.commit()?;
        drop(connection);
        self.get_paper(paper_id)
    }

    pub fn start_paper_revision(&self, paper_id: &str) -> Result<Paper, ArchiveError> {
        let connection = self
            .connection
            .lock()
            .map_err(|_| ArchiveError::LockPoisoned)?;
        let changed = connection.execute(
            "UPDATE papers SET archive_status='revising',updated_at=?1
             WHERE paper_id=?2 AND archive_status='archived'",
            params![
                Utc::now().to_rfc3339_opts(SecondsFormat::Millis, true),
                paper_id
            ],
        )?;
        if changed != 1 {
            return Err(invalid("仅已归档的论文可开启修订"));
        }
        drop(connection);
        self.get_paper(paper_id)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::TempDir;
    use uuid::Uuid;

    #[test]
    fn recognizes_standard_and_legacy_filename_formats() {
        let composite = classify("Figure/组合图/Figure1.png", 5);
        assert_eq!(composite.kind, "figure");
        assert_eq!(composite.figure_number, Some(1));
        let display = classify("Figure/子图/1-A-1-cell.tif", 12);
        assert_eq!(display.role, "display");
        assert_eq!(display.panel_label.as_deref(), Some("A"));
        assert_eq!(display.sequence, Some(1));
        let legacy = classify("Figure/统计原始图/2-a9.tif", 3);
        assert_eq!(legacy.role, "stat_image");
        assert_eq!(legacy.figure_number, Some(2));
        assert_eq!(legacy.sequence, Some(9));
        let table = classify("Table/1-A-ROS.xlsx", 7);
        assert_eq!(table.kind, "panel");
        assert_eq!(table.role, "statistics");
        let independent = classify("Table/T2-Group.csv", 7);
        assert_eq!(independent.kind, "table");
        assert_eq!(independent.figure_number, Some(2));
    }

    #[test]
    fn whole_folder_import_and_archive_are_persistent() {
        let temp = TempDir::new().unwrap();
        let source = temp.path().join("Example paper");
        for dir in [
            "Figure/组合图",
            "Figure/子图",
            "Figure/统计原始图",
            "Table",
            "Others",
        ] {
            fs::create_dir_all(source.join(dir)).unwrap();
        }
        for (relative, bytes) in [
            ("manuscript.pdf", b"%PDF example".as_slice()),
            ("Figure/组合图/Figure1.png", b"composite"),
            ("Figure/子图/1-A-1-cell.png", b"image"),
            ("Figure/统计原始图/1-A-2-control.png", b"sample"),
            ("Table/1-A-ROS.csv", b"value,unit\n12,AU"),
            ("Others/readme.txt", b"attachment"),
        ] {
            fs::write(source.join(relative), bytes).unwrap();
        }
        let root = temp.path().join("archive");
        let store = ArchiveStore::open(&root).unwrap();
        let scan = store.scan_folder(&source).unwrap();
        assert_eq!(scan.entries.len(), 6);
        assert_eq!(scan.manuscript.as_deref(), Some("manuscript.pdf"));
        let result = store.import_folder(&source).unwrap();
        assert!(result.failed.is_empty());
        assert_eq!(result.imported, 6);
        assert_eq!(result.paper.archive_status, "draft");
        let items = store.list_paper_items(&result.paper.paper_id).unwrap();
        assert_eq!(items.len(), 2);
        assert_eq!(
            store
                .list_paper_resources(&result.paper.paper_id)
                .unwrap()
                .len(),
            4
        );
        assert_eq!(
            store
                .list_other_resources(&result.paper.paper_id)
                .unwrap()
                .len(),
            1
        );
        assert!(
            store
                .check_paper(&result.paper.paper_id)
                .unwrap()
                .can_archive
        );
        let archived = store.finalize_paper(&result.paper.paper_id).unwrap();
        assert_eq!(archived.archive_status, "archived");
        assert_eq!(archived.archive_revision, 1);
        assert!(
            store
                .create_paper_item(&archived.paper_id, "table", "Table 5", None)
                .is_err()
        );
        drop(store);
        let reopened = ArchiveStore::open(root).unwrap();
        assert_eq!(
            reopened
                .get_paper(&archived.paper_id)
                .unwrap()
                .archive_status,
            "archived"
        );
        assert!(reopened.start_paper_revision(&archived.paper_id).is_ok());
        assert_eq!(
            reopened
                .get_paper(&archived.paper_id)
                .unwrap()
                .archive_status,
            "revising"
        );
    }

    #[test]
    fn scan_rejects_archive_directory_within_source() {
        let temp = TempDir::new().unwrap();
        let root = temp.path().join("source").join("warehouse");
        let store = ArchiveStore::open(&root).unwrap();
        assert!(store.scan_folder(temp.path()).is_err());
    }

    #[test]
    fn scan_finds_ambiguous_files_without_losing_them() {
        let temp = TempDir::new().unwrap();
        let source = temp.path().join("paper");
        fs::create_dir_all(source.join("Table")).unwrap();
        fs::write(source.join("Table").join("unknown.xlsx"), b"x").unwrap();
        let scan = scan_paper_folder(&source).unwrap();
        assert_eq!(scan.entries.len(), 1);
        assert!(!scan.warnings.is_empty());
        assert_eq!(scan.entries[0].kind, "other");
    }

    #[test]
    fn batch_plan_renames_logical_paths_without_touching_source() {
        let temporary = TempDir::new().unwrap();
        let source = temporary.path().join("test-paper");
        fs::create_dir_all(source.join("Figure/子图")).unwrap();
        fs::write(source.join("paper.pdf"), b"%PDF-1.4 demo").unwrap();
        fs::write(source.join("Figure/子图/old.png"), b"original bytes").unwrap();
        let store = ArchiveStore::open(temporary.path().join("warehouse")).unwrap();
        let scan = store.scan_folder(&source).unwrap();
        let edits: Vec<PlannedFile> = scan
            .entries
            .iter()
            .map(|e| {
                let mut plan = PlannedFile {
                    source_relative_path: e.relative_path.clone(),
                    logical_relative_path: e.relative_path.clone(),
                    role: e.role.clone(),
                    figure_number: e.figure_number,
                    panel_label: e.panel_label.clone(),
                    sequence: e.sequence,
                };
                if e.relative_path.ends_with("old.png") {
                    plan.logical_relative_path = "Figure/子图/1-A-1-original.png".into();
                    plan.role = "display".into();
                    plan.figure_number = Some(1);
                    plan.panel_label = Some("A".into());
                    plan.sequence = Some(1);
                }
                plan
            })
            .collect();
        let result = store.import_folder_with_plan(&source, edits).unwrap();
        assert_eq!(result.imported, 2);
        let rows = store.list_paper_resources(&result.paper.paper_id).unwrap();
        assert_eq!(rows.len(), 1);
        assert_eq!(
            rows[0].source_relative_path.as_deref(),
            Some("Figure/子图/1-A-1-original.png")
        );
        assert_eq!(rows[0].original_filename, "old.png");
        assert_eq!(
            fs::read(source.join("Figure/子图/old.png")).unwrap(),
            b"original bytes"
        );
        assert!(!source.join("Figure/子图/1-A-1-original.png").exists());
    }

    #[test]
    fn batch_plan_prevents_case_insensitive_logical_name_collisions() {
        let temporary = TempDir::new().unwrap();
        let source = temporary.path().join("paper");
        fs::create_dir_all(&source).unwrap();
        fs::write(source.join("paper.pdf"), b"pdf").unwrap();
        fs::write(source.join("a.txt"), b"a").unwrap();
        fs::write(source.join("b.txt"), b"b").unwrap();
        let store = ArchiveStore::open(temporary.path().join("warehouse")).unwrap();
        let scan = store.scan_folder(&source).unwrap();
        let plans: Vec<PlannedFile> = scan
            .entries
            .iter()
            .map(|e| PlannedFile {
                source_relative_path: e.relative_path.clone(),
                logical_relative_path: if e.relative_path == "paper.pdf" {
                    e.relative_path.clone()
                } else {
                    "Others/DUP.txt".into()
                },
                role: e.role.clone(),
                figure_number: e.figure_number,
                panel_label: e.panel_label.clone(),
                sequence: e.sequence,
            })
            .collect();
        assert!(store.import_folder_with_plan(&source, plans).is_err());
        assert!(store.list_papers().unwrap().is_empty());
    }
}
