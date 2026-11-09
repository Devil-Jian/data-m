using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PaperArchive.Core;
using PaperArchive.Core.Workspace;

namespace PaperArchive.Wpf;

public sealed class AssetRow
{
    private readonly ExperimentWorkspace _workspace;
    private BitmapImage? _thumbnail;
    public ExperimentAsset Asset { get; }
    public string FileName => Asset.FileName;
    public string RelativePath => Asset.RelativePath;
    public string DirectoryLabel => Path.GetDirectoryName(Asset.RelativePath)?.Replace('\\', '/') ?? ".";
    public string GroupPrefix => Asset.GroupPrefix;
    public string StageLabel => Asset.Stage switch
    {
        "raw" => "RAW", "process" => "PROCESS", "show" => "SHOW",
        "statistics" => "统计表", _ => "其他"
    };
    public string UsageLabel { get; }
    public string SizeLabel => Asset.Size >= 1048576
        ? $"{Asset.Size / 1048576d:0.0} MB"
        : Asset.Size >= 1024 ? $"{Asset.Size / 1024d:0.0} KB" : $"{Asset.Size} B";
    public bool IsImage => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".webp", ".gif" }
        .Contains(Path.GetExtension(Asset.FileName), StringComparer.OrdinalIgnoreCase);

    public BitmapImage? Thumbnail
    {
        get
        {
            if (!IsImage) return null;
            if (_thumbnail is not null) return _thumbnail;
            try
            {
                using var stream = File.OpenRead(_workspace.GetBlobPath(Asset));
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 230;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                _thumbnail = bitmap;
            }
            catch { /* Unsupported or damaged images must remain visible as files. */ }
            return _thumbnail;
        }
    }

    public AssetRow(ExperimentWorkspace workspace, ExperimentAsset asset, string usage)
    {
        _workspace = workspace;
        Asset = asset;
        UsageLabel = usage;
    }
}

public partial class MainWindow : Window
{
    private ExperimentWorkspace? _workspace;
    private ArchiveRepository? _archive;
    private ExperimentProject? _activeProject;
    private ExperimentResult? _activeResult;
    private readonly ObservableCollection<AssetRow> _visible = [];
    private readonly List<AssetRow> _all = [];
    private readonly List<PaperEntry> _papers = [];
    private bool _libraryMode;
    private bool _thumbnailMode;
    private bool _detailsVisible = true;
    private bool _refreshingTree;
    private string _activeFolder = "全部文件";

    private static readonly string[] FolderNames =
        ["全部文件", "全部图片", "raw_data", "process_data", "show_data", "统计表", "数据组"];

    public MainWindow()
    {
        InitializeComponent();
        FileList.ItemsSource = _visible;
        ThumbnailList.ItemsSource = _visible;
        FolderList.ItemsSource = FolderNames;
        // Ensure ContextMenuOpening also works on the first right click.
        ProjectTree.ContextMenu=new ContextMenu();
        FolderList.ContextMenu=new ContextMenu();
        FileList.ContextMenu=new ContextMenu();
        ThumbnailList.ContextMenu=new ContextMenu();
        Loaded += (_, _) => InitializeWarehouse();
    }

    private static string DefaultWarehouse()
    {
#if DEBUG
        var name = "PaperArchiveDevData";
#else
        var name = "PaperArchiveData";
#endif
        return Directory.Exists(@"D:\")
            ? Path.Combine(@"D:\", name)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PaperArchive", name);
    }

    private void InitializeWarehouse()
    {
        try
        {
            var warehouse = DefaultWarehouse();
            _workspace = ExperimentWorkspace.Open(warehouse);
            if(File.Exists(Path.Combine(warehouse,"details-pane.pref")) &&
                File.ReadAllText(Path.Combine(warehouse,"details-pane.pref")).Trim()=="0")
                ToggleDetails();
            WarehouseLabel.Text = warehouse;
            LoadProjectTree();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ShowError(Exception error) =>
        System.Windows.MessageBox.Show(this, error.Message, "PaperArchive - 操作失败",
            MessageBoxButton.OK, MessageBoxImage.Error);

    private void LoadProjectTree()
    {
        if (_workspace is null) return;
        _refreshingTree=true;
        try
        {
            ProjectTree.Items.Clear();
            var papers=_workspace.ListPaperProjects();
            foreach(var paper in papers)
            {
                var projects=_workspace.ListProjectsForPaper(paper.Id);
                var paperCount=projects.Sum(p=>_workspace.ListResults(p.Id).Sum(r=>r.FileCount));
                var paperNode=new TreeViewItem{
                    Header=TreeLabel(paper.Name,"\uE8A5",paperCount),
                    Tag=paper,IsExpanded=true};
                foreach(var project in projects)
                {
                    var units=_workspace.ListResults(project.Id);
                    var projectCount=units.Sum(x=>x.FileCount);
                    var projectNode=new TreeViewItem{
                        Header=TreeLabel(project.Name,"\uE8B7",projectCount),
                        Tag=project,IsExpanded=true};
                    foreach(var result in units)
                        projectNode.Items.Add(new TreeViewItem{
                            Header=TreeLabel(result.Name,"\uE8B7",result.FileCount),
                            Tag=result
                        });
                    paperNode.Items.Add(projectNode);
                }
                ProjectTree.Items.Add(paperNode);
            }
            foreach(TreeViewItem paperNode in ProjectTree.Items)
            {
                if(_activePaper?.Id==(paperNode.Tag as PaperProject)?.Id&&
                   _activeResult is null&&_activeProject is null){paperNode.IsSelected=true;return;}
                foreach(TreeViewItem projectNode in paperNode.Items)
                {
                    if(_activeProject?.Id==(projectNode.Tag as ExperimentProject)?.Id &&
                        _activeResult is null){projectNode.IsSelected=true;return;}
                    foreach(TreeViewItem resultNode in projectNode.Items)
                        if(_activeResult?.Id==(resultNode.Tag as ExperimentResult)?.Id)
                        {paperNode.IsExpanded=true;projectNode.IsExpanded=true;resultNode.IsSelected=true;return;}
                }
            }
            if(ProjectTree.Items.Count>0)
            {
                var first=(TreeViewItem)ProjectTree.Items[0];
                _activePaper=first.Tag as PaperProject;
                first.IsSelected=true;
            }
        }
        finally{_refreshingTree=false;}
    }

    private void ProjectTree_SelectedItemChanged(object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (_refreshingTree) return;
        if (_libraryMode)
        {
            if (ProjectTree.SelectedItem is TreeViewItem { Tag: PaperEntry paper })
                LoadPaper(paper);
            return;
        }

        if(ProjectTree.SelectedItem is not TreeViewItem node)return;
        if(node.Tag is PaperProject paperProject)
        {
            _activePaper=paperProject;_activeProject=null;_activeResult=null;
            _all.Clear();_visible.Clear();
        }
        else if(node.Tag is ExperimentProject project)
        {
            _activeProject=project;_activeResult=null;
            _activePaper=(node.Parent as TreeViewItem)?.Tag as PaperProject;
            _all.Clear();_visible.Clear();
        }
        else if(node.Tag is ExperimentResult result && _workspace is not null)
        {
            _activeResult=result;
            var parent=node.Parent as TreeViewItem;
            _activeProject=parent?.Tag as ExperimentProject;
            _activePaper=(parent?.Parent as TreeViewItem)?.Tag as PaperProject;
            LoadSelectedResult();
            RebuildFolderList();
        }
        RefreshItems();
    }

    private void RebuildFolderList()
    {
        // Left side chooses the folder/view scope. Data-group filtering is
        // intentionally left to the Data Group column in the central list.
        FolderList.ItemsSource = null;
        FolderList.ItemsSource = FolderNames;
        FolderList.SelectedIndex = 0;
    }

    private void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _activeFolder = FolderList.SelectedItem as string ?? "全部文件";
        RefreshItems();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => RefreshItems();

    private void RefreshItems()
    {
        if (_visible is null || FileList is null || SearchBox is null || CurrentLocation is null) return;
        if (_libraryMode)
        {
            UpdateStatus();
            return;
        }
        var search = SearchBox.Text.Trim();
        IEnumerable<AssetRow> query = _all.Where(x =>
            x.FileName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || x.RelativePath.Contains(search, StringComparison.OrdinalIgnoreCase)
            || x.GroupPrefix.Contains(search, StringComparison.OrdinalIgnoreCase)
            || x.Asset.Id.Contains(search, StringComparison.OrdinalIgnoreCase));
        query = _activeFolder switch
        {
            "全部图片" => query.Where(x => x.IsImage),
            "raw_data" => query.Where(x => x.Asset.Stage == "raw"),
            "process_data" => query.Where(x => x.Asset.Stage == "process"),
            "show_data" => query.Where(x => x.Asset.Stage == "show"),
            "统计表" => query.Where(x => x.Asset.Stage == "statistics"),
            "数据组" => query.OrderBy(x => x.GroupPrefix, StringComparer.OrdinalIgnoreCase),
            _ when _activeFolder.StartsWith("数据组: ", StringComparison.Ordinal) =>
                query.Where(x => x.GroupPrefix.Equals(
                    _activeFolder["数据组: ".Length..], StringComparison.OrdinalIgnoreCase)),
            _ => query
        };
        _visible.Clear();
        foreach (var row in ApplyTableSort(query.Where(AcceptColumnFilters))) _visible.Add(row);
        CurrentLocation.Text = _activeResult is null ? "请选择实验结果单元"
            : $"{_activePaper?.Name}  >  {_activeProject?.Name}  >  {_activeResult.Name}  >  {_activeFolder}";
        UpdateStatus();
        UpdateDetails();
    }

    private void UpdateStatus()
    {
        if (StatusText is null) return;
        if (_libraryMode)
        {
            StatusText.Text = $"论文资源库  |  显示 {LibraryFiles?.Items.Count ?? 0} / 共 {LibraryFiles?.Items.Count ?? 0} 个文件";
            return;
        }
        var path = _activeResult is null
            ? _activePaper?.Name ?? "论文项目"
            : $"{_activeProject?.Name}  >  {_activeResult.Name}  >  {_activeFolder}";
        var selected = _thumbnailMode ? ThumbnailList.SelectedItems.Count : FileList.SelectedItems.Count;
        StatusText.Text = $"{path}  |  显示 {_visible.Count} / 共 {_all.Count} 个文件  |  已选 {selected} 项";
    }

    private List<AssetRow> SelectedAssets() =>
        (_thumbnailMode ? ThumbnailList.SelectedItems : FileList.SelectedItems)
        .OfType<AssetRow>().ToList();

    private AssetRow? SelectedAsset() => SelectedAssets().FirstOrDefault();

    private void AssetSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdateStatus();
        UpdateDetails();
    }

    private void UpdateDetails()
    {
        if (DetailName is null || DetailMeta is null || PreviewImage is null
            || RelatedFiles is null || UsageList is null) return;
        var row = SelectedAsset();
        PreviewImage.Source = row?.Thumbnail;
        DetailName.Text = row?.FileName ?? "未选择文件";
        if (row is null || _workspace is null)
        {
            DetailMeta.Text = "";
            RelatedFiles.ItemsSource = null;
            UsageList.ItemsSource = null;
            return;
        }
        DetailMeta.Text = $"阶段：{row.StageLabel}\n数据组：{row.GroupPrefix}\n文件路径：{row.RelativePath}\n" +
                          $"大小：{row.SizeLabel}\nAsset ID：{row.Asset.Id}\nSHA-256：{row.Asset.Sha256}";
        RelatedFiles.ItemsSource = _all
            .Where(x => x.Asset.GroupId == row.Asset.GroupId)
            .Select(x => $"{x.StageLabel}  {x.FileName}").ToArray();
        UsageList.ItemsSource = _workspace.ListUsages(row.Asset.Id)
            .Select(x => $"{x.PaperTitle}\nFigure {x.Figure}{x.Panel} · {x.State}")
            .ToArray();
    }

    private void MainTab_Changed(object sender, RoutedEventArgs e)
    {
        if (ProjectTree is null || LibraryArea is null) return;
        _libraryMode = LibraryTab.IsChecked == true;
        var editor = _libraryMode ? Visibility.Collapsed : Visibility.Visible;
        CreatePaperButton.Visibility = editor;
        CreateProjectButton.Visibility = editor;
        CreateResultButton.Visibility = editor;
        ImportProjectButton.Visibility = editor;
        MoveButton.Visibility = editor;
        ImportButton.Visibility = editor;
        CopyButton.Visibility = editor;
        ExportButton.Visibility = editor;
        FigureButton.Visibility = editor;
        ConfirmUsageButton.Visibility = editor;
        SearchBox.Visibility = editor;
        SidebarHeading.Text = _libraryMode ? "论文" : "论文项目";
        LibraryArea.Visibility = _libraryMode ? Visibility.Visible : Visibility.Collapsed;
        FolderList.Visibility = editor;
        FileList.Visibility = _libraryMode || _thumbnailMode ? Visibility.Collapsed : Visibility.Visible;
        ThumbnailList.Visibility = _libraryMode || !_thumbnailMode ? Visibility.Collapsed : Visibility.Visible;
        if (_libraryMode) LoadPaperTree();
        else LoadProjectTree();
        UpdateStatus();
    }

    private void LoadPaperTree()
    {
        if (_workspace is null) return;
        _archive ??= ArchiveRepository.Open(_workspace.Root);
        _papers.Clear();
        _papers.AddRange(_archive.ListPapers());
        ProjectTree.Items.Clear();
        foreach (var paper in _papers)
            ProjectTree.Items.Add(new TreeViewItem { Header = paper.Title, Tag = paper });
        if (ProjectTree.Items.Count > 0)
            ((TreeViewItem)ProjectTree.Items[0]).IsSelected = true;
        else
        {
            LibraryFiles.ItemsSource = null;
            CurrentLocation.Text = "论文资源库  >  暂无归档论文";
            UpdateStatus();
        }
    }

    private void LoadPaper(PaperEntry paper)
    {
        if (_archive is null) return;
        LibraryFiles.ItemsSource = _archive.ListResources(paper.Id);
        CurrentLocation.Text = $"论文资源库  >  {paper.Title}";
        PreviewImage.Source = null;
        DetailName.Text = paper.Title;
        DetailMeta.Text = $"状态：{paper.Status}\nFigure 数量：{paper.FigureCount}\n文件数量：{paper.FileCount}";
        RelatedFiles.ItemsSource = null;
        UsageList.ItemsSource = null;
        UpdateStatus();
    }

    private void LibraryFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LibraryFiles.SelectedItem is not ResourceEntry resource || _archive is null) return;
        DetailName.Text = resource.FileName;
        DetailMeta.Text = $"角色：{resource.Role}\n位置：{resource.Group}\nSHA-256：{resource.Sha256}";
        PreviewImage.Source = PreviewFromFile(() => _archive.ResolveOriginal(resource.Sha256));
    }

    private static BitmapImage? PreviewFromFile(Func<string> getPath)
    {
        try
        {
            using var stream = File.OpenRead(getPath());
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 450;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }

    private static string? SelectFolder(string description)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = description, Multiselect = false };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private string? Prompt(string title, string description, string initial = "")
    {
        var dialog = new Window
        {
            Title = title, Width = 470, Height = 175, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this, ResizeMode = ResizeMode.NoResize
        };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = description, Margin = new Thickness(0, 0, 0, 10) });
        var input = new TextBox { Text = initial };
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var okay = new Button { Content = "确定", IsDefault = true };
        okay.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        var cancel = new Button { Content = "取消", IsCancel = true };
        buttons.Children.Add(okay);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        input.Focus();
        input.SelectAll();
        return dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(input.Text) ? input.Text.Trim() : null;
    }

    private void CreateProject_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace is null) return;
        if(_activePaper is null){System.Windows.MessageBox.Show(this,"请先创建或选择论文项目。","新建实验项目");return;}
        var name = Prompt("新建实验项目", "输入实验项目名称");
        if (name is null) return;
        try
        {
            var id = _workspace.CreateProjectForPaper(_activePaper.Id,name);
            _activeProject = _workspace.ListProjects().First(p => p.Id == id);
            _activeResult = null;
            LoadProjectTree();
            // The tree is hierarchical: Paper -> Experiment -> Result.
            foreach(TreeViewItem paperNode in ProjectTree.Items)
                foreach(TreeViewItem projectNode in paperNode.Items)
                    if(projectNode.Tag is ExperimentProject p && p.Id==id)
                    {projectNode.IsSelected=true;return;}
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace is null) return;
        if (_activeProject is null)
        {
            System.Windows.MessageBox.Show(this, "请先新建或选择一个实验项目。", "导入实验结果");
            return;
        }
        var folder = SelectFolder("选择包含 raw_data / process_data / show_data 的实验结果根目录");
        if (folder is null) return;
        var projectId = _activeProject.Id;
        try
        {
            ImportButton.IsEnabled = false;
            StatusText.Text = "正在复制并校验实验结果文件…";
            var imported = await Task.Run(() => _workspace.ImportResult(projectId, folder));
            _activeResult = _workspace.ListResults(projectId).First(r => r.Id == imported.ResultId);
            LoadProjectTree();
            LoadSelectedResult();
            RebuildFolderList();
            System.Windows.MessageBox.Show(this,
                $"已导入 {imported.FileCount} 个文件，识别 {imported.DataGroupCount} 个数据组。\n原始目录中的文件未被修改。",
                "导入完成");
        }
        catch (Exception ex) { ShowError(ex); }
        finally { ImportButton.IsEnabled = true; UpdateStatus(); }
    }

    private void Details_Click(object sender, RoutedEventArgs e) => ToggleDetails();

    private void ToggleDetails()
    {
        _detailsVisible = !_detailsVisible;
        DetailsColumn.Width = new GridLength(_detailsVisible ? 280 : 0);
        DetailsColumn.MinWidth = _detailsVisible ? 200 : 0;
        DetailsPane.Visibility = _detailsVisible ? Visibility.Visible : Visibility.Collapsed;
        DetailSplitter.Visibility = _detailsVisible ? Visibility.Visible : Visibility.Collapsed;
        DetailsChevron.Content=_detailsVisible?"‹":"›";
        try{File.WriteAllText(Path.Combine(_workspace?.Root??DefaultWarehouse(),"details-pane.pref"),
            _detailsVisible?"1":"0");}catch{}
    }

    private void DetailsView_Click(object sender, RoutedEventArgs e)
    {
        _thumbnailMode = false;
        FileList.Visibility = _libraryMode ? Visibility.Collapsed : Visibility.Visible;
        ThumbnailList.Visibility = Visibility.Collapsed;
        UpdateStatus();
        UpdateDetails();
    }

    private void ThumbnailsView_Click(object sender, RoutedEventArgs e)
    {
        _thumbnailMode = true;
        FileList.Visibility = Visibility.Collapsed;
        ThumbnailList.Visibility = _libraryMode ? Visibility.Collapsed : Visibility.Visible;
        UpdateStatus();
        UpdateDetails();
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopySelection();

    private void CopySelection()
    {
        var selected = SelectedAssets();
        if (selected.Count == 0 || _workspace is null) return;
        try
        {
            var cache = Path.Combine(Path.GetTempPath(), "PaperArchive", "Clipboard");
            Directory.CreateDirectory(cache);
            var paths = new List<string>();
            foreach (var row in selected)
            {
                var assetCache = Path.Combine(cache, row.Asset.Id);
                Directory.CreateDirectory(assetCache);
                var destination = Path.Combine(assetCache, row.FileName);
                File.Copy(_workspace.GetBlobPath(row.Asset), destination, overwrite: true);
                paths.Add(destination);
            }
            var data = new System.Windows.DataObject();
            data.SetData(System.Windows.DataFormats.FileDrop, paths.ToArray());
            if (selected.Count == 1 && selected[0].IsImage)
            {
                using var stream = File.OpenRead(_workspace.GetBlobPath(selected[0].Asset));
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                data.SetData(System.Windows.DataFormats.Bitmap, bitmap);
            }
            System.Windows.Clipboard.SetDataObject(data, true);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ExportOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace is null) return;
        var rows = SelectedAssets();
        if (rows.Count == 0) return;
        var choice=System.Windows.MessageBox.Show(this,
            "将原图导出到已建立论文的 Figure/Panel 工作目录？\n是：选择论文及图表位置；否：指定任意文件夹；取消：不导出。",
            "导出原图",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
        if(choice==MessageBoxResult.Cancel)return;
        var folder=choice==MessageBoxResult.Yes?
            PickPaperFigureExportFolder():SelectFolder("选择无损导出原图的目标文件夹");
        if(folder is null)return;
        try
        {
            foreach (var row in rows) _workspace.ExportOriginal(row.Asset, folder);
            System.Windows.MessageBox.Show(this, $"已无损导出 {rows.Count} 个文件。", "导出完成");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void Figure_Click(object sender, RoutedEventArgs e)
    {
        ShowFigureDialog();
    }

    private void Figure_Old_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace is null) return;
        var rows = SelectedAssets();
        if (rows.Count == 0) return;
        if (rows.Any(r => r.Asset.Stage != "show"))
        {
            System.Windows.MessageBox.Show(this, "仅能将 show_data 文件正式导出用于组图。", "请选择展示图片");
            return;
        }
        var paper = Prompt("导出用于组图", "目标论文名称");
        if (paper is null) return;
        var figureValue = Prompt("导出用于组图", "Figure 编号（正整数）", "1");
        if (!int.TryParse(figureValue, out var figure) || figure < 1) return;
        var panelValue = Prompt("导出用于组图", "Panel 编号（A–Z）", "A");
        if (panelValue is null || panelValue.Length != 1 || !char.IsAsciiLetter(panelValue[0])) return;
        var folder = SelectFolder("选择论文组图输出目录");
        if (folder is null) return;
        try
        {
            foreach (var row in rows)
                _workspace.ExportForFigure(row.Asset, paper, figure, panelValue, folder);
            LoadSelectedResult();
            System.Windows.MessageBox.Show(this,
                $"已导出 {rows.Count} 个原始文件，并记录用于 Figure {figure}{panelValue.ToUpperInvariant()}。\n请在最终组图后确认使用状态。",
                "导出完成");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ConfirmUsage_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace is null) return;
        var rows = SelectedAssets();
        if (rows.Count == 0) return;
        try
        {
            var count = 0;
            foreach (var row in rows)
            {
                var usage = _workspace.ListUsages(row.Asset.Id)
                    .FirstOrDefault(x => x.State == "exported");
                if (usage is null) continue;
                _workspace.SetUsageState(usage.Id, "confirmed");
                count++;
            }
            LoadSelectedResult();
            System.Windows.MessageBox.Show(this,
                count == 0 ? "选中图片没有待确认的组图导出记录。" :
                    $"已确认 {count} 条论文使用记录。最终归档前仍需核对 Figure/Panel 位置。",
                "论文使用确认");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void LoadSelectedResult()
    {
        if (_activeResult is null || _workspace is null) return;
        _all.Clear();
        foreach(var a in _workspace.ListAssets(_activeResult.Id))
        {
            var uses=_workspace.ListUsages(a.Id).Where(x=>x.State is "exported" or "confirmed")
                .Select(x=>$"Fig {x.Figure}{x.Panel}").Distinct().ToArray();
            _all.Add(new AssetRow(_workspace,a,uses.Length==0?"—":string.Join(", ",uses)));
        }
        RefreshItems();
    }

    private void Files_DoubleClick(object sender,MouseButtonEventArgs e)
    {
        // GridView headers and empty area bubble MouseDoubleClick to ListView.
        // Only open the *actual clicked row*, not a stale SelectedItem.
        if(e.OriginalSource is not DependencyObject origin||sender is not ItemsControl control)return;
        var container=ItemsControl.ContainerFromElement(control,origin) as ListBoxItem;
        if(container?.Content is not AssetRow row)return;
        e.Handled=true;
        OpenSelectedAsset(row);
    }

    private void OpenSelectedAsset(AssetRow? chosen=null)
    {
        if(_workspace is null|| (chosen??SelectedAsset()) is not { } row)return;
        try
        {
            var cache=Path.Combine(Path.GetTempPath(),"PaperArchive","Open",row.Asset.Id);
            Directory.CreateDirectory(cache);
            var target=Path.Combine(cache,row.FileName);
            File.Copy(_workspace.GetBlobPath(row.Asset),target,overwrite:true);
            Process.Start(new ProcessStartInfo(target){UseShellExecute=true});
        }
        catch(Exception ex){ShowError(ex);}
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers == (ModifierKeys.Alt | ModifierKeys.Shift) && e.Key == Key.P)
        {
            ToggleDetails();
            e.Handled = true;
        }
        else if(!_libraryMode && Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.X &&
            !SearchBox.IsKeyboardFocusWithin){CutAssets_Click(sender,new RoutedEventArgs());e.Handled=true;}
        else if(!_libraryMode && Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.V &&
            !SearchBox.IsKeyboardFocusWithin){PasteAssets_Click(sender,new RoutedEventArgs());e.Handled=true;}
        else if(!_libraryMode && e.Key==Key.F2){RenameSelected_Click(sender,new RoutedEventArgs());e.Handled=true;}
        else if(!_libraryMode && e.Key==Key.Delete){DeleteAssets_Click(sender,new RoutedEventArgs());e.Handled=true;}
        else if(!_libraryMode && e.Key==Key.Enter && Keyboard.Modifiers==ModifierKeys.Alt)
            {Properties_Click(sender,new RoutedEventArgs());e.Handled=true;}
        else if (!_libraryMode && Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C
                 && !SearchBox.IsKeyboardFocusWithin)
        {
            CopySelection();
            e.Handled = true;
        }
    }
}
