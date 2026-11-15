# P1 文件资源管理器键盘导航（Issue #13 / #5）

## 本阶段新增

- 在文件工作区获得键盘焦点后支持 ArrowUp / ArrowDown / Home / End / PageUp / PageDown。
- 普通方向键：移动焦点并单选；Shift+键：从稳定锚点连续选择；Ctrl+键：仅移动焦点，不改变当前选择；Ctrl+Shift+键：保留既有选择并追加范围。
- 键盘导航以当前可见逻辑 Asset ID 顺序为基准，不依赖对应文件行是否已被 TanStack Virtual 挂载。
- 在 Raw / Processed 虚拟列表中，焦点跳到离屏资产时自动滚动；焦点边框与选中背景独立渲染。
- 只在文件工作区获得键盘焦点时处理导航键；搜索、文本编辑等控件不应被拦截。
- 在操作模拟文件时不会创建 Replicate 或修改真实文件；稳定 ID 依然是演示模型，待 #14 对接 SQLite。

## 自动化验证

1. `npm run test:interaction`：方向键与范围选择、Ctrl 焦点模式、Home/End、PageUp/Down 和 10k ID。
2. `npm run test:e2e`：已选项方向键、Ctrl+方向键焦点变化、Shift 扩展、Home/End 自动滚动、搜索输入框快捷键隔离。
3. Windows CI：TypeScript、Chromium E2E、Rust 检查与桌面构建。

## Windows 11 / WebView2 实际验收（尚未完成）

- [ ] DPI 100%、125%、150% 下键盘与可见焦点位置一致。
- [ ] 文件点击后 ArrowDown、Shift+Down 与 Ctrl+Down 均符合 Windows Explorer 常见行为。
- [ ] 进入 10k 本地模拟资产，End 跳到最后一个文件，Home 返回首项，不渲染全部 DOM 行。
- [ ] 单击搜索框后 Ctrl+A 和方向键只影响编辑/输入，不改变文件选中状态。
- [ ] Esc 取消拖放后不触发任何文件操作。
- [ ] 实机 Tauri WebView2 运行通过，而非仅 Chromium 浏览器模拟通过。

## 约束

- `data-explorer-id` 使用演示 Asset ID，不等于数据库正式 ID。
- 当前键盘事件绑定到文档，但**导航键仅在文件工作区具有焦点时**生效；现有 Ctrl+C/X/V 等快捷键仍在文档级作用域，未来右键菜单和多窗口管理需按 #7 进一步统一。
- 虚拟列表固定行高 56px；复杂双轴滚动、可访问性增强仍留在后续任务。
