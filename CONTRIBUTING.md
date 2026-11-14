# Data-M 贡献与开发规范

## 适用范围

本规范优先约束 `docs/ROADMAP.md` 中 React + shadcn/ui + Tauri 2 正式开发路线。其他草稿分支继续隔离。

## 工作流

1. 创建或确认 Issue；写清用户目标、明确范围和验收用例。
2. 大功能附 Design Doc/ADR，明确安全边界、数据模型与异常恢复。
3. 从最近已验证的集成基线创建短生命周期功能分支，命名 `feat/<scope>`、`fix/<scope>`、`chore/<scope>`。
4. 先写单元/集成测试，再实现；变更尽可能与单个 Issue 对应。
5. 提交 PR：描述行为、截图/测试结果、残留风险、关联 Issue。
6. 检查 CI、Windows GUI 验收及数据完整性，评审后合并；不绕过保护规则。
7. Release 前执行备份恢复/迁移/安装测试；发布版本与变更说明。

## 代码边界

- UI 不允许直接执行任意 SQLite SQL 或文件系统破坏性操作；
- 交互层仅产生 intent，Rust Application/Domain 验证后执行；
- DataAsset 与 Biological Replicate 必须分离；
- 论文引用保持稳定 AssetVersion，不绑定可变路径；
- 文件写入/迁移必须有恢复方案和审计信息；
- 模拟数据必须显著标注为演示。

## 最低质量要求（Definition of Done）

- TypeScript/Rust 类型/编译、格式检查和适用的单元测试通过；
- 关键业务不变量有回归测试；
- 安全/大文件操作增加故障测试；
- 用户可见交互需 Windows 实机或 WebView2 测试证据；
- PR 描述、文档和任务验收项完整；
- 不将构建成功等同于实际交互和数据安全验收。

## 常用命令（当前 UI 演示）

```powershell
npm.cmd install
npm.cmd run test:interaction
npm.cmd run build:shadcn
npm.cmd run dev:shadcn
```

Tauri/Rust 命令在桌面项目创建后正式加入。
