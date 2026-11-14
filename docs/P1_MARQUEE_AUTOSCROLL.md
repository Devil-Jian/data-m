# P1 Explorer 框选自动滚动与验收（Issue #13、#5）

## 本轮已经实现

- 指针进入工作区上/下边缘时，通过 requestAnimationFrame 驱动垂直自动滚动。
- 使用 scroll-content 坐标计算框选范围，滚动后范围能自然延伸到屏幕外的逻辑条目。
- 允许 `getVirtualItems(host)` 返回所有虚拟条目的逻辑矩形，即使目标行尚未挂载 DOM，也可通过 `contentMarqueeHits` 判定。
- 默认渲染模式从当前工作区内 DOM 元素测量矩形，并使用当前可见范围内合法 Asset ID 过滤。
- Escape、pointercancel、blur、组件卸载、视图禁用后取消自动滚动帧与拖放请求。
- 内部列表仅水平溢出时，使用外层工作区的垂直滚动条，避免在无法滚动的元素上尝试自动滚动。

## 本轮测试

```powershell
npm.cmd install
npm.cmd run test:interaction
npm.cmd run test:assets
npm.cmd run test:e2e
npm.cmd run build:desktop
npm.cmd run tauri:dev
```

`npm run test:e2e` 需要先执行 `npx playwright install chromium`，在 Chromium 上测试交互。GitHub Actions `ui-demos.yml` 新增浏览器 E2E job；Windows Rust/桌面构建沿用 `desktop-windows.yml`。

## Windows 11 实际验收清单（必须人工或 Windows GUI 自动化执行）

- [ ] Tauri 窗口稳定启动，窗口缩放和 DPI 125%/150% 下选择范围与鼠标位置一致。
- [ ] Raw、Processed 两个资产拥有不同 ID，复制文件不会新增生物学重复。
- [ ] 鼠标从工作区空白处左键框选，跨多个资产释放时选择符合几何范围。
- [ ] 逆向框选（右下到左上）、Ctrl 追加框选正常。
- [ ] 指针按住靠近可滚动区域上下边缘，页面滚动、选区扩展；松开后立即停止。
- [ ] Ctrl/Shift 多选并跨视图切换，资产选中项按当前作用域正确保留。
- [ ] 整组拖拽到合法分组显示正确文件数量；拖向原分组不发生操作。
- [ ] 复制/移动快捷键不抢占搜索框输入。
- [ ] 在拖拽过程中按 Esc 后松开，不会显示已提交的移动/复制请求。
- [ ] 外部 Windows Explorer 拖入仅显示检测到路径数量，源文件不改变。

## 限制

- 现在的文件资产仍为确定性 **演示 ID**，不是 Rust/SQLite 中持久化的 AssetId/AssetVersionId。
- 虚拟列表的逻辑矩形接口已具备，但还没有接入真正的 TanStack Virtual 渲染器；10k 纯函数测试仅验证算法，不证明 10k UI 性能。
- 嵌套水平滚动与垂直滚动同时使用时，只实现基础自动滚动能力，复杂双轴滚动尚须后续完善。
- Chromium E2E 不是 Windows WebView2 端到端测试；CI 通过不代表 Windows 实机验收完成。
- 全部拖放仍仅发送演示意图，未涉及真实文件操作。
