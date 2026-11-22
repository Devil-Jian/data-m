# S9 Implementation Design — Paper / Figure / Panel
TASK_ID=S9. Independent base: S7 `15e784a6bf939cf509254aacdea33bb120cf9165`. S8 remains independent. Status: in_progress; Windows manual acceptance pending.

## Audited schema
Existing migration 0001 already defines papers(project_id,title,deleted_at), figures(paper_id,label,sort_order), panels(figure_id,label,sort_order), panel_asset_versions(panel_id,asset_version_id,usage) and representative_sources(panel_id,replicate_id,asset_version_id). Asset versions and Blob records are immutable and FK RESTRICT. S9 reuses schema v2; no migration number allocated. Notes/status fields are not present: avoid unauthorized schema changes; no unsupported persistence claims.

## Scope and invariant
Paper belongs to live project; versions eligible only where an active asset_link traverses assay → theme → same project; candidate version must belong to linked asset. Panel version association stores the exact immutable asset_versions.id and usage ('display','statistics','source','other'). Replicate association requires active group/assay/project and an asset_link matching exact asset_id and replicate. Figure/Panel sorting performed in a single IMMEDIATE transaction, with exact-membership validation. Nonempty containers cannot be deleted; deleting an association never deletes blobs. Mutations use s1_requests request tokens scoped to paper_workspace for replay protection and audit_events entries. All queries pass through controlled S1 test workspace connection. No file system writes.

## IPC
Single structured `paper_workspace({request})` with action `list|create|rename|delete|reorder|attach|detach|representative|clearRepresentative|metadata|candidates`; kinds `paper|figure|panel`. Scoped by projectId; id/parentId/versionId/versionIds/replicateId/status/notes/usage/orderIds/requestId supply action arguments. Snapshot returns papers with ordered figures/panels, association metadata and representative sources. Candidate versions are restricted to active project and provide assay/group/replicate/stage/name/hash. Mutations are transactional, server-authorized and idempotent for exact matching tokens.

## Parallel integration
No S8 dependency, table or migration. S8 may touch workbench and task state; preserve both patches through integration, do not replace S8 code with S7 baseline. S9 does not assign a schema migration number.

## Verification
Native Rust SQLite tests must exercise persistence, version pinning, project isolation, duplicate rejections, ordering and n invariance. Browser @S9 tests are UI-only when using injected IPC mock; Windows CI must build and upload an EXE before claiming machine_passed.
## Completed machine validation
Windows [CI #44002053468](https://github.com/Devil-Jian/data-m/actions/runs/44002053468) succeeded at source SHA `d61eb8349eba668071dd123e2452a2b61d933658`; 6 native S9 tests and 101 full Rust tests passed. [Unsigned Windows EXE artifact](https://github.com/Devil-Jian/data-m/actions/runs/44002053468/artifacts/14292513472) uploaded. Manual Windows signoff remains pending; this is not `accepted`.
