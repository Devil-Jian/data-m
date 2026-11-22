# S6 实施设计冻结：资产关联重命名、软删除与回收恢复

- 日期：2026-11-16；TASK_ID=S6；从 S5 最新 HEAD `6a0da0253a4af6473a3dbb634bb764adba8f80c0` 派生 `feat/s6-rename-recycle`。
- S5 PR #49 / S4 PR #47 均 Draft，未合并 main。只针对受控测试工作区。S7 备份/恢复签收前禁止珍贵真实数据。
- 原数据模型有 `asset_links.deleted_at` 与只针对活跃关联的 `asset_links_active_name` 唯一索引；无需添加数据库迁移。S4 正常列表仅显示活跃链接；回收区必须单独查询。

## 冻结身份及事务

类型化 IPC：`asset_lifecycle`，请求 `{action:'rename'|'soft_delete'|'restore',requestId,item?,items?,newName?}`；所有作用域携带 `assetLinkId / assetId / assetVersionId / assayId / stage / groupId / replicateId`，与 S5 对齐。重命名单个 item；删除/恢复 1–100 个 items。恢复要求被删状态，其余要求活动状态。数据库中核验链接、当前版本及活跃父层级。任何误配/无效父级拒绝。

`BEGIN IMMEDIATE` → 全批身份及名称/引用冲突预验证 → UPDATE 仅指定 `asset_links.display_name/deleted_at` → audit_events + s1_requests 同事务登记 → COMMIT；请求 ID 同载荷幂等返回首次结果，异载荷冲突。数据库写失败全回滚。重命名仅改变当前关联显示名称，不改变 data_assets 名称。

## 引用与冲突

Panel 的 `panel_asset_versions` / `representative_sources` 锁定 AssetVersionId，不能更改。对被上述表引用的资产，若一次批量删除会使全部活动 AssetLink 归零，拒绝 `REFERENCE_PROTECTED`，防止唯一导航关联消失。其他关联保持可读，任何动作不读写 Blob 字节、不新增 Replicate、不执行文件系统 rename/unlink/GC。

恢复只回原容器；原父级已软删除拒绝 INVALID_TARGET。恢复和重命名均检查活动同级名称；SQLite NOCASE 只识别 ASCII，Rust 在事务中使用 Unicode lowercase 加保守拒绝组合附加符、方向控制、易混淆格式字符，识别同级 Unicode 大小写冲突。不宣称覆盖 Unicode 的所有语言学 case-fold 特例。恢复同名返回 NAME_CONFLICT，不能覆盖或自动改名。

## 前端及验收

正式项目管理资产列表增加 F2/右键 rename、Delete/右键软删除确认和回收区；不同页/搜索上下文不得保留选区，确认与恢复反馈从真实 IPC 结果再查询。回收查询只显示已删链接，支持阶段与样本过滤及键集分页。普通浏览器 Mock 专供 Playwright，不能取代 Rust/SQLite。新增 s6_ 原生测试、@S6 UI 测试及完整 S0–S5 回归，Windows CI/EXE；WebView2 手动验收继续 manual_pending。