# PaperArchive V1.6-E6 — 研究项目 / 实验 / 样本 / 文件四级索引

> 日期：2026-11-13
>
> 状态：**E6 功能代码已开发；Windows CI 最终结果在总开发计划内登记；真实本机数据与图形操作待本地验收**。
>
> 分支：`feat/v16-task-driven-workspace`
>
> 依赖：E1 物理实验根目录身份与增量 Asset 索引、E2 Panel↔Experiment 严格一对一、E3–E5 统一界面/归档。

## 1. 已确认的新业务结构

用户实际数据示例：

```text
氧化应激研究/                    ← ResearchProject ID；仅科研组织分组
  实验方案.docx                 ← 研究项目级附件，不属于某个实验样本
  ROS/                         ← Experiment ID，直接供 Panel 关联
    ROS01_raw.tif
    ROS01_crop.tif
    ROS01_show.png
    ROS01_quant.xlsx
    ROS02_raw.tif
    ROS02_show.png
  WB/                          ← 另一个 Experiment ID
    WB01_raw.tif
    WB01_show.png
  细胞活力检测/                 ← 第三个 Experiment ID
    CV01_raw.xlsx
    CV01_show.png
```

**关系**：`ResearchProject 1→N Experiments`；`Experiment 1→N Samples`；`Sample 1→N Assets`；`Panel 0..1↔0..1 Experiment`。外层研究项目**不关联 Panel**，不限制一项研究对应多少论文、也不限制论文跨几个研究项目使用实验。内部 ID 不在普通 UI 显示。

E6 只在既有 E1 实验根目录外添加组织分组；绝不把“氧化应激研究”本身当作一个大实验，从而让 ROS/WB/细胞活力检测合并或导致 RAW/SHOW 样本误配。

## 2. E6 SQLite 新增表

核心源码：`native/PaperArchive.Core/ExperimentWorkspace.E6ResearchProjects.cs`、`ExperimentWorkspace.E6VerifiedMoves.cs`。

| 表 | 用途 | 关键约束 |
|---|---|---|
| `e6_research_projects` | 研究项目 ID、显示名、规范化根目录、创建/更新时间 | 根目录 `UNIQUE`；重命名显示名不改变 ID |
| `e6_research_experiments` | 研究项目与**已有 E1 root_id** 的归属映射 | `root_id PRIMARY KEY`，一个实验根目录当前仅归一个研究项目 |
| `e6_research_attachments` | 根目录下非实验的方案、汇总、文献等附件 | `UNIQUE(research_id, relative_path)`；原始字节存放在现有 SHA-256 Blob 仓库 |

已有 `e1_experiment_roots`、`experiment_units`、`sample_units`、`experiment_assets`、`e2_panel_experiment_links` 和归档 `workspace_archive_revisions` **没有破坏性迁移或变更主键**。

旧版尚未列入研究项目分组的实验显示在“未分组实验”，仍可供 Figure A/B/C 使用。研究项目名称可重命名；通过 `AssignE6Experiment` 可显式修改已登记的实验所属分组，**不移动磁盘字节，也不改变 Experiment ID**。

### 2.1 一键导入

`PreviewE6ResearchImport(researchDirectory)`：只读识别最外层文件夹名称、其**直接子文件夹**作为实验，以及根目录的研究项目级附件。一个实验内部的 `raw_data/process_data/show_data/quant_data` 等继续交给 E1 文件名索引；如果选到只有这些阶段目录的单一实验，不会错误把阶段目录当成独立实验。

`ImportE6ResearchProject(researchDirectory, displayName?, preserveVerifiedMovedIds?)`：
1. 为最外层目录取得研究项目稳定 ID；导入同一路径再次获取同一 ID。
2. 每个直接子文件夹独立调用 E1 `ImportResult`，内部仍以 ROS01 等文件名归样本与阶段；同一来源路径在任何已知旧项目登记过时复用已有 E1 root/Experiment/Sample/Asset ID，而不另建一个实验。
3. 研究项目根目录自己的方案/说明文件进入 `e6_research_attachments`，不会错误贴 ROS、WB 等样本标签；与 E1 一样保存内容寻址 Blob 原始字节。
4. 重复导入对未变动文件零新增 Asset；新增/改变文件只更新当前 E1 有效映射，历史资产和已归档 Revision 保持不可变。
5. 完成后返回研究项目/实验/新增文件数量；研究项目可直接用于 UI 分组浏览和搜索。

**注意：** 多实验的批量导入由每个 E1 实验自己的 SQLite 事务构成，不是跨全部实验文件的单个大事务。如果途中报错，已成功导入的部分**不会擅自删除**；修复问题后重复导入是幂等的，UI 会提示这是可恢复的部分导入。

### 2.2 原实验文件夹已被移动时如何保留 ID

例如以前导入过 `D:/数据/ROS`，现在移动到 `D:/数据/氧化应激研究/ROS`：

1. E6 `PreviewE6VerifiedMoves` 查找旧路径已经不存在、实验目录名匹配的 E1 根目录；
2. 对新路径下**每一个已经登记的有效文件**核对相对路径和 SHA-256；旧根已消失、所有文件匹配且唯一，才显示“可安全沿用原实验 ID”；
3. 导入确认弹窗明确列出识别的移动实验；只有确认后才允许 `preserveVerifiedMovedIds:true` 重定位 E1 根目录；
4. 重定位后正常增量导入，Sample、Asset、Experiment 和 E2 Panel 关联全部不更改；
5. 如果旧路径仍存在（复制而非移动）、内容不同、原路径歧义或来源不完整，**不能因为同名就静默合并**。继续按独立实验创建或要求先受控核对。

这避免旧数据在目录重组时被静默删除或重新分配，严格遵守 E0–E2 的稳定身份约束。

## 3. E5 新工作台中的 UI

修改 `E3WorkspaceWindow.cs`，新增 `E3WorkspaceWindow.E6.cs`。

在「实验资料库」提供：
- **导入研究项目**：直接选择最外层“氧化应激研究”，预览 ROS、WB、细胞活力检测和项目级文件数量，确认后批量导入；
- **导入单个实验**：继续支持过去选择 `ROS/`、`WB/` 等单个根目录的操作；
- **目录拖入**：识别有多个普通实验子目录时询问“研究项目”还是“单个实验”；识别 `raw_data/show_data` 等阶段文件夹时默认按单个实验处理；
- **分组列表**：按「氧化应激研究 → ROS / WB / 细胞活力检测」展示，原来没有分组的实验放在“未分组实验”；
- **研究项目文件**：从顶部研究项目下拉框可浏览与样本无关的研究方案、文献笔记和汇总资料；
- **重命名研究项目**：更新显示名称，不改变 Experiment ID、Panel 关系及历史归档；
- **Figure 卡片与实验选择器**：已关联实验显示 `研究项目名 / 实验名`，数据库仍只存唯一 Experiment ID。

E5 全局搜索增加研究项目名称、研究项目级文件名，以及所属研究项目内的实验定位。搜索、预览、原始内容加载不将研究项目 ID 显示给用户。

## 4. E6 验收与安全性

Workspace SmokeTests 新增：
1. 整个“氧化应激研究”下 ROS/WB/细胞活力检测三个目录→三个独立实验 ID；
2. 同一路径此前已单独导入过 ROS：E6 归组时沿用原 Experiment、Sample 和 Asset ID；
3. 重复导入父目录：零新增 Asset、同一研究项目 ID、同样的三条实验映射；
4. 在另一研究项目下也存在 ROS/ROS01：实验及样本 ID 严格隔离；
5. Figure A/B/C 分别绑定三个实验，数据库一对一约束依旧有效；
6. 研究项目重命名不改变任何 Panel→Experiment 关联；
7. 根目录 `实验方案.txt` 被归入研究项目附件并安全存入 SHA-256 Blob；
8. 旧 ROS 路径真实消失且新路径的每一个文件哈希完全一致时，确认后沿用原 E1 根目录及实验/文件 ID；
9. `raw_data/show_data` 等阶段目录不会被误识别为多个实验；
10. 研究项目和项目级附件进入全局搜索；
11. E4 旧 Revision JSON 与 SHA 仍然不可变。

此前 E0–E5 的 Workspace SmokeTests、旧归档兼容、E1/E2 维护 CLI 和 Windows WPF 构建均作为回归一起运行。

## 5. 本地更新与验证

Windows PowerShell（先备份数据仓库目录，包括 SQLite 和 Blob）：

```powershell
git fetch origin
git switch feat/v16-task-driven-workspace
git pull --ff-only origin feat/v16-task-driven-workspace
dotnet run --project native/PaperArchive.Wpf/PaperArchive.Wpf.csproj
```

打开主程序顶栏 **「论文工作台」→「实验资料库」→「导入研究项目」**，选择最外层“氧化应激研究”文件夹，检查预览中列出三个实验。确认后依次选择 ROS、WB、细胞活力检测，检查各自样本与 RAW/PROCESS/SHOW/QUANT；回到 Figure 1，把三个实验分别拖到 A/B/C，归档并查看旧 Revision 是否不变。

**未声称已访问用户本机真实科研数据**：本阶段通过构造的 Windows 自动化数据库验收，真实目录重组与拖放手感、图片格式、150% 缩放和大数据量性能仍应在本机人工测试。

## 6. 本阶段边界

E6 不把整个研究项目自动绑定 Figure，也不自动确认 PROCESS/RAW 顺序；旧样本、论文全文和成图的业务主键保持不变。研究项目级附件默认仅属于研究项目资料库，不会被悄悄算入某个 Panel 的实验资产快照。若未来需将某项研究级汇总文件作为图的实际来源，应明确归入对应实验或设计单独的引用入口。

E6 代码和测试验收完成后再考虑统一隐藏旧 WPF 主窗口；这不属于本次四级目录迁移的必要条件。
