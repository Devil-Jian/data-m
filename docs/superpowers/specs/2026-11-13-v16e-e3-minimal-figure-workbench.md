# PaperArchive V1.6-E3 — 极简 Figure 卡片工作台与实验资料浏览器

> 日期：2026-11-13  
> 阶段：**E3 已开发，Windows CI 验证结果登记于主开发计划**。  
> 分支：`feat/v16-task-driven-workspace`  
> 前置：E1 稳定实验根目录/样本索引，E2 Panel ↔ Experiment 严格一对一数据库约束。  
> 本阶段不改归档 Manifest，旧 D4 加工来源预检会在 E4 统一替换。

## 1. 交互目标与入口

**用户的真实操作**：导入一个实验文件夹 → 自动分配样本及 RAW/PROCESS/SHOW/QUANT 阶段 → 在论文的 Figure 1 建立 A、B、C 子图 → 一个实验拖到一个 Panel → 点击子图直接看样本和文件名/图片 → 如有需要导入完整 Figure 图片。

新入口位于原应用顶栏左侧的 **「论文工作台」** 按钮，打开独立的 `E3WorkspaceWindow`。这是一条并行的、较短的操作路径；原 WPF 操作界面仍在，可用于历史数据和旧 D4 归档，以免重构中破坏旧工作流。**E3 没有删除旧 UI 或旧数据库结构**。

主要代码：

- `native/PaperArchive.Wpf/E3WorkspaceWindow.cs`：WPF 三栏视图及交互；
- `native/PaperArchive.Wpf/MainWindow.E3.cs`：入口和返回旧视图后的刷新；
- `native/PaperArchive.Wpf/MainWindow.xaml`：主工具栏入口；
- 直接调用 E1 的 `ListE1FolderIdentities` / `ImportResult` / `ListSampleUnits` / `ListSampleUnitAssets`；
- 直接调用 E2 的 `LinkPanelExperiment` / `GetPanelExperiment` / `UnlinkPanelExperiment` / `ListPanelExperimentFiles`；
- Figure/Panel/成图文件复用原 `CreateFigure`、`CreatePanelsFast`、`ImportFigureComposite`、`ListFigureShowData`。

## 2. 新工作台结构

```text
┌─────────────────────────────────────────────────────────────────────────────┐
│ 论文工作台                         实验文件自动索引 · 子图与实验一对一      │
├────────────────┬──────────────────────────────┬─────────────────────────────┤
│ 论文           │ Figure / 子图                │ 样本与文件                  │
│ ＋ 新建论文    │ ＋ Figure  ＋ 子图            │ A · 01_ROS检测              │
│                │ 导入完整 Figure              │ ROS01 · ROS02 · ROS03       │
│ 模拟论文       │                              │                             │
│ ROS氧化应激    │ ┌───────────┐ ┌───────────┐  │ SHOW  ROS01_show.png       │
│                │ │ A         │ │ B         │  │ RAW   ROS01_raw.tif        │
│                │ │ ROS实验   │ │ WB实验    │  │ PROCESS ROS01_crop.tif     │
│                │ └───────────┘ └───────────┘  │ QUANT ROS01_quant.png       │
│                │                              │                             │
│                │ 实验资料库                  │ [选中文件的图片预览]        │
│                │ 搜索实验                    │                             │
│                │ 01_ROS检测                  │                             │
│                │ 02_WesternBlot              │                             │
└────────────────┴──────────────────────────────┴─────────────────────────────┘
```

- **左栏**：论文名称列表和创建论文；不显示 Paper ID。
- **中栏上部**：选择 Figure、添加 Figure/自动字母子图、Panel 实验卡片；卡片显示 A/B/C、实验名与样本数，不显示 Panel ID。
- **中栏下部**：实验资料库列表和实验名搜索；双击实验关联当前选中 Panel，也可以从实验列表拖入 A/B/C；支持将 Windows 实验文件夹直接拖入资料库或点击「导入实验文件夹」选择。
- **右栏**：当前实验样本列表和文件列表，按 SHOW、RAW、PROCESS、QUANT 分类显示文件名；单击图片预览，双击用 Windows 默认程序打开原始字节的临时副本，源仓库原件不变。
- **成图**：导入 Figure1.png/TIF 等完整 Figure，所选 Figure 的当前组合成图可在右侧预览；与实验样本索引分离存储。
- **低风险操作不弹窗**；Panel 已绑定其他实验时才显示简短「替换实验？」确认；解除绑定也会确认防误操作。E2 数据库拒绝另一个 Panel 复用同一 Experiment ID。

## 3. 操作路径（用户验收）

1. 启动 PaperArchive，单击顶栏「论文工作台」。
2. 「＋ 新建论文」，输入论文名称；「＋ Figure」建 Figure 1，「＋ 子图」连续生成 A/B/C。
3. 「导入实验文件夹」选择 `01_ROS检测` 或将目录拖入「实验资料库」，系统自动识别 `ROS01_raw`、`ROS01_crop`、`ROS01_show` 和 `ROS01_quant`。
4. 将实验拖到 A 卡片；A 立即显示实验名与样本数；不要求给每张 SHOW 建关系。
5. 点击 A，在右侧选择 ROS01、ROS02，直接查看对应文件名与图片。双击文件使用系统默认应用程序打开。
6. B 再试图绑定同一实验：弹一对一冲突提示，不自动抢占 A。
7. 在 A 已有实验时拖入另一实验：简短「替换实验？」；取消不改变数据库。
8. 选择 Figure 1 后点击「导入完整 Figure」，查看当前 Figure 图片；源实验的全部文件仍可通过 A/B/C 找回。
9. 解除某子图实验关联，只删除 E2 当前工作区连接，不删除实验文件、Asset ID 或历史 Revision。

## 4. 数据保护与兼容性

- 当前工作台通过稳定 Experiment/Sample/Asset ID 查询内部关联，用户仅看名称。
- 文件预览从内容寻址仓库 `blobs/sha256` 读取，不重新保存或覆盖原始科研图片。
- 双击打开时在系统临时目录创建专用副本，原始内容仓库保持只读。
- 旧 D3 的 `panel_sample_links`、`figure_panel_assets` 仍保留；新的关联仅写入 E2 的 `e2_panel_experiment_links`，不将旧多样本关联默认为唯一实验。
- 未解析样本名称的实验级文件，在实验范围内以“其他”显示；查看单个样本时不夹带其他样本的文件。
- 历史 Revision 仍以其冻结 Manifest 为准，当前 Figure 工作台不反写归档。
- 原 WPF 程序继续可使用，不要求在 E3 期间破坏式迁移老 UI。

## 5. 当前范围与验收界限

E3 自动化验证：Windows GitHub Actions 编译新 WPF 页面、Core、旧版兼容、工作区 SmokeTests，并打包可运行的 Windows 便携版。与 E1/E2 的数据索引、一对一约束以及不改变旧历史的核心回归共同运行。具体成功 run 见主开发文档。

**仍需 Windows 本机手工验收**：高 DPI/不同窗口尺寸、真实文件夹拖入、长列表滚动、图片的系统默认打开程序和键盘访问；GitHub CI 的编译成功不等于这些桌面交互已经实际点选验证。已安装的旧 UI 是回退入口。

**不在 E3 范围**：
- E4 才重做论文归档预检并生成完整实验资产集合 Manifest v3，移除 PROCESS/RAW 关系人工核对阻断。
- E5 才进行主程序三个入口的最终导航整合、全局搜索、性能优化和旧界面隐藏。当前新工作台为独立窗口，由旧程序顶栏启动。
- 当前没有对用户本机实际实验数据进行迁移或真实图像导入；需要本机拉取分支运行。
