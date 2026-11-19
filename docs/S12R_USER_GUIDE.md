# Data-M S12R 正式 Workbench 使用说明（未签收测试版）

## 使用前安全说明

当前仅建议在 Windows 11 与**可丢弃的测试科研数据**上运行。未签收的程序不应存放唯一的原始科研数据。SQLite / CAS 中原始文件按固定版本存储；前端只读所示数据来自真实 Tauri 后端，不自动注入 Demo 样本、虚构荧光图或统计数值。原生安全检查、同 SHA 机器验收和人工确认是独立条件。

## 1. 创建项目与实验结构

顶部“新建项目”创建 Project；左侧新建 Theme 与 Assay。选择 Assay 后，通过顶部 Group Tab 新建 Group，再创建生物学重复 Replicate。可在侧栏/当前分组/样本行直接执行重命名和删除，原生后端负责是否允许删除含依赖的实体。未成功返回且重新读取前，不视为成功提交。

推荐结构：Project（氧化应激）→ Theme（氧化应激机制）→ Assay（ROS / WB / 活力）→ Group（Control / H2O2）→ Replicate（ROS_CTRL01）。

## 2. 两种工作台与四个阶段

**全阶段全景**默认按一个 Replicate 一行显示 Raw/Processed/Statistics/Display。Raw/Processed 的真实多文件直接列在样本单元格；Statistics/Display 属于整个 Assay 的文件，未获 S8/S9 固定引用证据时明确标记“尚无可核验的本样本引用”。

**分阶段**可切 Raw、Processed、Statistics、Display；支持文件搜索、网格、虚拟列表与表格样式。大文件库使用后端每页至多 100 条分页，界面显示“已读取 x / 总数”，手动继续加载，不再把先前 1,000 条矩阵预览误认为资产全集。

注意：实验生物学总 n 与选定 S8 分析纳入 n 相互独立。只有查询到已冻结分析的纳入记录，或 Panel 固定版本的代表来源，才可标记相应样本。

## 3. 导入真实文件（S3）

选择已有实验，然后点击顶部“导入实验归档包”。选择阶段；Raw/Processed 必须明确选择所属 Group 和 Replicate，Statistics/Display 仅以 Assay 为目标。将**已安全解压**的测试文件或目录拖入 Windows 窗口，S3 先执行 prepare；检查文件清单、固定目标、名称冲突，再点击明确的“确认导入”。可查看进度、取消或重试未完成项。

当前**不支持 ZIP 一键解压、自动新建科研层级、自动识别论文包或仅凭文件名关联 Panel**。直接拖入 .zip 会在 S3 准备之前被界面拒绝。安全测试前必须备份原数据。

## 4. Explorer 文件操作（S5/S6）

在当前阶段选择文件，Ctrl/Shift 多选，使用 F2 重命名、Delete 逻辑删除、右键菜单、Ctrl+C/X/V 或拖放到 Group Tab。复制/移动必须再明确确认目标阶段及具体 Replicate（当目标为 Raw/Processed）；拖动不会自动写入。S5 以精确 AssetLink / AssetVersion 身份提交事务，完成后重新读取。

“回收区”支持 S6 受控逻辑恢复，不能覆盖原始 CAS Blob，也不能绕过原生引用保护。

## 5. 统计分析（S8）

点击“统计分析”打开原生 SQLite 分析抽屉。可新建分析、纳入或排除样本、选择固定输入及结果版本、保存草稿、冻结和创建修订。已冻结记录只读，当前筛选与纳入数量来源于实际选择的 Analysis，**不会根据文件名或 Demo 的 inStats 布尔值推断**。

## 6. 论文 Figure / Panel（S9/S10）

顶部“论文管理”显示真实 Paper/Figure/Panel 图卡；可新建、重命名、重排及删除满足原生依赖条件的空对象。Panel 详情显示固定 AssetVersionId、用途、SHA-256、候选版本、代表样本来源和真实 S10 溯源；可跳回来源 Assay，然后从显式导航横幅返回原 Panel。

Figure 导出通过原生 preview→确认→逐文件 SHA 校验/manifest，只有原生返回已验证 complete 才显示成功。浏览器 Mock 不会制造真实原生导出结果。

## 7. 数据安全（S7）

点击“完整性 · 备份 · 隔离恢复”打开工具层。先运行只读审计；受控确认后才创建本地一致性快照。每个快照须验证数据库及 Blob SHA-256；恢复只写向全新隔离目录，不覆盖运行仓库。**设备内备份不等于异地备份**。

## 8. 发布与限制

当前属于 S12R 未签收测试版，不代表 Windows 11 原生鼠标/触摸板、缩放、10,024 资产 GUI、源文件权限与高 DPI 已经过人工确认。发布必须以同一 commit SHA 的 Windows CI、NSIS/portable EXE、Manifest、独立 SHA-256 和人工验收记录为准；不得沿用旧 S11 或错误 PR #59 的成功状态。
