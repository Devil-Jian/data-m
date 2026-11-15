# Data-M｜AI 可执行开发规范（按功能闭环）

> **使用对象**：有 GitHub/本地代码执行能力的 AI 开发代理 + Windows 人工验收者。  
> **版本**：2026-11-15.1 · 审查快照：2026-11-15 · **不代表代码已发布**。  
> **真相优先级**：当前仓库代码与 CI > 本执行文档快照 > 历史聊天/设计推测。若冲突，先记录差异并更新任务，再实施。  
> **机器任务文件**：`DATA_M_AI_TASKS.json`（与本文 S0–S11 同源生成，状态与测试同步）。

## 0. 执行入口：给 AI 的最短指令

将本文件与 `DATA_M_AI_TASKS.json` 一起交给开发代理，并补充一句：

```text
你是 Data-M 的实现代理。先读取本规范与 DATA_M_AI_TASKS.json；
仅执行 TASK_ID=S0。核对当前 GitHub 事实和依赖；只修改本任务范围；
添加可重复测试并执行；能做的自动化必须实跑；不能做的 Windows 原生人工步骤
标记 manual_pending。建立短功能分支和 Draft PR，提交证据与剩余风险；
未经人工明确签收，严禁宣布 accepted 或自动合并 main。
```

后续将 `TASK_ID=S0` 改为具体 S1–S11；**一次只执行一个任务包**。如任务依赖未通过，则记录 `blocked`，不绕过依赖。

## 1. 项目不变量与授权边界

**技术基线**：Windows 11 单机、离线优先；React/TypeScript + shadcn/ui + Tauri 2 + Rust + SQLite + CAS；正式 UI 入口 `apps/desktop`，公用 UI 在 `packages/ui`，业务 UI 在 `packages/core`，Explorer 交互在 `packages/interaction`。

必须始终遵守：

1. **Replicate ≠ DataAsset ≠ AssetLink ≠ AssetVersion**。复制文件或关联，不得凭空增加生物学重复；同一资产多处归属需要准确定位关联。
2. 原始源文件只读语义；CAS Blob 字节不可修改；入库形成新 AssetVersion 而非就地覆盖；有引用的版本不可静默删改。
3. Panel 固定引用 `AssetVersionId`；统计纳入是 `Analysis × Replicate` 的关系，不是文件全局 boolean。
4. React 只表达操作意图，不直接执行 SQL、不直接读写 CAS；真正提交由 Rust 执行，并返回持久化结果。
5. SQLite 事务不等价于文件系统事务；安全导入需要 staging / commit / reconcile / crash test。
6. **没有 S0–S7 全部通过 + 恢复演练签收，禁止使用珍贵真实科研数据测试或正式入库**。使用专门创建的可抛弃测试样本。
7. 不执行危险 Git 操作（`push --force`、`reset --hard`、全仓历史清理、未经批准合并）；不混入旧 WPF/WinUI/Vue 路线。
8. **不允许修改测试使失败被掩盖**，不允许 `--passWithNoTests` 或跳过相关检查以宣称通过。

## 2. 审查基线与真实进度

| 领域 | 截至 2026-11-15 的证据 | 对验收的含义 |
|---|---|---|
| `main` | [仓库 main](https://github.com/Devil-Jian/data-m/tree/main) 仍以旧初始化 README 为主 | 未形成正式新客户端主线 |
| UI 统一 | [PR #40](https://github.com/Devil-Jian/data-m/pull/40) Draft；[Windows CI](https://github.com/Devil-Jian/data-m/actions/runs/43710969361)、[UI CI](https://github.com/Devil-Jian/data-m/actions/runs/43710969358) 成功 | 证明该提交通过自动化，不证明 WebView2 人工验收 |
| 文档 | [PR #41](https://github.com/Devil-Jian/data-m/pull/41) Draft；[PR #24](https://github.com/Devil-Jian/data-m/pull/24) 的 PRD/IPC 草案在独立分支 | 合并时需核对冲突和历史版本 |
| 存储 | [PR #36](https://github.com/Devil-Jian/data-m/pull/36) Schema、[#37](https://github.com/Devil-Jian/data-m/pull/37) CAS staging、[#38](https://github.com/Devil-Jian/data-m/pull/38) audit 均是 Draft | 只允许原型/临时数据库，不存在正式导入闭环 |
| 发布 | EXE 便携构建成功；`bundle.active=false` | 尚无可发布 NSIS/MSI 安装器 |

**进度状态**：`not_started` 无实现；`prototype_only` 仅原型；`manual_pending` 正在开发；`machine_passed` 自动验证完成；`manual_pending` 自动验证通过而 Windows 人工未验收；`accepted` 合并、机器与人工均签收；`blocked` 有未解除依赖/风险。**禁止用主观百分比代替这些状态。**

## 3. 统一流水线与证据规则

### 3.1 每个任务必须这样做

```text
读代码/查现有 PR → 核对依赖与风险 → 固定接口/测试场景
→ 实现 Rust/SQLite/CAS + 集成测试 → 接入 React UI
→ 自动化机器验收 → Windows GUI 人工验收（可待签）
→ Draft PR + 证据报告 → 人工批准后合并 → 更新状态
```

**PR 粒度**：每个 S 任务为一个可演示的完整交付包；允许一个包拆 2–3 个紧密关联的小 PR，但任何部分 PR 绿灯不代表整个包 accepted。下游任务仅在依赖满足的可用集成主线开发。

### 3.2 已有全局自动化命令

> 这些脚本位于 PR #40 候选代码；如果 `main` 尚未整合，则在指定候选分支或经批准的集成分支运行。测试环境需 Node/npm；原生 Windows 构建需 Rust/Cargo、C++ 工具链和 WebView2。Playwright 需先安装 Chromium。

| ID | 工作目录 | 命令 | 验证内容 |
|---|---|---|---|
| G01 | `.` | `npm run build:desktop` | TypeScript + Vite 桌面 UI 构建；不能替代原生验收 |
| G02 | `.` | `npm run test:interaction` | Explorer 选择/拖拽规则回归 |
| G03 | `.` | `npm run test:assets` | Asset/Replicate 身份不变量回归 |
| G04 | `.` | `npm run test:desktop` | 桌面前端单元测试 |
| G05 | `.` | `npm run test:e2e:desktop` | 5176 端口桌面渲染器浏览器 E2E |
| G06 | `.` | `npm run test:e2e` | Explorer 旧回归测试（保留至迁移完成） |
| G07 | `apps/desktop/src-tauri` | `cargo test --no-default-features` | Rust/SQLite/CAS 单元与集成测试 |
| G08 | `apps/desktop/src-tauri` | `cargo fmt --all -- --check` | Rust 格式检查 |
| G09 | `apps/desktop/src-tauri` | `cargo check` | Rust 编译检查 |
| G10 | `.` | `npm run tauri:build` | Windows CI 构建便携 EXE（当前不是安装程序） |

**验证前置**：锁定依赖版本（补齐仓库锁文件并采用可复现的 CI 安装策略）；执行 `npm install`（锁定后优先 `npm ci`），若 Playwright 未安装则先 `npx playwright install chromium`；Rust 命令均在 `apps/desktop/src-tauri` 执行。不要把尚未存在的新增测试目标报告成已通过。

**针对模块的新增测试约定（提案，须实现）**：Rust 测试名带 `s0_`…`s11_` 前缀；浏览器 E2E 标题带 `@S0`…`@S11`。任务包新增后，应可运行下面测试筛选命令；**筛选到 0 个测试不算成功**。其中浏览器 E2E 可以验证 UI 流程与 Mock，但涉及真实 Tauri IPC 的最终闭环还需 Rust 集成与 Windows 原生验证。

### 3.3 两类验收不能相互代替

**机器通过**需要：本次 `head_sha`、真实执行命令、退出码 0、用例执行数大于 0、Rust/JS/Playwright 结果、Windows CI URL（适用时）、具名断言覆盖成功/失败/恢复；不得只写“应该通过”。

**人工通过**需要：Windows 11 实际机器/VM、EXE/安装程序版本与 SHA、明确操作步骤及预期/实际结果、截图或录屏、原文件/仓库哈希对比（涉及文件时）、执行者和日期。AI 无法亲自操作 Windows GUI 时只能标记 `manual_pending`，提出手动验收清单。

**完成判定**：`accepted = 目标分支合并 + 相关机器测试全通过 + 必须的人工作业签字 + 安全边界明确 + 文档/Issue 更新`。缺其中一项不得关闭 Task/Issue。

## 4. 模块索引（按依赖顺序）

| ID | 功能包 | 依赖 | 当前开发进度 | Issue |
|---|---|---|---|---|
| **S0** | 工程主线与唯一桌面 UI | 无 | `manual_pending`，见 [执行报告](S0_EXECUTION_REPORT.md)，未签收 | #8, #12, #4, #13 |
| **S1** | 项目主题实验 CRUD | S0 | `prototype_only`，未验收 | #17, #14 |
| **S2** | 分组样本与四阶段结构 | S1 | `prototype_only`，未验收 | #17, #14 |
| **S3** | 安全文件导入与任务恢复 | S2 | `prototype_only`，未验收 | #6, #15, #14 |
| **S4** | 真实文件浏览搜索与选择 | S3 | `prototype_only`，未验收 | #5, #13, #17 |
| **S5** | 资产复制与逻辑移动 | S4 | `prototype_only`，未验收 | #6, #7, #13 |
| **S6** | 重命名软删除与回收恢复 | S5 | `prototype_only`，未验收 | #7, #13, #6 |
| **S7** | 完整性检查备份恢复 | S6 | `prototype_only`，未验收 | #16, #6, #15 |
| **S8** | 统计分析与样本纳入 | S7 | `prototype_only`，未验收 | #17, #19, #14 |
| **S9** | 论文组图与固定版本关联 | S7 | `prototype_only`，未验收 | #18, #14 |
| **S10** | 双向溯源与无损导出 | S8, S9 | `prototype_only`，未验收 | #19, #18 |
| **S11** | Windows 性能安装与正式交付 | S10 | `prototype_only`，未验收 | #20, #21, #11 |

> **安全里程碑**：S0–S7 是内部文件管理 MVP；S8–S10 是科研/论文增值闭环；S11 是 Windows 正式发布。S7 是正式科研数据的强制安全门槛。S9 可在 S8 开发期间并行准备，但任何人不能在 S7 前对真实数据解锁写入。

## 5. 功能任务定义（每次选一个 ID 执行）

### S0｜工程主线与唯一桌面 UI

**任务元数据**：`id=S0` · `priority=P0` · `depends_on=none` · `status=manual_pending` · Issues：[#8](https://github.com/Devil-Jian/data-m/issues/8), [#12](https://github.com/Devil-Jian/data-m/issues/12), [#4](https://github.com/Devil-Jian/data-m/issues/4), [#13](https://github.com/Devil-Jian/data-m/issues/13)。

**当前事实**：PR #40/41 与多层 Draft PR 已存在；本次独立 [PR #42](https://github.com/Devil-Jian/data-m/pull/42) 的实现提交已通过 Windows/Linux 机器验证，见 [执行报告](S0_EXECUTION_REPORT.md)。main 仍为初始化 README，WebView2 与集成签收未完成，S0 为 manual_pending 而非 accepted。

**用户结果**：统一可复现的正式工作树：一套 UI、一条主线、两种调试模式。

**前端范围**：apps/desktop 作为正式界面入口，packages/ui 统一样式与组件；保留核心 Workbench/interaction，清理重复前端源并修正 README 的 Vue/React 叙述；提供浏览器热更新入口和原生调试入口；不新增业务功能。

**后端范围**：保持只读 drop inspect 与 shell_status；禁止将实验性 CAS/DB 注册为写入 IPC；核对 Draft PR 依赖和 PR #35 的 Unicode 安全修复，避免遗漏改动。

**数据/接口规则**：主线不得误初始化或修改用户已有数据库；统一文档声明 Schema 原型不具备用户文件导入能力。

**机器验证（自动化，逐项可判定）**

- [ ] M1: main 或经明确批准的集成主线包含完整工程，工作树可从零安装。
- [ ] M2: Desktop 与浏览器加载同一 packages/ui；移除第二套生产 UI。
- [ ] M3: CI 在合并目标 SHA 下跑完 JS/Rust、Windows EXE 输出；所有必需任务通过。
- [ ] M4: 实际 repo 根目录存在可重复的安装/预览/测试说明，旧试验分支保持隔离。

**本任务必须新增或确认的自动测试入口**（若项目没有该测试，先创建而不是跳过）：

- `cargo test --no-default-features s0_`（cwd=`apps/desktop/src-tauri`）：复用或增加 S0 Rust smoke/test；验证壳与只读接口未出现写操作。
- `npm run test:e2e:desktop -- --grep "@S0"`（cwd=`.`）：增加 @S0 浏览器与 Desktop 一致性测试。

**人工验收（Windows 原生，机器不能代替）**

- [ ] H1: 在 Windows 11 上打开经 CI 生成的 EXE；检查页面布局、100% 与 150% DPI、缩放和焦点。
- [ ] H2: 鼠标单选、Ctrl/Shift、右键与 F2 交互；拖入仅展示只读预览，原文件哈希不变。
- [ ] H3: 检查浏览器 5176 与 WebView2 主页面一致，操作中无崩溃；保存截图、EXE SHA 和 Windows 版本。

**交付件**：可复现主线/集成 PR；README、技术栈 ADR 和工具链文档；Windows CI URL + EXE Artifact + Windows 实测记录。

**明确不做**：不在本任务开放文件导入/移动/删除；不合并历史 WPF/WinUI/Vue 试验路线。

**安全与退出边界**：禁止未经审查的 PR 链整体合并和对 main 的强制推送；未人工验收时只能标记 manual_pending。

**状态迁移**：原型/待开始 → 开发中 → 机器通过 → 人工待验收 → 验收通过；如果依赖或安全阻塞，改为 `blocked` 并写明原因。

### S1｜项目主题实验 CRUD

**任务元数据**：`id=S1` · `priority=P1` · `depends_on=S0` · `status=prototype_only` · Issues：[#17](https://github.com/Devil-Jian/data-m/issues/17), [#14](https://github.com/Devil-Jian/data-m/issues/14)。

**当前事实**：已有项目树 Demo 与 SQLite projects/themes/assays 表；生产 CRUD、IPC 和数据持久化未实现。

**用户结果**：在 UI 新建/重命名/软删除 Project → Theme → Assay，重启依然正确。

**前端范围**：复用左侧树、编辑对话框；加载/空态/冲突/失败显示；提交后重新查询服务器数据，不用 Demo 当成功凭据。

**后端范围**：Rust Repository + 结构化 Tauri CRUD 接口，参数校验、稳定 ID 与软删除检查；拒绝删除有子节点或关联资产的父节点，或提示按安全策略处理。

**数据/接口规则**：projects/themes/assays 及 FK；首次打开仅初始化受控的新测试数据库；幂等请求/事务；前端不提交任意 SQL。

**机器验证（自动化，逐项可判定）**

- [ ] M1: 单元/集成测试创建2项目及多级节点后，关闭连接重开数据库记录一致。
- [ ] M2: 空名称、非法 parent id、重复操作、删除非空父节点的错误分支均被覆盖。
- [ ] M3: 浏览器 E2E 有明确后端 mock/真实运行态标签；Native IPC 集成测试使用临时持久库。
- [ ] M4: 失败时零部分提交；结果仅来自 DB 查询。

**本任务必须新增或确认的自动测试入口**（若项目没有该测试，先创建而不是跳过）：

- `cargo test --no-default-features s1_`（cwd=`apps/desktop/src-tauri`）：新增 Rust 测试函数以 s1_ 开头；必须至少有 1 个且执行数量>0。
- `npm run test:e2e:desktop -- --grep "@S1"`（cwd=`.`）：新增 Playwright 用例标题带 @S1；必须至少有 1 个且执行数量>0。

**人工验收（Windows 原生，机器不能代替）**

- [ ] H1: 在 Windows EXE 中创建2项目、每项目多个主题/实验；退出重启后层级仍一致。
- [ ] H2: 尝试创建空名、删除有子项的项目；有清晰提示且数据未丢。
- [ ] H3: 手动证据包含界面前后截图与测试库文件检查。

**交付件**：Rust Repository + Tauri CRUD + UI 接线；针对临时 SQLite 的持久化/非法路径测试；功能录屏/截图与 PR 证据。

**明确不做**：不包括 Group/Replicate，交 S2；不加入云端账户或协作。

**安全与退出边界**：不得在已有未知 SQLite 文件上运行初始化迁移；没有真实 Tauri 存储时禁止声称闭环。

**状态迁移**：原型/待开始 → 开发中 → 机器通过 → 人工待验收 → 验收通过；如果依赖或安全阻塞，改为 `blocked` 并写明原因。

### S2｜分组样本与四阶段结构

**任务元数据**：`id=S2` · `priority=P1` · `depends_on=S1` · `status=prototype_only` · Issues：[#17](https://github.com/Devil-Jian/data-m/issues/17), [#14](https://github.com/Devil-Jian/data-m/issues/14)。

**当前事实**：存在 Group/Replicate Demo 与 schema/页面结构；尚无实际分组或样本 CRUD。

**用户结果**：建立分组、真实生物学重复，并呈现 Raw/Processed/Statistics/Display 四阶段容器。

**前端范围**：实验分组和 Replicate 的添加/修改/删除；样本量 n 独立显示；现有全景矩阵与阶段视图从 Repository 查询真实节点。

**后端范围**：group/replicate CRUD；禁止跨 assay 关联；阶段归属规则（Raw/Processed 为样本级，Statistics/Display 可为实验级）统一返回。

**数据/接口规则**：experiment_groups、replicates 与 asset_links 的层级约束；文件资产与生物重复分开计数，稳定 ID；删除非空容器需明确保护。

**机器验证（自动化，逐项可判定）**

- [ ] M1: 创建3组×6样本，DB 查询准确为18；重启后一致。
- [ ] M2: 非法跨实验关联被 FK 或用例拒绝。
- [ ] M3: 复制/关联资产不创建 Replicate 的不变量具备可运行回归测试。
- [ ] M4: 四阶段空视图无数据也不报错。

**本任务必须新增或确认的自动测试入口**（若项目没有该测试，先创建而不是跳过）：

- `cargo test --no-default-features s2_`（cwd=`apps/desktop/src-tauri`）：新增 Rust 测试函数以 s2_ 开头；必须至少有 1 个且执行数量>0。
- `npm run test:e2e:desktop -- --grep "@S2"`（cwd=`.`）：新增 Playwright 用例标题带 @S2；必须至少有 1 个且执行数量>0。

**人工验收（Windows 原生，机器不能代替）**

- [ ] H1: 在 Windows EXE 创建 Control/Model/Treatment 每组6个样本，重启后 n=6。
- [ ] H2: 在 Raw、Processed、Statistics、Display 间切换，检查分组显示及缺省状态。
- [ ] H3: 尝试非法关联/删除含样本分组，核对安全提示与数据库状态。

**交付件**：分组/样本 IPC 与 UI；四阶段数据契约；n 不变量测试/Windows 验收。

**明确不做**：不导入真实文件，交 S3；不实现统计计算和论文组图。

**安全与退出边界**：任何文件资产操作均不可改变真实 Replicate 数量；所有测试数据位于临时仓库。

**状态迁移**：原型/待开始 → 开发中 → 机器通过 → 人工待验收 → 验收通过；如果依赖或安全阻塞，改为 `blocked` 并写明原因。

### S3｜安全文件导入与任务恢复

**任务元数据**：`id=S3` · `priority=P0` · `depends_on=S2` · `status=prototype_only` · Issues：[#6](https://github.com/Devil-Jian/data-m/issues/6), [#15](https://github.com/Devil-Jian/data-m/issues/15), [#14](https://github.com/Devil-Jian/data-m/issues/14)。

**当前事实**：已有 Windows 只读拖入、Rust CAS staging 和 SHA-256 原型；未注册真实导入 IPC，缺少 SQL 事务协同/崩溃恢复。

**用户结果**：Explorer 拖入→检查→确认→进度→入库，源文件保持不变。

**前端范围**：复用外部拖入预览，增加目标容器与确认机制、任务进度/取消/重试/失败明细；不允许单靠预览“eligible”状态开启写入。

**后端范围**：Rust 校验源路径/类型及所属目标；流式复制、SHA-256、same-volume staging、不可覆盖 CAS 提交；持久 import job 状态；Blob/AssetVersion/AssetLink 事务登记与崩溃 reconcile；低空间/文件锁/重复文件/源文件读取期间改变/符号链接/路径竞态测试；最小权限。

**数据/接口规则**：Blob 由规范化 hash 索引；AssetId、AssetLinkId、AssetVersionId 不混淆；操作 requestId 幂等；数据库和文件系统跨资源协调要有可恢复状态机。

**机器验证（自动化，逐项可判定）**

- [ ] M1: 创建小文件、100文件和受控大文件测试；哈希/大小与来源逐项一致；源内容/修改时间不变。
- [ ] M2: 重复导入相同字节只复用 Blob，不混淆不同样本关联。
- [ ] M3: 取消/强制中止/磁盘满/重名/锁定/坏 hash 与重启 reconcile 后没有“已确认但缺 Blob”的版本。
- [ ] M4: 导入完成后重启可查询，重复请求幂等；测试用例包含故障注入且不是只跑 happy path。

**本任务必须新增或确认的自动测试入口**（若项目没有该测试，先创建而不是跳过）：

- `cargo test --no-default-features s3_`（cwd=`apps/desktop/src-tauri`）：新增 Rust 测试函数以 s3_ 开头；必须至少有 1 个且执行数量>0。
- `npm run test:e2e:desktop -- --grep "@S3"`（cwd=`.`）：新增 Playwright 用例标题带 @S3；必须至少有 1 个且执行数量>0。

**人工验收（Windows 原生，机器不能代替）**

- [ ] H1: Windows 原生 EXE 将测试文件拖入指定 Raw 容器，人工确认并观察进度；重启可见。
- [ ] H2: 用 disposable 源文件模拟取消与重试，比较源目录哈希、数量、时间戳及目录结构。
- [ ] H3: 人为中断后重新打开软件，检查状态、仓库审计；证据含 job 记录和哈希报告。

**交付件**：正式导入 IPC + job/进度事件；Staging/commit/reconcile 协议和故障测试；源保护与 SHA-256 记录。

**明确不做**：不支持复杂目录递归/外部拖出；不解析 TIFF/SVS/CZI 深层元数据；不对用户重要文件开放生产导入。

**安全与退出边界**：本任务完成只能在隔离测试仓库中试用；S7 备份恢复演练通过前明确禁止重要真实科研数据入库。

**状态迁移**：原型/待开始 → 开发中 → 机器通过 → 人工待验收 → 验收通过；如果依赖或安全阻塞，改为 `blocked` 并写明原因。

### S4｜真实文件浏览搜索与选择

**任务元数据**：`id=S4` · `priority=P1` · `depends_on=S3` · `status=prototype_only` · Issues：[#5](https://github.com/Devil-Jian/data-m/issues/5), [#13](https://github.com/Devil-Jian/data-m/issues/13), [#17](https://github.com/Devil-Jian/data-m/issues/17)。

**当前事实**：已有 DemoAsset 与 TanStack Virtual 10k 演示和键盘/鼠标交互；未连接真实 SQLite 查询。

**用户结果**：真实资产在正确实验位置以列表/表格/矩阵展示，搜索筛选与选择一致。

**前端范围**：将 DemoAsset 数据读取替换为真实查询；保留同一选择/焦点模型；列表分页、搜索、排序、详情抽屉，显示哈希、大小、版本、阶段、引用状态。

**后端范围**：只读 list_assets/get_asset 查询，按作用域和排序做稳定分页；区分 AssetId 与 AssetLinkId（同一资产可被多个容器引用），选择操作目标应精确到上下文。

**数据/接口规则**：SQLite 索引和分页；引用版本 id 只读返回，禁止暴露任意磁盘写路径；同一容器重名和检索范围契约。

**机器验证（自动化，逐项可判定）**

- [ ] M1: 从 S3 入库的100文件查询与 UI 总量一致，分页不重不漏。
- [ ] M2: 同一资产多容器关联时选中/拖拽作用域不混淆。
- [ ] M3: Ctrl+A/Shift/框选在当前过滤的逻辑集合上确定一致。
- [ ] M4: 真实 SQLite 的分页边界、非法游标和稳定排序有集成测试。

**本任务必须新增或确认的自动测试入口**（若项目没有该测试，先创建而不是跳过）：

- `cargo test --no-default-features s4_`（cwd=`apps/desktop/src-tauri`）：新增 Rust 测试函数以 s4_ 开头；必须至少有 1 个且执行数量>0。
- `npm run test:e2e:desktop -- --grep "@S4"`（cwd=`.`）：新增 Playwright 用例标题带 @S4；必须至少有 1 个且执行数量>0。

**人工验收（Windows 原生，机器不能代替）**

- [ ] H1: Windows EXE 在不同组和阶段浏览真实测试文件，检查名称/数量/来源。
- [ ] H2: 过滤、分页、跨视图多选，10k 数据量由 S11 执行完整性能验收。
- [ ] H3: 再次启动后搜索结果正确且不存在 Demo 混入。

**交付件**：SQLite 资产查询 API；真实资产视图与回归测试；分页/选择一致性 Windows 检查。

**明确不做**：不新增图片格式解析；此任务为只读浏览，不实现文件移动或删除。

**安全与退出边界**：允许渲染只读 hash/版本信息；选择/复制操作必须携带精确的作用域关联 ID。

**状态迁移**：原型/待开始 → 开发中 → 机器通过 → 人工待验收 → 验收通过；如果依赖或安全阻塞，改为 `blocked` 并写明原因。

### S5｜资产复制与逻辑移动

**任务元数据**：`id=S5` · `priority=P1` · `depends_on=S4` · `status=prototype_only` · Issues：[#6](https://github.com/Devil-Jian/data-m/issues/6), [#7](https://github.com/Devil-Jian/data-m/issues/7), [#13](https://github.com/Devil-Jian/data-m/issues/13)。

**当前事实**：已存在 Ctrl/Shift/拖放/剪贴板意图；尚无 Rust 实际资产链接事务。

**用户结果**：同仓库把测试资产复制或移动到合法目标，样本数量/版本来源保持稳定。

**前端范围**：复用右键、快捷键、拖动，展示目标、冲突和操作状态；成功以 DB 返回值更新视图；失败保持原选择与可解释错误。

**后端范围**：Rust copy/move asset associations，执行权限/引用/冲突验证与事务更新；以 AssetLinkId 指明要移动的可见归属；Blob/AssetVersion 不因逻辑复制而重写。

**数据/接口规则**：同一 Blob 可经多个 AssetLink 共享；ReplicateId 不能被文件复制动作插入；请求 ID 幂等、批次操作原子或明确部分成功策略（第一版统一原子）。

**机器验证（自动化，逐项可判定）**

- [ ] M1: 复制/移动到合法组后双向列表查询一致，重启后不回退。
- [ ] M2: 复制 10 个文件前后 Replicate 数不变，Blob 数不因纯逻辑复制增长。
- [ ] M3: 重名、非法目标和部分错误的批次回滚正确，版本哈希不变。
- [ ] M4: 所有操作都有结构化错误码和审计记录。

**本任务必须新增或确认的自动测试入口**（若项目没有该测试，先创建而不是跳过）：

- `cargo test --no-default-features s5_`（cwd=`apps/desktop/src-tauri`）：新增 Rust 测试函数以 s5_ 开头；必须至少有 1 个且执行数量>0。
- `npm run test:e2e:desktop -- --grep "@S5"`（cwd=`.`）：新增 Playwright 用例标题带 @S5；必须至少有 1 个且执行数量>0。

**人工验收（Windows 原生，机器不能代替）**

- [ ] H1: Windows EXE 使用 Ctrl+拖拽和 Shift+拖拽、Ctrl+C/X/V、右键操作。
- [ ] H2: 试跨组/跨阶段和非法位置；验证列表、样本 n、原文件 hash。
- [ ] H3: 刷新/重启与所见一致，失败不出现“成功”提示。

**交付件**：可重试操作接口、审计与错误映射；真实 Explorer 交互闭环；批量回滚和身份不变量测试。

**明确不做**：不实现跨仓库物理复制、外部 Explorer 拖出；不修改原始 Blob 字节。

**安全与退出边界**：操作目标必须精确到 AssetLink/Version 的合法组合；不允许同一 Asset 在多上下文产生误删除。

**状态迁移**：原型/待开始 → 开发中 → 机器通过 → 人工待验收 → 验收通过；如果依赖或安全阻塞，改为 `blocked` 并写明原因。

### S6｜重命名软删除与回收恢复

**任务元数据**：`id=S6` · `priority=P1` · `depends_on=S5` · `status=prototype_only` · Issues：[#7](https://github.com/Devil-Jian/data-m/issues/7), [#13](https://github.com/Devil-Jian/data-m/issues/13), [#6](https://github.com/Devil-Jian/data-m/issues/6)。

**当前事实**：F2 与 Windows 文件名校验仅 Demo；后端无正式软删除/恢复接口。

**用户结果**：重命名归属名称、软删除并从回收视图恢复，引用文件版本不失效。

**前端范围**：F2/右键重命名真实提交、删除确认、回收列表和恢复按钮；失败、重名、正在被引用时显示可理解的解释。

**后端范围**：Rust rename/delete/restore use cases；同容器同阶段冲突检查与外键校验；资产链接软删除；保留 CAS 原字节及被 Panel 锁定的版本。

**数据/接口规则**：display_name 修改不能改变 AssetId、AssetVersionId、BlobKey；跨关联删除与孤儿引用策略明确；回收冲突返回 NAME_CONFLICT。

**机器验证（自动化，逐项可判定）**

- [ ] M1: 非法字符、设备保留名、Unicode、大小写冲突和隐藏重名覆盖。
- [ ] M2: 删除→恢复后关联、版本及 hash 完全一致；重启持久。
- [ ] M3: 已引用 AssetVersion 不会物理删除；失败/并发重试一致。
- [ ] M4: 回收恢复和重命名操作在 Audit 里有记录。

**本任务必须新增或确认的自动测试入口**（若项目没有该测试，先创建而不是跳过）：

- `cargo test --no-default-features s6_`（cwd=`apps/desktop/src-tauri`）：新增 Rust 测试函数以 s6_ 开头；必须至少有 1 个且执行数量>0。
- `npm run test:e2e:desktop -- --grep "@S6"`（cwd=`.`）：新增 Playwright 用例标题带 @S6；必须至少有 1 个且执行数量>0。

**人工验收（Windows 原生，机器不能代替）**

- [ ] H1: Windows EXE 执行 F2、右键删除并从回收视图恢复。
- [ ] H2: 尝试同名、非法名称、被论文关联场景（可用临时测试关系），检查错误提示。
- [ ] H3: 重启后文件与链接状态正确，原文件字节保持不变。

**交付件**：Rename/soft-delete/restore IPC；回收视图与引用保护；错误场景与审计回归。

**明确不做**：不实现物理 Blob GC；不实现磁盘级永久删除、Windows 回收站集成。

**安全与退出边界**：强制禁止任何业务操作直接 unlink CAS objects；回收不是物理删除。

**状态迁移**：原型/待开始 → 开发中 → 机器通过 → 人工待验收 → 验收通过；如果依赖或安全阻塞，改为 `blocked` 并写明原因。

### S7｜完整性检查备份恢复

**任务元数据**：`id=S7` · `priority=P0` · `depends_on=S6` · `status=prototype_only` · Issues：[#16](https://github.com/Devil-Jian/data-m/issues/16), [#6](https://github.com/Devil-Jian/data-m/issues/6), [#15](https://github.com/Devil-Jian/data-m/issues/15)。

**当前事实**：有 SQLite/CAS 只读审计原型；无 WAL-safe 备份、重建验证、孤儿扫描、恢复 UI。

**用户结果**：对测试仓库核验、制作可验证快照，在空目录完整恢复文件和关系。

**前端范围**：完整性检查/备份/恢复工作台，显示阶段进度、校验报告及风险提示；恢复操作明确目标、冲突及将覆盖什么；不静默改写现有仓库。

**后端范围**：SQLite 在线一致性备份（WAL-safe）、Blob manifest、不可变快照 ID；恢复时校验所有 SHA-256、schema、FK、引用关系、孤儿 staging；锁/权限/中断恢复；读审计不自动修复/删除损坏对象；用临时目录故障注入。

**数据/接口规则**：备份 manifest 含 schema_version、snapshot_id、AssetVersion/Blob、大小、sha256；恢复到全新目录后 entity/relationship/blob counts 和每个哈希与备份时一致。

**机器验证（自动化，逐项可判定）**

- [ ] M1: 备份→清空测试仓库→恢复的全量对照测试通过。
- [ ] M2: 手工篡改字节、删除 Blob、断开 FK、插入孤儿 staging 能被识别且不误修复。
- [ ] M3: 执行中断/磁盘满后原仓库与已有备份保持可读，下一次可重试。
- [ ] M4: SQLite WAL 活跃写入下备份仍可打开并通过 integrity_check/FK 校验。

**本任务必须新增或确认的自动测试入口**（若项目没有该测试，先创建而不是跳过）：

- `cargo test --no-default-features s7_`（cwd=`apps/desktop/src-tauri`）：新增 Rust 测试函数以 s7_ 开头；必须至少有 1 个且执行数量>0。
- `npm run test:e2e:desktop -- --grep "@S7"`（cwd=`.`）：新增 Playwright 用例标题带 @S7；必须至少有 1 个且执行数量>0。

**人工验收（Windows 原生，机器不能代替）**

- [ ] H1: Windows EXE 对 S1–S6 构造的测试仓库做完整备份，复制至另一空目录恢复。
- [ ] H2: 逐项比较组数/样本数/资产版本/Blob 哈希；输出并保存 diff=0 的证据。
- [ ] H3: 人为损坏测试备份后应拒绝误恢复，原仓库不受影响。

**交付件**：可审计备份格式与恢复 IPC；WAL-safe 故障与整库恢复测试；Windows 恢复演练与逐项 hash/关系对比。

**明确不做**：不实现云备份；不允许对被检测坏的 Blob 自动猜测修复。

**安全与退出边界**：只有 S0–S7 全部通过且人工完成恢复演练，才可批准珍贵科研数据进入日常使用。

**状态迁移**：原型/待开始 → 开发中 → 机器通过 → 人工待验收 → 验收通过；如果依赖或安全阻塞，改为 `blocked` 并写明原因。

### S8｜统计分析与样本纳入

**任务元数据**：`id=S8` · `priority=P1` · `depends_on=S7` · `status=prototype_only` · Issues：[#17](https://github.com/Devil-Jian/data-m/issues/17), [#19](https://github.com/Devil-Jian/data-m/issues/19), [#14](https://github.com/Devil-Jian/data-m/issues/14)。

**当前事实**：已有 StatisticalAnalysis/AnalysisSample 表和演示 UI；没有真实分析 CRUD/输入版本锁定。

**用户结果**：记录某次统计选择了哪些生物学重复和哪些固定版本输入/结果，不执行统计计算。

**前端范围**：统计分析列表/编辑器，选择纳入样本并关联文件版本；保存、冻结及重开已保存分析的审计视图。

**后端范围**：创建/修改/冻结分析用例；验证跨组/跨 assay 的合法样本集合；建立分析输入/结果的 AssetVersion 引用或快照迁移，冻结后禁止悄悄更改。

**数据/接口规则**：analysis_samples.included 属于 analysis × replicate；不能是 DataAsset 全局字段；需追加输入/结果版本关系，迁移兼容 S1–S7 的已有测试仓库。

**机器验证（自动化，逐项可判定）**

- [ ] M1: 同一 n=6 组分析 A 纳入5、分析 B 纳入6，互不影响。
- [ ] M2: 复制 Raw 不改变两次统计 sample count；非法跨 assay 拒绝。
- [ ] M3: 冻结后的输入版本不随文件新版本/移动变化。
- [ ] M4: 迁移从 Schema v1 升级成功且原资产数据不丢。

**本任务必须新增或确认的自动测试入口**（若项目没有该测试，先创建而不是跳过）：

- `cargo test --no-default-features s8_`（cwd=`apps/desktop/src-tauri`）：新增 Rust 测试函数以 s8_ 开头；必须至少有 1 个且执行数量>0。
- `npm run test:e2e:desktop -- --grep "@S8"`（cwd=`.`）：新增 Playwright 用例标题带 @S8；必须至少有 1 个且执行数量>0。

**人工验收（Windows 原生，机器不能代替）**

- [ ] H1: Windows EXE 建立两个分析，分别选择5与6个样本；重新打开后选择仍正确。
- [ ] H2: 关联统计文件版本并冻结，修改文件上下文后引用无漂移。
- [ ] H3: 人工核对 n 与统计纳入清单，与生物学重复数量区分。

**交付件**：分析 CRUD 与 version snapshot；Schema 增量迁移 + 回归测试；Windows n/冻结验收。

**明确不做**：不自动执行 t 检验/ANOVA 或分析数值；不读专用实验格式算图。

**安全与退出边界**：每次统计的 included 与输入版本为关系级语义；不得通过文件级 boolean 全局表达。

**状态迁移**：原型/待开始 → 开发中 → 机器通过 → 人工待验收 → 验收通过；如果依赖或安全阻塞，改为 `blocked` 并写明原因。

### S9｜论文组图与固定版本关联

**任务元数据**：`id=S9` · `priority=P1` · `depends_on=S7` · `status=prototype_only` · Issues：[#18](https://github.com/Devil-Jian/data-m/issues/18), [#14](https://github.com/Devil-Jian/data-m/issues/14)。

**当前事实**：已有 Paper/Figure/Panel 示意 UI 与 schema；尚无真实 CRUD 和 Panel 版本关联。

**用户结果**：创建 Paper/Figure/Panel，绑定多个展示、统计和来源版本，重启和重命名后仍准确。

**前端范围**：论文树、Figure/Panel 创建排序、重命名、关联文件选取器；展示每条关联用途和固定版本，不误将最新文件版本覆盖旧引用。

**后端范围**：论文结构 CRUD/顺序事务；PanelAssetVersion 与代表来源用例；锁定 AssetVersionId，验证跨 Project/Assay 合法性，重复关联去重。

**数据/接口规则**：papers/figures/panels/panel_asset_versions/representative_sources；引用标识不能依赖易变磁盘路径或 asset display_name。

**机器验证（自动化，逐项可判定）**

- [ ] M1: 创建1 Paper、2 Figure、每张3 Panel，各 Panel 多文件版本关联并重启一致。
- [ ] M2: 把被引用 DataAsset 重命名/逻辑移动后 Panel 版本不变。
- [ ] M3: 资产新增 v2 不改变既有 Panel 钉住的 v1。
- [ ] M4: 非法/重复引用和顺序修改具有事务测试。

**本任务必须新增或确认的自动测试入口**（若项目没有该测试，先创建而不是跳过）：

- `cargo test --no-default-features s9_`（cwd=`apps/desktop/src-tauri`）：新增 Rust 测试函数以 s9_ 开头；必须至少有 1 个且执行数量>0。
- `npm run test:e2e:desktop -- --grep "@S9"`（cwd=`.`）：新增 Playwright 用例标题带 @S9；必须至少有 1 个且执行数量>0。

**人工验收（Windows 原生，机器不能代替）**

- [ ] H1: Windows EXE 创建 Figure 3 Panel A/B/C，关联 Raw/Processed/Statistics 测试文件。
- [ ] H2: 修改关联来源的文件名称、移动其逻辑位置并重启，Panel 仍展示原版本。
- [ ] H3: 对照所选版本 SHA 与 Panel 查询结果，保留证据。

**交付件**：Paper/Figure/Panel CRUD；固定版本关联与审计；面向 Windows 的引用稳定测试。

**明确不做**：不做在线画布排版或图片编辑；不做投稿网站集成。

**安全与退出边界**：默认阻止断链操作：已引用 AssetVersion 不能被静默物理删除或替换。

**状态迁移**：原型/待开始 → 开发中 → 机器通过 → 人工待验收 → 验收通过；如果依赖或安全阻塞，改为 `blocked` 并写明原因。

### S10｜双向溯源与无损导出

**任务元数据**：`id=S10` · `priority=P1` · `depends_on=S8,S9` · `status=prototype_only` · Issues：[#19](https://github.com/Devil-Jian/data-m/issues/19), [#18](https://github.com/Devil-Jian/data-m/issues/18)。

**当前事实**：只有 Demo 示例跳转；没有跨统计/Panel/文件的真实链路查询与导出 manifest。

**用户结果**：从 Panel 查到统计输入、Replicate 和原始文件；从 Asset 找到被引用的 Paper/Panel 并无损导出。

**前端范围**：Panel 的正向来源树、资产详情的反向引用列表；选择 Figure 导出目标并展示结果/失败日志。

**后端范围**：按固定 AssetVersion 查询正向/反向依赖及审计上下文；将所选版本字节导出、计算对照哈希并写 figure-manifest.json；失败时不修改 CAS。

**数据/接口规则**：manifest 包含 AssetId/VersionId、BlobKey、sha256、Panel、统计纳入集合及 schema/导出版本；多来源不能丢项，导出名称冲突有可预测处理。

**机器验证（自动化，逐项可判定）**

- [ ] M1: 任取 Panel 可追溯到其绑定的全部统计纳入样本和原始版本。
- [ ] M2: 逆向查询结果与正向引用集合匹配；软删除/逻辑移动后不丢链。
- [ ] M3: 所有导出文件的 SHA-256 与对应 CAS 注册 Blob 一致。
- [ ] M4: 导出路径不可穿越目标目录，部分失败不返回成功宣称。

**本任务必须新增或确认的自动测试入口**（若项目没有该测试，先创建而不是跳过）：

- `cargo test --no-default-features s10_`（cwd=`apps/desktop/src-tauri`）：新增 Rust 测试函数以 s10_ 开头；必须至少有 1 个且执行数量>0。
- `npm run test:e2e:desktop -- --grep "@S10"`（cwd=`.`）：新增 Playwright 用例标题带 @S10；必须至少有 1 个且执行数量>0。

**人工验收（Windows 原生，机器不能代替）**

- [ ] H1: 在 Windows EXE 选择 Figure 3B，逐级打开统计→纳入样本→原始文件。
- [ ] H2: 从原始文件反查 Panel；随后重命名/移动再查一次。
- [ ] H3: 导出至新空文件夹，对比 manifest 与实际文件数量、名称、SHA-256。

**交付件**：溯源 API 与引用索引；Figure manifest/文件无损导出；正反一致性、路径安全、哈希对照测试。

**明确不做**：不自动拼图、排版或编辑像素；不对接期刊投稿。

**安全与退出边界**：只允许从受控文件版本导出，所有目标路径和文件名都需 Rust 验证，不允许路径穿越。

**状态迁移**：原型/待开始 → 开发中 → 机器通过 → 人工待验收 → 验收通过；如果依赖或安全阻塞，改为 `blocked` 并写明原因。

### S11｜Windows 性能安装与正式交付

**任务元数据**：`id=S11` · `priority=P0` · `depends_on=S10` · `status=prototype_only` · Issues：[#20](https://github.com/Devil-Jian/data-m/issues/20), [#21](https://github.com/Devil-Jian/data-m/issues/21), [#11](https://github.com/Devil-Jian/data-m/issues/11)。

**当前事实**：已有 10k Demo 虚拟列表与便携 EXE CI Artifact；无原生真实 10k 基准、NSIS/MSI、升级/回滚验收。

**用户结果**：在无 Node/Rust 的干净 Windows 11 安装、升级并管理/恢复科研仓库。

**前端范围**：10k 真实 SQLite 文件浏览保持响应；长任务进度/取消可见；只实现必要的缩略图/延迟加载和操作错误提示。

**后端范围**：后台任务内存有界，仓库锁及恢复状态不会破坏并发操作；schema 迁移检查、版本回退/备份恢复验证；构建正式一种安装器。

**数据/接口规则**：发布 manifest（版本、构建 SHA、schema 支持范围、校验）、Release notes；采用 NSIS 或 MSI 一种作为首发，不能将当前 bundle.active=false 的 EXE 假称安装包。

**机器验证（自动化，逐项可判定）**

- [ ] M1: 真实 SQLite 10k+ 资产、分页搜索、选择和滚动回归通过；报告测试机器硬件/数据规模/延迟分布。
- [ ] M2: 以可复现基准设定：主列表首屏可交互≤3秒；同机 p95 搜索响应≤2秒；导入时 UI 无持续>500ms 阻塞（属于目标，不是现测结果）。
- [ ] M3: 干净 Windows 11 安装器可启动且无需 Node/Rust；升级/回滚不破坏已备份和恢复的数据。
- [ ] M4: 全部 P0/P1 缺陷关闭，Release 签核与恢复报告齐全。

**本任务必须新增或确认的自动测试入口**（若项目没有该测试，先创建而不是跳过）：

- `cargo test --no-default-features s11_`（cwd=`apps/desktop/src-tauri`）：新增 Rust 测试函数以 s11_ 开头；必须至少有 1 个且执行数量>0。
- `npm run test:e2e:desktop -- --grep "@S11"`（cwd=`.`）：新增 Playwright 用例标题带 @S11；必须至少有 1 个且执行数量>0。

**人工验收（Windows 原生，机器不能代替）**

- [ ] H1: 全新 Windows 11 电脑或 VM 不安装开发工具，测试安装/启动/升级/卸载。
- [ ] H2: 验证 100%/150% DPI、文件拖放与恢复，至少一次完整论文导出闭环。
- [ ] H3: 对照真实测试仓库数据数、哈希和引用；记录机器配置、版本、操作步骤、日志。

**交付件**：正式 NSIS/MSI 安装程序与 SHA；真实 10k 性能基准及回归；Windows 升级/回退、备份恢复与 Release 审查。

**明确不做**：不做云同步/多人权限/自动 AI 推理；没有全量多格式解析的承诺。

**安全与退出边界**：不得在缺少恢复演练、人工签字或仍有 P0/P1 缺陷时宣布 v1.0 正式完成。

**状态迁移**：原型/待开始 → 开发中 → 机器通过 → 人工待验收 → 验收通过；如果依赖或安全阻塞，改为 `blocked` 并写明原因。

## 6. AI 总控 Prompt（复制到有仓库工具的开发代理）

```text
角色：你是 Data-M 项目的资深全栈工程师 / Rust 数据安全工程师 / Windows 质量负责人。
目标：在真实仓库中按 DATA_M_AI_TASKS.json 的 TASK_ID 实现一个完整前后端功能包；不能把 Demo 当作完成。

硬约束：
1. 首先检查默认分支、当前 HEAD、Draft PR、Issue、CI、未提交改动，并核对计划快照。若文件不存在，先定位，不得猜测。
2. 仅执行指定 TASK_ID；若依赖没满足，输出阻塞证据并停止，不跨包“顺手”开发。
3. 优先重用 apps/desktop、packages/ui、packages/core、packages/interaction、现有 Rust modules。不得引入第二套 UI 或无关技术栈。
4. 先确定用户场景、最小 IPC/数据契约、成功/错误码与测试不变量；然后实现后端+前端闭环。
5. 对每个机器验收 M 编写可运行的单测/集成测试/Playwright 测试；覆盖失败路径、重启持久化和必要的安全故障。
6. 实际运行相关命令，逐项保留退出码、用例数和日志；测试失败先定位修复，禁止静默跳过、篡改断言或虚构通过。
7. 严格保护源科研文件和 CAS 不可变性；只用新建的临时测试仓库。S7 未签收之前不得开启真实重要科研数据入口。
8. 提交短功能分支和 Draft PR（若授权及工具允许）；不 force push、不直接改 main、不自动合并。保留已有用户改动。
9. Windows WebView2 真实鼠标/键盘/拖放、DPI、恢复演练属于人工证据；无法亲测则标 manual_pending。
10. 出现不可恢复的数据安全问题、仓库状态冲突或无法满足依赖时停止本任务并报告；不得继续冒险。

每次执行顺序：
A. 读取本规范、JSON、仓库代码和任务相关 Issue/PR；输出不超过 12 行的事实/风险/实施计划。
B. 显示将改变的模块、接口契约与测试矩阵；实现真实代码（含可重复测试）。
C. 跑该任务新增自动用例 + 必要全局回归 + Windows CI（有环境则跑原生构建）。
D. 进行差异审查：不得影响其他功能、测试仍严格、没有新增危险文件写入 API。
E. 输出带证据的执行报告，创建 Draft PR；若无仓库写权限，给出可核对的变更补丁与阻塞信息。

输出报告必须包含：TASK_ID / base_sha / head_sha / changed_files / interface_changes
/ test_command+cwd+exit_code+executed_count+log / machine_gate / manual_gate
/ P0-P1 risks / exact remaining work / PR URL（没有就 null）。
禁止输出“全部完成”除非人工作业已实测且合并正式基线。
```

## 7. 单任务启动 Prompt（每天实际执行一次）

```text
请读取仓库 https://github.com/Devil-Jian/data-m 的 AI_DEV_EXECUTION_SPEC.md
及 DATA_M_AI_TASKS.json，并执行 TASK_ID=<S0|S1|...|S11>。
按当前仓库事实修正过时的状态；在依赖已满足时，完成这个任务包的前端、
Rust/SQLite/CAS、IPC、错误处理、单元/集成/E2E 测试。
不要只写设计稿或模拟 UI。所有声明必须附真实代码/命令/结果证据。
可以拆成小 PR，但本次仅推进这一功能包，不更改其他模块的目标。
Windows 原生手工验收无法做时输出逐步操作清单并设置 manual_pending。
结束时更新任务状态并按文档第8节报告。
```

## 8. AI 执行结果结构与审查 Prompt

`DATA_M_AI_TASKS.json` 内 `execution_report_schema` 定义最少返回字段；建议每次执行保存 `docs/dev-reports/<task-id>-<YYYYMMDD>-<headsha7>.json`。示例：

```json
{
  "task_id": "S3",
  "base_sha": "<git base sha>",
  "head_sha": "<git head sha>",
  "status": "manual_pending",
  "pr_url": "<draft PR URL or null>",
  "machine_checks": [
    {
      "command": "cargo test --no-default-features s3_",
      "cwd": "apps/desktop/src-tauri",
      "exit_code": 0,
      "tests_executed": 12,
      "log_url": "<CI URL or checked log>"
    }
  ],
  "manual_checks": [
    {
      "step": "Windows 11 Explorer 拖入并核对源哈希",
      "status": "not_run",
      "evidence": null,
      "signoff_by": null
    }
  ],
  "residual_risks": [
    "真实 Windows 文件操作未做人工验收"
  ],
  "next_task_recommendation": "先完成本任务人工验收，不启动下游"
}
```

**独立代码审查 Prompt：**

```text
请审查 TASK_ID 的 Draft PR。先读取该 Task 的机器/人工验收标准与风险门槛。
逐项检查：数据不变量、IPC 安全、事务/崩溃恢复、错误分支、迁移兼容性、
测试是否真正执行且有足够断言、目标 SHA 与 CI 是否一致、是否偷偷使用 Demo。
只根据可验证证据给出 PASS / FAIL / MANUAL_PENDING；列出 blocker、
建议的最小修复及复现步骤。CI 绿色不能替代 Windows WebView2 人工验收。
不可声称未做的测试已完成，不可自动合并。
```

## 9. 人工验收模板（填写后才能 accepted）

```yaml
task_id: S0
tested_head_sha: "<sha>"
windows_version: "Windows 11 <build>"
artifact_sha256: "<sha256>"
tester: "<manual reviewer>"
tested_on: "YYYY-MM-DD"
manual_checks:
  - case: H1
    result: pass  # pass | fail | not_run
    evidence: "<screenshot_or_log_path>"
blocking_defects: []
approved_to_merge: false  # 必须人工明确签收
```

## 10. 任务完成的标准化发布闸口

| 判定 | 条件 | AI 可以自行标记？ |
|---|---|---|
| `manual_pending` | 已实现部分功能或已有原型但未全测 | 是 |
| `machine_passed` | task test + 相关全局测试通过；日志/非零用例数/sha 齐全 | 是，必须有证据 |
| `manual_pending` | 自动化通过，缺少真实 Windows 人工截图/签字 | 是 |
| `accepted` | 合并已批准的正式主线 + 自动化 + 人工签收齐全 | 只能依据实际签收证据报告，不得自行代签 |
| `blocked` | 有安全/依赖/环境风险无法满足 | 是，写明 blocker |

**终局验收**：从空仓库创建项目/三组×n=6 → 导入测试 Raw → 文件复制移动与恢复 → 两次不同 n 的统计分析 → Paper/Figure/Panel 固定版本 → 正反溯源 → 无损导出 → 备份/空目录恢复 → 在干净 Windows 11 安装并复核。任何一步失败均不能称为 v1.0 完成。

---

**维护约定**：本规范专管 AI 可执行功能与验收；视觉 PRD/产品叙述保留在原文档，不再在此重复。状态只因真实代码/PR/CI/手工证据更新。若新需求超出某任务范围，应新开 Issue 和下一版本计划，而不是扩大正在执行的任务包。
