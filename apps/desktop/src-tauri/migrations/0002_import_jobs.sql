-- Only applied transactionally after full v1 identity/schema validation.
DROP INDEX asset_links_active_name;
CREATE UNIQUE INDEX asset_links_active_name
  ON asset_links(assay_id,COALESCE(group_id,''),COALESCE(replicate_id,''),stage,display_name COLLATE NOCASE)
  WHERE deleted_at IS NULL;

CREATE TABLE import_jobs(
  id TEXT PRIMARY KEY,
  request_id TEXT NOT NULL UNIQUE,
  payload TEXT NOT NULL,
  target_json TEXT NOT NULL,
  status TEXT NOT NULL CHECK(status IN ('prepared','running','completed','failed','cancelled','interrupted','integrity_failed')),
  cancel_requested INTEGER NOT NULL DEFAULT 0 CHECK(cancel_requested IN (0,1)),
  error_code TEXT,
  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
) STRICT;
CREATE TABLE import_files(
  id TEXT PRIMARY KEY,
  job_id TEXT NOT NULL REFERENCES import_jobs(id) ON DELETE RESTRICT,
  ordinal INTEGER NOT NULL,
  source_path TEXT NOT NULL,
  source_identity TEXT NOT NULL,
  source_mtime TEXT NOT NULL,
  size_bytes INTEGER NOT NULL CHECK(size_bytes>=0),
  name TEXT NOT NULL,
  staging_name TEXT NOT NULL,
  staging_identity TEXT,
  sha256 TEXT,
  phase TEXT NOT NULL CHECK(phase IN ('queued','copying','staged','blob_published','committed')),
  status TEXT NOT NULL CHECK(status IN ('queued','copying','staged','blob_published','committed','failed','cancelled','interrupted')),
  copied_bytes INTEGER NOT NULL DEFAULT 0 CHECK(copied_bytes>=0),
  error_code TEXT,
  asset_id TEXT NOT NULL UNIQUE,
  version_id TEXT NOT NULL UNIQUE,
  link_id TEXT NOT NULL UNIQUE,
  UNIQUE(job_id,ordinal)
) STRICT;
CREATE INDEX import_files_job_idx ON import_files(job_id,ordinal);
INSERT INTO schema_migrations(version) VALUES(2);
