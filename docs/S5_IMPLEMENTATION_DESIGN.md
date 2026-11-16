# S5 资产关联复制与逻辑移动：实施设计（2026-11-16）

基线：`feat/s4-real-asset-browser` @ `789bac6cc9757023aa58b67bc986bb2a65227235`，PR #47 Draft；本阶段 `feat/s5-asset-link-copy-move`，不合并 main。严格沿用 S0–S11 路线。

## 代码审计结论

- 正式 UI 是 `apps/desktop` 的项目管理 → 实验分组样本 → 阶段页；演示 Workbench 不参与真实写入。
- S4 按当前查询条件展示 `AssetLinkId / AssetId / AssetVersionId`，但只显示当前页并以最新版本作视图。页面切换与筛选变更可能使选区无效；必须携带源上下文并在事务中逐项重验。
- S3 受控 SQLite v2 已具备 `asset_links` 唯一活动名称索引、`s1_requests` 请求账本、`audit_events` 审计表；无需为 S5 执行高风险 schema 迁移。
- Raw/Processed 只可定位到同实验的活跃 Group+Replicate；Statistics/Display 为实验级容器，不接受分组/样本 ID。
- CAS Blob 与 AssetVersion 不可变；S5 不需要也不允许文件系统写入或 Replicate INSERT。

## IPC 协议

`asset_transfer({request})`：
```ts
{
  action: "copy" | "move",
  requestId: string,
  items: Array<{
    assetLinkId: string, assetId: string, assetVersionId: string,
    assayId: string, stage: "raw"|"processed"|"statistics"|"display",
    groupId?: string|null, replicateId?: string|null
  }>,
  target: {
    assayId: string, stage: "raw"|"processed"|"statistics"|"display",
    groupId?: string|null, replicateId?: string|null
  }
}
```
- 请求数 1–100，拒绝重复 Link ID 与非法 ID；所有 ID 均 opaque，不接受路径或任意 SQL。
- 每条源都必须为活动关联，所属项目/主题/实验及样本活跃，传入 `AssetId` 和 `AssetVersionId` 必须等于查询时真实且仍为当前的版本；源作用域必须精确匹配。
- 目标必须为合法活动作用域；组级不是 S3 当前支持的文件落点。所有选项最终由 Rust/SQLite 校验，React 提示不构成授权。
- 响应包括本次 `requestId`、`action`、`links[{sourceLinkId,resultLinkId,assetId,assetVersionId}]`。复制返回新 Link ID；移动返回原 Link ID。结构化错误码：`INVALID_REQUEST`、`INVALID_TARGET`、`STALE_SELECTION`、`NAME_CONFLICT`、`NOT_FOUND`、`REQUEST_CONFLICT`、`DATABASE_UNAVAILABLE`、`UNKNOWN_DATABASE`。

## 事务、幂等与审计

1. `BEGIN IMMEDIATE`，先检查 `s1_requests` 的 `requestId`；同 ID 同规范化载荷从同事务审计结果重放；同 ID 不同载荷一律 `REQUEST_CONFLICT`。
2. 校验目标、全部源 ID / 当前版本 / 活动层级 / 作用域，再检查整个批次中重复文件名、目标已存在的同名文件（沿用 SQLite NOCASE 唯一索引）；先验证后写入。
3. `copy` 只 INSERT 新 `asset_links`；`move` 只 UPDATE 选定的 `asset_links` 的四个位置字段；不修改 `data_assets`、`asset_versions`、`blobs`、`replicates`。
4. 在**同一个** SQLite 事务写入 `audit_events`（含请求 ID、固定版本、逐项结果、目标）与 `s1_requests`，最后 COMMIT。任一失败、崩溃或冲突则完整回滚。跨进程写入经 SQLite IMMEDIATE 与现有连接序列化保护。
5. 不做物理移动/跨库文件复制。S5 仅保存资产链接，不改变 CAS 内容。S9 的论文固定版本功能不提前实现。

## 前端交互

- 源选区始终来源于 **当前页** 并转成包含源作用域的不可变快照；筛选/翻页和上下文切换立即失效。跨页不会累积隐式选择。
- 剪贴板只保留明确复制或剪切时的源快照（可跨阶段、跨实验页使用）；改变当前列表并不会悄悄改写剪贴板来源；后台在提交时重新验证。
- Ctrl+C / Ctrl+X / Ctrl+V、右键复制/剪切/粘贴；Ctrl+拖拽复制、Shift+拖拽移动；不带修饰键的拖拽不能提交。落点需为合法活动样本或实验阶段，并展示目标提示、错误和成功回执。
- 后端回复成功后重新查询列表，失败时保留选择及剪贴板。用户重试同一未确认请求可复用请求 ID。浏览器仅能使用显式注入的测试 Mock；真实客户端必须经 Tauri 命令提交。

## 测试 / 放行门槛

Rust 原生临时文件 SQLite：10 文件跨组复制、同一 Asset 跨样本移动、冲突及批次原子回滚、非法目标、旧版/错链上下文拒绝、请求幂等、hash/Blob/Replicate n 不变、重开数据库持久化、审计。Playwright `@S5` 验证界面事件，仅标记浏览器 Mock，不代替真实存储测试。CI 包含 S0–S5 非零专项、完整 Rust/前端回归、Windows Release EXE。
人工验收仍为 `manual_pending`；S7 备份恢复和人工签收前禁止珍贵/唯一真实科研数据用于测试。
