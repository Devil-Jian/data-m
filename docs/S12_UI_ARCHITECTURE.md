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
| External Windows drop | `inspect_drop_paths` preview | Read-only preview, explicit S3 `prepare → start` with legal target; task tray receives verified status | Real Windows drop manual sign-off |
| Project/Theme/Assay | `ProjectTreeManager` | Reuse existing real IPC | Shared breadcrumb + back/forward |
| Group/Replicate | `ExperimentTreeManager` | Retain existing real IPC; lock during import; show sample stage scope | Actual GUI drag/target integration acceptance |
| Files | `AssetBrowser` | 50-row SQLite page, grid/list/table, bounded marquee, real-ID multi-select, keyboard context, F2/Delete, app clipboard | Native GUI gestures and real-data manual sign-off |
| Statistics/Paper/Safety | Existing panels | Retain real APIs; live project sidebar + trail; shared busy guard; Paper project ID sync | End-to-end native GUI loop |
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

## Shared task status

The desktop root mounts a compact read-only `TaskTray`. S3 validated `ImportJob`, S7 validated safety progress/response and S10 `figure_export` completion are its only state sources. It does not issue writes, synthesize progress, persist an independent task database, or turn cancel requests into final cancellations. On restart the user returns to the real native module for authoritative status.

## Status

S12-00 audit and baseline contract: **partial**; formal screenshot and complete module contract matrix outstanding.
S12-01 **partial**: real-ID project breadcrumbs/history added, deleted node guarded; persistence and integrated deep links pending.
S12-02 **implemented / latest-head CI pending**: bounded real-asset grid/list/table and remount on scope change; marquee, context-menu keyboard/F2/Delete and transfer locks.
S12-03 **implemented / latest-head CI pending**: shared real-status task tray plus import, safety, Figure export adapters and cross-module write locks.
S12-04 **implemented / latest-head CI pending**: project tree + real breadcrumb remain visible during Experiment/Statistics/Paper/Safety panels, Paper project selector sync.
S12-05 **implemented / screenshots pending**: README/user handbook rewritten; demo remains isolated.
S12-06 **new-head CI pending**: prior S12 baseline SHA f72e67... passed entire Windows/UI build and artifact uploads. Follow-on interaction commits require a new full same-SHA run; manual Windows 11 GUI remains pending.

This is not a claim of full S12 acceptance.
