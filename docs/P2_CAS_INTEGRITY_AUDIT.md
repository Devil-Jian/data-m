# P2 · SQLite/CAS 只读一致性审计（Issue #16）

此增量属于 Data-M 的非破坏性验证层，**仅对测试库开放**。暂不通过 Tauri IPC 暴露给用户。

## 实现

`apps/desktop/src-tauri/src/cas_audit.rs` 提供 `audit_registered_blobs(&Connection, cas_root)`：

- 对 SQLite 执行 `PRAGMA integrity_check(10)` 和 `PRAGMA foreign_key_check`，报告异常而不执行修复。
- 只读取 `blobs` 表中登记的 `sha256` / `size_bytes`；CAS 对象路径仅由规范化的 **64 位小写十六进制 SHA-256** 拼接：`objects/aa/bb/<sha256>`。数据库内容不能直接变成任意文件系统路径。
- 在访问文件前检查 CAS 根目录、对象扇出目录及目标对象类型；发现最终路径符号链接、缺失文件或不规范类型会直接报告，不跟随目标符号链接。
- 通过与 CAS 导入一致的有界堆内存流式 SHA-256 重新校验对象，区分 `Missing`、`WrongSize`、`HashMismatch`、`UnsafePath`、`Unreadable`、`InvalidDigest`。
- 结果只包含 hash、问题类型和总数，不包含用户源路径。支持空数据库，不修改 SQLite，也不创建 CAS 文件。
- 仅扫描数据库已注册对象；**不会**扫描/删除孤立 CAS 文件或 staging 目录，因此不能把干净结果理解为完整恢复证明。

## 自动化测试

Rust 临时测试目录覆盖：
1. 已注册的正常 Blob 能通过审计；原源文件、数据库记录、修改时间不变。
2. 同大小但不同内容的对象触发 `HashMismatch`，不会自动修复。
3. 缺失对象触发 `Missing`，大小错误触发 `WrongSize`。
4. 人为破坏外键的临时 SQLite 测试库会报告外键错误，检查不修改数据库。
5. 相对路径/缺失 CAS 根目录直接拒绝，且不会自动创建目标。
6. Unix 下符号链接替换对象会触发 `UnsafePath`，不会读取链接目标。
7. 上游 CAS 原型同时覆盖 Windows 1 MiB 堆缓冲与重复去重。

```powershell
cd apps/desktop/src-tauri
cargo test --no-default-features
cargo fmt --all -- --check
cargo check
```

## 不满足的正式验收门槛

- 仍需仓库独占锁、打开的文件句柄与全路径组件竞争保护；在并发恶意替换路径的情况下当前检查并不提供安全保证。
- 未实现孤儿 Blob/staging 扫描、自动/手动 reconcile，未实现 WAL-safe 备份及全量恢复演练。
- 大规模数据库的重哈希检查需要队列、进度、节流、暂停/取消与校验缓存。
- 未接入 Tauri UI、持久数据库 Repository API，不应对用户科研数据执行。
- Windows WebView2 原生客户端、断电/故障注入和实际数据源保护检查尚未验收。

此 PR 属于 [Issue #16](https://github.com/Devil-Jian/data-m/issues/16) 的**部分交付**，不能用于宣布 P2 或软件 v1.0 完成。
