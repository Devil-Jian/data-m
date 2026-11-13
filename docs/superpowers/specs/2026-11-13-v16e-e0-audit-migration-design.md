# PaperArchive V1.6-E0 — 数据库基线审计、身份判定与安全迁移方案

> 执行日期：2026-11-13
>
> 状态：**E0 已实现只读审计与迁移设计，E1/E2 迁移尚未执行**。
>
> 相关主规格：[`2026-11-13-v16e-folder-first-figure-workspace-roadmap.md`](./2026-11-13-v16e-folder-first-figure-workspace-roadmap.md)

## A. 现有代码/SQLite 结构审计

| 当前对象 | SQLite 表与代码 | 关键现状 | E 阶段处置 |
|---|---|---|---|
| Paper / Figure / Panel | `paper_projects`、`figure_templates`、`panel_templates` / `ExperimentWorkspace.V13.cs` | 论文下 Figure 有编号；Figure 下 Panel 有 A/B/C；无 Experiment 绑定列 | **复用 ID 和结构**，E2 新增单独双向 UNIQUE 绑定表 |
| 项目 / 批次 | `experiment_projects`、`experiment_results` | 同一个实验可多次导入；Result 是批次，不是实验本身 | 保留，E1 增量扫描与源目录身份需区分 |
| D1 系统标签 | `experiment_result_labels`、`asset_system_tags` | 记录实验显示名、样本、阶段、来源类型；部分历史资产可能缺标签 | E1 只读预览后受控补建，不改写已归档来源 |
| D2 实验与样本 | `experiment_units`、`sample_units`、`sample_unit_assets` | 实验唯一键是 project ID + 大小写不敏感名称，**尚无原始文件夹规范身份** | 保留现有稳定 ID；E1 新增根目录身份证据，解决同名实验歧义 |
| D3 关联 | `panel_sample_links`、`figure_panel_assets` | Panel 可关联多个 Sample/SHOW；同一个 Experiment 可重复出现在多个 Panel | E0 出具候选；E2 添加 Panel↔Experiment 1:1 双 UNIQUE，不能自动剥夺旧关系 |
| Figure 快照 | `figure_composites`、`figure_composite_source_snapshots`、`composite_sample_source_snapshots` | 记录完整成图版本与旧 SHOW/样本证据 | 保留、只读旧快照；E4 新增完整实验文件集快照 |
| 论文版本 | `paper_document_figure_versions` | 论文文件关联被确认的 Figure 完整成图版本 | 复用版本绑定 |
| 归档数据库 | `archive.db` 的 `workspace_archive_revisions` 与 `workspace_archive_resources` | Manifest 版本 1/2 只保存旧来源形态 | E0 单独只读摘要；E4 新增 v3，不编辑 v1/v2 |

**审计确定的风险（来自代码结构，而不是已读取用户电脑的真实数据）：**

1. **文件夹身份缺失。** D2 的 `experiment_units` 只以“所在项目 + 实验名称”唯一，不存规范化物理根路径。两次不同物理文件夹、同名称导入，可能误合并到一个实验。E0 不能猜测哪个是真实根目录。
2. **旧 Panel 关联非 1:1。** 一个 Panel 可拥有多个样本/SHOW；这些可能同一 Experiment，也可能来自多个 Experiment。同一 Experiment 也可能被多个 Panel 复用。直接迁移或加 UNIQUE 约束会失败或丢失信息。
3. **旧版本证据与当前关系不同。** 必须保证当前 Experiment 解绑/重新关联不会修改已有 Figure 成图和 Revision 快照。
4. **旧系统标签仍有缺口。** `RefreshSystemTags` 只给当次没有标签的 Asset 插入标签，不能仅靠刷新样本索引确保所有旧资产完成 D1 系统标签迁移。
5. **原有归档检查冗余。** V1.6-D4 当前仍检查 PROCESS/RAW 上游边和 SHOW 确认；E4 要改成检查文件完整性和 Panel↔Experiment 唯一结构，不得把旧逻辑直接复用为新归档规则。

## B. E0 交付：只读数据库审计及基线工具

新增源码：
- `native/PaperArchive.Core/ExperimentWorkspace.E0Audit.cs`
- `native/PaperArchive.Core/ExperimentWorkspace.OpenForE0Audit`
- `native/PaperArchive.E0Audit/` 独立命令行项目

`PreviewE0Migration()` 返回：
- 当前项目、批次、实验、样本、资产、Panel、旧关联、成图及来源快照数量；
- 已建立和未建立系统标签的有效 Asset 数量；
- 每个 Experiment 的项目、显示名称、结果批次数、root_folder 标签批次数、legacy_fallback 批次数、已索引文件数、身份证据等级；
- 每个 Panel 的 **候选 Experiment ID 列表**、旧样本/SHOW 数量、无样本索引的 SHOW 数量、迁移决策；
- 旧结构不能自动转换为 1:1 时的明确问题；
- **workspace.db 独立基线指纹**：确定性 SHA-256 覆盖关键表所有行和关联；
- **archive.db 独立历史基线**：归档 Revision 数、资源数、Manifest 摘要；检测 Manifest JSON 的 SHA-256 与存储值是否一致。

仅对现有数据库执行 SELECT，不建新表、不重建系统标签、不写业务数据、不改归档；`OpenForE0Audit` 使用 SQLite ReadOnly 模式，**不执行 `ExperimentWorkspace.Open` 的自动初始化/升级**。

> 边界：工作区与归档是两份独立数据库。两者分别使用只读事务取稳定快照；它们并非跨数据库的单个原子事务。因此正式迁移前应停止应用程序并做一致性备份。旧到无法识别 E0 依赖表结构的数据库，工具安全报错而不是自行迁移。

### B1. 在本地运行真实数据 E0 基线报告

Windows PowerShell，在仓库根目录运行：

```powershell
dotnet run --project native/PaperArchive.E0Audit/PaperArchive.E0Audit.csproj -- "D:\PaperArchive-Data" | Out-File -FilePath ".\e0-baseline.json" -Encoding utf8
```

**注意：** 替换为自己使用的实际仓库目录（包含 `database/workspace.db`）；报告 `e0-baseline.json` 存在开发代码目录，而非数据仓库。请先关闭正在使用数据的桌面程序。报告内的 Experiment/Panel/Asset ID 仅用于内部审计，不进入普通 UI。

真实数据库尚未由本轮远程代码审计读取，**不能宣称用户实际项目共有多少实验、多少迁移冲突**。这些数量仅能在用户本机执行上述工具后获得。

### B2. Panel 迁移决策分类

| 结果 | 含义 | 后续动作 |
|---|---|---|
| `ready` | Panel 有一个唯一的 Experiment，且没有无索引 SHOW；该实验未用于其他 Panel | E2 可在用户确认迁移预览后写入 1:1 关系 |
| `empty` | Panel 尚无足够的旧关联证据 | 保持空，等待用户拖入实验 |
| `multiple_experiments` | 同一 Panel 关联两个及以上实验 | E1/E2 冲突列表，**不自动选取** |
| `experiment_reused` | 同一 Experiment 被两个及以上 Panel 引用 | 所有关联面板转为待处理，**不静默抢占** |
| `unresolved_assets` | 当前 Panel 的某些 SHOW 不可匹配到稳定样本/实验 | E1 补建标签/样本索引后再重新审计 |
| `experiment_identity_uncertain` | 某 Experiment 的根目录来源证据不足（无、多个结果批次、legacy 等） | E1 核对文件夹身份；这属于身份风险，不允许自动合并不同文件夹 |

注意 `ready` 只表示**旧关联结构逻辑上符合 1:1**；如该 Experiment 根目录身份证据仍不可靠，E1 必须先补齐才能自动应用，不得以 `ready` 跳过身份判定。多项冲突可同时出现在一个 Panel，审计问题列表分别保留原因。

## C. E1 数据库迁移设计（尚未执行）

### C1. 文件夹身份登记

新增 `experiment_folder_sources`（候选草案）：

```sql
CREATE TABLE experiment_folder_sources(
    experiment_id TEXT PRIMARY KEY REFERENCES experiment_units(id),
    source_root_normalized TEXT,
    source_fingerprint TEXT,
    source_kind TEXT NOT NULL,
    confidence TEXT NOT NULL,
    recorded_at TEXT NOT NULL
);
```

- `source_root_normalized` 用于相同目录的幂等更新，Windows 需统一大小写/斜杠和符号链接规则；相对路径、移动历史可单独表记录。
- `source_fingerprint` 是归属证据（如导入文件相对路径及哈希集合），**不能单独作为实验唯一身份**；用户复制文件夹时即使内容相同也可能是两个实验。
- 旧数据只有结果名称和标签、不存在原始绝对路径时，`confidence` 标记 `unknown`，不凭名称或哈希猜测合并。
- 多批次重复导入可能产生不同 Asset ID；E1 只做逻辑去重和稳定 Experiment/样本归属，不删除原数据。
- E1 的演进先建可空/兼容表，再填充确认的字段；不得对所有旧 Experiment 直接设 `NOT NULL root`。

### C2. 正式迁移事务与回滚

1. 关闭应用，使用 SQLite 在线备份接口（或 SQLite backup/校验后的复制）分别备份 `workspace.db` 和 `archive.db`，保留 WAL 一致性；记录原始资产与 Manifest 指纹。
2. 读取 E0 预览，筛选确切来源身份、可归属样本以及可映射 Panel。输出要转换的计数和冲突明细。
3. E1 事务中补建文件夹来源/实验资产归属与缺失标签；只对明确身份做自动映射，无法确定的留为“其他文件/待归组”。验证数据库外键与导入前后 Asset ID/哈希一致。
4. E2 创建双向唯一关联表；仅写入未冲突且根目录身份可信的映射。数据冲突不自动删旧链接、不自动断开别的 Panel；用户确认后再按单个 Panel 原子处理。
5. 每批迁移可重入，并保留操作日志和撤销映射；失败时整个事务回滚。备份的完整恢复途径必须独立于 UI。
6. E4 才写入新的归档 Manifest schema v3；旧 Manifest v1/v2 永不更新。冻结时包含真实文件 ID、哈希及实验/样本成员，不以当前磁盘原始目录状态代替归档历史。

**关键安全条件：** 迁移期间不得重写旧 Asset ID、SHA-256、文件内容、已归档历史或旧 Figure 版本；必须保留已删除/已恢复对象的历史兼容路径。

## D. E3 UI 线框与最短任务路径

```text
┌─ 论文工作台 ─ 实验资料库 ─ 论文档案库 ────────── [搜索] ─┐
│  论文：模拟论文                           [归档]          │
│  Figure 1  Figure 2  Figure 3                             │
│  ┌─────────────────────┬───────────────────────────┐        │
│  │ A                   │ B                         │        │
│  │ 01_ROS检测          │ 02_WesternBlot            │        │
│  │ 3 个样本 · 12 文件  │ 2 个样本 · 9 文件         │        │
│  └─────────────────────┴───────────────────────────┘        │
│  ┌─────────────────────┐      [ + 添加子图 ]                │
│  │ C · + 关联实验      │                                   │
│  └─────────────────────┘                                    │
│  [选择 A] → 右侧抽屉：ROS01 / ROS02 / ROS03 → 文件名、预览  │
└──────────────────────────────────────────────────────────────┘
```

交互红线：
- 所有关系的创建由“拖入实验”或“+ 关联实验”完成；不弹 PROCESS/RAW 关系确认。
- 实验已经绑定其他 Panel 时阻止第二次绑定并可跳转，旧实验替换才询问确认。
- 查看文件仅需 Figure → Panel → 样本，内部 ID、阶段数据库说明默认隐藏。
- 归档仅校验真实索引/字节/哈希，一旦出错提供精准跳转；不重复显示同一文件错误。

## E. E0 自动化验收与进入 E1 的门槛

已添加至 Workspace SmokeTests 的测试场景：

- 同一 Panel 指向一个 Experiment → `ready`；
- 空 Panel → `empty`；
- 同一 Panel 同时带两个 Experiment → `panel_multiple_experiments`；
- 一个 Experiment 被多个 Panel 复用 → `experiment_reused`；
- 审计前后旧 Panel、Asset 条数不变；
- 两次只读审计返回相同工作区指纹、归档基线；
- 归档 Manifest 自身 SHA-256 校验通过；
- 真实内容变更（新增导入）导致工作区指纹变化。

E0 出口条件：
1. 代码和回归自动化通过 Windows CI；
2. 本地审计 CLI 能以只读模式生成 JSON；
3. 迁移冲突分类与 E1/E2 设计固定；
4. 不依赖实际用户数据即可测试各冲突场景，但**真实旧数据是否有冲突仍待本机预览**；
5. 不启动 E1/E2 数据更改，也不提前宣布新 UI/Manifest v3 已实现。

## F. E0→E1 交接清单

- 首先读取本机 E0 JSON，查 `Issues` 中的 `panel_multiple_experiments`、`experiment_reused`、`experiment_identity_uncertain`、`legacy_untagged_assets`。
- 优先完善实验源文件夹身份登记（导入时保存根目录证据、旧数据受控匹配），再处理 Panel 的一对一迁移。
- E1 需增加只读预览、受控补建、事务恢复、幂等性及跨实验同名样本防误配测试。
- E0 仅实现只读审计与迁移计划；数据修复、实际映射写入、全新 Figure 工作台和新版归档功能分别属于 E1～E4。
