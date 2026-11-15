# S0 执行与验收报告

状态：`in_progress`。本报告不是 accepted 签收；未合并 main，未关闭关联 Issue。

## 范围与基线

- TASK_ID=S0，依据 `AI_DEV_EXECUTION_SPEC.md` 和 `DATA_M_AI_TASKS.json`。
- base_sha：`9a60c46751104b3ef87ac9b082b82dc0c08a955a`。
- 分支：`chore/s0-development-baseline`；独立 Draft PR 目标为 `docs/data-m-development-master-plan`，依赖 PR #41/#40，不整体合并历史链。
- 本次完成共享 UI 约束检查、npm 锁文件、CI 安装和测试入口、README/ADR/工具链说明、只读 IPC 防回归、PR #35 显示名安全修复。
- PR #35 原有用例包含字面量转义，不能证明真实控制符清洗；本次使用真实 Unicode/换行字符。
- 原 CSS 颜色断言被字面量换行包进注释，本次恢复执行。

## 机器验收

执行结果、源码 SHA 和 CI 链接在检查结束后补充。机器验收未全部通过前不标记 machine_passed 或 manual_pending。

## Windows 人工验收（全部 manual_pending）

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
