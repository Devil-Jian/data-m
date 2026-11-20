# S13 Windows 人工验收清单（机器通过后执行）

固定开发来源：`feat/s13-smart-folder-archive-import`，基线 SHA `635a971de70544fd298c3d3547b5bce872f56f59`。当前为测试版本，人工验收尚未完成。

## 准备

在 Windows 11 上使用可丢弃的科研试验数据。确认至少保留一套**独立磁盘**备份，不能拿唯一原始数据测试。先下载并验证对应 S13 SHA 的便携 EXE/NSIS 及 SHA256SUMS，确保不是先前 S12R EXE。

建议准备：

```text
Project A/
  Oxidative stress/
    ROS/
      Control/
        ROS01_raw.tif
        ROS01_process_crop.tif
        ROS01_process_adjust.tif
        ROS01_show.png
        ROS01_quant.csv
      H2O2/
        ROS02_raw.tif
        ROS02_process_crop.tif
    WB/
      Control/
        WB01_raw.tif
```

## 操作验收

- [ ] 正式 Workbench 顶部有“智能导入文件夹”，空项目也能点击。
- [ ] Windows 文件夹对话框和文件夹拖拽均能选择完整根目录。
- [ ] 导入前三种层级映射分别能正确生成或合并 Project/Theme/Assay/Group。
- [ ] `ROS01_raw`、`ROS01_process_crop` 自动归于同一 ROS01；Process 子标签识别 crop。
- [ ] `ROS01_show` 只作为实验级 Display；名字只作 ROS01 候选来源，不直接设置 Panel 代表来源。
- [ ] `ROS01_quant.csv` 必须人工确认；不确定文件不得静默导入。
- [ ] 预览可以改阶段、样本、分组、文件显示名、明确跳过，也能批量校对。
- [ ] 同一项目重复导入：已有同 SHA 同名文件跳过；同名不同 SHA 进入冲突，改显示名后正常处理。
- [ ] 点“检查冲突”前未创建项目或资产；确认前不发生文件复制。
- [ ] 导入后项目、主题、实验和样本可直接从正式 Workbench 浏览。
- [ ] 运行后手动暂停、关闭并重新打开向导，从“最近任务”继续；原生 S3 已入库文件不重复导入。
- [ ] 断电/结束任务的异常恢复后可继续；真正 CAS 版本身份与 S13 成功计数一致。
- [ ] 导入报告包含相对路径、样本阶段、处理标签及 SHA-256，**不包含原始 Windows 绝对路径**。
- [ ] 导入源目录及所有文件内容、修改时间保持不变。
- [ ] 超过 1,000 个文件时仍能分页预览、分批导入、继续；10,024 文件测试库内性能可接受。
- [ ] 文件夹包含软链接/Windows 重解析点、从仓库目录拖入、源文件中途改写时均被拒绝或标记重新确认。
- [ ] NSIS 安装、便携 EXE、Windows 11 缩放和输入法正常。
- [ ] 完成后 S7 完整性审计通过，并在原生 Figure/Panel 中能人工选择真实固定版本。

机器通过且用户完成以上项目后，才可由用户确认 `manual_passed`。无论是否通过，PR 保持 Draft 且不合并 `main`，除非用户另行授权。
