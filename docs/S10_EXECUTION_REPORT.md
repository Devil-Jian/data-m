# S10 Execution Report — in progress

- Task: S10, exact I1 baseline `2d5c23d447a150599118cadd18cde1b90926945b`.
- Branch: `feat/s10-bidirectional-trace-export`. Draft PR: https://github.com/Devil-Jian/data-m/pull/56 ; base: I1 integration branch.
- Implemented initially: controlled `provenance_trace` read-only exact-version Rust IPC; S0 whitelist registration; preliminary S10 native test for forward/reverse pins and recycled historical links. These are **not** evidence of successful CI without run results.
- Initial source files: `apps/desktop/src-tauri/src/provenance_trace.rs`, `apps/desktop/src-tauri/src/lib.rs`, `scripts/s0-baseline.test.mjs`.
- G0-01 through G0-07: **not completed**. No claim of fully tested v3 backup/restore.
- S10 full 14-case matrix: **not completed**. Partial native coverage only.
- Figure Manifest and CAS lossless export: **not implemented**.
- Initial formal Paper workspace Panel provenance and exact-version reverse lookup UI is implemented using real Tauri IPC; full asset detail navigation, Figure export preview and progress/cancel remain **not implemented**.
- Added @S10 Playwright renderer IPC-mock test; updated Windows workflow to require nonzero s10_ Rust and @S10 Playwright execution. Previous CI run #44048439668 failed cargo fmt; applied the logged Rustfmt patch in commit 29466687f58c34d8c437da740d8d9a3c247a335f. A later successful CI has not been confirmed.
- Windows CI/Release EXE: **pending verification**, no S10 EXE Artifact can be claimed.
- Windows manual acceptance, S7 off-device backup/isolated restore on real Windows: **not performed**.
- Safety: user original data was not accessed or modified. I1/S8/S9 upstream branches and main were not modified.
- Task state remains `in_progress / manual_pending` (machine gates outstanding).
- Before S11, finish S10 export, UI, G0 and complete Windows CI with exact tested source SHA.

## Additional code committed (verification pending)
- `figure_export.rs`: test-accessible exact CAS read-only Figure export core with unique staged folder, SHA/size checks, manifest logical references, byte dedup and fail-closed corruption handling. **Not an authorized native picker / production IPC; do not present as working user-facing export.**
- `repository_safety/tests.rs`: new disposable SQLite/CAS G0 combined v3 backup → verify → isolated restore test with two Figures, six Panels, frozen 5/6 analyses, and reverse trace count.
- `paper_workspace.rs`: server-side candidates search/paging with real total, plus native 605-row regression; formal Paper UI migrated to use that API.
- All new test results **pending**; there is no asserted G0 acceptance and no S10 successful Windows Release EXE evidence.

## Windows CI diagnostics through 2026-11-17
- #44051935200: failed `cargo fmt --check`; 18 hunks from real Windows job applied to `figure_export.rs`, `paper_workspace.rs`, `repository_safety/tests.rs` in subsequent commits.
- #44052241943: Rust fmt, S8/S9/S10 renderer E2E passed; failed G0 combined native test because restore label `combined-v3` began with forbidden Windows device prefix `com`. Fixed test label to `safe-v3-restore` and restricted name to `s10_` (instead of also matching `i1_`). All downstream Rust tests and EXE build were not reached in that run.
- Added formal asset detail reverse lookup with project ancestry validation and separate `@S10` renderer mock regression. Neither proves actual native runtime.
- Latest complete S10 CI remains pending: do NOT label `machine_passed` or supply unsigned EXE artifact URL from failed runs.

## Candidate native export and UI (not machine accepted)
- Added native Windows Shell folder picker inside Rust; the UI never passes an arbitrary target string. Rust export now has `preview`/`export`/cancel, progress events, streaming cancel checkpoints and reuse of S3 Windows parent-directory handles.
- Added Figure export preview/confirmation/progress/cancel/success-only result view to the formal S9 Paper workspace. Added explicitly mocked `@S10` failure path regression.
- Extended native exporter test for v2 vs pinned v1, incorrect Project, disallowed target inside repository, relative path, cancellation without visible partial package, and same-size corrupt CAS.
- Windows CI/Release EXE for these additions: **not yet confirmed**. Full G0/S10 fourteen-scenario matrix, persistent request idempotency and interrupted-job recovery remain outstanding.
