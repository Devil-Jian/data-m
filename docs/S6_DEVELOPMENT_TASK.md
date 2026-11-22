# Data-M S6 开发任务：重命名、软删除与回收恢复

> TASK_ID=S6 · 2026-11-16 · **开发指令（尚未开始 S6 代码实现）**
>
> 依据：`docs/AI_DEV_EXECUTION_SPEC.md` S6、`docs/DATA_M_AI_TASKS.json` S6 和 S0–S11 路线。S5 已由用户确认阶段开发完成，正式 `accepted` 仍受 PR 合并及完整人工证据限制。

## 一、开发基线与边界

- 仓库 `Devil-Jian/data-m`；上游开发分支 `feat/s5-asset-link-copy-move`，S5 最后一次完整 CI 验证 SHA `0c35e4465c604eace5bffff1a52f465d9e498f91`。
- [S5 Draft PR #49](https://github.com/Devil-Jian/data-m/pull/49) 的 base 是尚未合并的 [S4 Draft PR #47](https://github.com/Devil-Jian/data-m/pull/47)。进入 S6 时读取 S5 **最新 HEAD**，记录 SHA，再建立 `feat/s6-rename-recycle`；S6 Draft PR 以 S5 分支为 base。**不得从 main 开发，不得擅自合并 main**。
- 已知机器证据：[Windows CI #43847745981](https://github.com/Devil-Jian/data-m/actions/runs/43847745981) 成功，S5 Rust 8/8，Playwright 3/3，完整 Rust 69/69，桌面 E2E 29/29，Windows Release EXE 成功。
- 必读：`docs/AI_DEV_EXECUTION_SPEC.md`、`docs/DATA_M_AI_TASKS.json`、`docs/DEVELOPMENT_MASTER_PLAN.md`、`docs/S5_EXECUTION_REPORT.md`、`docs/S5_IMPLEMENTATION_DESIGN.md`、`docs/S3_IMPORT_PROTOCOL.md` 和数据库 Schema/正式 UI 源码。
- 范围仅 S6，不做 S7 完整性/备份恢复、S8 统计、S9 论文、S10 溯源、S11 发布。**S7 备份恢复人工签收前禁止珍贵/唯一的真实科研数据用于开发测试**。

## 二、用户功能与正式 UI

在真实 `apps/desktop` 「项目管理 → 实验 → 样本/阶段 → 已入库文件」中实现：

1. **F2 / 右键重命名**：单条选中，弹层预填当前显示名，Enter 确认、Esc 取消；只改变**当前 AssetLinkId** 的 `display_name`，同 `AssetId` 在其他容器的显示名不变。提示扩展名改变仅影响逻辑名称，不转换原文件。
2. **Delete / 右键移到回收区**：单条或当前页多条（上限 100），提供明确数量、实验/样本/阶段和“逻辑删除可恢复、不删磁盘文件”的确认；默认取消。失败时绝不乐观隐藏。
3. **回收区**：独立列表，显示已删除关联原名、原作用域、时间、引用保护/恢复可用性，按实验/阶段/样本检索及分页；不与正常资产列表混杂。
4. **恢复**：单条或批量恢复到**原作用域**，保留原 `AssetLinkId`；遇同名冲突或父级失效显示原因，绝不覆盖或暗中移动。
5. 全部成功/失败状态提供明确反馈；刷新和重启后以 SQLite 为准。搜索/翻页/阶段切换不得延续隐式旧选区。输入框中 F2/Delete 等快捷键不得误操作资产。

## 三、后端不可变约束与引用保护

当前 Schema v2 已有 `asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name,deleted_at)`。活动名称唯一索引 `asset_links_active_name` 限定同一实验+分组+样本+阶段、`COLLATE NOCASE`、`deleted_at IS NULL`。S4 列表隐藏软删除项；S6 增加独立回收列表。

- `Replicate ≠ DataAsset ≠ AssetLink ≠ AssetVersion`；所有操作以 `AssetLinkId` 为唯一目标，后端验证关联的 `AssetId`、**当前** `AssetVersionId`、精确源作用域与状态，严禁仅靠文件名执行。
- 重命名只更新 `asset_links.display_name`；删除只设置 `asset_links.deleted_at`；恢复只清空其 `deleted_at`。不写 `data_assets`、`asset_versions`、`blobs`、`replicates`，不更改 SHA-256、Panel 固定版本引用或生物学重复数 n；**不得调用文件系统 rename/unlink**。
- 版本引用在 `panel_asset_versions` 和 `representative_sources` 中：任何已锁定版本不得物理删改。保守策略：若资产存在任一固定版本引用，且该批软删除将清空该资产全部活动 `AssetLinkId`，则整批拒绝 `REFERENCE_PROTECTED`；如果仍有其他活动关联，可删当前链接但保留版本引用。必须考虑同批删除两条“最后关联”的联合作用。
- 恢复原父级必须仍处于活动状态且作用域合法；已删父级/跨实验样本关联返回 `INVALID_TARGET`，不能擅自更改落点。不能执行物理 Blob GC、磁盘永久删除或 Windows 回收站集成。

## 四、文件名与冲突规则

Rust 后端强制执行，React 可预提示但不能替代：
- 非空、Windows 兼容长度边界；拒绝路径字符 `/ \ : * ? " < > |`、控制字符、NUL、方向覆盖/高风险不可见字符、`.`、`..`、尾随句点/空格。
- 拒绝设备保留名 `CON`、`PRN`、`AUX`、`NUL`、`COM1–COM9`、`LPT1–LPT9` 及带扩展名形式；验证 Unicode 等价、大小写冲突和中文名称。
- `COLLATE NOCASE` 主要覆盖 ASCII，不代表完整 Unicode 折叠。S6 必须选定一致的 Unicode 规范化/比较策略，并在 `BEGIN IMMEDIATE` 内检验同级所有活动名称，配合数据库唯一索引防竞态；有无法安全覆盖的名称需保守拒绝并记录限制。
- 回收恢复前重新检查冲突；同名占用返回 `NAME_CONFLICT`，保留原回收状态，不覆盖、不自动创建“(1)”副本。

## 五、拟定类型化 API（实施审计后冻结）

```ts
type Scope = {assayId:string;stage:"raw"|"processed"|"statistics"|"display";
  groupId?:string|null;replicateId?:string|null};
type ExpectedLink = Scope & {assetLinkId:string;assetId:string;assetVersionId:string};
type LifecycleRequest =
 | {action:"rename";requestId:string;item:ExpectedLink;newName:string}
 | {action:"soft_delete"|"restore";requestId:string;items:ExpectedLink[]};
type LifecycleResult = {action:string;requestId:string;results:Array<{
 assetLinkId:string;assetId:string;assetVersionId:string;
 name:string;deletedAt:string|null}>};
// Tauri IPC: asset_lifecycle({request})
// Tauri IPC: list_recycled_assets({request:{assayId,stage?,groupId?,replicateId?,search?,cursor?,limit?}})
// read response: {items,total,nextCursor}; each item includes ExpectedLink,
// name,deletedAt,sha256,referenced,canRestore,restoreBlockReason?
```

- 写操作请求 ID、1–100 条批量项、枚举、ID 与名称均强验证；重复 `AssetLinkId` 直接拒绝。
- 结构化错误：`INVALID_REQUEST`、`INVALID_NAME`、`NOT_FOUND`、`STALE_SELECTION`、`INVALID_TARGET`、`NAME_CONFLICT`、`REFERENCE_PROTECTED`、`ALREADY_DELETED`、`ALREADY_ACTIVE`、`REQUEST_CONFLICT`、`DATABASE_UNAVAILABLE`、`UNKNOWN_DATABASE`，禁止泄漏仓库物理路径和 SQL。
- 已删除列表仅检索 `deleted_at IS NOT NULL`，按删除时间与 LinkId 稳定排序，优先键集分页。`canRestore` 只能做提示，真实 restore 必须在写事务重新验权和冲突。

## 六、事务、幂等与审计

重用 S5 经验证的受控数据库连接：`BEGIN IMMEDIATE` → 校验同 `requestId` 载荷（`s1_requests`）→ 验证**全部**链接身份、活动/删除状态、父级及引用保护 → 全批名称冲突预检 → 只 UPDATE 指定 `asset_links` → 在同一事务中将结果写入 `audit_events`（建议 `s6:<requestId>` 唯一审计 ID，含前后名称、作用域、版本和结果）及 `s1_requests` → `COMMIT`。

任何失败均完整回滚；同 ID 相同规范化载荷在重启后也返回首次结果，同 ID 不同载荷返回 `REQUEST_CONFLICT`。测试必须注入“部分 UPDATE 后审计 INSERT 失败”，证明无半批删除/恢复。不得调用路径或物理文件操作。

## 七、强制自动化与验收矩阵

**原生 Rust/SQLite `s6_` 非零用例**（必须真实临时 SQLite，不是 Mock）：

1. 同一 Asset 多链接，仅当前 Link 重命名，其他名称/AssetId/VersionId/SHA 不变；扩展名、合法中文/Unicode 可保存。
2. 空名称、Windows 设备名、非法字符、尾随句点/空格、Unicode 规范等价、大小写同名均正确拒绝；跨样本同名合法。
3. 单条删除后正常列表不可见、回收可见，恢复原 `AssetLinkId`、作用域和版本；刷新和**重开 SQLite** 正确。
4. 回收期间原位置被占名 → `NAME_CONFLICT`，不能覆盖；父级软删除后恢复拒绝。
5. Panel 固定版本引用及最后一条活动关联 → `REFERENCE_PROTECTED`；有多关联仍不得写版本或 Blob。
6. 10 文件批量删除/恢复、批内名称冲突、错误注入后全批回滚，任何失败无部分成功。
7. 重复请求相同结果和审计计数；同请求 ID 不同载荷冲突；错 `AssetLinkId/AssetId/AssetVersionId/scope` 拒绝。
8. Blob、AssetVersion、Replicate n、原文件 SHA-256 全程不变，任何已引用版本依旧可查询。

**Playwright `@S6`**：F2/Enter/Esc、右键重命名、Delete/取消/确认、回收恢复、多选、同名/保护提示、切页/过滤清空选区，必须非零；浏览器 Mock 只能测试 UI 接线，**不可替代 Rust/SQLite**。

CI 依次执行 `npm ci`、`npm run build:desktop`、`npm run test:interaction`、`npm run test:assets`、`npm run test:desktop`、`npm run test:e2e`、`npm run test:e2e:desktop`、`npm run test:e2e:desktop -- --grep "@S6"`；在 `apps/desktop/src-tauri` 运行 `cargo fmt --all -- --check`、`cargo test --locked --no-default-features s6_ -- --list`（验证用例数>0）、`cargo test --locked --no-default-features s6_`、`cargo test --locked --no-default-features`、`cargo check --locked`。保留全部 S0–S5 专项；Windows Actions 要对 S6 分支触发，并构建 Release EXE / 上传 Artifact。修复 CI 至全绿。

## 八、Windows 人工验收（不得提前标记通过）

使用可抛弃测试文件和本阶段 EXE：记录 EXE SHA-256、Actions Run、系统版本；实际按 F2、右键重命名、Delete/取消/确认、回收恢复；尝试非法名称、Unicode/大小写冲突、已引用版本、恢复时重名、跨页旧选区；核对源文件 SHA-256、系统 Blob 数、版本 ID、n、刷新重启后的状态。保存截图、实际结果、操作者和日期；未实际执行的项目为 `manual_pending`，不能写成 PASS。

## 九、开发与交付顺序

1. 审计 S5 代码、Schema、Panel 引用与正式 React 入口；冻结 Unicode/保护/回收设计，写 `docs/S6_IMPLEMENTATION_DESIGN.md`。
2. 开发 Rust/SQLite 真正写接口与回收查询，添加 `s6_`；接入正式项目管理的 F2、右键、Delete、回收列表和恢复。
3. 新增 `@S6`，跑 S0–S6 全回归；修复 Windows CI 至成功并构建便携 EXE。
4. 生成 `docs/S6_EXECUTION_REPORT.md`，更新任务 JSON/总计划；提交独立 Draft PR（base 为 S5 分支），交付提交 SHA、PR、CI、EXE、测试数量、Windows 人工清单及风险。
5. 不修改 S7–S11 状态，不合并 main，不用珍贵科研数据；仅在真实证据充分时标记机器通过/人工验收。

## 十、下一轮可直接执行的开发指令

```text
继续开发 Data-M 科研数据管理平台。
TASK_ID=S6 —— 重命名、软删除与回收恢复。
从 feat/s5-asset-link-copy-move 最新 HEAD 创建 feat/s6-rename-recycle，
记录基线 SHA；S5 PR #49 和 S4 PR #47 仍 Draft，禁止未经授权合并 main。
先读取 docs/AI_DEV_EXECUTION_SPEC.md、docs/DATA_M_AI_TASKS.json、
docs/S5_EXECUTION_REPORT.md、docs/S5_IMPLEMENTATION_DESIGN.md、
docs/S6_DEVELOPMENT_TASK.md、docs/DEVELOPMENT_MASTER_PLAN.md、
docs/S3_IMPORT_PROTOCOL.md。
严格沿原 S0–S11 计划，仅实施 S6。
审计并编写 S6 技术设计后，直接实现 Rust/SQLite 的 F2/右键重命名、
Delete/右键软删除、回收列表与恢复、请求幂等、原子事务、引用保护、
Windows 文件名与同名检查、审计，并接入真实项目管理 UI。
必须有非零 Rust s6_ 和 Playwright @S6 测试，运行 S0–S6 回归，
修复 Windows CI 直至全绿，构建便携 EXE，提交独立 Draft PR 和执行报告。
不得把 Mock 当成 Rust/SQLite 通过证据，不物理删除 Blob，不更改 n，
S7 备份恢复签收之前禁止使用珍贵或唯一的真实科研数据。
```
