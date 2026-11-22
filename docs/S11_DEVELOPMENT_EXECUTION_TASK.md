# Data-M S11 阶段开发执行指令（以 S10 完成机器验收为进入条件）

## 1. 任务与边界

- **TASK_ID：S11 — Windows 性能、安装与正式交付准备**。
- 仓库：`Devil-Jian/data-m`。
- **强制基线分支**：`feat/s10-bidirectional-trace-export`；基线 SHA：`420bcd4be18ef6b781a629f1b887c0c57611f2ee（受测代码；创建新分支前应包含后续文档提交的实际 S10 HEAD）`（必须在创建 S11 分支前以 GitHub 实际 HEAD 复核）。
- 经机器验收的 S10 代码 SHA：`420bcd4be18ef6b781a629f1b887c0c57611f2ee`；CI：`https://github.com/Devil-Jian/data-m/actions/runs/44094234909`。
- 从该 S10 基线新建 `feat/s11-windows-performance-installer`，创建 **Draft PR**，base 为 S10 分支。
- 保留 S8、S9、I1、S10 和 `main` 原有分支，不合并 `main`、不改写旧阶段历史；S7–S10 未做的人工验收不标为 `accepted`。
- 本阶段只使用**可抛弃合成数据**进行开发/机器测试；严禁用独有科研原始数据进行导入、清理、迁移、恢复或性能压测。

## 2. 开发前审计（禁止跳过）

依次检查：`docs/AI_DEV_EXECUTION_SPEC.md`、`docs/DATA_M_AI_TASKS.json`、`docs/S10_EXECUTION_REPORT.md`、`docs/S10_TRACE_EXPORT_HANDOFF.md`、`docs/S10_MACHINE_ACCEPTANCE_MATRIX.md`、`apps/desktop/src-tauri/tauri.conf.json`、`apps/desktop/src-tauri/Cargo.toml`、`.github/workflows/desktop-windows.yml`、实际资产浏览分页与搜索、S7 备份/隔离恢复、S10 Manifest/原子导出实现。记录代码/CI/EXE 的实际状态，不得把 GitHub Runner 的 headless CI 冒称已通过人机交互或干净 Windows 11 安装验收。

## 3. S11 工作包

### S11-01：真实 SQLite/CAS 10k+ 科研资产基准

- 在一次性临时仓库构造 10,000+ `DataAsset`、固定 `AssetVersion`、真实 `AssetLink`、多 Assay/Group/Replicate、多用途、跨 Paper/Panel 引用；包含中文、特殊 Unicode、长名称、大小写冲突和软删除状态。
- 使用与正式 UI 相同的 Tauri IPC、SQLite 查询和分页逻辑，不得仅凭 Demo 虚拟数组声称性能达标。
- 记录测试硬件、Windows 版本、数据规模、缓存冷热状态、首屏可交互延迟、搜索 p50/p95/p99、滚动与选择响应、内存峰值及资源占用。
- **性能目标**（不是既有实测）：首屏可交互 ≤3 秒、同机搜索 p95 ≤2 秒、后台导入时 UI 不发生持续 >500 ms 的主线程阻塞。对无法在 Runner 测得的 WebView2 指标保留明确的人工验收项目。

### S11-02：后台长任务/并发/恢复

- 覆盖大批量导入、统计查询、S7 备份/验证/恢复、S10 Figure 导出在并发负载下的内存边界、取消、进度、锁争用、崩溃再启动、重复请求、软删除与固定版本引用一致性。
- 资源分配以有界队列/流式文件 IO 为原则，不得为了性能降低 S3/S7/S10 的原始数据保护、哈希验证、回滚和审计要求。
- 形成明确的内存上界、运行失败错误码和恢复结果；禁止无授权覆盖/删除任何仓库、Blob 或用户选定导出文件夹。

### S11-03：Windows 正式安装器

- 选择 **NSIS 或 MSI 一种**作为首发安装目标；将 Tauri 配置从现有便携 EXE 开发模式按审核方案启用相应 bundle。明确版本号、应用标识、目标平台、安装目录、卸载行为和签名状态。
- GitHub Actions 必须在 Windows Runner 完成 Release 编译并实际上传安装器 Artifact；保留便携 EXE 作为诊断 Artifact，但不可冒充安装器。
- 安装器包和 EXE **分别计算 SHA-256**；同时生成 `release-manifest.json`（releaseVersion、sourceSha、schemaSupported、artifactName、artifactSha256、buildRunUrl、unsigned/signed 状态）和 Release Notes。
- 对应用数据目录采取独立于安装程序目录的存储策略；升级或卸载绝不能清除用户科研库。检查旧版本启动、升级中断、安装器失败和回退策略。

### S11-04：升级、迁移、备份与回退

- 在 Disposable v1/v2/v3 数据库和真实合成 v3 仓库上测试：旧版本→新版本、重复安装、首次启动、拒绝未来 Schema、迁移失败回滚、S7 备份校验与**隔离**恢复、S10 旧 Figure Manifest/固定版本引用继续可读。
- 不做数据库向旧版本的静默写入降级；无法降级时明确说明恢复为隔离仓库的流程。
- 可复现脚本保存每项结果的表计数、VersionId、Blob SHA-256、Panel/Analysis 冻结与引用哈希。

### S11-05：正式界面与交付质量

- 仅针对高频操作改进真实列表性能、必要的延迟加载/缩略图、有界分页、长任务进度/取消和错误提示；不得增加与科研数据来源真相冲突的 Demo 推断。
- 检查 100%/150% DPI、窗口缩放、键盘操作、失焦、项目切换、Paper/Analysis/资产浏览/备份恢复/导出完整闭环。CI 中 Playwright Mock 必须明确标记；原生文件对话框、拖放和安装器行为由 Windows 人工/VM 证据补齐。

## 4. 强制机器测试与验收门槛

1. 新增真实 Rust 测试，函数必须以 `s11_` 开头，至少一个真实 SQLite/CAS 10k+ 性能与稳定性用例；所有 S0–S10/I1 已有用例保留并继续运行。
2. 新增正式页面 Playwright `@S11` 非零用例，覆盖分页、取消、项目切换、安装/更新信息显示；不把 Mock 结果作为原生安装证明。
3. 必须运行 `cargo fmt --all -- --check`、`cargo test --locked --no-default-features s11_`、`cargo test --locked --no-default-features`、`cargo check --locked`、`npm ci`、TypeScript/Vite、全部桌面/Chromium E2E、严格 S0 IPC 白名单。
4. Windows CI 必须生成 **NSIS/MSI 安装器**、便携 EXE、各自 SHA 校验清单、release manifest，并在同一精确 SHA 的运行中上传全部 Artifact。
5. CI 机器层面满足 10k 真实 SQLite 数据一致性、搜索延迟目标及失败恢复；不能在无干净 Windows 11 GUI/真实安装过程证据时标注 `accepted` 或正式 v1.0 发布完成。
6. 除非全部机器门槛通过，否则 S11 留在 `in_progress`；通过后最多标记 `machine_passed / manual_pending`。

## 5. 禁止行为与待人工清单

- 不允许合并 `main`、修改 I1/S8/S9/S10 上游分支、关闭已有安全检查、跳过失败测试、以零用例冒充通过、将未签名测试包宣称正式签名发行版。
- 本轮人工验收暂缓：在**干净 Windows 11 VM/电脑**（无 Node/Rust）安装、启动、升级、回退、卸载；检查 100%/150% DPI、WebView2 文件/目录选择、拖放、10k 数据性能、Figure 导出、真实 S7 离盘隔离恢复；记录 EXE/安装器 SHA、日志和截图。
- 未经人工签收及 P0/P1 关闭，不发布 v1.0 正式版。

## 6. 提交文档与交接

- `docs/S11_IMPLEMENTATION_DESIGN.md`：真实现状、性能瓶颈、索引/缓存策略、安装器设计、安全威胁模型。
- `docs/S11_BENCHMARK_REPORT.md`：机器、数据集生成规则、10k+ 样本计数、冷/热运行次数、p50/p95/p99 与内存、失败基准及修复。
- `docs/S11_INSTALLATION_RELEASE_REPORT.md`：安装器格式、升级回退、机器 CI、Artifact URL、EXE/安装器 SHA、Release Notes、未签名状态与人工验收待办。
- 更新 `docs/DATA_M_AI_TASKS.json`，保留 S0–S10 与 I1 真实状态；不得修改人工状态。
- 每次最终交接必须包含：精确受测 SHA、Draft PR/base、成功的 Windows Actions URL、安装器/EXE Artifact、S11 与全量测试数量、原生 10k 数据库指标、开放缺陷及人工验收清单。

**进入条件：** S10 已由具有独立证据的 G0/S10 全套机器测试确认，状态应为 `machine_passed / manual_pending`；若条件不成立，不得开始 S11 正式阶段或将其作为已获准的开发基线。