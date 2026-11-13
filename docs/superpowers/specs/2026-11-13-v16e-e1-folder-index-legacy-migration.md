# PaperArchive V1.6-E1 — 文件夹优先索引与旧数据受控迁移实施记录

> 日期：2026-11-13
>
> 状态：**E1 实现完成；自动化验收记录见本文末尾。** 真实用户本机旧数据库未远程读取，迁移需在本地先预览后决定是否应用。
>
> 上一阶段：[E0 只读审计](./2026-11-13-v16e-e0-audit-migration-design.md)；主规划：[V1.6-E 总体阶段规格](./2026-11-13-v16e-folder-first-figure-workspace-roadmap.md)。

## 1. E1 的目标与用户行为

以**一个真实实验根文件夹**为入口，自动归并该目录中的多样本、RAW/PROCESS/SHOW/QUANT 文件。新扫描不需人工确认加工顺序；样本与阶段均为系统索引，不等同于人为确认的加工步骤。

- 同一物理根路径：一个稳定实验身份；再次扫描未变化的文件不会生成新的 Asset。
- 同一根路径下新文件/变化内容：仅为变化项新增不可变 Asset/版本；未变化的 Asset ID 不变。
- 不同物理根路径即使文件夹显示名称完全相同：也不能自动并成同一实验；在项目内安全区分显示名称，如 `SharedName`、`SharedName (2)`。
- 同一实验按规范化文件名前缀得到稳定 Sample ID；PROCESS 子类型可开放命名，**绝不推测 RAW→PROCESS→SHOW 的处理顺序**。
- 移动过的原工作区 Asset 仍保留旧 ID 和原用途；重新扫描原源目录，会为其当前来源建立有效新资产映射，而不是把移去其他项目的资产继续算作原目录的当前文件。
- 同一目录暂时删去文件：仅把文件从“当前源目录索引”中移除；原工作区的不可变 Asset、旧成图与归档 Revision 完全保留。恢复同一路径、同 SHA 的内容时复用已知历史 Asset ID。
- 无法可靠识别的图片、文档与统计文件仍保留在原实验/结果中，待 E3 UI 的“其他文件”显示，不因识别失败丢弃。

## 2. SQLite 实施：新增三张索引表 + 受控迁移日志

`native/PaperArchive.Core/ExperimentWorkspace.E1FolderIndex.cs`：

| SQLite 表 | 作用 | 关键约束 |
| --- | --- | --- |
| `e1_experiment_roots` | 同一个项目内源目录的规范身份、稳定 Experiment ID 与显示名 | `UNIQUE(project_id, normalized_root)`、`UNIQUE(project_id, experiment_name)` |
| `e1_root_result_batches` | 记录新/旧每个结果批次来源于哪个实验根目录 | `result_id PRIMARY KEY` |
| `e1_root_current_assets` | 根目录当前每个相对路径所指向的 Asset ID 与 SHA | `PRIMARY KEY(root_id, relative_path)` |

既有的 `experiment_units`、`sample_units`、`sample_unit_assets`、`experiment_assets` 和 `asset_system_tags` 保留，源文件、Content Addressed Blob 和归档数据库**不删不改**。

注意：根目录显示名不是全局唯一身份；源目录路径仅用来更新**当前**工作区文件集合，归档使用未来 E4 的不可变资产快照。复制一份相同内容到其他位置不自动与旧实验合并。

### 2.1 新导入/增量扫描

`ExperimentWorkspace.ImportResult(projectId, sourceDirectory)` 已引入 E1 路径登记和差量判断，兼容现有导入 UI：

1. 逐项读取并校验原文件字节 SHA-256，保护已有 Blob，不覆盖文件。
2. 查询 `(project_id, normalized_root)`；首次出现时创建根目录身份，内部关联稳定 `experiment_units.id`。
3. 如该源路径及 SHA 未变化：返回既有结果，无新 Asset；如新增/改变：只为变化文件创建版本化的新 Asset。
4. 旧工作区文件移动到其他结果/项目、被软删除时，不能继续作为这个根目录的有效当前文件。重新扫描时正确恢复当前索引。
5. 删除源文件（包括整个根目录变空）仅更新 `e1_root_current_assets`，不物理删除 Asset/Blob 或修改旧归档。
6. 恢复已经导入过的路径与完全一致的字节，查原文件夹历史结果并复用对应 Asset ID。
7. 创建新资产的标签仍沿用 D1/D2 稳定样本标注；PROCESS 锚点候选现在**只从同一实验的结果标签范围内计算**，不再将其他实验中的同名样本用作自动 PROCESS 锚点。

### 2.2 根目录搬迁与旧实验认领

- `RelinkE1FolderRoot(rootId, newPath)`：显式重定位，要求当前所有相对路径和 SHA-256 在新目录可验证；不改 Asset ID 或归档关系。
- `AdoptE1LegacyFolder(experimentId, sourcePath)`：旧实验原先没有记录物理源路径时，操作者明确指定目录；要求至少一个旧 Asset 的相对路径与 SHA 同时匹配才允许绑定原 Experiment ID。
- **绝不**用同名文件夹或相同内容哈希自动认领旧实验；验证不通过时报错，不写入部分关系。
- 源目录归属仅表示当前逻辑实验与文件位置，不替换旧 Paper/Figure/Panel 用途和归档快照。

## 3. 老数据库：预览 → SQLite 备份 → 事务补建 → 可撤销

`native/PaperArchive.Core/ExperimentWorkspace.E1LegacyMigration.cs`：

- `PreviewE1LegacyMigration(projectId)`：纯只读，列出缺少 `asset_system_tags` 的旧资产、样本识别、阶段分类、PROCESS 子类型及无法确定的记录，并给出基线指纹。
- `ApplyE1LegacyMigration(projectId, expectedFingerprint, backupDirectory)`：备份必须位于数据仓库外；使用 **SQLite BackupDatabase** 生成 WAL 一致的 `workspace.db` 备份；在一笔事务中仅为缺失的旧资产插入标签并刷新 D2 样本成员关系。
- `UndoE1LegacyMigration(sessionId)`：仅在迁移后工作区基线未变化时允许撤销；恢复原样本成员、删除该次新增标签和空样本/实验记录；若已有其他业务修改，拒绝覆盖，应使用保存的数据库备份人工受控恢复。
- 旧结果如果没有可信物理文件夹标签，自动使用带 Result ID 前缀的**独立临时实验名**，不因为两个旧结果名称一样而合并。后续可调用 `AdoptE1LegacyFolder` 核验真实源文件夹。
- 文件名无法识别的项目保留 `unresolved` 或 `conflict`，不会擅自塞入别的样本；这些文件并未删除。
- 原有纸稿版本、Figure 完整成图和旧已归档 Revision 全部不变。

### 3.1 本地命令行操作

新命令行项目：`native/PaperArchive.E1Index/`，使用 .NET 10 SDK。

首先在 PowerShell 切到仓库根目录，关闭 PaperArchive 桌面程序，备份整个数据仓库目录（包含 SQLite 与 `blobs/sha256`）。然后执行以下命令；把 `D:\PaperArchive-Data` 替换为实际包含 `database/workspace.db` 的仓库路径：

```powershell
# 查看旧项目 ID（仅维护 CLI 内显示）
dotnet run --project native/PaperArchive.E1Index -- projects "D:\PaperArchive-Data"

# 预览旧数据中缺少的系统标签（只读；先让新版客户端初始化 E1 schema）
dotnet run --project native/PaperArchive.E1Index -- preview "D:\PaperArchive-Data" "<projectId>"

# 确认预览无误后应用（必须使用上一命令返回的 Fingerprint）
dotnet run --project native/PaperArchive.E1Index -- apply "D:\PaperArchive-Data" "<projectId>" "<Fingerprint>" "E:\PaperArchive-E1-Backup"

# 如尚未产生后续操作，可按上一步返回的 SessionId 撤销
dotnet run --project native/PaperArchive.E1Index -- undo "D:\PaperArchive-Data" "<SessionId>"

# 查看已记录的实验根文件夹
dotnet run --project native/PaperArchive.E1Index -- folders "D:\PaperArchive-Data" "<projectId>"

# 对旧实验显式关联真实根目录；必须通过相对路径+SHA 校验
dotnet run --project native/PaperArchive.E1Index -- adopt "D:\PaperArchive-Data" "<experimentId>" "D:\科研数据\01_ROS检测"

# 如果已登记的实验文件夹被整体移动
dotnet run --project native/PaperArchive.E1Index -- relink "D:\PaperArchive-Data" "<folderRootId>" "E:\科研数据\01_ROS检测"
```

`apply` CLI 会额外在数据库架构初始化之前先保存一次 `workspace.db` 的 SQLite 备份，然后调用 API 再备份一次后应用迁移；如果指纹不符则拒绝写入。

**重要：** 对尚未升级至新结构的旧仓库，先做完整外部备份，再使用新版客户端初始化 schema，之后运行只读 `preview`。E1 命令行暂属于维护工具，普通前台将在 E3 隐藏项目 ID 和数据库操作细节。

## 4. 自动化验收范围

本阶段已扩展 Workspace SmokeTests，覆盖：

1. 同文件夹重复扫描零新增 Asset、稳定根目录 ID/Experiment ID；
2. 同一源目录新增/变更一个文件，只有该文件生成新 Asset，历史版本仍在；
3. 原 Asset 移到其他项目后重新扫描，不错误复用已移走的 Asset；
4. 同名不同物理文件夹不会归并一个实验，同名样本跨实验隔离；
5. 不同物理文件夹同内容仍以实验身份区分；
6. 手动重定位文件夹，经逐文件哈希验证后，Experiment 和资产 ID 不变；
7. 源文件删除/还原/目录变空时当前索引正确更新，历史文件记录不丢；
8. 旧库缺少系统标签时预览、迁移、备份、再次预览零待迁移及撤销；
9. 错误原始文件夹即便文件名相同，也因 SHA 不匹配拒绝认领；
10. 旧实验通过路径+SHA 验证后，继续使用原 Experiment/Sample/Asset ID；
11. 原 D1–D4 文件和论文来源逻辑，以及旧版数据迁移兼容测试。

Windows GitHub Actions `WPF Desktop (.NET 10)` 编译 Core、旧版 SmokeTests、工作区回归、E0/E1 CLI、WPF 并打包 Windows 便携版；具体通过的 run 记录在主开发文档 E1 完成记录中。

## 5. E1 完成边界与 E2 交接

**E1 交付的是数据层与维护工具，不是新工作台。**

- E2 才会创建 **Panel↔Experiment 严格双向 UNIQUE 一对一关联表**及迁移/替换/解绑逻辑。
- E3 才会创建“实验资料库 + Figure 卡片工作台”新 UI，取消普通操作的技术提示、让用户从 Panel 直接浏览实验的 RAW/PROCESS/SHOW/QUANT 文件。
- E4 才会取消旧 D4 的 PROCESS/RAW 来源归档检查，改用实验全文件集合和不可变 Manifest v3。
- 历史原始字节与归档始终受保护；本轮不执行用户本机数据迁移，因用户真实仓库尚未在此会话连接或上传。
- E1 的受控迁移只能对文件名已足够识别的内容自动归组；无可信源目录、无可解析样本名等仍需 E3 的“其他文件”或专用维护操作处理。

**下一步进入 E2：Panel 与 Experiment 严格一对一索引、旧 D3 映射冲突预览、替换/解绑与事务撤销。**
