-- Schema v2: retain existing papers, data_records, blobs and data_references.
ALTER TABLE papers ADD COLUMN manuscript_sha256 TEXT REFERENCES blobs(sha256) ON DELETE RESTRICT;
ALTER TABLE papers ADD COLUMN manuscript_filename TEXT;
ALTER TABLE papers ADD COLUMN manuscript_extension TEXT;

-- PDF anchors use a 1-based page number and normalized 0..1 coordinates.
-- DOCX anchors use kind=docx_image/docx_table and a 0-based occurrence index.
ALTER TABLE paper_items ADD COLUMN anchor_kind TEXT;
ALTER TABLE paper_items ADD COLUMN anchor_page INTEGER;
ALTER TABLE paper_items ADD COLUMN anchor_x REAL;
ALTER TABLE paper_items ADD COLUMN anchor_y REAL;
ALTER TABLE paper_items ADD COLUMN anchor_w REAL;
ALTER TABLE paper_items ADD COLUMN anchor_h REAL;
ALTER TABLE paper_items ADD COLUMN anchor_index INTEGER;

CREATE INDEX IF NOT EXISTS idx_papers_created ON papers(created_at);
CREATE INDEX IF NOT EXISTS idx_paper_items_anchor ON paper_items(paper_id, anchor_kind, anchor_page, anchor_index);
PRAGMA user_version = 2;
