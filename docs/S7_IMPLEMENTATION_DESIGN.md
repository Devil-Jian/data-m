# S7 实施设计：完整性审计、可验证快照与隔离恢复

> TASK_ID=S7 · 2026-11-17 · 以 S6 `feat/s6-rename-recycle` @ `ce7c7dab933dbe4f09d98fe5c41acee00646de8a` 为基线。S6 `machine_passed/manual_pending`，用户明确要求先开发 S7，**不等于提前签收 S6/S7 的人工验收**。所有 S4–S6 及上游 PR 均保持 Draft，不合并 main。

## 审计得到的硬约束
- 受控 SQLite 位于 `app_local_data_dir/s1-test-workspace/project-tree.sqlite`，有 app ID、schema_migrations 和 v2 严格 Schema 识别机制。服务端只能通过固定数据库路径操作，不接受前端提供 DB 或 CAS 物理路径。源库不存在、未知或 Schema 异常时，审计/备份必须拒绝而不得自行创建/修复。
- **正式 S3 导入**使用扁平 SHA-256 文件名 `s1-test-workspace/cas/objects/<64-lowercase-hex>`；`cas_audit.rs` 早期原型的 `objects/ab/cd/hash` 不是正式 S3 布局，S7 不得错误沿用。保留原型、但本期新的 S7 引擎只检查正式存储。
- `cas/staging` 中可能有导入意图记录的阶段文件，扫描时要从 `import_files.staging_name` 识别受跟踪暂存项；未知项识别为 `ORPHAN_STAGING`，不自动删除。还需识别 `cas/objects` 内未登记的 `ORPHAN_OBJECT`。
- SQL `PRAGMA integrity_check`、`PRAGMA foreign_key_check`、schema 识别、所有 blobs/asset_versions 引用及 SHA-256 的核验是备份和恢复的硬条件，不允许“部分成功”。
- 任何扫描不修改源库、物理源文件或 CAS；损坏对象绝不猜测修复或就地覆盖。保留 S0–S6 原测试。

## 快照格式 v1（封存后由应用视为不可修改）
目录：`<app_local_data_dir>/s7-snapshots/<snapshot_id>/`：
`database.sqlite`（SQLite **VACUUM INTO** 一致性数据库快照，保证 WAL 视图正确）、`objects/<sha256>`（每个已登记 Blob 原字节）、`manifest.json`。
manifest 严格版本化，包含 `formatVersion=1`、`snapshotId`、`schemaVersion=2`、`databaseSha256`、`databaseSizeBytes`、按固定表名单的完整 `tableCounts`、按 SHA 排序的 `blobs[{sha256,sizeBytes}]`、按版本 ID 排序的 `versions[{id,assetId,versionNumber,blobSha256}]`、`createdAt`。不得含绝对物理源路径。备份不承诺加密或防恶意修改的认证签名；**SHA 只用于检错，不是身份签名**。

备份执行：确认源库存在且识别 Schema → 对官方 SQLite 连接执行 `VACUUM INTO` 写到未发布的随机 staging 子目录 → 在该快照上做 SQLite/FK/参照/manifest 清单检查 → 以 S3 格式逐项检查并复制 CAS Blob 到 staging，并对**复制前后**字节计算 SHA；任何失败都不得发布最终快照 → 写入并同步 manifest → 重开独立快照二次验证 → 不覆盖地发布随机 snapshot_id 目录。中断、磁盘满只允许留下以 `.s7-` 开头的隔离 staging；旧备份和主仓库保持不变。

## 审计与恢复
操作接口为结构化 Tauri `repository_safety({request})`：
`action='audit'|'list'|'backup'|'verify'|'restore'`，前端仅允许 `snapshotId`（32位小写十六进制）和 `restoreLabel`（短 ASCII 安全标识），恢复必须显式 `confirm:true`。禁止任意数据库路径/恢复位置由 UI 传入。审计返回净额、按代码分类的问题、检查数量和命名的关系表 count，输出不含本地路径；长任务通过 Tauri `data-m-s7-progress` 事件报告 phase/completed/total。

`verify` 全量校验 sealed manifest / DB / FK / schema / 版本清单 / 目录内 Blob；**发现路径重解析、未知条目、篡改、丢失、大小不一致即拒绝**，不修改备份。恢复到 `<app_local_data_dir>/s7-restores/<restoreLabel>/`，要求目标不存在或完全空白（首版实际为目标必须不存在，保守不覆盖），使用隔离 staging 目录复制并再次校验；通过后使用原子不覆盖目录发布。不能就地覆盖当前运行仓库，也不自动切换运行中的数据库；恢复完成后返回独立已恢复测试库标签及 counts/diff，人工可对比，不将“成功恢复”误认为“已安全投入科研日常使用”。

### 安全与限制
- 不执行云备份、系统盘外备份传输、自动修复、CAS GC；**应用数据目录内快照不是异地备份**。离盘复制须人工处理且在新位置重复哈希核验。
- 在共享机器环境，恶意本地进程仍可能修改未签名快照；本阶段防误操作与损坏检验，不承诺防恶意篡改。Windows reparse/TOCTOU 与同时发生的导入写入必须以锁、普通文件句柄、二次哈希和失败关闭策略控制，不得凭空声称绝对安全。
- S7 自动化机器通过仍不代表用户批准珍贵/唯一真实科研数据入库；**S0–S7 全部签收并完成 Windows 恢复演练之后**才考虑解除门禁。

## 强制验证与交付
至少新增非零 `cargo test --locked --no-default-features s7_` 和 `npm run test:e2e:desktop -- --grep "@S7"`；真实 Rust 临时 SQLite 验证 WAL 活跃写入、manifest 与 Blob 列表、备份→隔离恢复 counts+版本/hash diff=0、孤儿 staging/对象、篡改/缺失 Blob、FK 故障、磁盘故障注入回滚、拒绝现有恢复目标、重复恢复冲突及损坏备份拒绝。浏览器 Mock 只验证界面事件，不能替代 Rust。
完整 Windows CI S0–S7 / Rust fmt/check / 桌面全量 / unsigned EXE Artifact 后，编写 `docs/S7_EXECUTION_REPORT.md`，记录代码 SHA、Draft PR、CI、EXE 和人工 H1–H3 未完成项，不未经授权合并 main。
