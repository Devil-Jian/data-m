# S0 执行与验收报告

状态：`accepted`。用户于 2026-11-15 明确确认人工验收通过；PR #42 已合并 main，合并提交 `937762fe068decfc5bdc9765ba945267832d9282`。以下机器证据与历史风险记录保留，最终签收以本段和文末记录为准，关联 Issue 未自动关闭。

## 范围与基线

- TASK_ID=S0，依据 `AI_DEV_EXECUTION_SPEC.md` 和 `DATA_M_AI_TASKS.json`。
- base_sha：`9a60c46751104b3ef87ac9b082b82dc0c08a955a`。
- 分支：`chore/s0-development-baseline`；独立 Draft PR 目标为 `docs/data-m-development-master-plan`，依赖 PR #41/#40，不整体合并历史链。
- 本次完成共享 UI 约束检查、npm 锁文件、CI 安装和测试入口、README/ADR/工具链说明、只读 IPC 防回归、PR #35 显示名安全修复。
- PR #35 原有用例包含字面量转义，不能证明真实控制符清洗；本次使用真实 Unicode/换行字符。
- 原 CSS 颜色断言被字面量换行包进注释，本次恢复执行。

## 机器验收

独立 Draft PR：[#42](https://github.com/Devil-Jian/data-m/pull/42)。初次验证提交 `c063bfd30fb468108f38beaee1f7df29442378b9`；后续提交补充依赖锁定与审查修复，最终证据在检查结束后记录。

本机 Node.js v26.13.1 / npm 11.20.0；CI 使用 Node.js 22。以下命令已实跑，退出码均为 0：

| 命令 | 结果 |
| --- | --- |
| `npm ci --no-audit --no-fund` | 从锁文件重装成功 |
| `npm run test:s0` | 3 项工程边界检查 |
| `npm run check` | 28 项交互、20 项资产等测试；三套历史 UI 与 Desktop 构建成功 |
| `npm run test:desktop` | 7 项测试 |
| `npm run test:e2e:desktop -- --grep "@S0"` | 2 项专项测试 |
| `npm run test:e2e:desktop` | 3 项测试 |
| `npm run test:e2e` | 29 项测试 |
| `powershell -NoProfile -File scripts/test-p1-drop-source.ps1 -SelfTest` | 源文件校验器自检通过；不证明原生 GUI |

发现并修复 Windows PowerShell 5.1 解析无 BOM 中文 UTF-8 脚本失败；修复后上述自检通过。独立审查未发现开放写 IPC，要求补齐 Cargo.lock 与防止 Rust 专项空跑，已纳入后续验证。

Windows 首轮 CI：[run 43727800015](https://github.com/Devil-Jian/data-m/actions/runs/43727800015) 的 4 项 S0、21 项 Rust 测试通过，但 rustfmt 失败，整轮失败；依据实际格式差异修正后重新验证，未放宽断言。

最终实现提交：`1c4765a1506f7d9f813bb45ff2fb8c1800bb39e1`。后续验收文档提交只更新证据与状态，不修改可执行代码、依赖或测试。

- [最终 Windows CI](https://github.com/Devil-Jian/data-m/actions/runs/43728489141)：整轮 `success`；使用 Node.js 22、已提交的 npm/Cargo 锁文件，完整 JS/Rust/浏览器检查、格式检查、cargo check 与 EXE 构建全部通过。
- [最终 Linux UI CI](https://github.com/Devil-Jian/data-m/actions/runs/43728489120)：三套历史 UI 构建与 Explorer 回归成功。
- Rust 专项实际包含 4 项，全量包含 21 项；CI 先检查专项发现数量非零，再执行测试。
- 同 SHA 的重复 push 流水线 `43728484414` 为节省资源主动取消；以上 PR 流水线才是验收依据。
- 便携 EXE artifact：`data-m-p1-unsigned-portable-windows`（沿用既有命名，不代表正式 P1 发布），位于最终 Windows CI。
- 已下载 EXE 至 `D:\paper\s0-artifacts-1c4765a\data-m-desktop.exe`，SHA-256：`5B86CB4ED6DAD971DFBF0051417F2E1FD6FE59BAC361EC5347084B535C4D356B`。未运行 GUI，哈希不能替代人工验收。
- 独立复审确认 Cargo 锁定、防空跑和只读 IPC 边界；未发现剩余 critical/important 代码问题。

结构化命令、退出码和用例数量见 [机器证据 JSON](S0_EXECUTION_REPORT.json)。`head_sha` 指实际受测实现提交，最后的文档提交仅记录结果。本机 Rust 下载未完成且无 C++ 工具链，未将本机原生验证报告为通过；上述原生结果来自同 SHA 的真实 Windows CI。

| S0 条件 | 证据与限制 |
| --- | --- |
| M1 | 候选集成分支含完整工程，npm ci 从零安装及 CI 通过；main 合并与集成签收未获批准，保持待审批 |
| M2 | Desktop 与兼容浏览器入口共用 packages/ui；正式浏览器与原生配置使用同一 apps/desktop；专项工程与 CSS/交互检查通过 |
| M3 | 对上述实现 SHA 的 PR 流水线检查并输出 EXE；原生 GUI 仍需人工 |
| M4 | 根 README、CONTRIBUTING、ADR 与 S0_TOOLCHAIN 给出安装、双模式预览与测试命令；历史路线保持隔离 |

## Windows 人工验收（用户已确认通过；保留复验步骤）

1. 从本 PR 成功的 Windows CI 下载未签名便携 EXE；记录 CI URL、head SHA、EXE SHA-256（`Get-FileHash`）、Windows 11 版本及执行者/日期。不要用旧构建代替。
2. 使用可抛弃测试数据。100% 与 150% DPI 各启动一次，检查布局、文字、缩放、Tab 焦点；记录截图。
3. 验证单选、Ctrl 多选、Shift 连选、空白框选、右键、F2 后 Esc 取消；确认演示操作不写真实文件。
4. 根目录运行 `powershell -File scripts/test-p1-drop-source.ps1`，按脚本提示拖入生成的中文/空格文件与目录，查看只读预览并关闭。脚本应确认 SHA-256、大小、修改时间及目录内容不变。失败须保留日志，不能签收。
5. 测试无效/不可读取路径的错误提示、连续拖入及 Esc 取消，界面可恢复；不显示绝对路径或控制字符，不提供真实导入成功提示。
6. 运行 `npm.cmd run dev:desktop`，对比 5176 浏览器与同 SHA 的 WebView2 页面；确认共享布局、控件颜色与菜单一致，无崩溃。浏览器不能模拟原生验收。
7. 保存截图/录屏、脚本结果、EXE/源文件哈希及实际/预期对照，由用户签字。未通过项保持待验收，禁止自动合并 main。

## 剩余风险

- 本机缺少 Windows C++ 工具链，原生机器验证依赖 PR 的 Windows CI；人工 GUI 尚未执行。
- main 仍为初始化主线；本次仅提交候选集成 PR，不代表已获准合并或满足 M1 主线批准条件。
- SQLite/CAS 仍是隔离原型，启动不初始化用户数据库，未注册写入 IPC。
- S1–S11 不在本次范围，下游不能依据本次 Draft PR 宣称 S0 accepted。

## 用户人工签收

2026-11-15（Asia/Singapore），用户在当前会话明确确认“S0 验收通过，合并到 main”。按用户确认记录 H1–H3 人工验收通过；未另行收到截图、Windows 版本或录屏，不虚构这些附件。PR 已改以 main 为目标；本记录提交时等待实际合并，合并成功后记录最终 accepted 状态。
