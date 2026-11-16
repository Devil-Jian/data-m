# S2 分组、样本与四阶段结构

状态：`accepted`。机器验证通过；2026-11-16 用户明确确认“S1-S3 人工验收通过，允许合并”。PR #45 已合并 main，合并提交 `edf477995369a9e45dfe6de944374aae2979c536`。

用户于 2026-11-16 授权 S1 机器通过后继续 S2、S3，最后一起人工验收。S1 保持 manual_pending，不将延后验收解释为 accepted。S2 基于 `feat/s1-project-tree` 的 `14eac03` 开发，独立 Draft PR 以该分支为目标；无自动合并授权。

## 范围

- 真实受控测试库的 Group / Replicate CRUD、稳定 ID、事务/幂等、跨 assay 拒绝、非空删除保护。
- 所选实验的分组/样本视图；样本 n 从 replicates 查询，不由文件数量推算。
- Raw/Processed 为样本级，Statistics/Display 为实验级，四阶段空视图。
- S1 受控库验证与原生持久化保持；普通浏览器无原生接口，测试 Mock 明确标注。
- 不导入文件，不添加统计计算、论文组图或生产存储能力。

## 机器验证

受测实现 SHA：`3742f70548ace31972cccccee6f4fa2ec01f56fa`，独立 [Draft PR #45](https://github.com/Devil-Jian/data-m/pull/45)，堆叠于 S1 #44。后续文档提交不修改可执行代码。

- [Windows CI](https://github.com/Devil-Jian/data-m/actions/runs/43735920859) 与 [Linux UI CI](https://github.com/Devil-Jian/data-m/actions/runs/43735920643) 整轮成功。
- S2 Rust 7 项（含真实临时库命令处理器），全量 Rust 42 项、S1 专项14项、S0专项4项，全部通过。
- JS 单元55项、工程检查3项；Desktop浏览器16项（含 S2 Mock 6 项）、Explorer29项，均通过。
- rustfmt、cargo check、Desktop与历史UI构建、Windows便携EXE均通过，artifact 为 `data-m-s2-unsigned-portable-windows`。最终统一人工验收使用包含 S1–S3 的候选 EXE，并另行记录该 EXE 哈希。
- 复审发现启动检查截取了空函数体，已先复现失败、再修正范围并验证真正覆盖 Builder；未放宽无启动写入断言。初轮格式失败按 Windows rustfmt 输出修正后重跑通过。

命令、退出码、用例数和日志见 [结构化证据](S2_EXECUTION_REPORT.json)。按用户授权可继续 S3 实现，不把 S2 标记 accepted。

## 统一人工验收（accepted）

2026-11-16 用户对 S1–S3 联合人工验收明确签收通过并授权合并。此处以用户原文作为签收证据，不虚构截图文件或外部证明。

1. 使用最终 S1–S3 候选 EXE，记录源码 SHA、EXE SHA-256、Windows 版本与截图。
2. 在 S1 新建实验，进入“分组与样本”，创建 Control/Model/Treatment 三组，每组 6 个样本；逐组 n=6，总数18。
3. 重启后核对分组、样本、稳定层级；切换四阶段，Raw/Processed 应要求选中样本，统计与展示为实验级。
4. 尝试删除含样本分组、非法关联；应拒绝且原有数量不变。删除空节点、重命名后重启确认。
5. S3 导入/复用文件后再次比较 n，文件数量不能增加生物学重复。人工未实际执行前保持 manual_pending。
