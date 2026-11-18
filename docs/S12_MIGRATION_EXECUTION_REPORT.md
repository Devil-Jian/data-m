# S12 migration execution report

Task: `S12-UNIFIED-WORKSPACE`
Branch: `feat/s12-unified-workspace`
Base: `feat/s11-windows-performance-installer` @ `e7b12d7022ed40d33bccc42f9a628b305e91ccf9`
Draft PR: https://github.com/Devil-Jian/data-m/pull/59

## Verified changes

- S12-00 **partial**: Audited S11 PR #57, root, project-tree implementation, desktop package and Windows workflow; created `docs/S12_UI_ARCHITECTURE.md`. Full UI screenshots and event matrix pending.
- S12-01 **partial**: Sole real desktop entry, accurate import warning, real-ID project breadcrumbs, bounded back/forward history and deleted-ID filtering. Cross-module deep-link persistence remains pending. Added explicit @S12 renderer mocks for navigation.
- S12-02 **partial**: Added grid/list/table to existing real AssetBrowser, preserving page size, `AssetLinkId` selection and server-side listing. Full keyboard parity, scope/error tests and native UI evidence outstanding.
- S12-03 **not done**: Integrated import/transfer/lifecycle task UI.
- S12-04 **partial**: Kept real project navigation sidebar visible when entering Experiment, Statistics, Paper and RepositorySafety modules, without changing their IPC. Full shared details layout and deep linking remain pending.
- S12-05 **partial**: Demo no longer mounted in official desktop root. Demo source and legacy regression fixtures retained. Updated README with real workspace, four stages and Windows NSIS candidate process; final user-manual screenshots pending.
- S12-06 **in progress**: Actual S12 commit `166e1bde727a763d69d41bde6298327310f70978` ran Windows CI `44308671524` (failed at legacy S0 canonical-workbench-import assertion); UI CI `44308671488` succeeded. S0 regression assertion updated for the S12 real-manager boundary; old S1/S2/S3 mode-toggle tests adapted without deleting functional assertions. Latest SHA CI still pending.

## File modifications

- `apps/desktop/src/main.tsx`: production root, remove demo switch and obsolete P2 copy.
- `tests/desktop-ui/s12-unified-entry.spec.ts`: explicit @S12 renderer smoke (mock/no native GUI guarantees).
- `.github/workflows/desktop-windows.yml`: @S12 test gate.
- `apps/desktop/src/asset-browser.tsx` and `.css`: three display modes over existing bounded real-asset result.
- `apps/desktop/src/project-tree.tsx` and `.css`: ID breadcrumbs, keyboard history and persistent project navigation during modules.
- `apps/desktop/src/experiment-tree.tsx`: key real-asset view by exact assay/stage/replicate scope.
- `scripts/s0-baseline.test.mjs` and S1/S2/S3 renderer tests: align entry assumptions with true unified desktop (retain API regression assertions).
- `README.md`: correct obsolete S1-only, no-NSIS and demo-first guidance.
- `docs/S12_UI_ARCHITECTURE.md`: S12 data and navigation boundaries.
- `docs/S12_MIGRATION_EXECUTION_REPORT.md`: this report.
- `docs/S12_MACHINE_ACCEPTANCE_MATRIX.md`: evidence matrix.

## Machine evidence

Head of first @S12 test/workflow commit: `87ad1965ddd32085f07546578e35b267363e542c`.
Verified after prior observation: UI run https://github.com/Devil-Jian/data-m/actions/runs/44308671488 succeeded. Windows run https://github.com/Devil-Jian/data-m/actions/runs/44308671524 failed at S0 source contract before the full suite; root cause was old expectation that the desktop itself imports `workbench-ui` rather than indirectly via the real `ProjectTreeManager`. That test is updated, and a new run on the latest head is still required. No successful S12 Windows build, installer, portable EXE, release SHA manifest, full regressions or native GUI pass is claimed. The full S0–S11 regressions may need updates because existing tests expected the old demo as default; do not disable tests to hide failures.

## Safety / acceptance

No SQLite migration or CAS modification. No sample data import. No main merge.
Machine status: `in_progress` (not `machine_passed`).
Human status: `manual_pending`.
Windows 11 manual checklist remains the source S12 task document §7.2.
