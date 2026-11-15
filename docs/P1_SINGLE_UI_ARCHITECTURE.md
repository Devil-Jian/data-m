# Data-M 正式前端收敛：单一 UI 源码

## 决策

- **正式前端入口**：`apps/desktop/src/main.tsx`。
- **唯一 shadcn UI 组件与 Tailwind 样式**：`packages/ui/src`、`@data-m/ui/style.css`。
- **业务组件**：`packages/core`（Workbench 和科研逻辑）。
- **交互引擎**：`packages/interaction`。
- **Windows 容器**：`apps/desktop/src-tauri`，并非第二套前端。
- `demos/shadcn` 暂时保留兼容性入口，通过**同一个 `@data-m/ui` 适配器**加载共享 Workbench；不再存放单独的 Button/Select/Input/Checkbox/Badge 源码。
- Mantine 和 Fluent 演示保留为历史比较，**不参与**桌面正式构建，也无需随正式 UI 同步修改。

## 本 PR 改动

1. 新增 `packages/ui` 包，迁入原先 `demos/shadcn/src/components/ui` 中的组件及工具函数。
2. `packages/ui/src/workbench-ui.tsx` 统一导出 `shadcnUI`，供 EXE 前端和原 shadcn 演示复用。
3. `packages/ui/src/style.css` 成为唯一 Tailwind v4 入口，并显式扫描 UI、core、desktop 源代码。
4. 删除 shadcn 演示目录下重复组件，避免将来修改一个按钮需要改两套。
5. Desktop 的直接依赖改为 `@data-m/ui`；底层 Radix / Tailwind 工具归组件包管理。
6. 修正 Desktop CSS E2E 对 Tailwind v4 `oklch()` 颜色表示的错误断言。

## 开发流程：一套 UI、三种执行模式

```powershell
npm install
npm run dev:desktop           # http://127.0.0.1:5176, Vite HMR；不编译 Rust
npm run tauri:dev             # WebView2 热更新；首次及 Rust 变动可能编译 Debug
npm run tauri:build           # 仅确认 UI 与原生验收后执行正式 EXE 构建
```

**日常只维护 `apps/desktop` + `packages/ui` + `packages/core` + `packages/interaction`。** 使用 `dev:shadcn` 的 5173 时也会加载同一套 UI，但它只是历史入口，不应被用来作为未来产品 UI 的开发目标。

## 安全与验收

```powershell
npm run build:desktop
npm run build:shadcn
npm run test:interaction
npm run test:e2e:desktop
npm run test:e2e
npm run tauri:build
```

Browser E2E 并不等于 Windows WebView2 的 DPI/缩放/原生文件拖入验收。真实文件操作仍全部保持 Demo-only；不要在 P2 事务/备份与恢复验证前将其开启。

此 PR 属于 UI 结构收敛，后续 PR 可逐步清理历史 Demo 文档和多 UI 对比脚本；不在本次变更中移除 Mantine/Fluent 供参考的入口。
