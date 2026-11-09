using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PaperArchive.Core.Workspace;

namespace PaperArchive.Wpf;

public partial class MainWindow
{
    private PaperProject? _activePaper;
    private readonly Dictionary<string,string> _columnFilters = new(StringComparer.Ordinal);
    private string _sortHeader="名称";
    private bool _sortAscending=true;
    private IReadOnlyList<string>? _cutAssetIds;
    private bool _clipboardCut;

    private void CreatePaper_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null)return;
        var name=Prompt("新建论文项目","论文工作名称（最终标题可以后修改）");
        if(name is null)return;
        try
        {
            var id=_workspace.CreatePaperProject(name);
            _activePaper=_workspace.ListPaperProjects().Single(x=>x.Id==id);
            _activeProject=null;_activeResult=null;_all.Clear();_visible.Clear();
            LoadProjectTree();
        }
        catch(Exception ex){ShowError(ex);}
    }

    private void CreateResult_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null||_activeProject is null)
        {
            System.Windows.MessageBox.Show(this,"请先选择论文项目中的实验项目。","创建实验结果");
            return;
        }
        var name=Prompt("新建实验结果单元","输入新的实验结果分类名称（例如：ROS 检测）");
        if(name is null)return;
        try
        {
            var id=_workspace.CreateResult(_activeProject.Id,name);
            _activeResult=_workspace.ListResults(_activeProject.Id).Single(x=>x.Id==id);
            _all.Clear();_visible.Clear();LoadProjectTree();RebuildFolderList();
        }
        catch(Exception ex){ShowError(ex);}
    }

    private async void ImportProject_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null||_activeProject is null)
        {
            System.Windows.MessageBox.Show(this,"先选择论文项目及其下的实验项目。","导入完整实验项目");
            return;
        }
        var folder=SelectFolder("选择尚未分类的完整实验项目文件夹");
        if(folder is null)return;
        var projectId=_activeProject.Id;
        try
        {
            ImportButton.IsEnabled=false;
            var result=await Task.Run(()=>_workspace.ImportUnsorted(projectId,folder));
            _activeResult=_workspace.ListResults(projectId).Single(x=>x.Id==result.ResultId);
            LoadSelectedResult();RebuildFolderList();LoadProjectTree();
            System.Windows.MessageBox.Show(this,$"已导入 {result.FileCount} 个文件至「待整理数据」。\n现在可创建实验结果单元并通过右键「移动到」整理。","导入完成");
        }
        catch(Exception ex){ShowError(ex);}
        finally{ImportButton.IsEnabled=true;UpdateStatus();}
    }

    private void RenameSelected_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null)return;
        try
        {
            if(SelectedAsset() is { } asset)
            {
                var rename=Prompt("重命名文件","修改工作区内的文件显示名称（原始仓库不修改）",asset.FileName);
                if(rename is null)return;
                _workspace.RenameAsset(asset.Asset.Id,rename);
                LoadSelectedResult();
                return;
            }
            if(ProjectTree.SelectedItem is not TreeViewItem node)return;
            switch(node.Tag)
            {
                case PaperProject paper:
                    var p=Prompt("重命名论文项目","论文项目名称",paper.Name);
                    if(p is not null){_workspace.RenamePaperProject(paper.Id,p);_activePaper=new(paper.Id,p);LoadProjectTree();}
                    break;
                case ExperimentProject project:
                    var eName=Prompt("重命名实验项目","实验项目名称",project.Name);
                    if(eName is not null){_workspace.RenameProject(project.Id,eName);_activeProject=new(project.Id,eName);LoadProjectTree();}
                    break;
                case ExperimentResult result:
                    var r=Prompt("重命名实验结果单元","结果单元名称",result.Name);
                    if(r is not null){_workspace.RenameResult(result.Id,r);_activeResult=result with {Name=r};LoadProjectTree();RefreshItems();}
                    break;
            }
        }
        catch(Exception ex){ShowError(ex);}
    }

    private sealed record ResultChoice(string Id,string Label)
    {
        public override string ToString()=>Label;
    }

    private string? PickResult(string title,bool excludeCurrent=false)
    {
        if(_workspace is null||_activePaper is null)return null;
        var choices=_workspace.ListProjectsForPaper(_activePaper.Id)
            .SelectMany(p=>_workspace.ListResults(p.Id)
                .Where(r=>!excludeCurrent||r.Id!=_activeResult?.Id)
                .Select(r=>new ResultChoice(r.Id,$"{p.Name}  /  {r.Name}")))
            .ToArray();
        if(choices.Length==0)
        {
            System.Windows.MessageBox.Show(this,"请先在论文项目中创建目标实验结果单元。","无可用目标");
            return null;
        }
        var dialog=new Window{Title=title,Owner=this,Width=520,Height=170,
            ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var stack=new StackPanel{Margin=new Thickness(18)};
        stack.Children.Add(new TextBlock{Text="选择目标实验结果单元",Margin=new Thickness(0,0,0,8)});
        var cb=new ComboBox{ItemsSource=choices,SelectedIndex=0,MinHeight=29};
        stack.Children.Add(cb);
        var ok=new Button{Content="确定",HorizontalAlignment=HorizontalAlignment.Right,
            Margin=new Thickness(0,12,0,0),IsDefault=true};
        ok.Click+=(_,_)=>{dialog.DialogResult=true;dialog.Close();};
        stack.Children.Add(ok);dialog.Content=stack;
        return dialog.ShowDialog()==true?(cb.SelectedItem as ResultChoice)?.Id:null;
    }

    private void MoveOrCopyAssets(bool copy)
    {
        if(_workspace is null)return;
        var rows=SelectedAssets();
        if(rows.Count==0)return;
        var target=PickResult(copy?"复制到实验结果单元":"移动到实验结果单元",!copy);
        if(target is null)return;
        var ids=rows.Select(x=>x.Asset.Id).ToArray();
        try
        {
            int DoOperation(bool merge)=>copy?
                _workspace.CopyAssets(ids,target,merge):_workspace.MoveAssets(ids,target,merge);
            int count;
            try{count=DoOperation(false);}
            catch(InvalidOperationException ex) when(ex.Message.Contains("数据组"))
            {
                if(System.Windows.MessageBox.Show(this,
                    ex.Message+"\n确定要将同名前缀的数据合并到同一数据组吗？",
                    "数据组冲突",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
                count=DoOperation(true);
            }
            LoadSelectedResult();RebuildFolderList();LoadProjectTree();
            System.Windows.MessageBox.Show(this,$"已{(copy?"复制":"移动")} {count} 个文件。\n原始文件未发生修改。","整理完成");
        }
        catch(Exception ex){ShowError(ex);}
    }

    private void MoveSelected_Click(object sender,RoutedEventArgs e)=>MoveOrCopyAssets(false);
    private void CopyToResult_Click(object sender,RoutedEventArgs e)=>MoveOrCopyAssets(true);

    private void DeleteAssets_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null)return;
        var rows=SelectedAssets();
        if(rows.Count==0)return;
        if(System.Windows.MessageBox.Show(this,
            $"将 {rows.Count} 个文件移入软件回收站？\n原始文件及物理仓库内容不会被删除。",
            "确认删除",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        try{_workspace.SetAssetDeleted(rows.Select(r=>r.Asset.Id),true);LoadSelectedResult();RebuildFolderList();}
        catch(Exception ex){ShowError(ex);}
    }

    private void RestoreAssets_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null||_activeResult is null)return;
        var deleted=_workspace.ListDeletedAssets(_activeResult.Id);
        if(deleted.Count==0)
        {
            System.Windows.MessageBox.Show(this,"当前实验结果没有已删除文件。","软件回收站");
            return;
        }
        var dialog=new Window{Title="软件回收站 · 恢复文件",Owner=this,Width=580,Height=400,
            WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var root=new DockPanel{Margin=new Thickness(14)};
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,
            HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};
        DockPanel.SetDock(buttons,Dock.Bottom);
        var cancel=new Button{Content="取消",IsCancel=true};
        var restore=new Button{Content="恢复所选",IsDefault=true};
        buttons.Children.Add(restore);buttons.Children.Add(cancel);root.Children.Add(buttons);
        var list=new ListBox{SelectionMode=SelectionMode.Extended,
            ItemsSource=deleted,DisplayMemberPath="RelativePath"};
        root.Children.Add(list);
        restore.Click+=(_,_)=>{if(list.SelectedItems.Count>0){dialog.DialogResult=true;dialog.Close();}};
        dialog.Content=root;
        if(dialog.ShowDialog()!=true)return;
        try
        {
            var ids=list.SelectedItems.OfType<ExperimentAsset>().Select(x=>x.Id).ToArray();
            _workspace.SetAssetDeleted(ids,false);
            LoadSelectedResult();RebuildFolderList();
        }
        catch(Exception ex){ShowError(ex);}
    }

    private void CutAssets_Click(object sender,RoutedEventArgs e)
    {
        _cutAssetIds=SelectedAssets().Select(x=>x.Asset.Id).ToArray();
        _clipboardCut=true;
    }

    private void CopyAssetsReference_Click(object sender,RoutedEventArgs e)
    {
        _cutAssetIds=SelectedAssets().Select(x=>x.Asset.Id).ToArray();
        _clipboardCut=false;
    }

    private void PasteAssets_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null||_cutAssetIds is not {Count:>0})return;
        var target=_activeResult?.Id??PickResult("粘贴到实验结果");
        if(target is null)return;
        try
        {
            if(_clipboardCut)_workspace.MoveAssets(_cutAssetIds,target);
            else _workspace.CopyAssets(_cutAssetIds,target);
            if(_clipboardCut)_cutAssetIds=null;
            LoadSelectedResult();RebuildFolderList();LoadProjectTree();
        }
        catch(InvalidOperationException ex) when(ex.Message.Contains("数据组"))
        {
            if(System.Windows.MessageBox.Show(this,
                ex.Message+"\n合并同名数据组？","数据组冲突",MessageBoxButton.YesNo)==MessageBoxResult.Yes)
            {
                try
                {
                    if(_clipboardCut)_workspace.MoveAssets(_cutAssetIds!,target,true);
                    else _workspace.CopyAssets(_cutAssetIds!,target,true);
                    if(_clipboardCut)_cutAssetIds=null;
                    LoadSelectedResult();RebuildFolderList();LoadProjectTree();
                }
                catch(Exception inner){ShowError(inner);}
            }
        }
        catch(Exception ex){ShowError(ex);}
    }

    private void CopyPath_Click(object sender,RoutedEventArgs e)
    {
        if(SelectedAsset() is { } row)
            System.Windows.Clipboard.SetText(row.Asset.RelativePath);
    }

    private void Properties_Click(object sender,RoutedEventArgs e)
    {
        if(!_detailsVisible)ToggleDetails();
        UpdateDetails();
    }

    private static MenuItem Action(string label,RoutedEventHandler handler)
    {
        var item=new MenuItem{Header=label};
        item.Click+=handler;
        return item;
    }

    private static TreeViewItem? TreeItemFrom(DependencyObject? origin)
    {
        while(origin is not null)
        {
            if(origin is TreeViewItem node)return node;
            origin=VisualTreeHelper.GetParent(origin);
        }
        return null;
    }

    private void ProjectTree_PreviewMouseRightButtonDown(object sender,MouseButtonEventArgs e)
    {
        if(e.OriginalSource is DependencyObject src && TreeItemFrom(src) is { } node)
            node.IsSelected=true;
    }

    private void ProjectTree_ContextMenuOpening(object sender,ContextMenuEventArgs e)
    {
        if(_libraryMode)return;
        var menu=new ContextMenu();
        menu.Items.Add(Action("新建论文项目",CreatePaper_Click));
        if(ProjectTree.SelectedItem is TreeViewItem {Tag: PaperProject})
            menu.Items.Add(Action("新建实验项目",CreateProject_Click));
        if(_activeProject is not null)
        {
            menu.Items.Add(Action("新建实验结果单元",CreateResult_Click));
            menu.Items.Add(Action("导入完整实验项目（待整理）",ImportProject_Click));
            menu.Items.Add(Action("导入单个实验结果",Import_Click));
            menu.Items.Add(new Separator());
            menu.Items.Add(Action("重命名    F2",RenameSelected_Click));
        }
        ProjectTree.ContextMenu=menu;
    }

    private void FolderList_ContextMenuOpening(object sender,ContextMenuEventArgs e)
    {
        if(_libraryMode)return;
        var menu=new ContextMenu();
        menu.Items.Add(Action("新建实验结果单元",CreateResult_Click));
        menu.Items.Add(Action("导入完整实验项目",ImportProject_Click));
        menu.Items.Add(Action("粘贴到当前结果    Ctrl+V",PasteAssets_Click));
        menu.Items.Add(Action("查看回收站",RestoreAssets_Click));
        FolderList.ContextMenu=menu;
    }

    private void Files_PreviewMouseRightButtonDown(object sender,MouseButtonEventArgs e)
    {
        if(e.OriginalSource is not DependencyObject source||sender is not ItemsControl items)return;
        if(ItemsControl.ContainerFromElement(items,source) is ListBoxItem item&&!item.IsSelected)
        {
            items.UnselectAll();
            item.IsSelected=true;
        }
    }

    private void Files_ContextMenuOpening(object sender,ContextMenuEventArgs e)
    {
        if(_libraryMode)return;
        var menu=new ContextMenu();
        menu.Items.Add(Action("打开",(_,_)=>OpenSelectedAsset()));
        menu.Items.Add(Action("复制图片 / 文件    Ctrl+C",Copy_Click));
        menu.Items.Add(Action("剪切    Ctrl+X",CutAssets_Click));
        menu.Items.Add(Action("复制到实验结果单元",CopyToResult_Click));
        menu.Items.Add(Action("移动到实验结果单元",MoveSelected_Click));
        menu.Items.Add(Action("粘贴    Ctrl+V",PasteAssets_Click));
        menu.Items.Add(new Separator());
        menu.Items.Add(Action("重命名    F2",RenameSelected_Click));
        menu.Items.Add(Action("复制相对路径",CopyPath_Click));
        menu.Items.Add(Action("导出原图…",ExportOriginal_Click));
        if(SelectedAssets().All(x=>x.Asset.Stage=="show"))
            menu.Items.Add(Action("导出用于组图…",Figure_Click));
        menu.Items.Add(new Separator());
        menu.Items.Add(Action("删除到软件回收站    Delete",DeleteAssets_Click));
        menu.Items.Add(Action("属性    Alt+Enter",Properties_Click));
        (sender as FrameworkElement)!.ContextMenu=menu;
    }

    private void ColumnHeader_Click(object sender,RoutedEventArgs e)
    {
        if(e.OriginalSource is not GridViewColumnHeader header || header.Role==GridViewColumnHeaderRole.Padding)return;
        var column=header.Content?.ToString()??"";
        if(string.IsNullOrEmpty(column))return;
        if(_sortHeader==column)_sortAscending=!_sortAscending;
        else{_sortHeader=column;_sortAscending=true;}
        RefreshItems();
        e.Handled=true;
    }

    private void ColumnHeader_ContextMenuOpening(object sender,ContextMenuEventArgs e)
    {
        var header=sender as GridViewColumnHeader;
        var column=header?.Content?.ToString()??"";
        if(column is not ("名称" or "阶段" or "数据组" or "所在目录" or "大小" or "论文位置"))return;
        var menu=new ContextMenu();
        menu.Items.Add(Action("全部（清除该列筛选）",(_,_)=>{_columnFilters.Remove(column);RefreshItems();}));
        if(column=="阶段")
            foreach(var value in new[]{"RAW","PROCESS","SHOW","统计表","其他"})
                menu.Items.Add(Action(value,(_,_)=>{_columnFilters[column]=value;RefreshItems();}));
        else if(column=="数据组")
            foreach(var value in _all.Select(x=>x.GroupPrefix).Distinct().OrderBy(x=>x).Take(80))
                menu.Items.Add(Action(value,(_,_)=>{_columnFilters[column]=value;RefreshItems();}));
        else if(column=="论文位置")
        {
            menu.Items.Add(Action("未用于论文",(_,_)=>{_columnFilters[column]="—";RefreshItems();}));
            menu.Items.Add(Action("已用于论文",(_,_)=>{_columnFilters[column]="*";RefreshItems();}));
        }
        else menu.Items.Add(Action("设置筛选条件…",(_,_)=>
        {
            var initial=_columnFilters.GetValueOrDefault(column,"");
            var filter=Prompt("筛选 · "+column,column=="大小"?"输入最小文件大小（MB）": "输入包含的文字",initial);
            if(filter is null)return;
            _columnFilters[column]=filter;
            RefreshItems();
        }));
        header!.ContextMenu=menu;
    }

    private bool AcceptColumnFilters(AssetRow row)
    {
        foreach(var (column,value) in _columnFilters)
        {
            switch(column)
            {
                case "名称" when !row.FileName.Contains(value,StringComparison.OrdinalIgnoreCase):return false;
                case "阶段" when !row.StageLabel.Equals(value,StringComparison.OrdinalIgnoreCase):return false;
                case "数据组" when !row.GroupPrefix.Equals(value,StringComparison.OrdinalIgnoreCase):return false;
                case "所在目录" when !row.RelativePath.Contains(value,StringComparison.OrdinalIgnoreCase):return false;
                case "论文位置" when value=="*" && row.UsageLabel=="—":return false;
                case "论文位置" when value!="*" && !row.UsageLabel.Contains(value,StringComparison.OrdinalIgnoreCase):return false;
                case "大小" when double.TryParse(value,out var minMegabytes) && row.Asset.Size < minMegabytes*1024*1024:return false;
            }
        }
        return true;
    }

    private IEnumerable<AssetRow> ApplyTableSort(IEnumerable<AssetRow> rows)
    {
        Func<AssetRow,object> key=_sortHeader switch
        {
            "阶段"=>r=>r.Asset.Stage switch {"raw"=>0,"process"=>1,"show"=>2,"statistics"=>3,_=>4},
            "数据组"=>r=>r.GroupPrefix,
            "所在目录"=>r=>Path.GetDirectoryName(r.RelativePath)?.Replace('\\','/')??"",
            "大小"=>r=>r.Asset.Size,
            "论文位置"=>r=>FigureOrder(r.UsageLabel),
            _=>r=>r.FileName
        };
        return _sortAscending?rows.OrderBy(key).ThenBy(x=>x.FileName,StringComparer.OrdinalIgnoreCase)
            :rows.OrderByDescending(key).ThenBy(x=>x.FileName,StringComparer.OrdinalIgnoreCase);
    }

    private static long FigureOrder(string label)
    {
        var match=Regex.Match(label,@"(?:Fig(?:ure)?\s*)(\d+)([A-Za-z]*)",RegexOptions.IgnoreCase);
        if(!match.Success)return long.MaxValue;
        return long.Parse(match.Groups[1].Value)*1000+
            (match.Groups[2].Value.Length>0?char.ToUpperInvariant(match.Groups[2].Value[0])-64:0);
    }

    private void ShowFigureDialog()
    {
        if(_workspace is null||SelectedAssets() is not {Count:>0} rows)return;
        if(rows.Any(x=>x.Asset.Stage!="show"))
        {
            System.Windows.MessageBox.Show(this,"仅 show_data 图片可以建立论文组图用途。","组图导出");
            return;
        }
        var papers=_workspace.ListPaperProjects().Where(x=>!x.Name.StartsWith("历史工作区")).ToArray();
        if(papers.Length==0)
        {
            System.Windows.MessageBox.Show(this,"请先创建论文项目。","论文项目未建立");
            return;
        }
        var dlg=new Window{Title="导出用于组图",Owner=this,Width=520,Height=395,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
        var grid=new Grid{Margin=new Thickness(20)};
        for(int i=0;i<7;i++)grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var title=new TextBlock{Text=$"已选择 {rows.Count} 个 SHOW 文件",FontWeight=FontWeights.SemiBold,
            Margin=new Thickness(0,0,0,16)};
        Grid.SetRow(title,0);grid.Children.Add(title);
        var paperBox=new ComboBox{ItemsSource=papers,DisplayMemberPath="Name",SelectedItem=
            papers.FirstOrDefault(x=>x.Id==_activePaper?.Id)??papers[0],Height=30,Margin=new Thickness(0,0,0,10)};
        var figBox=new ComboBox{DisplayMemberPath="Number",Height=30,MinWidth=220};
        var panelBox=new ComboBox{DisplayMemberPath="Label",Height=30,MinWidth=220};
        var newFig=new Button{Content="+ 新建 Figure"};
        var newPanel=new Button{Content="+ 新建 Panel"};
        void RefreshPanels()
        {
            panelBox.ItemsSource=figBox.SelectedItem is FigureTemplate fig?
                _workspace.ListPanels(fig.Id):null;
            if(panelBox.Items.Count>0)panelBox.SelectedIndex=0;
        }
        void RefreshFigures()
        {
            figBox.ItemsSource=paperBox.SelectedItem is PaperProject p?
                _workspace.ListFigures(p.Id):null;
            if(figBox.Items.Count>0)figBox.SelectedIndex=0;
            else panelBox.ItemsSource=null;
        }
        paperBox.SelectionChanged+=(_,_)=>RefreshFigures();
        figBox.SelectionChanged+=(_,_)=>RefreshPanels();
        newFig.Click+=(_,_)=>
        {
            if(paperBox.SelectedItem is not PaperProject p)return;
            try{_workspace.CreateFigure(p.Id);RefreshFigures();figBox.SelectedIndex=figBox.Items.Count-1;}
            catch(Exception ex){ShowError(ex);}
        };
        newPanel.Click+=(_,_)=>
        {
            if(figBox.SelectedItem is not FigureTemplate f)return;
            var label=Prompt("新建 Panel","Panel 标签（A、B、C…）",
                ((char)('A'+Math.Min(25,panelBox.Items.Count))).ToString());
            if(label is null)return;
            try{_workspace.CreatePanel(f.Id,label);RefreshPanels();panelBox.SelectedIndex=panelBox.Items.Count-1;}
            catch(Exception ex){ShowError(ex);}
        };
        void Row(int index,string label,Control input,Button? action=null)
        {
            var wrap=new StackPanel{Margin=new Thickness(0,0,0,8)};
            wrap.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,0,0,5)});
            var horizontal=new DockPanel();
            if(action is not null)
            {
                DockPanel.SetDock(action,Dock.Right);
                horizontal.Children.Add(action);
            }
            horizontal.Children.Add(input);
            wrap.Children.Add(horizontal);
            Grid.SetRow(wrap,index);grid.Children.Add(wrap);
        }
        Row(1,"目标论文项目",paperBox);
        Row(2,"Figure",figBox,newFig);
        Row(3,"Panel",panelBox,newPanel);
        var hint=new TextBlock{Text="文件将按原始字节导出；用途先记录为「已导出」，最终使用另行确认。",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,9,0,13)};
        Grid.SetRow(hint,4);grid.Children.Add(hint);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=new Button{Content="取消",IsCancel=true};
        var ok=new Button{Content="选择导出目录…",IsDefault=true};
        ok.Click+=(_,_)=>{if(figBox.SelectedItem is null||panelBox.SelectedItem is null)
        {System.Windows.MessageBox.Show(dlg,"请先选择或创建 Figure 和 Panel。");return;}
        dlg.DialogResult=true;dlg.Close();};
        buttons.Children.Add(ok);buttons.Children.Add(cancel);Grid.SetRow(buttons,5);grid.Children.Add(buttons);
        dlg.Content=grid;RefreshFigures();
        if(dlg.ShowDialog()!=true||
            paperBox.SelectedItem is not PaperProject selectedPaper||
            figBox.SelectedItem is not FigureTemplate selectedFigure||
            panelBox.SelectedItem is not PanelTemplate selectedPanel)return;
        var folder=SelectFolder("选择组图文件导出位置");
        if(folder is null)return;
        try
        {
            foreach(var row in rows)
            {
                var usage=_workspace.ExportForFigure(row.Asset,selectedPaper.Name,
                    selectedFigure.Number,selectedPanel.Label,folder);
                _workspace.AssignUsageToPaper(usage.Id,selectedPaper.Id);
            }
            LoadSelectedResult();
            System.Windows.MessageBox.Show(this,
                $"已导出 {rows.Count} 个文件并关联 Figure {selectedFigure.Number}{selectedPanel.Label}。","组图导出完成");
        }
        catch(Exception ex){ShowError(ex);}
    }
}
