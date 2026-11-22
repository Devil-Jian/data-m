# S8 执行报告：统计分析与独立样本纳入

> TASK_ID=S8 · 2026-11-17 · **machine_passed / manual_pending**。机器验证与 Windows Release EXE 已完成；Windows 11 原生 WebView2 人工验收尚未执行，状态非 accepted，PR 保持 Draft。

## 开发基线与提交身份
- 仓库：`Devil-Jian/data-m`。
- 上游 S7：`feat/s7-integrity-backup-restore` @ `15e784a6bf939cf509254aacdea33bb120cf9165`，`machine_passed / manual_pending`，不以 S8 机器通过代替 S7 恢复演练。
- 当前分支：`feat/s8-statistics-sample-inclusion`。
- [S8 Draft PR #52](https://github.com/Devil-Jian/data-m/pull/52) 以 S7 分支为 base；**未合并 main**。
- **成功 Windows CI 实测代码 SHA：`d52b1e9c9851d718ff9ae80d4e0c700290c6da22`**。后续纯文档提交不是这个 SHA 的编译输入，不应错误标记为重测。
- [Windows desktop shell #44003979357](https://github.com/Devil-Jian/data-m/actions/runs/44003979357)：status=completed、conclusion=success，全部步骤从 GitHub job 实际结果/原始日志核查。
- [S8 Windows EXE Artifact #14293614430](https://github.com/Devil-Jian/data-m/actions/runs/44003979357/artifacts/14293614430)：`data-m-s8-unsigned-portable-windows`，ZIP 4,151,074 bytes，实际上传且未过期；属于**未签名便携 EXE**，不是 NSIS/MSI 安装器。EXE 文件自身 SHA-256 待下载后单独计算。

## Schema 迁移与数据约束
- `apps/desktop/src-tauri/migrations/0003_statistics.sql`：在保留 `statistical_analyses`、`analysis_samples`、原 AssetVersion 与 Panel ID 的基础上，增添 `notes`、`updated_at` 和 `analysis_asset_versions(analysis_id,asset_version_id,role)`，其中 role = input/result。
- 正式 `project_tree.rs` 校验 Schema 身份后，以 SQLite IMMEDIATE 事务从 v1→v2→v3 或 v2→v3 升级，拒绝非法旧纳入关系并保留原 ID；不修改 CAS 字节。
- 数据库触发器与服务端验证拒绝跨 Assay 的 Replicate / 文件版本，冻结后拒绝普通更新、删除或替换纳入及版本关系。
- S7 快照验证按 manifest 的 Schema v2/v3 区分表清单。历史 v2 快照原样验证并可在独立目录恢复；新 v3 包含统计版本引用表计数，不回写或篡改封存快照。
- S3、S1 历史测试中原本硬编码的 Schema v2/v3 期望已按本阶段版本更新，仍保留原有迁移原子性及非法未来 Schema 拒绝的断言。

## API 与正式 UI
- Rust/Tauri `statistical_analysis({request})`：`list/get/create/save/freeze/revise`；按 Assay 查询分析清单、纳入样本、可关联固定版本。创建/保存/冻结/修订与 `audit_events` 在同一事务，冻结修订保留原记录并创建新 ID。
- 正式项目管理入口「统计分析 (S8)」，而不是独立 Demo 生产数据。左侧分析列表，中间按 Group 勾选 Replicate 并显示实验实际 n、纳入 n、未纳入 n，右侧选择输入/结果 `AssetVersionId`。冻结只读，修订可追溯。
- `scripts/s0-baseline.test.mjs` 增加经授权的新 IPC，保留严格白名单而不是关闭检查。

## Windows Actions 实测

| 测试项 | Windows CI #44003979357 实际结果 | 结论 |
|---|---|---|
| `cargo fmt --all -- --check` | 标准格式通过 | PASS |
| S0 基线检查 | 3 passed / 0 failed | PASS |
| TypeScript / Vite、交互及 UI 基础测试 | CI 全部通过 | PASS |
| Windows Chromium 交互回归 | 29 passed / 0 failed | PASS |
| 桌面全量 Playwright | 40 passed / 0 failed | PASS |
| `@S8` Playwright（明确 Mock） | 3 passed / 0 failed | PASS（UI） |
| `s8_` 原生 Rust/SQLite | **8 passed / 0 failed** | PASS |
| `s7_` 原生备份恢复 | 15 passed / 0 failed | PASS |
| S6/S5/S4/S3/S2/S1/S0 Rust 专项 | 11 / 8 / 2 / 17 / 7 / 14 / 4 passed | PASS |
| `cargo test --locked --no-default-features` 全量 | **103 passed / 0 failed** | PASS |
| `cargo check --locked` | Windows 完整检查通过 | PASS |
| `npm run tauri:build` Windows Release EXE | 构建步骤成功 | PASS |
| EXE Artifact 上传 | #14293614430，4,151,074-byte ZIP | PASS |
| Windows 11 WebView2 实机人工验收 | 未执行（用户暂缓） | manual_pending |

测试总数非零、未跳过失败项目；浏览器 Mock 仅覆盖 UI，Rust 临时 SQLite 测试才覆盖业务持久性和事务约束。

## 失败与修复历史（全部保留）
1. [Windows #43999891949](https://github.com/Devil-Jian/data-m/actions/runs/43999891949)：Rustfmt 失败；按 Windows runner 原始 39 处差异修复，不跳过格式校验。
2. [Windows #44000628135](https://github.com/Devil-Jian/data-m/actions/runs/44000628135)：S0 严格 IPC 白名单未允许新的 S8 命令；在白名单内显式添加并保留意外命令拒绝。
3. [Windows #44001227534](https://github.com/Devil-Jian/data-m/actions/runs/44001227534)：S3 旧测试仍断言最终 Schema=2；修改为验证 v3 且保留旧 ID、原始数据与迁移回滚断言，修复后 S3 17/17。
4. [Windows #44002434013](https://github.com/Devil-Jian/data-m/actions/runs/44002434013)：S1 篡改测试把正式 Schema v3 当非法值；改用未知未来 v4，仍验证错误拒绝和字节不变；最终 S1 14/14。
5. 最终 #44003979357 SUCCESS，非伪造零测试或取消工作流成功。

## Windows 原生人工验收清单（全部未完成）
- [ ] 下载与实测代码 SHA 匹配的 Artifact，计算 ZIP 与 EXE 各自 SHA-256，记录 Windows 版本、日期和操作者。
- [ ] 在 Windows 11 WebView2 中用可抛弃测试项目创建 Control/Model/Treatment，Control 六个生物学重复，建立分析 A（Control 5 个）与分析 B（Control 6 个）。
- [ ] 重启 EXE 重新查看两个分析，核对实际 n=6 而两次纳入 n 独立。
- [ ] 复制、移动、重命名 Raw 文件并新建资产版本，确认 n 与冻结的输入/结果版本均不漂移。
- [ ] 关联输入和结果 AssetVersion，冻结，尝试直接修改应拒绝；创建明确修订并检查审计历史。
- [ ] 软删除有关 AssetLink，确认历史冻结分析仍能查到固定版本；试验非法跨 Assay 样本与文件关联。
- [ ] 使用可抛弃数据库演练 Schema v1/v2 迁移、S7 历史 v2 快照验证和隔离恢复，保存哈希/ID/关系 count diff=0 证据。
- [ ] 保存人工截图、错误案例、EXE/源文件哈希，不得以浏览器 Mock 替代原生验收。

## 边界与遗留风险
- **S8 已达到机器放行标准，但未达到人工 accepted**；S7 同样依旧 manual_pending；不得合并 main。
- 未签名便携 EXE 不是正式安装包，Windows 11 实机、WebView2、DPI 与长期运行需要用户人工检查。
- S7 用户恢复演练与离盘备份签收之前，**禁止珍贵或唯一科研原始数据投入正式使用**。
- 不进行 t 检验/ANOVA 或数据计算，不修改原始文件/CAS 与现有 Panel 固定关联，不自动实施 S9–S11。
