# Data-M S13：既有科研归档目录智能导入（开发与验收）

> 阶段：S13-01–S13-07。原始基线 `635a971de70544fd298c3d3547b5bce872f56f59`，分支 `feat/s13-smart-folder-archive-import`。**代码已提交，machine_pending / manual_pending**；不得将旧 S12R CI 作为 S13 验收，也不得合并 main。

## 目标与范围

将 Windows 中已有的科研归档文件夹，批量映射到 Project → Theme → Assay → Group → Replicate，并按后缀生成 Raw、Processed、Statistics、Display 原生资产。新功能由 S13 任务队列管理、S1/S2 真实节点 CRUD 和 S3 逐文件持久任务组合而成；不直接写入或删除 CAS，不移动原始 Windows 文件。

## S13-01 安全扫描

原生 `archive_import` 的 `scan` action 接受用户选中的**一个根文件夹**。UI 支持 Windows 文件夹选择器和 Tauri OS 文件夹拖放。递归深度最多 20 层、单任务最多 50,000 个普通文件；使用原生 metadata + SHA-256 流式校验，不接受符号链接、Windows reparse points、特殊文件或仓库内部/祖先路径。文件修改时间/大小/哈希在预检时再次检查。只读源文件，不修改原文件夹。扫描清单分 500 项分页显示。

扫描可能读取大量源文件的全部字节以取得 SHA；对于大型实验请保持外接磁盘联机并使用不变的原文件。取消选择目录不会生成任务。扫描任务状态持久保存在本机 `s13-archive-queue.sqlite`（项目 SQLite 的旁路队列数据库），不改变现有 schema v3 和不可变 CAS 结构。**S7 的项目备份不等于 S13 任务队列备份**；任务队列需要单独保存，不把任务队列的快照当作科研数据源备份。

## S13-02 目录映射

支持三种确定性布局：

- `project_theme_assay_group`：根目录为项目，第一层主题，第二层实验，第三层分组。
- `theme_assay_group`：根目录为主题，第一层实验，第二层分组；必须选择已有项目。
- `assay_group`：根目录为实验，第一层分组；必须选择已有项目并填写所属主题。

实验内部可有额外普通分类目录，不将更深目录静默作为另一个项目/实验。缺少分组目录的 Raw/Processed 进入待确认，除非用户明确填写默认分组；实验级 Statistics/Display 不依赖 Group。用户可在预览中改名、重新指派阶段、分组、样本，或者明确跳过。

## S13-03 样本及处理阶段

文件名 `ROS01_raw.tif` 自动识别样本 ROS01、Raw；`ROS01_process_crop.tif` 标记 ROS01、Processed、处理子标签 crop；`ROS01_show.png` 识别 Display，附带 ROS01 **候选来源**。Statistics 和 Display 依然是 assay-scoped；用户不能凭文件名虚构冻结统计样本、Paper/Panel 固定版本或代表来源。图像 `_quant.tif` 对应 Processed 和 quant 用途标签；`_quant.csv` 必须显式判断是样本定量还是统计结果，默认待确认。无法识别的命名同样待确认。

导入队列记录 `relativePath`、分层名称、`processTag`、SHA、状态，报告仅导出相对路径（不暴露 Windows 绝对源路径）。S13 保存的标签属于导入清单元数据；后续 Panel 统计用途仍由 S8/S9/S10 真实引用建立。

## S13-04 识别预览和冲突处理

先扫描 → 浏览预览 → 校对待确认条目/批量操作 → **检查冲突与源文件**。预检会读取已存在的同项目/主题/实验/分组/样本作用域、文件名与版本 SHA。相同目标与相同 SHA 自动跳过；同名不同内容默认进入待确认，必须明确跳过或指定新显示文件名。目标层级中存在同名歧义或源文件变化时阻止直接导入。S13 不支持以“覆盖”方式改写已有版本，也不向旧文件的 blob 写入。

## S13-05 真实批量导入与恢复

用户点击“确认批量导入”之后才创建或匹配真实 Project/Theme/Assay/Group/Replicate，再根据已确认的 assay + stage + group + replicate 将文件分成每批 **最多 500** 项（小于 S3 1000 项安全上限）。批量操作调用原有 S3 prepare、start、get、retry；通过新增的可选 `aliases` 字段把**新逻辑显示名称**传给 S3，不修改本地源文件名。

每次 S3 prepare 返回的 jobId 都必须写入持久 S13 队列之后才启动；重启后先检查已记录 jobId 的原生进度，使用已有 S3 重试逻辑，不重复创建 CAS 固定版本。S13 的 reconcile 从真实 S3 `import_files` 与 `asset_versions` 查询固定的 SHA 与 version identity，不能由浏览器自行宣布“已成功”。

任务状态包括 scanned/review/prepared/running/paused/failed/completed；关闭向导不会回滚已提交的文件。暂停停止界面调度，正在运行的原生 S3 任务可能完成，继续时核验原生真值。批量过程中可出现部分成功；不得宣称整任务具有一次性跨文件系统原子性。

## S13-06 UI、模板和报告

正式 Workbench 顶栏新增“智能导入文件夹”，与 S3 “导入实验归档包”并列；即使项目为空也可使用。预览显示当前文件数、待确认、已验证、跳过、失败及当前任务；可以加载更多分页、手工/批量校对、保存与加载目录识别模板、查看最近任务并继续、导出带 SHA 的完整 JSON 导入报告；完成后跳转到真实科研工作区。

## S13-07 安全、自动化验收

- Rust `s13_` 测试：命名阶段、Process 子标签、源路径逃逸与符号链接、只读扫描、文件改动、重复 SHA/同名冲突、任务持久化、超过 1,000 条目录项的分页、导入计划恢复及模板。
- Vitest：S13 IPC 对象严格验证、重复身份、报告及多目标协调器。
- Playwright：Windows 目录选择器 Mock、待确认 quant 校对、预检、创建结构、S3 受控提交、Workbench 结果展示。
- Windows：现有 S0–S12R + I1 回归、S13 用例、Rust full suite、10,024 资产基准、Tauri Release EXE、NSIS、Release Manifest 及 SHA256。
- 最终状态只能由 **同一 SHA** CI 和安装包产物确认；人工 Windows 11 EXE 与真实科研目录的误操作测试必须保留 `manual_pending`。

## 不在本阶段隐式实现

不解压 ZIP 或 7z、不自动关联论文 Panel、不凭文件名冻结统计纳入、不覆盖科研原件、不自动修改数据处理图像、不自动选择生物学代表来源、不合并 main。

## 安装与测试提醒

此阶段构建为未签名科研测试版。先使用可丢弃的科研数据和备份验证目录结构、命名冲突、源文件只读与断点恢复；不要以尚未签收的软件保存唯一的原始研究数据。
