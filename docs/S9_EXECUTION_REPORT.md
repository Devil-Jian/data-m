# S9 execution report — machine verified

**TASK_ID:** S9 — 论文组图与固定版本关联  
**Branch:** `feat/s9-paper-figure-panel`  
**Fixed S7 base:** `15e784a6bf939cf509254aacdea33bb120cf9165`  
**Draft PR:** [#53](https://github.com/Devil-Jian/data-m/pull/53), base `feat/s7-integrity-backup-restore`  
**Status:** `machine_passed / manual_pending` (not `accepted`). No merge. S8 remains independent.

## Verified Windows build
- **GitHub Actions Windows workflow:** [run #44002053468](https://github.com/Devil-Jian/data-m/actions/runs/44002053468)
- **Exact code SHA validated:** `d61eb8349eba668071dd123e2452a2b61d933658`.
- **Conclusion:** success; actual Windows release EXE built and artifact upload succeeded.
- **Portable EXE Artifact:** [data-m-s9-unsigned-portable-windows](https://github.com/Devil-Jian/data-m/actions/runs/44002053468/artifacts/14292513472), Artifact ID `14292513472`; uploaded ZIP size 4,183,862 bytes; unsigned executable.
- **Automated test evidence:** `cargo test --locked --no-default-features s9_`: 6 passed, 0 failed. Full native Rust suite: 101 passed, 0 failed. `@S9` Playwright: 1 passed. All S0–S7 targeted Rust and browser regression gates passed. Windows Chromium interaction E2E 29 passed; renderer full E2E 38 passed; TS/Vite build, cargo fmt and cargo check passed.
- This machine validation references the code SHA above. Subsequent documentation-only commits do not change the executable source; manual Windows acceptance remains unperformed.

## Implemented
- Paper CRUD (create/list/read/update title/soft archive empty Paper), status and notes persisted as latest `paper_metadata` audit event.
- Ordered Figure CRUD and Panel CRUD; exact-membership validation prevents partial/invalid reordering. Nonempty referenced Panel and Figure deletion protected.
- Multiple per-Panel fixed `AssetVersionId` links using usages `display`, `statistics`, `source`, `other`; multi-select is a single transaction, duplicate registration refused, explicit unlink never deletes files or blobs.
- Server validates Paper Project and file/experiment Project ownership, version existence, biological replicate provenance, and existing Panel membership before selecting representative source; referenced representative versions protected against last-link detach.
- `paper_workspace` Tauri IPC integrates with formal React paper workspace, not just demo fixtures. Left paper/figure/panel tree, middle pinned version metadata/usage/hash and representative sources, right scoped searchable file candidate selection; browser UI uses IPC rather than manual version ID entry.
- Request replay protection through existing `s1_requests`, mutation audit through `audit_events`, SQLite IMMEDIATE transactions and existing foreign keys/immutability triggers.
- Existing S7 tables reused: `papers`, `figures`, `panels`, `panel_asset_versions`, `representative_sources`, `asset_versions`, `asset_links`, `data_assets`, `assays`, `replicates`. **No migration** and no S8 API dependency.
- All changes isolated to `feat/s9-paper-figure-panel`. Windows workflow correction rebuilt from S7 baseline after diagnosing malformed generated YAML; exact Rust module order corrected from Windows rustfmt evidence.

## Automated testing and remaining risks
- Six native `s9_` test functions cover persisted structure/version pin, project isolation/duplicate rejection, transactional ordering/invalid ordering rollback, atomic bulk attach and representative reference protection, link rename/copy with stable pin and unchanged n, and metadata persistence.
- @S9 browser Playwright uses explicitly mocked frontend IPC; **not** proof of running Tauri IPC in real desktop GUI. Native SQLite tests separately validate actual repository.
- Browser candidate query is capped at 500 and lacks complete server-backed pagination/hierarchical browsing; large research projects require enhancement.
- Paper status/notes reuse the existing append-only audit table rather than adding S9 migration columns; may require integration refactor when S8 merges.
- Tests do not independently cover every requested S9-01 through S9-08 permutation (e.g. exact 2 Figures × 3 Panels × multiple versions, fault-injection mid-sort, full source recycle/physical deletion under all usage combinations). They do verify the principal fixed-version and sample-count invariants.
- S8 and S9 UI entry/workflow/task documents must be integrated by review. Avoid overwriting S8 changes or independently choosing a migration number.

## Windows manual acceptance — pending
- [ ] Download exact successful EXE Artifact above; record EXE SHA-256, Windows version and screenshots.
- [ ] Create one Paper, two Figures and three Panels each; bind multiple display/statistics/source versions, restart and re-open.
- [ ] Add v2 while Panel pins v1, and verify no silent upgrade.
- [ ] Rename/move/copy linked asset and verify pinned version plus biological replicate count remain unchanged.
- [ ] Validate illegal cross-Project/replicate relationships, duplicate registration, conflicting reorder and explicit unlink.
- [ ] Confirm protected source version cannot silently disappear; verify S7 backup/restore in disposable test repository.
- [ ] Integrate S8+S9 under a dedicated integration branch, verify schema upgrades/CI again; do not merge main without explicit authorization.

**Machine conclusion:** S9 `machine_passed / manual_pending`. Human acceptance was explicitly deferred by the user and is not asserted.
