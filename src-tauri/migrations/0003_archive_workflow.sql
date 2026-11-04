-- v3: separate paper draft processing from read-only archived browsing.
ALTER TABLE papers ADD COLUMN archive_status TEXT NOT NULL DEFAULT 'draft'
    CHECK (archive_status IN ('draft', 'archived', 'revising'));
ALTER TABLE papers ADD COLUMN archived_at TEXT;
ALTER TABLE papers ADD COLUMN archive_revision INTEGER NOT NULL DEFAULT 0;
ALTER TABLE data_references ADD COLUMN resource_role TEXT NOT NULL DEFAULT 'unspecified'
    CHECK(resource_role IN ('composite', 'display', 'stat_image', 'statistics', 'other', 'unspecified'));
ALTER TABLE data_references ADD COLUMN source_relative_path TEXT;
CREATE TABLE paper_other_resources (
    paper_id TEXT NOT NULL REFERENCES papers(paper_id) ON DELETE CASCADE,
    data_id TEXT NOT NULL REFERENCES data_records(data_id) ON DELETE RESTRICT,
    source_relative_path TEXT NOT NULL,
    PRIMARY KEY (paper_id, data_id)
);
CREATE TABLE paper_archive_snapshots (
    paper_id TEXT NOT NULL REFERENCES papers(paper_id) ON DELETE CASCADE,
    revision INTEGER NOT NULL CHECK(revision >= 1),
    snapshot_json TEXT NOT NULL,
    archived_at TEXT NOT NULL,
    PRIMARY KEY (paper_id, revision)
);
CREATE INDEX idx_papers_status ON papers(archive_status, updated_at);
PRAGMA user_version = 3;
