# S6 执行报告：重命名、软删除与回收恢复

> TASK_ID=S6 · 2026-11-16 · 当前初始记录：**开发代码与专项测试已提交，CI 验证中；人工验收待签**。

## 基线与仓库事实

- 来源：S5 `feat/s5-asset-link-copy-move` @ `6a0da0253a4af6473a3dbb634bb764adba8f80c0`（阶段用户确认；PR #49 Draft/未合并）。
- 功能分支：`feat/s6-rename-recycle`。
- [S6 Draft PR #50](https://github.com/Devil-Jian/data-m/pull/50)，base=S5 分支；S4 #47/S5 #49 仍 Draft；未合并 main。
- 方案：[S6 实施设计](S6_IMPLEMENTATION_DESIGN.md)，开发要求：[S6 任务文档](S6_DEVELOPMENT_TASK.md)。

## 已实现功能（待本次提交对应的 CI 验证）

1. Rust `asset_lifecycle({request})` 支持 `rename / soft_delete / restore`，服务端重验 `AssetLinkId + AssetId + AssetVersionId + assay + stage + group + replicate` 及关联活跃/已删除状态；单次 rename 与 1–100 条批量回收/恢复。
2. `BEGIN IMMEDIATE` 全批原子更改 `asset_links.display_name/deleted_at`；请求 ID 持久幂等，`s1_requests` 与 `audit_events` 同事务；任意失败回滚。
3. Windows 名称安全检查、同级冲突和 Unicode 大小写检查；保守拒绝组合附加符及若干易混淆控制字符；恢复不能覆盖原位置占名关联。
4. 被 Panel / representative 固定版本引用的资产不得删掉最后一条活动关联（跨同批联合检查）。**不修改文件系统、CAS Blob、AssetVersion、Panel、Replicate 或 n**。
5. `list_recycled_assets({request})` 只查询软删除项，按删除时间+LinkId 键集分页，标记恢复冲突与父级失效。
6. 正式项目管理资产浏览器：F2/右键重命名、Delete/右键逻辑删除确认、回收区列表与恢复、错误提示和写入后的重新查询；无服务时只允许明确注入的浏览器 Mock。
7. 新增真实 Rust/SQLite `s6_` 测试以及浏览器 `@S6` 交互回归；Windows workflow 明确检查非零用例、运行完整 S0–S5 回归并构建 EXE。

## 测试和验收状态

| 项目 | 状态 | 证据 |
|---|---|---|
| Rust `s6_` SQLite 实测 | CI 验证中 | 需与最终 SHA 匹配的非零通过数量 |
| `@S6` Playwright Mock | CI 验证中 | 不替代 Rust/SQLite |
| S0–S5 全回归 | CI 验证中 | 必须保留 |
| TypeScript/Vite、Rust fmt/check | CI 验证中 | Windows Actions |
| Windows Release EXE | CI 验证中 | 待最终 Artifact |
| 原生 Windows 11 WebView2 手工验收 | `manual_pending` | 不得把浏览器 Mock 当真实指针交互 |
| 正式 `accepted` / main 合并 | 否 | PR #50 Draft；无人工逐项证据 |

## Windows 人工验收清单（仅可抛弃测试数据）

- [ ] 记录 Windows 构建版本、EXE 文件 SHA-256、对应 Actions Run/Artifact ID。
- [ ] 样本级 Raw 导入至少 10 个可抛弃文件，保存导入前源文件 SHA-256、Blob 数、版本数、生物学 n。
- [ ] 真实 F2 和右键重命名中英文文件，Esc 取消、Enter 保存；刷新/重启显示名稳定，其他位置同 AssetId 的关联不变。
- [ ] 验证 Windows 禁止名 CON/NUL/LPT1、非法字符、尾随点/空格与大小写/Unicode 重名冲突，不静默覆盖。
- [ ] Delete 键/右键删除、取消确认、批量删除；回收区显示原位置/时间，恢复后原 LinkId 和 SHA 不变。
- [ ] 在回收期间占用原文件名，恢复返回 NAME_CONFLICT，既有文件不覆盖。
- [ ] 为测试 AssetVersion 添加临时 Panel 固定版本引用，最后一条活动链接不能删除；其他引用版本仍可查询。
- [ ] 按筛选、切换样本、分页后执行操作，确认没有对隐藏或旧选区误操作。
- [ ] 重启应用与 SQLite 查询确认所有结果持久，原始磁盘文件 SHA 和 Blob 数、AssetVersion、Replicate n 均未变化。
- [ ] 保存截图、执行者、日期和每项 PASS/FAIL；无证据者继续 `manual_pending`。

## 安全与已知边界

- S6 不执行任何物理删除，不实现 Blob GC、Windows 回收站、S7 备份与恢复、S8–S11 任务。
- SQLite 原有 `NOCASE` 不等于完整 Unicode case-fold；Rust 拒绝危险组合附加符并支持常见 Unicode lowercase 冲突，但对于高风险的 Unicode 兼容折叠需持续安全审计；人工验收不可跳过。
- S5/S4 上游 PR 未合 main。S7 备份恢复及 Windows 人工恢复演练签收前禁止使用珍贵或唯一的原始科研数据。
