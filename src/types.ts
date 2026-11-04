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
