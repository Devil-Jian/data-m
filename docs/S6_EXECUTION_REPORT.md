# S6 执行报告：重命名、软删除与回收恢复

> TASK_ID=S6 · 2026-11-16 · 阶段结果：**machine_passed / manual_pending**。机器 CI 与 Windows Release EXE 已成功；Windows 11 WebView2 人工验收待签；PR 仍 Draft、未合并 main，正式 `accepted` 不成立。

## 1. 基线、提交、PR 和交付物

- 仓库：`Devil-Jian/data-m`；S6 分支：`feat/s6-rename-recycle`。
- 起始 S5 基线 SHA：`6a0da0253a4af6473a3dbb634bb764adba8f80c0`。
- [S6 Draft PR #50](https://github.com/Devil-Jian/data-m/pull/50)，base 为 `feat/s5-asset-link-copy-move`，上游 S5 [Draft #49](https://github.com/Devil-Jian/data-m/pull/49) 依赖 S4 [Draft #47](https://github.com/Devil-Jian/data-m/pull/47)，均未合并 main。禁止未经明确授权合并。
- **最终已验证的代码 SHA**：`f591786df0d0d5a3049547cfc75612855bd16b0c`。后续仅更新文档状态，**不声称文档 HEAD 经此 CI 重新测试**。
- **Windows Actions**：[Windows desktop shell #43895539113](https://github.com/Devil-Jian/data-m/actions/runs/43895539113)，对上述代码 SHA 的 `status=completed`、`conclusion=success`；CI 原始日志已核实。
- **Windows 便携 EXE**：[data-m-s6-unsigned-portable-windows · Artifact #14240920142](https://github.com/Devil-Jian/data-m/actions/runs/43895539113/artifacts/14240920142)，ZIP 大小约 4.05 MB，含 `data-m-desktop.exe`，未签名、不是安装器；Artifact 默认保留期 14 天。**EXE 实际 SHA-256 尚须人工下载核算**，不可凭空写一个值。
- 设计：[S6_IMPLEMENTATION_DESIGN.md](S6_IMPLEMENTATION_DESIGN.md)；执行目标：[S6_DEVELOPMENT_TASK.md](S6_DEVELOPMENT_TASK.md)。

## 2. 实际开发交付

1. Rust/Tauri `asset_lifecycle({request})` 实现 `rename`、`soft_delete`、`restore`，单次重命名或 1–100 条批量关联；每条都以服务端核验的 `AssetLinkId + AssetId + 当前 AssetVersionId + 实验/阶段/分组/样本作用域` 为准，拒绝陈旧上下文。
2. SQLite `BEGIN IMMEDIATE` 事务仅改变 `asset_links.display_name` 和 `asset_links.deleted_at`；`s1_requests` 持久请求 ID 幂等、`audit_events` 同事务记录操作前关联身份/名称/删除状态和提交后的响应；失败全批回滚。
3. Windows 文件名校验（控制符、非法路径字符、设备保留名和尾随句点/空格），Unicode lowercase 同级名称冲突；为避免未经规范化的旧数据覆盖，保守拒绝含组合字符的危险同级冲突。**这不是 Unicode 全量 normalization/case-fold 支持**，见风险说明。
4. 受 `panel_asset_versions` 或 `representative_sources` 固定版本引用保护的资产，禁止在单次批量删除中失去所有活跃链接（`REFERENCE_PROTECTED`）。存在其他活动关联时可回收当前关联，但固定版本和来源保持不变。
5. `list_recycled_assets({request})` 独立回收查询，已删除项单独列表、作用域过滤、删除时间+LinkId 键集分页、恢复可行性与阻塞原因；恢复写入时再次检查活跃父级与同名冲突。
6. 正式 React 「项目管理 → 实验 → 样本/阶段 → 已入库文件」接入 F2/右键重命名、Delete/右键软删除、删除确认、回收列表、批量选择/恢复、可解释错误和成功后真实重查。普通文件列表不显示已回收关联。
7. 完全未触碰物理原始源文件、CAS Blob、AssetVersion、DataAsset、Replicate 数量/生物学 n、Panel 固定版本，也未实现物理垃圾回收或 Windows 回收站集成。
8. CI 只增加 S6 所需严格 IPC 白名单与非零专项检测；完整 S0–S5 回归和 Windows EXE 构建保留。

## 3. 已证实的 Windows CI 测试

| 检查 | 实际证据 | 结果 |
|---|---|---|
| Rust `cargo fmt --all -- --check` | Windows #43895539113 | PASS |
| 类型检查 + Vite / S0 baseline / 前端单元 | Windows #43895539113 | PASS |
| S6 原生 `cargo test --locked --no-default-features s6_` | **11 passed / 0 failed**，先检查非零发现 | PASS |
| S6 浏览器 `npm run test:e2e:desktop -- --grep "@S6"` | **4 passed / 0 failed**，显式浏览器 Mock | PASS（仅 UI） |
| 完整 `cargo test --locked --no-default-features` | **80 passed / 0 failed** | PASS |
| 桌面 Playwright 全量 | **33 passed** | PASS |
| S0、S1、S2、S3、S4、S5 原生回归 | 分别 4、14、7、17、2、8 passed | PASS |
| `cargo check --locked` | Windows #43895539113 | PASS |
| Windows Release `npm run tauri:build` | Windows #43895539113 | PASS |
| EXE Artifact 上传 | #14240920142，已核验存在 | PASS |
| Windows 11 真机 WebView2 使用、EXE/源哈希、重启等逐项人工证据 | 尚未提供 | `manual_pending` |

测试边界：`@S6` 是明确注入的浏览器 Mock，只验证正式 UI 事件与呈现；实际事务通过 `s6_` 的原生 Rust/SQLite 临时数据库测试证明。没有用 Mock 假装真实 Windows GUI。

## 4. Windows 人工验收清单（只可抛弃数据）

1. [ ] 下载上述 Artifact 解压 `data-m-desktop.exe`，记录 Windows 版本、执行者与日期。PowerShell 执行 `Get-FileHash .\data-m-desktop.exe -Algorithm SHA256` 并保存结果。
2. [ ] 新建独立测试 Project/Theme/Assay/Group/Replicate，在 Raw 导入至少 10 份可抛弃测试文件；记录导入源每个文件的 `Get-FileHash -Algorithm SHA256`、文件数与 n。
3. [ ] F2、Enter、Esc、右键重命名中英文名称；新名只改变当前关联的显示名，其他同 AssetId 的关联、固定版本、SHA 不变；重启后保持。
4. [ ] 尝试 `CON.txt`、`NUL`、`LPT1.csv`、非法字符、尾随空格/句点、ASCII/Unicode 大小写冲突，观察拒绝与错误提示，不允许覆盖。
5. [ ] Delete、右键移至回收区、取消确认与批量确认；普通列表消失、回收区含原名/时间/作用域；恢复后 LinkId/版本/哈希未变。
6. [ ] 回收后另一活动关联占原名，恢复返回 `NAME_CONFLICT` 且不覆盖。父节点被软删时恢复不得重新挂到其他样本。
7. [ ] 临时建立引用了 AssetVersion 的 Panel fixture，最后一条活动关联必须受到 `REFERENCE_PROTECTED` 保护；有其他活动关联时回收不能破坏论文版本引用。
8. [ ] 翻页、过滤、切换样本后，不能用之前的选区错误删除/重命名隐藏关联。
9. [ ] 刷新及退出重启后再次对比原文件 SHA、SQLite Blob/AssetVersion/Replicate 数与论文引用；回收动作不是物理删除。
10. [ ] 保存截图/录屏、实际 PASS/FAIL、时间、执行者及 EXE SHA。**未实际做的项不能写 PASS**。

## 5. 结论与残余风险

- **S6 机器开发交付：`machine_passed`，原生人工：`manual_pending`；正式 `accepted` 待人工逐项证据 + 上游及本阶段经授权集成。**
- 当前所用 SQLite `NOCASE` 主要处理 ASCII；Rust 实施 Unicode lowercase 和保守拒绝组合字符，但不宣称完整 Unicode normalization/case-fold。对于高风险兼容折叠/跨文件系统大小写规则，继续通过受控测试验证。
- 测试证据只证明指定代码提交和用例；不能代表珍贵数据级完整性/备份恢复，也不能证明真实 WebView2 的所有鼠标/键盘边缘场景。
- **S7 完整性检查、备份恢复与人工恢复演练正式签收前，禁止以珍贵或唯一的真实科研原始数据测试或正式入库。**
- 不推进 S7–S11，不自动合并 main。
