PRAGMA foreign_keys = ON;

CREATE TABLE archive_meta (
    singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
    archive_id TEXT NOT NULL UNIQUE,
    schema_version INTEGER NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE blobs (
    sha256 TEXT PRIMARY KEY CHECK (length(sha256) = 64),
    file_size INTEGER NOT NULL CHECK (file_size >= 0),
    relative_path TEXT NOT NULL UNIQUE,
    media_type TEXT,
    created_at TEXT NOT NULL
);

CREATE TABLE data_records (
    data_id TEXT PRIMARY KEY,
    display_id TEXT NOT NULL UNIQUE,
    blob_sha256 TEXT NOT NULL REFERENCES blobs(sha256) ON DELETE RESTRICT,
    original_filename TEXT NOT NULL,
    file_extension TEXT NOT NULL DEFAULT '',
    category TEXT NOT NULL CHECK (category IN ('image', 'spreadsheet', 'document', 'other')),
    note TEXT NOT NULL DEFAULT '',
    created_by TEXT,
    created_at TEXT NOT NULL,
    deleted_at TEXT
);

CREATE TABLE papers (
    paper_id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    doi TEXT,
    journal TEXT,
    authors TEXT,
    year INTEGER,
    status TEXT NOT NULL DEFAULT 'draft',
    revision INTEGER NOT NULL DEFAULT 1 CHECK (revision >= 1),
    content_hash TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE paper_items (
    item_id TEXT PRIMARY KEY,
    paper_id TEXT NOT NULL REFERENCES papers(paper_id) ON DELETE CASCADE,
    parent_id TEXT REFERENCES paper_items(item_id) ON DELETE RESTRICT,
    item_type TEXT NOT NULL CHECK (item_type IN ('figure', 'panel', 'table')),
    label TEXT NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0,
    asset_blob_sha256 TEXT REFERENCES blobs(sha256) ON DELETE RESTRICT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE data_references (
    reference_id TEXT PRIMARY KEY,
    data_id TEXT NOT NULL REFERENCES data_records(data_id) ON DELETE RESTRICT,
    item_id TEXT NOT NULL REFERENCES paper_items(item_id) ON DELETE CASCADE,
    reuse_note TEXT,
    created_at TEXT NOT NULL,
    UNIQUE (data_id, item_id)
);

CREATE TABLE package_imports (
    package_id TEXT PRIMARY KEY,
    source_archive_id TEXT NOT NULL,
    package_schema_version INTEGER NOT NULL,
    package_sha256 TEXT NOT NULL CHECK (length(package_sha256) = 64),
    imported_at TEXT NOT NULL,
    import_status TEXT NOT NULL
);

CREATE TABLE import_conflicts (
    conflict_id TEXT PRIMARY KEY,
    package_id TEXT REFERENCES package_imports(package_id) ON DELETE CASCADE,
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    local_hash TEXT,
    incoming_hash TEXT,
    resolution TEXT,
    created_at TEXT NOT NULL
);

CREATE TABLE operation_logs (
    operation_id TEXT PRIMARY KEY,
    operation_type TEXT NOT NULL,
    entity_type TEXT,
    entity_id TEXT,
    detail_json TEXT,
    created_at TEXT NOT NULL
);

CREATE INDEX idx_data_records_blob_sha256 ON data_records(blob_sha256);
CREATE INDEX idx_data_records_created_at ON data_records(created_at);
CREATE INDEX idx_papers_title ON papers(title);
CREATE INDEX idx_papers_doi ON papers(doi);
CREATE INDEX idx_paper_items_paper_order ON paper_items(paper_id, sort_order);
CREATE INDEX idx_paper_items_parent ON paper_items(parent_id);
CREATE INDEX idx_data_references_data ON data_references(data_id);
CREATE INDEX idx_data_references_item ON data_references(item_id);
CREATE INDEX idx_import_conflicts_package ON import_conflicts(package_id);

PRAGMA user_version = 1;
