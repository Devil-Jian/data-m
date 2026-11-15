# 本地开发基线

准备日期：2026-11-15。此记录用于本地开发，不代表 S0 已验收。

## 工作区与来源

- 正式开发工作区：`D:\paper\data-m-dev`（Git linked worktree）。
- 本地分支：`chore/s0-development-baseline`，尚未发布，不配置远端上游。
- 基础提交：`9a60c46751104b3ef87ac9b082b82dc0c08a955a`。
- 来源：`origin/docs/data-m-development-master-plan`；继承 `origin/refactor/p1-single-ui-package` 的完整工程，额外增加三份开发规划文档。
- 原工作区 `D:\paper\data-m` 保持在 `feat/file-interaction-engine`，其未跟踪文件保留。

## 日常开发

在 VS Code 中打开 `D:\paper\data-m-dev`，运行：

```powershell
cd D:\paper\data-m-dev
npm.cmd run dev:desktop
```

访问 http://127.0.0.1:5176 。只维护正式入口 `apps/desktop`、共享组件 `packages/ui`、业务组件 `packages/core` 和交互层 `packages/interaction`。

若终端继承的 PATH 尚未更新，可仅为当前终端补充：

```powershell
$env:Path += ';C:\Program Files\nodejs'
```

本机验证环境：Node.js v26.13.1、npm 11.20.0；现有 CI 使用 Node.js 22，二者并非完全相同的工具链。首次安装生成的根目录 `package-lock.json` 留待审查提交；后续按该锁文件使用 `npm.cmd ci`。

## 验证记录

- `npm.cmd install --no-audit --no-fund`：成功。
- `npm.cmd run check`：成功；包含交互测试 28 项、资产等测试 20 项、三套历史 UI 构建和正式 Desktop 构建。
- `npm.cmd run test:desktop`：成功，7 项测试。
- `npm.cmd run test:e2e:desktop`：成功，3 项测试。
- `npm.cmd run test:e2e`：成功，29 项测试。
- 浏览器测试以 `CI=1` 运行，避免复用其他工作区的既有服务。

## 尚未完成的 S0 工作

- 当前环境未找到 Rust/Cargo；Rust 测试、格式检查、编译及原生 EXE 构建未运行。Windows C++ 工具链与 WebView2 仍需核验。
- 根 README 和 CONTRIBUTING 仍含历史 UI 比选/旧技术路线说明，需要在 S0 中整理。
- 锁文件尚未提交，CI 尚未切换为基于锁文件的安装。
- `s0_` / `@S0` 专项测试与当前集成提交的 Windows CI 尚未验收。
- Windows WebView2 的拖放、DPI、焦点及源文件保护人工验收未执行。
- 未合并 main，未发布 PR，未开放真实文件写入；后续按 `AI_DEV_EXECUTION_SPEC.md` 逐项执行。
