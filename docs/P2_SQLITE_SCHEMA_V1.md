# Data-M SQLite Schema v1（Issue #14）

## 本增量范围

本 PR 实现 **数据模型、迁移和约束测试**，不打开用户数据库、不导入文件，也不向 WebView 暴露写入接口。全部数据库测试使用独立的 SQLite 内存连接。

领域模型：

```text
Project → Theme → Assay → ExperimentGroup → Replicate
DataAsset → AssetVersion → Blob(sha256, bytes)
                        │
Assay / Group / Replicate ─ AssetLink ─ DataAsset
Paper → Figure → Panel ─ PanelAssetVersion ─ AssetVersion
                          └─ RepresentativeSource ─ Replicate / AssetVersion
StatisticalAnalysis ─ AnalysisSample(included) ─ Replicate
```

**基础不变量**

- 生物学样本和文件资产拥有不同的主键与表：复制 AssetLink 不会自动插入 Replicate 或改变样本数。
- AssetVersion 引用 CAS Blob SHA-256；登记后的 Blob 元信息和版本身份受禁止 UPDATE/DELETE 的触发器保护。
- Panel 关联固定的 AssetVersionId，而不是路径或可变 DataAsset 名称；逻辑重命名不能断开论文来源。
- 某次统计的 `included` 仅在 `analysis_samples` 关系上，不是文件的全局 boolean；代表来源位于 Panel/Replicate 关系中。
- AssetLink 可以软删除；同 Assay、Group、Stage 中尚未删除的文件名执行大小写不敏感的唯一性检查。
- 外键强制 ON；跨 Assay/Group 的不匹配关联直接拒绝。
- 不读取或修改未知 SQLite 文件。只有空库可初始化；未来未知版本与不完整版本历史均拒绝继续。

**迁移与运行**

- `apps/desktop/src-tauri/migrations/0001_initial.sql`
- `apps/desktop/src-tauri/src/domain_db.rs`（`initialize_empty_database`）
- `apps/desktop/src-tauri/Cargo.toml` 增加 `rusqlite` 的 bundled SQLite 依赖。

所有迁移在事务内执行，`user_version=1` 和 `schema_migrations` 中记录版本；重复执行 v1 初始化不产生新表或重复记录。未实现数据库打开/备份/恢复的正式 UI API。

## 验证

```powershell
cd apps/desktop/src-tauri
cargo fmt --all -- --check
cargo test
cargo check
```

测试覆盖：迁移幂等、外键、样本数量不随复制资产变化、跨组关联拒绝、文件重名/软删除、SHA 格式与不可变性、Panel 固定版本、统计纳入关系和未知数据库安全拒绝。

## 尚未完成与发布门禁

这是 **P2/#14 的部分完成**，不是正式 P2 归档能力。后续任务：
- #14 Repository 接口、SQLite 文件路径管理、数据迁移升级与版本前向兼容；
- #6 / #15 CAS 原始文件安全复制与分块哈希、staging/finalize/reconcile、故障注入与源文件保护；
- #16 SQLite WAL-safe 备份、完整恢复和参照完整性验证；
- #17–#19 正式实验 CRUD、论文固定版本链接和双向溯源；
- #20 / #21 真实 Windows GUI、10k 大数据与正式 NSIS/MSI 安装升级门禁。

**不要**将新的迁移代码直接用于任何现有科研数据库。生产迁移与备份恢复未经审查和演练。PR 保持 Draft，直到 Windows CI 和迁移验收通过。
