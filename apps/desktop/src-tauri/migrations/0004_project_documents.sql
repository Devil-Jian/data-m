-- S14 project-scoped paper/figure source files, distinct from assay-scoped
-- experimental asset links and from logical Paper/Figure/Panel entities.
-- The existing immutable S3 CAS blob + AssetVersion identity is preserved.
CREATE TABLE project_document_links(
    id TEXT PRIMARY KEY,
    project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE RESTRICT,
    asset_id TEXT NOT NULL REFERENCES data_assets(id) ON DELETE RESTRICT,
    kind TEXT NOT NULL CHECK(kind IN ('paper','figure')),
    display_name TEXT NOT NULL CHECK(length(trim(display_name)) > 0),
    deleted_at TEXT
) STRICT;
CREATE UNIQUE INDEX project_document_active_name
    ON project_document_links(project_id,kind,display_name COLLATE NOCASE)
    WHERE deleted_at IS NULL;
CREATE INDEX project_document_asset_idx ON project_document_links(asset_id);
INSERT INTO schema_migrations(version) VALUES (4);
