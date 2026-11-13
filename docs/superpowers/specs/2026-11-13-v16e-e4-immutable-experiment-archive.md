# PaperArchive V1.6-E4：轻量归档与完整实验文件索引快照

> 日期：2026-11-13  
> 状态：E4 代码实现与回归测试已提交；构建结论以主阶段文档的 CI 结果为准。  
> 开发分支：`feat/v16-task-driven-workspace`  
> 依赖：E1 稳定实验/样本/文件索引、E2 Panel↔Experiment 双向一对一、E3 Figure 卡片工作台。

## 1. 本阶段解决什么问题

旧 D4 归档以每张 SHOW 的人工用途确认和显式 PROCESS→RAW 加工边为核心，因此产生多项“来源待核对”，且不能完整覆盖实验文件夹中的 QUANT、实验级说明文件等。

**E4 将新的归档依据改为：**

```text
论文全文
  └─ Figure N（归档时当前完整成图版本）
      └─ Panel A / B / C
          └─ 唯一 Experiment ID
              ├─ Sample ID → RAW / PROCESS / SHOW / QUANT ...
              └─ 实验级其他文件
```

Panel 指向**整个实验文件夹**，不是某一张 SHOW，也不要求把 RAW/PROCESS 文件按人工确认的顺序串起来。所有关系通过稳定 ID 查询并冻结，前台只显示论文名、Figure、A/B/C、实验名、样本名和文件名。

## 2. 实现文件与职责

| 文件 | 核心职责 |
|---|---|
| `native/PaperArchive.Core/ExperimentWorkspace.E4ArchiveIndex.cs` | `ListE4PanelAssets(panelId)`：读取 Panel 唯一实验在 E1 文件夹索引中的**当前**全部 Asset，关联 D2 稳定样本 ID；没有可信 E1 根目录的旧实验保守使用现有样本成员 |
| `native/PaperArchive.Core/ArchiveRepository.E4.cs` | `PrepareWorkspaceArchiveE4`、`PublishWorkspaceArchiveE4`、`ArchiveWorkspacePaperE4`：生成 schemaVersion 3 Manifest，校验实际内容，事务归档，阻止过时确认 |
| `native/PaperArchive.Wpf/E3WorkspaceWindow.cs` | 新工作台增加导入全文及一键归档按钮，缺失项按位置简要提示，不显示 PROCESS/RAW 人工加工关系 |
| `native/PaperArchive.Wpf/MainWindow.Archive.cs` | 已迁移到 E2 Panel—实验关联的论文，从旧主界面归档也自动进入 E4 简化流程；只有旧 D3-only 论文继续使用原 v2 路径，防止静默改变旧历史语义 |
| `native/PaperArchive.Wpf/MainWindow.ArchiveInline.cs` | 新归档库树形查看 schema 3：论文全文 → Figure → A/B/C 实验 → 样本 → 按阶段的文件名；schema 1/2 仍按原旧快照读取 |
| `native/PaperArchive.Workspace.SmokeTests/Program.cs` | 全实验归档、历史不可变、文件版本变化与 Blob 丢失回归 |

## 3. 归档预检规则

需要满足：
1. 论文全文存在于当前论文项目；
2. 至少一个 Figure，且每个 Figure 都有当前的完整成图图片；
3. 每个 Figure 有实际 Panel，且每个 Panel 恰好关联一个实验（E2 在数据库层阻止重复）；
4. Panel 所关联实验至少存在一份可归档 Asset；
5. 每个入选文稿、完整 Figure 和实验文件对应的内容寻址 Blob 必须存在，字节 SHA-256 和大小与索引一致；
6. 归档前再生成一次 v3 预览，检查 Manifest SHA-256 与用户确认时的快照完全一致；不一致则要求重新确认。

**不再检查、也不弹出：**
- SHOW 是否显式引用 PROCESS；
- PROCESS 是否显式引用 RAW；
- PROCESS/RAW 是否经人工确认；
- 文件是否存在四个阶段的全部成员；
- 多张 SHOW 的具体加工先后顺序。

没有 SHOW、RAW、PROCESS 或 QUANT 其中任一阶段的实验，只要实验下确有文件、所有内容校验通过，均可真实归档其现有文件集合。无法识别样本归属的文件可在实验级“其他文件”保存，而不是强行编造 Sample ID。

## 4. Manifest v3 数据结构和反向索引

Manifest 明确记录：
- `schemaVersion: 3`、Paper ID、文稿 ID/文件名/SHA；
- 各 Figure ID、编号、当前完整成图 ID、版本、文件名、SHA；
- 各 Panel ID/字母，唯一 Experiment ID/实验名；
- Panel 下各 Sample ID/样本名，RAW/PROCESS/SHOW/QUANT Asset ID、文件名、阶段、相对路径、大小、SHA；
- 未能归到样本的实验附件同样进入 Panel 的资产列表；
- 独立归档资源表保留所有文稿、Figure、Panel 文件的角色和路径，可从 Asset ID 反查使用该文件的论文 Revision。

**每个 Panel 的 Asset 列表先根据当前源文件夹索引取值，再按 Asset ID 去重。** 重复导入时相同文件不能产生重复归档引用；不同 SHA 的版本保留稳定 Asset ID，但 v3 当前快照只包含当时实际生效的版本。旧版本仍留在旧 Revision，不受替换影响。

归档写入已有的 `workspace_archive_revisions` 和 `workspace_archive_resources`，由 SQLite 事务保证同一 Revision 内 Manifest/资源一致。Manifest 本身的 SHA-256 用作唯一去重键；再次归档未变化的版本返回原 Revision 而不产生重复记录。

本阶段**没有编辑历史 schema 1/2 的任何 JSON**。版本 3 是新版本的格式选择，不是对旧版本进行升级或推断旧关联。

## 5. 前台流程

```text
打开「论文工作台」
  → 创建论文、Figure、A/B/C
  → 导入实验文件夹并拖给相应 Panel
  → 导入每张完整 Figure
  → 导入论文全文
  → 点击「归档论文」
  → 正常时只显示 Figure 数、文件数、预计归档摘要
  → 确认并生成 Revision
```

有问题时只显示实际错误：
- `Figure 1-A 尚未关联实验`；
- `Figure 2 尚未导入完整 Figure 图片`；
- `ROS01_show.png 的原始内容丢失`。

没有“待核对加工关系”列表，也无要求勾选的复杂来源确认框。

归档库选择历史 Revision 时，新 v3 通过冻结 Manifest 构建 Figure → Panel → 实验 → 样本 → 文件名树。所有信息来自历史 JSON，不读取当前 E2 绑定来补充旧 Revision。

## 6. 自动化验收

已添加专门的 V1.6-E4 Workspace SmokeTests 场景：

1. 一个独立实验的 RAW、PROCESS、SHOW、QUANT 和实验说明文件在 Panel A 下完整出现；
2. 未手动建立任何 PROCESS→RAW、SHOW→PROCESS 边仍可通过归档，无 `review`；
3. Manifest 是 schemaVersion 3，包含稳定 Experiment ID，全部字节内容经过校验；
4. 发布一次生成 Revision 1，内容不变重复发布是幂等的；
5. 修改同源 SHOW 文件只新增该文件的 Asset 版本，归档 Revision 2 只包含当前版本，不包含已被取代的 SHOW Asset；
6. Revision 1 的 Manifest JSON 和 SHA 保持不变；
7. 使用旧预览在数据变化后提交，被 E4 拒绝；
8. 刻意暂时移走现用 Blob，预检识别 `missing_blob` 并阻止归档；
9. 既有旧版 schema 2 Revision 的 Manifest 与内容保持不变；
10. 原有 D1–D4、E0–E3 测试并行运行，兼容原 WPF/便携版构建。

## 7. 已知边界与 E5 交接

- E4 的 v3 路径仅对具有 E2 Panel→Experiment 关系的论文开放；纯旧 D3 论文走兼容 v2 流程。在 E2 旧关系未获确认前不会凭文件名自动选取唯一实验归档。
- v3 记录的是当前**工作区索引和已保存的内容寻址 Blob**，不是外部实验文件夹的即时修改。外部原始目录新增/变更文件后，需先通过导入/增量扫描刷新 E1 索引，再归档新 Revision。
- 现有新 Figure 工作台仍作为可独立启动窗口，E5 再统一“论文工作台 / 实验资料库 / 论文档案库”三大导航、搜索和高 DPI 性能。
- Windows CI 只能验证代码编译、数据回归及便携版打包；真实桌面鼠标拖放、图片打开和归档按钮流程还需要本地 Windows 人工验收。
- 后续若要增强多进程同时修改工作区的原子性，可将 E4 创建 Manifest 的索引读取升级为一致 SQLite 只读事务；目前双预检哈希、发布前 Blob 校验和归档数据库事务是主要防护。

**下一步：E5 最终交互收尾、三主入口导航、全局检索、性能和真实 Windows 验收。**
