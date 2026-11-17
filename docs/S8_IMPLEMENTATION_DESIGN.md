# S8 实施设计：独立统计分析、纳入关系与固定文件版本

> TASK_ID=S8 · 基线 S7 `15e784a6bf939cf509254aacdea33bb120cf9165` · 分支 `feat/s8-statistics-sample-inclusion` · 状态：in_progress，人工验收不在本阶段执行。

## 已核查的代码事实
- `migrations/0001_initial.sql` 已建 `statistical_analyses(id,assay_id,name,frozen_at)` 及 `analysis_samples(analysis_id,replicate_id,included)`；当前缺真正生产 CRUD 和跨 Assay、冻结的持久化约束。
- 正式库由 `project_tree.rs` 管理；当前认可 v1/v2，正常打开自动事务升级至 v2；不能只改 schema-only 的 `domain_db.rs`。
- `asset_versions.id` 是不可变的固定文件版本身份，`asset_links` 只是逻辑归属，复制和移动不能修改 `replicates`。
- S7 `repository_safety.rs` 和 `repository_safety/snapshot.rs` 使用固定表集合和 v2 manifest；必须保证 v2 快照原样验证和隔离恢复。

## Schema v3 与迁移
增量文件：`0003_statistics.sql`。沿用旧 ID 和原有两表，为分析加 `notes`、`updated_at`；新建 `analysis_asset_versions(analysis_id,asset_version_id,role)`，`role` 为 `input` 或 `result`；任何时候不在 DataAsset 上创建 included 字段。

旧 v1 先按既有 v2 迁移升级；新 v2 经过完整可信 Schema 校验后，单个 `IMMEDIATE` 事务写入 v3；旧分析、样本及冻结时间保留。迁移前读取旧纳入行，检查它们所属的组与 Assay 是否匹配；发现非法旧行即失败关闭，绝不能静默删除或修正。Schema 历史、user_version 与数据必须原子提交；重复打开幂等。旧快照验证应按其记录的版本重构正确预期 Schema，新快照 Schema v3 采用扩展表清单。

DB 触发器作为防线：拒绝跨 Assay 的 Replicate；仅允许引用与分析所属 Assay 有历史 `asset_links` 关系的 AssetVersion；冻结后拒绝修改分析元数据、纳入行及版本关联。曾被软删除的 `asset_links` 仍可以作为来源证据；不修改任何 `asset_versions` 或 CAS 字节。

## 事务 API 草案（必须实现，不能将 UI Mock 代替服务端）
`statistical_analysis({request})`：动作 `list | get | create | save | freeze | revise`，均通过正式受控数据库访问器执行；结构化请求只接受分析、Assay、Replicate 和 AssetVersion 稳定 ID、名称备注及明确 `inputVersionIds`/`resultVersionIds`；不接受物理路径或 SQL。创建可在一个事务内写入分析、纳入与关联；保存在一个事务内验证全部 ID/状态、替换当前草稿纳入和文件版本集合并记录审计；冻结在单事务内锁定前完成数据校验、插入审计、设置 `frozen_at`。冻结后以新分析 ID 复制为草稿修订，写入 `audit_events` 的原分析 ID/新 ID 关联；拒绝原 ID 的静默覆盖。读取直接从持久库而不是前端状态回填。

列表返回 `assayId`、`analysisId`、`status`、`includedN`、`updatedAt`；详情按组返回 `experimentN` 和本分析 `includedN`（均由数据库查询，不是缓存列），并返回固定 `AssetVersionId`。选中/未选中样本应使用纳入关系明确表达，不能通过文件列表推断。

## 工作区 UI 目标
正式「项目管理」实验页内：左侧分析清单、中间组/生物学重复选择和实际 n / 纳入 n / 排除 n、右侧输入/结果固定版本；底栏保存、冻结、创建可审计修订。分析切换重新请求 Rust；Frozen 只读且保留历史。只显示该 Assay 可追溯的版本，默认保留用户筛选，不批量创建 Replicate。

## 必须验证
- Rust/SQLite：分析 A 5/6 与 B 6/6 相互独立，复制文件不改 n，跨 Assay 拒绝，软删除历史固定版本可查；冻结不可修改，重启持久化，事务失败回滚；v1/v2 迁移保留原 ID，S7 v2 旧快照仍可验证/恢复。
- Playwright：真实工作区入口、组勾选及 n 显示、保存切换、冻结只读、修订；UI Mock 明确标注。
- CI：`cargo test --locked --no-default-features s8_` 非零；`npm run test:e2e:desktop -- --grep "@S8"` 非零；再运行全部 Rust/桌面及既有 S0–S7、Windows EXE 上传；保留关联 SHA 与 CI/Artifact URL。任何未执行项不得标 PASS。

## 安全边界
不执行统计计算、不改变 CAS 与 Panel 固定版本、不自动并入 main。S7 和 S8 的 Windows 人工验收仍是 `manual_pending`；未完成全部恢复演练之前，不使用珍贵/唯一原始科研数据。
