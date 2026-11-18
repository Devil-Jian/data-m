# S12 UI architecture and migration contract

Status: **in_progress**. Base: S11 `e7b12d7022ed40d33bccc42f9a628b305e91ccf9`. No Schema migration.

## Production boundaries

- Desktop root `apps/desktop/src/main.tsx` mounts `ProjectTreeManager` only. The previous `Workbench` and its `INITIAL_PROJECTS` remain present in the source tree solely for isolated demo/test use and are never mounted by the official desktop root.
- Project/Theme/Assay node identity comes from `projectTreeApi` (Tauri project_tree). Group/Replicate identity comes from `ExperimentTreeManager` (existing experiment API).
- Raw and Processed require a Replicate ID; Statistics and Display use an Assay ID. Production stages: `raw | processed | statistics | display`. Legacy demo `stats` has no production authority.
- Existing `asset-browser`, `asset-transfer`, `asset-lifecycle`, `import-jobs`, `statistical-analysis`, `paper-workspace`, `provenance-view`, and `repository-safety` retain their Rust IPC and repository rules.
- `AssetLinkId` identifies a selected placement. `AssetVersionId` identifies immutable content version; labels and filenames do not authorize modifications.

## Current migration map

| Area | Old source | Current S12 action | Follow-up |
| --- | --- | --- | --- |
| Main entry | Dual `Workbench` / `ProjectTreeManager` mode bar | Real manager alone, no demo toggle | Shared shell and deep links |
| External Windows drop | `inspect_drop_paths` preview | Removed obsolete “P2 unavailable” copy | Explicit safe import target UX |
| Project/Theme/Assay | `ProjectTreeManager` | Reuse existing real IPC | Shared breadcrumb + back/forward |
| Group/Replicate | `ExperimentTreeManager` | Retain existing real IPC | Unified sidebar |
| Files | `AssetBrowser` | Retain existing real IPC | Explorer grid/list/table and keyboard parity |
| Statistics/Paper/Safety | Existing panels | Retain real APIs | Common navigation/side panel |
| Demo test fixtures | `packages/core` | No longer rendered at production root | Isolated test/demo entry |

## Safety and testing

- No automatic file import on global OS drop; only authorized `prepare → start` with target confirmation.
- Drop preview does not mutate CAS, SQLite or source bytes.
- Changes should only be confirmed after successful IPC and fresh read.
- Never create Replicate records during link copy or move.
- Frozen Analysis is read-only; Paper Panel references exact `AssetVersionId`.
- Backups are local snapshots and isolated recovery, never in-place overwrite.
- Ordinary browser without Tauri must not silently display fake projects. Explicit test-only IPC mocks remain separate from real runtime.
- S12 requires nonzero `@S12` E2E and full legacy suites before `machine_passed`.
- Windows 11 native DPI/drag-drop/GUI performance remains `manual_pending` until user acceptance.

## Status

S12-00 audit and baseline contract: **partial**; formal screenshot and complete module contract matrix outstanding.
S12-01 root-only entry migration: **partial**; shared shell/navigation history and refresh persistence outstanding.
S12-02 through S12-06: **not completed**.

This is not a claim of full S12 acceptance.
