# 论文原始数据归档平台

Windows 10 / 11（64 位）离线单机科研数据管理桌面应用。第一阶段实现安全的本地原始文件归档，并为论文、Figure / Panel / Table 与原始数据的引用关系预留数据库结构。

## 已实现的功能

- Tauri 2 桌面程序、Vue 3 + TypeScript 界面、Rust 数据存储核心。
- SQLite WAL 数据库、外键约束、版本化初始迁移。
- 原始文件通过固定 1 MiB 缓冲区流式导入，计算 SHA-256，执行 staging 文件同步。
- 使用 SHA-256 内容地址存储不可变 Blob；相同内容只保存一个 Blob，每次导入都会产生独立的 UUIDv7 Data ID。
- 原始文件和 SQLite 元数据分离保存；数据库不存文件 BLOB。
- Windows 默认数据目录 `D:\PaperArchiveData`。如果 D 盘不存在，保持未初始化状态并提示选择本地目录，不会自动改存 C 盘。
- 通过原生对话框选择本地目录、批量导入文件、显示归档记录和哈希。
- Windows GitHub Actions 进行 Rust 检查、测试及前端构建。

第一阶段**尚未**提供论文档案编辑、Figure / Panel / Table CRUD、rdpack 导入导出、跨电脑合并、科研格式专用解析或云同步。

## 运行与构建

需要 Node.js 22、Rust 1.90+、Windows C++ 构建工具及 WebView2 开发运行环境。

```powershell
npm install
npm run tauri dev
```

执行以下命令进行验证：

```powershell
cargo fmt --manifest-path src-tauri/Cargo.toml --all -- --check
cargo test --manifest-path src-tauri/Cargo.toml --all-targets
npm run build
```

生成 Windows NSIS 安装程序：

```powershell
npm run tauri build
```

安装程序输出位置通常为 `src-tauri/target/release/bundle/nsis/`。Windows CI 的构建状态请以对应提交的 GitHub Actions 结果为准。

## 数据仓库结构

```text
D:\PaperArchiveData\
├── database\
│   └── archive.db
├── blobs\
│   └── sha256\
│       └── ab\
│           └── ab...64-character SHA-256...
└── staging\
```

- `archive_id`、`data_id` 采用 UUIDv7。数据跨电脑迁移后不应重新分配。
- `display_id` 仅用于显示，不能当作数据库外键；哈希标识内容而不标识数据记录。
- 所有原始文件存于文件系统；SQLite 的 `blobs.relative_path` 存相对地址。
- 数据库插入失败时不会返回导入成功。可能遗留的孤立 Blob 不会被自动删除；仅在核实无引用后才可清理。
- `PAPER_ARCHIVE_DATA_DIR` 环境变量仅供开发、测试时覆盖默认路径，生产环境建议在应用界面选择目录。
- 本软件当前不提供自动备份或同步功能，重要科研数据应另行备份整个仓库目录。

## 开发分支与设计文档

- 第一阶段实施计划：`docs/superpowers/plans/2026-11-03-archive-foundation.md`
- 基础架构设计：`docs/superpowers/specs/2026-11-03-paper-archive-foundation-design.md`
- 工作分支：`feat/archive-foundation`
