# PaperArchive WPF desktop (PRD v1.4)

This is the new **Windows 10/11 native desktop** implementation. The previous
WinUI 3 app in `native/PaperArchive.Native` is preserved for comparison.
`PaperArchive.Core` retains its legacy v1-v3 archive database, SHA-256 content
store, scanner, and read-only paper library.

## Develop in VS Code

Install the .NET 10 SDK, VS Code and C# Dev Kit. On **Windows**:

```powershell
git switch feat/wpf-desktop
dotnet restore native/PaperArchive.Wpf/PaperArchive.Wpf.csproj
dotnet run --project native/PaperArchive.Wpf/PaperArchive.Wpf.csproj
```

Edit `MainWindow.xaml` for the Explorer-like UI and `MainWindow.xaml.cs`
for interactions. WPF runs as a normal window; **do not reinstall an installer
to preview each change**. A Visual Studio installation with .NET Desktop
Development is optional for the graphical XAML designer/Hot Reload.

## Data safety

- Debug build: `D:\PaperArchiveDevData` (or per-user LocalAppData when D: is absent).
- Release build: `D:\PaperArchiveData` (or per-user LocalAppData when D: is absent).
- Existing archive: `database/archive.db`, **unchanged**.
- New experiment workspace: `database/workspace.db`, separate SQLite schema.
- Imported files are copied to `blobs/sha256/xx/<hash>`, preserving the source.
- Same filename prefix within one result unit forms a **data group**. It does
  not prove one processed file was directly derived from another.
- Export Original copies bytes and verifies SHA-256, never silently overwrites.
- Figure export requires `show_data` and records an `exported` usage;
  actual manuscript use still requires confirmation.

## Implemented in the first WPF integration slice

- Two main tabs, Explorer-like toolbar/tree/details, detailed file list and
  image thumbnails, search/filter, status bar with current location and
  visible/total/selected counts.
- Toggle right-hand details pane (button or Alt+Shift+P).
- Create experiment project, import entire result folder, recognize
  `raw_data`, `process_data`, `show_data` and statistics files.
- Automatic prefix-based group association with raw/process/show and
  preservation of original relative paths and SHA-256.
- Selected file preview, multi-selection, Ctrl+C clipboard file objects +
  bitmap for supported single images, byte-preserving export.
- Export selected show_data to Figure/Panel with usage metadata, then confirm selected exported uses in the toolbar.
- Existing paper archive resource list remains browsable through the Core API.

## Known next steps (not yet implemented)

- Figure/PDF page navigation with embedded document rendering.
- Full paper archive creation from the new experiment workspace.
- Advanced grouping rules, drag/drop and safe bulk renaming with undo.
- Thumbnail virtualization for extremely large folders, medical/scientific
  proprietary format codecs, cross-machine export/import.
- Full WPF UI accessibility review and Windows runtime interaction tests.

## Tests and builds

```powershell
dotnet run --project native/PaperArchive.Core.SmokeTests/PaperArchive.Core.SmokeTests.csproj
dotnet run --project native/PaperArchive.Workspace.SmokeTests/PaperArchive.Workspace.SmokeTests.csproj
dotnet build native/PaperArchive.Wpf/PaperArchive.Wpf.csproj
dotnet publish native/PaperArchive.Wpf/PaperArchive.Wpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o publish/PaperArchive
```

GitHub Actions `wpf-desktop.yml` builds on Windows and uploads a portable
ZIP. It is not yet an installer; do not distribute as a stable release before
end-to-end acceptance testing.

## V1.3 paper-first workflow

1. Create a **paper project** in the pre-archive workspace.
2. Select it and create one or more **experiment projects**.
3. Import a complete unsorted project into **待整理数据**, or import a
   preorganized experiment result folder.
4. Use the project tree context menu to create empty result units, move/copy
   entire result units, rename, soft-delete, or restore. Right-click selected
   image files to move/copy between result units. Blob SHA-256 bytes are shared,
   while a logical copy creates a new Asset ID. Moving keeps the Asset ID.
5. Click a file-list column to sort; right-click a header to filter. Sidebar
   is only a browsing scope selector, not a second group filter.
6. Use the splitter-edge chevron or Alt+Shift+P to collapse the details pane.
7. Export **SHOW** assets to a selected paper / Figure / Panel; create new
   Figure and Panel templates directly in the dialog.
8. Plain "Export Original" may export to any folder or an existing paper
   Figure/Panel directory. Plain export does **not** mark final paper usage.
9. Double-click a real file row to open it; column headers and empty space
   must never open the previously selected file.

## Safe schema upgrade

The first launch initializes new v1.3 tables in `workspace.db` and adds
nullable paper ownership and logical deletion columns without changing
legacy `archive.db`. Existing experiment projects are placed under a
dedicated `历史工作区（待分配）` paper project.

## Limitations before the final V1.3 milestone

- Side-panel sorting/filter controls are functional, but virtualized gallery
  and rich Figure/PDF rendering remain in subsequent milestones.
- Full undo journal for multi-file moves is not yet available; operations
  are SQLite transactional and conflict-checked.
- Tree deletion is recoverable (logical tombstone), while deleted files are
  retained in the underlying immutable blob store.
- Context menu operations are defined for project/result nodes and files.
  Adding arbitrary nested logical subfolders is a future extension.

## V1.4 Windows Explorer interaction changes

- **Scope navigation**: clicking the paper project shows *all* files in all its experiments; clicking an experiment project shows all files in its result units; clicking a result unit narrows to only that unit. The `所属实验结果` column is visible for aggregate scopes. Status bar totals follow the chosen scope rather than the last opened result.
- **Drag/drop**: from selected file rows or thumbnails, drag to a result unit (move), experiment project (move to `待整理数据`), or paper project (choose an experiment project first). Hold **Ctrl** to logically copy instead. Blob SHA-256 data is never physically moved. Conflicting data group prefixes require explicit merge consent and the transaction rolls back on conflict.
- **Navigation pane**: light Explorer-like folder glyphs, selected-row highlighting, tree in the upper area with its own scrolling; `浏览位置` stays docked to the bottom of the sidebar and has a resizable divider.
- **Blank tree context menu**: only `新建论文项目`, `新建实验项目`, `新建结果单元`; invalid options are disabled. Node-specific right clicks still provide their usual editing operations.
- **Menus**: the top workflow line contains `新建 ▾`, `导入 ▾`, `导出 ▾`. The lower command line contains cut, copy, paste, rename, delete, move, sort, view and more. Search operates within the current selected scope. Menu and column-header sorting share one state.
- **Details pane**: the original subtle border chevron remains; no wide toolbar toggle is added. Alt+Shift+P is still supported.

The UI uses familiar WPF controls with a native light palette. It is intentionally
not a pixel-perfect replica of the Windows 11 Explorer shell.

### V1.4 regression checklist (Windows)

1. Click a paper project, then its experiment, then a result unit. The visible list and lower-left path/visible/total counts must match each scope.
2. Switch to `show_data` while a paper project is selected. Files from multiple result units should remain visible; `所属实验结果` disambiguates them.
3. Drag one or multiple selected files to a result unit. Default is **move**; Ctrl-drag is **copy**. The source files must remain unchanged; after the operation, re-open the result and verify the counts.
4. Drag to a parent experiment project. Files should go to its `待整理数据` unit. Drag to a paper project and select the destination experiment project from the dialog. Drop outside a valid target must have no effect.
5. Right-click blank tree space: only three creation options. Right-click an actual node: node-specific options.
6. Test top New/Import/Export menus and lower command line, then sort from both the command bar and column headers.
7. Resize the main window. The browsing section should stay at the sidebar bottom; toggle and resize the right details pane.
8. Double-click table headers and empty space: **no file must open**. Double-click a file row: the correct file must open.
9. Re-run the V1.3 move/copy/soft-delete/restore tests and verify the original SHA-256 manifest.

### Current release limitations

- A green GitHub Actions build proves compilation and basic Core tests, **not** manual WPF interaction correctness or a full production release.
- Multi-operation undo, per-file conflict-preview wizard and arbitrary nested virtual folders remain later milestones.
- PDF/DOCX inline reading, paper finalization and transfer packages remain later milestones.
