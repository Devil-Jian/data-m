# I1 集成实施设计

- 基线 S8: 8ef0609f2360962674544b76a834bbd4ff2de8e0；S9: 14fd3a951d67aa92091ed8a10c989dfa2c08b71f；共同 S7: 15e784a6bf939cf509254aacdea33bb120cf9165。
- 保留 S8 v3 SQLite 迁移与统计域、S7 WAL/隔离恢复；S9 Paper/Figure/Panel 使用已有 v2 起的论文结构表，不新增 v4。
- 冲突矩阵：lib.rs / workbench.tsx / scripts/s0-baseline.test.mjs / desktop-windows.yml 合并；DATA_M_AI_TASKS.json 保留 S8 原记录，S9 状态由独立报告记录。
- 双 IPC 明确注册和白名单，互不复用 requestId 业务命名空间；S8 分析和 S9 Panel 均引用精确 AssetVersionId。
- S9 候选最多 500 条，本期明确 UI 提示可能截断；过滤仅对已加载项生效，后续须实现服务端分页。
- S10 仍负责完整双向溯源、Figure Manifest 和安全导出。
- 禁止触及真实用户仓库和 CAS；S7–S9 人工验收待完成。
