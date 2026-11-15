# P1 文件交互可靠性修复（Issue #13）

## 本次修改

1. 将矩形框选的 DOM 查询限制到当前 Explorer 画布，不再全局查询整个 document。
2. 使用当前画布的可视矩形裁剪框选命中；画布外其他资产不会被误选。
3. 在 pointerdown 记录被拖拽的 ID 快照。拖动已有选中项时保持整体多选；拖动未选中项时只拖该项。
4. 检查 pointerId，只响应起始指针的移动/抬起，避免触控板/多指干扰。
5. Esc 取消后阻断后续 pointerup 提交；切到论文模块或对话框时重置手势。
6. 输入框/可编辑组件保留原生 Ctrl+A/C/V 行为，快捷键不触发文件级操作。
7. 补充纯规则单元测试，覆盖多选、反向框选、画布裁剪和拖拽集合快照。

## 实现边界

- 仍为演示 Replicate 选择，真实 DataAsset 与 Replicate 尚需拆开（Issue #13 后续任务）。
- 鼠标滚动边缘自动框选、虚拟列表映射、多窗口和 Windows GUI E2E 均未实现。
- 拖拽仅产生意图，不写文件、数据库或论文引用。

## 测试

`npm run test:interaction`；`npm run build:all`；`npm run build:desktop`。

必须在 Windows WebView2 真机验收之后，才能将本任务标为完整完成。
