# S8 执行报告：统计分析与生物学重复独立纳入

> TASK_ID=S8 · 2026-11-17 · **in_progress / CI_pending / manual_pending**。本文件记录已提交代码与未确认结果，不把尚未执行的检查写成通过。

## 基线与分支
- 仓库：Devil-Jian/data-m。
- 上游 S7：`feat/s7-integrity-backup-restore` @ `15e784a6bf939cf509254aacdea33bb120cf9165`，`machine_passed / manual_pending`。
- 当前 S8：`feat/s8-statistics-sample-inclusion`；以 S7 为 PR base，不合并 main。

## 代码改动
- `migrations/0003_statistics.sql`：保留原统计表和 ID，新增分析备注、更新时间和 `analysis_asset_versions`；数据库触发器拒绝跨实验样本和文件关系，并保护冻结分析不被普通写入修改。
- `project_tree.rs`：Schema v1→v2→v3 增量事务迁移；v2 既有纳入关系跨 Assay 预检，失败时拒绝迁移。
- `statistical_analysis.rs`：真实 Tauri/SQLite 接口 `list/get/create/save/freeze/revise`，纳入数量按关系查询，冻结与审计在同一数据库事务；冻结修订保留旧 ID 与引用。
- `statistical-analysis.ts(x)`：正式项目工作区内的左分析列表、中间分组重复、右侧输入/结果版本；支持保存、冻结、修订、重载。
- S7 `repository_safety.rs`、`snapshot.rs` 和 `repository-safety.ts`：Schema v2 历史快照继续受支持，新 v3 快照计数包含统计版本关联。
- 测试代码：新增六个以 `s8_` 命名的 Rust/SQLite 专项与三个标题含 `@S8` 的 Playwright UI Mock 测试。**测试文件数量不是测试通过数量。**

## 验证进度（不得推断通过）
- Rust `cargo test --locked --no-default-features s8_`：待 Windows CI 实测，已编写 6 个测试函数。
- Playwright `npm run test:e2e:desktop -- --grep "@S8"`：待 Windows CI 实测，已编写 3 个显式 UI Mock 用例。
- Cargo fmt / check、TypeScript / Vite、S0–S7 全量、Windows Release EXE、Artifact 上传：待 CI。
- CI URL：待记录真实 Run。测试 SHA：待按实际成功 workflow 固定。Artifact URL：尚无。
- 未解决风险：Windows 编译/格式/前端测试可能发现错误；v1/v2 实际迁移、历史 S7 v2 快照恢复、原生跨进程重启持久化仍需专门实测补强；未运行任何人工验收。

## Windows 人工验收（全部待完成）
- [ ] 下载对应 SHA 的 Windows 便携 EXE，在 Windows 11/WebView2 中打开正式项目管理和 S8 统计分析。
- [ ] 建立 Control 六重复及分析 A（5/6）、分析 B（6/6）；重启核对两分析独立且实验 n=6。
- [ ] 复制、移动、重命名 Raw 文件，验证实际 n 和两次分析纳入关系不变。
- [ ] 输入/结果固定到 AssetVersionId，软删除链接或新增版本后旧分析引用不漂移。
- [ ] 冻结后尝试直接编辑并确认拒绝；新建修订且审计可查。
- [ ] 用可抛弃数据演练 v1/v2 升级与 S7 历史 v2 快照验证/隔离恢复。
- [ ] 记录 EXE SHA-256、截图、原文件/CAS 哈希、测试执行者和日期。

S8 不执行 t 检验/ANOVA，不修改 CAS 或 Panel 关系；S0–S7 未完成人工恢复验收前，禁止珍贵/唯一科研数据投入使用。禁止提前标为 accepted 或合并 main。
