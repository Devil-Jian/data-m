# S9 integration handoff (independent of S8)
Base: S7 `15e784a6bf939cf509254aacdea33bb120cf9165`. S9 branch `feat/s9-paper-figure-panel`; Draft PR #53 (base S7). Do not merge S8, S9 or main without user approval.

## Changed files
- `apps/desktop/src-tauri/src/paper_workspace.rs` (new): transactional Paper/Figure/Panel repository, pins, representative validation, constrained candidate read, native SQLite tests.
- `apps/desktop/src-tauri/src/lib.rs`: registers `paper_workspace`.
- `packages/core/src/paper-workspace.tsx`, `paper-workspace.css` (new): formal three-column SQLite-backed paper workspace.
- `packages/core/src/workbench.tsx`: paper route to new formal component; removes demo-only paper navigator from visible route.
- `tests/desktop-ui/s9-paper.spec.ts`: renderer mock coverage; not a substitute for native SQLite tests.
- `.github/workflows/desktop-windows.yml`: Windows S9 native/UI gates and artifact naming.
- `docs/S9_IMPLEMENTATION_DESIGN.md`, `S9_EXECUTION_REPORT.md`, `S9_INTEGRATION_HANDOFF.md`: S9-only documentation.

## IPC and data
`paper_workspace({request})` uses camelCase fields:
`action` = `list|candidates|create|rename|delete|reorder|attach|detach|representative|clearRepresentative|metadata`; `projectId` required.
`kind` = `paper|figure|panel` for structural operations, `parentId` for nested creates/reorder, `id` for target, `name` for create/rename, `orderIds` for exact sibling permutation, `versionId` (or atomic bulk `versionIds`) and `usage` (`display|statistics|source|other`) for association, `replicateId` for representative, `requestId` for mutations.
For `metadata` provide `{id,status,notes}`. `clearRepresentative` accepts `{id,replicateId}`. Representative update must reference a version already pinned to Panel. Response: `{papers:[{id,title,status,notes,figures:[{id,label,panels:[{id,label,assets,representatives}]}]}],candidates:[{assetVersionId,assetId,versionNumber,name,sha256,stage,assayId,groupId,replicateId}]}`.
Notes: `candidates` returns a capped set; production pagination/search must be evaluated. Paper status/notes have no S7 columns; S9 persists them in latest scoped `paper_metadata` audit event (append-only), so S8 integration must preserve or migrate that contract. Asset version IDs never inferred from display names.

## Schema and migration
No new schema or migration. Existing S7 v2 schema tables and S1 idempotent request/audit tables reused. S8 migration numbering untouched.

## S8 integration conflicts
The `packages/core/src/workbench.tsx`, `.github/workflows/desktop-windows.yml`, `docs/DATA_M_AI_TASKS.json`, and `docs/AI_DEV_EXECUTION_SPEC.md` files may overlap S8 edits. Integrate both changes by review, do not overwrite either side wholesale. S8's analyses remain independent of `paper_workspace`. Run both S8 and S9 targeted Rust/UI suites after merge, then full Windows Release and S0–S7, migrations upgrade/backup/restore, and sample-count invariants.

## Current evidence
PR #53 exists and is Draft. Windows [CI #44002053468](https://github.com/Devil-Jian/data-m/actions/runs/44002053468) verified code SHA `d61eb8349eba668071dd123e2452a2b61d933658`: 6 S9 native, 101 full Rust tests; Release EXE Artifact [14292513472](https://github.com/Devil-Jian/data-m/actions/runs/44002053468/artifacts/14292513472). **machine_passed / manual_pending**; Windows manual signoff remains pending. No unique/precious research data.