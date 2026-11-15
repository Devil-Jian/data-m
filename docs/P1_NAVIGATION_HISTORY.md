# P1 · Explorer 目录历史（Issue #7、#13）

> 本功能管理 **Data-M 窗口内部** 的目录/模块历史。不是浏览器网页历史，不读取或修改任何科研文件。

## 已实现的行为

- 项目、主题、实验、分组、论文模块、论文 Panel 的导航目标以 **稳定 ID** 记录在会话历史中。
- 顶部新增“后退到上一目录”“前进到下一目录”按钮，并支持 `Alt+←` / `Alt+→`。
- 返回与前进恢复对应的项目、主题、实验、分组、论文与 Panel 状态，并清除原文件选择和搜索关键字，避免跨目录误操作。
- 相同位置重复点击不增加历史条目；后退后选择新位置，会截断原先的前进分支。
- 最多保留最近 50 个记录。项目或实验实体不存在时，自动跳过无效目标，避免路由错误回退到另一个项目的默认目录。
- 搜索文本、文件多选、过滤与表格滚动不形成新的目录访问记录。
- 当文本输入框获得焦点时，`Alt+←/→` 不执行 Data-M 内部目录导航。
- 对于当前没有可后退/前进目标的情况，按钮显示禁用状态，应用内快捷键不会使宿主浏览器意外离开应用。

## 实现文件

- `packages/core/src/navigation-history.ts`：纯函数历史栈、固定容量、前进截断及已失效目标检查。
- `packages/core/src/navigation-history.test.ts`：前进/后退边界、路径复用、容量限制、无效 ID 跳过。
- `packages/core/src/workbench.tsx`：同步实际 UI 目录状态，应用导航按钮及快捷键。
- `tests/e2e/explorer.spec.ts`：分组前进后退、模块切换、前进截断、选择不增加历史条目。

## 运行验证

```powershell
npm.cmd install
npm.cmd run test:assets
npm.cmd run test:interaction
npm.cmd run test:e2e
npm.cmd run build:desktop
```

Windows CI 另外执行 Rust 测试与桌面二进制构建。浏览器 Playwright 成功 **不能替代** Windows WebView2 GUI 真机验收。

## 安全与剩余范围

- 只记录 UI 位置 ID，历史不持久化、不存储 DICOM/生物样本信息、路径或文件内容。
- 当前仍为演示 Asset ID；生产环境应在 Rust/SQLite 内以稳定 Asset/Container ID 解析导航并处理版本变更。
- 不启用任何实际文件复制、重命名、剪切、删除或移动；菜单/剪贴板仍仅产生模拟意图。
- 后续：Windows WebView2 Alt 键兼容性、高 DPI 与屏幕阅读器检查；跨应用会话恢复；实际路径/Asset 关系对接需通过 P2 安全设计。
