-- Data-M Schema v1. New, empty repositories only; never run against unrecognized DBs.
-- All IDs are opaque stable application-generated strings. Foreign keys must be ON per connection.
CREATE TABLE schema_migrations(
  version INTEGER PRIMARY KEY CHECK (version > 0),
  applied_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
) STRICT;

CREATE TABLE projects(
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL CHECK(length(trim(name)) > 0),
  deleted_at TEXT
) STRICT;
CREATE TABLE themes(
  id TEXT PRIMARY KEY,
  project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE RESTRICT,
  name TEXT NOT NULL CHECK(length(trim(name)) > 0),
  deleted_at TEXT,
  UNIQUE(project_id,id)
) STRICT;
CREATE TABLE assays(
  id TEXT PRIMARY KEY,
  theme_id TEXT NOT NULL REFERENCES themes(id) ON DELETE RESTRICT,
  name TEXT NOT NULL CHECK(length(trim(name)) > 0),
  deleted_at TEXT,
  UNIQUE(theme_id,id)
) STRICT;
CREATE TABLE experiment_groups(
  id TEXT PRIMARY KEY,
  assay_id TEXT NOT NULL REFERENCES assays(id) ON DELETE RESTRICT,
  name TEXT NOT NULL CHECK(length(trim(name)) > 0),
  sort_order INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT,
  UNIQUE(assay_id,id),
  UNIQUE(id,assay_id)
) STRICT;
CREATE TABLE replicates(
  id TEXT PRIMARY KEY,
  group_id TEXT NOT NULL REFERENCES experiment_groups(id) ON DELETE RESTRICT,
  label TEXT NOT NULL CHECK(length(trim(label)) > 0),
  deleted_at TEXT,
  UNIQUE(group_id,id),
  UNIQUE(id,group_id)
) STRICT;

-- CAS blobs are immutable bytes addressed by lowercase sha256.
CREATE TABLE blobs(
  sha256 TEXT PRIMARY KEY CHECK(
    length(sha256)=64 AND sha256 NOT GLOB '*[^0-9a-f]*'
  ),
  size_bytes INTEGER NOT NULL CHECK(size_bytes>=0),
  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
) STRICT;
CREATE TABLE data_assets(
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL CHECK(length(trim(name))>0),
  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
  deleted_at TEXT
) STRICT;
CREATE TABLE asset_versions(
  id TEXT PRIMARY KEY,
  asset_id TEXT NOT NULL REFERENCES data_assets(id) ON DELETE RESTRICT,
  version_number INTEGER NOT NULL CHECK(version_number>0),
  blob_sha256 TEXT NOT NULL REFERENCES blobs(sha256) ON DELETE RESTRICT,
  source_name TEXT NOT NULL,
  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
  UNIQUE(asset_id,version_number),
  UNIQUE(asset_id,id)
) STRICT;
-- A file may appear in multiple contexts; a logical copy never inserts a replicate.
CREATE TABLE asset_links(
  id TEXT PRIMARY KEY,
  asset_id TEXT NOT NULL REFERENCES data_assets(id) ON DELETE RESTRICT,
  assay_id TEXT NOT NULL REFERENCES assays(id) ON DELETE RESTRICT,
  group_id TEXT REFERENCES experiment_groups(id) ON DELETE RESTRICT,
  replicate_id TEXT REFERENCES replicates(id) ON DELETE RESTRICT,
  stage TEXT NOT NULL CHECK(stage IN ('raw','processed','statistics','display')),
  display_name TEXT NOT NULL CHECK(length(trim(display_name))>0),
  deleted_at TEXT,
  CHECK(replicate_id IS NULL OR group_id IS NOT NULL),
  FOREIGN KEY(group_id,assay_id) REFERENCES experiment_groups(id,assay_id),
  FOREIGN KEY(replicate_id,group_id) REFERENCES replicates(id,group_id)
) STRICT;
CREATE UNIQUE INDEX asset_links_active_name
  ON asset_links(assay_id,COALESCE(group_id,''),stage,display_name COLLATE NOCASE)
  WHERE deleted_at IS NULL;

CREATE TABLE statistical_analyses(
  id TEXT PRIMARY KEY,
  assay_id TEXT NOT NULL REFERENCES assays(id) ON DELETE RESTRICT,
  name TEXT NOT NULL,
  frozen_at TEXT
) STRICT;
CREATE TABLE analysis_samples(
  analysis_id TEXT NOT NULL REFERENCES statistical_analyses(id) ON DELETE RESTRICT,
  replicate_id TEXT NOT NULL REFERENCES replicates(id) ON DELETE RESTRICT,
  included INTEGER NOT NULL CHECK(included IN (0,1)),
  PRIMARY KEY(analysis_id,replicate_id)
) STRICT;

CREATE TABLE papers(
  id TEXT PRIMARY KEY,
  project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE RESTRICT,
  title TEXT NOT NULL CHECK(length(trim(title))>0),
  deleted_at TEXT
) STRICT;
CREATE TABLE figures(
  id TEXT PRIMARY KEY,
  paper_id TEXT NOT NULL REFERENCES papers(id) ON DELETE RESTRICT,
  label TEXT NOT NULL,
  sort_order INTEGER NOT NULL DEFAULT 0
) STRICT;
CREATE TABLE panels(
  id TEXT PRIMARY KEY,
  figure_id TEXT NOT NULL REFERENCES figures(id) ON DELETE RESTRICT,
  label TEXT NOT NULL,
  sort_order INTEGER NOT NULL DEFAULT 0
) STRICT;
-- Pinned version links survive logical file moves and renames.
CREATE TABLE panel_asset_versions(
  panel_id TEXT NOT NULL REFERENCES panels(id) ON DELETE RESTRICT,
  asset_version_id TEXT NOT NULL REFERENCES asset_versions(id) ON DELETE RESTRICT,
  usage TEXT NOT NULL CHECK(usage IN ('display','statistics','source','other')),
  PRIMARY KEY(panel_id,asset_version_id,usage)
) STRICT;
CREATE TABLE representative_sources(
  panel_id TEXT NOT NULL REFERENCES panels(id) ON DELETE RESTRICT,
  replicate_id TEXT NOT NULL REFERENCES replicates(id) ON DELETE RESTRICT,
  asset_version_id TEXT REFERENCES asset_versions(id) ON DELETE RESTRICT,
  PRIMARY KEY(panel_id,replicate_id)
) STRICT;

CREATE TABLE audit_events(
  id TEXT PRIMARY KEY,
  action TEXT NOT NULL CHECK(length(trim(action))>0),
  object_type TEXT NOT NULL,
  object_id TEXT NOT NULL,
  details_json TEXT NOT NULL DEFAULT '{}',
  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
) STRICT;

-- Protect registered CAS contents and version identity; add versions rather than rewrite them.
CREATE TRIGGER no_blob_update BEFORE UPDATE ON blobs
BEGIN SELECT RAISE(ABORT,'blob metadata is immutable'); END;
CREATE TRIGGER no_asset_version_update BEFORE UPDATE ON asset_versions
BEGIN SELECT RAISE(ABORT,'asset versions are immutable'); END;
CREATE TRIGGER no_blob_delete BEFORE DELETE ON blobs
BEGIN SELECT RAISE(ABORT,'blob removal requires verified garbage collection'); END;
CREATE TRIGGER no_asset_version_delete BEFORE DELETE ON asset_versions
BEGIN SELECT RAISE(ABORT,'asset version removal is disabled'); END;

CREATE INDEX asset_versions_asset_idx ON asset_versions(asset_id);
CREATE INDEX asset_links_asset_idx ON asset_links(asset_id);
CREATE INDEX asset_links_assay_idx ON asset_links(assay_id,stage) WHERE deleted_at IS NULL;
CREATE INDEX replicates_group_idx ON replicates(group_id);
CREATE INDEX panel_asset_versions_version_idx ON panel_asset_versions(asset_version_id);
CREATE INDEX analysis_samples_replicate_idx ON analysis_samples(replicate_id);
INSERT INTO schema_migrations(version) VALUES (1);
