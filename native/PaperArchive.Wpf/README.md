# PaperArchive WPF desktop (PRD v1.3)

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
