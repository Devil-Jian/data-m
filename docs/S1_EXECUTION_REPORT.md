# S1 项目、主题、实验管理：执行报告

状态：`manual_pending`。2026-11-16 机器验证通过，Windows 人工未签收，S1 未合并；只执行 S1，不推进 S2。

## 基线与范围

- 依赖 S0 已通过用户人工验收并经 PR #42 合并 main；PR #43 补齐最终签收状态。
- S1 分支：`feat/s1-project-tree`；base_sha：`8a7d2f9c5ed584caa13fbca64de69771da7d08c0`。
- Project → Theme → Assay 创建、重命名、软删除和查询；不添加生物学重复或文件资产操作。
- 结构化 `project_tree` IPC；SQLite 事务、requestId 幂等、稳定节点 ID、父级与非空删除保护。
- 仅创建应用目录下独立 S1 测试数据库，拒绝未知既有数据库，不做迁移或修复。
- React 仅查询与提交操作；浏览器 Mock 明确标注，不能替代真实 Native IPC 及 Windows 人工验收。

## 机器验证

本机 Node.js v26.13.1：`npm run test:s0` 3 项工程检查、`npm run check` 的 48 项单元测试和四套构建、`npm run test:desktop` 7 项测试、`npm run test:e2e:desktop` 10 项（含 7 项 @S1）、`npm run test:e2e` 29 项均已实跑，退出码 0。原生 Rust/IPC 仍待本 PR 的 Windows CI；不把浏览器 Mock 作为原生成功证据。

审查发现首次初始化直接发布空数据库会使初始化失败后的重试永久被拒绝。已改为先初始化受控临时库、完成后独占发布；增加初始化故障/重试和未知目标不可覆盖回归。以下记录实际执行结果，历史失败轮次不计为通过。

最终实现提交：`de9644e7987c96d9aaa615f04ad0efafde45e05d`。独立 [Draft PR #44](https://github.com/Devil-Jian/data-m/pull/44)，目标 main。后续文档提交仅记录证据，不改可执行源、依赖或测试。

- [Windows CI 43733131429](https://github.com/Devil-Jian/data-m/actions/runs/43733131429)：整轮 success；Node.js 22 与锁定的 npm/Cargo 依赖。
- [Linux UI CI 43733131389](https://github.com/Devil-Jian/data-m/actions/runs/43733131389)：整轮 success。
- 14 项 S1 Rust 测试（12 项真实库测试、2 项真实命令处理器与临时库测试）；全量 Rust 35 项（包含 S1），0 失败、0 忽略；S0 专项 4 项。
- Desktop 浏览器 10 项（含 S1 Mock 7 项）和 Explorer 29 项；JS 单元 55 项、工程检查 3 项。
- Rust fmt、cargo check、Desktop 与历史 UI 构建、Windows 便携 EXE 构建均通过。失败诊断步骤只在失败时运行，成功流程正常跳过诊断，不跳过验收测试。
- SQLite 原始建表 SQL 会保留 LF/CRLF，已复现并修正仅换行不同导致自己的数据库被误拒绝的问题，新增双变体识别回归。
- 独立复审确认初始化故障已修复，未发现剩余 critical/important 代码问题；GUI 验收不在自动测试证明范围内。

逐条命令、退出码、用例数、源码 SHA 和日志链接见 [结构化机器证据](S1_EXECUTION_REPORT.json)。

## 本地验收产物

CI artifact：`data-m-s1-unsigned-portable-windows`。已下载至 `D:\paper\s1-artifacts-de9644e\data-m-desktop.exe`，未启动 GUI。

EXE SHA-256：`5ADFC11AC41E4F5C22FDD1CFFBBA53BDD0D176D6A592964544234AB10AF3542B`。这是未签名便携测试程序，不是安装器。请勿使用旧 S0 EXE 验收 S1。

项目管理首次调用时才创建应用本地数据目录下的 `s1-test-workspace/project-tree.sqlite`，不在启动时初始化用户数据库。Windows 默认父目录由 Tauri 的应用标识 `com.deviljian.datam` 决定。

原生 handler 集成采用 Tauri MockRuntime + 真实临时 SQLite 库，测试请求序列化、命令注册、持久化及失败回滚；这不是 WebView2 GUI 验收。[Tauri 官方 IPC 测试接口](https://docs.rs/tauri/latest/tauri/test/fn.get_ipc_response.html)

Windows CI `43732333032` 曾编译成功但测试二进制在发现用例前以 `0xc0000139` 退出，不能计为测试通过。调查对应 Tauri 的 Windows Common Controls 清单仅链接 bins、未进入单元测试二进制的已知问题；按维护者方案将相同的 Common Controls v6 清单通过 MSVC 链接器嵌入所有产物，不删除或跳过 IPC 测试。最终 Windows CI 已验证原生测试与 EXE 链接全部成功。[上游问题与维护者方案](https://github.com/tauri-apps/tauri/issues/13419)

## Windows 人工验收（manual_pending）

1. 使用本次 S1 CI EXE，记录源码 SHA、EXE SHA-256、Windows 版本、日期和执行者，不使用旧 S0 EXE。
2. 点击“项目管理”，确认标注为原生持久化模式，首次受控测试库为空。浏览器没有注入 Mock 时应提示原生接口不可用，不显示假成功。
3. 创建两个项目，每个创建多个主题和实验；重命名各级节点。关闭 EXE 再打开并进入管理页，逐项比对名称、数量和层级；保存前后截图。
4. 测试空名称和删除非空项目/主题，必须给出明确错误；原树保持不变。删除空实验、空主题及空项目后重启确认软删除结果。
5. 在专门构造、可丢弃的测试环境验证未知数据库被拒绝，前后哈希不变；不要替换任何已有真实科研数据库。异常及拒绝结果保留日志。
6. 保存实际结果和截图、测试库检查记录。通过后由用户明确签收，未签收前保持待验收；不自动合并 S1。

## 剩余风险

本机无完整 C++/Rust 开发环境，原生检查依赖本 PR 的 Windows CI。GUI 与数据库恢复的人工证据尚未执行。S0–S7 与恢复演练全部签收之前禁止珍贵科研数据日常入库。
