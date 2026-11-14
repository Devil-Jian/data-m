# Data-M 领域实体草案

> 此文档是设计基线，具体数据库字段与 migration 由 Issue #14 实现后确定。

```
Project 1─* Theme 1─* Assay 1─* Group 1─* Replicate
                               |
                               └─* AssetAssociation ─* DataAsset 1─* AssetVersion ─1 Blob
Paper 1─* Figure 1─* Panel 1─* PanelAssetLink ─1 AssetVersion
Assay 1─* StatisticalAnalysis 1─* AnalysisSample ─1 Replicate
Panel 1─* RepresentativeSource ─1 (Replicate/AssetVersion)
```

## 核心不变量

1. **文件与样本分离**：复制 DataAsset 或 AssetAssociation 不能新增 Replicate，也不能改变生物学样本 n。
2. **数据阶段**：Raw / Processed / Statistics / Display 是资产角色或组织关系；同一内容可能被多个逻辑上下文引用。
3. **统计纳入**：`AnalysisSample.included` 针对某次具体统计分析与某个 Replicate；不得仅由文件上的 `inStats` 字段全局控制。
4. **代表图源**：`RepresentativeSource` 针对具体 Panel 和某个版本；不能简单设为 Replicate 全局属性。
5. **论文引用**：通过 `AssetVersionId` / 稳定源 ID 链接，逻辑移动或重命名不应失效。
6. **不可变版本**：Raw 文件入库后保存原始字节，不允许覆盖修改；新版本产生新 AssetVersion。
7. **删除保护**：有活跃引用的版本不得物理删除；先执行软删除和引用分析。

## 待详细设计

- 一份统计文件可能来源于多个 Assay/Group；分析输入是 Replicate 还是具体 AssetVersion；
- 变更统计分析样本集合时的审计与冻结规则；
- 数据阶段是 DataAsset 属性还是与 Container 的多对多关系；
- CAS 中 Blob GC 的安全回收策略；
- 数据库升级兼容旧分支 SQLite 存储格式的迁移路径。
