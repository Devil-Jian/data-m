# Data-M UI 框架对比

## 对比范围

三套演示共享完全相同的 Gemini 业务骨架、布局结构和数据集，便于比较组件本身。

| 指标 | shadcn 风格 / Radix | Mantine | Fluent UI 2 |
| --- | --- | --- | --- |
| UI 定制 | Tailwind + 本地组件源码 | 完整主题与 props | Fluent tokens 和系统规范 |
| 文件矩阵 | 共享 Workbench | 共享 Workbench | 共享 Workbench |
| 选择、下拉、按钮 | Radix Select/Checkbox + CVA Button | Mantine Select/Checkbox/Button | Fluent Dropdown/Checkbox/Button |
| 视觉取向 | 现代、克制、可深度定制 | 完整一致的数据工具 | 微软桌面/Windows 风格 |
| 封装 Windows | 可进一步接 Tauri/Electron | 可进一步接 Tauri/Electron | 可进一步接 Tauri/Electron |

## 验收场景

1. 切换 Nrf2 与肝 IRI 两个项目，确认 Theme 和 Assay 导航切换。
2. 在 ROS 模块切换四个分组；在全景矩阵中仅保留 Raw 与 Display。
3. 切换列表/表格布局，搜索 `Rep3`，筛选“仅代表图”。
4. 勾选样本，切换统计计入/代表图标记，导出演示 CSV 清单。
5. 切换论文管理，打开 FRBM 的 Fig 1a；查看展示文件、统计数据和 n=6 分组摘要。
6. 点击“溯源至实验单元”，确认进入 ROS 全阶段视图且代表样本高亮，然后点击“返回论文”。
7. 从实验页面的 Fig 1a 引用标签进入对应论文子图。
8. 新建演示项目并切换，刷新浏览器后恢复初始数据。
9. 使用键盘 Tab 操作可交互控件，检查清晰的焦点与选择反馈。
10. 在 1366×768 / 1920×1080 分辨率下验证滚动与布局稳定性。

## 开发边界

保持 Gemini 的顶栏、左侧实验导航、右侧工作区、论文 Panel 详情抽屉和双模块逻辑。这里没有重写为三栏式资源管理器，也没有引入后端。

Demo 使用的重复样本与统计结果只用于交互；统计行展示的是即时均值与样本标准差，不代表原始 Excel 的正式检验结果。

## 后续接入后端

对接 API 时，应将 `data.ts` 模拟数据替换为后端实体和查询；从全景矩阵移至服务端分页/虚拟滚动；文件导入、SHA-256、文件版本及 Panel 多对多关联交由后端保证。UI 不允许以“复制文件”代替新增独立生物学重复。
