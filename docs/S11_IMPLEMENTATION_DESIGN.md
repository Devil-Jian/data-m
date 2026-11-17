# Data-M S11 Implementation Design

## Baseline and compatibility
- The independent S11 branch derives from S10 HEAD ad623adac7b9b632f3f09725ac63b1a7725953b4, following the machine-approved S10 code at 420bcd4be18ef6b781a629f1b887c0c57611f2ee. S10 remains manual_pending. No main merge or upstream branch edits.
- Existing native architecture: Tauri 2 Rust, app_local_data_dir outside the installation directory, SQLite schema v1/v2/v3, strict owned-schema recognition with v1→v2→v3 transactional upgrade. S3 durable import jobs and recovery, S7 immutable backed-up snapshots and isolated restore, S10 Figure staged export with SHA-256 checks and cancellation.
- Asset list uses a bounded 50-row UI page, production SQLite query with OFFSET cursor, stable sort and server-side name filtering. No unsupported schema v4 or silent database changes.

## S11 actual candidate changes
- New Rust s11_ test writes 10,024 distinct physical CAS objects, registers one Blob/Asset/Version/Link per object, three Assays, 36 Replicates, frozen analysis, pinned Panel references, Chinese/Unicode and long names and soft deletion. Checks real production list_at SQL, bounded pagination across projects, SHA validation, query p50/p95/p99 and Windows process memory/CPU counters. This is not a native GUI first-paint measurement.
- Renderer @S11 tests exercise the formal project workspace against an explicitly marked mock of 10,024 rows, bounded page selection, search and project navigation. The mock is not native database benchmark evidence.
- Correct nested context-menu routing for right-clicks on action buttons inside data-explorer-id rows without allowing toolbar buttons to trigger an asset menu.
- NSIS current-user unsigned candidate installation bundle, retaining portable EXE. One manifest script computes and verifies separate SHA-256 for both outputs. The version stays 0.1.0, not signed official v1.0.
- Application data must remain under Tauri LocalAppData storage, never under executable bundle. No new uninstall-delete, CAS GC, silent downmigration, path-based asset overwrite or unsafe snapshot recovery is introduced.

## Threat model and limitations
- Unknown SQLite schema or future version: reject before mutation. AssetVersion and Blob immutable; historical Panel and frozen Analysis references unchanged.
- Missing/tampered CAS object or export failure: fail closed; no partial publish. Existing S3/S7/S10 tests remain mandatory and are not skipped.
- App install/uninstall: candidate config must not add data-directory cleanup; VM installation/upgrade/rollback/uninstall behavior still requires Windows 11 human/VM evidence.
- CPU and memory from Rust test are process-level cumulative/peak measurements. WebView2 event-loop stalls, first meaningful paint, native import concurrency and native dialogs cannot be inferred from Playwright renderer mocks.
