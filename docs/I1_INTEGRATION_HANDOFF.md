# I1 S8 + S9 统一交接

S8 为唯一 Schema v3 基线；保留 statistical_analyses、analysis_samples、版本固定引用及冻结修订。S9 使用 papers、figures、panels、panel_asset_versions、representative_sources 现有结构；Paper 元数据在 audit_events。

IPC：statistical_analysis({request}) 与 paper_workspace({request}) 同时显式注册。正式工作台：项目管理内 S8 统计分析入口、论文管理内 S9 Paper/Figure/Panel 工作区。Panel 多用途固定版本，候选页面最多 500 条并显式告警；不能推断全部项目候选。

S10 关联模型基于分析固定输入/输出版本 ID 与 Panel 固定版本 ID，必须进一步实现受控双向查询与安全导出。I1 不宣称这些 S10 能力完成。

机器/人工验收状态以 I1 执行报告和 GitHub Actions 为准；原 S8、S9 分支不修改，main 不合并。
