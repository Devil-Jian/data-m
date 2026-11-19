# S12R 开发与验收记录

TASK_ID：`S12R-DEMO-WORKBENCH-PRODUCTION`
唯一起点：`e7b12d7022ed40d33bccc42f9a628b305e91ccf9`（S11；PR #57）。
独立分支：`feat/s12r-demo-workbench-production`，Draft PR #60；不引入错误的 #59，不合并 main。
当前状态：**development_integrated / machine_pending / manual_pending**。以最终同 SHA GitHub Actions 为唯一机器验收依据。

## 代码层面已接入的功能

### S12R-00：可重复的原始 SHA 视觉基准
- `scripts/capture-s12r-baseline.mjs` 加入 Windows CI，最终运行时从不可变 `e7b12d...` worktree 启动原 Demo，采集首页、矩阵、阶段、右键、论文图卡/抽屉、窄屏截图。
- `tests/desktop-ui/s12r-visual-contract.spec.ts` 使用同一窗口/DPI 对显式 Mock 的正式版和 Demo 共享组件取样，并附加截图。
- **尚未执行最终 CI，不得声称基线截图已实际生成或通过视觉验收。**

### S12R-01：UI 母版单源组件
- 原版 Workbench 抽离 `WorkbenchHeader`、`ResearchNavigationTree`、`WorkbenchExplorerToolbar`、`WorkbenchStatusBar`、`VirtualAssetList`、`WorkbenchFigureList`。
- Demo 与 Production 已共用上述结构及 `workbench.css`；Project/Assay/Asset/Paper 的数据由 adapter 注入，正式版不注入 `INITIAL_PROJECTS`。
- 原生矩阵单元格与业务抽屉仍针对真实作用域渲染，不使用演示预览图像。全屏同尺寸视觉差异仍须查看 CI 截图。

### S12R-02：真实项目与实验结构
- 项目、主题、实验、Group、Replicate 现均提供 Workbench 原位新建、重命名及依赖保护删除入口；S1/S2 原生请求含幂等键。
- 实验 n 和分组 n 使用 SQLite 数据，项目/实验异步读取具有限作用域失效检查。真正重启持久化和故障注入需原生验收。

### S12R-03：四阶段关系与分页
- 默认全景矩阵和分阶段视图保留 Raw/Processed 样本级、Statistics/Display 实验级的真实范围；支持样本多文件，避免伪造样本级关联。
- S8 已冻结纳入分析与 S9 固定显示/代表来源的证据可在样本矩阵中显示，`n` 与纳入 `n` 独立。
- `use-production-stage-assets.ts` 按后端 S4 游标每页 100 条分页，去除分阶段浏览旧 1,000 条封顶；`production-adapter.ts` O(assets+replicates) 索引、实验级计数取原生 total。初始矩阵仍有有界预览并显式提示数据不完整。
- 网格、列表和表格视图已接；大列表使用原版虚拟滚动，DOM 不应展开所有文件。需 10,024 资产 GUI 性能机器/实机证据。

### S12R-04：Explorer 操作闭环
- `useExplorerInteraction` 复用单击/多选/框选/右键/菜单键/F2/Delete/Ctrl+C/X/V/分组拖放；实际 S5 显式目标复制/移动与 S6 逻辑重命名、删除、回收和恢复。
- 对象身份为当前作用域 `AssetLinkId + AssetVersionId`；Raw/Processed 目标必须选定真实 Replicate，统计/展示不能虚构 replicate。写入后重读原生真值。

### S12R-05：文件准备与导入
- 正式顶部进入真实 `ImportJobsPanel`，选阶段、明确样本/Assay 级目标，原生 OS Drop→S3 prepare→人工 start→轮询进度、取消与重试。
- .zip 在准备阶段前被拒绝；本阶段不宣称 ZIP 解包、一键导入整个科研论文项目或自动映射科研结果。

### S12R-06：分析与论文
- 原生 S8 统计分析抽屉支持 create/save/freeze/revise、纳入样本与输入/结果固定版本；冻结数据才显示为可信纳入证据。
- 原生 S9 Paper/Figure/Panel 复用 Demo 的 Figure/Panel 图卡及详情抽屉；支持创建/改名/依赖保护删除、Figure/Panel 重排、论文状态备注、attach/detach 固定 AssetVersion、representative/clear。
- S10 正向/反向追溯、返回 Panel 上下文、Figure preview→确认→SHA manifest 与取消由真实原生服务执行。无真实图像格式的文件只显示原始名字、版本、Hash 等可信属性。

### S12R-07：数据安全与发布隔离
- S7 audit、backup、verify、new-label isolated restore 已作为工具浮层接入，保持不能覆盖运行仓库、不等于异地备份的提示。
- Demo 旧 UI 移至 DEV 专用 lazy import，发布入口不再静态导入演示工作区。发布打包前新增 `scripts/check-s12r-release-bundle.mjs` 扫描禁止的真实数据模拟种子。
- 已撰写 `docs/S12R_USER_GUIDE.md`。未签名测试构建文案保持明确。

### S12R-08：最终门禁（仅开发结束后执行）
- Windows CI：S0–S11 旧测试、S12R Vitest / Playwright 非零用例、固定基线截图、Demo bundle 泄露检查、原生 Rust full tests、10,024 基准、Tauri NSIS/portable、独立 SHA-256 manifest。
- 只有全部通过且产物来自同 SHA 才能标记 `machine_passed`；未执行 Windows 11 真人 EXE 检查，持续 `manual_pending`。

## 人工验收仍需独立完成

Windows 11 EXE 启动、真实数据和 10,024 文件 GUI 浏览性能、键盘/鼠标拖动、S3 源文件保护、S7 快照/隔离恢复、S8 冻结、S9 Figure/Panel 固定关联、S10 Manifest 物理校验、视觉 7 项对照。任何未通过项必须如实记录。

重要约束：不修改 SQLite Schema v3；不物理改写 CAS；不以模拟统计/荧光缩略图充当真实科研值；不自主合并 main；不把未执行的人工验收标记通过。

## 最终集成送检

功能代码已集中提交，当前进入最终同 SHA Windows/UI CI 门禁。只有 Actions 全部成功且 Windows NSIS、portable EXE、release Manifest 与独立 SHA-256 校验产物来自同一提交时才标记 `machine_passed`。测试中仍不允许把浏览器 Mock 等同 Windows 11 原生 GUI 人工验收。

## 最终机器验收尝试记录

- 第一次统一 CI `42472f0c0ceeeb732dcdc5f2eb2626e58a7e0d2c`：三套 Demo 构建成功，但 Windows S0 源码契约检查失败（正式桌面入口移除了对 canonical UI 的直接引用）。
- 已通过桌面入口显式传递 `UIKit`（`55c98e43154421ea3a687f5f5081e73e8bcfe2fc`）修复，旧 Demo 数据仍受 DEV 动态模块隔离。后续同 SHA CI 仍需重新执行，不宣称机器验收通过。

- 第二次 CI `210b54ca2e9ae6e1b478c81191c430e9b9811615`：S0 和早期原生 S10 通过；正式桌面编译发现 `production-paper.ts` 与 `production-paper.tsx` 同名导致解析到数据 API 而非 React 组件，及默认 UIKit 名称引用错误。已将 React 组件迁为 `production-paper-workbench.tsx`、明确导入并修复 UIKit 默认值。发布隔离检查因上游编译中断而没有可检查的 dist；待下一轮完整 CI 验证。

- 第三次 CI `3fe23d840293a45e4ac03aed7b7c85d78a394888`：三套 Demo 变体构建成功、S0 和早期 Rust 通过；桌面 TSC 只报 S9 requestId UUID 模板类型不兼容。已显式将编辑提交的 `requestId` 参数声明为 `string`；发布隔离步骤因上游构建失败尚未运行有效扫描。重新送检。

- 第四次 CI `eff8aae211d3df3a118afcfde8fb0fd23ff41197`：S0、桌面 TypeScript+Vite、S12R 单测、Demo 发布包隔离、原 SHA 截图采集成功。独立 Linux 浏览器交互 21 通过、8 失败；8 项均在旧虚拟文件列表用例等待 `getByTitle('虚拟文件列表')`，共享工具栏重构丢失原始可访问 title。已恢复旧 title，原有 8 项测试保留并重新送检。
