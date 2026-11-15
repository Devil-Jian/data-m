# S0 工具链与复现

- Windows 11，Git、Node.js 22（CI 基线），npm；根目录使用已提交锁文件运行 `npm.cmd ci`，不要复制旧工作区 node_modules。
- Rust stable MSVC 工具链（含 rustfmt），Visual Studio 2022 Build Tools 的“使用 C++ 的桌面开发”和 Windows SDK；本机未配置 C++ 时，不能声称原生验证通过。
- WebView2 Runtime；安装开发工具后完整退出并重开 VS Code，检查 `node --version`、`npm.cmd --version`、`cargo --version`、`rustc --version`。
- 仅浏览器 UI 开发不需要 Rust/C++。运行 `npm.cmd run dev:desktop`；原生热更新运行 `npm.cmd run tauri:dev`。
- Playwright 首次运行：`npx.cmd playwright install chromium`。Linux CI 使用 `--with-deps`。
- Rust 工作目录为 `apps/desktop/src-tauri`，依赖使用 Cargo.lock，测试/check 使用 `--locked`。
- 全量检查见根 README。S0 的 `s0_` 与 `@S0` 筛选必须执行非零用例。
- CI 的 EXE 是未签名便携测试程序，不是安装器。人工验收必须记录 EXE SHA-256、源码 SHA、Windows 版本、截图及原文件校验。

## 依赖与主线审查

候选基于 `9a60c46`，继承 PR #40 统一 UI 和 PR #41 文档。PR #36–#39 的 SQLite/CAS/桌面原型仍未合并，不能视为正式存储能力。PR #35 的显示名清洗未被该链继承，S0 单独补回并使用真实 Unicode 控制字符测试。不会整体合并历史 PR 链。
