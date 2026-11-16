# S3 安全测试导入与任务恢复

状态：`manual_pending`，机器验证通过；Windows GUI/拖放人工验收未执行，未合并。

用户于 2026-11-16 明确授权在 S1/S2 机器通过后继续 S3，最后统一人工验收。S2 受测实现 `3742f70` 已通过 Windows/Linux 与 EXE 构建，本分支基于其文档提交 `faf94af537e03de57751accb74cc955683832d79`。S1/S2 保持 manual_pending，不把延后验收记为 accepted。

## 范围与协议

协议见 [S3_IMPORT_PROTOCOL.md](S3_IMPORT_PROTOCOL.md)。仅隔离测试仓库和可丢弃源，prepare → 明确确认 → 后台复制/校验 → 每文件登记；任务、阶段、ID 与错误持久化。全部完成才成功，部分完成/取消必须显示计数；复制与关联不得改变生物学样本 n。

源文件只读；CAS hash 不可覆盖；已知测试库 v1→v2 事务迁移，未知库拒绝。只增加已有锁定版本 windows-sys 0.61.2 的直接 Windows API 依赖，用于句柄身份、共享锁和 reparse 检查，未引入新版本或包；Cargo.lock 由 --locked CI 验证。

## 机器验证

受测源码提交：`095d52752b63eb13b8a88c265ef0dbea78dfc0f8`。Draft PR：[PR #46](https://github.com/Devil-Jian/data-m/pull/46)。

- Windows 原生流水线 [43791397423](https://github.com/Devil-Jian/data-m/actions/runs/43791397423) 全部通过：Rustfmt、锁定依赖、前端构建、浏览器回归、Rust 测试、`cargo check --locked`、release EXE 构建及产物上传。
- S3 Rust 原生套件执行 17 项，17 通过、0 失败；包含 100 文件与 64 MiB 流式复制、源字节/mtime 不变、去重与幂等、取消边界、磁盘写入失败、坏 Blob、staging 身份竞态、仓库进程锁、真实子进程在 staged/published 断点退出并恢复、v1→v2 回滚、真实 Tauri IPC，以及 Windows 原地 junction 攻击/哨兵防护。
- 完整 Rust 套件执行 59 项，59 通过；S2 过滤套件 7/7、S1 过滤套件 14/14、S0 过滤套件 4/4。
- 浏览器执行：原文件交互 29/29、桌面全量 25/25、S3 显式 Mock 9/9；本地另执行 55 项 JS 单元测试和 3 项 S0 基线守卫，全部通过。
- 跨平台 Web 构建流水线 [43791397325](https://github.com/Devil-Jian/data-m/actions/runs/43791397325) 的 Fluent、Mantine、shadcn 与 interaction-e2e 全部通过。S3 受保护文件服务明确为 Windows-only，非 Windows 返回 `UNSUPPORTED_PLATFORM`，不以浏览器 Mock 替代原生验收。
- 候选 EXE：`data-m-s3-unsigned-portable-windows/data-m-desktop.exe`，11,100,160 bytes，SHA-256 `D543AFF7EA171A14FBBB0A91F5CBC691A60E78F57E611EC55A3F89442ED7AED6`；本地下载副本位于 `D:\paper\s3-artifacts-095d527\data-m-desktop.exe`。

结构化证据见 [S3_EXECUTION_REPORT.json](S3_EXECUTION_REPORT.json)。浏览器 Mock、SQL smoke 或仅编译成功均未被当作原生通过证据。

## S1–S3 统一 Windows 人工验收（全部待执行）

1. 使用最后 S3 候选 EXE，记录源码 SHA、EXE SHA-256、Windows 版本、DPI 和执行者。不要使用旧 S1/S2 二进制打开已升级的测试库。
2. S1：创建两个测试项目，各建多个主题、实验；重命名，尝试空名称/删除非空父级，退出重开后比较树和数量。
3. S2：在实验建立 Control/Model/Treatment 各6样本，确认 n=6/6/6、总18；切换四阶段，Raw/Processed 选样本，Statistics/Display 为实验级。尝试非法关联与非空删除。
4. S3：准备专门生成的小文件、同字节不同名文件、100个小文件和受控大文件，记录 SHA-256、大小、修改时间和源目录项。拖入所选 Raw/样本，确认前不得复制；核对目标后明确确认，观察进度、已完成数量与 hash。
5. 取消和重试，检查已完成文件不重复登记、未完成项可重试；同字节可复用 Blob，不同样本关联不混淆，样本 n 始终18。重名、源变动或锁定要明确报错，不能整批伪成功。
6. 用可丢弃源执行中途中止 EXE，再重新打开查看恢复状态、任务和已登记文件；核对源哈希/大小/时间/目录项不变，任务与对象/版本一致。不要对重要科研文件做故障试验。
7. 保存截图/录屏、任务结果、源前后 hash 和测试库检查。只有用户明确签收后才修改 accepted 或合并；S7 备份恢复门槛仍未解锁。
