# Windows 原生版（迁移测试）

当前新架构位于 `feat/winui-native`，使用 **C# + .NET 10 + WinUI 3**。原有 Tauri/Vue/Rust 项目仍保留供兼容和回退，不再作为新版本界面开发主线。

- [原生版操作手册](native/操作手册.md)
- [Windows 原生 CI 构建](https://github.com/Devil-Jian/data-m/actions/workflows/native-winui.yml)
- 原生项目：`native/PaperArchive.Core`、`native/PaperArchive.Native`。
- 数据格式：直接兼容原 SQLite v1/v2/v3 与 SHA-256 Blob 仓库。升级前须备份整个仓库。
- 当前阶段已实现基础文件夹扫描、批量分类与编号、归档、归档后资源列表；论文内嵌 PDF/DOCX 阅读及复杂图像/Excel 预览尚在迁移中。
- 安装包与图形化人工验收完成前，保留为测试版，不代替稳定版本。

---

# 论文原始数据归档平台 V0.5

Windows 10/11 离线单机应用。当前版本重点改造**归档前的整理体验**，采用紧凑型桌面文件管理器界面；正式归档后继续使用只读论文资源浏览器。

## 典型操作（V0.5）

1. 点击「论文归档」→「打开论文文件夹」，选择含论文正文、Figure、Table、Others 的根目录。
2. 系统自动识别组合图、子图、统计原始图与统计数据的归属。
3. 如果目录与文件名规范，可以直接「完成归档」。
4. 若存在异常，在中间文件表格中使用 Ctrl/Shift 多选，批量调整资源角色、逻辑移动、指定 Figure/Panel 或使用 F2 批量重命名；命名窗口先展示变更预览。
5. 批量操作支持 Ctrl+Z 撤销、Ctrl+Y 重做，右键提供常用分类菜单。
6. 「完成归档」将经过确认的逻辑路径/数据归属交给 Rust 批量导入服务；重复名称或非法路径将拒绝执行。完成后进入论文资源库。

**操作不会重命名或移动源文件。** 整理得到的是归档库内的逻辑路径与图表映射；真实源文件名、SHA-256 和内容保持不变。正式归档前自动校验数据完整性。

支持原始 PNG/JPEG/WebP 图片的简易检查器预览；TIFF 与其他科研数据格式目前优先展示文件元信息，深度解析仍待后续版本。

## 标准目录

```text
论文标题/
├── 论文正文.docx
├── Figure/
│   ├── 组合图/
│   │   └── Figure1.png
│   ├── 子图/
│   │   ├── 1-A-1-细胞代表图.tif
│   │   └── 1-B-1-染色图.tif
│   └── 统计原始图/
│       └── 1-A-2-重复样本.tif
├── Table/
│   ├── 1-A-ROS统计.xlsx
│   └── T1-临床特征.xlsx
└── Others/
    └── 附录.pdf
```

兼容大小写不同的 Figure/Panel 编号及 `1-A1.png`、`2-a9.tif` 等旧命名。初次导入不需要逐个创建 Figure、Panel 和文件引用。

## 工程与兼容

- Tauri 2 + Vue 3 + TypeScript + Rust + SQLite；Windows 10/11 x64；默认仓库 `D:\PaperArchiveData`。
- SQLite WAL + FULL、外键、迁移 v1/v2 → v3、UUIDv7 与 SHA-256 不可变原始文件。
- V0.5 沿用 v3 数据库结构。批量整理计划通过新增 IPC 在导入前校验，不会改动旧有归档数据。
- 对于已经导入却未完成的旧版草稿，目前仍有部分后续编辑功能需要进一步整合到新文件管理器；新界面优先支持从**原始论文文件夹**启动完整归档。
- Windows CI 负责 Rust 测试、前端构建和 NSIS 安装包。真实 Windows 操作体验仍需人工验收。
- 本地数据仓库请独立备份；当前未提供 rdpack 跨电脑无损汇总、完整修订历史恢复和所有科研文件格式的内嵌预览。

## 开发运行

```powershell
npm ci
npm run tauri dev
cargo fmt --manifest-path src-tauri/Cargo.toml --all -- --check
cargo test --locked --manifest-path src-tauri/Cargo.toml --all-targets
npm run build
npm run tauri build
```

代码开发分支：`feat/archive-foundation`；审核 PR：https://github.com/Devil-Jian/data-m/pull/1。
