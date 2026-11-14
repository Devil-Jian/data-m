# P1 · Windows Explorer 原生拖入只读预览（Issue #4）

## 功能范围

Data-M 的 Tauri 2 Windows WebView2 接收系统文件拖放事件。当前版本只建立确认前的安全预览，不触碰用户真实科研文件。这个功能的重点是 **P1 原生交互与可见反馈**，不能被解释为 P2 正式资产导入。

## 操作路径

1. Windows Explorer 选择一个或多个普通文件/文件夹，拖入 Data-M 窗口。
2. 松手后 Tauri 原生事件向只读 Rust 命令 `inspect_drop_paths` 提交临时 OS 路径。
3. Rust 通过 `symlink_metadata` 判断文件/文件夹、读取文件大小，仅返回**文件名**及类型、大小、是否可进一步检查。
4. UI 显示前 16 个文件条目及总项目数；Rust 最多检查 100 项，额外数量明确标示。
5. 用户可以随时关闭预览。不会执行复制、移动、删除、递归扫描或数据库写入。

## 安全边界

- 无路径上传、无网络请求。操作系统完整路径不写入 React 状态，也不回传至前端预览数据。
- 不跟随符号链接；不递归遍历文件夹；不猜测数据内容或文件哈希。
- 显示“可进一步检查”并不承诺该文件最终可导入；后端必须重新验证来源、权限、文件哈希与安全存储后才可入库。
- 对空值、相对路径、超长路径、不可读文件、符号链接和非常规文件类型给出保守判断。
- 不对用户源文件调用写入、移动、重命名或删除。
- 对重复快速拖入使用 requestId 防止旧请求覆盖新预览；关闭面板后异步请求不得重新打开预览。
- Tauri 命令本身是**只读检查**，不是授权导入接口。
- 项目尚未接入 SQLite / CAS，也没有实现真正的 Explorer 文件复制。

## 开发与测试

```powershell
npm.cmd install
npm.cmd run test:desktop
npm.cmd run build:desktop
npm.cmd run tauri:dev
```

Rust 测试：

```powershell
cd apps/desktop/src-tauri
cargo fmt --all -- --check
cargo test
cargo check
```

CI：`.github/workflows/desktop-windows.yml` 运行 TypeScript 构建、桌面预览状态测试、原有浏览器 E2E、Rust 单元测试及 Windows 二进制构建。

## Windows WebView2 原生手工验收（待执行）

- [ ] 把 `.tif` / `.xlsx` 拖入原生 Tauri 窗口，展示**名称**和文件大小，文件本身不改变。
- [ ] 拖入一个目录：只显示“文件夹”，不枚举其内容。
- [ ] 先拖 A 后立刻拖 B：只显示最后一个完成的检查结果。
- [ ] 在检查期间关闭预览：结果不会自动重新出现。
- [ ] 拖入超出 100 项：提示数量上限，无长时间 UI 冻结。
- [ ] 路径包含中文、空格和 Unicode：能显示文件名。
- [ ] 有符号链接或无效路径：标为不可用，不读取内容。
- [ ] 在 Windows 高 DPI 125% / 150% 和不同窗口尺寸下，提示不遮挡主要文件视图。
- [ ] 确认源文件的 SHA-256、大小、修改时间保持不变。

## 后续（P2）

单独实现用户确认、目标容器选择、流式复制至 staging、SHA-256 校验、CAS 落盘与 SQLite 事务、失败恢复，不能复用这个预览命令直接执行导入。
