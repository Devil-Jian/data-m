# PaperArchive V1.6-E — 文件夹优先的 Figure 实验索引与极简工作台：阶段性开发规格

> 状态：已确认需求 / 待开发（本提交仅为开发文档）
>
> 日期：2026-11-13
>
> 目标开发分支：`feat/v16-task-driven-workspace`（建议实现时从当前稳定节点切出 `feat/v16e-folder-first-workspace`）
>
> 上一阶段基线：V1.6-D1～D4。原始资产与历史归档完全保留。本规格优先于旧版“逐张 SHOW 关联、手工确认 PROCESS→RAW 顺序”的前台工作流；旧版数据结构只作为兼容读取/迁移来源。

## 1. 一句话目标与非目标

**目标：** 将每个实验根文件夹自动映射为唯一实验，将其内部同名主体、不同阶段后缀的文件自动归为样本；用户只需将一个实验关联到 Figure 的一个子图 A/B/C，即可从论文 → Figure → Panel → 实验 → 样本 → 全部原始/处理/展示/定量文件检索，归档时冻结可复现的索引快照。

**不是**科研加工步骤管理器、人工 PROCESS/RAW 血缘审核器，也不是要求逐张 SHOW 确认的论文数据登记系统。前台不显示任何数据库 ID、SHA-256、解析状态技术字段（这些保留在后台诊断和导出清单中）。

## 2. 已确认且不可破坏的业务约束

1. 一个论文可包含多个 Figure；一个 Figure 可包含多个子图 Panel（A、B、C……）。
2. **Panel ↔ 实验严格一对一：** 同一 Panel 最多一个 Experiment；同一 Experiment 最多分配给一个 Panel。未分配允许为空；替换/解除是显式操作。若用户确需把同内容用于另一子图，须创建独立实验单元（不能静默复用同一 Experiment ID）。
3. **一个实验 = 一个逻辑实验根文件夹。** 文件夹里可有多个样本，并可含 `raw_data/process_data/show_data/quant_data` 等子目录；子目录不自动成为另一个实验。
4. 实验包含多个样本；同实验内按标准化样本名建立稳定 Sample ID；样本关联任意数量的 RAW、PROCESS、SHOW、QUANT 文件，以及其他文件的实验级归属。
5. **仅按文件名/目录判断样本与阶段，不推断 RAW→PROCESS→SHOW 的加工先后。** 自动找到全部同样本文件就是预期溯源能力。
6. 导入、再次扫描、重命名/移动、版本管理均不得修改原始文件内容；Asset ID 稳定、SHA-256 不变；相同文件名但不同字节不能混为一个 Asset。
7. 不覆盖旧归档、历史完整 Figure 和旧 schema；已归档 Revision 只读且其文件清单与索引不随工作区修改而变化。
8. 操作以文件/文件夹名称、图像预览和搜索为中心；仅少数不可逆或可能覆盖工作区关系的操作需要二次确认。

### 2.1 必须正确区分的实体

- **实验文件夹**是资源集合；不等同于软件的论文项目，也不等同于可重复导入的单次“结果批次”。
- **样本名**仅在同一 Experiment 中唯一；不同实验的 `ROS01` 绝不跨实验自动合并。
- **Panel 绑定实验**意味着从该子图可查看实验的所有样本和文件，**不等于所有 SHOW 实际出现在成图里**。
- **Figure 成图文件**（例如 `Figure1.png`）是独立的版本化展示资产；实验文件是可追溯来源集合。
- **论文全文**与具体 Figure 完整成图版本保持明确关联；新版本归档必须重新冻结相应快照。

## 3. 目标数据模型与唯一索引

主链：

```text
Paper ─< Figure ─< Panel ──(0..1 : 0..1)── ExperimentFolder
                                              ├─ Sample 1 ─< Asset(s)
                                              ├─ Sample 2 ─< Asset(s)
                                              └─ OtherFiles ─< Asset(s)
```

建议优先复用现有 `figure_templates`、`panel_templates`、`experiment_units`、`sample_units`、`experiment_assets`、`sample_unit_assets`，新增兼容关联表：

```sql
CREATE TABLE panel_experiment_links (
  panel_id TEXT PRIMARY KEY REFERENCES panel_templates(id) ON DELETE CASCADE,
  experiment_id TEXT NOT NULL UNIQUE REFERENCES experiment_units(id),
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
CREATE INDEX ix_panel_experiment_experiment
  ON panel_experiment_links(experiment_id);
```

注意：上方为**目标结构示意**，实际迁移需检查旧数据中的重复分配和孤儿关联；不得先建唯一索引再强行丢弃冲突数据。工程实现应按 SQLite 当前迁移框架执行并启用外键、事务和数据备份。

配套建议：
- `experiment_folder_sources`：Experiment ID → 导入根目录的规范化来源身份、显示名称、首次导入路径、历史路径、数据来源与可信度。显示名称可以重复；**绝不能仅用文件夹名称全局判唯一**。
- `experiment_asset_membership`（如现有 `experiment_assets` 到 Result/Project 的关系足够可推导则用视图）：保证每个 Asset 可归到确切实验，不因批次导入造成重复计数。
- `sample_unit_assets`：一个 Asset ID 只归一个样本；不确定样本的文件归属实验的“其他文件”。
- `panel_experiment_history` 或轻量操作日志：记录替换/解除，支持立即撤销；**与不可变归档 Revision 区分**。
- `figure_experiment_snapshots`：完整成图版本生成时的 Panel→Experiment、样本文件列表快照；归档时复用/固定当前已选的成图版本及完整来源集。
- `workspace_archive` 新版 Manifest（建议 schemaVersion=3）：保存 Paper/Figure/Panel/Experiment/Sample/Asset 的稳定 ID、文件名、阶段、哈希、归档时内容引用和被采用的 Figure 完整成图版本。旧 schema 1/2 原样只读，绝不反写历史。

唯一性：
- Experiment ID 稳定；来源根路径、导入批次与显示名称不作为唯一身份的充分条件。
- Sample `UNIQUE(experiment_id, normalized_sample_name)`，保证大小写/分隔符规则一致。
- Panel `PRIMARY KEY(panel_id)` + Experiment `UNIQUE(experiment_id)` 施加双向 1:1。
- Asset `id` 永远是实体主键；相同哈希为同一内容但**不一定**为同一实体；显示层可合并，归档层不得漏掉不同的合法资产引用。
- 同一物理文件夹重复扫描：默认增量更新同一 Experiment，保证可重复执行（幂等）；**复制到另一路径的目录不能擅自认为原实验的同一身份**。

## 4. 文件名匹配与导入契约

示例：

```text
01_ROS检测/
  raw_data/ROS01_raw.tif
  process_data/ROS01_crop.tif
  process_data/ROS01_adjusted.tif
  show_data/ROS01_show.png
  quant_data/ROS01_quant.png
  raw_data/ROS02_raw.tif
  show_data/ROS02_show.png
  ROS实验说明.txt
```

自动得到一个 Experiment `01_ROS检测`；样本 `ROS01`、`ROS02`；说明文档归到实验“其他文件”，无需伪造样本。

解析顺序：
1. 固定后缀：`_raw`、`_show`、`_quant`、`_process`，支持大小写、后缀前 `-/_`、版本 `_v2` 等；优先剥离明确版本标识后提取样本主体。
2. 在**当前实验范围**内，依据已有固定后缀样本名锚点，把 `ROS01_crop`、`ROS01_adjusted` 等识别为 PROCESS（仅分类，不推断处理先后）。
3. 阶段子目录用作辅助信号。发生文件后缀和目录类别冲突时，以明确命名规则为准，并在该实验“需整理文件”提供低干扰提示；**不影响其他文件的导入和论文归档**。
4. `ROS01` 与 `ROS010` 以最长/完整分隔符锚点区分；模糊归属和无法可靠识别的文件统一放“其他文件”。
5. 多个 SHOW、同名不同 SHA 或同名不同版本：全部保留；主 UI 显示多个文件版本，可预览。基本流程不要求手动选用一个 SHOW。
6. CSV、Excel、文档等非图像文件作为实验其他/统计附件保留；不能强行标成 SHOW 或 QUANT 图片。
7. 增量更新：新增、删除、重命名、阶段目录移动均只改变当前工作区索引；旧 Asset ID 和历史版本不消失。需要区分外部源路径失效与内容仓库中受保护的原始副本。

## 5. 前端信息架构：仅三个主入口

### 5.1 实验资料库（文件夹优先）

默认：实验文件夹列表/卡片 + 搜索 + `导入实验文件夹`。每行只显示实验名、样本数、文件数及已被哪个 Figure 子图使用；可拖入 Windows 文件夹导入。

点击实验：左侧样本列表；右侧 SHOW 图片预览和 RAW/PROCESS/QUANT 文件名列表。单击预览、双击系统默认程序打开；右键仅保留打开/所在位置/复制路径等基础项。未识别文件统一归“其他文件”。同内容重复导入按“阶段＋文件名＋哈希”折叠并显示引用数；不删除 Asset。

### 5.2 论文工作台（核心）

论文列表 → 选论文 → Figure 顶部标签 → 子图 A/B/C 卡片。子图卡片主标题为 `A` 和对应的**唯一实验名称**，次要信息为样本/文件数；未分配显示 `+ 关联实验`。

高频路径：
1. 选择/创建论文（必要时导入论文全文）。
2. 创建 Figure，自动编号；添加 Panel，自动 A/B/C 编号。
3. 直接把实验卡片拖到空 Panel，或点 `+ 关联实验` 选择。成功即时生效，无多余弹窗。
4. 若实验已用于其他 Panel：禁止一对多绑定，直接提示被占用位置并可跳转；绝不静默抢占。
5. 若 Panel 已绑定另一实验：弹简单“替换实验？”确认；替换后支持撤销，旧 Revision 不改。
6. 单击 Panel：在**同一个窗口右侧**展示该实验的样本、文件名及预览，不打开多级弹窗。
7. 每个 Figure 顶部有组合成图区域：导入/替换 `Figure1.png`，可查历史完整成图版本。

Panel 卡片不露出 Asset ID、SHOW 选用、RAW/PROCESS 关系表或“待核对来源”。实验下全部文件自动可达。

### 5.3 论文档案库（只读）

论文列表 → Revision → Figure（成图预览）→ Panel A/B/C → 归档时的实验、样本、文件名列表。所有列表来自对应 Revision 的**冻结 Manifest**，而非实时实验索引。支持单击预览、打开归档原始内容、副本导出与全局搜索。

### 5.4 统一 UI 规范

- 主导航：`论文工作台`、`实验资料库`、`论文档案库`；右上全局搜索。
- 默认不显示数据库 ID、SHA、阶段解释、状态说明、候选来源及技术确认框；必要细节收于“高级信息/诊断”。
- 常用操作 1～2 次点击完成；少用模态对话框；确认只针对替换、删除、归档等高风险操作。
- Figure 卡片布局自适应；实验详情是内嵌侧栏而非弹窗；键盘/鼠标均可操作，拖放是便捷方式而非唯一通路。
- Windows WPF 采用 MVVM/可测试 ViewModel；大文件列表虚拟化和异步缩略图，避免 UI 线程扫描/哈希计算。

## 6. 归档契约：只检查真正需要的东西

正常流程：`归档论文` → 自动检查 → 摘要“X 个 Figure / Y 个实验 / Z 个文件” → 确认生成 Revision。

必须阻断：
- 选定论文全文/具体 Figure 版本不存在或未完成必要绑定；
- 要归档的 Figure 某个**实际保留的 Panel**没有 Experiment（产品应允许删除无用空 Panel，而非为它虚构实验）；
- 一个 Experiment 被多个 Panel 占用，或引用了不存在的实验/资产；
- 归档所需的原始字节丢失或 SHA-256 校验失败；
- 导入/归档期间数据索引发生变化而造成快照不一致（必须重新计算预检）。

**不再阻断或提醒**：
- SHOW 缺少 PROCESS 来源；
- PROCESS 没有 RAW 边；
- 用户尚未明确各 PROCESS 的先后顺序；
- 某个实验没有特定阶段文件（只有实验关联仍然允许归档，应如实保存其现有资产清单）；
- 文件名无法可靠归属样本，但已经安全归入实验“其他文件”。

归档采用事务+完整性校验；保存该次实验全文件集合、已采用 Figure 成图版本、Paper→Figure→Panel→Experiment→Sample→Asset 关系快照。**不能只写 Panel 的 SHOW 文件**；不允许后续工作区新增文件自动污染既有 Revision。

真正错误在可操作位置显示“Figure 2-C 尚未关联实验 → 去关联”，相同错误按具体 Panel/Asset 归并，不重复弹提示。

## 7. 从 V1.6-D 到 V1.6-E 的迁移原则

当前已存在：
- D1 `asset_system_tags`、实验识别标签；
- D2 `experiment_units`、`sample_units`、`sample_unit_assets` 和备注/用户标签；
- D3 `panel_sample_links`、`figure_panel_assets`、`composite_sample_source_snapshots`；
- D4 `ArchiveRepository.Workspace.cs` 的 Panel/SHOW 和 PROCESS/RAW 来源预检，Manifest v2；
- WPF 多树、多来源确认界面与 Sample Explorer。

迁移策略：
1. **先只读扫描并报告冲突**：以已有资源 ID / 结果批次 / 目录根为证据确定实验集合。旧数据没有足够根目录证据时不得只凭同名文件夹合并；保留“待分组”实验，不丢失文件。
2. 在同一事务中补建新表/索引并生成新 Panel→Experiment 映射；若旧 Panel 对应多个样本但它们都指向同一个 Experiment，可安全归约到该实验。若同一 Panel 的样本来自多个实验，或一个实验已有多个 Panel，归入少量**迁移冲突待处理**列表，不自动选一个覆盖。
3. 迁移工具提供预览、影响数量、显式应用、回滚日志；多次运行应幂等。工作区备份作为前置动作。
4. 旧 `panel_sample_links`、`figure_panel_assets` 与来源边不删除。过渡期兼容读取，但不能让两条索引路径在新界面生成重复实验/文件。新绑定以 `panel_experiment_links` 为准。
5. D4 旧归档 Manifest schema 1/2 原样只读，**不得**用当前实验成员重建旧历史来源；仅从 V1.6-E 首个新版归档起使用完整实验文件清单快照。
6. 通过灰度开关启用新工作台；确认新旧版本数据互操作与历史兼容后，隐藏原来的人工血缘确认入口，而不是删除旧数据库结构。
7. 迁移前后核对所有有效 Asset ID、Blob SHA、已确认 Figure 版本、旧 Revision 数量及 Manifest SHA 完全不变。

## 8. 分阶段开发计划与验收门槛

| 阶段 | 主目标 | 交付物 | 验收门槛 |
| --- | --- | --- | --- |
| **E0：基线/设计冻结（✅ 已完成，2026-11-13）** | 已审计新旧 SQLite 结构、实现只读审计与 1:1 迁移冲突预览 | `ExperimentWorkspace.E0Audit.cs`、`PaperArchive.E0Audit` CLI、[E0 迁移设计记录](./2026-11-13-v16e-e0-audit-migration-design.md)、回归测试 | Windows SmokeTests/CLI 编译与 WPF 打包已通过；真实用户数据库的审计计数须在本机执行 |
| **E1：实验自动索引（✅ 已完成，2026-11-13）** | 真实根文件夹身份、增量扫描、稳定实验/样本/Asset 索引及旧数据受控迁移 | `ExperimentWorkspace.E1FolderIndex.cs`、`ExperimentWorkspace.E1LegacyMigration.cs`、[E1 开发记录](./2026-11-13-v16e-e1-folder-index-legacy-migration.md)、E1 维护 CLI 与 SmokeTests | Windows Core/旧版/Workspace/CLI/WPF/打包全部通过；用户本机旧数据迁移需自行预览与执行 |
| **E2：严格一对一 Panel 绑定（✅ 已完成，2026-11-13）** | Panel ↔ Experiment 双向 UNIQUE，安全替换/解绑/撤销与旧 D3 数据受控迁移 | `ExperimentWorkspace.E2PanelExperiments.cs`、`PaperArchive.E2Links` CLI、[E2 开发记录](./2026-11-13-v16e-e2-panel-experiment-one-to-one.md)、回归测试 | Windows Core/旧版/Workspace/E0-E2 CLI/WPF/打包全通过；历史 D3/Revision 不被改写 |
| **E3：新论文工作台** | Figure 卡片、A/B/C、实验拖放与内嵌样本文件详情 | 全新 WPF 视图、实验选择器、预览、搜索/导航 | “创建 Figure→添加 Panel→绑定实验→查看 RAW”无需技术弹窗，键盘也可完成 |
| **E4：归档与版本** | 基于实验全文件集合的不可变 Revision | 简化归档预检、Manifest v3、Figure 成图版本绑定、只读归档浏览 | 无 PROCESS 链仍可归档；每条文件可按 Figure/Panel 回溯；修改工作区不影响旧 Revision |
| **E5：交互收尾/发布** | 统一三大入口、迁移旧 UI、性能与安装验证 | 快捷搜索、错误定位、数据备份/恢复、打包、操作手册 | 大样本列表操作流畅；Windows 手工拖放/预览通过；GitHub Actions 全通过 |

各阶段严格顺序：**E1 数据质量 → E2 一对一约束 → E3 新 UI → E4 新归档 → E5 收尾**；E0 是实施前的短准备。每阶段独立提交、运行原有 SmokeTests、增加针对性测试，Windows WPF 构建绿灯后才进入下一阶段。

## 9. 跨阶段自动化验收场景

1. 导入 `01_ROS检测`（3 个样本、跨阶段目录），可自动查到 `ROS01_raw/crop/adjusted/show/quant`，没有 PROCESS 顺序提醒。
2. 同文件夹重复导入/重新扫描两次：Experiment 和 Sample ID 不复制；实际有效内容不重复计数；历史 Asset ID 不更改。
3. `ROS01` 和 `ROS010` 不误匹配；另一个实验中的 `ROS01` 不被合并。
4. `ROS01_show.png` 有两个不同 SHA 版本：两者均可查；不能悄然替换旧文件的资产身份。
5. Panel A 绑定 ROS 实验成功；Panel B 再绑定同一个实验被数据库/界面共同拒绝；Panel A 改绑 WesternBlot 必须确认并可撤销。
6. Figure 1 下 A/B/C 各自绑定不同实验，点击某 Panel 直接显示该实验所有样本、所有阶段文件和可预览的 SHOW。
7. 发布 Revision 1 后向 ROS 实验增添 `ROS03_quant.png`，归档 Revision 1 的文件集合与 manifest hash 不变；Revision 2 在重新冻结时可包含它。
8. 已归档 v1/v2 的旧 Figure/Panel/SHOW 快照保持可查看，不能被当前 Panel 实验关系伪造回填。
9. 实验下有“其他文件”、没有 SHOW 的样本，也允许作为实验的一部分归档（只要文件内容与索引可校验）。
10. 故意删除 Blob 或修改 SHA 对应的字节，归档必须阻断并定位到文件；缺少 PROCESS 边不得阻断。
11. 旧数据迁移冲突（多个实验映射同一 Panel、同实验被多个 Panel 使用）产生有限、可操作的迁移任务，不会改写已归档 Revision。
12. Windows 1920×1080 和缩小窗口可完成导入、拖放、点击选择、图片预览与归档；无内部 ID/技术状态侵占普通视图。

## 10. 每阶段提交/交接规范

每阶段须交付：主要变动文件、SQLite schema/migration 说明、自动化测试结果、真实交互操作步骤、已知限制、下阶段依赖。禁止将 GitHub Actions 构建通过等同于真实 Windows 桌面交互验收。

每次变更必须保持：
- 原始文件只读与 SHA-256 内容寻址；
- 稳定 Asset/Experiment/Sample ID；
- 不跨实验误配；
- 旧论文与归档 Revision 的不可变性；
- 可恢复、可回滚的数据库迁移；
- 前台操作简洁，不向用户转移自动索引的维护成本。

## 11. 本轮边界与下一步

阶段性设计文档提交后，**E0、E1、E2 已完成代码实施和 Windows 自动化验收**（详见下文记录）。**E3～E5 尚未开始；新 Figure 卡片 UI、实验拖放与 Manifest v3 尚未实施。** E2 的严格 Panel↔Experiment 双向 1:1 约束已在后台数据库实现，但当前 WPF 仍使用旧交互。

下一步进入 **E3：极简 Figure 工作台及实验文件浏览 UI**。E3 必须调用 E2 的新绑定与文件浏览 API，不再让用户逐个维护样本或 SHOW 关联。旧数据的一对一迁移需先运行 E0/E1/E2 预览，并保留全部历史 D3 关联与归档 Revision。

## 12. E0 完成记录（2026-11-13）

**状态：✅ 已完成（代码实现、自动化验收、迁移方案；真实用户数据本机预览尚未执行）**

- **实现：** 新增 `native/PaperArchive.Core/ExperimentWorkspace.E0Audit.cs`，返回独立实验/样本/资产/Panel 计数、来源身份可信度、Panel→Experiment 候选映射、双向一对一冲突及未映射 SHOW、确定性工作区基线 SHA-256。
- **只读：** `ExperimentWorkspace.OpenForE0Audit(root)` 以 SQLite ReadOnly 模式读取既有 `workspace.db`，不执行数据库升级或创建新目录；独立检查 `archive.db` 归档 Revision 数量、资源数、Manifest SHA-256 与历史指纹，**不修改旧 Revision**。
- **可运行工具：** `native/PaperArchive.E0Audit/`，输出 JSON。PowerShell 使用方法及安全约束见 [E0 数据库审计与迁移方案](./2026-11-13-v16e-e0-audit-migration-design.md)。
- **迁移设计：** 已定义根目录身份登记、D2 稳定实验/样本 ID 复用、旧 Panel 多实验冲突与同实验多 Panel 冲突处理、事务备份/幂等/回滚、旧 Manifest 兼容和 E3 卡片式 UI 操作草图。
- **测试：** E0 加入 Workspace SmokeTests，覆盖只读审计重复结果一致、单实验候选、Panel 多实验、实验跨 Panel 复用、空 Panel、归档 Manifest 哈希校验、业务数据数量不变和新增资产使指纹变化。
- **CI：** Windows WPF Desktop 工作流 [run 43357330134](https://github.com/Devil-Jian/data-m/actions/runs/43357330134) **成功**：Core、旧版兼容、Workspace SmokeTests、E0 CLI、WPF 编译与便携版打包全部通过。
- **已知限制：** 本轮代码审计的是仓库数据模型与测试构造样例；无法远程读取用户 Windows 本机数据。真实旧库存在多少身份冲突/重复引用，须在本机运行审计命令获取。E0 只生成报告，**不会执行 E1/E2 数据改写**。

**E1 前置条件：** 优先以本机报告确认实验根目录身份、缺标签资产数与双向 1:1 冲突数量；开始迁移前做完整 SQLite/Blob 仓库备份并记录双库指纹。

## 13. E1 完成记录（2026-11-13）

**状态：✅ 已完成代码实现与自动化验收；真实用户 Windows 数据尚未执行迁移**

- **文件夹身份：** 新增 `e1_experiment_roots`、`e1_root_result_batches`、`e1_root_current_assets`，建立规范源根路径与稳定 Experiment ID 的对应关系；同名不同路径的实验分离。用户在旧数据中主动认领旧实验时，必须核验相对路径和 SHA-256，拒绝按名称猜测合并。
- **增量扫描：** `ImportResult` 现支持相同源根目录未变化时零 Asset 新增；仅变更文件生成新 Asset/版本；已移动的原 Asset 不误认为仍属于当前源目录。删除/恢复文件、整个文件夹暂时变空和 SHA 完全恢复时，保留历史 Asset 并尽可能复用其稳定 ID。
- **样本索引：** 原 `sample_asset_index` 的开放 PROCESS 后缀锚点收敛在同实验来源范围；D1/D2 标签与稳定样本单元保留，RAW/PROCESS/SHOW/QUANT 不需要加工先后确认。
- **旧数据迁移：** 新增 `PreviewE1LegacyMigration`、`ApplyE1LegacyMigration`、`UndoE1LegacyMigration`，支持基线指纹拒绝陈旧操作、外部 SQLite BackupDatabase 备份、事务补建缺失系统标签、受保护撤销。真实身份不明的旧结果不会自动跨文件夹合并。
- **维护入口：** `native/PaperArchive.E1Index` 支持 `projects`、`preview`、`apply`、`undo`、`folders`、`adopt`、`relink`；命令与安全注意事项见 [E1 专项开发文档](./2026-11-13-v16e-e1-folder-index-legacy-migration.md)。
- **测试：** 包含重复扫描幂等、项目/文件夹身份隔离、增量文件版本、移动后重扫、删除与恢复及空目录、旧库标签预览/备份/撤销、对错误 SHA 文件夹拒绝认领、核验成功后复用历史 Experiment/Sample/Asset ID，以及 D1–D4 全量历史兼容。
- **Windows 自动化：** [WPF Desktop run 43361900800](https://github.com/Devil-Jian/data-m/actions/runs/43361900800) **成功**，Core、旧版 SmokeTests、Workspace SmokeTests、E0/E1 CLI、WPF 编译、发布、打包全部通过。
- **明确未完成/不在 E1 范围：** E2 的 Panel↔Experiment 双向唯一关联；E3 全新论文卡片工作台；E4 简化归档检查、完整实验文件集合 Manifest v3。旧 D4 的 PROCESS/RAW 人工确认界面与归档检查直到 E4 才会被替换。未上传/连接本机数据库，不能宣称真实旧数据已自动迁移。
- **E2 交接：** 直接消费 E1 的稳定 `experiment_units.id` 与 E0 冲突预览；对可安全映射的旧 Panel 采用事务写入双向 UNIQUE 关系，其他冲突保留历史，不作自动删除。

## 14. E2 完成记录（2026-11-13）

**状态：✅ 已完成代码实现与自动化验收；真实用户本机旧数据尚未执行迁移**

- **严格 1:1：** 新增 `e2_panel_experiment_links`，使用 `panel_id PRIMARY KEY` + `experiment_id UNIQUE` 在 SQLite 层强制一个 Panel 最多一个实验、一个实验最多一个 Panel；空 Panel 与未使用实验允许存在。
- **操作 API：** `LinkPanelExperiment`、`UnlinkPanelExperiment`、`UndoPanelExperimentChange`，显式 `replace:true` 才可替换，绝不抢占其他 Panel 已绑定的实验；对同一绑定重复操作幂等。
- **自动文件检索：** `GetPanelExperiment`、`ListFigurePanelExperiments`、`ListPanelExperimentFiles`，从 Figure/Panel 的稳定 ID 直接取得实验名称、样本及 RAW/PROCESS/SHOW/QUANT 文件，不依赖加工顺序或逐张 SHOW 人工确认。
- **旧数据迁移：** `PreviewE2LegacyMigration` 读取 E0 旧 D3 关联冲突信息和 E1 文件夹身份，只对无冲突、来源已核实且未被 E2 占用的候选标记 `ready`；`ApplyE2LegacyMigration` 使用仓库外 SQLite 备份、乐观指纹与事务生成绑定；`UndoE2LegacyMigration` 在无后续变化时恢复。无法确定的旧关系仍完整保留，**不自动选一个实验或删除旧映射**。
- **维护入口：** 新增 `native/PaperArchive.E2Links` CLI，用于预览、受控应用/撤销、绑定/替换/解绑、查看关联实验及文件；具体操作见 [E2 专项开发文档](./2026-11-13-v16e-e2-panel-experiment-one-to-one.md)。
- **安全保障：** 旧 `panel_sample_links`、`figure_panel_assets`、历史 Figure 完整成图快照、Asset ID、Blob SHA-256 及 `archive.db` Revision/Manifest 全部保留。旧 D3 数据仅用作只读迁移证据。
- **回归：** 覆盖一对一重复绑定限制、显式替换与最新操作撤销、防旧操作覆盖后续关联、自动查询样本文件、旧 D3 多实验/复用冲突、过期预览拒绝、事务备份与迁移批次撤销、归档 Manifest 指纹不变。
- **Windows CI：** [WPF Desktop run 43382202862](https://github.com/Devil-Jian/data-m/actions/runs/43382202862) **成功**，Core、旧版兼容、Workspace SmokeTests、E0/E1/E2 CLI、WPF 编译及 Windows 便携版打包全部通过。
- **明确尚未实现：** E3 新 Figure 卡片工作台及拖动实验的简化 UI；E4 取消旧 PROCESS/RAW 加工关系归档提示与完整实验文件清单 Manifest v3。当前 WPF 前台仍以旧 D3/D4 方式运作。
- **E3 交接：** Figure 的 A/B/C 卡片只显示实验名称、样本数与文件名；拖入/选择一个实验调用 E2 API，点击卡片使用 `ListPanelExperimentFiles` 展示全部关联文件；不要在前台显示内部 ID。
