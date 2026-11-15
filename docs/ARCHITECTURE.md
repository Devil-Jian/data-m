# Data-M 架构基线（React + Tauri 路线）

## 约束

Windows 11 单机、离线、文件体量可能很大；必须保护原始文件、支持论文引用长期稳定。所选 UI 为 React + shadcn/ui，保留原 Gemini 布局。

## 系统边界

```
React UI (Presentation)
  └─ Interaction Engine (pointer/keyboard → FileOperationIntent)
     └─ Type-safe Tauri IPC client
        └─ Rust Commands / Application Use Cases
           ├─ Domain (entities + invariants)
           ├─ Repository interfaces
           └─ Infrastructure (SQLite + CAS + Windows native APIs)
```

- **Presentation**：界面布局、文件列表、模态对话框、进度和错误提示。
- **Interaction**：选择/框选/拖放/快捷键，不决定科研规则和物理存储。
- **Application**：MoveAssets, ImportAssets, LinkPanel, ExportFigure 等独立用例；事务协调。
- **Domain**：资产身份、统计样本资格、原始文件不可变、版本和引用完整性。
- **Infrastructure**：SQLite migrations/repositories、SHA-256/CAS、任务队列、Windows shell 集成。

**依赖规则**：Domain 不依赖 React/Tauri/SQLite；Application 依赖 Domain 和抽象接口；Infrastructure 实现抽象接口。Tauri Commands 是薄适配层，禁止把业务规则塞进命令入口。

## 关键命名

- `ReplicateId`：真实生物学重复标识。
- `AssetId`：逻辑文件资产标识。
- `AssetVersionId`：不可变物理版本标识。
- `BlobKey`：CAS 内容哈希键。
- `ContainerId`：逻辑文件夹/实验单元归属，不等同于磁盘绝对路径。
- `FileOperationIntent`：前端请求，包括 assetIds、targetContainerId、operation。

## 技术决策

- 选用 React/shadcn/Tauri/Rust/SQLite/CAS：见 ADR-001。
- 内部拖放采用 Pointer Events 与可测试的交互状态机；外部 Windows Explorer 拖入交给 Tauri 原生事件。
- SQLite 负责元数据事务；文件写入需要 staging、校验、持久化与恢复协议，避免数据库/磁盘不一致。
- CAS Blob 不允许因逻辑复制而直接修改已有内容。

## 现存实现及未完成项

- 交互试验已有 `packages/interaction`，React Workbench 中对演示 Replicate 进行框选和拖拽意图提示。
- 真正的 DataAsset ID、Rust Commands、真实文件移动、Tauri 桌面 EXE 尚未在所选分支完成。
- 仓库存在 Tauri/Vue、WinUI 3、WPF 等历史或并行 Draft PR；**未验证等价性、不自动合并**，优先确保选定路线的架构一致性。
