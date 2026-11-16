# S7 执行报告：SQLite/CAS 完整性检查、可验证快照与隔离恢复

> TASK_ID=S7 · 2026-11-17 · 当前状态：**代码已提交；Windows CI / EXE 核验中，人工恢复演练暂不验收**。正式 `accepted` 未达成。

## 开发基线与依赖边界

- 仓库：`Devil-Jian/data-m`。
- 上游 S6：`feat/s6-rename-recycle` @ `ce7c7dab933dbe4f09d98fe5c41acee00646de8a`，S6 实际代码 CI 先前已 `machine_passed`，人工 `manual_pending`。用户此次明确要求**先不进行 S6 人工验收，直接开发 S7**；这不是签收前序人工验收或解除珍贵数据安全限制。
- 当前 S7：`feat/s7-integrity-backup-restore`；[S7 Draft PR #51](https://github.com/Devil-Jian/data-m/pull/51) 以 S6 分支为 base。上游 S4 #47 / S5 #49 / S6 #50 均仍 Draft 未合并；不修改 main。
- [S7 实施设计](S7_IMPLEMENTATION_DESIGN.md)，原始验收规格 [AI_DEV_EXECUTION_SPEC.md](AI_DEV_EXECUTION_SPEC.md) 的 S7，机器状态见 [DATA_M_AI_TASKS.json](DATA_M_AI_TASKS.json)。

## 本阶段实施的行为

1. 正式 `repository_safety({request})` Tauri IPC：`audit`、`list`、`backup`、`verify`、`restore`；所有操作显式触发，不在应用启动时扫描/初始化用户路径；参数只允许受控 `snapshotId` 与安全 `restoreLabel`，没有任意读写磁盘路径参数。
2. 只读审计：当前 Schema v2 识别、SQLite `integrity_check`、`foreign_key_check`、关系计数、所有登记 Blob 的字节数和 SHA-256 校验、S3 正式扁平 CAS `cas/objects/<sha256>` 路径、未登记对象和孤儿 staging；报告错误码、数量与脱敏对象标签。**不更改被检测文件**。
3. 创建备份：对已识别且非进行中的测试仓库，通过 `VACUUM INTO` 获取含 WAL 已提交数据的一致性 SQLite 快照，建立快照数据库 SHA-256、Schema、所有模型表计数、每条 AssetVersion/Blob 及哈希的 `manifest.json`。逐项验证并复制 CAS 字节，最后对 staging 整体验证，成功才以唯一 ID 目录封存。中断/写入失败时不发布半成品，不覆盖既有备份。
4. 恢复：先验证封存快照及所有 Blob，再向应用数据目录中**以前不存在的新测试目录**复制数据库和 CAS；重复校验快照内的 Entity/Relationship/Blob counts 和 SHA，成功才发布恢复目录。拒绝目标同名和损坏快照。**不覆盖原仓库，也不自动切换正在运行的数据库**。
5. 正式「项目管理」工作区新增 S7 安全工作台：手动完整性检查、备份确认勾选、快照查看/校验、恢复目录标签与二次确认、任务阶段进度、错误码与关系计数。浏览器 Mock 仅用于前端事件测试，真实 SQL/哈希由原生 Rust 测试承担。
6. S0 严格授权 IPC 列表仅增加经审计的 `repository_safety`，Windows Actions 保留 S0–S6 所有机器测试并新增非零 `s7_`、`@S7` 及 Windows Release EXE。

## Windows CI 机器验证（必须与最终代码 SHA 对齐）

| 检查 | 当前状态 | 后续需回填证据 |
|---|---|---|
| Rust `cargo fmt --all -- --check` | CI 验证中 | 运行 URL、SHA、是否通过 |
| 原生 `cargo test --locked --no-default-features s7_` | CI 验证中 | 实测发现数量、passed/failed |
| S0–S6 原生 Rust 与前端专项回归 | CI 验证中 | 全套通过数量 |
| Chromium `npm run test:e2e:desktop -- --grep "@S7"` | CI 验证中 | 浏览器 Mock，不能替代 Rust |
| 桌面 Playwright 全量 | CI 验证中 | passed/failed |
| `cargo check --locked` / Vite build | CI 验证中 | pass/fail |
| Windows Release EXE + GitHub Artifact | CI 验证中 | 仅成功后填写有效 Artifact |
| Windows 11 真实恢复演练 | `manual_pending` | 用户暂不验收；不可伪写 PASS |

## 必须完成的 Windows 人工恢复演练（用户明确暂缓）

- [ ] 在 Windows 11 上使用本阶段未签名便携 EXE，仅创建可抛弃的 S1–S6 测试仓库，保存测试源、EXE 和最终备份文件的 SHA-256 记录。
- [ ] 对样本 Raw/Processed、统计/展示文件、软删除回收链接、Panel 固定版本引用，运行只读审计并保存问题数、Blob 数、Replicate n、AssetVersion/AssetLink/Panel 关系计数。
- [ ] 通过备份确认生成快照，使用「验证哈希」复核 manifest/Schema/FK/每个 Blob，另行将快照复制到独立磁盘并验证副本。**应用内同设备备份不等于离盘灾备**。
- [ ] 实际使用新目录恢复，核对原仓库与独立恢复仓库的所有实体、关联计数及每个 Blob SHA-256，记录差异 `diff=0` 的真实报告。
- [ ] 人为篡改测试备份中的 Blob、删除 Blob、损坏 manifest 或 database，确认恢复被拒绝且现有仓库/旧封存备份字节保持不变。
- [ ] 注入或制造导入未完成状态、未知 staging、磁盘不可用/写入失败，确认不会发布半成品；随后重试完整成功。
- [ ] 记录操作者、日期、Windows 版本、EXE SHA、CI/Artifact 版本、截图/日志、每项 PASS/FAIL。未执行项目继续为 `manual_pending`。

## 现阶段限制与明确风险

- 当前快照位于本机应用数据目录，**不是云备份、外部磁盘备份或认证签名格式**；SHA-256 检验意外损坏，不防止同时篡改 manifest 和字节的恶意行为。
- 源仓库仍可能存在 S3 活跃写入或外部软件并发修改；S7 采用 SQLite 一致性快照、逐文件哈希与失败关闭策略，但不能将这些测试替代 Windows 多进程实际故障恢复演练。
- 崩溃可能残留以 `.s7-` 开头的未封存临时目录，**它们不是有效快照**；本阶段不会自动删除未知 CAS/Blob。
- 仅从正式 S3 扁平 CAS 布局读取；早期 CAS 原型使用分层目录不作为 S7 生产存储模型。
- **S0–S7 全阶段与 Windows 原生恢复演练人工签收前，不允许珍贵或唯一原始科研数据进入日常使用。** S7 自动化机器通过仍不等于正式 `accepted`。
