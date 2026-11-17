# I1 S8 + S9 integration execution report

TASK_ID: I1. Branch: `feat/i1-s8-s9-integration`. Draft PR: [#54](https://github.com/Devil-Jian/data-m/pull/54) against `feat/s8-statistics-sample-inclusion`.
S8 base: `8ef0609f2360962674544b76a834bbd4ff2de8e0`; S9 source: `14fd3a951d67aa92091ed8a10c989dfa2c08b71f`; shared S7 ancestor: `15e784a6bf939cf509254aacdea33bb120cf9165`.

## Confirmed Windows CI machine result

- **Exact tested code SHA**: `ac65c16d4304d9f90d81d09e328d0b4f8e6549fa`.
- **Windows Actions**: [run #44014564074](https://github.com/Devil-Jian/data-m/actions/runs/44014564074) — completed / SUCCESS.
- **EXE artifact**: [data-m-i1-unsigned-portable-windows, ID 14299186149](https://github.com/Devil-Jian/data-m/actions/runs/44014564074/artifacts/14299186149). ZIP size 4,199,654 bytes. Artifact ZIP SHA-256: `378d0052cde1f1637b7d1563e1b20ed8f289a377abf422ad0b94c03dd7f1e9f4`. This digest is for the Artifact archive, **not** the EXE itself.
- Native Rust: S9 6/6, S8 8/8, I1 1/1; all Rust 110/110.
- Playwright desktop: 42/42; Windows Chromium: 29/29; S9 @S9: 1/1, I1 @I1: 1/1; @S8: 3/3.
- Rustfmt, S0 baseline, TypeScript/Vite, interaction/unit checks, Windows desktop E2E, S0–S7 native and browser regressions, `cargo check --locked`, Windows Release `npm run tauri:build`, and Artifact upload all succeeded.
- Prior failures fixed: first malformed generated Windows YAML (run #44014233942); later Windows rustfmt failure (run #44014429426). Neither gate disabled.

## Actual integrated implementation

- Retained S8 migration `0003_statistics.sql` and schema v3, statistical analysis inclusion/freeze/revisions and S7 snapshot implementation.
- Added S9 Paper/Figure/Panel backend and pinned asset-version usage links without a new schema version.
- Registered both `statistical_analysis` and `paper_workspace` in `lib.rs` and the S0 explicit whitelist.
- Formal Project Manager now links S8 statistics and S9 paper workspace; switching out and back from both workspaces has @I1 browser coverage.
- The paper candidate list remains capped at 500 on the server; UI warns when 500 are loaded and clarifies that filters only cover loaded candidates.

## Machine gate status and unverified mandatory cases

**I1 status: `in_progress / manual_pending`, not `machine_passed` or `accepted`.** A successful CI/EXE is established, but required I1-01–I1-12 scenario matrix is *not* fully implemented as independently demonstrated native integration tests. In particular:
- A native test demonstrates v3 schema and persisted Paper together, but not the entire v2→v3 cross-module analysis+Paper read/write workflow.
- Missing explicit combined test for two Figures × three Panels × multiple pins each after reload.
- Missing integrated frozen analysis A/B n=6 inclusion 5/6 + Panel v1/v2 pin invariance in the same disposable repository.
- Missing fault-injection and cross-Project/Assay integrated batch rollback tests using both modules.
- Missing dedicated v3 S7 backup→verify→isolated restore with integrated analysis and Panel records and hash/count diff=0.
- Existing S8/S9 independent native tests prove narrower properties only; no implication of all I1 acceptance scenarios.

## Pending human and delivery requirements

- Windows 11 WebView2 human test, EXE SHA-256 evidence and real drag/drop interactions: NOT EXECUTED.
- S7 real backup/restore drill and off-device backup: NOT EXECUTED.
- Sensitive or unique research data may not be placed in this build until safety gates are accepted.
- No merges of original S8/S9 PRs or main. S10 full bidirectional tracing, Figure Manifest and safe lossless export remain outside I1.
- Documentation-only commits after the exact tested source SHA do not represent a new compiled build.
