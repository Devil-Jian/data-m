# 数据库详细设计 V1.0（基础阶段）

## 1. 数据库文件

默认位置：D:\PaperArchiveData\database\archive.db

SQLite 配置：
- journal_mode = WAL
- synchronous = FULL
- foreign_keys = ON
- busy_timeout = 5000
- 数据库写入通过单一连接互斥锁串行化
- schema 版本使用 PRAGMA user_version 管理

原始文件不写入 SQLite BLOB。数据库只保存元数据、哈希和仓库相对路径。

## 2. ER 关系

- archive_meta：单个归档库元数据。
- blobs：按 SHA-256 唯一的物理文件实体。
- data_records：用户管理的数据记录；多个 Data Record 可指向同一个 Blob。
- papers：论文档案。
- paper_items：Figure、Panel、Table 统一对象；Panel 使用 parent_id 指向 Figure。
- data_references：Data Record 与 Panel / Table 的多对多关系。
- package_imports：已导入的归档包及其幂等标识。
- import_conflicts：导入冲突快照。
- operation_logs：关键操作记录。

## 3. 表结构

### archive_meta
| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| singleton | INTEGER | PK, CHECK=1 | 单行表 |
| archive_id | TEXT | UNIQUE, NOT NULL | UUIDv7 |
| schema_version | INTEGER | NOT NULL | 结构版本 |
| created_at | TEXT | NOT NULL | UTC ISO 时间 |
| updated_at | TEXT | NOT NULL | UTC ISO 时间 |

### blobs
| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| sha256 | TEXT | PK, length=64 | 小写十六进制 SHA-256 |
| file_size | INTEGER | NOT NULL, >=0 | 文件字节数 |
| relative_path | TEXT | UNIQUE, NOT NULL | 相对仓库路径 |
| media_type | TEXT | NULL | MIME 类型 |
| created_at | TEXT | NOT NULL | 首次入库时间 |

### data_records
| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| data_id | TEXT | PK | UUIDv7 |
| display_id | TEXT | UNIQUE, NOT NULL | RD- 前缀展示编号 |
| blob_sha256 | TEXT | FK blobs, NOT NULL | 文件内容身份 |
| original_filename | TEXT | NOT NULL | 原始文件名 |
| file_extension | TEXT | NOT NULL | 小写扩展名，可为空 |
| category | TEXT | NOT NULL | image / spreadsheet / document / other |
| note | TEXT | NOT NULL DEFAULT '' | 备注 |
| created_by | TEXT | NULL | 操作者名称快照 |
| created_at | TEXT | NOT NULL | 入库时间 |
| deleted_at | TEXT | NULL | 软删除时间 |

### papers
| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| paper_id | TEXT | PK | UUIDv7 |
| title | TEXT | NOT NULL | 论文标题 |
| doi | TEXT | NULL | DOI |
| journal | TEXT | NULL | 期刊 |
| authors | TEXT | NULL | 作者 |
| year | INTEGER | NULL | 年份 |
| status | TEXT | NOT NULL DEFAULT 'draft' | 论文状态 |
| revision | INTEGER | NOT NULL DEFAULT 1 | 本地修订号 |
| content_hash | TEXT | NULL | 结构内容摘要 |
| created_at | TEXT | NOT NULL | 创建时间 |
| updated_at | TEXT | NOT NULL | 更新时间 |

### paper_items
| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| item_id | TEXT | PK | UUIDv7 |
| paper_id | TEXT | FK papers, NOT NULL | 所属论文 |
| parent_id | TEXT | FK paper_items, NULL | Panel 的父 Figure |
| item_type | TEXT | CHECK figure/panel/table | 对象类型 |
| label | TEXT | NOT NULL | Figure 2 / 2A / Table 1 |
| sort_order | INTEGER | NOT NULL DEFAULT 0 | 排序 |
| asset_blob_sha256 | TEXT | FK blobs, NULL | 最终图/表文件 |
| created_at | TEXT | NOT NULL | 创建时间 |
| updated_at | TEXT | NOT NULL | 更新时间 |

### data_references
| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| reference_id | TEXT | PK | UUIDv7 |
| data_id | TEXT | FK data_records, NOT NULL | 被引用数据 |
| item_id | TEXT | FK paper_items, NOT NULL | 引用目标 |
| reuse_note | TEXT | NULL | 复用说明 |
| created_at | TEXT | NOT NULL | 建立时间 |
| UNIQUE(data_id, item_id) | | | 同一对象关系不重复 |

### package_imports
| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| package_id | TEXT | PK | 归档包 UUID |
| source_archive_id | TEXT | NOT NULL | 来源归档库 |
| package_schema_version | INTEGER | NOT NULL | 包格式版本 |
| package_sha256 | TEXT | NOT NULL | 包文件哈希 |
| imported_at | TEXT | NOT NULL | 导入时间 |
| import_status | TEXT | NOT NULL | 状态 |

### import_conflicts
| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| conflict_id | TEXT | PK | UUIDv7 |
| package_id | TEXT | FK package_imports | 来源包 |
| entity_type | TEXT | NOT NULL | paper/data/reference 等 |
| entity_id | TEXT | NOT NULL | 冲突对象 ID |
| local_hash | TEXT | NULL | 本地摘要 |
| incoming_hash | TEXT | NULL | 包内摘要 |
| resolution | TEXT | NULL | 人工处理结果 |
| created_at | TEXT | NOT NULL | 发现时间 |

### operation_logs
| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| operation_id | TEXT | PK | UUIDv7 |
| operation_type | TEXT | NOT NULL | import/export/delete/restore 等 |
| entity_type | TEXT | NULL | 业务实体类型 |
| entity_id | TEXT | NULL | 业务实体 ID |
| detail_json | TEXT | NULL | 结构化操作详情 |
| created_at | TEXT | NOT NULL | UTC 时间 |

## 4. 索引

- data_records(blob_sha256)
- data_records(created_at)
- papers(title)
- papers(doi)
- paper_items(paper_id, sort_order)
- paper_items(parent_id)
- data_references(data_id)
- data_references(item_id)
- import_conflicts(package_id)

## 5. 关键不变量

1. 一个 Data Record 只有一个 Blob。
2. 一个 Blob 可被多个 Data Record 共享。
3. Data ID 与 SHA-256 不等价。
4. 原始文件内容变更必须创建新 Blob 和新 Data ID。
5. 业务关系通过 UUID 外键建立，不使用展示编号。
6. 被引用数据默认不允许物理删除。
7. 任何导入操作都必须在数据库事务中提交业务元数据。
8. 文件系统中的孤立 Blob 只能在确认没有数据库引用后清理。
