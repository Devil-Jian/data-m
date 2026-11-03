# Tauri IPC 接口规范 V1.0（基础阶段）

## 1. 约束

- 前端只通过 Tauri IPC 调用本地能力。
- 前端不直接连接 SQLite。
- Rust command 不暴露数据库连接、内部目录句柄或任意文件系统写入接口。
- 所有文件导入使用源路径参数，由 Rust 端流式读取。
- 大文件导入放入 Tauri blocking task pool，避免阻塞 WebView UI。
- 错误信息使用可读字符串返回；详细错误写入本地日志。

## 2. DTO

### ArchiveStatus
| 字段 | 类型 | 说明 |
|---|---|---|
| initialized | boolean | 当前仓库是否已初始化 |
| data_dir | string | 当前数据目录或默认候选目录 |
| archive_id | string 或 null | 归档库 UUIDv7 |
| schema_version | number 或 null | 数据库结构版本 |

### DataRecord
| 字段 | 类型 | 说明 |
|---|---|---|
| data_id | string | UUIDv7 |
| display_id | string | RD- 前缀展示编号 |
| sha256 | string | 文件 SHA-256 |
| original_filename | string | 原始文件名 |
| file_extension | string | 小写扩展名 |
| category | string | image / spreadsheet / document / other |
| file_size | number | 字节数 |
| created_at | string | UTC ISO 时间 |

## 3. Commands

### get_archive_status
输入：无  
输出：ArchiveStatus  
行为：返回当前仓库状态；未初始化时返回默认候选目录。

### initialize_archive
输入：path 可选字符串  
输出：ArchiveStatus  
行为：path 为空时使用默认目录；path 有值时验证绝对本地路径并初始化。D 盘不存在时不得静默回退。

### list_data_records
输入：无  
输出：DataRecord 数组  
行为：按 created_at 倒序返回当前未软删除的数据记录。后续增加分页参数时保持兼容。

### import_data_file
输入：path 字符串  
输出：DataRecord  
行为：Rust 端读取文件、流式计算 SHA-256、写入 Blob 并创建 Data Record。文件系统或数据库失败时返回错误，不返回部分成功。

## 4. 前端文件选择

目录选择：
- 调用 open({ directory: true, multiple: false })。
- 用户取消时不调用 initialize_archive。
- 选择路径后调用 initialize_archive。

文件选择：
- 调用 open({ multiple: true })。
- 用户取消时不执行导入。
- 多文件按顺序调用 import_data_file，避免无上限并发写入。

## 5. 错误类别

- ARCHIVE_NOT_INITIALIZED
- INVALID_DATA_DIRECTORY
- SOURCE_NOT_FOUND
- SOURCE_NOT_READABLE
- STORAGE_IO_ERROR
- DATABASE_ERROR
- BLOB_INTEGRITY_ERROR
- UNSUPPORTED_SCHEMA_VERSION

前端可以先展示错误消息，后续再按稳定错误码提供本地化提示。
