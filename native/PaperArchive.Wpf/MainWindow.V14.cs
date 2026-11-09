using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PaperArchive.Core.Workspace;

namespace PaperArchive.Wpf;

public partial class MainWindow
{
    private readonly string _dragSession=Guid.NewGuid().ToString("N");
    private sealed record AssetDrag(string Session,string[] AssetIds);
    private Point _dragStart;
    private AssetRow? _dragStartedOn;
    private bool _dragging;
    private TreeViewItem? _rightClickedTreeItem;
    private TreeViewItem? _hoverTreeItem;
    private DispatcherTimer? _expandTimer;

    private string CurrentScopePath()
    {
        string path=_activePaper?.Name??"论文项目";
        if(_activeProject is not null)path+=" > "+_activeProject.Name;
        if(_activeResult is not null)path+=" > "+_activeResult.Name;
        return path+" > "+_activeFolder;
    }

    private void LoadScopeAssets()
    {
        if(_workspace is null)return;
        _all.Clear();
        var assets=_workspace.ListScopeAssets(_activePaper?.Id,
            _activeProject?.Id,_activeResult?.Id);
        foreach(var entry in assets)
        {
            var usage=_workspace.ListUsages(entry.Asset.Id)
                .Where(x=>x.State is "confirmed" or "exported")
                .Select(x=>$"Fig {x.Figure}{x.Panel}").Distinct().ToArray();
            _all.Add(new AssetRow(_workspace,entry.Asset,
                usage.Length==0?"—":string.Join(", ",usage),entry.ResultName));
        }
        // The result name column is needed only for aggregate views.
        if(FileList?.View is GridView gv)
        {
            var col=gv.Columns.FirstOrDefault(c=>c.Header?.ToString()=="所属实验结果");
            if(col is not null)col.Width=_activeResult is null?155:0;
        }
        RefreshItems();
    }

    private FrameworkElement TreeLabel(string label,string glyph,int count=-1)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal};
        row.Children.Add(new TextBlock{
            Text=glyph, FontFamily=new FontFamily("Segoe MDL2 Assets"),
            FontSize=14,Foreground=new SolidColorBrush(Color.FromRgb(210,151,34)),
            Width=23,VerticalAlignment=VerticalAlignment.Center});
        row.Children.Add(new TextBlock{Text=label,FontSize=12,
            VerticalAlignment=VerticalAlignment.Center});
        if(count>=0)row.Children.Add(new TextBlock{
            Text=$" ({count})",FontSize=12,Foreground=Brushes.DimGray,
            VerticalAlignment=VerticalAlignment.Center});
        return row;
    }

    private void UpdateActionAvailability()
    {
        if(CreateProjectButton is null)return;
        CreateProjectButton.IsEnabled=_activePaper is not null && !_libraryMode;
        CreateResultButton.IsEnabled=_activeProject is not null && !_libraryMode;
        ImportButton.IsEnabled=_activeProject is not null && !_libraryMode;
        ImportProjectButton.IsEnabled=_activeProject is not null && !_libraryMode;
    }

    private static bool FindRowsContainer(ItemsControl control,DependencyObject? origin,
        out ListBoxItem? found)
    {
        found=null;
        if(origin is null)return false;
        found=ItemsControl.ContainerFromElement(control,origin) as ListBoxItem;
        return found is not null;
    }

    private void Files_PreviewMouseLeftButtonDown(object sender,MouseButtonEventArgs e)
    {
        _dragStartedOn=null;
        if(_libraryMode||sender is not ItemsControl list||
           e.OriginalSource is not DependencyObject source)return;
        _dragStart=e.GetPosition(list);
        if(FindRowsContainer(list,source,out var item))
            _dragStartedOn=item?.Content as AssetRow;
    }

    private void Files_PreviewMouseMove(object sender,MouseEventArgs e)
    {
        if(_dragging||_libraryMode||_dragStartedOn is null||
           e.LeftButton!=MouseButtonState.Pressed||sender is not ItemsControl view)return;
        var point=e.GetPosition(view);
        if(Math.Abs(point.X-_dragStart.X)<SystemParameters.MinimumHorizontalDragDistance &&
           Math.Abs(point.Y-_dragStart.Y)<SystemParameters.MinimumVerticalDragDistance)return;
        var selected=SelectedAssets();
        if(!selected.Any(x=>x.Asset.Id==_dragStartedOn.Asset.Id))
            selected=[_dragStartedOn];
        if(selected.Count==0)return;
        var bundle=new AssetDrag(_dragSession,selected.Select(x=>x.Asset.Id).ToArray());
        var data=new DataObject();
        data.SetData(typeof(AssetDrag),bundle);
        _dragging=true;
        try{System.Windows.DragDrop.DoDragDrop(view,data,
            DragDropEffects.Move|DragDropEffects.Copy);}
        finally{_dragging=false;_dragStartedOn=null;StopExpandTimer();}
    }

    private AssetDrag? ReadDrag(DragEventArgs e)
    {
        if(!e.Data.GetDataPresent(typeof(AssetDrag)))return null;
        if(e.Data.GetData(typeof(AssetDrag)) is not AssetDrag drag ||
           drag.Session!=_dragSession||drag.AssetIds.Length==0)return null;
        return drag;
    }

    private static bool IsValidDropNode(TreeViewItem? item) =>
        item?.Tag is ExperimentResult or ExperimentProject;

    private void StopExpandTimer()
    {
        _expandTimer?.Stop();
        _hoverTreeItem=null;
    }

    private void ProjectTree_DragLeave(object sender,DragEventArgs e)
    {
        if(e.GetPosition(ProjectTree) is var p &&
            (p.X<0||p.Y<0||p.X>ProjectTree.ActualWidth||p.Y>ProjectTree.ActualHeight))
            StopExpandTimer();
    }

    private void ProjectTree_PreviewDragOver(object sender,DragEventArgs e)
    {
        var node=e.OriginalSource is DependencyObject origin?TreeItemFrom(origin):null;
        var bundle=ReadDrag(e);
        if(bundle is null||!IsValidDropNode(node)||_libraryMode)
        {
            e.Effects=DragDropEffects.None;
            StopExpandTimer();
        }
        else
        {
            e.Effects=(e.KeyStates&DragDropKeyStates.ControlKey)!=0?
                DragDropEffects.Copy:DragDropEffects.Move;
            if(!ReferenceEquals(_hoverTreeItem,node))
            {
                StopExpandTimer();
                _hoverTreeItem=node;
                if(node?.HasItems==true && !node.IsExpanded)
                {
                    _expandTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(700)};
                    _expandTimer.Tick+=(_,_)=>{if(_hoverTreeItem is { } n)n.IsExpanded=true;StopExpandTimer();};
                    _expandTimer.Start();
                }
            }
        }
        e.Handled=true;
    }

    private void ProjectTree_PreviewDrop(object sender,DragEventArgs e)
    {
        StopExpandTimer();
        var data=ReadDrag(e);
        var node=e.OriginalSource is DependencyObject origin?TreeItemFrom(origin):null;
        e.Handled=true;
        if(data is null||node is null||_workspace is null||!IsValidDropNode(node))return;
        var copy=(e.KeyStates&DragDropKeyStates.ControlKey)!=0;
        try
        {
            // Paper nodes are deliberately not valid drop targets.
            string targetId=node.Tag switch
            {
                ExperimentResult result=>result.Id,
                ExperimentProject project=>_workspace.GetOrCreateUnsortedResult(project.Id),
                _=>throw new InvalidOperationException("请选择实验项目或结果单元。")
            };
            // MoveAssets / CopyAssets check all conflicts within a single SQLite
            // transaction. The first try may fail safely without partial moves.
            int Run(bool merge)=>copy?
                _workspace.CopyAssets(data.AssetIds,targetId,merge):
                _workspace.MoveAssets(data.AssetIds,targetId,merge);
            try{Run(false);}
            catch(InvalidOperationException ex) when(ex.Message.Contains("数据组"))
            {
                var decision=System.Windows.MessageBox.Show(this,
                    ex.Message+"\n确认要合并同名前缀的数据组吗？",
                    "数据组冲突",MessageBoxButton.YesNo,MessageBoxImage.Warning);
                if(decision!=MessageBoxResult.Yes)return;
                Run(true);
            }
            _activeResult=null;
            if(node.Tag is ExperimentResult dest)
            {
                _activeResult=dest;
                _activeProject=_workspace.ListProjects().FirstOrDefault(p=>p.Id==dest.ProjectId);
            }
            else if(node.Tag is ExperimentProject destProject)
                _activeProject=destProject;
            _activePaper=_workspace.ListPaperProjects()
                .FirstOrDefault(p=>_workspace.ListProjectsForPaper(p.Id)
                    .Any(x=>x.Id==_activeProject?.Id));
            LoadProjectTree();
            LoadScopeAssets();
            e.Effects=copy?DragDropEffects.Copy:DragDropEffects.Move;
        }
        catch(Exception ex){ShowError(ex);}
    }

    private void FigureTemplate_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null||_activePaper is null)return;
        var menu=new ContextMenu();
        menu.Items.Add(Action("新建 Figure",(_,_)=>{
            try
            {
                var id=_workspace.CreateFigure(_activePaper.Id);
                var f=_workspace.ListFigures(_activePaper.Id).First(x=>x.Id==id);
                System.Windows.MessageBox.Show(this,$"已新建 Figure {f.Number}。");
            }
            catch(Exception ex){ShowError(ex);}
        }));
        menu.Items.Add(Action("新建 Panel",(_,_)=>{
            try
            {
                var figures=_workspace.ListFigures(_activePaper.Id);
                if(figures.Count==0)
                {
                    System.Windows.MessageBox.Show(this,"请先新建 Figure。");
                    return;
                }
                var number=Prompt("新建 Panel","输入所属 Figure 编号",figures[^1].Number.ToString());
                if(!int.TryParse(number,out var n))return;
                var figure=figures.FirstOrDefault(f=>f.Number==n);
                if(figure is null){System.Windows.MessageBox.Show(this,"Figure 不存在。");return;}
                var defaultLabel=((char)('A'+Math.Min(25,_workspace.ListPanels(figure.Id).Count))).ToString();
                var label=Prompt("新建 Panel","输入 Panel 标签",defaultLabel);
                if(label is not null)_workspace.CreatePanel(figure.Id,label);
            }
            catch(Exception ex){ShowError(ex);}
        }));
        OpenButtonMenu(sender,menu);
    }

    private void SortMenu_Click(object sender,RoutedEventArgs e)
    {
        var menu=new ContextMenu();
        foreach(var title in new[]{"名称","阶段","数据组","所属实验结果","所在目录","大小","论文位置"})
            menu.Items.Add(Action(title,(_,_)=>{
                _sortHeader=title;_sortAscending=true;RefreshItems();
            }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Action("升序",(_,_)=>{_sortAscending=true;RefreshItems();}));
        menu.Items.Add(Action("降序",(_,_)=>{_sortAscending=false;RefreshItems();}));
        OpenButtonMenu(sender,menu);
    }

    private void ViewMenu_Click(object sender,RoutedEventArgs e)
    {
        var menu=new ContextMenu();
        menu.Items.Add(Action("详细信息",DetailsView_Click));
        menu.Items.Add(Action("大图标",ThumbnailsView_Click));
        menu.Items.Add(new Separator());
        menu.Items.Add(Action("显示 / 隐藏详情窗格",Details_Click));
        OpenButtonMenu(sender,menu);
    }

    private void MoreMenu_Click(object sender,RoutedEventArgs e)
    {
        var menu=new ContextMenu();
        menu.Items.Add(Action("复制到实验结果单元…",CopyToResult_Click));
        menu.Items.Add(Action("确认用于论文",ConfirmUsage_Click));
        menu.Items.Add(Action("复制文件路径",CopyPath_Click));
        menu.Items.Add(Action("属性",Properties_Click));
        menu.Items.Add(new Separator());
        menu.Items.Add(Action("查看软件回收站",RestoreAssets_Click));
        OpenButtonMenu(sender,menu);
    }

    private static void OpenButtonMenu(object sender,ContextMenu menu)
    {
        menu.PlacementTarget=sender as UIElement;
        menu.Placement=PlacementMode.Bottom;
        menu.IsOpen=true;
    }

    private void ExportResultFolder_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null||_activeResult is null)
        {
            System.Windows.MessageBox.Show(this,"请先选择一个实验结果单元。");
            return;
        }
        var destination=SelectFolder("选择整理后的完整结果目录导出位置");
        if(destination is null)return;
        try
        {
            var baseDir=Path.Combine(destination,_activeResult.Name);
            // Original relative structure is preserved; no destination file is
            // overwritten because ExportOriginal allocates collision-safe names.
            var rows=_workspace.ListAssets(_activeResult.Id);
            foreach(var row in rows)
            {
                var folder=Path.GetDirectoryName(row.RelativePath.Replace('/',Path.DirectorySeparatorChar))??"";
                var output=Path.GetFullPath(Path.Combine(baseDir,folder));
                if(!output.StartsWith(Path.GetFullPath(baseDir)+Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) &&
                   !output.Equals(Path.GetFullPath(baseDir),StringComparison.OrdinalIgnoreCase))
                    throw new IOException("不允许导出到目标文件夹以外。");
                _workspace.ExportOriginal(row,output);
            }
            System.Windows.MessageBox.Show(this,$"已导出 {_workspace.ListAssets(_activeResult.Id).Count} 个原始文件。");
        }
        catch(Exception ex){ShowError(ex);}
    }
}
