# P1 · TanStack Virtual 文件列表集成

范围：Issue #13 / #5；在不引入真实文件写入的情况下，把实际虚拟滚动列表接到共享 Explorer Selection Engine。

## 已实现

- 分阶段 Raw / Processed 页增加“虚拟文件列表”按钮，与原卡片模式切换。
- `packages/core/src/virtual-asset-list.tsx` 使用 `@tanstack/react-virtual` 渲染固定 56px 行和少量 overscan 行。
- `packages/core/src/virtual-layout.ts` 计算所有资产的 **content-space** 矩形，不依赖其 DOM 是否存在。
- `packages/interaction/src/useExplorerInteraction.ts` 仅在虚拟容器标记 `data-explorer-virtual=true` 时使用该布局，否则沿用 DOM hit-test。
- 同一批 Asset ID 在卡片/列表共享选择集合，Ctrl+A / Shift 连选范围不依赖实际挂载的 DOM 行。
- `?virtualDemo=10000` 是 **localhost-only** 的压力测试数据（10k 虚构文件），它不会创建任何 Replicate、更改统计 n、写入文件或 DB；不是 10k 真实文件导入。
- 新增 Vitest 10k 布局/离屏命中/跨屏选择测试，以及 Playwright 限制 DOM 数量和跨 10k 范围选择测试。

## 快速验收

```powershell
npm.cmd install
npm.cmd run test:assets
npm.cmd run test:interaction
npm.cmd run test:e2e
npm.cmd run build:desktop
npm.cmd run tauri:dev
```

在开发预览 http://127.0.0.1:5173/?virtualDemo=10000 选择“分阶段 → Raw → 虚拟文件列表”；检查滚动至末尾后 DOM 文件行数量保持有界，Ctrl+A 或首尾 Shift 选择显示“10000 个文件资产”，实验组样本数仍为 6。

## 设计取舍

- 选择引擎与 TanStack Virtual 解耦；提供逻辑布局矩形适配器。
- 使用固定行高保证几何与实际 CSS 一致；动态高度虚拟网格以后另设任务。
- 文件对象使用 DemoAsset 演示身份；P2 引入真实 SQLite/CAS AssetId 和 AssetVersionId 后替换数据源。
- 当前列表的选择/拖动仍只展示业务意图，不能触碰用户科研文件。
- Chromium/Linux E2E 与 Windows CI 通过也不代表原生 WebView2 GUI 实机验收完成。

## 后续

- Windows 11 WebView2 实机验证 DPI、指针取消、拖拽、最大化/窗口切换。
- 真实 10k 文件/缩略图与 I/O 性能测量。
- 复杂双轴表格、动态行高、无障碍键盘焦点与可见滚动导航。
- 后端 AssetId / AssetVersionId 持久化对接（#14）。
