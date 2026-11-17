# Data-M S10 — G0 / S10 机器验收矩阵

> 检查条件：**只能在 `420bcd4be18ef6b781a629f1b887c0c57611f2ee` 的 Windows Actions `https://github.com/Devil-Jian/data-m/actions/runs/44094234909` 完整为 SUCCESS、Rust/Playwright 非零且 Release EXE Artifact 真实存在后，将下方门槛判定为机器通过。** 本文只统计一次性 SQLite/CAS 和明确命名的 renderer Mock；不能替代 Windows WebView2 真实人工验收。

## G0：I1 联合回归

| ID | 代码证据 | 机器判定 |
|---|---|---|
| G0-01 | `s10_g0_01_v1_and_v2_upgrade_to_v3_preserve_pinned_references_and_reject_future`；`s10_g0_01_invalid_cross_assay_v2_migration_rolls_back_before_v3` | v1→v3 经 v2 事务、v2→v3、v3 重开；未来版本拒绝且不改字节；已有错误 Assay 关系的 v2 升级拒绝并保留 v2 schema/data。 |
| G0-02 | `s10_combined_schema_v3_backup_restore_frozen_analysis_and_panel_pins`；既有 S8 `s8_` 套件 | 同 Assay 六个 Replicate，A=5/B=6；冻结后拒绝更改纳入和版本输入/输出。 |
| G0-03 | 同上；`s9_rename_copy_links_do_not_change_version_or_n`；`s10_exact_version_forward_reverse_and_frozen_snapshot` | v1 精确身份、v2 新版本、AssetLink 复制/软删除，Panel/Analysis 历史引用不改。 |
| G0-04 | `s10_combined_schema_v3_backup_restore_frozen_analysis_and_panel_pins` | 一个 Paper、两个 Figure、每 Figure 三个 Panel，多用途和 v1/v3 版本；隔离恢复后校验数量与各 Figure 的导出边界。 |
| G0-05 | `s9_rejects_cross_project_and_duplicates`、`s9_transactional_order_and_failed_permutation_rollback`、`s9_atomic_bulk_attach_and_representative_guard`、跨 Assay 迁移拒绝测试 | 错误跨 Project/Assay、重复关联、失序或批量操作失败封闭；回滚无部分提交。 |
| G0-06 | 综合 v3 备份→验证→隔离恢复→溯源→无损 Figure 导出测试；`s8_sealed_schema_v2_snapshot_still_verifies_and_restores` | v3/v2 的 S7 备份恢复、版本 ID、Blob SHA、冻结 A/B、Panel 引用持久存在。 |
| G0-07 | `@I1 formal project workspace opens S8 and S9 without using demo data`（含 `page.reload` 再导航）；`@I1 project switch discards stale delayed Paper responses` | 同一 Project 入口的 UI 进入/离开/刷新、换项目后不出现旧 Paper 缓存；明确 renderer IPC Mock。 |

## S10：十四项最小测试矩阵

| ID | 代码证据 | 机器判定 |
|---|---|---|
| S10-01 | 综合 v3 fixture 中 Panel3 正向两个版本，`provenance_trace` 真实 Rust 查询；`s10_exact_version_forward_reverse_and_frozen_snapshot` | 精确版本、SHA、用途、两个历史 AssetLink（含软删除），不遗漏。 |
| S10-02 | 综合 fixture A=5/B=6；`analysis_samples` 冻结历史集合；Panel 和 Analysis 的 SQL 边分别标记 | 版本共用只证明共引用，不虚构 Panel→Analysis 因果 FK。 |
| S10-03 | `s10_exact_version_forward_reverse_and_frozen_snapshot`；综合逆查 9 条 v1 边 | 正反引用按精确版本；多 AssetLink 不膨胀边数；跨 Project 拒绝。 |
| S10-04 | 综合 2 Figure/6 Panel fixture（v1/v3 混合），每 Figure 独立导出 | 关闭/重新打开后 Figure 范围不越界；第二 Figure 不含第一 Figure 独有 v3。 |
| S10-05 | v1 固定引用 + 新增未被使用的 v2；独立 v3 新版本 | 原始固定 v1 不漂移到最新版本。 |
| S10-06 | S9 真实复制/重命名/软删除回归；综合 fixture 历史 AssetLink | 引用身份保持，冻结纳入集合保持 5/6。 |
| S10-07 | `s10_figure_manifest_exact_bytes_deduplicated_and_corruption_closed` | 多 Panel 逻辑引用全部保留，重复 SHA 只复制一份物理字节。 |
| S10-08 | 单 fixture v3 隔离恢复后真实 Figure 导出 + 两个独立 CAS Blob SHA/文件大小比对 | 实际导出文件 hash 等于登记 blob，第二 Figure 只含其对应 blob。 |
| S10-09 | `s10_windows_directory_reparse_ancestors_cannot_authorize_export`；Windows 导出期间替换目录的测试；`safe_dir`；SHA-only 文件名 | 拒绝重解析点/相对路径/受控仓库内部目标；Windows 目标目录句柄无 delete-share 防重命名，已存在发布包不覆盖。原始 Unicode/保留名称仅在 JSON metadata，不进入物理子路径。 |
| S10-10 | `s10_invalid_digest_missing_blob_and_symlink_are_rejected`、完整 Figure 导出损坏 Blob 失败和后续 Blob 写入位置故障注入 | 缺失、大小/哈希错误、部分写入错误均返回失败，临时目录不发布为成功包；不以模拟文件写错误冒称真实磁盘满硬件测试。 |
| S10-11 | `s10_durable_at_most_once_claim_survives_reopening_sqlite`、同 requestId replay 严格 hash、`s10_copy_cancel_midstream_preserves_original_and_never_marks_complete`、`s10_aborted_process_staging_recovery_requires_durable_claim_marker_and_age` | 重复请求不重复写，重新打开 SQLite 后仍有 claim；取消不发布；仅清理属于持久 claim 的老化暂存目录，不删未知目录。 |
| S10-12 | G0-01/06 和现有 S7/S8/I1/Rust 全量 suite | 不改 schema v3 或 S7 备份协议，旧快照仍按原格式验证恢复。 |
| S10-13 | `@S10 formal Paper Panel provenance and exact-version reverse navigation (renderer IPC mock)`、`@S10 formal asset detail reverse reference is project scoped (renderer IPC mock)` | 正式 UI 入口、反向列表、Figure 导出预览/失败不显示成功，明确是 Mock E2E。 |
| S10-14 | Windows Actions `https://github.com/Devil-Jian/data-m/actions/runs/44094234909` | `s10_` Rust / `@S10` Playwright 非零，S0–S9/I1、全量、`cargo check`、Release EXE / Artifact 成功。 |

## 无损导出 Manifest 示例（来自测试 fixture 的确定性字段）

- Paper ID：`paper`；Figure ID：`fig`；Panel 3（`panel3`）同时绑定 `version` 和 `version-v3`。
- 逻辑引用 5 条，物理 SHA 文件 2 份；A=5、B=6，跨 Paper 关联不可推断为 Panel→Analysis 的额外 FK。
- `files/714afe2b4fbcba2182ce256fc6cf0e4a4a86092c7ffd62e73bd3aed11438d302`：源测试字节长 38，SHA-256=`714afe2b4fbcba2182ce256fc6cf0e4a4a86092c7ffd62e73bd3aed11438d302`。
- `files/41e3be29a06c751878f2e3ee4538f3ae7c57a1c4629a126fa8cae8f8fc3a43fd`：新版本测试字节长 39，SHA-256=`41e3be29a06c751878f2e3ee4538f3ae7c57a1c4629a126fa8cae8f8fc3a43fd`。
- 导出路径按 SHA 命名，原始版本名如 `CON.原始.tif` 只保留在 JSON 字段中，不作为 Windows 物理文件名。
- 每条边仍持有独立 `panelId`、`assetVersionId`、`assetId`、`usage`、`relativePath`、`sha256`、`sizeBytes`、`verified`。

## 人工验收（**未执行**）

Windows 11 WebView2 原生 Figure 目录选择与进度/取消；真实 EXE 的文件拖放、全流程论文来源树；重新命名/逻辑移动后的引用显示；S7 离盘备份/隔离恢复演练；100%/150% DPI。不得将 headless CI 证明为这些人工项目已通过。

## 机器验收结论

- 代码 SHA：`420bcd4be18ef6b781a629f1b887c0c57611f2ee`。
- Windows CI：`https://github.com/Devil-Jian/data-m/actions/runs/44094234909`，结论 `SUCCESS（桌面 1 条历史 Demo UI 测试首次失败、自动重试通过，记为 flaky）`。
- 原生 S10：`11/11 通过`；Rust 全量：`121/121 通过`；桌面 E2E：`共 45 项：44 一次通过、1 flaky 重试通过`；Chromium E2E：`29/29 通过`。
- Windows unsigned EXE Artifact：`https://github.com/Devil-Jian/data-m/actions/runs/44094234909/artifacts/14333783685`；下载 ZIP SHA-256：`2b2689150b6c3a088cf0144c92651b8352dba71c832afd4129e2f0936e3f6a22`（非内部 EXE hash）。
- 阶段状态：`machine_passed / manual_pending（不等于 accepted）`；人工验收：`manual_pending`。无人工签收不得标为 accepted，不合并 main。