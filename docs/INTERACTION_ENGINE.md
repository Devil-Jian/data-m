# 文件交互引擎 — 设计与验收规范（第一阶段）

## 目的

在保留 Gemini 原稿顶栏/左侧导航/实验全景矩阵与论文 Panel 布局的情况下，引入独立、可测试的 Windows Explorer 风格交互层。当前是 React 浏览器演示中的鼠标交互，不包含 Tauri 原生外部文件拖放。

## 模块边界

- `packages/interaction/src/model.ts`：选择状态、Ctrl/Shift 语义、矩形相交检测、拖拽意图和无效目标检查。纯函数方便单元测试。
- `packages/interaction/src/useExplorerInteraction.ts`：Pointer Events 和键盘事件绑定；聚合 `onDrop` 回调，将意图发送给业务层。没有文件系统写入。
- `packages/core/src/workbench.tsx`：Gemini UI 集成，仅负责用演示数据渲染 item，显示选中、虚线目标和拖动提示；未处理真实存储。

## 标准交互规则

| 场景 | 预期行为 |
| --- | --- |
| 左键点击文件行/卡片 | 单选 |
| Ctrl+单击 | 追加或取消单个选择 |
| Shift+单击 | 从锚点到目标的连续选择 |
| Ctrl+Shift+单击 | 原选择上追加连续范围 |
| Ctrl+A | 选中当前分组/过滤结果全部可见项 |
| 空白区域按住左键移动 | 半透明框选，允许任意方向 |
| Ctrl + 框选 | 基于拖动开始时的选择追加命中项 |
| 选中文件组拖向顶部其他分组 | 保持选中集合并显示目标反馈 |
| Ctrl + 拖拽 | 复制意图；Shift 强制移动 |
| 拖回原组或拖至非目标 | 不执行操作 |
| Esc | 取消拖动；非拖动时清除选择 |
| Ctrl+C/X/V、Delete、F2 | 提交操作请求，不修改真实文件 |

## 严格安全限制

1. 演示用 `Replicate` 不能直接作为真实 `DataAsset`。复制或移动文件不应改变统计 n。
2. `onDrop` 只输出 `itemIds/sourceId/targetId/operation` 的意图；未来后端验证用户权限、原始文件不可变性、论文引用、样本归属及事务后才能提交。
3. 原始科研文件禁止被浏览器脚本直接删除；外部 Windows 拖入需 Tauri 2 原生路径事件并执行安全复制。
4. 跨应用拖出到 Windows Explorer 不属于此阶段；将来需要原生 OLE/COM 能力。
5. 当前示例没有虚拟列表；海量文件与自动滚动框选将在独立性能任务中实现。

## 单元测试

运行：`npm run test:interaction`

覆盖：单选、Ctrl 切换、Shift 正/反向区间、Ctrl+Shift 追加、当前可见项全选、矩形框选追加/替换、反向选区几何、拖拽修饰键、同容器与空目标拒绝。

## 下一阶段工程任务

- Tauri 2 桌面壳与 Windows WebView2 拖入验证。
- 真实 Asset ID 与 Replicate ID 分离；SQLite + CAS 文件资产 API。
- 虚拟列表中的跨屏选择与矩形框选自动滚动。
- 右键菜单、剪贴板目标解析、F2 编辑器、撤销/重做和操作队列。
- Playwright/WebView2 端到端鼠标键盘交互测试。
