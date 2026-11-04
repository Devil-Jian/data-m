# 本地归档库与数据资源模块 Implementation Plan

> For agentic workers: REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 建立可运行的 Tauri 桌面工程，并交付本地归档库初始化、SQLite schema、流式文件导入、UUIDv7 Data ID、数据列表和基础 UI。

**Architecture:** Vue 3 前端通过 Tauri IPC 调用 Rust commands。Rust 的 ArchiveStore 负责 SQLite 和本地文件仓库，原始文件以 SHA-256 内容寻址存储；SQLite 仅保存元数据和关系。

**Tech Stack:** Tauri 2、Vue 3、TypeScript、Rust、rusqlite（bundled SQLite）、UUIDv7、SHA-256、tauri-plugin-dialog、Windows GitHub Actions。

**Spec:** docs/superpowers/specs/2026-11-03-paper-archive-foundation-design.md

## Global Constraints

- 操作系统：Windows 10 / Windows 11，64 位。
- 默认数据仓库：D:\PaperArchiveData。
- 软件形态：Tauri 2 桌面应用。
- 前端：Vue 3 + TypeScript。
- 核心业务：Rust。
- 数据库：SQLite，WAL 模式，外键开启，写入串行化。
- 原始文件保存在 NTFS 文件系统，不存入 SQLite BLOB。
- 原始文件路径以 SHA-256 内容寻址；数据库保存相对路径。
- Data ID 使用 UUIDv7，跨电脑迁移时不重新分配。
- 文件导入必须流式读写，不将完整文件载入内存。
- D 盘不存在时必须让用户选择本地目录，不得静默切换位置。

## Review Focus

1. 源文件在导入过程中消失、不可读或读取失败时，不得留下 Data Record。
2. 大文件导入必须采用固定缓冲区流式处理，内存使用不随文件大小线性增长。
3. 同一 SHA-256 多次导入时只保存一个 Blob，但每次创建独立 Data ID。
4. 数据库提交失败时不得返回成功；遗留孤立 Blob 不得被误删。
5. D 盘不存在或用户选择非法/非本地路径时，应用保持未初始化并给出明确错误。

---

### Task 1: 工程骨架与 Windows CI

**Files:**
- Create: package.json
- Create: index.html
- Create: vite.config.ts
- Create: tsconfig.json
- Create: tsconfig.node.json
- Create: src/main.ts
- Create: src/App.vue
- Create: src-tauri/Cargo.toml
- Create: src-tauri/build.rs
- Create: src-tauri/tauri.conf.json
- Create: src-tauri/capabilities/default.json
- Create: src-tauri/src/main.rs
- Create: src-tauri/src/lib.rs
- Create: .github/workflows/ci.yml
- Create: .gitignore

**Interfaces:**
- Tauri library crate name: paper_archive_lib.
- Vite dev server: http://localhost:1420.
- Frontend entry: src/main.ts.
- Rust entry point: paper_archive_lib::run().

- [ ] Step 1: Create minimal project manifests and shell UI.
- [ ] Step 2: Add Windows CI for cargo fmt --check, cargo test, npm ci, and npm run build.
- [ ] Step 3: Verify workflow configuration and commit as build: scaffold Tauri and Vue desktop app.

### Task 2: ArchiveStore contract tests (TDD RED)

**Files:**
- Create: src-tauri/src/archive/mod.rs
- Create: src-tauri/src/archive/store.rs
- Create: src-tauri/src/archive/models.rs
- Create: src-tauri/src/archive/error.rs
- Test: unit tests in src-tauri/src/archive/store.rs

**Interfaces:**
- ArchiveStore::open(root: impl AsRef<Path>) -> Result<ArchiveStore, ArchiveError>
- ArchiveStore::import_file(source: impl AsRef<Path>) -> Result<DataRecord, ArchiveError>
- ArchiveStore::list_data_records() -> Result<Vec<DataRecord>, ArchiveError>
- ArchiveStore::archive_info() -> Result<ArchiveInfo, ArchiveError>

- [ ] Step 1: Write tests for directory initialization and stable archive_id.
- [ ] Step 2: Run cargo test --manifest-path src-tauri/Cargo.toml archive::store::tests::opens_archive_and_keeps_archive_id_stable -- --exact; expect compile failure because ArchiveStore implementation is not present yet.
- [ ] Step 3: Write tests for file hash preservation, duplicate-content Blob reuse with distinct Data IDs, same-name/different-content preservation, missing-source failure, and list metadata.
- [ ] Step 4: Run the focused test suite and confirm tests fail only because implementation is missing.
- [ ] Step 5: Commit contract tests as test: define archive storage behavior.

### Task 3: SQLite schema and ArchiveStore implementation

**Files:**
- Create: src-tauri/migrations/0001_initial.sql
- Modify: src-tauri/src/archive/store.rs
- Modify: src-tauri/src/archive/models.rs
- Modify: src-tauri/src/archive/error.rs

**Interfaces:**
- ArchiveInfo { archive_id: String, schema_version: i64, data_dir: String }
- DataRecord { data_id: String, display_id: String, sha256: String, original_filename: String, file_extension: String, category: String, file_size: u64, created_at: String }
- ArchiveError distinguishes IO, SQLite, invalid source, unsupported schema, and corrupt existing Blob.

- [ ] Step 1: Add schema for archive_meta, blobs, data_records, papers, paper_items, data_references, package_imports, import_conflicts and operation_logs.
- [ ] Step 2: Implement directory initialization and versioned SQLite migration.
- [ ] Step 3: Implement fixed-buffer streaming copy to staging while computing SHA-256.
- [ ] Step 4: Implement content-addressed Blob storage and verify an existing Blob before reuse.
- [ ] Step 5: Insert Blob and Data Record in one SQLite transaction; never overwrite an existing Blob.
- [ ] Step 6: Implement list_data_records and archive_info.
- [ ] Step 7: Run focused tests, then full cargo test --manifest-path src-tauri/Cargo.toml; all tests must pass.
- [ ] Step 8: Commit as feat: add local archive store and content-addressed imports.

### Task 4: Tauri application state and IPC commands

**Files:**
- Create: src-tauri/src/state.rs
- Create: src-tauri/src/commands/mod.rs
- Modify: src-tauri/src/lib.rs
- Modify: src-tauri/Cargo.toml

**Interfaces:**
- AppState stores the default path and Mutex<Option<Arc<ArchiveStore>>>.
- get_archive_status() -> Result<ArchiveStatus, String>
- initialize_archive(path: Option<String>) -> Result<ArchiveStatus, String>
- list_data_records() -> Result<Vec<DataRecord>, String>
- import_data_file(path: String) -> Result<DataRecord, String>

- [ ] Step 1: Add Tauri dialog plugin and default capability permissions.
- [ ] Step 2: Implement default directory resolution: Windows uses D:\PaperArchiveData; environment override supports tests/development; other platforms use a user-local development path.
- [ ] Step 3: Implement startup initialization only when the default drive exists; otherwise leave the app usable in an uninitialized state.
- [ ] Step 4: Implement commands; run large file import in Tauri blocking task pool.
- [ ] Step 5: Verify cargo fmt --check and cargo test --manifest-path src-tauri/Cargo.toml.
- [ ] Step 6: Commit as feat: expose archive services through Tauri IPC.

### Task 5: Vue archive dashboard and native file dialogs

**Files:**
- Modify: package.json
- Modify: src/App.vue
- Modify: src/main.ts
- Modify: src-tauri/src/lib.rs
- Modify: src-tauri/capabilities/default.json

**Interfaces:**
- Frontend invokes the four IPC commands defined in Task 4.
- Data directory selection uses @tauri-apps/plugin-dialog with open({ directory: true, multiple: false }).
- File selection uses open({ multiple: true }); selected files are imported sequentially.

- [ ] Step 1: Add dialog frontend API and TypeScript DTO definitions.
- [ ] Step 2: Build a dashboard showing archive state, selected directory and data count.
- [ ] Step 3: Add choose-directory and import-file flows.
- [ ] Step 4: Render data records with filename, Data ID, category, size, SHA-256 and created time.
- [ ] Step 5: Add loading, empty and error states; do not block UI while importing.
- [ ] Step 6: Run npm ci, npm run build, Rust format and test commands.
- [ ] Step 7: Commit as feat: add local data library dashboard.

### Task 6: Verification and handoff

**Files:**
- Modify: README.md
- Review: all files in this plan.

- [ ] Step 1: Re-read this plan and verify every requirement against code and tests.
- [ ] Step 2: Verify CI result for the latest commit; report any checks that could not run.
- [ ] Step 3: Inspect repository diff and ensure no generated build output or local data files are committed.
- [ ] Step 4: Prepare implementation summary and open a draft PR from feat/archive-foundation to main after verification.
