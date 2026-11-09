# V1.5 组图工作区 Bug 修复与交互收敛开发设计

- 需求编号：V15-14
- 版本：开发设计初稿（待编码）
- 日期：2026-11-09
- 目标分支：`feat/wpf-desktop`
- 范围：论文项目 → 组图项目 → Figure → Panel
- 优先级：P0
- 本文仅定义实现方案，不代表功能已经修复。

## 1. 目标与边界

修复以下问题：

1. 组图项目右键菜单过于复杂；鼠标右键**选中项**与**空白区域**应呈现不同操作。
2. Panel 中选中已关联的 SHOW 图片后，右侧详细信息栏没有预览图。
3. 导入的完整 Figure 图片应自动纳入当前 Figure 的 `show_data` 逻辑管理范围，同时保留独立成图版本历史。

交互原则：不启动另一套完整管理界面；沿用当前资源管理器与右侧详情栏。需要确认或输入时，仅使用紧凑弹窗；UI 采用现有工作区一致的像素风格（整数尺寸、清晰边框、有限色板、无模糊阴影、状态可读），不牺牲可访问性和 DPI 缩放。

**数据不变量**：Figure/Panel 是逻辑占位；实验图片的 Asset ID、实验归属、文件内容及 Blob SHA-256 不随组图操作改变。任何删除/取消关联都不得删除原始实验 Blob。

## 2. 当前实现检查与问题定位

### 2.1 右键菜单

- 文件：`native/PaperArchive.Wpf/MainWindow.Figures.cs`
- 当前方法：`FigureFiles_ContextMenuOpening`
- 当前根据 `_currentFigure`、`_currentPanel` 以及 `FigureFiles.SelectedItem` 组合菜单，没有根据鼠标右键实际命中的 `ListViewItem` 区分空白处与项目。
- 风险：右键空白处时保留上次选中项；用户会看到重命名/删除等不属于点击目标的命令。Figure 内部菜单也长期展示导出、导入、设为当前等上下文不符的项目。

### 2.2 预览缺失

- 文件：`native/PaperArchive.Wpf/MainWindow.Figures.cs`
- 当前方法：`FigureFiles_SelectionChanged`
- `PanelAssetLink` 行包含 `AssetId`、`Sha256`、`FileName` 等信息；当前 `PreviewImage.Source` 只在对象为 `FigureComposite` 时设值，其他类型均为 `null`。
- 对照：`native/PaperArchive.Wpf/MainWindow.xaml.cs` 中 `AssetRow.Thumbnail` 通过 `_workspace.GetBlobPath(Asset)` 解码素材。Panel 详情应共享同一 Blob 解析及图像解码策略。
- 注意：WPF 对部分 TIFF 压缩/多帧格式的支持取决于解码器，必须有错误与不支持格式的明确占位状态，不能把“没有缩略图”当成“图片不存在”。

### 2.3 完整成图导入

- 文件：`native/PaperArchive.Core/ExperimentWorkspace.Figures.cs`，方法：`ImportFigureComposite`。
- 当前复制图片到暂存区、计算 SHA-256、存储内容寻址 Blob，随后写入 `figure_composites` 版本表。
- 当前 `native/PaperArchive.Wpf/MainWindow.Figures.cs` 的 `RefreshFigureView` 将完整成图作为 Figure 列表中的一类行展示，尚无独立的 `show_data` 分类视图。
- **不应**直接将完整成图插入某个 `experiment_assets` 记录，因为该表有实验项目/结果/组的归属语义；强行插入会污染实验数据结构。

## 3. 交互定义：右键命中与命令矩阵

### 3.1 必须使用实际命中的目标

右键事件处理流程：

1. 从 `ContextMenuOpeningEventArgs.OriginalSource` 沿可视树查找所在 `ListViewItem`（可复用 `ItemsControl.ContainerFromElement`）；若无容器则是空白处。
2. 命中行时：若行未被选中，先把该行设为当前选择；若已选中且属于多选集合，保留多选，按多选规则构建菜单。
3. 命中空白时：清除当前列表行选择，只显示**当前位置**的创建类操作。
4. 缓存本次右键目标到独立上下文对象，由菜单命令读取；避免打开菜单后 `SelectedItem` 被异步刷新修改而对错误对象操作。
5. 执行前重新校验目标 ID 与所属 Figure/Panel，失败时显示无副作用错误信息。

### 3.2 单选右键菜单

| 位置/行类型 | 主菜单 | 次级菜单/行为 |
| --- | --- | --- |
| 组图项目空白 | 新建 Figure、批量新建 Figure… | 无删除、重命名 |
| Figure 行 | 打开、重命名 F2、删除 Delete | 新建 Panel、批量新建 Panel… |
| Figure 内空白 | 新建 Panel、批量新建 Panel…、导入完整 Figure… | 资源导出在顶部工具栏 |
| Panel 行 | 打开、重命名 F2、删除 Delete | 关联 SHOW 素材… |
| Panel 内空白 | 关联 SHOW 素材… | 无取消关联 |
| Panel 的关联图片行 | 预览、详细信息、取消关联 | 不允许删除实验原始文件 |
| 完整成图行 | 预览、导出、设为当前版本 | 版本管理入口可见；历史版仍可预览 |
| 完整成图列表空白 | 导入完整 Figure… | 无对象级操作 |

**禁止**在 Panel 关联图片上提供含糊的“删除文件”；仅提供“取消关联”，并在确认文案中说明不删除实验原始图片。完整成图的删除版本能力不在本轮新增，避免与版本安全策略冲突。

多选：仅展示对同质选择可执行的操作。Panel 多张关联素材可“批量取消关联”；混合行类型时不提供危险命令，保持“打开/详细信息”等安全动作。

### 3.3 左键、快捷键与操作反馈

- 单击：选中并刷新右侧详情；双击或 Enter：打开 Figure / Panel 或预览图片。
- F2：仅对 Figure/Panel 占位有效；Delete：按目标类型进行删除/取消关联，显示影响范围。
- `Ctrl+Shift+F`、`Ctrl+Shift+P` 保持快速创建；`Alt+Shift+P` 继续显示/隐藏详情栏。
- 创建后留在当前层级、列表刷新、选中最后创建项、项目树同步更新；正常成功不弹出成功窗口。
- 危险操作有明确确认及影响数目；失败不移除 UI 行。

### 3.4 像素风格（视觉约束）

建议将右键菜单统一为紧凑矩形样式：1px 实线边框、统一 4px/8px 网格间距、无投影或仅硬边阴影、像素感图标。选中与禁用状态必须通过高对比颜色和文字同时表达；适配 100%/125%/150% DPI，并保留键盘操作与屏幕阅读器名称。

## 4. Panel 详情图片预览修复

### 4.1 数据解析

选中 `PanelAssetLink` 时：

1. 使用其 `AssetId` 解析源 `ExperimentAsset`（复用现有工作区读取逻辑，必要时增加只读 `GetAssetById` API）。
2. 验证来源为当前论文可访问的 SHOW 素材，且记录未被删除；关联已失效时显示“来源素材不可用”。
3. 通过 `_workspace.GetBlobPath(asset)` 获取内容寻址 Blob 的真实路径，禁止将外部路径拼接为 Blob 路径。
4. 使用与实验资源预览一致的解码函数设置 `PreviewImage.Source`；`BitmapImage` 必须使用 `CacheOption.OnLoad`，读取流完成后关闭、冻结以便跨线程安全使用。
5. 详情同时显示：文件名、分辨率（可读取时）、文件大小、SHA-256、来源实验结果/组、关联 Panel、素材 ID。

### 4.2 UI 状态

- 图片可读：显示等比缩放预览，不能拉伸变形。
- 格式不支持：显示“此格式暂不支持预览，可打开原始文件”；不得显示空白无解释。
- Blob 缺失或校验失败：显示损坏/缺失状态，禁止悄然展示其他同名图片。
- 快速切换列表：为预览加载绑定选择版本/取消令牌，防止上一条的异步结果覆盖新选择。
- 取消选中或离开 Panel：清理旧预览、旧元数据及旧关联列表。
- TIFF 多页：本轮默认仅预览首帧并标注“多页 TIFF”；保留原始字节，未来可扩展分页。

### 4.3 避免重复实现

优先抽取共享的只读预览服务（例如 `ImagePreviewService`），由实验项目 `AssetRow`、Panel 关联图片和完整 Figure 共同复用。提供加载失败的用户可见状态，不再以空 `catch` 静默失败。

## 5. 完整 Figure 自动管理到 show_data

### 5.1 语义

**本设计将“导入完整图自动管理到 showdata”定义为：完整 Figure 归属当前 Figure 的 `show_data` 逻辑素材集合，而不是强制移动到某个实验项目的物理 `show_data` 文件夹。**

建议资源结构：

```text
论文项目
└── 组图项目
    └── Figure 1
        ├── Panel A
        ├── Panel B
        ├── Panel C
        ├── show_data
        │   ├── Figure1_final.tif [v1, 当前]
        │   └── Figure1_revised.tif [v2, 历史/候选]
        └── 完整组图（版本视图，可复用同一记录）
```

- `show_data` 是**逻辑分类**；若已有“完整组图”节点，可将其调整为“show_data · 完整组图”，避免同一图片在两个列表中被误认为两份文件。
- 一次导入只生成一份物理 Blob（按 SHA-256 去重），并在 `figure_composites` 中建立一个版本记录。
- 相同内容重复导入时的版本策略：默认允许新增版本但共享 Blob，UI 标识“与现有版本内容一致”，不静默覆盖旧版。
- 不自动写入 `figure_panel_assets`，完整成图属于 Figure 级成果，不应冒充 Panel 来源素材。
- 不向实验项目 `experiment_assets` 自动增加新行；来源于实验数据的原始 SHOW 图片仍保持原实验归属。

### 5.2 数据层方案

优先**复用现有 `figure_composites`**；通过 Figure ID 查询即可生成 `show_data` 视图。若未来存在其他 Figure 级 SHOW 文件，再增加带 `source_kind` 的独立索引，不在本次创建多余文件副本。

导入时：

1. 验证扩展名、文件可读性、Figure ID。
2. 将输入复制到 staging，计算 SHA-256 并确保 Blob 校验通过。
3. 在数据库事务中新增版本记录并更新 `show_data` 逻辑展示所需的元信息；文件写入和数据库事务不可原子提交时，采用可恢复的暂存/清理策略，不留下指向不存在 Blob 的版本记录。
4. 保留源文件不动；禁止覆盖已有文件。
5. 事务成功后刷新 Figure 树、`show_data` 列表、版本数与详情区，并选中新导入版本；不再强制弹“导入成功”提示。
6. 清晰区分“当前正式版本”“候选/历史版本”。

### 5.3 删除与恢复

- “取消 Panel 关联”仅删除 `figure_panel_assets` 关联行，绝不删除 `experiment_assets` 或 Blob。
- 删除 Figure 前列出 Panel 数、SHOW 引用数、完整图版本数；保留跨项目引用保护检查。
- 删除版本或 Figure 后，物理 Blob 不直接清除；如需 Blob 回收，必须由独立的引用计数/可达性扫描与回收流程执行。
- UI 中所有记录与物理 Blob 之间保持可追溯的 SHA-256。

## 6. 建议代码改造

| 文件 | 改造 |
| --- | --- |
| `native/PaperArchive.Wpf/MainWindow.Figures.cs` | 拆分右键命中识别、菜单工厂、操作命令；Panel 图片选择时加载预览；导入完成后刷新 show_data |
| `native/PaperArchive.Wpf/MainWindow.xaml` | 紧凑像素风格 ContextMenu/详情占位样式；必要时补充 show_data 视图入口 |
| `native/PaperArchive.Wpf/MainWindow.xaml.cs` | 共享预览初始化及详情状态，避免实验列表和 Panel 列表双份逻辑 |
| `native/PaperArchive.Core/ExperimentWorkspace.Figures.cs` | 新增安全的只读素材解析/逻辑 show_data 查询（优先复用现有记录），处理版本归属和导入异常 |
| `native/PaperArchive.Core/ExperimentWorkspace.V13.cs` | 若需要，在现有 Figure/Panel 树模型中增加 show_data 节点，不创建虚假的实验结果 |
| `native/PaperArchive.Workspace.SmokeTests/Program.cs` | 增加导入、关联、删除、版本和数据保护回归测试 |

建议分层：`FigureContextTarget`（命中目标），`FigureContextMenuBuilder`（命令集合），`ImagePreviewService`（解码与错误状态），`FigureShowDataQuery`（当前 Figure 成图集合）。命令处理应基于对象 ID，而非展示名称。

## 7. 验收标准

### A. 菜单上下文

- A01：右键组图空白处，仅出现 Figure 创建相关命令；即使此前选中 Figure 行也不出现“删除”。
- A02：右键 Figure 行，出现打开/重命名/删除和 Panel 创建；右键另外一个 Figure 不会误删前一个。
- A03：Figure 内右键空白处，仅出现 Panel 创建与 Figure 级导入等命令。
- A04：右键 Panel 行有重命名/删除；右键 Panel 内图片只有预览/详细信息/取消关联等对象级操作。
- A05：键盘 F2、Delete、Enter 与鼠标菜单使用同一个目标检查及命令路径。
- A06：多选、搜索过滤、刷新后右键、不同 DPI 下均无旧选择穿透。

### B. 图片预览

- B01：Panel A 关联一张 PNG SHOW 图片，选中后右侧出现正确预览与来源信息。
- B02：JPG/TIFF/多页 TIFF 可解码时展示预览或明确的首帧；无法解码时显示明确提示。
- B03：快速选中图片 A → B，不会在 B 的详情中显示 A 的预览。
- B04：缺失或损坏 Blob 不崩溃、不误用同名图片；原始数据不会因预览被修改。
- B05：取消关联后仅该 Panel 的链接消失，实验项目内 SHOW 图片及 SHA-256 保持不变。

### C. 完整成图 show_data

- C01：向 Figure 1 导入 `Figure1_final.tif` 后，Figure 1 的 `show_data` 视图立即出现此图，标记 v1/当前版本。
- C02：导入第二版显示 v2；切换当前版本后列表、详情及导出目标一致。
- C03：Blob 数量不因同内容重复导入而增加；版本记录仍可分别追踪。
- C04：导入不创建实验结果，不移动实验原始图片，不制造 Panel 素材链接。
- C05：导入失败不留下假条目；源文件完整。
- C06：关闭重开工作区，show_data 和版本关系仍存在。

### D. 整体

- D01：不打开完整管理窗口，仅通过当前资源管理器、右侧详情栏与必要的小弹窗交互。
- D02：上下文菜单风格、图标与焦点状态遵守统一像素风格规范。
- D03：Windows WPF 构建、Workspace SmokeTests 与人工验收全部通过后，才可标记“已修复”。

## 8. 实施顺序

1. 先修右键命中与菜单；统一操作对象识别及危险命令确认。
2. 修 Panel 图片预览，抽取可共享的解码方法及错误状态。
3. 将 `figure_composites` 映射为 Figure 级 `show_data` 逻辑分类，打通导入后刷新及版本状态。
4. 增加回归测试，核对数据归属、SHA-256、删除安全及 UI 无误触。
5. 在 Windows 环境进行 WPF 构建、测试及实际像素风格检查。

## 9. 待确认的产品语义（不阻碍 1、2 项修复）

用户提到的“showdata”按 **Figure 范围的 show_data 逻辑管理**实现；若未来要求实际复制到实验项目的 `show_data` 目录，需另起迁移需求，并明确目标实验结果归属、重名冲突和数据溯源规则。默认不擅自改变实验项目所属关系。
