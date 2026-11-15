# P1 · Explorer 右键菜单完整键盘访问（Issue #7 增量）

## 本轮行为

在文件工作区有焦点时：
- `Shift+F10` 或 Windows 菜单键（`ContextMenu`）打开当前焦点文件的右键菜单；如果未聚焦文件，则打开空白工作区菜单。
- 如果焦点文件已在多选集合内，菜单保留全部多选 Asset ID；若焦点文件未选，则只选择焦点文件。
- 初始焦点落在第一个可操作菜单项。菜单内部的 `ArrowUp`、`ArrowDown` 会循环切换，`Home` / `End` 跳到首/末可用项。
- 禁用项（例如多选时的重命名）不会获得键盘焦点。
- `Enter` / `Space` 使用原生按钮激活菜单操作，并将焦点返回 Explorer 工作区。
- `Escape` 只关闭菜单，不删除已选文件，也不会清空选择集合。
- 当搜索框或其他可编辑控件聚焦时，菜单键不会触发 Explorer 文件操作。
- 虚拟列表依赖逻辑 Asset ID 与可见 DOM 位置，`End` 跳到 10k 列表末尾后，菜单从当前焦点文件的位置弹出。

## 验证

```powershell
npm run test:interaction
npm run test:assets
npm run test:e2e
npm run build:desktop
```

Playwright 新增四项交互检查：Shift+F10 焦点循环、菜单键保留双文件多选、输入框隔离、10k 虚拟列表末项菜单。Windows Chromium CI 也运行同一套测试。

## 安全边界和剩余工作

- 当前仅支持演示 Asset ID。复制/移动/剪切/删除与重命名**没有文件写入副作用**；没有与系统剪贴板同步。
- 菜单焦点循环遵循常见键盘导航规则，但完整 Windows 11 WebView2 的高 DPI、屏幕阅读器和 GUI 实机验收尚未执行。
- 右键菜单不会保留跨项目的操作队列；未来必须由 P2 Rust/SQLite/CAS 后端校验目标、事务与引用。
- [Issue #7](https://github.com/Devil-Jian/data-m/issues/7) 继续保持 Open，不能因为浏览器 E2E 通过而称为真实文件操作验收完成。
