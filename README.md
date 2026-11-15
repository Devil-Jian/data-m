# Data-M

Windows 11 单机、离线优先的科研实验与论文归档客户端。

正式路线：**React / TypeScript / shadcn/ui + Tauri 2 / Rust + SQLite / CAS**。这是 S0 集成候选，尚未人工验收或合并 main。SQLite/CAS 仅为原型；原生 IPC 只开放状态查询和只读拖入检查，不能导入、移动或删除真实科研文件。

## 从零安装与预览

安装 Node.js 22（与 CI 一致）、npm 和 Git，检出此 PR 分支。在根目录执行：

```powershell
git clone --branch chore/s0-development-baseline https://github.com/Devil-Jian/data-m.git data-m-dev
cd data-m-dev
npm.cmd ci
npm.cmd run dev:desktop
```

访问 http://127.0.0.1:5176 。原生 WebView2 热更新需先配置 [Windows 工具链](docs/S0_TOOLCHAIN.md)，然后运行 `npm.cmd run tauri:dev`。

两种模式均加载 `apps/desktop/src/main.tsx`，复用 `packages/ui`。浏览器不能替代 Windows 原生拖入和 DPI 验收。

## 验证

```powershell
npm.cmd run test:s0
npm.cmd run check
npm.cmd run test:desktop
npx.cmd playwright install chromium
npm.cmd run test:e2e:desktop -- --grep "@S0"
npm.cmd run test:e2e:desktop
npm.cmd run test:e2e
cd apps/desktop/src-tauri
cargo test --locked --no-default-features s0_
cargo test --locked --no-default-features
cargo fmt --all -- --check
cargo check --locked
```

回到根目录执行 `npm.cmd run tauri:build` 生成未签名便携 EXE；`bundle.active=false`，不是安装程序。

## 工程边界

| 目录 | 职责 |
| --- | --- |
| `apps/desktop` | 唯一正式 UI 入口与 Tauri 容器 |
| `packages/ui` | 唯一 shadcn 组件、适配器和 Tailwind 样式 |
| `packages/core` | 业务视图与演示数据 |
| `packages/interaction` | Explorer 选择、键盘和拖动意图 |
| `apps/desktop/src-tauri` | 只读 IPC，未开放 SQLite/CAS 写入 |
| `demos/*` | 历史比选；shadcn 兼容入口复用共享 UI |

不合入历史 WPF/WinUI/Vue 路线。Replicate、DataAsset、AssetLink、AssetVersion 分别建模；文件操作不能增加生物学重复。S0–S7 验收与恢复演练之前禁止使用珍贵科研数据。

按 [执行规范](docs/AI_DEV_EXECUTION_SPEC.md) 和 [机器任务](docs/DATA_M_AI_TASKS.json) 一次执行一个任务。参见 [S0 报告](docs/S0_EXECUTION_REPORT.md)、[ADR](docs/adr/0001-desktop-stack.md) 和 [贡献规范](CONTRIBUTING.md)。
