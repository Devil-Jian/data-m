-- S8 Schema v3: additive statistics metadata and immutable pinned-version relationships.
-- Apply only to a fully recognized v2 repository in one IMMEDIATE transaction.
ALTER TABLE statistical_analyses ADD COLUMN notes TEXT NOT NULL DEFAULT '';
ALTER TABLE statistical_analyses ADD COLUMN updated_at TEXT NOT NULL DEFAULT '';
CREATE TABLE analysis_asset_versions(
  analysis_id TEXT NOT NULL REFERENCES statistical_analyses(id) ON DELETE RESTRICT,
  asset_version_id TEXT NOT NULL REFERENCES asset_versions(id) ON DELETE RESTRICT,
  role TEXT NOT NULL CHECK(role IN ('input','result')),
  PRIMARY KEY(analysis_id,asset_version_id,role)
) STRICT;
CREATE INDEX analysis_asset_versions_version_idx ON analysis_asset_versions(asset_version_id);

-- Existing application checks are backed by server-side DB guards.
CREATE TRIGGER s8_samples_assay_insert BEFORE INSERT ON analysis_samples
WHEN NOT EXISTS (
  SELECT 1 FROM statistical_analyses a
  JOIN replicates r ON r.id=NEW.replicate_id
  JOIN experiment_groups g ON g.id=r.group_id
  WHERE a.id=NEW.analysis_id AND g.assay_id=a.assay_id
)
BEGIN SELECT RAISE(ABORT,'replicate belongs to another assay'); END;
CREATE TRIGGER s8_samples_assay_update BEFORE UPDATE ON analysis_samples
WHEN NOT EXISTS (
  SELECT 1 FROM statistical_analyses a
  JOIN replicates r ON r.id=NEW.replicate_id
  JOIN experiment_groups g ON g.id=r.group_id
  WHERE a.id=NEW.analysis_id AND g.assay_id=a.assay_id
)
BEGIN SELECT RAISE(ABORT,'replicate belongs to another assay'); END;

-- A version must have been associated with the assay at least once.
-- Soft-deleted links are intentionally eligible for historical provenance.
CREATE TRIGGER s8_versions_assay_insert BEFORE INSERT ON analysis_asset_versions
WHEN NOT EXISTS (
  SELECT 1 FROM statistical_analyses a
  JOIN asset_versions v ON v.id=NEW.asset_version_id
  JOIN asset_links l ON l.asset_id=v.asset_id AND l.assay_id=a.assay_id
  WHERE a.id=NEW.analysis_id
)
BEGIN SELECT RAISE(ABORT,'asset version is unrelated to assay'); END;
CREATE TRIGGER s8_versions_assay_update BEFORE UPDATE ON analysis_asset_versions
WHEN NOT EXISTS (
  SELECT 1 FROM statistical_analyses a
  JOIN asset_versions v ON v.id=NEW.asset_version_id
  JOIN asset_links l ON l.asset_id=v.asset_id AND l.assay_id=a.assay_id
  WHERE a.id=NEW.analysis_id
)
BEGIN SELECT RAISE(ABORT,'asset version is unrelated to assay'); END;

CREATE TRIGGER s8_analysis_frozen_update BEFORE UPDATE ON statistical_analyses
WHEN OLD.frozen_at IS NOT NULL
BEGIN SELECT RAISE(ABORT,'frozen analysis is immutable'); END;
CREATE TRIGGER s8_analysis_delete BEFORE DELETE ON statistical_analyses
BEGIN SELECT RAISE(ABORT,'analysis deletion requires audited retention policy'); END;
CREATE TRIGGER s8_samples_frozen_insert BEFORE INSERT ON analysis_samples
WHEN EXISTS(SELECT 1 FROM statistical_analyses WHERE id=NEW.analysis_id AND frozen_at IS NOT NULL)
BEGIN SELECT RAISE(ABORT,'frozen inclusion is immutable'); END;
CREATE TRIGGER s8_samples_frozen_update BEFORE UPDATE ON analysis_samples
WHEN EXISTS(SELECT 1 FROM statistical_analyses WHERE id=OLD.analysis_id AND frozen_at IS NOT NULL)
   OR EXISTS(SELECT 1 FROM statistical_analyses WHERE id=NEW.analysis_id AND frozen_at IS NOT NULL)
BEGIN SELECT RAISE(ABORT,'frozen inclusion is immutable'); END;
CREATE TRIGGER s8_samples_frozen_delete BEFORE DELETE ON analysis_samples
WHEN EXISTS(SELECT 1 FROM statistical_analyses WHERE id=OLD.analysis_id AND frozen_at IS NOT NULL)
BEGIN SELECT RAISE(ABORT,'frozen inclusion is immutable'); END;
CREATE TRIGGER s8_versions_frozen_insert BEFORE INSERT ON analysis_asset_versions
WHEN EXISTS(SELECT 1 FROM statistical_analyses WHERE id=NEW.analysis_id AND frozen_at IS NOT NULL)
BEGIN SELECT RAISE(ABORT,'frozen version reference is immutable'); END;
CREATE TRIGGER s8_versions_frozen_update BEFORE UPDATE ON analysis_asset_versions
WHEN EXISTS(SELECT 1 FROM statistical_analyses WHERE id=OLD.analysis_id AND frozen_at IS NOT NULL)
   OR EXISTS(SELECT 1 FROM statistical_analyses WHERE id=NEW.analysis_id AND frozen_at IS NOT NULL)
BEGIN SELECT RAISE(ABORT,'frozen version reference is immutable'); END;
CREATE TRIGGER s8_versions_frozen_delete BEFORE DELETE ON analysis_asset_versions
WHEN EXISTS(SELECT 1 FROM statistical_analyses WHERE id=OLD.analysis_id AND frozen_at IS NOT NULL)
BEGIN SELECT RAISE(ABORT,'frozen version reference is immutable'); END;
INSERT INTO schema_migrations(version) VALUES(3);
