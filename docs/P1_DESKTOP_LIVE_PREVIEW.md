# Data-M 前端实时预览与 Windows EXE 验收流程

## 目标

采用 **一份正式前端**（`apps/desktop/src/main.tsx` → `Workbench` + shadcn 组件），浏览器和 Windows EXE 使用完全一致的组件及样式。开发期间优先采用 Vite HMR 热更新，不再通过反复打包 EXE 来检查 UI。

## A. 纯浏览器实时预览（推荐日常使用，零 Rust 编译）

Windows PowerShell：

```powershell
# 在工作副本仓库根目录
npm install
npm run preview:desktop
```

打开 **http://127.0.0.1:5176**。任何 React/CSS/组件改动会由 Vite 自动热更新；没有调用任何真实文件写入 IPC。

注意：`npm run dev:shadcn` 的 5173 是 UI 比选演示，`npm run preview:desktop` 的 **5176 才是 Windows EXE 使用的前端入口**。正式 UI 修改应优先查看 5176，避免编辑了一套 demo 而 EXE 未同步。

日常前端验收：

```powershell
npm run build:desktop
npm run test:e2e:desktop
npm run test:e2e
```

`build:desktop` 只做 TypeScript/Vite 构建，**不**进行 Rust Release EXE 打包。新增的 desktop E2E 在 5176 检查 shadcn 组件计算样式、文件右键菜单和键盘重命名意图。现有 Explorer E2E 继续在 5173 运行。

## B. Windows 原生 WebView2 预览（仅测试原生行为）

```powershell
npm run preview:desktop:windows
# 等价于 npm run tauri:dev
```

Tauri 开发态可能在首次启动或 Rust 代码改动时**编译 Debug Rust**，但前端 React/CSS 修改由 Vite HMR 自动刷新，**不会每次重新构建 Release EXE**。

只有以下行为要求原生 WebView2 验收：Windows Explorer 外部文件拖入、原生窗口缩放/DPI、多显示器、焦点/系统快捷键、Windows 文件选择权限、桌面 WebView2 的原生 CSS 实测。浏览器预览不能取代原生验收。

## C. 最终一次性打包与发布前验收

当 5176 的布局、右键/快捷键和桌面原生交互均确认无误后：

```powershell
npm run check
npm run test:e2e:desktop
npm run test:e2e
npm run tauri:build
```

仓库当前 `tauri.conf.json` 中 `bundle.active=false`，`tauri:build` 只生成 Windows EXE（通常位于 `apps/desktop/src-tauri/target/release/`），**并非 NSIS/MSI 安装程序**。正式安装、升级与备份验证属于 P4。

## UI 样式一致性修复

`demos/shadcn/src/style.css` 增加 Tailwind v4 `@source` 声明，显式扫描 shadcn、core 和 desktop 跨 workspace 源码，避免构建 Desktop Vite 时由于扫描路径差异丢失 `bg-blue-600`、`rounded-md`、`px-3` 等组件实用类，出现原生无样式 HTML 按钮或输入框。

## 右键与快捷键的正确触发范围

- **文件资产区域**：右键文件行/卡片/图片预览或文件内部控件，打开自定义文件菜单；复制、剪切、删除、F2 仅产生演示意图，不操作磁盘。
- **文件工作区空白处**：右键打开当前分组菜单。
- **工具栏、实验分组按钮、搜索框、论文导航**：不应错误打开文件操作菜单。浏览器原生右键菜单是正常行为。
- **快捷键作用域**：需先单击文件使 Explorer 获得焦点；`F2` 为单文件重命名预览，`Shift+F10`/ContextMenu 键为文件菜单，`Ctrl+A/C/X/V`/Delete 仅针对 Explorer。输入框和其他页面不应被快捷键劫持。

## 开发与安全边界

浏览器/桌面开发预览均为演示数据。SQLite/CAS 目前没有正式 Tauri 写入接口，原始科研文件不能被导入/删除/移动。EXE 的最终像素一致性仍需要 Windows WebView2 实机截图与 DPI 对比。

本流程将在 Review/CI 完成后合并；Draft PR 不等于已在 `main` 中发布。
