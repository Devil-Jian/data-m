# PaperArchive V1.6-E2 — Panel ↔ Experiment 双向一对一索引及安全迁移

> 日期：2026-11-13
>
> 阶段：E2 已实施，Windows 自动化验收见末尾；E3 新 UI、E4 新归档另行开发。
>
> 开发分支：`feat/v16-task-driven-workspace`
>
> 上游依赖：[E0 数据库审计](./2026-11-13-v16e-e0-audit-migration-design.md)、[E1 文件夹与样本索引](./2026-11-13-v16e-e1-folder-index-legacy-migration.md)

## 一、目标与固定业务约束

Figure 1 中的 A、B、C 分别是一个 Panel，每个 Panel **最多关联一个独立实验**；同一个实验 **最多关联一个 Panel**。可存在尚未关联的 Panel 或未被使用的实验。

```text
论文 → Figure 1 → A ── 一对一 ── 01_ROS检测 → ROS01/ROS02 → RAW/PROCESS/SHOW/QUANT
                  B ── 一对一 ── 02_WesternBlot → WB01/WB02 → 对应全部文件
                  C ── 一对一 ── 03_细胞活性 → CV01/CV02 → 对应全部文件
```

用户只决定**哪个实验关联哪个子图**。不选择 RAW/PROCESS/SHOW 的加工顺序，也不需要单独建立每张展示图片的来源链。内部数据库保存唯一 ID，普通前台不显示 ID。

## 二、E2 新增 SQLite 数据结构

代码：`native/PaperArchive.Core/ExperimentWorkspace.E2PanelExperiments.cs`。

```sql
CREATE TABLE e2_panel_experiment_links(
    panel_id TEXT PRIMARY KEY REFERENCES panel_templates(id) ON DELETE CASCADE,
    experiment_id TEXT NOT NULL UNIQUE REFERENCES experiment_units(id),
    linked_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
CREATE UNIQUE INDEX ux_e2_panel_experiment_id
    ON e2_panel_experiment_links(experiment_id);
```

- `panel_id PRIMARY KEY`：防止一个子图绑定多个实验；
- `experiment_id UNIQUE`：防止同一实验同时出现在多个子图；
- 数据库外键保证 Panel 和实验存在；
- 修改操作在 SQLite 事务中执行，不拆分成不一致的多步骤修改；
- 新表**不修改**旧 `panel_sample_links`、`figure_panel_assets`、`figure_composite_source_snapshots`、`composite_sample_source_snapshots`；
- 新增 `e2_panel_experiment_changes` 保存绑定/替换/解绑记录，`e2_migration_sessions` 保存批次迁移前后指纹、备份位置及撤销状态。

所有已归档论文 Revision 的 Manifest、资产、哈希及旧版本来源快照均保持不变。

## 三、E2 核心 API

| API | 行为 | 安全规则 |
| --- | --- | --- |
| `LinkPanelExperiment(panelId, experimentId)` | 将一个实验绑定到空 Panel | 已绑定不同实验时拒绝，已被其他 Panel 使用的实验拒绝；重复绑定同一对是无操作 |
| `LinkPanelExperiment(..., replace:true)` | 明确替换当前 Panel 的实验 | 只更新当前 Panel 关系；禁止抢占其他 Panel 正在使用的实验 |
| `UnlinkPanelExperiment(panelId)` | 解除 Panel—实验关系 | 不删除实验、样本、文件或旧归档；空绑定重复解绑无操作 |
| `UndoPanelExperimentChange(changeId)` | 撤销最近有效的某次变更 | 若该 Panel 有任何后续关联操作，或先前实验被别人占用，拒绝覆盖 |
| `GetPanelExperiment(panelId)` | 读取关联的实验名称和 Figure A/B/C 位置 | 无绑定返回 null |
| `ListFigurePanelExperiments(figureId)` | 列出 Figure 下已绑定实验的子图 | 依据稳定 ID 读取 |
| `ListPanelExperimentFiles(panelId)` | 直接列出所关联实验的样本文件及其他文件 | RAW/PROCESS/SHOW/QUANT 自动可达；按 Asset ID 去重，不推断加工链 |

API 和数据表在 E2 交付，新的可拖放卡片工作台属于 E3。

## 四、旧 D3 数据：只读预览 → 备份 → 受控应用

**旧系统的 Panel 可以有多个 Sample/SHOW，因此不能直接添加 UNIQUE 并自动删掉多余行。**

`PreviewE2LegacyMigration()` 使用 E0 的旧关联候选与 E1 已核验的物理文件夹身份，按以下优先级决定：

| 决策值 | 处理 |
| --- | --- |
| `ready` | 恰好一个实验、没有未映射 SHOW、该实验未被其他旧 Panel 复用、E1 根目录身份已登记，且当前 E2 Panel/实验无占用；可迁移 |
| `already_linked` | 这对 Panel—实验关系已存在；无需重复写入 |
| `root_unverified` | 旧实验根目录尚无 E1 身份证据；先用 E1 核验/认领 |
| `multiple_experiments` | 同一旧 Panel 指向多个实验；保留旧数据，等待用户选择 |
| `experiment_reused` | 同一实验出现在多个旧 Panel；不能自动裁决归属 |
| `unresolved_assets` | 有旧 SHOW 文件无法确定稳定实验身份；补齐 E1 索引 |
| `panel_occupied` / `experiment_occupied` | 新一对一关系已被其他绑定占用，不覆盖 |
| `empty` | 空子图，不建立虚构实验关联 |

`ApplyE2LegacyMigration(fingerprint, externalBackupDirectory)`：
1. 使用 SQLite BackupDatabase 在**仓库外**备份 `workspace.db`；
2. 重新读取并核对预览指纹，且在提交事务中再次校验；
3. 只迁移 `ready`，其他冲突保持不变；
4. 每条自动关联写入 E2 操作日志，整批写入迁移会话；
5. 旧 D3/D4 关联、文件实体和归档不变。

`UndoE2LegacyMigration(sessionId)`：
- 若迁移后的索引或旧业务数据已发生变更，不尝试覆盖后续工作；报错并提示使用 SQLite 备份人工受控恢复；
- 若没有后续操作，整批解绑 E2 新建关系、记录会话撤销状态；
- 不删除旧 D3 关联。

## 五、E2 运维工具与本地命令

新增 `native/PaperArchive.E2Links/`（.NET 10 命令行工具）。建议先退出桌面软件，并完整备份整个仓库（数据库、Blob 原始副本）。

```powershell
# 拉取分支
git switch feat/v16-task-driven-workspace
git pull --ff-only origin feat/v16-task-driven-workspace

# E0/E1 已升级数据库上，查看可迁移的旧子图关联（只读）
dotnet run --project native/PaperArchive.E2Links -- preview "D:\PaperArchive-Data"

# 明确确认预览结果后，将 Fingerprint 从预览 JSON 中复制进下一命令
dotnet run --project native/PaperArchive.E2Links -- apply "D:\PaperArchive-Data" "<Fingerprint>" "E:\PaperArchive-E2-Backup"

# 仅在没有后续修改时可撤销该批迁移
dotnet run --project native/PaperArchive.E2Links -- undo-migration "D:\PaperArchive-Data" "<SessionId>"

# 手动关联 / 显式替换 / 解绑（内部 Panel ID、Experiment ID 仅维护工具使用）
dotnet run --project native/PaperArchive.E2Links -- link "D:\PaperArchive-Data" "<panelId>" "<experimentId>"
dotnet run --project native/PaperArchive.E2Links -- replace "D:\PaperArchive-Data" "<panelId>" "<experimentId>"
dotnet run --project native/PaperArchive.E2Links -- unlink "D:\PaperArchive-Data" "<panelId>"

# 撤销本次操作
dotnet run --project native/PaperArchive.E2Links -- undo "D:\PaperArchive-Data" "<ChangeId>"

# 查看一个子图的实验及自动索引文件
dotnet run --project native/PaperArchive.E2Links -- show "D:\PaperArchive-Data" "<panelId>"
dotnet run --project native/PaperArchive.E2Links -- files "D:\PaperArchive-Data" "<panelId>"
```

**注意：** 预览 CLI 不会自动升级旧数据库。对尚无 E2 表的老仓库，先执行整体外部备份，再在受控条件下使用新版客户端初始化结构；否则预览会安全报错。普通界面无需接触 ID，E3 将替代上述维护命令。

## 六、回归与安全验收

新增 Workspace SmokeTests 覆盖：

- 同一 Panel 重复绑定同实验幂等；
- 同一 Experiment 绑定第二个 Panel 被拒绝；
- 已绑定 Panel 未明确 replace 时拒绝覆盖；
- 显式 replace/解除成功，最新变更可撤销；
- 更早的操作不能在 Panel 有后续操作时越权撤销；
- 从 Figure Panel 查询实验及 SHOW 等文件，无需人工 SHOW 加工链；
- E0 旧 D3 多实验和同实验多 Panel 冲突不被自动选择；
- `ready` 且 E1 根目录核验的旧 Panel 才可受控批量迁移；
- 错误指纹阻止应用；应用前有独立 SQLite 备份；
- 迁移会话可安全撤销且二次撤销无操作；
- 历史 D3 Panel Sample 关系、Asset ID、旧 Revision Manifest 哈希不变；
- 与 E0、E1 和原 D1～D4 测试同时运行。

Windows Workflow：`WPF Desktop (.NET 10)` 编译 Core、旧 Core、Workspace SmokeTests、E0/E1/E2 维护工具、WPF 和 Windows 便携版。验证成功的实际 run URL 在主阶段文档标记。

## 七、E2 边界与 E3 交接

**E2 是后台数据层，不修改当前老 WPF 操作界面。** 当前旧 D3 来源展示和 D4 的 PROCESS/RAW 归档提醒仍存在；E3 负责替换界面，E4 负责取消旧归档确认逻辑并生成完整实验集合 Manifest v3。

E3 应直接调用 `GetPanelExperiment`、`ListFigurePanelExperiments`、`ListPanelExperimentFiles` 与 `LinkPanelExperiment`，以名称/图片预览展示。不需要在普通界面呈现数据库 ID 或要求逐个关联 SHOW。

E3 UI 只能允许用户从实验资料库拖入一个实验到一个 Panel，并对替换已有绑定要求简短确认。不存在静默抢占其他 Panel 中的实验。E3 不能根据遗留 D3 多对多关系自行发明一个唯一的实验关联。
