# PaperArchive V1.6-E5 — 三主入口整合、索引搜索与桌面性能收尾

> 开发日期：2026-11-13
>
> 分支：`feat/v16-task-driven-workspace`
>
> 状态：**E5 代码已交付；自动化验收以主阶段文档最终 CI 为准。真实 Windows 图形界面人工操作需本机验收。**
>
> 前置：E0 数据审计、E1 稳定实验索引、E2 Panel↔Experiment 一对一、E3 Figure 工作台、E4 Manifest v3 完整归档。

## 1. 三大主入口的最终信息架构

在原程序顶栏点击 **「论文工作台」**，打开统一三入口的 `E3WorkspaceWindow`。窗口内不再需要跳回旧 D3/D4 树形界面：

| 入口 | 主要内容 | 典型操作 |
|---|---|---|
| **论文工作台** | 论文列表、Figure 选择、A/B/C 子图卡片、可拖放实验、右侧样本/文件与图片预览 | 论文 → Figure → Panel → 拖入实验；导入成图/全文并执行 E4 归档 |
| **实验资料库** | 不要求先创建论文；实验根文件夹列表、文件名过滤、样本及当前有效 RAW/PROCESS/SHOW/QUANT/其他附件 | 拖入目录，自动识别样本；单击样本或查看整个实验，双击文件用系统默认应用打开 |
| **论文档案库** | 已归档论文列表、Revision 选择、按冻结 Manifest 展开的 Figure→Panel→实验→样本→文件和预览 | 查看历史 Revision 1/2/3，直接阅读当次归档索引，不从当前工作区重建旧版本 |

三个入口在同一窗口顶部切换。原窗口继续保留，供尚未迁移的旧 D3/D4 数据使用；本阶段不删除任何旧表、旧归档或历史文件。

**UI 不展示数据库 ID、SHA-256 和 PROCESS/RAW 人工加工顺序**，这些只在后台日志、数据库与维护工具中存在。

### 1.1 统一搜索

同一窗口顶部有全局搜索框：支持论文名称、实验名称、当前实验文件名、历史归档文件名、论文 Revision 归档路径；匹配后双击/回车跳到对应的论文、实验文件或冻结历史文件。

- 工作区使用 `ExperimentWorkspace.SearchE5Workspace(query,limit)`，从论文/实验/当前 E1 文件索引读取；
- 归档使用 `ArchiveRepository.SearchE5Archives(query,limit)`，**只读取已保存的 Revision 资源表**，不借当前实验成员推断历史；
- SQL 使用有界 `LIMIT`、大小写不敏感 LIKE 和参数化文字转义；`%`、`_` 不解释为任意字符；
- 输入等待 300ms 后搜索，数据库检索在后台线程执行，新输入到来取消过时展示；最多显示约 120 个候选，避免大数据库直接堆积 UI 项；
- 搜索结果只显示名称和路径提示，内部导航 ID 不显示在界面上。

### 1.2 实验文件浏览准确性

`ExperimentWorkspace.ListE5CurrentExperimentFiles(experimentId)` 使用 E1 的**当前相对路径→Asset ID**映射：未变化文件复用旧 Asset，变更文件展示新的当前 Asset；旧版本保留在历史来源/Revision 中，但不混进当前实验资料浏览。

没有可靠源目录身份的旧实验仍按 D2 稳定样本成员读取，不自动把同名文件夹合并。用户选择单个样本时只展示其对应文件；取消样本选择时展示整个实验的当前文件和未分组附件。搜索和界面都不推断 RAW→PROCESS→SHOW 的加工先后。

### 1.3 UI 性能与资源保护

- 实验、样本和文件 ListBox 开启 WPF `VirtualizingPanel.IsVirtualizing` 与回收虚拟化容器；
- 图片预览改为 `Task.Run` 后台读取和约 720px 缩略图解码，`BitmapImage.Freeze()` 后回传给 UI；切换文件时取消上一次预览，避免加载大 TIFF 阻塞界面；
- 实验文件夹列表在一个窗口生命周期内缓存，导入后主动失效缓存；不为每次卡片选择重新扫描全部实验项目；
- 图片预览始终读取 SHA-256 内容仓库，不保存覆盖原件；双击打开生成系统临时副本，原始 Asset 保持不变；
- 归档历史图片从历史 Manifest 里的 SHA 读取，不从当前 SHOW 重建；
- 文件夹导入使用后台任务，不占用 UI 线程做全目录哈希。

## 2. 新增及修改文件

- `native/PaperArchive.Core/ExperimentWorkspace.E5Search.cs`：全局检索与仅当前有效文件集合查询；
- `native/PaperArchive.Core/ArchiveRepository.E5Search.cs`：历史 Revision 资源检索；
- `native/PaperArchive.Wpf/E3WorkspaceWindow.E5.cs`：统一顶栏/三主入口/全局搜索/历史 Revision 树形导航/后台图片预览；
- `native/PaperArchive.Wpf/E3WorkspaceWindow.cs`：整合导航、当前文件视图、缓存与无论文时的独立实验导入；
- `native/PaperArchive.Workspace.SmokeTests/Program.cs`：E5 SQL 查询、当前版本隔离、归档历史查询测试。

没有改变 E1/E2/E4 的关系数据库主键、双向一对一唯一约束、Blob 内容、schema v1/v2/v3 Manifest 或不可变 Revision。

## 3. 自动化验收清单

| 场景 | 测试方法 |
|---|---|
| 论文/实验/文件可检索 | SmokeTests 中导入的 E4 论文、文件夹和 E401_show 图片均由统一 SQL 索引查出 |
| 搜索通配符作为普通文字 | `%` 不匹配所有记录、`_` 仅匹配真正含下划线的文件/实验 |
| 当前实验视图不包含被替换 SHOW | 修改同源 SHOW 后，`ListE5CurrentExperimentFiles` 只包含新的 Asset ID |
| 非图像实验附件不丢失 | `实验说明.txt` 仍在当前实验有效资产集合 |
| 旧实验样本兜底 | E1 根目录未确认的旧实验仍能用 D2 已有样本成员读取文件 |
| 历史同名文件跨 Revision 搜索 | 归档库按文件名检索得到 Revision 1 和 Revision 2，内容 SHA 各自保留 |
| 旧归档不变 | 执行 E5 搜索后，v3 Revision 1 和 Revision 2 的 Manifest JSON 均不变化 |
| Windows 兼容构建 | WPF Desktop GitHub Actions 编译 Core、旧版、Workspace SmokeTests、E0/E1/E2 工具、WPF 并发布便携版 |

## 4. 本地真实 Windows 人工验收

**GitHub Actions 的成功仅证明代码和自动化测试通过，不能代替图形界面鼠标与高 DPI 真机验收。** 在 Windows 10/11、1366×768/1920×1080、高 DPI 125%/150% 按以下流程核对：

1. 用真实数据仓库启动主程序，点顶栏「论文工作台」，确认新窗口顶部可在三个入口间切换且无内部数据库 ID 显示。
2. 不创建论文，先在「实验资料库」导入一个文件夹（按钮和文件夹拖入均试），检查 ROS01 的 RAW/PROCESS/SHOW/QUANT 以及实验级未归组附件。
3. 新建论文和 Figure 1 的 A/B/C，把三个不同实验分别拖入，验证一个实验不能重复给 B/C，确认替换提示与取消的行为。
4. 单击 A，先查看整个实验所有样本和阶段文件，再选择一个样本，确认不混入另一个样本；双击真实 TIFF/PNG，检查使用默认应用打开。
5. 在全局搜索输入实验名、样本文件名、论文名、历史归档文件名；按 Enter 或双击结果，验证跳转及取消输入时的行为。
6. 为论文导入全文、Figure 成图并归档 Revision 1；修改一个 SHOW 后增量扫描再归档 Revision 2；在「论文档案库」分别查看两个冻结来源树和内容。
7. 对包含大量图片的实验滚动列表并快速切换预览；确认 UI 不明显卡顿、无跨文件串图；检查低分辨率和 150% 缩放时导航和归档按钮仍可达。
8. 人工备份仓库，再打开旧版论文验证 schema 1/2 历史 Revision 不受 E5 新界面与搜索影响。

若本机出现行为问题，按界面项记录复现输入、窗口缩放、文件格式、弹窗提示和日志；不以 CI 成功掩盖尚未执行的真机验收。

## 5. 发布边界

E5 交付“三入口统一窗口 + 搜索 + 当前/历史文件隔离 + 性能基础优化”，**没有删除旧前台窗口**。原程序顶栏「论文工作台」是进入新三入口工作台的方式，旧前台保留作为历史兼容。

若仍有 E0/E1/E2 旧库迁移冲突，在受控备份和用户确认前不会自动合并实验或抢占 Panel。E4 新归档需要完整 Figure/Panel/Experiment 绑定与内容 SHA 完整性。

下一步建议在真实 Windows 数据库上完成上述人工体验与迁移验证，再决定是否将新三入口窗口直接设为默认首页并隐藏旧界面入口。
