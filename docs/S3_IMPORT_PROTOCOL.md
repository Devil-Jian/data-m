# S3 导入协议设计（S2 机器通过后实施）

本设计不是已实现或验收声明。只允许受控测试库、普通文件和可丢弃源。S1–S3 的人工验收由用户明确延后；不自动合并，不开放生产导入。

## 接口与任务

单个结构化 IPC `import_jobs({request})`。action 为 prepare/start/get/list/cancel/retry；prepare 提供 paths、target、requestId，其余按 action 提供 jobId，禁止任意数据库或 CAS 目标路径。

target `{assayId,stage,groupId?,replicateId?}`：Raw/Processed 必须是该实验的活跃样本，组 ID 校验或从样本推导；Statistics/Display 为实验级，禁止混入样本/组。所有 SQL 提交时再次验证活跃目标。

字段规则：prepare 必须有 requestId/paths/target，不得带 jobId；start/get/cancel/retry 只接受 jobId；list 不接受其他字段。prepare 的同 requestId/同载荷返回同一 job，不同载荷冲突。start 对 running/终态任务不创建重复 worker；cancel 重复调用稳定返回当前状态；retry 仅对失败/取消/中断任务排队未完成文件，running/completed 时为幂等查询。所有动作都验证字段形状。

统一响应 `{jobs:[JobView]}`；JobView 含 id/status/target/files/totalBytes/copiedBytes/completedCount/errorCode，FileView 含 id/name/sizeBytes/sha256/status/assetVersionId/errorCode。不返回源绝对路径、OS 错误字符串或任意磁盘写路径。

prepare 仅检查并持久化原生源路径与文件身份，返回待确认任务；UI 显式确认后 start 才复制。启动后台工作线程，进度通过安全事件与查询接口提供。取消状态持久化；重试同一任务只处理未完成文件，已提交文件不重复登记。

## 数据库和仓库

- 沿用受控测试库及 application_id。已知 v1 库才能事务升级到 v2；先只读验证品牌、版本、完整 schema/history。未知库不执行迁移、PRAGMA 写入或修复。
- v2 增加 import_jobs/import_files 等持久日志；保留原有项目/样本与 s1_requests。更新资产名称唯一索引，使用 `assay_id,COALESCE(group_id,''),COALESCE(replicate_id,''),stage,display_name COLLATE NOCASE`，既允许不同样本同名，又阻止实验级 NULL 作用域漏过重名检查。
- CAS 固定在受控库父目录内，staging 与 objects 同卷；拒绝任何根/子目录符号链接或 reparse point，所有相对名仅由内部十六进制 ID/hash 生成。
- Windows 仓库锁使用进程生存期 OS 文件共享/锁语义，进程退出后可重新获得，不依赖手工删除陈旧锁文件。
- 共享 S1/S2 连接验证必须识别已验证的 v1 与 v2，迁移后仍可打开旧项目/样本；覆盖已知 v1 升级、事务中断、三个服务重复打开与未知文件字节/时间不变。

## 每文件可恢复提交

1. 先持久化 job/file 意图、稳定 Asset/Version/Link ID、预期源身份和内部 staging 标识。
2. 用只读句柄打开普通源；Windows 拒绝 reparse point，并通过共享模式阻止写入/删除竞态。比较准备阶段身份、大小和修改时间；复制期间持续持有源句柄。
3. 流式复制到专属 staging（有界堆缓冲、取消检查），计算 SHA-256；同步数据后保存 staged 状态、hash 与大小。源不写入、不重命名、不删除。
4. 不可覆盖地发布到 objects/hash。已存在时重算哈希和大小后才复用；损坏对象报错，不就地覆盖或猜测修复。
5. 确认对象有效且不可被本进程并发改写后，在同一 SQLite 事务登记 Blob/AssetVersion/AssetLink 和 file committed。版本/关联 ID 来自持久任务日志，重复执行不会生成重复版本。
6. job 仅当全部文件已提交才 completed。取消/部分失败必须明确显示已完成数量与失败明细，不能将整个批次伪装成成功。提交以文件为原子单位，不在大文件复制期间保持长数据库事务。

文件状态：queued → copying → staged → blob_published → committed；失败/取消保存 phase 与错误。任务状态：prepared → running → completed/failed/cancelled/interrupted/integrity_failed。取消标志和登记在同一序列化数据库边界判定：登记前已取消则不登记，登记先完成则保留该文件的 committed ID/计数，后续文件取消。全部文件已提交优先 completed；重试不重新导入已提交文件。覆盖取消紧邻登记前后与并发 start。

路径验证不能只检查后按字符串重开。Windows 对受控仓库目录链持有拒绝删除/重命名的目录句柄，拒绝 reparse；发布时仍受这些句柄保护。新对象发布后或现有对象复用前，用拒绝写/删的句柄重算 hash，并持有到 SQL 登记完成。暂存身份在复制、发布间保持稳定；目录/对象替换竞态必须失败或使用已验证的相同字节，不能越界写入或登记错误对象。

## 恢复

进入导入服务时在仓库锁下 reconcile；不在应用启动时初始化用户库。持久日志区分 copying/staged/blob_published/committed。部分 staging 不计成功，重试重新复制；已发布且校验正确的对象可按原 ID 恢复登记。恢复前再次校验目标与名称冲突。可保留有日志的失败 staging/孤儿对象供审计，不自动删除未知对象。

已提交对象丢失或 hash 损坏时报告 integrity_failed 并阻止伪成功，不静默删除引用版本、不猜测源文件恢复；真正备份恢复属于 S7。应用自己的正常提交/取消/崩溃路径不能产生已确认却无 Blob 的版本。

## 必须验证

- 小文件、100文件、受控大文件；原字节、大小、修改时间、目录项不变，复制 hash 一致。
- 同字节不同名称/样本复用 Blob，样本 n 不变；请求重放不重复版本，文件同名冲突明确失败。
- 文件锁、重解析点、源在确认前变化、复制中写入尝试、路径越界、坏对象、磁盘满、取消、重试。
- 在 staged、发布后登记前等断点真正终止子进程，再新建服务恢复；断点后的 Blob/Version/Link 不变量和源哈希必须检查，不能只靠正常返回模拟崩溃。
- s3_ 原生/真实临时库/IPC 和 @S3 显式 Mock 前端测试执行数量大于零；最终 Windows CI EXE 与统一人工验收步骤一起交付。
