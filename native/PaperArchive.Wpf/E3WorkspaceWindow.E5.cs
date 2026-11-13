using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PaperArchive.Core;
using PaperArchive.Core.Workspace;

namespace PaperArchive.Wpf;

public sealed partial class E3WorkspaceWindow
{
    private Grid? _e5Body;
    private Grid? _e5ArchivePane;
    private Grid? _e5SearchPane;
    private ColumnDefinition? _e5PaperColumn;
    private ColumnDefinition? _e5ExperimentColumn;
    private RowDefinition? _e5FigureHeadingRow;
    private RowDefinition? _e5CardsRow;
    private RowDefinition? _e5ExperimentsRow;
    private FrameworkElement? _e5FigureHeader;
    private FrameworkElement? _e5CardsScroll;
    private string _e5Mode="workspace";
    private CancellationTokenSource? _e5PreviewCancel;
    private CancellationTokenSource? _e5SearchCancel;
    private readonly DispatcherTimer _e5SearchTimer=new(){Interval=TimeSpan.FromMilliseconds(300)};
    private readonly TextBox _e5GlobalSearch=new(){MinWidth=220};
    private readonly ListBox _e5Results=new();
    private readonly ListBox _e5ArchivePapers=new();
    private readonly ComboBox _e5ArchiveRevisions=new();
    private readonly TreeView _e5ArchiveTree=new();
    private readonly Image _e5ArchivePreview=new(){Stretch=Stretch.Uniform};
    private readonly TextBlock _e5ArchiveInfo=new(){TextWrapping=TextWrapping.Wrap,
        Foreground=Brushes.DimGray,Margin=new Thickness(0,8,0,8)};
    private ArchiveRepository? _e5Archive;
    private PaperEntry? _e5SelectedArchivePaper;
    private WorkspaceArchiveRevision? _e5SelectedArchiveRevision;
    private E1FolderIdentity[]? _e5ExperimentCache;

    private sealed record E5SearchRow(
        string Kind,string Title,string Subtitle,string TargetId,
        string? ExperimentId=null,int? Revision=null,string? FileSha=null)
    {
        public string Display=>Title+"  ·  "+Subtitle;
    }

    private sealed record E5FrozenFile(string FileName,string Sha256,string Stage);

    private static void EnableE5Virtualization(ItemsControl control)
    {
        control.SetValue(VirtualizingPanel.IsVirtualizingProperty,true);
        control.SetValue(VirtualizingPanel.VirtualizationModeProperty,
            VirtualizationMode.Recycling);
        control.SetValue(ScrollViewer.CanContentScrollProperty,true);
    }

    private static bool E5IsPreviewable(string name)=>
        new[]{".png",".jpg",".jpeg",".bmp",".tif",".tiff",".gif",".webp"}
            .Contains(Path.GetExtension(name),StringComparer.OrdinalIgnoreCase);

    private async Task<BitmapImage?> LoadE5BitmapAsync(
        string sha,CancellationToken cancellationToken)
    {
        if(sha.Length!=64||!sha.All(Uri.IsHexDigit))return null;
        var path=Path.Combine(_repo.Root,"blobs","sha256",sha[..2],sha);
        // Offload byte read and thumbnail decode from the WPF dispatcher.
        return await Task.Run(()=>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream=File.OpenRead(path);
            var bitmap=new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption=BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth=720;
            bitmap.StreamSource=stream;
            bitmap.EndInit();
            bitmap.Freeze();
            cancellationToken.ThrowIfCancellationRequested();
            return bitmap;
        },cancellationToken);
    }

    private void InitializeE5Shell(Grid root,DockPanel originalHeader,
        Grid workspaceBody,Grid center,FrameworkElement figureHeader,
        FrameworkElement cardsScroll,FrameworkElement experimentPane)
    {
        _e5Body=workspaceBody;
        _e5PaperColumn=workspaceBody.ColumnDefinitions[0];
        _e5ExperimentColumn=workspaceBody.ColumnDefinitions[1];
        _e5FigureHeadingRow=center.RowDefinitions[0];
        _e5CardsRow=center.RowDefinitions[1];
        _e5ExperimentsRow=center.RowDefinitions[2];
        _e5FigureHeader=figureHeader;
        _e5CardsScroll=cardsScroll;

        var navigation=new DockPanel{Margin=new Thickness(18,0,18,10),
            LastChildFill=false};
        var modes=new WrapPanel{Orientation=Orientation.Horizontal};
        foreach(var (label,mode) in new[]{
            ("论文工作台","workspace"),("实验资料库","experiments"),
            ("论文档案库","archive")})
        {
            var button=Action(label,(_,_)=>SwitchE5Mode(mode));
            button.FontWeight=FontWeights.SemiBold;
            button.Padding=new Thickness(16,7,16,7);
            modes.Children.Add(button);
        }
        DockPanel.SetDock(modes,Dock.Left);
        navigation.Children.Add(modes);
        _e5GlobalSearch.MinWidth=220;
        _e5GlobalSearch.Margin=new Thickness(14,0,0,7);
        _e5GlobalSearch.VerticalContentAlignment=VerticalAlignment.Center;
        _e5GlobalSearch.ToolTip="全局搜索论文、实验、样本文件及历史归档";
        _e5GlobalSearch.TextChanged+=(_,_)=>_e5SearchTimer.StopAndStart();
        DockPanel.SetDock(_e5GlobalSearch,Dock.Right);
        navigation.Children.Add(_e5GlobalSearch);
        var navTitle=new TextBlock{Text="搜索",VerticalAlignment=VerticalAlignment.Center,
            Margin=new Thickness(12,0,0,7),Foreground=Brushes.DimGray};
        DockPanel.SetDock(navTitle,Dock.Right);
        navigation.Children.Add(navTitle);

        var header=new StackPanel();
        root.Children.Remove(originalHeader);
        header.Children.Add(originalHeader);
        header.Children.Add(navigation);
        Grid.SetRow(header,0);
        root.Children.Add(header);

        _e5ArchivePane=BuildE5ArchivePane();
        _e5ArchivePane.Visibility=Visibility.Collapsed;
        Grid.SetRow(_e5ArchivePane,1);
        root.Children.Add(_e5ArchivePane);

        _e5SearchPane=BuildE5SearchPane();
        _e5SearchPane.Visibility=Visibility.Collapsed;
        Grid.SetRow(_e5SearchPane,1);
        root.Children.Add(_e5SearchPane);

        _e5SearchTimer.Tick+=async (_,_)=>{
            _e5SearchTimer.Stop();
            await SearchE5Async();
        };
        _e5GlobalSearch.KeyDown+=(_,e)=>{
            if(e.Key==Key.Escape){_e5GlobalSearch.Clear();SwitchE5Mode("workspace");}
        };
        SwitchE5Mode("workspace");
    }

    private void SwitchE5Mode(string mode)
    {
        if(_e5Body is null||_e5ArchivePane is null||_e5SearchPane is null)return;
        _e5Mode=mode;
        _e5Body.Visibility=mode is "workspace" or "experiments"?
            Visibility.Visible:Visibility.Collapsed;
        _e5ArchivePane.Visibility=mode=="archive"?Visibility.Visible:Visibility.Collapsed;
        _e5SearchPane.Visibility=mode=="search"?Visibility.Visible:Visibility.Collapsed;
        var library=mode=="experiments";
        _e5PaperColumn!.Width=library?new GridLength(0):new GridLength(238);
        _e5ExperimentColumn!.Width=library?new GridLength(360):new GridLength(450);
        _e5FigureHeadingRow!.Height=library?new GridLength(0):GridLength.Auto;
        _e5CardsRow!.Height=library?new GridLength(0):
            new GridLength(1,GridUnitType.Star);
        _e5ExperimentsRow!.Height=library?
            new GridLength(1,GridUnitType.Star):new GridLength(285);
        _e5FigureHeader!.Visibility=library?Visibility.Collapsed:Visibility.Visible;
        _e5CardsScroll!.Visibility=library?Visibility.Collapsed:Visibility.Visible;
        if(library)
        {
            _panel=null;
            if(_experiment is null&&_experiments.Items.Count>0)
                _experiments.SelectedIndex=0;
            ShowDetails();
        }
        if(mode=="archive")ReloadE5ArchivePapers();
    }

    private Grid BuildE5SearchPane()
    {
        var grid=new Grid{Background=Brushes.White,Margin=new Thickness(12)};
        grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        grid.RowDefinitions.Add(new RowDefinition());
        var title=Heading("全局搜索 · 论文 / 实验 / 文件 / 历史版本");
        title.Margin=new Thickness(18);
        grid.Children.Add(title);
        _e5Results.DisplayMemberPath="Display";
        EnableE5Virtualization(_e5Results);
        _e5Results.MouseDoubleClick+=(_,_)=>ActivateE5SearchResult();
        _e5Results.KeyDown+=(_,e)=>{
            if(e.Key==Key.Enter)ActivateE5SearchResult();
        };
        _e5Results.Margin=new Thickness(18,0,18,18);
        Grid.SetRow(_e5Results,1);
        grid.Children.Add(_e5Results);
        return grid;
    }

    private async Task SearchE5Async()
    {
        var query=_e5GlobalSearch.Text.Trim();
        _e5SearchCancel?.Cancel();
        if(query.Length==0)
        {
            if(_e5Mode=="search")SwitchE5Mode("workspace");
            _e5Results.ItemsSource=null;
            return;
        }
        var cancellation=new CancellationTokenSource();
        _e5SearchCancel=cancellation;
        SwitchE5Mode("search");
        try
        {
            var rows=await Task.Run(()=>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var workspace=_repo.SearchE5Workspace(query,70)
                    .Select(x=>new E5SearchRow(x.Kind,x.Title,x.Context,
                        x.TargetId,x.ExperimentId));
                var archive=ArchiveRepository.Open(_repo.Root)
                    .SearchE5Archives(query,70)
                    .Select(x=>new E5SearchRow("archive",x.FileName,
                        x.PaperName+" / Revision "+x.Revision+" / "+x.Location,
                        x.PaperId,null,x.Revision,x.Sha256));
                cancellation.Token.ThrowIfCancellationRequested();
                return workspace.Concat(archive).Take(120).ToArray();
            },cancellation.Token);
            if(!cancellation.IsCancellationRequested && _e5GlobalSearch.Text.Trim()==query)
                _e5Results.ItemsSource=rows;
        }
        catch(OperationCanceledException) { }
        catch(Exception error)
        {
            if(!cancellation.IsCancellationRequested)
                MessageBox.Show(this,error.Message,"搜索失败");
        }
    }

    private void ActivateE5SearchResult()
    {
        if(_e5Results.SelectedItem is not E5SearchRow selected)return;
        _e5GlobalSearch.TextChanged-=E5SearchTextChangedNoOp;
        _e5SearchTimer.Stop();
        _e5GlobalSearch.Clear();
        if(selected.Kind=="archive")
        {
            SwitchE5Mode("archive");
            _e5ArchivePapers.SelectedItem=_e5ArchivePapers.Items
                .Cast<PaperEntry>().FirstOrDefault(x=>x.Id==selected.TargetId);
            _e5ArchiveRevisions.SelectedItem=_e5ArchiveRevisions.Items
                .Cast<WorkspaceArchiveRevision>()
                .FirstOrDefault(x=>x.Revision==selected.Revision);
            SelectE5FrozenFile(selected.FileSha);
            return;
        }
        if(selected.Kind=="paper")
        {
            SwitchE5Mode("workspace");
            _papers.SelectedItem=_papers.Items.Cast<PaperProject>()
                .FirstOrDefault(x=>x.Id==selected.TargetId);
            return;
        }
        SwitchE5Mode("experiments");
        if(selected.ExperimentId is not null)
        {
            _experiments.SelectedItem=_experiments.Items.Cast<E1FolderIdentity>()
                .FirstOrDefault(x=>x.ExperimentId==selected.ExperimentId);
            if(selected.Kind=="file")
                _files.SelectedItem=_files.Items.Cast<FileItem>()
                    .FirstOrDefault(x=>x.Name==selected.Title);
        }
    }

    // Keeping an explicit empty handler out of the observable navigation path.
    private void E5SearchTextChangedNoOp(object sender,TextChangedEventArgs e) { }

    private Grid BuildE5ArchivePane()
    {
        var grid=new Grid{Background=Brushes.White,Margin=new Thickness(12)};
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(240)});
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(390)});
        grid.ColumnDefinitions.Add(new ColumnDefinition{
            Width=new GridLength(1,GridUnitType.Star)});

        var left=new DockPanel{Margin=new Thickness(14)};
        var heading=Heading("归档论文");
        DockPanel.SetDock(heading,Dock.Top);left.Children.Add(heading);
        _e5ArchivePapers.DisplayMemberPath="Title";
        EnableE5Virtualization(_e5ArchivePapers);
        _e5ArchivePapers.SelectionChanged+=(_,_)=>SelectE5ArchivePaper();
        left.Children.Add(_e5ArchivePapers);
        Grid.SetColumn(left,0);grid.Children.Add(left);

        var center=new DockPanel{Margin=new Thickness(14)};
        var top=new StackPanel();
        top.Children.Add(Heading("归档 Revision"));
        _e5ArchiveRevisions.DisplayMemberPath="Revision";
        _e5ArchiveRevisions.SelectionChanged+=(_,_)=>LoadE5Revision();
        top.Children.Add(_e5ArchiveRevisions);
        DockPanel.SetDock(top,Dock.Top);center.Children.Add(top);
        _e5ArchiveTree.SelectedItemChanged+=async (_,_)=>await PreviewE5ArchiveSelection();
        center.Children.Add(_e5ArchiveTree);
        Grid.SetColumn(center,1);grid.Children.Add(center);

        var right=new Grid{Margin=new Thickness(18)};
        right.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        right.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        right.Children.Add(_e5ArchiveInfo);
        var imageContainer=new Border{Background=Brushes.White,
            BorderThickness=new Thickness(1),BorderBrush=Brushes.LightGray,
            Padding=new Thickness(8)};
        imageContainer.Child=_e5ArchivePreview;
        Grid.SetRow(imageContainer,1);right.Children.Add(imageContainer);
        Grid.SetColumn(right,2);grid.Children.Add(right);
        return grid;
    }

    private void ReloadE5ArchivePapers()
    {
        _e5Archive??=ArchiveRepository.Open(_repo.Root);
        var previous=_e5SelectedArchivePaper?.Id;
        var papers=_e5Archive.ListPapers();
        _e5ArchivePapers.ItemsSource=papers;
        _e5ArchivePapers.SelectedItem=papers.FirstOrDefault(x=>x.Id==previous)
            ??papers.FirstOrDefault();
    }

    private void SelectE5ArchivePaper()
    {
        _e5SelectedArchivePaper=_e5ArchivePapers.SelectedItem as PaperEntry;
        _e5ArchiveTree.Items.Clear();
        _e5ArchivePreview.Source=null;
        if(_e5Archive is null||_e5SelectedArchivePaper is null)
        {
            _e5ArchiveRevisions.ItemsSource=null;return;
        }
        var revisions=_e5Archive.ListWorkspaceRevisions(_e5SelectedArchivePaper.Id);
        _e5ArchiveRevisions.ItemsSource=revisions;
        _e5ArchiveRevisions.SelectedItem=revisions.FirstOrDefault();
        if(revisions.Count==0)
            _e5ArchiveInfo.Text="旧版目录归档没有冻结的 Revision 索引。";
    }

    private static IEnumerable<JsonElement> E5Children(JsonElement e,string key)=>
        e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(key,out var val)
        &&val.ValueKind==JsonValueKind.Array?val.EnumerateArray().ToArray():[];

    private static string E5Text(JsonElement e,string key)=>
        e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(key,out var value)
            ?value.ToString():"";

    private TreeViewItem E5Node(ItemsControl parent,string text,E5FrozenFile? file=null)
    {
        var node=new TreeViewItem{Header=text,Tag=file,IsExpanded=true};
        parent.Items.Add(node);return node;
    }

    private void LoadE5Revision()
    {
        _e5SelectedArchiveRevision=_e5ArchiveRevisions.SelectedItem
            as WorkspaceArchiveRevision;
        _e5ArchiveTree.Items.Clear();
        _e5ArchivePreview.Source=null;
        if(_e5Archive is null||_e5SelectedArchivePaper is null||
            _e5SelectedArchiveRevision is null)return;
        try
        {
            var manifest=_e5Archive.GetWorkspaceArchiveManifest(
                _e5SelectedArchivePaper.Id,_e5SelectedArchiveRevision.Revision);
            if(manifest is null)return;
            using var json=JsonDocument.Parse(manifest);
            var root=json.RootElement;
            var schema=root.TryGetProperty("schemaVersion",out var version)?
                version.GetInt32():1;
            _e5ArchiveInfo.Text=_e5SelectedArchivePaper.Title+
                " · Revision "+_e5SelectedArchiveRevision.Revision+
                " · 历史索引只读，不跟随工作区变化";
            foreach(var document in root.TryGetProperty("document",out var d)?
                new[]{d}:Array.Empty<JsonElement>())
                E5Node(_e5ArchiveTree,"论文全文 · "+E5Text(document,"FileName"),
                    new(E5Text(document,"FileName"),E5Text(document,"Sha256"),"manuscript"));
            if(schema==3)
            {
                foreach(var figure in E5Children(root,"figures"))
                {
                    var figureNode=E5Node(_e5ArchiveTree,
                        "Figure "+E5Text(figure,"FigureNumber")+
                        " · "+E5Text(figure,"FileName"),
                        new(E5Text(figure,"FileName"),E5Text(figure,"Sha256"),"composite"));
                    foreach(var panel in E5Children(figure,"sources"))
                    {
                        var panelNode=E5Node(figureNode,"Panel "+
                            E5Text(panel,"PanelLabel")+" · "+
                            E5Text(panel,"ExperimentName"));
                        var sampleAssetIds=new HashSet<string>(StringComparer.Ordinal);
                        foreach(var sample in E5Children(panel,"sampleUnits"))
                        {
                            var sampleNode=E5Node(panelNode,E5Text(sample,"SampleName"));
                            foreach(var file in E5Children(sample,"assets"))
                            {
                                sampleAssetIds.Add(E5Text(file,"AssetId"));
                                E5Node(sampleNode,E5Text(file,"Stage").ToUpperInvariant()+
                                    " · "+E5Text(file,"FileName"),
                                    new(E5Text(file,"FileName"),E5Text(file,"Sha256"),
                                        E5Text(file,"Stage")));
                            }
                        }
                        foreach(var file in E5Children(panel,"assets"))
                        {
                            if(sampleAssetIds.Contains(E5Text(file,"AssetId")))continue;
                            E5Node(panelNode,"其他 · "+E5Text(file,"FileName"),
                                new(E5Text(file,"FileName"),E5Text(file,"Sha256"),
                                    E5Text(file,"Stage")));
                        }
                    }
                }
            }
            else
            {
                // Legacy manifests are displayed only from their own frozen
                // resources, not interpreted through current E2 assignments.
                foreach(var item in E5Children(root,"resources"))
                    E5Node(_e5ArchiveTree,E5Text(item,"Location")+" / "+
                        E5Text(item,"FileName"),
                        new(E5Text(item,"FileName"),E5Text(item,"Sha256"),
                            E5Text(item,"Role")));
            }
        }
        catch(Exception ex)
        {
            _e5ArchiveTree.Items.Clear();
            _e5ArchiveInfo.Text="历史文件完整性检查失败："+ex.Message;
        }
    }

    private void SelectE5FrozenFile(string? sha)
    {
        if(string.IsNullOrEmpty(sha))return;
        bool Select(ItemsControl parent)
        {
            foreach(var obj in parent.Items)
            {
                if(obj is not TreeViewItem node)continue;
                if(node.Tag is E5FrozenFile file&&
                    file.Sha256.Equals(sha,StringComparison.OrdinalIgnoreCase))
                {
                    node.IsSelected=true;node.BringIntoView();return true;
                }
                if(Select(node))return true;
            }
            return false;
        }
        Select(_e5ArchiveTree);
    }

    private async Task PreviewE5ArchiveSelection()
    {
        _e5ArchivePreview.Source=null;
        _e5PreviewCancel?.Cancel();
        if(_e5ArchiveTree.SelectedItem is not TreeViewItem node||
            node.Tag is not E5FrozenFile file)return;
        _e5ArchiveInfo.Text=file.FileName+" · "+file.Stage+
            " · 来自冻结 Revision（只读）";
        if(!E5IsPreviewable(file.FileName))return;
        var cancellation=new CancellationTokenSource();
        _e5PreviewCancel=cancellation;
        try
        {
            var bitmap=await LoadE5BitmapAsync(file.Sha256,cancellation.Token);
            if(!cancellation.IsCancellationRequested)_e5ArchivePreview.Source=bitmap;
        }
        catch(OperationCanceledException) { }
        catch(IOException) { _e5ArchiveInfo.Text+=" · 原始副本缺失"; }
        catch(NotSupportedException) { _e5ArchiveInfo.Text+=" · 图片格式暂不支持预览"; }
        catch(System.Windows.Media.Imaging.FileFormatException) {
            _e5ArchiveInfo.Text+=" · 无法解码";
        }
    }
}

internal static class E5DispatcherTimerExtensions
{
    public static void StopAndStart(this DispatcherTimer timer)
    {
        timer.Stop();timer.Start();
    }
}
