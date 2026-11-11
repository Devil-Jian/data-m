# PaperArchive V1.6 开发进度与跨对话交接文档

> 交接日期：2026-11-11  
> 文档用途：在新的 ChatGPT 对话中快速恢复完整项目背景、目前代码状态、设计约束和下一步任务。  
> 交接对象：下一轮开发助手 / 项目维护者。  
> 状态：**V1.6-A 第一阶段已实施；V1.6-B 部分实施；V1.6-C 尚未实施。**

## 0. 先读这里：新对话最重要的信息

- 仓库：**[Devil-Jian/data-m](https://github.com/Devil-Jian/data-m)**。
- **当前唯一继续开发分支：[`feat/v16-task-driven-workspace`](https://github.com/Devil-Jian/data-m/tree/feat/v16-task-driven-workspace)**；从 `feat/wpf-desktop` 分出，尚未合并。不要误在 `main` 或旧分支上修改。
- 本交接文档写入之前，代码 HEAD 为 **`da8593621fd1dc70cc1f828bdd6876d41469a641`**。实际开始下一轮开发前需重新检查远端 HEAD，不要把此 SHA 当成永不变化的最新值。
- 技术栈：**C# / .NET 10 / Windows WPF / SQLite**。工作台使用 `workspace.db`，资源库使用 `archive.db`；图片、论文全文等原件使用 SHA-256 内容寻址 Blob。
- 最新代码验证：Windows **WPF Desktop (.NET 10)** 工作流 [42985972382](https://github.com/Devil-Jian/data-m/actions/runs/42985972382) 为 `success`；通用 CI [42985972436](https://github.com/Devil-Jian/data-m/actions/runs/42985972436) 为 `success`。包含 Workspace SmokeTests、WPF 编译、便携版发布与打包。**尚无 Windows GUI 人工交互验收记录**。
- V1.6 核心原则：**论文任务驱动、导入后自动整理、通过操作记录关联、只处理科研事实上的例外、一次确认归档**。不能将“文件名相似/同组”误认为“已证实的加工流程”。
- 用户明确不采用像素风格。继续沿用现有简洁原生 Windows/WPF 三栏交互，不引入 pixel-art UI。
- 下一步重点：**V1.6-B 的统一样本索引、旧数据安全重建索引、直接拖拽关联 SHOW→Panel、减少 Figure 来源核对步骤**。其后才是 V1.6-C 可视化 Figure 工作区和资源库内联溯源。

## 1. 产品目标与业务语义

### 1.1 整体流程

归档前工作台承担：

1. 导入实验数据（RAW、PROCESS、SHOW、QUANT、统计表）。
2. 自动分类、识别实验样本，记录来源候选。
3. 建立 Figure / Panel，Panel 使用实验项目中的 **SHOW** 图片的逻辑引用。
4. 导入完整 Figure 成图，自动生成不可变版本与来源候选快照。
5. 导入论文全文（DOCX/PDF/DOC），为论文绑定**实际采用的 Figure 成图版本**。
6. 在论文任务首页只显示尚需处理的问题。
7. 已就绪后归档到论文资源库；归档后可继续编辑工作台并发布下一 Revision。

归档后资源库承担：只读历史版本、论文/成图/素材关系溯源、检索及导出。**归档不是移动/删除工作台项目，而是创建经过校验的不可变关系快照**。

### 1.2 主要数据链

```text
论文全文（具体文档 ID）
  → 论文确认采用的 Figure 完整成图版本 ID
  → 该成图版本的 Panel/SHOW 来源快照（历史固定）
  → SHOW 图片 Asset ID
  → PROCESS 图片 Asset ID（明确关联或同组候选）
  → RAW 图片 Asset ID（明确关联或同组候选）
```

*正向*从论文追溯实验原件，*反向*从实验图片查询被哪些 Panel、Figure、论文及归档 Revision 使用。正式加工关系需要真实证据或人工确认；同组索引默认只能是候选。

### 1.3 图像阶段与最新命名约定

| 阶段 | 用途 | 目录 | 命名约定 |
|---|---|---|---|
| RAW / `raw` | 原始实验图片 | `raw_data/` | `CV01_raw.tif` |
| PROCESS / `process` | 图像处理的中间阶段 | `process_data/` | `CV01_crop.tif`、`CV01_adjusted.tif`、`CV01_任意处理名.tif` |
| SHOW / `show` | 用于 Figure/Panel 正式展示 | `show_data/` | `CV01_show.png` |
| **QUANT / `quant`** | **已处理、仅用于定量分析和统计取数的图片，不用于 Figure 展示** | **`quant_data/`** | **`CV01_quant.tif`、`CV01_quant_v2.tif`** |
| statistics / `statistics` | 统计结果表格 | 可在任意目录 | `*.csv`、`*.xlsx`、`*.xls` |

- `_raw`、`_show`、`_quant`（可带 `_vN`）为有明确语义的后缀。PROCESS **不要求固定后缀**。
- 同次导入时，系统使用 RAW/SHOW/QUANT 识别的样本名锚点，将 `CV01_unknownProcessing.tif` 等归入 CV01，支持 `_` 或 `-` 分隔；不能把 CV010 误归入 CV01。
- 此自动分组目前主要适用于**同一次导入**；**旧数据的安全重建索引、跨次增量重匹配尚未完成**。
- `quant_data` 目录和 `_quant` 专用后缀均能识别 QUANT 图片；CSV/XLSX 仍为 statistics，不会被当成 QUANT。
- QUANT 可以记录从 RAW 或 PROCESS 产生的来源关系；**QUANT 不等于 SHOW，不允许作为正式 Panel 展示素材**。
- **同组归属可以自动确定，加工顺序如 RAW→crop→adjusted→SHOW 不能仅凭文件名自动确认**。

## 2. 已完成开发范围

### 2.1 原有 WPF 工作台（V1.3—V1.5，继承自旧分支）

- 论文项目 → 实验项目/实验结果/样本组 → 文件管理。
- 文件筛选：全部文件、全部图片、`raw_data`、`process_data`、`show_data`、统计表；V1.6 已新增 `quant_data`。
- Figure/Panel 逻辑结构：Figure 为工作容器；Panel 为 SHOW 逻辑链接；`Figure1` 图片节点为完整成图版本占位，创建 Figure 时自动出现但**不创建空图片字节**。
- Figure 版本、Panel 素材引用、成图来源快照、历史版本保护。
- 论文全文可关联特定 Figure 成图版本；DOCX 的 Fig/Figure 引用可作为**待核对候选**自动提取；PDF/DOC 尚未实现相同程度的可靠自动识别。
- SHOW→PROCESS→RAW 已确认关系与同组候选关系分开保存；旧成图支持显式补录“待核对来源”。
- 归档服务 V15-16：归档预检、原件 SHA-256 校验、不可变 Manifest、Revision、重复内容幂等、历史版本浏览；旧论文资源库兼容。

### 2.2 V1.6-A 已落地：任务驱动工作台第一阶段

关键代码：

- `native/PaperArchive.Core/ExperimentWorkspace.Tasks.cs`
  - `PaperWorkTask` / `PaperWorkSummary`
  - `GetPaperWorkSummary(paperId)`：根据真实数据关系**推导**任务状态，没有额外的“手动完成”标志。
  - `GetPrimaryPaperDocument` / `SetPrimaryPaperDocument`：记住主文稿 ID。只有一份全文时不必人工选择；多份时仅选择一次。
  - 按阻断问题与待核对问题区分优先级。
- `native/PaperArchive.Wpf/MainWindow.Tasks.cs`
  - 选择论文项目根节点显示任务首页与项目进度；任务可点击定位。
  - 从首页进入全文、组图、素材导入；不用先执行复杂的创建流程。
  - 顶部根据项目首页上下文隐藏不相关的文件操作。
- `native/PaperArchive.Wpf/MainWindow.xaml` 与相关 `MainWindow.*.cs`
  - 任务概览嵌入三栏布局；保留已有树、文件列表、右侧详情。
- `native/PaperArchive.Wpf/MainWindow.Archive.cs`
  - 减少重复选择主文稿和归档检查弹窗；正常数据仅一次最终确认。
  - 存在来源待核对项时必须明确同意其以未确认状态进入快照。
  - 相同内容不会创建新的 Revision，归档后自动转资源库。
- `native/PaperArchive.Core/ArchiveRepository.Workspace.cs`
  - `GetLatestWorkspaceArchive` 等查询支持工作台显示既有归档状态。

**注意**：“任务首页”第一阶段已实现，但仍需 Windows 真机进行布局、导航、长文本与操作效率验收，不应宣称 V1.6-A 的所有 UX 目标完全验收。

### 2.3 V1.6-B 已部分落地：QUANT 与同次导入分组

关键代码：

- `native/PaperArchive.Core/ExperimentWorkspace.Quant.cs`
  - 安全迁移 SQLite `experiment_assets.stage` CHECK 约束，新增 `quant`；
  - 保留已有 Asset ID、SHA-256、数据组以及旧引用关系；兼容老数据库（已自动测试）。
- `native/PaperArchive.Core/ExperimentWorkspace.cs`
  - `DetectStage` 可按 `quant_data` 目录、`_quant` 后缀及图像扩展名识别 QUANT；
  - `DetectPrefix` 识别 `_quant` / `_quant_v2`；
  - `ImportResult` 同次导入时借助 RAW/SHOW/QUANT 样本锚点识别任意 PROCESS 后缀。
- `native/PaperArchive.Core/ExperimentWorkspace.Provenance.cs`
  - QUANT 支持 RAW/PROCESS 上游候选与显式已确认来源记录；
  - SHOW 的正式展示关系规则不变。
- `native/PaperArchive.Wpf/MainWindow.xaml.cs`
  - 左下“文件筛选”增加 `quant_data`；
  - QUANT 标签与 SHOW、统计表分开显示。

### 2.4 已验证内容

交接前代码 HEAD：`da8593621fd1dc70cc1f828bdd6876d41469a641`。

- WPF Desktop (.NET 10)：[GitHub Actions #42985972382](https://github.com/Devil-Jian/data-m/actions/runs/42985972382) — **success**。
- 通用 CI：[GitHub Actions #42985972436](https://github.com/Devil-Jian/data-m/actions/runs/42985972436) — **success**。
- 覆盖旧实验记录升级、QUANT/SHOW 分类区分、CSV 仍为统计表、任意 PROCESS 后缀自动归入同次导入的样本组、PROCESS→QUANT 关系、QUANT 不能正式用于 SHOW/Panel、原有全文/Figure/归档数据链等自动化回归。
- **尚未进行实际 Windows 桌面 UI 的人工测试**。自动通过不意味着拖拽体验与视觉已经验收。

## 3. 未完成任务与优先顺序

### P0：V1.6-B 下一步

1. **统一样本索引：增量匹配与安全重建**
   - 支持旧导入数据的样本名识别与分组候选；跨次导入自动识别同实验结果下同一样本。
   - 不能擅自改变已确认关系；提供预览、歧义提示、取消/撤销与回滚方案。
   - 自动区分 RAW、PROCESS、SHOW、QUANT、statistics；定量图不进入展示素材候选列表。
2. **把样本组索引与实际加工关系分开呈现**
   - 文件名同组自动索引，无需重复核对；PROCESS 处理顺序仍是可选的已确认来源记录。
   - 图像右侧显示“同一样本”“来源（已确认/候选）”“使用位置”。
3. **Figure 来源核对界面简化**
   - 用户已经反馈“Figure 1 · 核对数据来源 / 成图版本没有记录 Panel/SHOW 来源”提示过于繁琐。
   - 点击“查看来源”后应就地展示 Panel/SHOW 缩略图、对应历史成图及状态。
   - 如历史来源缺失，提供“以当前 Panel 为候选并核对”的**一个连贯流程**，不再逼用户先右键补录再右键确认。
4. **SHOW → Panel 直接拖拽**
   - 拖至 Panel 建立逻辑引用；拖至 Figure 空白处自动创建下一个 Panel。
   - 支持批量 SHOW、撤销、明显的引用反馈；不移动原始字节。
5. **减少任务噪声**
   - 自动同样本匹配不应产生每张图的“必须人工核对”任务。
   - 只有真正含糊、无法确定或涉及论文最终采用的科研事实，才需要用户判断。

### P1：V1.6-C

- Figure 主页面：最终成图大图预览、Panel 缩略图、版本历史与就地操作。
- 论文集中确认引用的 Figure/成图版本；DOCX 候选与后续可提取文本 PDF 支持。
- 归档资源库**内联**展示完整溯源和 Revision，不再要求多级独立弹窗。
- 从任一素材反向查询论文引用与归档 Revision；全局检索论文、Figure、数据组、文件名与 SHA-256。
- 完成 UI 实机验收、性能、空状态、错误恢复、导航状态保持。

### 未开发但曾讨论的扩展

- 扫描版 PDF 的文字识别、旧版 DOC 自动 Figure 引用识别。
- 完整溯源 CSV/JSON 导出。
- 可选的整个项目归档（当前归档的范围主要是**指定主文稿正式引用的 Figure 及相关来源**，不包含全部闲置草稿）。
- 真正通用的统计数据模型及“定量图片 → 原始测量结果表/统计图”细粒度关系。

## 4. 已知风险与不能越过的边界

1. **不虚构科研加工关系**：同组名不等于已知 RAW→PROCESS→SHOW 顺序；系统候选 ≠ 用户确认的事实。
2. **旧数据兼容**：旧版 `experiment_assets` 表需升级 CHECK 约束；迁移已加事务和外键完整性测试，但请在真实数据副本上进一步验证。
3. **旧样本组不会自动修正**：新增任意 PROCESS 后缀归组的规则，目前对导入批次有效；历史错误分组需要安全重建工具。
4. **不可变历史**：已归档 Manifest、被论文采用的成图版本及来源快照不可被当前工作区的编辑操作覆盖。
5. **零字节“成图占位”禁止**：`Figure1` 只是逻辑图片槽位，实际导入才产生内容寻址 Blob。
6. **QUANT 与 SHOW 不混合**：`_quant` 仅作为统计取数图片，禁止当成正式展示素材自动绑定 Panel。
7. **必须保留原始实验图片**：逻辑移动/重命名/引用不应修改仓库里的原件字节；任何删除必须明确作用范围。
8. **GUI 尚未验收**：分支虽然自动化构建成功，但仍需在实际 Windows WPF 交互中检查按钮、Splitter、树导航、任务入口、布局与可用性。
9. **UI 样式约束**：不使用像素风格；沿用原生现代、简洁、低装饰的 Windows 界面。

## 5. 核心代码入口速查

| 功能 | 主要文件 |
|---|---|
| SQLite 工作台初始化、导入、分类、数据组 | `native/PaperArchive.Core/ExperimentWorkspace.cs` |
| 论文项目与实验结果层级 | `native/PaperArchive.Core/ExperimentWorkspace.V13.cs`、`ExperimentWorkspace.V13.Tree.cs` |
| QUANT 数据阶段迁移 | `native/PaperArchive.Core/ExperimentWorkspace.Quant.cs` |
| 论文主文稿与任务投影 | `native/PaperArchive.Core/ExperimentWorkspace.Tasks.cs` |
| Figure/Panel/完成成图版本 | `native/PaperArchive.Core/ExperimentWorkspace.Figures.cs` |
| 论文与成图关系、历史快照、实验来源 | `native/PaperArchive.Core/ExperimentWorkspace.Provenance.cs` |
| 论文全文导入 | `native/PaperArchive.Core/ExperimentWorkspace.Documents.cs` |
| 归档版本快照与 SHA 校验 | `native/PaperArchive.Core/ArchiveRepository.Workspace.cs` |
| 主 WPF 布局与文件筛选 | `native/PaperArchive.Wpf/MainWindow.xaml`、`MainWindow.xaml.cs` |
| 任务首页和任务跳转 | `native/PaperArchive.Wpf/MainWindow.Tasks.cs` |
| 归档 UI | `native/PaperArchive.Wpf/MainWindow.Archive.cs` |
| Figure/Panel UI | `native/PaperArchive.Wpf/MainWindow.Figures.cs` |
| 论文全文 UI | `native/PaperArchive.Wpf/MainWindow.Manuscripts.cs` |
| 完整工作区回归 | `native/PaperArchive.Workspace.SmokeTests/Program.cs` |
| Windows 构建 | `.github/workflows/wpf-desktop.yml` |

详细交互规范：[`docs/superpowers/specs/2026-11-11-v16-task-driven-workspace-ux.md`](../superpowers/specs/2026-11-11-v16-task-driven-workspace-ux.md)。

## 6. 本地拉取、运行与验证

在 Windows PowerShell / Git Bash：

```bash
git clone https://github.com/Devil-Jian/data-m.git
cd data-m
git fetch origin
git switch --track origin/feat/v16-task-driven-workspace
```

如果已有本地仓库：

```bash
git fetch origin
git switch feat/v16-task-driven-workspace
git pull --ff-only origin feat/v16-task-driven-workspace
git status
```

.NET 10 SDK / Windows：

```powershell
dotnet run --project native/PaperArchive.Workspace.SmokeTests/PaperArchive.Workspace.SmokeTests.csproj -c Release
dotnet build native/PaperArchive.Wpf/PaperArchive.Wpf.csproj -c Release
dotnet run --project native/PaperArchive.Wpf/PaperArchive.Wpf.csproj
```

GitHub Actions：[仓库 Actions](https://github.com/Devil-Jian/data-m/actions)。最新代码是否通过请按当前 **HEAD** 的工作流重新确认；不要只根据历史绿灯推断新提交通过。

## 7. 新对话推荐开场提示词（可直接复制）

> 我正在开发 GitHub 项目 Devil-Jian/data-m。请先阅读分支 **feat/v16-task-driven-workspace** 的交接文档：`docs/handoffs/2026-11-11-v16-development-handoff.md` 和设计文档 `docs/superpowers/specs/2026-11-11-v16-task-driven-workspace-ux.md`。  
> 技术栈为 C#/.NET 10 WPF/SQLite，当前 V1.6-A 任务驱动首页已完成第一阶段，V1.6-B 已加入 QUANT 定量图片阶段（`quant_data`、`_quant`）、同次导入按 RAW/SHOW/QUANT 锚点识别任意 PROCESS 后缀。  
> 请在 **feat/v16-task-driven-workspace** 上继续开发，优先推进：样本索引的旧数据安全重建和跨批次匹配、简化 Figure 历史来源核对、SHOW 拖拽关联 Panel、统一正反向数据索引。  
> 严格保留旧数据 ID、原始 Blob、归档历史快照，不把同组候选当成真实加工关系，QUANT 不允许用于正式 Figure Panel。UI 不采用像素风格。修改后提交 GitHub 并验证 Windows WPF 构建与测试。  
> 请先核查远端最新 HEAD、相关代码和 CI，再给出分阶段实施计划，随后按我提出的具体新需求实施。

## 8. 交接结论

**当前系统具备可运行的 WPF 工作台、任务首页、论文–Figure 版本绑定、实验来源候选/确认、版本化归档以及 QUANT 定量图片分类。** 最新代码通过自动化 CI/WPF 构建，但 UI 尚未进行人工验收。

**最应继续解决的是维护成本**：将用户通过文件命名已经表达的“同一样本”信息转为稳定索引，支持旧文件的安全重建，让 Figure 来源核对变成一处完成的界面流程，同时通过拖拽形成 Panel 引用。随后再优化归档资源库的直观浏览与全局检索。
