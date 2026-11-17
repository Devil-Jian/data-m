# S7 执行报告：SQLite/CAS 完整性检查、可验证快照与隔离恢复

> TASK_ID=S7 · 2026-11-17 · 当前状态：**`machine_passed / manual_pending`**。Windows CI、真实 Rust/SQLite 专项与 Windows EXE 全部完成；真实 Windows 11 WebView2 人工恢复演练由用户暂缓。正式 `accepted` 未达成，PR 仍 Draft。

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

## 机器验收：已证实的 CI、代码 SHA 和 Windows EXE

- **机器验证代码 SHA**：`53818cff49d94cbe7fe4aa152bcaa882cc72d6c5`。此 SHA 是本次成功运行实际检出的源代码，后续文档-only 提交不冒充已重测的代码。
- **[Windows desktop shell #43988872497](https://github.com/Devil-Jian/data-m/actions/runs/43988872497)**：`status=completed`，`conclusion=success`，逐个步骤结果已从 Windows GitHub Actions 原始日志核查，关联上述代码 SHA。
- **[EXE Artifact #14287546424](https://github.com/Devil-Jian/data-m/actions/runs/43988872497/artifacts/14287546424)**：`data-m-s7-unsigned-portable-windows`，上传成功，ZIP 4,136,696 bytes。含未签名的 `data-m-desktop.exe`，不是正式安装包。**实际 EXE SHA-256 尚需下载后手动计算**，不可将 ZIP 体积或 GitHub Artifact ID 当成 EXE 哈希。
- S7 旧失败 CI [#43911039553](https://github.com/Devil-Jian/data-m/actions/runs/43911039553) 的原生 Rust 阶段在编译时报告 2 个 `E0597`：`src/repository_safety.rs:350` 的 `blob_list` 和 `src/repository_safety.rs:364` 的 `version_list`。原因是 `rusqlite::Statement::query_map` 迭代器链处于函数末尾表达式，`stmt` drop 顺序导致借用生存期校验失败。修复为局部 `let result = ...collect(...)` 后返回；同一调用中的查询和结果类型保持不变。首次修复 SHA `6cd3179a305d9ad68a7c63b396697e6c953989ea` 在 [#43988721788](https://github.com/Devil-Jian/data-m/actions/runs/43988721788) 暴露仅格式检查失败，进一步按 runner `rustfmt` 的真实差异修正；最终成功 SHA 为 `53818cff...`。未降低任何安全校验或删除失败测试。

| 验证项目 | Windows CI #43988872497 实测 | 结果 |
|---|---|---|
| Rust `cargo fmt --all -- --check` | 代码格式符合标准 | **PASS** |
| TypeScript、Vite、S0 baseline 与 UI 基础测试 | 工作流各步骤全部成功 | **PASS** |
| S7 原生 `cargo test --locked --no-default-features s7_` | **15 passed / 0 failed**，非零测试发现校验 | **PASS** |
| S7 `@S7` 桌面 Playwright（显式 Mock） | **4 passed / 0 failed** | **PASS（仅 UI）** |
| S7 + S0–S6 完整 Rust `cargo test --locked --no-default-features` | **95 passed / 0 failed** | **PASS** |
| 桌面 Playwright 全量 | **37 passed** | **PASS** |
| Windows Chromium pointer/keyboard `npm run test:e2e` | **29 passed** | **PASS** |
| S6/S5/S4/S3/S2/S1/S0 原生专项回归 | 分别 **11/8/2/17/7/14/4 passed** | **PASS** |
| `cargo check --locked` | Windows 完整编译检查 | **PASS** |
| Windows Release EXE `npm run tauri:build` | 编译成功 | **PASS** |
| S7 unsigned portable EXE 上传 | Artifact **#14287546424** 已核验 | **PASS** |
| 真实 Windows 11 WebView2 备份与恢复演练、EXE/源文件 SHA-256 核验 | **尚未执行，用户明确暂缓** | **manual_pending** |

**机器测试边界**：浏览器 `@S7` 使用明确注入的 Mock，仅证明 UI/交互；真正 SQLite/WAL/文件哈希/崩溃注入/隔离恢复由 `s7_` 原生临时数据测试承担。CI 通过不代表在真实 Windows 工作目录完成备份恢复演练，更不代表可以操作珍贵或唯一科研数据。

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
