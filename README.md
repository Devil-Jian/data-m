# Data-M — 统一正式科研数据工作区（S12 开发候选）

Data-M 是 Windows 11 单机、离线优先的科研实验文件、统计分析与论文溯源桌面客户端。技术栈：React / TypeScript / shadcn/ui + Tauri 2 / Rust + SQLite / Content-Addressed Storage (CAS)。

**状态：S12 开发中，非正式发布版本。** 上游为 `feat/s11-windows-performance-installer`，本开发分支为 `feat/s12-unified-workspace`，Draft PR [#59](https://github.com/Devil-Jian/data-m/pull/59)。不得将正在进行的开发与 Windows 11 人工验收视为已通过。重要或唯一真实科研数据不能用于开发验收。

## 启动

Windows 11 预览需要 Node.js 22、npm、Git。原生应用构建还需要 Tauri Windows Rust/MSVC、WebView2 工具链（参见 [S0_TOOLCHAIN](docs/S0_TOOLCHAIN.md)）。

```powershell
git clone --branch feat/s12-unified-workspace https://github.com/Devil-Jian/data-m.git
cd data-m
npm.cmd ci
npm.cmd run dev:desktop
```

浏览器地址 http://127.0.0.1:5176 。**浏览器渲染器没有原生 SQLite/CAS**；若未显式注入自动测试 Mock，项目读取应显示原生接口不可用，不能使用演示资产进行真正操作。

连接本机原生 IPC 的开发版：

```powershell
npm.cmd run tauri:dev
```

候选 Windows 生成（未签名，不代表正式发布）：

```powershell
npm.cmd run tauri:build
```

CI 定义在 `.github/workflows/desktop-windows.yml`；S11 已配置 NSIS 安装器、便携 EXE、SHA-256 清单流程。**只有成功 CI 中实际上传并独立核实的同 SHA 产物才可以声称已构建。** 旧的 “bundle.active=false / 无 NSIS” 说明已过期。

## 一套真实科研工作区

启动后直接进入**项目管理**，没有“项目管理 / 演示工作区”的模式切换。正式根组件使用真实 `project_tree` IPC；`packages/core` 的 `INITIAL_PROJECTS` 等演示数据仅服务于独立 Demo/测试入口，不作为正式项目来源。

真实结构：

```text
Project 项目 → Theme 研究主题 → Assay 实验
  ├ Group 分组 → Replicate 生物学重复
  │                 ├ Raw         样本级
  │                 └ Processed   样本级
  ├ Statistics                  实验级
  └ Display                     实验级
```

项目树选择真实 ID；可使用面包屑和后退/前进按钮导航。实验中选中具体样本后浏览 Raw/Processed，Statistics/Display 使用整个 Assay 作为目标，不能写入 Replicate 作用域。真实文件浏览器使用 `list_assets` 分页、名称搜索、排序和列表/网格/表格显示。选区以 `AssetLinkId` 为准，切换页面或目标要清空可写选区。

**安全导入流程**：先选择合法实验和 Raw/Processed 样本目标或实验级 Statistics/Display 目标，打开“安全导入”，准备记录、检查并显式确认后才实际执行。全局 Windows 拖入默认只读预览，**不自动入库**；`ROS01_raw.tif` 等文件名不会在本阶段自动推断或分配目标。正式操作可通过现有原生接口进行资产复制/移动、重命名、回收恢复、统计分析冻结与修订、Paper/Figure/Panel 固定版本引用、溯源和 Figure 导出、只读完整性检查与隔离恢复。具体交互迁移尚在 S12 进行中。

## 验证与证据

```powershell
npm.cmd run test:s0
npm.cmd run check
npm.cmd run test:desktop
npx.cmd playwright install chromium
npm.cmd run test:e2e:desktop -- --grep "@S12"
npm.cmd run test:e2e:desktop
npm.cmd run test:e2e
cd apps/desktop/src-tauri
cargo fmt --all -- --check
cargo test --locked --no-default-features -- --test-threads=1
cargo check --locked
```

浏览器端 Playwright 的显式 Mock 只验证 UI 交互，不证明 Windows 原生 WebView2、CAS/SQLite 持久化或真实 GUI 性能。10k 资产测试必须分别报告真实后端查询与真实 Windows GUI 测量。

[统一工作区操作手册](docs/S12_USER_WORKFLOW.md) 包含实验四阶段、真实文件选择/快捷键、导入、回收、任务状态和 Figure 无损导出流程。

详细的 [UI 迁移设计](docs/S12_UI_ARCHITECTURE.md)、[实施记录](docs/S12_MIGRATION_EXECUTION_REPORT.md) 和 [验收矩阵](docs/S12_MACHINE_ACCEPTANCE_MATRIX.md) 记录 S12 的实现范围与待办。源安全约束：数据原始字节不可变，资产复制/移动不生成 Replicate，Panel 固定版本不能由显示文件名替代，恢复必须写入全新隔离目录而非覆盖运行仓库。

无需也不得自行合并 `main`、将人工验收标为通过或发布官方 v1.0。
