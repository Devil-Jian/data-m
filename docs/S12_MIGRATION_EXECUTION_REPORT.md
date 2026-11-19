# S12 migration execution report

Task: `S12-UNIFIED-WORKSPACE`
Branch: `feat/s12-unified-workspace`
Base: `feat/s11-windows-performance-installer` @ `e7b12d7022ed40d33bccc42f9a628b305e91ccf9`
Draft PR: https://github.com/Devil-Jian/data-m/pull/59

## Verified changes

- S12-00 **partial**: Audited S11 PR #57, root, project-tree implementation, desktop package and Windows workflow; created `docs/S12_UI_ARCHITECTURE.md`. Full UI screenshots and event matrix pending.
- S12-01 **partial**: Sole real desktop entry, accurate import warning, real-ID project breadcrumbs, bounded back/forward history and deleted-ID filtering. Last-valid-node preference now restores on reload only if live project tree confirms the ID; cross-module deep links remain pending. Added explicit @S12 renderer mocks for navigation.
- S12-02 **partial**: Added grid/list/table to existing real AssetBrowser, preserving page size, `AssetLinkId` selection and server-side listing. Full keyboard parity, scope/error tests and native UI evidence outstanding.
- S12-03 **partial**: Existing real import/transfer/lifecycle APIs remain wired. Shared module sidebar now rejects navigation during experiment mutations and import lock; S2/S3 tests assert the lock. Unified long-task drawer and full safety E2E remain pending.
- S12-04 **partial**: Kept real project navigation sidebar visible when entering Experiment, Statistics, Paper and RepositorySafety modules, without changing their IPC. Full shared details layout and deep linking remain pending.
- S12-05 **partial**: Demo no longer mounted in official desktop root. Demo source and legacy regression fixtures retained. Updated README with real workspace, four stages and Windows NSIS candidate process; final user-manual screenshots pending.
- S12-06 **in progress**: Actual S12 commit `166e1bde727a763d69d41bde6298327310f70978` ran Windows CI `44308671524` (failed at legacy S0 canonical-workbench-import assertion); UI CI `44308671488` succeeded. S0 regression assertion updated for the S12 real-manager boundary. A subsequent Windows run `44311352353` passed S0 source checks and early Rust smoke but failed old `@S0` browser tests asserting the removed demo ROS project. The original demo Explorer keyboard/menu test has been preserved in `tests/e2e/demo-legacy-workbench.spec.ts`, while `desktop-preview.spec.ts` checks the live desktop entry. S1–S8, S10, S11 and I1 tests that relied on removed mode buttons were migrated; S11 nested Demo menu coverage moved into `tests/e2e/s11-demo-context.spec.ts`. Latest code SHA still requires full green CI.

## File modifications

- `apps/desktop/src/main.tsx`: production root, remove demo switch and obsolete P2 copy.
- `tests/desktop-ui/s12-unified-entry.spec.ts`: explicit @S12 renderer smoke (mock/no native GUI guarantees).
- `.github/workflows/desktop-windows.yml`: @S12 test gate.
- `apps/desktop/src/asset-browser.tsx` and `.css`: three display modes over existing bounded real-asset result.
- `apps/desktop/src/project-tree.tsx` and `.css`: ID breadcrumbs, keyboard history and persistent project navigation during modules.
- `apps/desktop/src/experiment-tree.tsx`: key real-asset view by exact assay/stage/replicate scope.
- `scripts/s0-baseline.test.mjs`, `tests/desktop-ui/desktop-preview.spec.ts` and S1–S8, S10, S11, I1 renderer tests: align entry assumptions with true unified desktop (retain functional and data API assertions).
- `tests/e2e/demo-legacy-workbench.spec.ts`, `tests/e2e/s11-demo-context.spec.ts`: preserve original demo-only right-click, F2, copy and nested-menu tests against the isolated shadcn demo instead of injecting fake demo data into the production root.
- `README.md`: correct obsolete S1-only, no-NSIS and demo-first guidance.
- `docs/S12_UI_ARCHITECTURE.md`: S12 data and navigation boundaries.
- `docs/S12_MIGRATION_EXECUTION_REPORT.md`: this report.
- `docs/S12_MACHINE_ACCEPTANCE_MATRIX.md`: evidence matrix.

## Machine evidence

Head of first @S12 test/workflow commit: `87ad1965ddd32085f07546578e35b267363e542c`.
Verified after prior observation: UI run https://github.com/Devil-Jian/data-m/actions/runs/44308671488 succeeded. Windows run https://github.com/Devil-Jian/data-m/actions/runs/44308671524 failed at S0 source contract before the full suite; root cause was old expectation that the desktop itself imports `workbench-ui` rather than indirectly via the real `ProjectTreeManager`. That source contract test was updated. The next Windows run `44311352353` passed S0 source checks but failed `@S0` renderer assumptions still expecting demo assets; these have now been migrated to real desktop checks and isolated demo tests. Windows run `44313726030` subsequently passed S0 and 47/48 desktop tests, failing one unconverted S1 unavailable-mode button action; it has now been corrected. Latest-head rerun remains required. A later S12 baseline SHA `f72e67f...` **did** pass Windows install/portable EXE and complete CI (see follow-on section). No newer follow-on head success is claimed until its own CI completes. The full S0–S11 regressions may need updates because existing tests expected the old demo as default; do not disable tests to hide failures.

## S12 follow-on interaction implementation (2026-11-19)

Changes on the **same S12 Draft PR #59**, after previously green Windows build `f72e67fdb58f6a45c238c61319961032051a1e3e`:

- **S12-02 actual Explorer:** bounded page-only rubber-band selection (including Ctrl additive mode), keyboard context menu via `Shift+F10` / Menu, up/down menu focus, Escape restore to the selected real asset, outside-click dismissal, viewport-bound menu placement. F2/Delete still enter the existing **real S6 confirmation and lifecycle IPC**, never delete CAS bytes. Current-page `AssetLinkId` identity, backend paging and disabled actions during transfer preserved.
- **S12-03 genuine task summary:** `apps/desktop/src/task-tray.tsx` is a compact, optional read-only progress tray. Only validated import responses, S7 operations/progress and S10 native export confirmation publish statuses. Prepared is not completed, cancellation requests are not final cancellation, and failed/uncertain calls never display complete. Existing cancel/retry remains inside each source module; task tray cannot issue persistence operations. Long tasks disable project navigation; tests cover S3 prepared/completed and S7 sealed-snapshot reporting.
- **S12-04 common navigation:** Project/Theme/Assay live-ID trail is visible in Experiment, Analysis, Paper, and RepositorySafety modules; alt-arrow history leaving one module does not keep stale content. S7/S8/S9 busy state blocks cross-module navigation. Paper initial project context and its project selector synchronize real Project IDs with the shared sidebar.
- **S12-05 guidance:** `docs/S12_USER_WORKFLOW.md` now describes four-stage scopes, keyboard/marquee selection, S3 safe prepare/start, S5 transfer, S6 recovery, task status semantics, pinned version export and isolated recovery.
- **S12-06 new E2E:** augmented `@S12` / S4 real asset Explorer context/F2/Delete tests, S3 and S7 progress-tray checks, Paper real project-switch test. Browser tests explicitly inject Mock/IPC fixtures and do not claim native WebView2 evidence.

**Prior fully passing exact-SHA machine baseline (before this follow-on):**

- Windows https://github.com/Devil-Jian/data-m/actions/runs/44315421546 — `completed/success`, Rust suite, S0–S12 renderer CI, 10,024 real SQLite/CAS asset benchmark, NSIS + portable EXE, release SHA verification and artifact upload.
- UI https://github.com/Devil-Jian/data-m/actions/runs/44315421442 — `completed/success`.
- Baseline SHA: `f72e67fdb58f6a45c238c61319961032051a1e3e`.
- **Those green runs do not validate the additional commits above.** A final green Windows/UI run and same-SHA artifact are required before recording the new S12 head as `machine_passed`.

Remaining evidence: compare screenshots from genuine Windows WebView2, actual Windows drag gestures and native 10k GUI performance, full disposable-data closed-loop manual verification (never assume completed). No SQLite/CAS schema change or `main` merge.

## Safety / acceptance

No SQLite migration or CAS modification. No sample data import. No main merge.
Machine status: `in_progress` (not `machine_passed`).
Human status: `manual_pending`.
Windows 11 manual checklist remains the source S12 task document §7.2.

## Follow-up machine triage (2026-11-18)

- [Windows #44313726030](https://github.com/Devil-Jian/data-m/actions/runs/44313726030) failed after 47 desktop UI tests passed, 1 failed; the final failing S1 test had an obsolete `项目管理` mode click. No actual SQLite/CAS regression was demonstrated by that failure, but the run is **not passed**. Removed the stale click, keeping unavailable-backend assertions.
- The latest code also adds ID-validated last project selection from optional local preference. Project tree always re-queries real backend; a stale persisted ID is discarded rather than creating a phantom project. New @S12 E2E covers reload and stale ID.
- Following this commit a new Windows and UI run must finish before any `machine_passed` declaration. The Windows installer / executable SHA and native GUI manual checklist remain pending.
