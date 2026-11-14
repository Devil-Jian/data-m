# Tauri IPC 契约草案

> 此文档约束前后端模块边界；接口尚未正式实现。P1 桌面壳只有 `shell_status` 示例命令。

## 统一约定

- 所有文件对象使用稳定 `AssetId` 和 `AssetVersionId`，不允许让 React 直接提交任意 SQL。
- 文件操作命令使用 JSON 请求及明确的结构化错误码，不返回内部完整异常堆栈。
- 批量操作支持 `requestId` 幂等标识，便于失败重试和审计。
- `OperationIntent` 是请求，不等于成功；只有 Rust 用例提交完成后 UI 才更新持久数据。
- 进度应通过具名 Tauri Event 推送，前端取消监听须清理。
- 外部文件路径只允许从用户明确选择/拖入事件取得，并在 Rust 层进行规范化及安全检查。

## 预计命令（非现有 API）

| 命令 | 请求 | 返回 | 归属 |
| --- | --- | --- | --- |
| `list_assets` | containerId、过滤、分页 | assets、nextCursor | P2 |
| `import_assets` | sourcePaths、containerId、requestId | taskId | P2 |
| `move_assets` | assetIds、targetContainerId、requestId | committed IDs | P2 |
| `copy_assets` | assetIds、targetContainerId、requestId | new association IDs | P2 |
| `rename_asset` | assetId、newDisplayName | updated asset | P2 |
| `delete_assets` | assetIds、requestId | deleted IDs | P2 |
| `verify_assets` | assetIds / scope | verification report | P2 |
| `list_experiments` | projectId | hierarchy | P3 |
| `create_analysis` | assayId、replicateIds、inputs | analysis ID | P3 |
| `link_panel_assets` | panelId、assetVersionIds、roles | links | P3 |
| `trace_panel` | panelId | provenance tree | P3 |

## 错误模型

```json
{
  "code": "ASSET_IN_USE",
  "message": "文件版本正在被论文 Panel 引用",
  "requestId": "uuid",
  "details": { "assetId": "..." }
}
```

建议错误码：`INVALID_TARGET`, `ASSET_NOT_FOUND`, `ASSET_IN_USE`, `NAME_CONFLICT`, `IO_ERROR`, `DISK_FULL`, `OPERATION_CANCELLED`, `VERSION_CONFLICT`。

## 前端调用模式

```typescript
// Design sketch only; method not yet registered in Rust.
const result = await invoke('move_assets', {
  request: {
    assetIds: selectedAssetIds,
    targetContainerId,
    requestId
  }
});
```

在真实 Rust 用例完成前，UI 必须以“待实现”或“请求意图”显示，不能虚构成功。
