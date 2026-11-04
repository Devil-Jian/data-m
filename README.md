# 论文原始数据归档平台（Paper Archive）

面向 Windows 10 / 11 的 **EndNote 式科研论文数据归档软件**，以**论文为主条目**，关联论文正文、Figure、Panel、Table、原始图片、统计表和其他科研文件。

当前版本：**v0.2.0（开发测试版）**。本机独立运行，不需要服务器；数据仓库默认保存在 `D:\PaperArchiveData`。

## v0.2.0 的核心体验

| 区域 | 功能 |
| --- | --- |
| 左栏 | 论文条目、论文标题与 DOI 搜索、创建论文、切换文章 |
| 中栏 | PDF / DOCX 论文内嵌预览；查看文章中的 Figure、Panel、Table |
| 右栏 | 按 Figure、Panel、Table 分组的论文资源库；文件详情、SHA-256、大小、归档时间、跨论文引用位置 |

可从右栏为某个图表**导入原始文件**，也可**关联 v0.1.0 已经导入的 Data ID**。同一文件可以被多个图表引用，引用关系不会复制或覆盖原始文件。

### 点击文章图表跳转到原始资源

文章原文中的图片通常并不包含原始数据 ID，所以需要进行一次图表位置绑定：

**PDF 正文**

1. 左侧选择论文，在中栏导入 PDF。
2. 右侧创建 Figure / Panel / Table，并导入或关联对应原始数据。
3. 点击图表资源组中的「绑定文章位置」。
4. 在 PDF 页面上拖动鼠标**框选图片或表格区域**。
5. 保存后再次点击该图像区域，右栏会自动选中、滚动到对应资源组。

**DOCX 正文**

1. 左侧选择论文，在中栏导入 DOCX。
2. 右侧建立图表资源组并关联原始文件。
3. 点击「绑定文章位置」，然后**点击 DOCX 文章中的图片或表格**。
4. 此后再点击该图片/表格，右侧定位相应资源目录。

部分 PDF 中与资源标签完全匹配的图注文字也可以直接点击。**目前不承诺自动识别所有 PDF 图片内容及原始数据关系**；图像区域与 Data ID 的映射需在首次使用时建立。

## 使用与升级

- Windows 10 / Windows 11 x64。
- 从 [GitHub Actions](https://github.com/Devil-Jian/data-m/actions) 下载对应 CI 的 `paper-archive-windows-nsis` 安装包。
- 安装后首次启动使用 `D:\PaperArchiveData`；D 盘不存在时会提示选择本地目录。
- 从 v0.1.0 升级到 v0.2.0 时，SQLite schema 由 v1 **增量迁移到 v2**，旧的 Blob、Data ID、文件名和 SHA-256 记录保留。
- **建议升级前先备份整个 `D:\PaperArchiveData` 文件夹**（软件完全退出后复制）。
- 默认原始文件不可变，多个 Data Record 可以共用同一 SHA-256 Blob。物理文件不是按原始文件名命名，而是内容寻址保存；可点击「在 Windows 资源管理器中定位文件」查看。

## 已实现的技术基础

- Tauri 2 + Vue 3 + TypeScript + Rust。
- SQLite WAL + FULL、外键约束、schema v1 → v2 原地迁移。
- Rust 1 MiB 固定缓冲区流式导入，SHA-256，文件 staging，同内容 Blob 去重。
- UUIDv7 全局唯一 Paper / Figure / Panel / Data / Reference 标识。
- 原始数据与图表多对多引用，保留同一 Data ID 的多篇论文使用记录。
- PDF.js 本地按页预览、PDF 归一化矩形热点；docx-preview 本地 DOCX 只读 HTML 预览和图片/表格绑定。
- 文件详情面板、支持 PNG / JPEG / WebP 图片详情预览、系统资源管理器定位。
- Windows CI 检查 Rust 格式、单元测试、Vue 构建，生成 NSIS 安装程序。

## 当前限制

- 原文正文预览限 PDF / DOCX；旧版 DOC、特殊科研格式、TIFF 和 XLSX 原始文件可存储和查看元信息，但并非全部支持**内嵌内容预览**。
- 论文内嵌预览当前上限 80 MiB，图片详情内嵌预览上限 24 MiB；超过阈值会明确提示，不会修改或删除原始数据。
- 尚未实现自动从 Word / PDF 解析出所有图表并自动匹配文件。PDF 图像区域与 DOCX 图片绑定需要用户确认一次。
- 尚未完成 `.rdpack` 跨电脑导入/汇总、自动备份恢复、文件删除/整理、高级搜索和完整论文元数据编辑；这些仍属于后续 MVP 阶段。
- 尚未完成真实 Windows 10 / 11 安装后的端到端人工验收。此版本不应作为唯一科研数据备份。
- 原始文件的哈希仓库路径可能与原始文件名不同；完整原始文件名保存在 SQLite 元数据中。

## 本地开发

需要 Node.js 22、Rust 1.90+、Windows C++ Build Tools 和 WebView2。

```powershell
npm ci
npm run tauri dev
```

运行测试：

```powershell
cargo fmt --manifest-path src-tauri/Cargo.toml --all -- --check
cargo test --locked --manifest-path src-tauri/Cargo.toml --all-targets
npm run build
```

Windows NSIS 安装包：

```powershell
npm run tauri build
```

## 设计文档

- `docs/superpowers/specs/2026-11-04-paper-first-workspace.md`：论文优先三栏界面、图表热点与数据关联。
- `docs/superpowers/specs/2026-11-03-paper-archive-foundation-design.md`：本地归档核心。
- `docs/database/database-design-v1.0.md`：基础数据模型说明（schema v2 以实际迁移脚本为准）。
- `src-tauri/migrations/0002_paper_workspace.sql`：新增论文正文和图表热点字段的迁移。

代码开发分支：`feat/archive-foundation`；草稿 PR：[#1](https://github.com/Devil-Jian/data-m/pull/1)。
