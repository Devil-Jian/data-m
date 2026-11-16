# S4 实施报告（未验收）

- TASK_ID：S4
- 分支：`feat/s4-real-asset-browser`
- 基线：`main` @ `fbf84716871465a4fc3f230aa3c4009c80e35dc1`
- 当前提交（初次 PR）：`29d767f0e5c40529e06c2b5d8cdb0376fd9ea5bd`
- PR：[Draft #47](https://github.com/Devil-Jian/data-m/pull/47)
- 状态：`in_progress`（不能标为 machine_passed / manual_pending / accepted）

## 已实施
- 新增 Rust `list_assets`、`get_asset` 只读查询逻辑；按 assay、stage、group、replicate 和名称过滤；名称/时间/大小稳定排序，最多每页 100 条。
- 响应区分 `AssetId`、`AssetLinkId`、`AssetVersionId`，返回 SHA-256、大小、版本、引用标志而不返回源路径或 CAS 写路径。
- 实验阶段视图接入文件浏览器；支持搜索、排序、分页、当前页 Shift/Ctrl、多选、文件详情。
- Rust 测试 `s4_100_files_stable_pagination_and_detail`、`s4_filters_bad_cursor_and_context_identity`；浏览器 E2E `@S4` 明确使用 Mock。
- Windows CI 新增 S4 筛选测试，要求非空。

## 机器验收（严禁虚报）

| 项目 | 命令 | 结果 |
|---|---|---|
| S4 Rust 集成 | `cargo test --no-default-features s4_` | 未执行/待 CI |
| S4 浏览器 Mock | `npm run test:e2e:desktop -- --grep "@S4"` | 未执行/待 CI |
| 完整 Rust | `cargo test --no-default-features` | 未执行/待 CI |
| TypeScript & Vite | `npm run build:desktop` | 未执行/待 CI |
| Rust 格式和 check | `cargo fmt --all -- --check`; `cargo check --locked` | 未执行/待 CI |
| Windows EXE | `npm run tauri:build` | 未执行/待 CI |

以上没有运行证据，不代表成功。当前执行容器无 Cargo/rustfmt，且不能直连 GitHub 克隆仓库；使用已连接 GitHub API 写入分支。CI 完成后须记录真实 head_sha、命令、退出码、执行数量和构建链接。

## 未满足的机器验收项 / 风险
- M1：Rust 使用 100 条 SQLite 测试插入，不等于 S3 真实导入 100 文件后 UI 计数；尚需端到端测试。
- M2：已以 AssetLinkId 作为选择 ID，但跨容器拖拽及跨视图同资产多关联仍需专门回归。
- M3：选择目前限定在当前分页；跨分页和框选/过滤逻辑全集的一致性尚未达到规范。
- M4：分页和错误游标测试已编写，尚未运行；偏移游标在并发插入时可能发生漂移，需快照/键集机制。
- 当前没有 Windows GUI 实测，也没有已验证 EXE 和 SHA。
- 仍处于受控测试仓库阶段，S7 签收前禁止导入珍贵科研数据。

## Windows 11 人工验收记录模板
1. 用 S3 从专门创建、可丢弃的文件夹向同一实验 Raw/Processed/Statistics/Display 导入 100 个测试文件。导入前后对比源目录文件数量、SHA-256 和修改时间。记录实际结果及截图。
2. 重启 EXE，打开“项目管理 → 实验 → 分组与样本”，选择 Group / Replicate / Stage；检查每页 50 条、数量、排序、搜索和具体资产内容，不得混入演示数据。
3. 两个阶段中建立同一 DataAsset 的不同 AssetLink（若有测试 fixture），核对所选 ID、详情上下文和文件版本；跨容器选择不串位。
4. 验证 Ctrl、Shift、Ctrl+A、框选及分页/过滤后选择行为；任何不一致标记 failed，不当作通过。
5. 记录 Windows 11 版本、DPI、EXE 文件 SHA、受测 commit、执行者和日期，以及错误截图/录屏。完成后人工签字。
