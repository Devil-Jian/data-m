# S12R 开发阶段记录（以实际代码和 CI 为准）

- TASK_ID: S12R-DEMO-WORKBENCH-PRODUCTION
- 唯一起点: e7b12d7022ed40d33bccc42f9a628b305e91ccf9
- 分支: feat/s12r-demo-workbench-production
- PR: #60 (Draft)
- 状态: in_progress / machine_pending / manual_pending
- 严禁从 PR #59 引入代码；未合并 main。

## 本轮已经提交的实现

1. production-adapter.ts：复用 S1/S2/S4 API，对应 Project/Theme/Assay、Group/Replicate 和四阶段 AssetLink；显式记录分页是否完整，不用 Demo 数据补齐。
2. production-workbench.tsx：桌面默认入口改为真实科研工作区，沿用演示的 CSS 类名、顶栏、侧栏、分组 Tab、全阶段矩阵/分阶段布局；真实新建项目/主题/实验/分组/样本，以及文件元数据抽屉。
3. 原 useExplorerInteraction 引擎复用至真实文件视图，支持选区、鼠标手势及键盘/右键操作入口；S6 真实重命名、逻辑删除，写入后重新查询。
4. production-adapter.test.ts、s12r-production.spec.ts：阶段作用域、无 Demo 注入及浏览器显式 Mock 回归案例。
5. Windows 工作流增加 S12R 分支；保留历史测试、Rust 测试、NSIS/portable 构建步骤。

## 未完成，不得标记通过

- S12R-00：尚未冻结固定原 SHA 的完整截图/录像证据。
- S12R-01：视觉 CSS 与原版共享，但 WorkbenchView 纯视图同源抽离尚未完成；必须进一步消除两份结构性 JSX 的漂移。
- S12R-02：已有创建和局部重命名操作；全层级重命名、删除及重试/刷新边界仍需闭环。
- S12R-03：首版真实全景矩阵已接入。当前每阶段最多查询 1,000 资产且会标记 incomplete，不满足 10,024 真实文件性能门槛；Statistics/Display 样本贡献证据尚未接 Analysis/Panel。
- S12R-04：文件真实重命名/回收已接入，但跨作用域复制、移动、框选性能和虚拟文件列表均未完成。
- S12R-05：导入向导未接；当前入口禁用而非假成功。
- S12R-06：正式论文 Figure/Panel 图卡、侧面抽屉和固定版本溯源未接，未加载 Demo 假论文。
- S12R-07：S7 安全工具入口及正式 UI 扩展尚未完成。
- S12R-08：无法从当前环境运行原生 Windows GUI；完整 npm/Rust/Playwright/NSIS 同 SHA CI 尚无成功证据。
- 历史桌面 E2E 仍可能假定双模式工作区，须逐条迁移为正式入口的同等行为测试，禁止直接删测求绿。

**验收结论：尚未交付，不能标记 machine_passed。**
