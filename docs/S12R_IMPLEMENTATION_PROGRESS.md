# S12R 演示 Workbench 产品化 — 实际开发记录

- TASK_ID: S12R-DEMO-WORKBENCH-PRODUCTION
- 唯一基线 SHA: `e7b12d7022ed40d33bccc42f9a628b305e91ccf9`
- 分支: `feat/s12r-demo-workbench-production`
- PR: #60 (Draft，base S11)；未合并 main，未沿用错误 PR #59。
- 状态: **in_progress / machine_pending / manual_pending**。机器状态以最新 SHA 的 Windows Actions 为准。

## S12R-00：原 SHA 和母版

已核对 `packages/core/src/workbench.tsx`、交互 Hook、SQLite IPC 和原始分支关系；尚缺固定窗口/DPI 的原版 Demo 七类截图、录像及人工视觉证据。未标记完成。

## S12R-01：单源视觉组件与适配层

- `apps/desktop/src/production-adapter.ts`：读取真实 S1/S2/S4 数据，保留 Project/Theme/Assay、Group/Replicate、AssetLink/AssetVersion ID；显式报告阶段资产页不完整。
- `packages/core/src/workbench-header.tsx`：从原版 Demo 抽离共享顶栏；Demo 和 Production 现使用同一组件。
- `packages/core/src/virtual-asset-list.tsx` / `virtual-layout.ts`：通用化原始虚拟行和几何逻辑，Demo/Production 复用。
- **未完成**：整页 `WorkbenchView` 纯 UI 的完整同源抽离、全屏视觉对照、交互录屏。

## S12R-02：项目与实验真实 CRUD

正式入口 `ProductionWorkbench` 使用真实项目/实验数据，支持基本创建和部分重命名，自动查询当前 `n`。尚需补全所有节点的轻量上下文 CRUD、失败恢复和跨项目并发测试。未标记阶段通过。

## S12R-03：真实样本 × 四阶段全景

- 全景矩阵与 Raw/Processed/Statistics/Display 分阶段视图已接真实后端。
- Raw/Processed 按 Replicate ID 展示；Statistics/Display 仅展示 Assay 级文件数与“未证实本样本引用”，不制造虚假关联。
- 保留样本多文件；阶段查询默认上限每阶段 1,000 条，显示 `incomplete` 防止伪造完整性。
- **未完成**：S8/S9/S10 的逐样本统计贡献证据、服务端分页完整浏览、10,024 资产真实 GUI 稳定性。

## S12R-04：真实 Explorer 复制/移动/生命周期/虚拟列表

- 复用 `useExplorerInteraction` 与通用 `VirtualAssetList`；真实 AssetLink ID 作为选区身份，真实 AssetVersion ID 用于事务和详细信息。
- S6 重命名、逻辑删除接后端，写入后重新查询。
- 新增 `production-transfer.ts`：S5 复制/移动前冻结来源身份、选择阶段和目标；Raw/Processed **必须明确选择存在的 Replicate**；确认后原生事务提交并重新查询。
- 新增 Vitest 与 Playwright 的选择/目标保护回归。
- **未完成**：所有拖拽目标的完整行为保真、10k 前端分页与虚拟 DOM 全量机器证据、回收站入口。

## S12R-05：S3 安全导入

正式顶栏的导入入口现打开 `ImportJobsPanel`，通过实际 `import_jobs` 和原生拖入注册器受控执行 prepare→确认 start→进度/取消/重试；按样本级或 Assay 级目标选取，不支持假 ZIP 解包。流程仍需真实 Windows GUI E2E 和源文件安全实验验证。

## S12R-06：论文 Figure/Panel 产品化

- `production-paper.ts`、`production-paper.tsx`：验证真实 S9 Paper/Figure/Panel 和固定 `AssetVersionId`，用演示布局等级展示 Figure 图卡与 Panel 抽屉。
- Panel 展示明确的用途/版本/Hash/代表来源，真实候选固定版本 attach、detach 及返回实验；原生 S10 ProvenanceView、Figure preview→确认导出→SHA manifest 校验、取消入口。
- 有显式 browser Mock E2E，不向真实项目注入 Demo 图像。
- **未完成**：完整重排/重命名/删除交互、S8 frozen Analysis 深度汇总、Panel 上下文返航、全流程 native S9/S10 UI 机器证据。

## S12R-07：安全工具、正式发行态

正式首页不自动注入 `INITIAL_PROJECTS`；保留开发专用 `?legacy=1` 回归入口，仅允许在 `import.meta.env.DEV` 环境使用。S7 安全工具母版风格入口、实测打包产物是否无演示种子注入、版本文案仍待完成。

## S12R-08：验证和发布

- Windows 工作流已包括 S0–S11 旧门禁、S12R Node/Vitest 和非零 `@S12R` Playwright 门禁、Rust、NSIS、portable EXE 与 Manifest。
- 先前 Windows run #44452440775 因 `production-workbench.tsx` TypeScript TS2322 在 Tauri beforeBuildCommand 失败；已分离 S1/S2 请求类型并修复。
- **新 SHA 必须重新通过所有门禁**。构建完成前不得宣称生成可用 EXE、不得标记 `machine_passed`。
- 原生 Windows 11 鼠标拖拽/10k GUI/真实科研包/论文导出需用户人工验收，保持 `manual_pending`。

> 安全约束：不修改 Schema v3、不物理删除 CAS、不用模拟统计或预览冒充科研数据、不自主合并 main、不得把上一轮 CI 成绩算作本轮验收。
