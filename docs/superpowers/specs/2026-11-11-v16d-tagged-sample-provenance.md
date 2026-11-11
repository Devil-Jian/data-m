# PaperArchive V1.6-D — 基于文件名、样本单元与 Panel 的标签式溯源

> 状态：设计基线已确认；按 D1 → D2 → D3 → D4 逐阶段开发。2026-11-11。
> 研发分支：`feat/v16-task-driven-workspace`；实现时保留既有 PaperArchive 数据与归档兼容性。

## 1. 目标与边界

将日常操作由“逐图片维护 RAW/PROCESS/SHOW 来源边、逐张选择用途”转为：
**导入实验根目录 → 自动解析实验/样本/阶段 → 显示样本实验单元 → 把样本拖到 Panel → 归档前校验 SHOW 用途。**

数据分类/同样本关系**不等于**真实加工顺序。即使 ROS01 拥有 RAW、若干 PROCESS、SHOW 和 QUANT，也不得凭名字断言 `crop → adjusted → show`；显式的 `asset_provenance_edges` 才能提供加工关系证据。

范围是工作区自动标签、稳定样本单元、自定义标签/备注、Panel/SHOW 选择、归档校验及论文版本索引。已归档 Revision、原始内容寻址 blob 和稳定 Asset ID 不重写。

## 2. 对象与身份

| 对象 | 唯一身份 | 用途 |
|---|---|---|
| 实验单元 Experiment Unit | 系统生成 ID；导入根文件夹名为展示名称 | 保存根目录名、导入来源；跨结果重名不可凭名称自动跨实验合并 |
| 样本单元 Sample Unit | (Experiment Unit ID, 规范化样本名) 的稳定 ID | 不依赖文件名或旧 `experiment_groups` ID |
| 文件 Asset | 现有 `experiment_assets.id` / SHA-256 | 永久的文件引用及字节快照 |
| 系统标签快照 | (Asset ID, parser rule version) | 实验、样本、阶段、PROCESS 子标签、文件名版本、解析状态及证据 |
| 自定义标签 | 标签 ID | 多标签分类；改名不影响系统标签 |
| 备注 | 实体类型+实体 ID | 不承载关系或归档身份 |
| Panel ↔ Sample | 稳定 Panel ID + Sample Unit ID | 样本用于 Panel；可多对多 |
| Panel ↔ SHOW | 现有 `figure_panel_assets` 与选定具体 SHOW Asset ID | 显示图片的精确引用 |
| 论文 ↔ 成图版本 | 现有文稿 ID ↔ Figure Composite ID | 归档使用某个已确认版本，不随工作区变化 |

**旧 `experiment_groups` 按 Result 独立，原 ID 不变。** 新 Sample Unit 是覆盖其上的聚合层；独立实验之间出现同名 ROS01 不跨实验合并。未经明确确认，不能由相同样本名跨 Experiment Unit 合并。

## 3. 命名解析规则 v1

示例导入根目录 `ROS实验/`：

| 文件名 | 实验标签 | 样本 | 阶段 | 子标签 | 文件版本 |
|---|---|---|---|---|---|
| ROS01_raw.tif | ROS实验 | ROS01 | RAW | — | 1 |
| ROS01_crop.tif | ROS实验 | ROS01 | PROCESS | crop | 1 |
| ROS01_adjusted_v2.tif | ROS实验 | ROS01 | PROCESS | adjusted | 2 |
| ROS01_show.png | ROS实验 | ROS01 | SHOW | — | 1 |
| ROS01_quant.tif | ROS实验 | ROS01 | QUANT | — | 1 |
| ROS010_raw.tif | ROS实验 | ROS010 | RAW | — | 1 |

1. 实验名**取用户选择的实验根文件夹名称**，不是 `raw_data`、`show_data` 或当前 UI 位置；对旧导入数据可设 `legacy_fallback` 标记。
2. 先提取文件名末尾独立的 `_vN` 版本标记，再对 `_raw`、`_show`、`_quant`、`_process` 识别固定阶段（大小写不敏感，允许 `-`）。
3. RAW、SHOW、QUANT 是同一 Sample Unit 的**逻辑槽位**。允许同阶段多个不可变文件版本，但“当前选用版本”须唯一且显式。
4. PROCESS 可以具有任意描述后缀；先以 RAW/SHOW/QUANT 的样本键为锚点，对其他文件执行**最长且具有分隔符边界**的匹配；剩余部分为 PROCESS 子标签。无锚点或长度并列冲突记为 `unresolved`，不擅自归组。ROS01 不得匹配 ROS010。
5. 若文件夹阶段与文件名末尾明确固定阶段相冲突：标记 `conflict`，不静默选一个；统计文件（CSV/XLSX 等）不当作 QUANT 图片；旧版本按迁移计划处理。
6. 同样本判断只建立成员关系；PROCESS 多个文件可对应一个 SHOW；没有明确证据不得自动生成真实处理序列。
7. 自定义标签不参与样本名解析。重命名和重新解析前展示差异；历史版本不可改写。

## 4. 日常交互

### 实验单元主页

默认显示**样本卡片/列表**而非散落的文件行：

`ROS实验 | ROS01 | RAW 1 | PROCESS 3 | SHOW 1 | QUANT 1 | 用途 Fig 1-A | 完整`

点击样本显示四阶段文件分组，系统标签、用户标签、备注和真实加工证据分开显示。少量未识别文件集中放“待识别/冲突”，允许批量处理，不能自动假定已确认。

### Panel 匹配

拖动 **Sample Unit** 到 Figure 的 Panel：创建 `panel_sample_links`；一个样本只有一个有效 SHOW 时，自动选择并关联实际 SHOW Asset；多个 SHOW 时选择明确版本；没有 SHOW 时提示缺口，不生成虚假文件。一个 Panel 可以关联多个样本。重复拖入幂等，不移动 blob。

生成的用途来自结构化 `paper_id/figure_id/panel_id/asset_id`，展示成 `Figure 1-A`。Figure 重编号、Panel 重命名仅改变显示文本，不破坏引用。工作区用途与最终 composite source snapshot 分开。

### 论文与归档

文稿中的 `Figure 1` 先产生候选索引；**确认文稿 ↔ 具体 Figure Composite 版本**后才能作为已采纳图。归档 Manifest 保存具体 composite、Panel、SHOW Asset、样本信息及已确认/候选关系，之后不可变。归档后按归档 Revision 展示，不能从当前 Panel 读取历史。

## 5. 归档强制校验语义

“SHOW 用途不能为空”适用于**本次纳入论文归档/成图来源的 SHOW**；未使用的备选 SHOW 可以留在工作区，不应强行指定虚假用途。归档阻断项：

- 无法解析实验/样本、解析冲突且涉及本次选用 SHOW；
- 用途缺少具体 Panel ID / Panel 不属于当前 Figure / Paper；
- 本次确认的成图版本缺 SHOW 或已选 SHOW 与快照不一致；
- 引用丢失 Asset、内容哈希变化、被删除的 Panel / Figure；
- 文稿仅有 Figure 号码未绑定已确认完整成图版本。

PROCESS 加工顺序未确认时只标记“无真实处理链证据”，除非产品单独要求加工关系完整性；不可自行伪造加工历史。已经归档的旧 Revision 不溯及修改。

## 6. 数据库迁移原则

- 先增表和索引；**不修改**现有 `experiment_assets`、`experiment_groups` 的主键、SHA 与字节，也不删除 `sample_asset_index`、`figure_panel_assets` 和 provenance edges。
- D1 自动标签表挂 Asset ID，需记录规则版本、标签来源 `parsed`/`legacy_fallback`、`resolved`/`unresolved`/`conflict` 状态；新导入在同一事务内写入，旧数据按只读预览和显式升级流程迁移。
- D2 稳定 Sample Unit/用户标签/备注不混入系统标签；D3 的 Panel ↔ Sample 是新关联，SHOW 仍用精确 Asset；D4 Manifest 扩展版本号且兼容旧 schema。
- 所有迁移必须有幂等、回滚/恢复与范围隔离测试；不对已归档 Revision 做补写。

## 7. 分步实施和验收

### D1：自动标签和导入解析（第一实施批次）

- [x] 定义文件名解析器 v1：固定 `raw/show/quant`、PROCESS 子标签与版本号。
- [x] 导入时记录实验根文件夹名；新增系统标签表与查询 API。
- [x] 支持最长边界的 PROCESS 归组；无法明确判断保留为待识别。
- [ ] 新增解析冲突检查、数据库迁移与跨导入一致性回归。（已实现冲突标注和基础兼容测试；旧数据的预览迁移仍待开发。）
- [x] WPF 详情栏显示系统标签、解析状态；不更改历史归档。

### D2：样本实验单元与自定义标签

- [ ] 稳定 Sample Unit 和实验 ID，支持跨同实验不同结果汇总。
- [ ] 自定义标签/批量赋值/备注（不改变阶段系统标签）。
- [ ] 以样本为中心的列表、阶段分组缩略图、待识别处理界面。
- [ ] 保证旧 group/Asset ID 和撤销行为兼容。

### D3：样本 → Panel

- [ ] Panel 样本多对多；一张有效 SHOW 免确认、多版本显式选择。
- [ ] 拖放/撤销、用途实时计算、源文件只读、跨论文限制。
- [ ] 完整成图版本保存精确 Panel/SHOW 来源快照。

### D4：归档阻断与论文版本链

- [ ] 预检本次采用的 SHOW 用途必须精确 Panel，不可空。
- [ ] 文稿 Figure 引用 ↔ 确认的成图版本 ↔ Panel ↔ SHOW ↔ Sample Unit。
- [ ] Revision 只读展示和历史迁移兼容。
- [ ] 在真实 Windows 上完成 GUI 验收、千级/万级素材性能及迁移回归。

## 8. 不变量

1. 同名样本/文件仅提供聚合候选，不构成“真实加工历史已确认”的证据。
2. QUANT 不能替代 SHOW 作为论文 Panel 展示素材。
3. 单个 Asset 的字节、SHA 和身份永不因更改标签而变更。
4. 历史 composite 来源快照和 archive Revision 不跟随工作区当前版本变化。
5. 自动解析失败时保留源文件与待识别记录；不静默丢弃。
6. D1 不提前启用 D4 的新阻断规则，必须随 UX 与数据迁移同步上线。

## 9. 下一阶段依赖

D2 的样本身份将基于 D1 的 `experiment` + `sample_key` 与解析来源，D3 不读取任意文件名拼凑 Panel 身份，D4 仅使用完整结构化关联。因此 D1 必须先定义稳定、可测试的解析语义及数据留存策略。

## 10. D1 第一批实现记录（2026-11-11）

- 新增 `ExperimentWorkspace.SystemTags.cs`：纯函数命名解析、版本号、原始实验目录标签、PROCESS 子标签、解析状态及 `asset_system_tags` 查询。
- `ImportResult` 将根文件夹名保存到 `experiment_result_labels`；`RefreshSampleIndex` 的同一个 SQLite 事务同步派生系统标签。保持原 Asset、group 与归档 Revision 不变。
- 支持不使用 `raw_data/show_data` 子目录的根目录平铺文件；只有样本边界明确时才将未知图片归为 PROCESS。
- WPF 素材详细信息显示自动系统标签与识别状态；旧数据未建立标签时明确标注“尚未建立”。
- **暂未交付**：稳定的实验/样本实体、跨实验导入合并规则、自定义标签、备注、Panel 样本拖放、归档强制用途检查；这些属于 D2–D4。
- **遗留 D1**：旧数据批量标签迁移的可预览/可撤销流程，歧义和冲突的集中处理 UI，以及更全面的命名规则覆盖。
