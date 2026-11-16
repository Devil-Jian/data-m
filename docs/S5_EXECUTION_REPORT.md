# S5 执行报告：资产复制与逻辑移动（机器通过，人工待验收）

- TASK_ID：S5，开发分支：`feat/s5-asset-link-copy-move`
- 唯一 S4 开发基线：`789bac6cc9757023aa58b67bc986bb2a65227235`
- 上游 PR：[S4 #47](https://github.com/Devil-Jian/data-m/pull/47)，仍 Draft，未合 main
- 本阶段 Draft PR：[S5 #49](https://github.com/Devil-Jian/data-m/pull/49)，以 S4 分支为 base，不合并 main
- 方案：[S5 实施设计](S5_IMPLEMENTATION_DESIGN.md)
- 状态：`machine_passed / manual_pending`。**机器 CI 有实际成功记录，但 Windows 11 WebView2 人工验收尚未完成；不标记 accepted，不合并 main。**

## 实施范围

1. 新增 `asset_transfer` Tauri IPC。每个来源携带并在事务中核验 `AssetLinkId`、`AssetId`、当前 `AssetVersionId` 及源作用域。
2. 目标为合法活跃实验+样本的 Raw/Processed，或实验级 Statistics/Display；非法跨实验/跨组组合拒绝。
3. 单批 1–100 条。复制仅创建 `asset_links`，移动仅改写指定 `asset_links` 的目标作用域，不写 CAS Blob、不修改 AssetVersion、绝不新增 Replicate。
4. `BEGIN IMMEDIATE` 原子事务，批次同名预检查与唯一索引保护，`s1_requests` 重试幂等和 `audit_events` 同事务存储。成功时返回逐条新/旧 LinkId；错误返回结构化 code。
5. 正式「项目管理」工作区 Ctrl+C/X/V、右键菜单、Ctrl/Shift 拖拽、选择当前页安全快照、成功刷新 DB 列表/错误提示。演示工作区仍不是真实接口。

## Windows 机器验证证据（已核验）

- **成功 CI**：[Windows desktop shell #43845442075](https://github.com/Devil-Jian/data-m/actions/runs/43845442075)，结论 `success`，代码提交 `ad7eaeab2abc9bd5158ed18db6242384ebab1013`，Windows Release EXE 编译及上传均 `success`。
- **便携 EXE Artifact**：[data-m-s5-unsigned-portable-windows #14215881133](https://github.com/Devil-Jian/data-m/actions/runs/43845442075/artifacts/14215881133)，未签名的压缩包，约 4 MB；EXE 的 SHA-256 需下载解压后实测，不预设具体哈希。
- **原生数据库专项**：Rust `s5_` **8 passed / 0 failed**，非零用例检出通过。包含 10 文件跨组、跨样本移动、幂等重放、非法目标和陈旧关联/版本、同名冲突、审计约束故障注入后批量回滚、Blob / SHA / n 及重开持久化。
- **原生回归**：S4 2、S3 17、S2 7、S1 14、S0 4 项专项全部通过；完整 Rust **69 passed / 0 failed**。
- **浏览器专项**：`@S5` **3 passed / 0 failed**，桌面全量 Playwright **29 passed / 0 failed**，其他 S0–S4 renderer 回归通过。
- **关键限制**：Windows Headless Chromium 没有生成真实 HTML5 `dragstart` 鼠标事件；`@S5` 改用完整 DOM `DragEvent/DataTransfer` 事件和显式浏览器 Mock 验证 UI 接线。**不能据此认定真实 WebView2 Ctrl/Shift 鼠标拖拽通过**；此项仍需人工验收。SQLite 测试独立运行真实 Rust/SQLite，不依赖浏览器 Mock。


| 项目 | 命令/证据 | 状态 |
|---|---|---|
| S5 Rust 原生 SQLite | `cargo test --locked --no-default-features s5_`；8 passed | machine_passed |
| S5 Playwright 交互 | `npm run test:e2e:desktop -- --grep "@S5"`；3 passed，显式 Mock/DOM DnD | machine_passed（仅交互） |
| S0–S4 原生回归 | S0 4、S1 14、S2 7、S3 17、S4 2 | machine_passed |
| 全量前端 | TypeScript/Vite、前端测试、Desktop E2E 29 passed、各阶段专项 | machine_passed |
| 完整 Rust | `cargo test --locked --no-default-features`；69 passed | machine_passed |
| Rust 编译/格式 | `cargo fmt --all -- --check`、`cargo check --locked` | machine_passed |
| Windows Release EXE | `npm run tauri:build`、Artifact #14215881133 | machine_passed |
| Windows 原生 GUI / 文件校验 | Windows 11 用户实测，含真实指针拖动与 hash | manual_pending |

**证据边界：** 机器成功只证明指定代码提交的自动化和编译；人工 WebView2、SHA 对照、S7 备份恢复验收均未签收。S5 不标记 `accepted`。浏览器 Mock 不替代 Rust/SQLite。

## Windows 11 人工验收清单（仅可抛弃测试数据）

1. 下载本次 Windows Actions 的未签名 `data-m-desktop.exe` 并记录 Actions run、Artifact ID、EXE SHA-256、Windows 版本。PowerShell 示例：`Get-FileHash .\data-m-desktop.exe -Algorithm SHA256`。
2. 启动 EXE →「项目管理」→ 新建测试项目、实验，建立 Control 与 Treatment 两组，每组至少一个 Replicate，记下 **n**。
3. 在 Control/R1 的 Raw 用 S3 导入 10 个各有不同文件名的**可抛弃**文件，保留源路径及每个源的 `Get-FileHash -Algorithm SHA256` 清单，检查真实列表数量。
4. 全选这 10 条 → Ctrl+C → 导航 Treatment/R2 的 Raw → Ctrl+V。检查目标增加 10 条、源仍有 10 条、资产版本/SHA 相同、n 不变。刷新页面和重启 EXE 后两处仍存在。
5. 将其中一个文件 Ctrl+X → 切换另一个合法样本 → Ctrl+V。检查源关联消失、目标出现，AssetLinkId 不变，AssetId/AssetVersionId 不变， n 不变。
6. 分别使用右键「复制/剪切/粘贴」以及 Ctrl+拖拽、Shift+拖拽，把文件关联移动至样本级 Raw/Processed、实验级 Statistics/Display 的合法落点；普通无修饰键拖拽不得暗中执行。
7. 故意把同名文件粘贴到同一容器：显示 `NAME_CONFLICT`，整个批次没有部分更改。尝试 Raw/Processed 的无样本落点、已删除/不同实验的无效样本：清晰拒绝。切换页、过滤后不得误操作旧选区；复制剪贴板的来源必须在服务端再次验证。
8. 连续重试同一请求、模拟操作后 UI 失联并重开确认结果不重复；检查仍为相同资产版本/原始 SHA，并确认 Blob 数量不因逻辑复制增大；若无 SQLite 查看工具，保留后端自动化结果作为机器佐证，不把 UI 猜测当作数据库计数。
9. 保存成功、失败、刷新/重启后的 GUI 截图/录屏、人工测试执行日期、操作者、EXE SHA 与源文件 hash 对照。将每项实测 PASS/FAIL 回填；**没有这些证据不标记 Windows 人工已验收**。

## 已知限制 / 安全边界

- S5 不做 S6 重命名/删除/回收，不做 S7 完整性备份恢复、S8 统计、S9 论文。
- S4 跨页保留选区、过滤全集选择、框选仍未实现；S5 只对当前页明确选中可见的 `AssetLinkId` 执行，剪贴板保留显式来源快照，Rust 提交时重新验证。
- S5 不支持物理跨仓库文件复制或外部 Explorer 拖出；只复制/移动关联。
- Windows WebView2 指针拖拽、源文件与 CAS 完整性以及长时间使用，需要人工复核。CI 的拖拽 E2E 是 DOM DragEvent/DataTransfer 模拟，并非真实 Windows 指针动作。
- **在 S0–S7 与恢复演练正式签收前，禁止以珍贵、唯一的真实科研数据进行开发或验收测试。**
