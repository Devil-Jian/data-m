# 论文原始数据归档平台 V0.4

Windows 10/11 离线单机软件。采用 Tauri 2 + Vue 3 + TypeScript + Rust + SQLite，默认仓库 D:\PaperArchiveData。

## 两个界面

- 论文归档：选择整篇论文文件夹、扫描预览、自动识别 Figure/Panel/Table，调整文件角色和归属、归档检查、完成归档。
- 论文资源库：只显示正式归档的论文；左侧论文目录、中间 PDF/DOCX 文章与关系视图、右侧原始数据；点击文章图表快速定位，默认只读。

## 文件夹命名约定

论文目录包含一份位于根目录的 PDF 或 DOCX，以及 Figure、Table、Others 文件夹。

    论文标题/
      manuscript.docx
      Figure/
        组合图/
          Figure1-结果.png
        子图/
          1-A-1-代表图.tif
        统计原始图/
          1-A-2-重复实验.tif
      Table/
        1-A-ROS.xlsx
        T1-临床特征.xlsx
      Others/
        analysis.py

Figure/组合图 识别最终组合图；Figure/子图 识别展示子图；Figure/统计原始图 识别未出现在最终组图中的统计原始图；Table/1-A 识别 Figure 1A 的原始统计数据；Table/T1 识别独立 Table 1。编号大小写不敏感，兼容 1-A1.png 和 2-a9.tif 等旧格式。

扫描时展示识别报告，不确定的文件保留在待分类 / Others。导入为论文草稿后，可以修正图表归属、文件角色，或补充正文及原始文件，不改变原文件的内容和名称。

## 归档流程

1. 顶部选择“论文归档”，点击“导入整篇论文文件夹”。
2. 阅读扫描识别报告，确认后一次导入为待归档草稿。
3. 检查 Figure/Panel/Table 和数据关系，必要时修正角色和位置绑定。
4. 点击“重新检查”；必须处理的错误消除后点击“完成归档”。
5. 自动进入“论文资源库”；浏览文章图像和原始数据的对应关系。

归档状态：draft（草稿）、archived（只读归档）、revising（修订草稿）。
完成归档时保存不可变的关系快照和修订号。当前 V0.4 尚未提供在资源库中选择历史快照并回滚的完整界面。

## 数据存储与兼容性

- 默认 D:\PaperArchiveData；D 盘不存在时要求选择本地目录，不自动写入 C 盘。
- SQLite WAL、FULL 同步、外键和增量迁移。V0.4 的 schema v3 可从 v1/v2 升级，原始 Blob 与 Data ID 不重新分配。
- 流式 SHA-256 文件导入；相同内容 Blob 去重、每次导入拥有独立 UUIDv7 Data ID。
- 完成归档前验证正文、引用关系和原始文件 SHA-256；关系调整不会移动、重新编码或删除原始文件。
- PDF / DOCX 预览、JPEG / PNG / WebP 内嵌资源预览。TIFF、XLSX 及专用科研文件可以归档、查看元信息并定位原文件，暂不保证全部内嵌解析。
- 升级前务必完全退出软件并备份整个归档库目录。

## 当前限制

没有实现 rdpack 完整无损跨电脑导入汇总、自动备份、完整历史快照浏览/恢复和所有科研格式的原位预览。尚需实际 Windows 10/11 安装后的人工测试，不能把本软件当作科研数据的唯一备份。

## 开发

需要 Node.js 22、Rust 1.90+、Windows C++ Build Tools、WebView2。运行 npm ci、npm run tauri dev；测试运行 cargo fmt --manifest-path src-tauri/Cargo.toml --all -- --check 和 cargo test --locked --manifest-path src-tauri/Cargo.toml --all-targets；安装包由 npm run tauri build 生成。

功能分支 feat/archive-foundation；代码审查 PR：https://github.com/Devil-Jian/data-m/pull/1
