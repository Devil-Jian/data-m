//! S11 disposable, physically materialized CAS + SQLite fixture.
//! This exercises the *same* list_at/detail_at SQL invoked by Tauri list_assets.
//! It is not a WebView2 first-paint benchmark or a substitute for GUI acceptance.
use super::*;
use crate::project_tree::with_database_at;
use rusqlite::params;
use sha2::{Digest, Sha256};
use std::collections::HashSet;
use std::fs;
use std::time::{Duration, Instant};

const COUNT: usize = 10_024;

fn make_fixture() -> (tempfile::TempDir, std::path::PathBuf, Vec<String>) {
    let dir = tempfile::tempdir().unwrap();
    let live = dir.path().join("throwaway-repository");
    let db = live.join("project-tree.sqlite");
    let objects = live.join("cas").join("objects");
    fs::create_dir_all(&objects).unwrap();
    let mut hashes = Vec::with_capacity(COUNT);
    with_database_at(&db, |connection| {
        let tx = connection.transaction()?;
        tx.execute("INSERT INTO projects(id,name) VALUES('p','S11 disposable')", [])?;
        tx.execute("INSERT INTO themes(id,project_id,name) VALUES('t','p','stress')", [])?;
        for a in 0..3 {
            tx.execute(
                "INSERT INTO assays(id,theme_id,name) VALUES(?1,'t',?2)",
                params![format!("a{a}"), format!("实验 {a}")],
            )?;
            for g in 0..2 {
                tx.execute(
                    "INSERT INTO experiment_groups(id,assay_id,name) VALUES(?1,?2,?3)",
                    params![format!("g{a}{g}"), format!("a{a}"), format!("group {g}")],
                )?;
                for r in 0..6 {
                    tx.execute(
                        "INSERT INTO replicates(id,group_id,label) VALUES(?1,?2,?3)",
                        params![format!("r{a}{g}{r}"), format!("g{a}{g}"), format!("R{r}")],
                    )?;
                }
            }
        }
        tx.execute_batch(
            "INSERT INTO papers(id,project_id,title) VALUES('paper','p','S11 paper');
             INSERT INTO figures(id,paper_id,label) VALUES('fig','paper','Figure 1');
             INSERT INTO panels(id,figure_id,label) VALUES('panel','fig','A');
             INSERT INTO statistical_analyses(id,assay_id,name) VALUES('analysis','a0','Frozen');",
        )?;
        for i in 0..COUNT {
            let id = format!("{i:05}");
            let source = format!("S11 fixture {id} immutable original bytes");
            let hash = format!("{:x}", Sha256::digest(source.as_bytes()));
            fs::write(objects.join(&hash), source.as_bytes()).unwrap();
            let name = if i % 131 == 0 {
                format!("中文_科研样本_{id}_🧪.tif")
            } else if i % 137 == 0 {
                format!("科研样本_{id}_{}.tif", "长".repeat(80))
            } else {
                format!("科研样本_{id}_raw.tif")
            };
            let a = i % 3;
            let g = (i / 3) % 2;
            let r = (i / 6) % 6;
            let stage = ["raw", "processed", "statistics", "display"][i % 4];
            let group = if i % 4 < 2 {
                Some(format!("g{a}{g}"))
            } else {
                None
            };
            let sample = if group.is_some() {
                Some(format!("r{a}{g}{r}"))
            } else {
                None
            };
            tx.execute(
                "INSERT INTO blobs(sha256,size_bytes) VALUES(?1,?2)",
                params![hash, source.len() as i64],
            )?;
            tx.execute(
                "INSERT INTO data_assets(id,name) VALUES(?1,?2)",
                params![format!("asset{id}"), name],
            )?;
            tx.execute(
                "INSERT INTO asset_versions(id,asset_id,version_number,blob_sha256,source_name)
                 VALUES(?1,?2,1,?3,?4)",
                params![format!("v{id}"), format!("asset{id}"), hash, name],
            )?;
            tx.execute(
                "INSERT INTO asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name,deleted_at)
                 VALUES(?1,?2,?3,?4,?5,?6,?7,?8)",
                params![
                    format!("l{id}"),
                    format!("asset{id}"),
                    format!("a{a}"),
                    group,
                    sample,
                    stage,
                    name,
                    if i > 0 && i % 997 == 0 { Some("2026-01-01") } else { None }
                ],
            )?;
            hashes.push(hash);
        }
        tx.execute(
            "INSERT INTO panel_asset_versions(panel_id,asset_version_id,usage)
             VALUES('panel','v00000','display'),('panel','v00003','source')",
            [],
        )?;
        tx.execute(
            "INSERT INTO analysis_samples(analysis_id,replicate_id,included)
             VALUES('analysis','r000',1)",
            [],
        )?;
        tx.execute(
            "INSERT INTO analysis_asset_versions(analysis_id,asset_version_id,role)
             VALUES('analysis','v00000','input')",
            [],
        )?;
        tx.execute(
            "UPDATE statistical_analyses SET frozen_at='2026-11-17T00:00:00Z'
             WHERE id='analysis'",
            [],
        )?;
        tx.commit()?;
        Ok(())
    })
    .unwrap();
    (dir, db, hashes)
}

fn query(assay: &str) -> AssetQuery {
    AssetQuery {
        assay_id: assay.into(),
        stage: None,
        group_id: None,
        replicate_id: None,
        search: None,
        sort: Some("name".into()),
        cursor: None,
        limit: Some(50),
    }
}

fn percentile(values: &[Duration], pct: f64) -> u128 {
    let mut sorted = values.to_vec();
    sorted.sort();
    let index = ((sorted.len() as f64 * pct).ceil() as usize).saturating_sub(1);
    sorted[index].as_millis()
}

#[cfg(windows)]
fn process_resources() -> (u64, u64) {
    use windows_sys::Win32::Foundation::FILETIME;
    use windows_sys::Win32::System::ProcessStatus::{GetProcessMemoryInfo, PROCESS_MEMORY_COUNTERS};
    use windows_sys::Win32::System::Threading::{GetCurrentProcess, GetProcessTimes};
    unsafe {
        let process = GetCurrentProcess();
        let mut memory: PROCESS_MEMORY_COUNTERS = std::mem::zeroed();
        let memory_ok = GetProcessMemoryInfo(
            process,
            &mut memory,
            std::mem::size_of::<PROCESS_MEMORY_COUNTERS>() as u32,
        );
        let mut created: FILETIME = std::mem::zeroed();
        let mut exited: FILETIME = std::mem::zeroed();
        let mut kernel: FILETIME = std::mem::zeroed();
        let mut user: FILETIME = std::mem::zeroed();
        let cpu_ok = GetProcessTimes(process, &mut created, &mut exited, &mut kernel, &mut user);
        let ticks = |v: FILETIME| ((v.dwHighDateTime as u64) << 32) | v.dwLowDateTime as u64;
        (
            if memory_ok != 0 {
                memory.PeakWorkingSetSize as u64
            } else {
                0
            },
            if cpu_ok != 0 {
                (ticks(kernel) + ticks(user)) / 10_000
            } else {
                0
            },
        )
    }
}

#[cfg(not(windows))]
fn process_resources() -> (u64, u64) {
    (0, 0)
}

#[test]
fn s11_real_sqlite_cas_10024_asset_search_page_and_pinned_integrity() {
    let started = Instant::now();
    let (_directory, db, hashes) = make_fixture();
    assert_eq!(hashes.len(), COUNT);
    let fixture_ms = started.elapsed().as_millis();
    let (peak_memory, cpu_ms) = process_resources();
    let first_started = Instant::now();
    let first = list_at(&db, query("a0")).unwrap();
    let first_query_ms = first_started.elapsed().as_millis();
    assert_eq!(first.items.len(), 50);
    assert!(first.total > 3_000);
    let mut durations = Vec::new();
    for n in 0..40 {
        let mut q = query(&format!("a{}", n % 3));
        q.search = Some(if n % 2 == 0 {
            "科研样本".into()
        } else {
            format!("{:05}", (n * 233) % COUNT)
        });
        let time = Instant::now();
        let page = list_at(&db, q).unwrap();
        durations.push(time.elapsed());
        assert!(page.items.len() <= 50);
    }
    let p50 = percentile(&durations, 0.50);
    let p95 = percentile(&durations, 0.95);
    let p99 = percentile(&durations, 0.99);
    let mut ids = HashSet::new();
    for assay in ["a0", "a1", "a2"] {
        let mut q = query(assay);
        loop {
            let page = list_at(&db, q.clone()).unwrap();
            for row in &page.items {
                assert!(ids.insert(row.asset_link_id.clone()));
                let actual = fs::read(
                    db.parent().unwrap().join("cas").join("objects").join(&row.sha256),
                )
                .unwrap();
                assert_eq!(format!("{:x}", Sha256::digest(&actual)), row.sha256);
            }
            match page.next_cursor {
                Some(cursor) => q.cursor = Some(cursor),
                None => break,
            }
        }
    }
    assert_eq!(ids.len(), COUNT - (COUNT - 1) / 997);
    with_database_at(&db, |connection| {
        let counts = [
            ("data_assets", COUNT as i64),
            ("asset_versions", COUNT as i64),
            ("asset_links", COUNT as i64),
            ("blobs", COUNT as i64),
            ("replicates", 36),
            ("panel_asset_versions", 2),
            ("analysis_asset_versions", 1),
        ];
        for (table, expected) in counts {
            let actual: i64 =
                connection.query_row(&format!("SELECT count(*) FROM {table}"), [], |r| r.get(0))?;
            assert_eq!(actual, expected, "{table}");
        }
        assert_eq!(
            connection.query_row(
                "SELECT count(*) FROM statistical_analyses WHERE frozen_at IS NOT NULL",
                [],
                |r| r.get::<_, i64>(0),
            )?,
            1
        );
        let version = connection.query_row(
            "SELECT asset_version_id FROM panel_asset_versions WHERE usage='display'",
            [],
            |r| r.get::<_, String>(0),
        )?;
        assert_eq!(version, "v00000");
        Ok(())
    })
    .unwrap();
    let (peak_memory_after, cpu_after_ms) = process_resources();
    println!(
        "S11_METRICS dataset=sqlite-cas count={COUNT} fixture_ms={fixture_ms} first_query_ms={first_query_ms} search_p50_ms={p50} search_p95_ms={p95} search_p99_ms={p99} peak_working_set_bytes={} process_cpu_ms={} verified_links={} (native query only, not GUI)",
        peak_memory_after.max(peak_memory),
        cpu_after_ms.max(cpu_ms),
        ids.len()
    );
    assert!(first_query_ms <= 3_000, "native first list query exceeded 3 seconds");
    assert!(p95 <= 2_000, "native search query p95 exceeded 2 seconds");
}
