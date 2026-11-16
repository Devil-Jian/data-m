# S4 实施与验收记录（2026-11-16 用户签收）

- TASK_ID：`S4`，真实文件浏览、搜索、选择。
- 工作分支：`feat/s4-real-asset-browser`；基线：S3 已合并的 `main`。
- 受测代码提交：`d8c2efe2b71601e3097c580e507c5418c87b43ac`（此后仅文档修订不代表重新测试）。
- PR：[Draft #47](https://github.com/Devil-Jian/data-m/pull/47)，截至本次记录仍 **Open / Draft / 未合并 main**。
- **用户阶段验收结果：合格 / 已签收（2026-11-16）**。用户在 Windows S4 EXE 中确认 100 张 ROS 模拟 PNG 文件导入无问题，并在会话中明确表示“S4验收合格，在文档中标记”。
- **机器结果：PASS**。**正式任务状态：用户签收、待代码合并**；按 [AI 执行规范](AI_DEV_EXECUTION_SPEC.md) 的 `accepted` 严格定义，未合并前不标记为正式集成完成，不自动合并。
- **范围限制**：本次阶段性签收不代表全量 S4 功能/回归项均有截图、哈希和跨视图证据；存在已知限制，见下文。用户允许按原计划进入 S5 开发。

## 已实现的 S4 功能

- 新增 Rust `list_assets`、`get_asset` 只读查询；按 Assay、Stage、Group、Replicate、名称过滤，提供稳定排序、分页与资产详情。
- 区分 `AssetId`、`AssetLinkId`、`AssetVersionId`，对 UI 返回 SHA-256、大小、版本、引用标志，不返回用户源路径或 CAS 写入路径。
- 正式项目管理中的实验阶段页接入真实文件列表；搜索、排序、分页、当前页 Ctrl/Shift 选择和详情可用。
- 与仍为模拟数据的「演示工作区」保持隔离；**模拟 UI 不等于正式科研数据管理功能**。

## 已验证机器证据

| 检查项 | 结果 | 证据 |
|---|---|---|
| Rust 格式检查 | PASS | Windows CI `cargo fmt --all -- --check` |
| TypeScript/Vite、交互与桌面测试 | PASS | Windows CI 完整前端步骤 |
| 桌面 Playwright E2E（含 `@S4`） | PASS | Windows CI S0–S4 浏览器回归 |
| S4 Rust/SQLite 专项测试（非零用例） | PASS | `cargo test --locked --no-default-features s4_` |
| S0–S3 Rust 专项 + 完整 Rust | PASS | `cargo test --locked --no-default-features` |
| Rust 类型编译检查 | PASS | `cargo check --locked` |
| Windows Release EXE 构建及上传 | PASS | `npm run tauri:build`、Artifact 上传 |

- [Windows desktop shell #43826786651](https://github.com/Devil-Jian/data-m/actions/runs/43826786651)：**completed / success**，全部步骤通过。
- [Build UI demo variants #43826786640](https://github.com/Devil-Jian/data-m/actions/runs/43826786640)：**completed / success**。
- [data-m-s4-unsigned-portable-windows Artifact #14207632088](https://github.com/Devil-Jian/data-m/actions/runs/43826786651/artifacts/14207632088)：含 `data-m-desktop.exe`，未签名便携测试版（非 Windows 安装器），GitHub Artifact 约 2026-11-30 到期。
- 这些证据只覆盖上述提交与构建，不能代替所有 Windows GUI 人工测试或正式科研数据安全验收。

## Windows 人工签收记录（据用户明确反馈）

| 人工项 | 记录结果 | 证据边界 |
|---|---|---|
| 从 GitHub Actions 下载 S4 EXE 并启动测试 | 已由用户使用并反馈 | 未提供 Windows build / DPI / EXE SHA 记录 |
| 100 张 `ROS001_raw.png` 至 `ROS100_raw.png` 模拟文件导入 | **用户确认导入无问题** | 未提供截图/原始源哈希前后对照 |
| 用户对 S4 阶段签收 | **合格（2026-11-16）** | 用户明确声明；不等于剩余全部用例均单独提供证据 |
| 50+50 分页、搜索、排序、重启后持久化 | 未单独提供结果证据 | 不据此宣称已人工通过 |
| 同一资产多上下文、跨页选择、框选/跨视图 | 未满足完整规范或缺少实测 | 见遗留事项 |

## 已知缺口、留待后续验收的项

1. 真实 S3→S4 100 文件端到端导入/分页/列表总数对照没有完整自动化证据；Rust 中的 100 条 SQLite 测试是独立构造数据。用户已确认真实导入成功，但其他环节不能直接推定通过。
2. 同一 DataAsset 的多 `AssetLinkId` 上下文选择与跨容器拖拽尚需针对性测试。
3. 当前页多选已接入；跨分页选择、过滤全集选择与框选/拖拽一致性仍是已知差距。**S5 不得直接复用有歧义的选区作为写操作授权**，必须验证 ID 和目标作用域。
4. 偏移游标在并发变更时可能漂移，后续需快照/键集分页或等效约束。
5. 软件仍处于隔离测试仓库阶段。**S7 完整性校验、备份恢复及人工演练签收之前，不得导入珍贵/唯一科研原始数据**。

## 下一阶段

**TASK_ID=S5：资产复制与逻辑移动**，遵循 [AI 可执行开发规范 S5](AI_DEV_EXECUTION_SPEC.md) 以及 [DATA_M_AI_TASKS.json](DATA_M_AI_TASKS.json)。S5 新分支须基于已签收的 S4 最新代码与文档基线；如 S4 PR 尚未合并，可从 `feat/s4-real-asset-browser` 建分支并明确依赖，禁止未经授权合并 main。S5 必须完成真实 Rust 事务、幂等请求、原子回滚、审计和 `@S5`/ `s5_` 非零自动化测试及 Windows EXE 人工清单。

**版本状态规则**：用户阶段签收与正式 `accepted`（需合并、完整证据）分开记录；本记录不隐瞒仍待补测项。
