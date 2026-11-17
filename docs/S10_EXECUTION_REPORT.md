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
