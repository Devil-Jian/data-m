# S2 分组、样本与四阶段结构

状态：`in_progress`，未人工验收、未合并。

用户于 2026-11-16 授权 S1 机器通过后继续 S2、S3，最后一起人工验收。S1 保持 manual_pending，不将延后验收解释为 accepted。S2 基于 `feat/s1-project-tree` 的 `14eac03` 开发，独立 Draft PR 以该分支为目标；无自动合并授权。

## 范围

- 真实受控测试库的 Group / Replicate CRUD、稳定 ID、事务/幂等、跨 assay 拒绝、非空删除保护。
- 所选实验的分组/样本视图；样本 n 从 replicates 查询，不由文件数量推算。
- Raw/Processed 为样本级，Statistics/Display 为实验级，四阶段空视图。
- S1 受控库验证与原生持久化保持；普通浏览器无原生接口，测试 Mock 明确标注。
- 不导入文件，不添加统计计算、论文组图或生产存储能力。

## 机器验证

待记录本次受测 SHA、真实命令、退出码、非零用例数、Windows/Linux CI 和 EXE 哈希。不能用 Python SQL smoke 或浏览器 Mock 代替 Rust/Tauri/真实库验证。

## 延后统一人工验收

1. 使用最终 S1–S3 候选 EXE，记录源码 SHA、EXE SHA-256、Windows 版本与截图。
2. 在 S1 新建实验，进入“分组与样本”，创建 Control/Model/Treatment 三组，每组 6 个样本；逐组 n=6，总数18。
3. 重启后核对分组、样本、稳定层级；切换四阶段，Raw/Processed 应要求选中样本，统计与展示为实验级。
4. 尝试删除含样本分组、非法关联；应拒绝且原有数量不变。删除空节点、重命名后重启确认。
5. S3 导入/复用文件后再次比较 n，文件数量不能增加生物学重复。人工未实际执行前保持 manual_pending。
