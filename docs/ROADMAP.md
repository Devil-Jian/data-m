# Data-M 四阶段开发路线图

> **方案基线（2026-11-14）**：React + TypeScript + shadcn/ui + Tauri 2 + Rust + SQLite + CAS。
> 本路线图用于所选择的 React / Tauri 正式开发路线。仓库里已有的 WPF、WinUI 试验分支不自动合并，也不在本路线图中当作已完成工作。

## 0. 项目开发制度（P1 的前置任务，不另增第五个开发阶段）

- 一个 Issue 记录一个用户可验证的需求、缺陷或基础设施任务。
- P0（阻断/数据安全）高于 P1（核心功能）高于 P2（体验优化）。
- 大型功能需 PRD/RFC 或 ADR；简单修复允许 Issue 中直接给设计依据。
- 开发路径：Issue → feature branch → 实现/单测 → PR → CI → Windows 验收 → merge → Release。
- `main` 仅接受通过验收的内容；历史试验分支保持隔离。
- 开发完成与验证完成分开记录。CI 通过不是 Windows GUI 手工验收通过。

## 阶段总览

| 阶段 | Epic | 范围 | 退出标准 |
| --- | --- | --- | --- |
| P1 客户端与交互 | [#8](https://github.com/Devil-Jian/data-m/issues/8) | shadcn UI、Tauri 2、文件选择/拖拽/键盘 | EXE 独立运行；矩形框选/快捷键/E2E/外部安全拖入实际验证 |
| P2 资产管理 | [#9](https://github.com/Devil-Jian/data-m/issues/9) | Asset/Version、SQLite、CAS、导入/移动/删除/备份 | 原始数据不可变、失败可恢复、数据一致性测试通过 |
| P3 科研业务 | [#10](https://github.com/Devil-Jian/data-m/issues/10) | 五级实验、四阶段、Paper/Figure/Panel 和溯源 | 从原始数据到论文子图完整闭环，双向溯源不失效 |
| P4 正式发布 | [#11](https://github.com/Devil-Jian/data-m/issues/11) | 性能、质量、Windows 安装和发布 | 清洁 Windows 环境安装/升级、备份恢复演练、v1.0.0 门禁 |

## P1 · Windows 客户端与 Explorer 交互

- [ ] [#12 工程规范/ADR/Windows CI](https://github.com/Devil-Jian/data-m/issues/12) — 前置基线；本次 PR 先创建文档模板。
- [ ] [#4 Tauri 2 桌面窗口与 Windows 文件拖入](https://github.com/Devil-Jian/data-m/issues/4)。
- [ ] [#13 真实 Asset ID 的选择、框选和拖拽验收](https://github.com/Devil-Jian/data-m/issues/13)。
- [ ] [#5 大规模虚拟视图与 E2E](https://github.com/Devil-Jian/data-m/issues/5)。
- [ ] [#7 快捷键、菜单与剪贴板语义](https://github.com/Devil-Jian/data-m/issues/7)。

**边界**：交互产生 `FileOperationIntent`；不直接编辑 Blob 或 SQLite。选中文件必须具有 Asset ID，不得把 Replicate 当成文件实体。正式客户端实现前，现有 `demos/shadcn` 仍是 Web 前端演示。

## P2 · 本地科研资产管理

- [ ] [#14 Domain 与 SQLite Schema](https://github.com/Devil-Jian/data-m/issues/14)。
- [ ] [#6 CAS / SQLite 安全文件操作](https://github.com/Devil-Jian/data-m/issues/6)。
- [ ] [#15 文件导入队列/原始数据保护/异常恢复](https://github.com/Devil-Jian/data-m/issues/15)。
- [ ] [#16 备份与一致性校验](https://github.com/Devil-Jian/data-m/issues/16)。

**边界**：外部导入默认复制；内部逻辑移动与复制不产生额外生物学重复；Blob 内容不可原地修改；SQLite 事务不直接涵盖文件系统，需 staging/finalize/reconcile 恢复机制。

## P3 · 科研业务闭环

- [ ] [#17 五级实验与四阶段归档](https://github.com/Devil-Jian/data-m/issues/17)。
- [ ] [#18 Paper/Figure/Panel 关联与版本锁定](https://github.com/Devil-Jian/data-m/issues/18)。
- [ ] [#19 双向溯源与审计](https://github.com/Devil-Jian/data-m/issues/19)。

**关键不变量**：
1. Project → Theme → Assay → Group → Biological Replicate；FileAsset 与 Replicate 是两个实体。
2. `inStats` 属于统计分析与样本之间的关系；`isRepresentative` 属于 Panel 和展示来源之间的关系，不是全局文件属性。
3. Paper/Panel 引用 AssetVersion 的稳定 ID，而不是易变路径。
4. 逻辑移动不导致引用断链；导出的 manifest 带版本和哈希。

## P4 · Windows 发布与稳定性

- [ ] [#20 10k+ 性能、缩略图和响应性](https://github.com/Devil-Jian/data-m/issues/20)。
- [ ] [#21 Windows Installer、升级/回滚、v1.0.0 验收](https://github.com/Devil-Jian/data-m/issues/21)。

**发布硬门槛**：真实 Windows 11 安装/卸载、受保护科研资产完整性、SQLite 迁移/备份恢复、无阻断缺陷、测试报告、版本与变更日志。

## 依赖顺序

```
P1/Issue #12 基础规范
  ├─ #4 桌面外壳
  ├─ #13 + #5 + #7 交互
  └─ P2/#14 领域模型（可提前并行设计）
            ↓
P2/#6 资产操作 → #15 安全导入 → #16 备份恢复
            ↓
P3/#17 实验归档 → #18 论文关联 → #19 双向溯源
            ↓
P4/#20 性能和 #21 Windows 正式发布
```

## 每个 Issue 的 Definition of Done

- [ ] 验收标准逐项满足，有必要的截图/日志/测试报告；
- [ ] 涉及数据不变量、文件操作的测试已添加；
- [ ] TypeScript/Rust 类型检查、格式检查与相关单元/集成测试通过；
- [ ] 没有破坏现有实验/论文主业务；
- [ ] PR 清楚描述设计变更、风险、测试和未完成项；
- [ ] Windows GUI 与文件操作涉及的手工验证有记录；
- [ ] 文档及版本变更说明按需更新；
- [ ] 审查通过后才允许标记 Issue 完成。

## 实施原则

- **模块化单体**：前端 React features，Tauri IPC，Rust Application/Domain/Infrastructure。无需微服务。
- **短分支和小 PR**：一个 PR 尽量围绕一个 Issue；对跨模块变更拆成可独立验证的阶段。
- **安全优先**：原始数据不可变、明确引用检查、可恢复操作优先于视觉交互。
- **演示与生产明确隔离**：模拟数据不得被误称为真实归档；未实现的文件操作只展示操作意图。
