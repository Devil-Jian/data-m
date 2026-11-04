export interface ArchiveStatus {
  initialized: boolean;
  data_dir: string | null;
  archive_id: string | null;
  schema_version: number | null;
  error: string | null;
}

export interface DataRecord {
  data_id: string;
  display_id: string;
  sha256: string;
  original_filename: string;
  file_extension: string;
  category: string;
  file_size: number;
  created_at: string;
}

export interface Paper {
  paper_id: string;
  title: string;
  doi: string | null;
  manuscript_sha256: string | null;
  manuscript_filename: string | null;
  manuscript_extension: string | null;
  created_at: string;
  updated_at: string;
  item_count: number;
  resource_count: number;
  archive_status: "draft" | "archived" | "revising";
  archive_revision: number;
  archived_at: string | null;
}

export interface PaperItem {
  item_id: string;
  paper_id: string;
  parent_id: string | null;
  item_type: "figure" | "panel" | "table";
  label: string;
  sort_order: number;
  anchor_kind: "pdf" | "docx_image" | "docx_table" | null;
  anchor_page: number | null;
  anchor_x: number | null;
  anchor_y: number | null;
  anchor_w: number | null;
  anchor_h: number | null;
  anchor_index: number | null;
}

export interface ItemAnchor {
  kind: "pdf" | "docx_image" | "docx_table";
  page: number | null;
  x: number | null;
  y: number | null;
  w: number | null;
  h: number | null;
  index: number | null;
}

export interface PaperResource {
  reference_id: string;
  item_id: string;
  item_label: string;
  data_id: string;
  display_id: string;
  original_filename: string;
  category: string;
  file_size: number;
  sha256: string;
  created_at: string;
  resource_role: "composite" | "display" | "stat_image" | "statistics" | "other" | "unspecified";
  source_relative_path: string | null;
}

export interface DataUsage {
  paper_id: string;
  paper_title: string;
  item_id: string;
  item_label: string;
}

export interface DataDetails {
  record: DataRecord;
  relative_path: string;
  usages: DataUsage[];
}

export interface ScanEntry {
  relative_path: string;
  kind: string;
  role: string;
  figure_number: number | null;
  panel_label: string | null;
  sequence: number | null;
  issue: string | null;
  file_size: number;
}

export interface FolderScan {
  folder: string;
  paper_title: string;
  manuscript: string | null;
  entries: ScanEntry[];
  warnings: string[];
  total_bytes: number;
}

export interface ImportResult {
  paper: Paper;
  imported: number;
  failed: string[];
}

export interface ArchiveIssue {
  severity: "error" | "warning";
  message: string;
}

export interface ArchiveCheck {
  paper_id: string;
  can_archive: boolean;
  issues: ArchiveIssue[];
}

export interface OtherResource {
  data_id: string;
  original_filename: string;
  source_relative_path: string;
  sha256: string;
  file_size: number;
}
