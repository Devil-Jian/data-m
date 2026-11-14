# Data-M — 前端组件体系对比演示

科研实验与论文管理系统（Windows 单机版）的 **三套 React UI 技术栈演示**。

> 此分支是 UI 比选实验，不替代 `main` 的原始 Tauri 2 / Vue 3 / Rust 架构规划。主分支保持不变。

## 三套独立演示

| 版本 | 入口 | 运行命令 | 本地地址 |
| --- | --- | --- | --- |
| React + shadcn 风格组件（Radix + Tailwind CSS） | `demos/shadcn` | `npm run dev:shadcn` | http://127.0.0.1:5173 |
| React + Mantine | `demos/mantine` | `npm run dev:mantine` | http://127.0.0.1:5174 |
| React + Fluent UI 2 | `demos/fluent` | `npm run dev:fluent` | http://127.0.0.1:5175 |

要求 Node.js 22+。在仓库根目录执行：

```powershell
git clone -b feat/ui-framework-comparison https://github.com/Devil-Jian/data-m.git
cd data-m
npm install
npm run dev:shadcn
# 另开两个终端可分别执行：
npm run dev:mantine
npm run dev:fluent
```

```powershell
npm run build:all
```

每套演示构建结果位于对应的 `demos/<name>/dist`。

## 保留 Gemini 原型的业务结构与工作流

- 顶部深色导航栏：科研项目切换、**实验整理 / 论文管理** 双模块切换、新建课题、导入/导出入口。
- 左侧项目树：**Project → Theme → Assay → Group → Replicate** 五级实验数据模型。
- 实验视图：**全阶段全景（样本 × 阶段）** 与 **分阶段** 两种模式；01_Raw / 02_Processed / 03_Statistics / 04_Display 四阶段。
- 列表、矩阵、详细表格切换；分组切换、阶段筛选、搜索、统计纳入与代表图标记、批量选择、演示 CSV 清单导出。
- 论文视图：**Paper → Figure → Panel**、子图缩略图、对应统计数据、子图详情侧边栏。
- 双向溯源：Panel → 对应 Assay 的四阶段归档；代表样本高亮；实验中的代表图标记 → Paper Panel；返回原 Panel。

共享数据在 `packages/core/src/data.ts`：Nrf2/HO-1 和肝缺血再灌注两项目，5 个实验单元，12 个分组与全部示例 Replicate；三套 UI 使用同一套演示数据和状态逻辑，以便对比视觉风格，而不引入业务行为差异。

## 工程分层

```
packages/core/src/
  data.ts             共享类型和演示数据
  workbench.tsx       Gemini 布局与业务交互（UI Kit 接口注入）
  workbench.css       Gemini 几何布局、矩阵与 Panel 详情样式
demos/
  shadcn/             本地拥有的 shadcn 风格 Radix/Tailwind 组件
  mantine/            原生 Mantine 8 组件适配
  fluent/             原生 Fluent UI React v9 (Fluent 2) 组件适配
.github/workflows/
  ui-demos.yml        三套工程的 TypeScript + Vite 构建检验
```

每套演示使用对应真实组件库（Button / Select / Input / Checkbox / Badge），不是仅替换 CSS 的三个静态截图。共享的是产品布局与业务模型，差异由 UI Kit 承担。

## 重要说明

**这是纯前端演示，不连接数据库、不持有任何真实实验影像。**

- 图片与图表是 **SVG/CSS 示意预览**；演示数值不能用于科研结论。
- 新建项目、样本角色切换只影响当前浏览器会话，刷新后恢复初始演示数据。
- `导入归档包`、`复制/移动`、`投稿 ZIP` 会明确提示需要后端实现，不会假装执行真实文件操作。
- “导出清单”提供真实可下载的 **演示 CSV manifest**，不等于原始文件归档。
- 正式数据管理仍需另外实现 FastAPI/SQLite/CAS、权限与不可变版本、真实关联查询。

本分支旨在选出最终 UI 架构；选择后可将胜出的 React 前端接入正式后端，或评估与主分支规划的 Tauri 2 壳集成。

详见 [UI 对比说明](docs/UI_COMPARISON.md)。

## 文件交互引擎（开发分支）

交互实现所在分支：`feat/file-interaction-engine`，以 `feat/ui-framework-comparison` 为基础，不修改 `main`。

- 选择逻辑：`packages/interaction/src/model.ts`，纯 TypeScript，独立于组件框架。
- React 事件适配：`packages/interaction/src/useExplorerInteraction.ts`。
- 现有 Gemini/shadcn 工作区：支持 Ctrl 单选/切换、Shift 连续选择、空白处矩形框选、内部拖动到顶部实验分组、Ctrl 拖拽复制提示、Esc 取消、Ctrl+A/C/X/V、Delete、F2。
- 拖动到其他分组时目前 **只显示真实操作请求，不改变任何样本或文件**。真实文件复制/移动、数据库事务、论文引用检查须在后端阶段实施。
- 当前识别对象是演示用 Replicate 行/卡片；正式的 `DataAsset` 文件实体要独立于生物学重复，不能将复制文件解释成复制样本。

运行与测试：
```powershell
git switch feat/file-interaction-engine
npm install
npm run test:interaction
npm run build:all
npm run dev:shadcn
```
设计与验收规范见 [交互引擎设计文档](docs/INTERACTION_ENGINE.md)。
