# S10 Execution Report — Windows CI passed; full stage acceptance incomplete

- Task: S10, exact I1 baseline `2d5c23d447a150599118cadd18cde1b90926945b`.
- Branch: `feat/s10-bidirectional-trace-export`. Draft PR: https://github.com/Devil-Jian/data-m/pull/56 ; base: I1 integration branch.
- Implemented initially: controlled `provenance_trace` read-only exact-version Rust IPC; S0 whitelist registration; preliminary S10 native test for forward/reverse pins and recycled historical links. These are **not** evidence of successful CI without run results.
- Initial source files: `apps/desktop/src-tauri/src/provenance_trace.rs`, `apps/desktop/src-tauri/src/lib.rs`, `scripts/s0-baseline.test.mjs`.
- G0-01 through G0-07: **not completed**. No claim of fully tested v3 backup/restore.
- S10 full 14-case matrix: **not completed**. Partial native coverage only.
- Figure Manifest and CAS lossless export: **implemented as a candidate**, including native IPC; machine tests passed on the precise SHA below, but complete S10 safety matrix is not yet demonstrated.
- Initial formal Paper workspace Panel provenance and exact-version reverse lookup UI is implemented using real Tauri IPC; full asset detail navigation, Figure export preview and progress/cancel remain **not implemented**.
- Added @S10 Playwright renderer IPC-mock test; updated Windows workflow to require nonzero s10_ Rust and @S10 Playwright execution. Previous CI run #44048439668 failed cargo fmt; applied the logged Rustfmt patch in commit 29466687f58c34d8c437da740d8d9a3c247a335f. A later successful CI has not been confirmed.
- Windows CI/Release EXE: **successful for the exact tested code SHA below**; this is not complete S10 stage acceptance.
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

## Verified successful Windows build (2026-11-17, supersedes older pending-build notes)

- **Exact source SHA actually tested:** `8dfb20fe82f82d3f184b4be20031526fbfdc7bdd`.
- **Windows Actions:** https://github.com/Devil-Jian/data-m/actions/runs/44059250671 — completed **SUCCESS**.
- **EXE test artifact:** https://github.com/Devil-Jian/data-m/actions/runs/44059250671/artifacts/14319090043 — `data-m-s10-unsigned-portable-windows`, ZIP size **4,318,334 bytes**, ZIP SHA-256 `59864fe66873593d5ae9df539b8e7cedd89c8a854cf210e0b121bbf70681df0d`. This is the **ZIP hash**, not a measured EXE hash.
- Native `cargo test --locked --no-default-features`: **114/114 passed**. `s10_` native: **4/4 passed**. All S0–S9/I1 existing selected suites ran in the Windows job.
- Desktop Playwright: **44/44 passed**. Windows Chromium: **29/29 passed**. The @S10 renderer tests and @S9/@I1 tests passed. Renderer tests explicitly mock native IPC; they do not prove Windows file picker operation.
- `cargo fmt --all -- --check`, S0 strict IPC contract, TS/Vite, desktop unit, `cargo check --locked`, Windows Release `npm run tauri:build`, EXE Artifact upload: **passed**.
- Actual failure-and-fix history: #44057446883 Figure publish failed `Access is denied` while a Manifest handle remained open. Fixed manifest handle release and Windows directory-handle sharing. #44058581679 failed 1/44 renderer tests due ambiguous 'Figure 1' locator following addition of export-select option; narrowed tree assertion and disambiguated option text. Final #44059250671 succeeded.
- Manifest v1 candidate contains exact pinned references, file SHA/size, byte dedup physical paths, historical analysis inclusion by exact version, all Figure Panels including empty Panels, representative source Replicate IDs, and relevant audit event IDs. These last additions have a dedicated Rust assertion in the 4/4 native suite.
- **Status: `in_progress / manual_pending`, not `machine_passed` or `accepted`.** CI/EXE success proves these checks for this SHA, but required G0-01–07 and S10-01–14 exhaustive matrix is not independently satisfied. Specific open gates: persistent requestId idempotency/restart reconciliation, interrupted staging orphan recovery, adversarial Windows reparse/TOCTOU and low-disk fault injection, full multi-project/Assay rollback and transition matrix, explicit handling of unproven Panel→Analysis causal links, user-driven Windows WebView2 safety rehearsal, and S7 real off-device isolated recovery drill.
- No changes to main or upstream I1/S8/S9, no automated merge, no formal release, and no precious/unique real research data tested.

## Final machine acceptance checkpoint — 2026-11-17 (authoritative; supersedes all prior "pending" statements)

**Task S10 is `machine_passed / manual_pending`, not `accepted`.** The complete machine matrix has been checked in as [S10_MACHINE_ACCEPTANCE_MATRIX.md](S10_MACHINE_ACCEPTANCE_MATRIX.md). This checkpoint overrides the provisional status recorded in the historical sections above.

- **Exact tested code SHA:** `420bcd4be18ef6b781a629f1b887c0c57611f2ee`; later commits on the S10 branch are documentation-only and must not be confused with the tested runtime SHA.
- **Windows Actions:** https://github.com/Devil-Jian/data-m/actions/runs/44094234909 — **SUCCESS**. UI-demo variants workflow also succeeded.
- **Rust tests:** `cargo test --locked --no-default-features` **121/121 passed**; filtered `s10_` **11/11 passed**, plus S0–S9/I1 filtered regression tests. `cargo fmt --all -- --check` and `cargo check --locked` succeeded.
- **Browser tests:** Windows Chromium E2E **29/29**. Desktop E2E had **45 tests: 44 first-pass, 1 flaky first attempt and passed on retry** (legacy Demo right-click context menu); the workflow was successful but this flaky test remains visible as a follow-up, not erased or misreported.
- **Unsigned portable Windows EXE:** Release built and uploaded successfully to https://github.com/Devil-Jian/data-m/actions/runs/44094234909/artifacts/14333783685 ; Artifact ZIP bytes **4,341,949**, ZIP SHA-256 **`2b2689150b6c3a088cf0144c92651b8352dba71c832afd4129e2f0936e3f6a22`** (this digest is for the downloadable ZIP, not the inner EXE).
- **G0-01–07 / S10-01–14:** each machine gate has named native-SQLite/CAS or explicitly mocked renderer coverage in the machine matrix. Key new evidence: historical v1/v2→v3 migration and invalid cross-Assay rollback; v3 backup/isolated restore and frozen analysis A=5/B=6; two Figures/six Panels/pinned multi-version trace; real exported CAS byte/SHA checks and physical dedup; durable request claim, cancel and guarded stale-stage cleanup; corrupt/missing Blob, symlink/reparse, forced write-failure and Windows destination rename attack tests. Shared exact versions do **not** invent a Panel→Analysis causal foreign key.
- **Artificial fixture limitations:** machine tests use disposable SQLite/CAS and renderer IPC mocks, not precious research data. This check does NOT constitute Windows 11 WebView2 native picker/drag-and-drop human testing, off-device S7 recovery with real user data, DPI/device validation, clean installer testing, or any formal user sign-off.
- **Remaining follow-up:** fix/monitor the flaky old Demo context-menu E2E during S11 QA; do not suppress it or misstate its first-attempt failure. S7–S10 human states remain **manual_pending**. S11 installer/10k/Windows real GUI acceptance remains a separate stage; no main merge or official v1.0 release.
- **S11 handoff:** [S11_DEVELOPMENT_EXECUTION_TASK.md](S11_DEVELOPMENT_EXECUTION_TASK.md). Create a separate S11 branch from the latest S10 HEAD once rechecked, and a Draft PR against S10; never modify S10 baseline from the S11 branch.
