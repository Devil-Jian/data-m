# 论文原始数据归档平台：基础版本设计

- 日期：2026-11-03
- 状态：按已确认总体架构进入实施
- 目标仓库：Devil-Jian/data-m
- 开发分支：feat/archive-foundation

## 1. 目标与使用场景

构建 Windows 单机桌面软件，用于管理论文、Figure / Panel / Table 以及科研原始文件。第一阶段交付可靠的本地数据仓库基础：能够初始化数据目录、建立 SQLite 数据库、把文件以不可变 Blob 方式写入仓库、生成全局 Data ID，并列出已归档数据。

主要使用者是单机环境中的科研人员。核心数据必须保存在本地，断网时可正常使用。默认仓库位置为 D:\PaperArchiveData；如果 D 盘不存在，界面必须让用户选择本地目录，不能静默改到其他位置。

## 2. 目标

1. 建立可安装、可运行的 Windows 桌面应用工程。
2. 建立 SQLite 数据库及版本化迁移机制。
3. 建立本地文件仓库目录结构，数据库与原始文件分离。
4. 文件导入使用流式复制和 SHA-256 校验，避免将大文件整体载入内存。
5. 每次导入生成 UUIDv7 Data ID；相同内容可共享 Blob，但保留各自 Data 记录。
6. 提供归档库状态、目录初始化、文件导入和数据列表 IPC 命令。
7. 提供基本桌面界面：仓库状态、选择数据目录、导入文件、查看数据列表。
8. 建立自动化测试和 Windows CI。

## 3. 非目标

本阶段不实现论文档案、Figure / Panel / Table 的完整 CRUD 页面、.rdpack 完整导入导出、跨电脑合并与冲突解决界面、图片相似性检测、AI 分类、统计分析、云端同步、服务器部署、多用户实时协作，以及 CZI、ND2、FCS 等专用科研格式解析。

这些能力保留在总体产品路线中，不能为了提前实现而破坏当前 ID、Blob 和引用关系设计。

## 4. 约束

- 操作系统：Windows 10 / Windows 11，64 位。
- 默认数据仓库：D:\PaperArchiveData。
- 软件形态：Tauri 2 桌面应用。
- 前端：Vue 3 + TypeScript。
- 核心业务：Rust。
- 数据库：SQLite，WAL 模式，外键开启，写入串行化。
- 原始文件保存在 NTFS 文件系统，不存入 SQLite BLOB。
- 原始文件路径以 SHA-256 内容寻址；数据库保存相对路径。
- Data ID 使用 UUIDv7，跨电脑迁移时不重新分配。
- 核心操作不依赖互联网。
- 文件导入必须流式读写；单篇论文归档通常小于 1 GB 不构成整体内存加载的理由。

## 5. 架构方案比较

### 方案 A：Electron + Vue + Node.js + SQLite

优点：生态成熟，前端开发人员容易上手，桌面插件丰富。
缺点：运行时体积和内存开销较大；文件系统和数据库核心逻辑分散在 Node 与前端边界中；本项目没有需要 Electron 的特殊能力。

### 方案 B：Tauri 2 + Vue + Rust + SQLite（选定）

优点：桌面包较轻；Rust 适合流式文件处理、哈希校验和本地数据库操作；前端与本地文件系统通过明确 IPC 边界隔离；不需要本地 HTTP 服务。
代价：Rust 工程和 Windows WebView2 构建需要额外配置，部分前端插件生态不如 Electron 丰富。

### 方案 C：.NET WPF + SQLite

优点：Windows 原生集成成熟，文件系统、安装和系统对话框支持直接。
缺点：界面与业务生态转向 .NET；若未来希望复用 Vue 前端能力，技术栈统一性较弱。

选择方案 B。本产品是 Windows 单机科研数据管理软件，主要技术风险在大文件安全导入、内容寻址存储和数据迁移；Rust 本地核心层与 Tauri IPC 更适合把这些能力集中管理，同时保留 Vue 界面的开发效率。

## 6. 第一阶段模块边界

### 6.1 前端
- 仓库状态展示；
- 首次初始化或切换仓库目录；
- 调用原生文件选择器；
- 导入文件并刷新列表；
- 展示 Data ID、文件名、分类、大小、SHA-256 和导入时间。

前端不直接读写 SQLite，不直接拼接仓库物理路径。

### 6.2 Rust 应用层
- AppState 管理当前 ArchiveStore；
- Tauri commands 作为唯一 IPC 入口；
- 大文件导入通过后台阻塞任务执行；
- 对外返回序列化 DTO，不暴露 SQLite Connection。

### 6.3 ArchiveStore
- 初始化目录与数据库；
- 执行数据库迁移；
- 流式复制源文件并计算 SHA-256；
- Blob 内容寻址和去重；
- 创建 Data Record；
- 列表查询。

### 6.4 SQLite
第一阶段创建 archive_meta、blobs、data_records、papers、paper_items、data_references、package_imports、import_conflicts 和 operation_logs 表。第一阶段只实现数据资源库实际使用到的查询和写入；论文与图表表先确定稳定结构，后续逐步接入。

## 7. 数据与文件身份

- archive_id、data_id、paper_id、item_id、reference_id 均使用 UUIDv7。
- display_id 是面向用户的编号，不作为关系外键。
- sha256 是 Blob 内容身份，不是 Data Record 身份。
- 同一文件内容导入两次：生成两个 Data ID，指向同一个 Blob。
- 文件名相同但内容不同：生成不同 Blob 和不同 Data ID。
- 原始文件不允许覆盖；修改内容视为新文件导入。

## 8. 文件导入一致性

导入顺序：
1. 校验源路径和文件可读性。
2. 将文件按固定大小缓冲区流式复制到 staging，同时计算 SHA-256。
3. 对 staging 文件执行 flush / sync。
4. 将 staging 文件移动到基于 SHA-256 的正式 Blob 路径。
5. 开启 SQLite 事务，写入 Blob（若不存在）和 Data Record。
6. 提交事务并返回记录。

文件系统与 SQLite 不能组成单一原子事务。若数据库提交失败，可能留下未被数据库引用的孤立 Blob；后续清理任务只能清理确认无引用的孤立文件，不能删除被引用文件。

## 9. 默认数据目录行为

- Windows 默认：D:\PaperArchiveData。
- PAPER_ARCHIVE_DATA_DIR 环境变量可用于开发和自动化测试覆盖默认路径。
- D 盘不存在时，应用保持未初始化状态，提示用户通过目录选择器选择本地目录。
- 用户显式选择目录后，程序验证路径为绝对本地路径，再初始化目录和数据库。
- 不自动将数据移动到 C 盘或用户目录。

## 10. IPC 接口初稿

- get_archive_status() -> ArchiveStatus
- initialize_archive(path: Option<String>) -> ArchiveStatus
- list_data_records() -> Vec<DataRecord>
- import_data_file(path: String) -> DataRecord

ArchiveStatus 包含 initialized、data_dir、archive_id、schema_version。
DataRecord 包含 data_id、display_id、sha256、original_filename、file_extension、category、file_size、created_at。

所有命令错误返回可读错误信息；不得把内部错误堆栈直接暴露给前端。

## 11. 测试与质量门槛

必须覆盖：
- 初始化目录结构和数据库；
- 重开仓库时 archive_id 保持不变；
- 导入后原始文件 SHA-256 与源文件一致；
- 相同内容的多条 Data Record 共享 Blob，但 Data ID 不同；
- 同名不同内容文件不被覆盖；
- 不存在的源文件导入失败且不产生 Data Record；
- 数据列表包含准确文件大小、哈希和元数据；
- Windows CI 执行 Rust 测试、格式检查和前端构建。

## 12. 后续阶段

本阶段通过后，下一阶段按顺序实现论文与图表对象 CRUD、引用关系管理、检索预览，然后实现 .rdpack 导出、预检、导入和冲突处理。所有后续功能必须继续遵守全局 UUID、Blob 不可变和导入幂等原则。
