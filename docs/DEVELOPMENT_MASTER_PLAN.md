# Data-M 产品需求、开发进度、后续计划与验收基线

> S1 当前为 `manual_pending`：PR #44 的项目/主题/实验持久化已通过 Windows/Linux 机器验证，未人工签收、未合并；证据与人工清单见 [S1 报告](S1_EXECUTION_REPORT.md)。

> S0 最新执行状态：`accepted`；PR #42 已通过机器验证、用户人工签收并合并 main，证据见 [S0 执行报告](S0_EXECUTION_REPORT.md)。S1 可从该主线开始；下文保留历史快照。

> 文档版本：v1.0（2026-11-15，开发状态快照）  
> 产品：Data-M —— Windows 单机科研实验数据与论文图表资产管理系统  
> 唯一目标技术栈：React 19 + TypeScript + shadcn 风格组件（Radix/Tailwind）+ Tauri 2 + Rust + SQLite + SHA-256 CAS  
> 本文性质：**产品和工程执行基线**，不是已发布软件的功能说明。由 PRD v0.1、ROADMAP、TEST_PLAN、IPC_API、各 P1/P2 实施说明、Issues 与 CI 状态整理；未实装内容全部按“计划”描述。  
> **统计时点：2026-11-15；后续更新必须刷新 PR/Issue/CI 状态并记录修订日期。**

## 0. 结论与状态边界

Data-M 已经有可运行的 React 科研工作台、Tauri 2 Windows 桌面外壳、完善的演示资产交互和初步 SQLite/CAS 基础模块，但尚无**可用于真实科研原始文件的正式归档流程**，也尚无 Windows Installer 或 v1.0 发布版本。

当前最新集成候选为 [Draft PR #40](https://github.com/Devil-Jian/data-m/pull/40)，分支为 **refactor/p1-single-ui-package**。2026-11-15 最新的两条 CI 均成功：

- [Windows desktop shell · #43710969361](https://github.com/Devil-Jian/data-m/actions/runs/43710969361)：TypeScript/Vite 构建、交互测试、Windows Chromium E2E、桌面渲染器 CSS/快捷键 E2E、Rust 测试/格式/检查、Windows 可执行程序构建和 Artifact 上传成功。
- [Build UI demo variants · #43710969358](https://github.com/Devil-Jian/data-m/actions/runs/43710969358)：shadcn/Mantine/Fluent 构建及现有浏览器交互测试成功。
- 上述 CI 结果**不等于**已在真实 Windows 11 WebView2 窗口完成鼠标、文件拖放、DPI、缩放与持久数据安全验收。

**重要仓库状态：** PR #22–#40 共 19 个仍是 Open/Draft；其中 #35 是与 #36–#40 路线并行的修复分支，并非已经统一合入。**main 仍沿用旧的 Tauri/Vue 3 初始规划**，选定的 React/Tauri 正式路线尚未通过审查并合并到 main。原来的 WPF/WinUI 分支为隔离试验，不并入本路线。

以下完成度仅为**主观的剩余工作量估算**，不是测试通过率、Issue 关闭率或承诺进度：P1 ~75%、P2 ~20%、P3 ~5%、P4 ~5%，整体约 25%–30%。四个 Epic #8–#11 和相关工作 Issue 均仍 Open。**验收状态以实际证据为准，百分比不能作为放行依据。**

### 0.1 统一状态词典

| 状态 | 定义 | 能否标记完成 |
|---|---|---|
| 规划中 | 已有需求/接口或 UI 草图，尚无符合要求的实现 | 否 |
| 演示实现 | 仅模拟数据、内存状态或操作意图，未对真实文件/数据库写入 | 否 |
| 原型实现 | 已有可运行 Rust/SQLite 代码及单元测试，未接正式 IPC/事务/恢复 | 否 |
| CI 已通过 | 自动构建及相关测试通过，但尚无真实 Windows/数据验收 | 否 |
| 验收通过 | Issue 验收清单逐项签署、Windows 实测/故障注入/证据完整、PR 经审查合并 | 是 |

项目应同时记录 **代码状态、CI 状态、原生验收状态、正式发布状态**，禁止以单一“完成百分比”代替。

## 1. 产品目标、用户与范围

### 1.1 要解决的问题

科研实验图像、显微数据、统计源文件和论文图片通常散落在不同磁盘目录中，传统资源管理器只认识文件路径，不理解实验分组、生物学重复、统计纳入、组图来源和论文版本。Data-M 的目的，是在 **不破坏原始文件** 的前提下，把“文件资源管理器交互”与“科研实验语义”和“论文出处”结合起来。

核心目标：

1. 像 Windows Explorer 一样选中文件、框选、Ctrl/Shift 多选、拖放和使用快捷键。
2. 在 Project → Theme → Assay → Group → Biological Replicate 五级层级下组织实验。
3. 以 01_Raw、02_Processed、03_Statistics、04_Display 四阶段组织文件资产与结果。
4. 以 Paper → Figure → Panel 组织论文图表，支持一个 Panel 多个展示源和统计依据。
5. 固定不可变 AssetVersion；逻辑移动/重命名、文件复制、版本升级不破坏论文引用，不误增统计生物学样本数。
6. 在 Windows 11 上离线独立运行，提供审计、校验、备份与恢复。

### 1.2 目标用户与支持边界

- 首发目标：**Windows 11 单机单用户**；EXE 运行时不依赖 Node、浏览器或在线服务器。
- 数据：TIFF、常规显微图像、统计表格及其他科研附件；可以存储大文件，但**不承诺**在 v1.0 深度解析所有 TIFF/SVS/CZI 等专有格式。
- 仓库：本地 NTFS 文件系统优先；外接盘、共享目录、符号链接/重解析点需要明确路径及故障测试，未验收的文件系统类型不得宣称完整支持。
- v1.0 不承诺：云同步、多人实时协作、权限服务器、Windows Shell 全功能替换、AI 解析/自动分析、复杂医学或显微格式的全量渲染、原生拖出至 Windows Explorer（另立后续任务）。
- 数据安全优先级高于界面丰富度和开发速度。

## 2. 单一正式前端架构与开发流程

### 2.1 唯一代码来源

~~~text
apps/desktop/
  src/main.tsx                 正式 React 入口（浏览器和 EXE 同一份）
  src/desktop.css             桌面增量样式
  src-tauri/                 Tauri 2、Rust、SQLite/CAS
packages/
  ui/src/                   唯一 shadcn 组件、样式和 shadcnUI 适配器
  core/src/                 业务 Workbench、领域展示、演示数据
  interaction/src/          Explorer 选择/键盘/拖拽状态机
demos/
  shadcn/                   旧演示入口，复用 @data-m/ui，不维护第二套组件
  mantine/、fluent/         历史技术选型对照，非正式交付目标
~~~

- 浏览器预览**不等于**第二套 UI：正式 React 入口为 apps/desktop，Vite 和 Tauri WebView2 读取同一套组件。
- 原有 5173 shadcn 演示仅作历史比较；**日常开发以 5176 为准**。
- **@data-m/ui** 已在 PR #40 集中管理 Button/Select/Checkbox/Input/Badge/Tailwind 样式；删除演示目录重复组件，解决双重维护。
- React 只提交**类型化操作意图**，不能执行任意 SQL、写盘、删除用户文件。Rust 应用层决定真正是否可以提交。

### 2.2 最便捷的本地工作流（已确认）

Windows PowerShell，在项目根目录运行：

~~~powershell
git fetch origin
git switch --track origin/refactor/p1-single-ui-package
npm install
npm run dev:desktop
~~~

如果分支已存在，应先 git status 并使用 git switch 进入对应分支，不重复创建本地跟踪分支。打开 **http://127.0.0.1:5176**。

- 日常 React/CSS 调整：Vite HMR 自动刷新；**不用安装 Rust，不用每次编译 EXE**。
- 前端检查：npm run build:desktop、npm run test:desktop、npm run test:e2e:desktop；必要时再运行 npm run test:e2e。
- 用户本地出现的 cargo metadata / program not found，来自运行 tauri dev 时没有可用的 Rust 工具链；**不影响 5176 浏览器开发**。
- 原生 WebView2、外部 Windows 文件拖放与 DPI：由具备 Rust/MSVC 环境的验证机器或 CI 产出的便携 EXE 进行**阶段性人工验证**。
- 最终打包：npm run tauri:build 或交由 GitHub Actions 的 Windows Runner。当前 tauri.conf.json 的 bundle.active=false，生成的是**未签名便携 EXE，不是正式 NSIS/MSI 安装器**。
- CI Artifact 默认短期保留（例如该次 EXE Artifact 7 天），不应当作永久发布渠道。

### 2.3 UI 设计与交互统一标准

界面保留：深色顶部导航、左侧实验树、中央文件/图表工作区、状态栏、实验整理与论文管理双模块。提供全阶段矩阵和单阶段 Grid/List/Table 视图。任何页面都应使用统一组件库和状态/提示风格。

- Ctrl、Shift、反向框选、框选滚动、跨虚拟列表焦点、拖拽到实验分组；文件选区基于 AssetId 而不是 ReplicateId。
- 右键**文件资产**打开文件菜单；右键非文件工具栏可保持浏览器系统菜单。Shift+F10 / ContextMenu 等键只在 Explorer 获得焦点时生效；搜索框输入不能被文件快捷键拦截。
- F2、复制、剪切、Delete 目前只显示演示意图/重命名预览；不能对用户声称已修改真实文件。
- Windows 原生 DragDropEvent 目前只读检查拖入对象的类型/名称/大小，绝不在 P1 意外启用真实导入。
- 需要分别对浏览器与原生 WebView2 进行可访问性、缩放、滚动、右键、键盘和 DPI 验收（建议记录 100%、125%、150% 显示缩放的截图与操作结果）。

## 3. 领域数据模型与必须永远成立的不变量

~~~text
Project → Theme → Assay → ExperimentGroup → Replicate
                               │
                               └─ AssetLink ─ DataAsset → AssetVersion → Blob(SHA-256)
Assay → StatisticalAnalysis → AnalysisSample(included) → Replicate
Paper → Figure → Panel → PanelAssetVersion → AssetVersion
                     └─ RepresentativeSource → Replicate / AssetVersion
~~~

**必须遵守的规则：**

- **Replicate 是生物学重复；DataAsset 是文件资产。** 复制或移动一个文件不得增加 Replicate，也不得改变分析的生物学样本量 n。
- **AssetId、AssetVersionId、BlobKey 不可混淆。** Blob 按 SHA-256 去重；逻辑文件可以产生多个不可变版本；展示或论文引用的是稳定版本，不是绝对路径。
- **四阶段是文件角色/关联，不是单纯在磁盘上重命名文件。** 同一 AssetVersion 可以在多个允许的实验上下文被引用，但不得造成双计数。
- **统计纳入**属于 AnalysisSample → Replicate 的**某次分析**关系；**代表图来源**属于 Panel → Replicate / AssetVersion 关系，不能仅用全局 inStats、isRepresentative 标志记录。
- 已冻结的统计结果和 Panel 引用不自动追踪“最新文件”；逻辑移动/重命名不得断链。
- Raw 入库原始字节不可改写；如需修改内容，应创建新 AssetVersion。
- 物理删除之前必须校验引用；先软删除，回收和 Blob GC 需要独立安全设计、版本引用检查和审计。

**已实现的基础：** [PR #36](https://github.com/Devil-Jian/data-m/pull/36) 提供 Schema v1、约束、不可变触发器、空库初始化和临时 SQLite 测试。**尚未实现**正式文件数据库 Repository、现有仓库升级/迁移、生产用户数据库打开与 SQLite/文件系统协调服务。

## 4. 完整功能需求矩阵

状态说明：**演示**=屏幕可操作但只改会话/示意数据；**原型**=Rust 或 SQLite 测试代码；**待建**=尚未满足正式业务能力；**CI 通过**不等于验收。

| 编号 | 功能与预期行为 | 目前证据/状态 | 主要后续工作 | 验收边界 |
|---|---|---|---|---|
| UI-01 | 顶部导航、项目选择、两大模块切换 | 演示；PR #23–#40 | 原生窗口、视觉/DPI 验收 | 5176/EXE 同款布局 |
| UI-02 | 五级实验树、分组与样本导航 | 演示 | 实体 CRUD/SQLite 查询 | 重启后数据仍在 |
| UI-03 | Raw/Processed/Statistics/Display 全景和阶段视图 | 演示 | 接真实 AssetLink 查询 | 文件与样本关系正确 |
| UI-04 | Grid/List/Table、筛选、搜索 | 演示、CI 通过 | 大数据查询、持久选择语义 | 不出现数据错位 |
| UI-05 | Ctrl/Shift/框选/拖拽多文件 | 演示、CI 通过 | WebView2 手工验收+真实操作路由 | 无误选/误删/跨域操作 |
| UI-06 | 10k 虚拟 Asset 列表与跨屏键盘导航 | 演示、CI 通过 | 性能基准与真实分页 | 不一次加载 10k 大文件 |
| UI-07 | 右键菜单、F2、Ctrl+C/X/V、Delete | 演示、CI 通过 | 真实冲突提示、剪贴板和撤销/恢复 | 搜索框不被劫持 |
| UI-08 | Windows Explorer 外部文件拖入 | 只读预览实现 | 安全确认→导入队列 | 未确认绝不写仓库 |
| UI-09 | 统一 shadcn UI、单一 Tailwind 入口 | PR #40、CI 通过 | Windows WebView2 视觉验收 | 不丢失按钮/输入框样式 |
| ST-01 | Project/Theme/Assay/Group/Replicate Schema | SQLite 原型 | Repository/迁移/正式查询 | FK/唯一性/层级正确 |
| ST-02 | DataAsset/Version/Blob/AssetLink | SQLite 原型 | API、权限、读写事务 | 版本不可变，复制不增 n |
| ST-03 | 流式 SHA-256/CAS staging、去重 | Rust 原型 PR #37 | 路径安全、锁、协调事务、故障恢复 | 原始源文件字节不变 |
| ST-04 | 导入队列、取消、重试、进度 | 待建 | Rust 后台任务 + UI IPC | 可取消、异常后可恢复 |
| ST-05 | 复制/逻辑移动/重命名/软删除 | 演示意图，正式服务待建 | 引用检查、冲突策略、审计 | 已引用版本不丢失 |
| ST-06 | SQLite/CAS 只读一致性校验 | Rust 原型 PR #38 | 可控仓库扫描、孤儿检查、恢复 | 不修改损坏对象 |
| ST-07 | WAL-safe 备份、恢复、迁移回滚 | 待建 | 全量/增量快照与恢复演练 | 新环境恢复一致 |
| RS-01 | 实验样本与统计纳入管理 | 演示 | AnalysisSample 正式事务 | 统计 n 仅由纳入集合决定 |
| RS-02 | Paper/Figure/Panel CRUD | 演示 | 持久实体与排序/版本 | 数据重启仍存在 |
| RS-03 | Panel 多来源、统计和代表图关联 | 演示 + Schema | 真实 AssetVersion 关联 | 多文件、版本稳定 |
| RS-04 | 论文 ↔ 实验双向溯源 | 演示跳转 | 持久引用查询/审计 | 正反向关系一致 |
| RS-05 | Figure/Panel 文件与 manifest 无损导出 | 演示 CSV | 导出真正字节及固定版本清单 | SHA-256 不变 |
| QA-01 | 大文件/10k 资产性能、缩略图 | 部分虚拟列表 | 后台哈希、缩略图缓存、压力测试 | 可复现的 Windows 基准 |
| QA-02 | Windows Installer、升级/卸载/回滚 | 未开始；仅 unsigned EXE | NSIS 或 MSI、签名策略、恢复 | 干净 Win11 验收 |
| QA-03 | 发布审计、Release Notes、v1.0.0 | 未开始 | 风险关闭、数据恢复签字 | 无阻断 P0/P1 |

## 5. 现有实现、PR、CI 与已知分支风险

### 5.1 可验证交付（截至本版）

- **P1 交互链：** Draft PR #23、#25–#34：Tauri 桌面外壳、资产 ID 演示分离、框选/自动滚动、真实 TanStack Virtual 虚拟列表、键盘导航、右键菜单、目录历史、F2 预览。自动化 E2E 通过；Windows 真机原生验收缺失。
- **P1 UI 收敛：** [PR #39](https://github.com/Devil-Jian/data-m/pull/39) 提供 5176 预览和跨 workspace 样式扫描；[PR #40](https://github.com/Devil-Jian/data-m/pull/40) 将组件与样式集中到 packages/ui，修复 Tailwind v4 OKLCH 颜色测试假失败。**#40 的 Windows 和 UI CI 均成功**。
- **P2 #14：** [PR #36](https://github.com/Devil-Jian/data-m/pull/36) 提供 SQLite Schema v1；[CI #43693009429](https://github.com/Devil-Jian/data-m/actions/runs/43693009429) 通过。
- **P2 #6/#15：** [PR #37](https://github.com/Devil-Jian/data-m/pull/37) 提供 Rust 隔离的 CAS 流式复制原型：绝对路径普通文件、安全 staging、SHA-256、no-clobber、重复文件内容验证、异常不覆盖损坏目标；其早期独立 PR CI 失败的 Windows 1 MiB 测试栈溢出已改为堆缓冲，并在下游 #38/#40 成功 CI 中得到验证。**不能因为下游 CI 通过就认为生产导入已经安全**。
- **P2 #16：** [PR #38](https://github.com/Devil-Jian/data-m/pull/38) 提供只读 SQLite integrity_check/foreign_key_check 和 CAS Missing/WrongSize/HashMismatch/UnsafePath 检测；[CI #43698395373](https://github.com/Devil-Jian/data-m/actions/runs/43698395373) 通过。未实现 WAL-safe 备份/恢复和孤儿文件 reconcile。
- **P3：** 当前 Paper/Figure/Panel、统计纳入、代表性来源和双向跳转主要是模拟数据/前端演示；数据库包含部分关系结构，**无正式用户可用 CRUD/查询服务**。
- **P4：** Windows Runner 可生成未签名便携 EXE Artifact。**非正式安装包**，尚无干净 Windows 11 安装/升级/恢复/持续稳定性签字。

### 5.2 对旧分支/旧说明的处理

1. **不能把 PR #40 直接当作 main 已具备的功能。** 现阶段 19 个新路线 PR #22–#40 仍为 Draft/Open，合并前应建立可审查的集成顺序。
2. PR #35（原生拖入 Unicode 名称安全）属于**独立兄弟分支**，曾有失败 CI；必须审查其必要修复并整合或明确废弃，不可静默遗漏。
3. PR #24 所含 PRD/TEST_PLAN/IPC_API 在 docs/product-prd-v1 分支，**不是保证已进入 #40 的正式发行文档**；需要纳入最终文档主线。
4. 仓库部分 README/ARCHITECTURE 仍描述旧的 UI 比选或 Vue/Tauri 初始状态；主路线收敛后应更新，以免用户误用 5173 或错误技术栈。
5. WPF、WinUI 历史试验分支与所选 React/Tauri 路线完全隔离；未经专项评估不得混合合并。

## 6. 后续开发任务安排（有依赖、有交付、有验收）

建议按 **G0 → G1 → G2 → G3 → G4** 作为里程碑，不以时间估计或 PR 数量替代实际验收。

### G0 · 工程整合与 P1 原生验收（最高优先级）

| 顺序 | 对应任务 | 必须交付 | 放行证据 |
|---|---|---|---|
| G0-1 | #12、PR #22–#24、#40 | PRD/ADR/IPC/TEST_PLAN 与新 UI 架构同一基线；整理堆叠 PR | 依赖关系清单、审查记录、绿色 CI |
| G0-2 | #35 与 #30 冲突评估 | Unicode 名称、Windows 重解析点和拖入保护修复归一 | Windows 真机拖入及源完整性报告 |
| G0-3 | #4/#5/#7/#13 | 5176/EXE 完整样式，右键/键盘/框选/10k 虚拟列表、焦点作用域 | 100%/125%/150% DPI 截图和操作清单 |
| G0-4 | 合并门禁 | 将 React/Tauri 新路线在受控审查后逐步合入 main | 无未处理 PR 冲突，main 上 CI 通过 |

**P1 退出条件：** EXE 可脱离开发服务器运行；浏览器与 WebView2 不丢失主要 UI 装饰；文件操作演示逻辑的鼠标/键盘行为一致；原生外部拖入只读检查不会改变源文件。**P1 不允许宣称真实导入可用。**

### G1 · P2 数据层安全闭环（阻断真实数据使用）

| 顺序 | 对应 Issue | 开发任务 | 验收证据 |
|---|---|---|---|
| G1-1 | #14 | 实现 Repository 接口、数据库创建/打开策略、schema 版本及前向迁移；文件路径/目录策略 | 临时库、迁移幂等与未知库拒绝 |
| G1-2 | #6 | 完善 CAS 路径与并发安全、原子完成、Blob 注册协议、事务协调；文件变更与对象校验 | 失败注入、源文件 SHA 不变 |
| G1-3 | #15 | import_jobs 状态机、后台进度/取消/重试、冲突预检、staging/finalize/reconcile | 取消/断电/磁盘满/锁定文件恢复 |
| G1-4 | #6/#15 | 逻辑移动、复制、重命名、软删除与引用检查，受控 Tauri IPC | 无样本数变化、无悬空版本/引用 |
| G1-5 | #16 | SQLite WAL-safe 备份、Blob manifest、恢复、孤儿对象检查及恢复演练 | 新目录/新机器还原后校验一致 |
| G1-6 | 集成 | 用**独立生成的测试素材**在真实 Windows WebView2 连接 UI 与 Rust 服务 | 真实文件 E2E、全链故障报告 |

**P2 退出条件：** 真实数据操作默认安全、可回滚或明确可恢复；导入/移动/删除不损坏原始源文件与引用；SQLite 和 CAS 在失败后可重建一致状态；备份可从独立环境恢复。未达标前**真实科研目录继续禁用写入**。

### G2 · P3 科研实验与论文闭环

| 顺序 | 对应 Issue | 交付功能 | 最低业务验收 |
|---|---|---|---|
| G2-1 | #17 | 项目/主题/实验/分组/生物学重复 CRUD；四阶段真实资产归档 | 空仓库创建多组 n=6 实验，导入后 n 不随文件复制增加 |
| G2-2 | #17 | 统计分析及 AnalysisSample 选择、历史版本 | 一次统计只纳入指定 5/6 Replicate，可查依据 |
| G2-3 | #18 | Paper/Figure/Panel CRUD、多源 AssetVersion 关联、代表来源 | 多个 Panel 固定版本，移动/重命名不掉链 |
| G2-4 | #19 | Panel → Display/Statistics → Processed/Raw/Replicate 正向，以及资产 → Panel 反向溯源 | 随机 Panel 可查全部样本与源版本 |
| G2-5 | #18/#19 | 发布冻结、无损导出 Figure 素材与 manifest | SHA 与版本一致，重建导出结果可解释 |

**P3 退出条件：** 从一个空数据仓库开始，完成“项目 → 实验 → 生物学重复 → 原始/处理文件 → 统计 → Panel → 无损导出 → 反向溯源”全流程 E2E；重启软件和逻辑移动后引用保持正确。

### G3 · P4 稳定性、性能、安全和 Windows 正式打包

| 顺序 | 对应 Issue | 交付任务 | 验收证据 |
|---|---|---|---|
| G3-1 | #20 | 10k+ 真实记录浏览、懒加载、缩略图/缓存、后台哈希隔离 | 固定测试机器与数据集的可复现基准 |
| G3-2 | #20 | 批量导入、取消、重试和长期运行压力测试 | 内存/CPU/响应性与错误日志 |
| G3-3 | #21 | 首发 NSIS 或 MSI 安装器、干净 Win11 安装/卸载 | 无 Node/Rust 环境运行证明 |
| G3-4 | #21/#16 | 跨版本 Schema/配置迁移、升级失败回退、备份恢复 | 冷启动升级与恢复演练记录 |
| G3-5 | #21 | 依赖安全审计、权限最小化、Release Notes、版本签署 | P0/P1 缺陷清零、完整发布清单 |

**P4 退出条件：** 可安装、可升级/回退、可恢复、性能有实测依据、没有阻断缺陷，才考虑发布 v1.0.0。

### G4 · v1.0 发布决策

须同时满足所有 Gate：G0–G3 验收、用户确认 UI、原始源保护与备份恢复实际演练完成、无已知 P0/P1、main CI 通过、Windows Release/安装产物校验通过。所有条件未达成前仅能发布 Preview/Alpha，不能标识为“正式科研资产管理软件”。

## 7. 安全门禁、失败注入与不可越过的红线

### 7.1 文件安全红线（P0）

- 禁止使用用户源路径作为可写目标；外部导入默认**复制**，原始文件大小/mtime/SHA-256 前后可校验。
- 禁止在没有事务协调、源文件保护、冲突检查、错误恢复和测试之前开放真实移动/删除。
- CAS 已有对象绝不因同名或“去重”覆盖；损坏必须报告、隔离并提供人工可审计恢复方案。
- 不能把 SQLite COMMIT 解释成文件持久化完成；必须明确 staging → 校验 → finalize → DB 登记 → reconcile 的失败状态及重试幂等语义。
- 未确认文件实际存在且校验通过时不能写入“成功导入”状态；任何任务中断都不能留下被视为成功的悬空 AssetVersion。
- 物理 GC 只能在引用锁、版本保护和备份方案通过验证后启用；软删除不等于可直接删除 Blob。
- 不把任意 JS/React 参数映射为 SQL 或任意 Windows 文件路径；跨目录、符号链接与重解析点必须在 Rust 层验证。

### 7.2 强制故障场景

必须使用**临时数据目录**进行自动化和实机失败注入，并留证据：

| 测试 | 预期结果 |
|---|---|
| 重复内容、不同文件名导入 | 不覆盖已存在 Blob；版本与关联按规则建立 |
| 同组同阶段大小写不同的同名冲突 | 明确冲突策略，不悄悄覆盖 |
| 源文件在复制过程中变更/锁定 | 拒绝不可信内容，源文件不被写入 |
| staging 期间取消/断电 | 任务可识别并恢复，临时文件可审计 |
| CAS finalize 后 DB 写入失败 | 不出现永久“成功版本”悬空记录，reconcile 能解释孤儿 |
| 磁盘空间不足、权限拒绝 | 任务失败可见、无文件破坏、可重试 |
| CAS 文件损坏或缺失 | 校验报告明确，不能静默再生成替代版本 |
| 被多个 Panel 引用的文件逻辑移动/重命名 | 所有 Panel 引用仍指向正确版本 |
| 被引用的 AssetVersion 软删除/回收 | 引用保护有效，不得静默物理删除 |
| WAL 数据库备份、恢复到另一目录 | 数据、Blob、Panel、分析关系一致 |
| 10k 条真实 Asset + 异步批量导入 | UI 可交互，内存有边界，错误可回溯 |

### 7.3 重要未完成风险

| 风险 | 当前事实 | 后续处置 |
|---|---|---|
| 深度堆叠 Draft PR | #22–#40 尚未正式整合 | 先整理审查、基线、冲突与分支依赖 |
| 独立 #35 修复可能遗漏 | 不在 #36–#40 主线祖先上 | 单独核验并选择性合入 |
| 原生 WebView2 与浏览器差异 | 自动 CI 通过但无实机 DPI/拖入签字 | G0 原生验收 |
| P2 CAS 原型存在路径竞争/协调缺口 | 无正式仓库事务与恢复 | G1 文件安全门禁 |
| SQLite 备份/恢复缺失 | #16 仅只读审计原型 | G1 WAL-safe 备份与恢复 |
| P3 仅模拟数据 | 真正的 CRUD、Panel 多源仍未落地 | G2 正式业务 API |
| 旧 README/PRD 分支分散 | 内容不完全符合当前选定代码 | G0 文档收敛 |
| 安装器未完成 | CI 只提供 unsigned EXE | G3 NSIS/MSI 和干净环境验证 |

## 8. 验收矩阵与签字规则

四类证据必须分开保存：

**A. 代码/自动化**：TypeScript 构建、Vitest、Rust 单元与格式/检查、Playwright Browser E2E、Windows EXE 构建、产物 SHA/日志。  
**B. Windows 原生交互**：Windows 11 WebView2 独立窗口、鼠标/右键/键盘、外部 Explorer 拖放、100%/125%/150% DPI、中文/Unicode 文件名、关闭重开。  
**C. 真实数据安全**：测试用真实字节文件的 SHA、mtime、版本关联、事务中断、磁盘满、恢复/备份校验、论文引用完整性。  
**D. 用户业务验收**：完整 n=6 实验、统计纳入 n=5、组图多来源、正反溯源、无损导出、离线启动与重启保留数据。

**严格的 Definition of Done：**

1. Issue 验收标准逐条有证据和复现步骤。
2. 数据不变量有自动化单元/集成/故障测试。
3. TypeScript/Rust 构建、格式、相关 CI 全绿。
4. Windows 真机需要实测的功能有版本、环境、截图/录像、结果和测试人记录。
5. 代码经过评审，PR 风险、迁移和回滚说明完整。
6. 不存在未处理的 P0（数据安全/损坏）或阻断 P1 问题。
7. 已经**按依赖顺序合并到目标正式分支**，或在状态中明确仍是 Draft；未合并不得标记已发布。

建议增设可复制的验收记录字段：日期、OS build、Windows 缩放、分支/SHA、数据集路径（脱敏）、测试项目、期望、实际、截图或 CI 链接、问题编号、结果、签字人。

## 9. 版本与发布边界

| 交付类别 | 允许宣称 | 禁止宣称 |
|---|---|---|
| UI Demo / Browser Preview | 演示实验树、论文界面、文件交互和模拟数据 | 真实科研资产已入库 |
| P1 Portable EXE / Alpha | 可在 Windows 打开并展示桌面交互；可只读预览拖入元数据 | 正式安装、真实安全导入、可恢复仓库 |
| P2 Internal Test | 在隔离临时目录进行文件导入、校验、恢复实验 | 未审计就用于唯一的真实原始文件 |
| P3 Beta | 全部科研业务链在验收测试仓库跑通 | 未通过备份恢复仍可放心生产 |
| P4 v1.0.0 | 完成安装、迁移、恢复、性能与正式验收后可交付 | 已知 P0/P1 缺陷存在时仍称稳定 |

正式 v1.0 发布前需固定版本号、Release Notes、产物 SHA-256、依赖审计报告、兼容/升级说明、数据备份指导和验收证据索引。

## 10. 研发制度、通知策略与操作指令

**研发节奏：** Issue → 设计/ADR → 独立分支 → 实现及测试 → Draft PR → CI → Windows 人工/安全验收 → Review → Merge → Release。优先一项可独立验收的小改动，避免长期将多层 PR 叠在未验证基线上。

**GitHub 通知：** CI 自动运行并留在 GitHub Actions。默认不主动逐次发送邮件或单次开发汇报；用户自行在 GitHub Notifications → Actions 关闭 Email（保留站内通知和 CI），仅在重要里程碑形成汇总。**关闭邮件不等于关闭 CI。**

**常用开发命令（位于最新 React/Tauri 分支仓库根目录）：**

~~~powershell
npm install
npm run dev:desktop           # http://127.0.0.1:5176；日常唯一正式预览
npm run build:desktop         # 前端 TypeScript/Vite 检查，不编译 Rust
npm run test:desktop          # 桌面前端单元测试
npm run test:e2e:desktop      # 5176 桌面渲染器 CSS/快捷键
npm run test:e2e              # 5173 历史 shadcn 交互回归
# 以下由具备 Rust/MSVC 的 Windows 环境或 CI 执行：
npm run tauri:build           # 生成未签名便携 EXE；目前无安装器
~~~

本地 PowerShell 没有 cargo 时不要反复执行 tauri:dev；日常 UI 预览不需要 Rust。Windows 真实验收可下载 GitHub Actions 的临时 EXE Artifact，但临时 Artifact 过期后需要重新构建。

## 11. 下一次开发的首要可执行任务（按顺序）

**T0 — 基线与审核：** 将 PR #24 的 PRD/TEST_PLAN/IPC 文档纳入选定 React/Tauri 目标基线，复查 #35 的独立修复，更新旧 README，建立清晰、可合并的 PR 依赖图；核实 #40 的原生 WebView2 人工验收。

**T1 — 正式 Repository：** 在 #36 Schema 基础上设计可测试的本地数据库路径/创建/只读打开/升级和 Repository API，继续保持真实文件写入关闭。

**T2 — CAS/SQLite 操作日志与恢复状态机：** 定义 import job states、staging/finalize/commit/reconcile、幂等重试与孤儿对象审计；实施故障注入，优先保护 Raw 源。

**T3 — 安全导入和 WAL-safe 备份：** 以临时测试素材验证真实 Windows 导入；实现完整快照并从独立目录恢复。**只有 T2/T3 通过才开放真实用户导入 UI。**

**T4 — 实验与论文业务：** #17 → #18 → #19：真实 CRUD、统计纳入、Panel 固定版本、多源无损导出和双向溯源。

**T5 — 最终产品：** #20 → #21：性能、Windows 安装/升级、数据恢复演练、正式版本验收。

## 12. 核心依据与后续维护

- [PRD v0.1（PR #24 分支）](https://github.com/Devil-Jian/data-m/blob/docs/product-prd-v1/docs/PRD.md)
- [四阶段 ROADMAP](https://github.com/Devil-Jian/data-m/blob/refactor/p1-single-ui-package/docs/ROADMAP.md)
- [测试计划 v0.1（PR #24 分支）](https://github.com/Devil-Jian/data-m/blob/docs/product-prd-v1/docs/TEST_PLAN.md)
- [Tauri IPC 契约（PR #24 分支）](https://github.com/Devil-Jian/data-m/blob/docs/product-prd-v1/docs/IPC_API.md)
- [统一正式 UI 源码说明](https://github.com/Devil-Jian/data-m/blob/refactor/p1-single-ui-package/docs/P1_SINGLE_UI_ARCHITECTURE.md)
- [Desktop 5176 HMR 预览](https://github.com/Devil-Jian/data-m/blob/refactor/p1-single-ui-package/docs/P1_DESKTOP_LIVE_PREVIEW.md)
- [SQLite Schema v1](https://github.com/Devil-Jian/data-m/blob/refactor/p1-single-ui-package/docs/P2_SQLITE_SCHEMA_V1.md)
- [CAS 隔离 staging 原型](https://github.com/Devil-Jian/data-m/blob/refactor/p1-single-ui-package/docs/P2_CAS_STAGING_PROTOTYPE.md)
- [SQLite/CAS 一致性审计](https://github.com/Devil-Jian/data-m/blob/refactor/p1-single-ui-package/docs/P2_CAS_INTEGRITY_AUDIT.md)
- [P1 Epic #8](https://github.com/Devil-Jian/data-m/issues/8)、[P2 Epic #9](https://github.com/Devil-Jian/data-m/issues/9)、[P3 Epic #10](https://github.com/Devil-Jian/data-m/issues/10)、[P4 Epic #11](https://github.com/Devil-Jian/data-m/issues/11)
- [当前最完整 UI 集成候选 PR #40](https://github.com/Devil-Jian/data-m/pull/40)、[最新 Windows CI](https://github.com/Devil-Jian/data-m/actions/runs/43710969361)

**更新约定：** 每次关闭一个 Issue、合并重要 PR 或发生架构/数据安全决策时，更新第 0、4、5、6、8、11 节及文档版本；不得直接把 Draft 原型写为正式可用功能。
