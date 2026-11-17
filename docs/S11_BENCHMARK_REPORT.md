# Data-M S11 Benchmark Report

**Status: pending real S11 CI verification. Do not substitute the Demo 10k list for native results.**

- Fixture: 10,024 physically materialized SHA-256 CAS files and matching SQLite Blob/DataAsset/AssetVersion/AssetLink rows in a disposable repository, three Assays and 36 biological Replicates, paper/Panel pins, frozen Analysis, Unicode/long filenames and selected soft deletions.
- Native path: identical asset_browser::list_at/get_asset query SQL exposed through Tauri list_assets/get_asset. Three Assays traversed in 50-row cursor pages with full independent CAS hash verification.
- Search benchmark: 40 queries, Windows Runner cold first native list call and p50/p95/p99 measured in milliseconds. Gate native first list <=3000ms, native search p95 <=2000ms. First WebView2 paint <=3s and >500ms main-thread stalls while importing are not measured by this Rust SQL benchmark.
- Runtime: windows-latest image; exact hardware/OS revision and cache details **to record from CI**. Windows peak working set bytes and CPU milliseconds are captured from the test process. Log record prefix: S11_METRICS.
- **Measured data: pending successful Windows Actions run**. CI benchmark artifact and exact tested SHA, run link, cold/warm timings and remaining risk must be appended after completion.

## Preliminary CI #44119846920 — failed full-suite run (not acceptance)
- Exact tested candidate SHA: 427e0f20273d9abda18e2330e43a4b29931bce68.
- Dedicated native benchmark: cargo test s11_ -- --nocapture --test-threads=1 passed, 2 tests. Physical 10,024 CAS objects; 10,014 active links after documented tombstones. fixture_ms=7831, first_query_ms=57, search_p50_ms=33, search_p95_ms=63, search_p99_ms=65; process peak working-set 17,539,072 bytes; cumulative CPU time 21,500 ms. Native Rust/SQLite only, not WebView2.
- Full default parallel Rust run: 122 passed, 1 failed (same S11 performance test), first_query_ms=139, search_p50_ms=166, search_p95_ms=2449, search_p99_ms=3980, peak working set 29,589,504 bytes, CPU time 49,015 ms. Numerous unrelated native integrity/recovery tests ran in parallel and interfered with fixture IO/CPU. This is recorded as a genuine observed contention result, not discarded or represented as passing.
- Corrective action: run full suite with --test-threads=1 (all tests executed, none skipped), and keep a separately threshold-gated dedicated s11_ benchmark in serial. This distinguishes controlled-query latency from synthetic CI-suite contention. Concurrent real background import plus WebView2 UI latency remains unverified and must not be claimed as passed.
