# I1 integration handoff — S8 + S9

**Working branch:** `feat/i1-s8-s9-integration`; [Draft PR #54](https://github.com/Devil-Jian/data-m/pull/54) against S8. Tested executable source: `ac65c16d4304d9f90d81d09e328d0b4f8e6549fa`; [Windows success #44014564074](https://github.com/Devil-Jian/data-m/actions/runs/44014564074); [EXE artifact #14299186149](https://github.com/Devil-Jian/data-m/actions/runs/44014564074/artifacts/14299186149).

## Shared repository and schema
S8 v3 schema is the sole migration target; `0003_statistics.sql` is retained. Statistics: `statistical_analyses`, `analysis_samples`, inputs/results pin exact `asset_versions.id`, frozen revisions. S9: S7-existing `papers`, `figures`, `panels`, `panel_asset_versions`, `representative_sources`; paper status and notes are audited metadata. No schema v4.

## IPC and UI
The Tauri explicit invoke handler includes both `statistical_analysis({request})` and `paper_workspace({request})`. The core workbench retains its demo paper route, while the real native Project Manager has separate S8 statistics and S9 paper navigation. Each workspace reads the controlled database. The Paper workspace can return to Project Manager.

S9 `paper_workspace` actions: `list`, `candidates`, `create`, `rename`, `delete`, `reorder`, `attach`, `detach`, `representative`, `clearRepresentative`, `metadata`. Requests carry `projectId`; pins are exact immutable `AssetVersionId` and usage `display|statistics|source|other`. Candidate query is limited to 500 without server-side pagination; a visible warning appears when result count reaches 500.

## S10 readiness and known gaps
Cross-module analysis inputs/outputs and Panel links can be joined by exact immutable asset version identifier, but **full S10 bidirectional tracing, safe Figure Manifest and export are not implemented**. Finish required I1 combined native scenarios before tagging machine acceptance; current CI only establishes successful combined build and the listed narrower tests. Windows human safety acceptance remains pending.
