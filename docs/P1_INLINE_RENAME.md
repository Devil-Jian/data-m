# P1 · Windows F2 文件重命名交互与安全校验

本增量属于 [Issue #7](https://github.com/Devil-Jian/data-m/issues/7)。目前仅演示 Asset ID 的文件名编辑**候选值**，不进行实际磁盘重命名。

## 实现

- 点击一个文件后按 `F2`，或通过右键菜单选择“重命名”，在该文件所在位置出现内联输入框。
- 原文件的扩展名默认不选中，初始光标选中主体名称，便于保持原始科研格式。
- `Enter` 检查候选名称：不能为空、不能包含 Windows 不允许的字符或控制字符、不能以空格或句点结尾、不能是 CON/PRN/AUX/NUL/COM1–9/LPT1–9 等设备名、不能超过 255 个字符。
- 为防止误改数据类型，本 P1 演示禁止更换扩展名。仅在当前文件所在的逻辑容器、期相/阶段内，检查大小写不敏感的文件名冲突。
- 候选有效时，显示明确的“演示重命名请求，未修改真实文件”通知。Asset ID、Replicate ID、源文件名和任何磁盘内容均保持不变。
- `Escape` 或编辑失焦会放弃候选修改，恢复 Explorer 工作区焦点。
- 支持 Raw/Processed 的全景矩阵、表格、卡片/虚拟列表，以及统计、展示文件的编辑入口。
- 10,000 个虚拟条目的输入只挂载在当前被编辑的可见文件上，不会渲染整个列表。

## 验证

```powershell
npm.cmd install
npm.cmd run test:assets
npm.cmd run test:e2e
npm.cmd run build:desktop
```

单元测试验证 Windows 文件名规则、Unicode 名称、扩展名及重名冲突。Playwright 测试覆盖 F2 编辑、编辑内容选中范围、无效名称、右键重命名、Esc 取消和大型虚拟列表。

## 明确边界

- 用户点击 Enter 后 **不修改 UI 数据模型中的正式文件名**。提示是请求预览而不是承诺完成。
- 没有调用 Node、Rust 或 Tauri 修改文件的 API；既不移动、更名也不删除科研数据。
- P2 必须由 Rust 实现经过授权的真实重命名：验证容器、跨平台文件系统、冲突策略、AssetVersion/Panel 引用、事务历史与失败回滚；不能直接将前端校验视为安全许可。
- Windows WebView2 GUI 真机验收仍需执行。
