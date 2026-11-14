# ADR-0001：选择 React/shadcn/Tauri/Rust 作为 Data-M 主开发路线

- 状态：Accepted for current roadmap（仅代表本轮开发方案，不代表已与全部既有草稿合并）
- 日期：2026-11-14
- 关联：#8、#12

## 背景

Data-M 是 Windows 11 单机科研实验与论文归档客户端。用户选择 shadcn 风格，并要求保留 Gemini 原有的双模块布局与五级实验/四阶段数据视图，同时支持资源管理器级文件交互。

仓库中另有 WPF、WinUI 3、旧 Tauri/Vue 的独立原型及草稿 PR。它们可作参考，但没有作为当前选定 React 路线的实现基线。

## 决策

- Frontend：React + TypeScript + Vite + shadcn/ui。
- Desktop：Tauri 2 / Windows WebView2。
- Backend：Rust application/domain/infrastructure。
- Metadata：SQLite。
- Files：CAS SHA-256、独立 DataAsset/AssetVersion。
- Internal interaction：Pointer Events/可测试的 Selection 和 Drag intent；Windows 文件拖入经 Tauri。

## 理由

现有 shadcn Demo 已有业务布局，Tauri 能复用 React；Rust 可以在本地直接执行大文件安全处理，避免另外维护 Python 服务进程；SQLite 适合单机数据目录。

## 风险及代价

- WebView2 的原生拖放与 HTML5 drag events 需要明确边界；
- 完整拖出至 Windows Explorer 需要原生 OLE/COM 集成，属于以后独立任务；
- Rust 业务层需要严格事务、恢复及跨平台/Windows E2E 测试；
- 历史 WPF/WinUI 数据和迁移代码可能有可复用概念，但不能直接假定兼容。

## 反事实选择

- Electron：生态成熟，但封装体积和运行时开销通常更高。
- WinUI 3 / WPF：Windows 原生控件更深入，但重写 React GUI 成本更高。
- FastAPI sidecar：可用，但本项目第一版无必要多维护一个常驻后端进程。
