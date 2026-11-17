# Data-M S11 Benchmark Report

**Status: pending real S11 CI verification. Do not substitute the Demo 10k list for native results.**

- Fixture: 10,024 physically materialized SHA-256 CAS files and matching SQLite Blob/DataAsset/AssetVersion/AssetLink rows in a disposable repository, three Assays and 36 biological Replicates, paper/Panel pins, frozen Analysis, Unicode/long filenames and selected soft deletions.
- Native path: identical asset_browser::list_at/get_asset query SQL exposed through Tauri list_assets/get_asset. Three Assays traversed in 50-row cursor pages with full independent CAS hash verification.
- Search benchmark: 40 queries, Windows Runner cold first native list call and p50/p95/p99 measured in milliseconds. Gate native first list <=3000ms, native search p95 <=2000ms. First WebView2 paint <=3s and >500ms main-thread stalls while importing are not measured by this Rust SQL benchmark.
- Runtime: windows-latest image; exact hardware/OS revision and cache details **to record from CI**. Windows peak working set bytes and CPU milliseconds are captured from the test process. Log record prefix: S11_METRICS.
- **Measured data: pending successful Windows Actions run**. CI benchmark artifact and exact tested SHA, run link, cold/warm timings and remaining risk must be appended after completion.
