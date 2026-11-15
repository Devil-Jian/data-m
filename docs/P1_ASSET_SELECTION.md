# P1 文件资产级选择模型（Issue #13 增量）

## 已实现（演示数据级别）

- 每个生物学重复 `ReplicateId` 映射不同的 Raw/Processed 演示文件 `AssetId`；两者身份不再混用。
- `asset:raw:<repId>` 和 `asset:processed:<repId>` 在全景矩阵、列表/表格与分阶段文件卡片中保持一致。
- 统计和展示文件由 Assay 统一管理，演示 `replicateId=null`，不会按样本行错误复制一份。
- 行级勾选是“勾选本行可见文件资产”，不表示选择整个生物学重复。
- 拖拽回调只携带文件资产 ID；Assay 级统计/展示资产禁止直接拖放到样本分组。
- 逻辑复制/移动仅产生提示，任何操作都不改变生物学重复数量。
- `demo-assets.test.ts` 检查身份区分、跨视图 ID 一致和样本数量不变量。

## 仍需实现

- 这些演示 ID 不是数据库正式 AssetId；P2 Rust/SQLite 将定义持久 ID 与不可变 AssetVersion。
- 矩形框选边缘自动滚动、虚拟列表、Windows WebView2 E2E 测试仍需后续增量。
- 顶部实验树与当前的逻辑分组是演示容器，不具备真实文件仓库。
- 真实导入、移动、复制、删除全部不可用；原始科研文件未被修改。

## 验收检查

1. 在全阶段全景的 Raw 与 Processed 列，对同一个 Replicate 的两个文件分别执行 Ctrl 多选。
2. 从矩阵切到 List/Table，选中的文件保持可见高亮。
3. 切到单阶段 Raw，文件 ID 与原全景 Raw 项相同；Processed 不属于本视图。
4. Stats/Display 阶段选中的文件没有生物学重复 ID。
5. 同时选择多个文件并拖动到其他分组，只应提交文件资产操作意图，不改变 n 或任何真实磁盘数据。
6. 运行 `npm run test:assets`、`npm run test:interaction`、`npm run build:all` 和 `npm run build:desktop`。

> Windows 实际鼠标和键盘验收尚未完成，此 PR 保持 Draft，Issue #13 保持 Open。
