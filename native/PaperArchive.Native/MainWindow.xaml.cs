using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using PaperArchive.Core;
using WinRT.Interop;
using Windows.Storage.Pickers;

namespace PaperArchive.Native;

public sealed class FileRow
{
    public string Name { get; }
    public string Directory { get; }
    public string FigureLabel { get; }
    public string RoleLabel { get; }
    public string SizeLabel { get; }
    public string? Source { get; }
    public string? DataId { get; }
    public string? Sha { get; }
    public ImportPlan? Plan { get; }
    public long Size { get; }

    public FileRow(ScannedFile file, ImportPlan plan)
    {
        Source = file.SourceRelativePath;
        Plan = plan;
        Name = Path.GetFileName(plan.LogicalRelativePath);
        Directory = Path.GetDirectoryName(plan.LogicalRelativePath.Replace('/', Path.DirectorySeparatorChar)) ?? "";
        FigureLabel = plan.Figure.HasValue ? $"Figure {plan.Figure}{plan.Panel}" : "—";
        RoleLabel = RoleName(plan.Role);
        Size = file.Size;
        SizeLabel = DisplaySize(file.Size);
    }

    public FileRow(ResourceEntry record)
    {
        DataId = record.DataId;
        Sha = record.Sha256;
        Name = record.FileName;
        Directory = record.Group;
        FigureLabel = record.Group;
        RoleLabel = RoleName(record.Role);
        Size = record.Size;
        SizeLabel = DisplaySize(record.Size);
    }

    private static string RoleName(string role) => role switch
    {
        "manuscript" => "论文",
        "composite" => "组合图",
        "display" => "子图",
        "stat_image" => "统计原始图",
        "statistics" => "统计表",
        "other" => "附件",
        _ => "待分类"
    };

    private static string DisplaySize(long bytes) => bytes >= 1_048_576
        ? $"{bytes / 1_048_576d:0.0} MB"
        : bytes >= 1024 ? $"{bytes / 1024d:0.0} KB" : $"{bytes} B";
}

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<FileRow> _visible = [];
    private PaperScan? _scan;
    private ArchiveRepository? _repository;
    private readonly Dictionary<string, ImportPlan> _plans = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PaperEntry> _papers = [];
    private readonly List<List<ImportPlan>> _undo = [];
    private bool _inLibrary;
    private string _activeFolder = "全部文件";

    private static readonly string[] FolderNames =
        ["全部文件", "Figure/组合图", "Figure/子图", "Figure/统计原始图", "Table", "Others", "待处理"];

    public MainWindow()
    {
        InitializeComponent();
        FileList.ItemsSource = _visible;
        FolderList.ItemsSource = FolderNames;
        FolderList.SelectedIndex = 0;
        TryOpenDefaultWarehouse();
        ShowProcessing();
    }

    private void TryOpenDefaultWarehouse()
    {
        if (!Directory.Exists(@"D:\")) { StatusText.Text = "请选择数据仓库（默认 D 盘）"; return; }
        try { _repository = ArchiveRepository.Open(@"D:\PaperArchiveData"); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async Task<string?> ChooseDirectoryAsync(string title)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private async Task<bool> EnsureWarehouseAsync()
    {
        if (_repository is not null) return true;
        var root = await ChooseDirectoryAsync("数据仓库");
        if (root is null) return false;
        try
        {
            _repository = await Task.Run(() => ArchiveRepository.Open(root));
            return true;
        }
        catch (Exception ex) { await ShowErrorAsync(ex.Message); return false; }
    }

    private async Task ShowErrorAsync(string text)
    {
        StatusText.Text = text;
        await new ContentDialog
        {
            Title = "操作失败",
            Content = text,
            CloseButtonText = "关闭",
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        }.ShowAsync();
    }

    private void ProcessTab_Click(object sender, RoutedEventArgs e) => ShowProcessing();
    private void LibraryTab_Click(object sender, RoutedEventArgs e) => ShowLibrary();

    private void ShowProcessing()
    {
        _inLibrary = false;
        SidebarTitle.Text = "目录";
        FolderList.ItemsSource = FolderNames;
        FolderList.SelectedIndex = 0;
        OpenButton.Visibility = Visibility.Visible;
        RoleButton.Visibility = Visibility.Visible;
        RenameButton.Visibility = Visibility.Visible;
        ImportButton.Visibility = Visibility.Visible;
        FileList.SelectionMode = ListViewSelectionMode.Multiple;
        RefreshRows();
    }

    private async void ShowLibrary()
    {
        _inLibrary = true;
        SidebarTitle.Text = "论文";
        OpenButton.Visibility = Visibility.Collapsed;
        RoleButton.Visibility = Visibility.Collapsed;
        RenameButton.Visibility = Visibility.Collapsed;
        ImportButton.Visibility = Visibility.Collapsed;
        FileList.SelectionMode = ListViewSelectionMode.Single;
        _papers.Clear();
        if (!await EnsureWarehouseAsync()) return;
        try { _papers.AddRange(await Task.Run(() => _repository!.ListPapers())); }
        catch (Exception ex) { await ShowErrorAsync(ex.Message); }
        FolderList.ItemsSource = _papers.Select(p => p.Title).ToArray();
        FolderList.SelectedIndex = _papers.Count > 0 ? 0 : -1;
        if (_papers.Count == 0) { _visible.Clear(); StatusText.Text = "暂无归档论文"; }
    }

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = await ChooseDirectoryAsync("打开论文文件夹");
        if (folder is null) return;
        try
        {
            StatusText.Text = "扫描中…";
            _scan = await Task.Run(() => PaperFolderScanner.Scan(folder));
            _plans.Clear();
            _undo.Clear();
            foreach (var file in _scan.Files)
            {
                _plans.Add(file.SourceRelativePath, new ImportPlan(
                    file.SourceRelativePath, file.LogicalRelativePath, file.Role,
                    file.Figure, file.Panel, file.Sequence));
            }
            _activeFolder = "全部文件";
            FolderList.SelectedIndex = 0;
            RefreshRows();
            StatusText.Text = $"{_scan.Files.Count} 个文件 · {_scan.WarningCount} 个待检查";
        }
        catch (Exception ex) { await ShowErrorAsync(ex.Message); }
    }

    private void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_inLibrary)
        {
            var index = FolderList.SelectedIndex;
            if (index < 0 || index >= _papers.Count || _repository is null) return;
            try
            {
                _visible.Clear();
                foreach (var resource in _repository.ListResources(_papers[index].Id))
                    _visible.Add(new FileRow(resource));
                StatusText.Text = $"{_visible.Count} 个图表资源";
            }
            catch (Exception ex) { StatusText.Text = ex.Message; }
            return;
        }
        if (FolderList.SelectedItem is string chosen) _activeFolder = chosen;
        RefreshRows();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshRows();

    private void RefreshRows()
    {
        if (_inLibrary || _scan is null) return;
        var search = SearchBox.Text?.Trim() ?? "";
        _visible.Clear();
        foreach (var file in _scan.Files)
        {
            var plan = _plans[file.SourceRelativePath];
            var matchesFolder = _activeFolder == "全部文件" ||
                (_activeFolder == "待处理" ? file.Warning is not null || plan.Role == "unspecified" :
                    plan.LogicalRelativePath.StartsWith(_activeFolder + "/", StringComparison.OrdinalIgnoreCase));
            if (!matchesFolder || !plan.LogicalRelativePath.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
            _visible.Add(new FileRow(file, plan));
        }
    }

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileList.SelectedItem is not FileRow row)
        {
            DetailName.Text = "";
            DetailMeta.Text = "";
            PreviewImage.Source = null;
            return;
        }
        DetailName.Text = row.Name;
        DetailMeta.Text = $"{row.Directory}\n{row.FigureLabel} · {row.RoleLabel}\n{row.SizeLabel}";
        PreviewImage.Source = null;
        var file = _inLibrary && row.Sha is not null ? _repository?.ResolveOriginal(row.Sha)
            : _scan is not null && row.Source is not null ?
                Path.Combine(_scan.Root, row.Source.Replace('/', Path.DirectorySeparatorChar)) : null;
        if (file is null || !System.IO.File.Exists(file)) return;
        try
        {
            var extension = Path.GetExtension(row.Name);
            if (row.Size <= 16_000_000
                && (extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)))
                PreviewImage.Source = new BitmapImage(new Uri(file));
        }
        catch { /* keep metadata visible for unsupported image codecs */ }
    }

    private List<FileRow> SelectedRows() => FileList.SelectedItems.OfType<FileRow>().ToList();

    private void SaveUndo()
    {
        _undo.Add(_plans.Values.ToList());
        if (_undo.Count > 30) _undo.RemoveAt(0);
    }

    private async void RoleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_inLibrary || SelectedRows().Count == 0) return;
        var picker = new ComboBox { ItemsSource = new[]
            { "展示子图", "统计原始图", "最终组合图", "统计数据", "其他附件" }, SelectedIndex = 0 };
        var dialog = new ContentDialog
        {
            Title = "批量分类", Content = picker, PrimaryButtonText = "应用",
            CloseButtonText = "取消", XamlRoot = ((FrameworkElement)Content).XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var role = picker.SelectedIndex switch
        {
            0 => "display", 1 => "stat_image", 2 => "composite",
            3 => "statistics", _ => "other"
        };
        SaveUndo();
        foreach (var row in SelectedRows())
            if (row.Source is not null)
                _plans[row.Source] = _plans[row.Source] with { Role = role };
        RefreshRows();
        StatusText.Text = "批量分类已应用";
    }

    private async void RenameButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedRows();
        if (_inLibrary || selected.Count == 0) return;
        var number = new TextBox { Text = "1", Header = "Figure" };
        var letter = new TextBox { Text = "A", Header = "Panel" };
        var start = new TextBox { Text = "1", Header = "起始序号" };
        var panelContent = new StackPanel { Spacing = 8 };
        panelContent.Children.Add(number);
        panelContent.Children.Add(letter);
        panelContent.Children.Add(start);
        var dialog = new ContentDialog
        {
            Title = "批量编号", Content = panelContent, PrimaryButtonText = "应用",
            CloseButtonText = "取消", XamlRoot = ((FrameworkElement)Content).XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (!int.TryParse(number.Text, out var figure) || figure < 1 ||
            !int.TryParse(start.Text, out var sequence) || sequence < 1 ||
            letter.Text.Length != 1 || !char.IsAsciiLetter(letter.Text[0]))
        {
            await ShowErrorAsync("Figure、Panel 或序号不合法。");
            return;
        }
        var panel = letter.Text.ToUpperInvariant();
        var replacements = new List<(string Source, ImportPlan Plan)>();
        for (var index = 0; index < selected.Count; index++)
        {
            var row = selected[index];
            if (row.Source is null) continue;
            var plan = _plans[row.Source];
            var baseName = Path.GetFileNameWithoutExtension(plan.LogicalRelativePath);
            var extension = Path.GetExtension(plan.LogicalRelativePath);
            var newFileName = $"{figure}-{panel}-{sequence + index}-{baseName}{extension}";
            var oldFolder = Path.GetDirectoryName(plan.LogicalRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var logical = string.IsNullOrEmpty(oldFolder) ? newFileName :
                oldFolder.Replace('\\', '/') + "/" + newFileName;
            replacements.Add((row.Source, plan with
            {
                LogicalRelativePath = logical, Figure = figure, Panel = panel, Sequence = sequence + index
            }));
        }
        var currentNames = _plans.Values.Select(p => p.LogicalRelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var replacement in replacements)
            currentNames.Remove(_plans[replacement.Source].LogicalRelativePath);
        if (replacements.Any(x => !ArchiveRepository.ValidLogicalPath(x.Plan.LogicalRelativePath) ||
            !currentNames.Add(x.Plan.LogicalRelativePath)))
        {
            await ShowErrorAsync("批量命名存在冲突，未应用更改。");
            return;
        }
        SaveUndo();
        foreach (var (source, plan) in replacements) _plans[source] = plan;
        RefreshRows();
        StatusText.Text = $"已为 {replacements.Count} 个文件设置编号";
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scan is null || _inLibrary) return;
        if (!await EnsureWarehouseAsync()) return;
        var dialog = new ContentDialog
        {
            Title = "完成归档",
            Content = $"归档 {_scan.Files.Count} 个文件到数据仓库？",
            PrimaryButtonText = "归档", CloseButtonText = "取消",
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            ImportButton.IsEnabled = false;
            StatusText.Text = "归档中…";
            var plans = _plans.Values.ToList();
            var result = await Task.Run(() => _repository!.Import(_scan, plans));
            StatusText.Text = $"归档完成 · {result.Files} 个文件";
            _scan = null; _visible.Clear();
            ShowLibrary();
        }
        catch (Exception ex) { await ShowErrorAsync(ex.Message); }
        finally { ImportButton.IsEnabled = true; }
    }

    private async void RevealButton_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is not FileRow row) return;
        try
        {
            var path = _inLibrary && row.Sha is not null ? _repository!.ResolveOriginal(row.Sha)
                : _scan is not null && row.Source is not null
                    ? Path.Combine(_scan.Root, row.Source.Replace('/', Path.DirectorySeparatorChar)) : null;
            if (path is null) return;
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex) { await ShowErrorAsync(ex.Message); }
    }
}
