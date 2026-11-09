using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PaperArchive.Core.Workspace;

namespace PaperArchive.Wpf;

public partial class MainWindow
{
    private sealed record PaperFiguresNode(string PaperId);
    private sealed record FigureNode(string PaperId,string FigureId);
    private sealed record PanelNode(string PaperId,string FigureId,string PanelId);
    private sealed record FigureCompositesNode(string PaperId,string FigureId);
    private sealed record FigureRow(string Name,string Type,string Count,
        string Status,string Detail,object Item);
    private bool _figureMode;
    private FigureTemplate? _currentFigure;
    private PanelTemplate? _currentPanel;
    private bool _showOnlyComposites;

    private string FigurePath()
    {
        var path=(_activePaper?.Name??"论文项目")+" > 组图项目";
        if(_currentFigure is not null)path+=" > Figure "+_currentFigure.Number;
        if(_showOnlyComposites)path+=" > 完整组图";
        else if(_currentPanel is not null)path+=" > Panel "+_currentPanel.Label;
        return path;
    }

    private void ShowFigures(string paperId,string? figureId=null,
        string? panelId=null,bool compositesOnly=false)
    {
        if(_workspace is null)return;
        _figureMode=true;
        _showOnlyComposites=compositesOnly;
        _showManuscripts=false;
        _activePaper=_workspace.ListPaperProjects().FirstOrDefault(x=>x.Id==paperId);
        _activeProject=null;_activeResult=null;
        _currentFigure=figureId is null?null:
            _workspace.ListFigures(paperId).FirstOrDefault(x=>x.Id==figureId);
        _currentPanel=panelId is null||_currentFigure is null?null:
            _workspace.ListPanels(_currentFigure.Id).FirstOrDefault(x=>x.Id==panelId);
        FigureArea.Visibility=Visibility.Visible;
        ManuscriptArea.Visibility=Visibility.Collapsed;
        FileList.Visibility=Visibility.Collapsed;
        ThumbnailList.Visibility=Visibility.Collapsed;
        FolderList.IsEnabled=false;
        RefreshFigureView();
    }

    private void CloseFigures()
    {
        _figureMode=false;
        _currentFigure=null;_currentPanel=null;_showOnlyComposites=false;
        if(FigureArea is not null)FigureArea.Visibility=Visibility.Collapsed;
        if(FolderList is not null)FolderList.IsEnabled=true;
        if(!_libraryMode&&!_showManuscripts)
        {
            FileList.Visibility=_thumbnailMode?Visibility.Collapsed:Visibility.Visible;
            ThumbnailList.Visibility=_thumbnailMode?Visibility.Visible:Visibility.Collapsed;
        }
    }

    private void RefreshFigureView()
    {
        if(!_figureMode||_workspace is null||_activePaper is null||FigureFiles is null)return;
        var rows=new List<FigureRow>();
        if(_currentFigure is null)
        {
            foreach(var overview in _workspace.ListFigureOverview(_activePaper.Id))
                rows.Add(new($"Figure {overview.Figure.Number}","Figure 占位",
                    $"{overview.PanelCount} Panel",
                    overview.CurrentFileName is null?"未导入完整组图":"已导入完整组图",
                    $"{overview.LinkedAssetCount} 张素材 · {overview.CompositeCount} 个成图版本",
                    overview.Figure));
        }
        else if(_currentPanel is not null)
        {
            foreach(var asset in _workspace.ListPanelAssets(_currentPanel.Id))
                rows.Add(new(asset.FileName,"SHOW 素材",
                    $"{asset.Size/1024.0:0.0} KB","已关联",
                    $"{asset.ResultName} / {asset.GroupPrefix}",asset));
        }
        else if(_showOnlyComposites)
        {
            foreach(var composite in _workspace.ListFigureComposites(_currentFigure.Id))
                rows.Add(new(composite.FileName,"完整 Figure",
                    $"{composite.Size/1024.0:0.0} KB",
                    composite.IsCurrent?"当前正式版本":"历史版本",
                    $"v{composite.Version} · {composite.ImportedAt}",composite));
        }
        else
        {
            foreach(var panel in _workspace.ListPanelOverview(_currentFigure.Id))
                rows.Add(new("Panel "+panel.Panel.Label,"Panel 占位",
                    panel.LinkedAssetCount.ToString(),
                    panel.LinkedAssetCount==0?"未关联图片":"已关联",
                    "双击查看关联的 SHOW 图片",panel.Panel));
            foreach(var composite in _workspace.ListFigureComposites(_currentFigure.Id))
                rows.Add(new(composite.FileName,"完整 Figure",
                    $"{composite.Size/1024.0:0.0} KB",
                    composite.IsCurrent?"当前正式版本":"历史版本",
                    $"v{composite.Version}",composite));
        }
        var query=SearchBox?.Text.Trim()??"";
        FigureFiles.ItemsSource=rows.Where(x=>x.Name.Contains(query,StringComparison.OrdinalIgnoreCase)
            ||x.Detail.Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();
        CurrentLocation.Text=FigurePath();
        QuickFigureButton.IsEnabled=_currentFigure is null;
        BatchFigureButton.IsEnabled=_currentFigure is null;
        QuickPanelButton.IsEnabled=_currentFigure is not null&&_currentPanel is null;
        BatchPanelButton.IsEnabled=QuickPanelButton.IsEnabled;
        UpdateStatus();
        PreviewImage.Source=null;
        DetailName.Text=_currentPanel is not null?"Panel "+_currentPanel.Label:
            _currentFigure is not null?"Figure "+_currentFigure.Number:"组图项目";
        DetailMeta.Text=_currentPanel is not null?"占位引用实验项目中的 SHOW 图片，不移动原始文件。":
            "Figure/Panel 为虚拟占位，组合 Figure 图片是独立保存的文件。";
        RelatedFiles.ItemsSource=null;UsageList.ItemsSource=null;
    }

    private void FigureFiles_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(FigureFiles?.SelectedItem is not FigureRow row)
        {
            UpdateStatus();return;
        }
        PreviewImage.Source=row.Item is FigureComposite c&&_workspace is not null?
            PreviewFromFile(()=>Path.Combine(_workspace.Root,"blobs","sha256",
                c.Sha256[..2],c.Sha256)):null;
        DetailName.Text=row.Name;
        DetailMeta.Text=$"类型：{row.Type}\n状态：{row.Status}\n{row.Detail}";
        if(row.Item is PanelAssetLink link)
        {
            DetailMeta.Text+=$"\nAsset ID：{link.AssetId}\nSHA-256：{link.Sha256}";
            RelatedFiles.ItemsSource=new[]{"同组 RAW / PROCESS 可在实验项目内查看。"};
        }
        else RelatedFiles.ItemsSource=null;
        UpdateStatus();
    }

    private void FigureFiles_DoubleClick(object sender,MouseButtonEventArgs e)
    {
        if(e.OriginalSource is not DependencyObject source)return;
        if(ItemsControl.ContainerFromElement(FigureFiles,source) is not ListViewItem item||
            item.Content is not FigureRow row ||_activePaper is null)return;
        e.Handled=true;
        switch(row.Item)
        {
            case FigureTemplate figure:
                SelectTreeTag(new FigureNode(_activePaper.Id,figure.Id));break;
            case PanelTemplate panel when _currentFigure is not null:
                SelectTreeTag(new PanelNode(_activePaper.Id,_currentFigure.Id,panel.Id));break;
            case FigureComposite composite:
                OpenFigureComposite(composite);break;
        }
    }

    private void OpenFigureComposite(FigureComposite composite)
    {
        if(_workspace is null)return;
        try
        {
            var destination=Path.Combine(Path.GetTempPath(),"PaperArchive","Figure",
                composite.Id);
            var file=_workspace.ExportComposite(composite,destination);
            Process.Start(new ProcessStartInfo(file){UseShellExecute=true});
        }
        catch(Exception ex){ShowError(ex);}
    }

    private FigureTemplate? CurrentOrSelectedFigure()
    {
        if(_workspace is null)return null;
        if(_currentFigure is not null)return _currentFigure;
        return (FigureFiles?.SelectedItem as FigureRow)?.Item as FigureTemplate;
    }

    private PanelTemplate? CurrentOrSelectedPanel()
    {
        if(_currentPanel is not null)return _currentPanel;
        return (FigureFiles?.SelectedItem as FigureRow)?.Item as PanelTemplate;
    }

    private void QuickFigure_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null||_activePaper is null)return;
        try
        {
            var f=_workspace.CreateFiguresFast(_activePaper.Id,1).Single();
            LoadProjectTree();
            SelectTreeTag(new FigureNode(_activePaper.Id,f.Id));
        }
        catch(Exception ex){ShowError(ex);}
    }

    private void QuickPanel_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null||_activePaper is null||CurrentOrSelectedFigure() is not { } f)return;
        try
        {
            var p=_workspace.CreatePanelsFast(f.Id,1).Single();
            LoadProjectTree();
            SelectTreeTag(new PanelNode(_activePaper.Id,f.Id,p.Id));
        }
        catch(Exception ex){ShowError(ex);}
    }

    private static string? PromptFields(Window owner,string title,
        string label1,string label2,string default1,string default2,
        out string second)
    {
        second="";
        var dlg=new Window{Title=title,Owner=owner,Width=410,Height=235,
            ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new StackPanel{Margin=new Thickness(18)};
        panel.Children.Add(new TextBlock{Text=label1});
        var first=new TextBox{Text=default1,Margin=new Thickness(0,5,0,11)};
        panel.Children.Add(first);
        panel.Children.Add(new TextBlock{Text=label2});
        var next=new TextBox{Text=default2,Margin=new Thickness(0,5,0,13)};
        panel.Children.Add(next);
        var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var ok=new Button{Content="创建",IsDefault=true};
        ok.Click+=(_,_)=>{dlg.DialogResult=true;dlg.Close();};
        var cancel=new Button{Content="取消",IsCancel=true};
        row.Children.Add(ok);row.Children.Add(cancel);panel.Children.Add(row);
        dlg.Content=panel;
        if(dlg.ShowDialog()!=true)return null;
        second=next.Text.Trim();
        return first.Text.Trim();
    }

    private (int Count,int Panels,int Start)? AskBatchFigureOptions(string paperId)
    {
        if(_workspace is null)return null;
        var occupied=_workspace.ListFigures(paperId).Select(f=>f.Number).ToHashSet();
        var defaultStart=occupied.Count==0?1:occupied.Max()+1;
        var dlg=new Window{Title="批量新建 Figure",Owner=this,Width=475,Height=318,
            ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var body=new StackPanel{Margin=new Thickness(20)};
        var startBox=new TextBox{Text=defaultStart.ToString(),Margin=new Thickness(0,4,0,10)};
        var countBox=new TextBox{Text="4",Margin=new Thickness(0,4,0,10)};
        var panelBox=new TextBox{Text="3",Margin=new Thickness(0,4,0,10)};
        void AddInput(string label,TextBox control)
        {
            body.Children.Add(new TextBlock{Text=label});
            body.Children.Add(control);
        }
        AddInput("起始 Figure 编号（正整数）",startBox);
        AddInput("创建 Figure 数量（1–100）",countBox);
        AddInput("每张 Figure 的 Panel 数量（0–26）",panelBox);
        var preview=new TextBlock{TextWrapping=TextWrapping.Wrap,
            Foreground=System.Windows.Media.Brushes.DimGray,
            Margin=new Thickness(0,3,0,10)};
        body.Children.Add(preview);
        var accept=new Button{Content="创建",IsDefault=true};
        void Refresh()
        {
            if(!int.TryParse(startBox.Text,out var start)||start<=0||
               !int.TryParse(countBox.Text,out var count)||count<1||count>100||
               !int.TryParse(panelBox.Text,out var panels)||panels<0||panels>26)
            {
                preview.Text="请输入有效的起始编号、Figure 数量和 Panel 数量。";
                accept.IsEnabled=false;return;
            }
            var sequence=new List<int>();
            var number=start;
            while(sequence.Count<count && number<int.MaxValue)
            {
                if(!occupied.Contains(number))sequence.Add(number);
                number++;
            }
            if(sequence.Count!=count)
            {
                preview.Text="起始编号超出可用范围。";accept.IsEnabled=false;return;
            }
            preview.Text="预计创建："+string.Join("、",sequence.Take(8).Select(x=>$"Figure {x}"))+
                (sequence.Count>8?$" …… 共 {sequence.Count} 个":"")+
                (panels>0?$"；每个 Figure 自动创建 Panel A–{(char)('A'+panels-1)}":"");
            accept.IsEnabled=true;
        }
        startBox.TextChanged+=(_,_)=>Refresh();
        countBox.TextChanged+=(_,_)=>Refresh();
        panelBox.TextChanged+=(_,_)=>Refresh();
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,
            HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=new Button{Content="取消",IsCancel=true};
        accept.Click+=(_,_)=>{dlg.DialogResult=true;dlg.Close();};
        buttons.Children.Add(accept);buttons.Children.Add(cancel);
        body.Children.Add(buttons);
        dlg.Content=body;Refresh();
        return dlg.ShowDialog()==true?
            (int.Parse(countBox.Text),int.Parse(panelBox.Text),int.Parse(startBox.Text)):null;
    }

    private void BatchFigure_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null||_activePaper is null)return;
        var options=AskBatchFigureOptions(_activePaper.Id);
        if(options is not { } selected)return;
        try
        {
            var created=_workspace.CreateFiguresFast(_activePaper.Id,
                selected.Count,selected.Panels,selected.Start);
            LoadProjectTree();
            SelectTreeTag(new PaperFiguresNode(_activePaper.Id));
            RefreshFigureView();
            if(created.Count==1)FigureFiles.SelectedItem=
                FigureFiles.Items.Cast<FigureRow>()
                    .FirstOrDefault(x=>x.Item is FigureTemplate f&&f.Id==created[0].Id);
        }
        catch(Exception ex){ShowError(ex);}
    }

    private void BatchPanel_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null||_activePaper is null||CurrentOrSelectedFigure() is not { } figure)return;
        var existing=_workspace.ListPanels(figure.Id);
        var last=existing.Where(p=>p.Label.Length==1&&p.Label[0]>='A'&&p.Label[0]<='Z')
            .Select(p=>p.Label[0]).DefaultIfEmpty('@').Max();
        var first=PromptFields(this,"批量新建 Panel",
            "起始标签（A–Z）","创建 Panel 数量（1–26）",
            last<'Z'?((char)(last+1)).ToString():"A","3",out var countValue);
        if(first is null||first.Length!=1||!char.IsAsciiLetter(first[0])||
           !int.TryParse(countValue,out var count))return;
        try
        {
            var created=_workspace.CreatePanelsFast(figure.Id,count,first.ToUpperInvariant());
            LoadProjectTree();
            SelectTreeTag(new FigureNode(_activePaper.Id,figure.Id));
            RefreshFigureView();
            var lastCreated=created[^1];
            FigureFiles.SelectedItem=FigureFiles.Items.Cast<FigureRow>()
                .FirstOrDefault(x=>x.Item is PanelTemplate p&&p.Id==lastCreated.Id);
        }
        catch(Exception ex){ShowError(ex);}
    }

    private PanelTemplate? ChoosePanel(string paperId)
    {
        if(_workspace is null)return null;
        var figures=_workspace.ListFigures(paperId);
        if(figures.Count==0)
        {
            System.Windows.MessageBox.Show(this,"请先在组图项目中创建 Figure 和 Panel。");return null;
        }
        var dlg=new Window{Title="选择组图 Panel",Owner=this,Width=455,Height=240,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
        var layout=new StackPanel{Margin=new Thickness(17)};
        layout.Children.Add(new TextBlock{Text="目标 Figure"});
        var fBox=new ComboBox{ItemsSource=figures,DisplayMemberPath="Number",
            SelectedItem=figures.FirstOrDefault(x=>x.Id==_currentFigure?.Id)??figures[0],
            Height=30,Margin=new Thickness(0,7,0,13)};
        layout.Children.Add(fBox);
        layout.Children.Add(new TextBlock{Text="目标 Panel"});
        var pBox=new ComboBox{DisplayMemberPath="Label",Height=30,Margin=new Thickness(0,7,0,13)};
        void RefreshPanels()
        {
            pBox.ItemsSource=fBox.SelectedItem is FigureTemplate f?
                _workspace.ListPanels(f.Id):null;
            if(pBox.Items.Count>0)pBox.SelectedItem=_currentPanel is null?pBox.Items[0]:
                pBox.Items.Cast<PanelTemplate>().FirstOrDefault(x=>x.Id==_currentPanel.Id)??pBox.Items[0];
        }
        fBox.SelectionChanged+=(_,_)=>RefreshPanels();
        layout.Children.Add(pBox);
        var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var ok=new Button{Content="确定",IsDefault=true};
        ok.Click+=(_,_)=>{if(pBox.SelectedItem is null)
        {System.Windows.MessageBox.Show(dlg,"请选择已有 Panel。");return;}
        dlg.DialogResult=true;dlg.Close();};
        var cancel=new Button{Content="取消",IsCancel=true};
        row.Children.Add(ok);row.Children.Add(cancel);layout.Children.Add(row);
        dlg.Content=layout;RefreshPanels();
        return dlg.ShowDialog()==true?pBox.SelectedItem as PanelTemplate:null;
    }

    private void LinkSelectedToPanel_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null)return;
        var selected=SelectedAssets();
        if(selected.Count==0)
        {
            System.Windows.MessageBox.Show(this,"请先在实验项目选中一张或多张 SHOW 图片。");return;
        }
        if(selected.Any(x=>x.Asset.Stage!="show"))
        {
            System.Windows.MessageBox.Show(this,"只有 SHOW 图片可以关联到 Panel。");return;
        }
        var paperId=_activePaper?.Id;
        if(paperId is null)return;
        var target=ChoosePanel(paperId);
        if(target is null)return;
        try
        {
            _workspace.LinkShowAssets(target.Id,selected.Select(x=>x.Asset.Id));
            LoadProjectTree();
            RefreshItems();
        }
        catch(Exception ex){ShowError(ex);}
    }

    private void LinkShowFromFigure_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null||_activePaper is null)return;
        var panel=CurrentOrSelectedPanel()??ChoosePanel(_activePaper.Id);
        if(panel is null)return;
        var choices=_workspace.ListScopeAssets(paperId:_activePaper.Id)
            .Where(x=>x.Asset.Stage=="show")
            .ToArray();
        if(choices.Length==0)
        {
            System.Windows.MessageBox.Show(this,"该论文项目尚未导入 SHOW 图片。");return;
        }
        var dlg=new Window{Title="关联实验 SHOW 图片到 Panel "+panel.Label,
            Owner=this,Width=620,Height=475,
            WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var root=new DockPanel{Margin=new Thickness(14)};
        var footer=new StackPanel{Orientation=Orientation.Horizontal,
            HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};
        DockPanel.SetDock(footer,Dock.Bottom);
        var ok=new Button{Content="关联选中图片",IsDefault=true};
        ok.Click+=(_,_)=>{dlg.DialogResult=true;dlg.Close();};
        var cancel=new Button{Content="取消",IsCancel=true};
        footer.Children.Add(ok);footer.Children.Add(cancel);root.Children.Add(footer);
        var list=new ListView{SelectionMode=SelectionMode.Extended};
        list.ItemsSource=choices.Select(x=>new ShowChoice(x)).ToArray();
        list.View=new GridView
        {
            Columns=
            {
                new GridViewColumn{Header="文件名",DisplayMemberBinding=new System.Windows.Data.Binding("Name"),Width=220},
                new GridViewColumn{Header="实验结果",DisplayMemberBinding=new System.Windows.Data.Binding("ResultName"),Width=170},
                new GridViewColumn{Header="数据组",DisplayMemberBinding=new System.Windows.Data.Binding("Prefix"),Width=120}
            }
        };
        root.Children.Add(list);dlg.Content=root;
        if(dlg.ShowDialog()!=true)return;
        var ids=list.SelectedItems.Cast<ShowChoice>().Select(x=>x.AssetId).ToArray();
        if(ids.Length==0)return;
        try
        {
            _workspace.LinkShowAssets(panel.Id,ids);
            LoadProjectTree();
            if(_currentFigure is not null)RefreshFigureView();
            else SelectTreeTag(new PanelNode(_activePaper.Id,
                _workspace.ListFigures(_activePaper.Id)
                    .First(f=>_workspace.ListPanels(f.Id).Any(p=>p.Id==panel.Id)).Id,panel.Id));
        }
        catch(Exception ex){ShowError(ex);}
    }

    private sealed record ShowChoice(ScopedExperimentAsset Entry)
    {
        public string AssetId=>Entry.Asset.Id;
        public string Name=>Entry.Asset.FileName;
        public string ResultName=>Entry.ResultName;
        public string Prefix=>Entry.Asset.GroupPrefix;
    }

    private void ExportFigureMaterials_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null||CurrentOrSelectedFigure() is not { } figure)return;
        var folder=SelectFolder("选择 Figure "+figure.Number+" 的全部素材导出目录");
        if(folder is null)return;
        try
        {
            var target=Path.Combine(folder,"Figure_"+figure.Number+"_组图素材");
            var result=_workspace.ExportFigureMaterials(figure.Id,target);
            System.Windows.MessageBox.Show(this,
                $"已导出 {result.ExportedCount} 张 SHOW 素材。\n按 Panel 分目录，并生成素材清单。\n{result.TargetFolder}",
                "批量导出完成");
        }
        catch(Exception ex){ShowError(ex);}
    }

    private void ImportComposite_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null||CurrentOrSelectedFigure() is not { } figure)return;
        var dialog=new Microsoft.Win32.OpenFileDialog
        {
            Title="导入完整 Figure "+figure.Number,
            Filter="科研成图 (*.tif;*.tiff;*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.tif;*.tiff;*.png;*.jpg;*.jpeg;*.bmp;*.webp",
            Multiselect=false
        };
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            var composite=_workspace.ImportFigureComposite(figure.Id,dialog.FileName);
            LoadProjectTree();
            RefreshFigureView();
            System.Windows.MessageBox.Show(this,
                $"已导入 {composite.FileName}（版本 v{composite.Version}）。\n原始字节已安全归档。",
                "完整组图导入");
        }
        catch(Exception ex){ShowError(ex);}
    }

    private FigureComposite? CurrentOrSelectedComposite()
    {
        if(FigureFiles?.SelectedItem is FigureRow{Item:FigureComposite c})return c;
        return _workspace is null||_currentFigure is null?null:
            _workspace.ListFigureComposites(_currentFigure.Id).FirstOrDefault(x=>x.IsCurrent);
    }

    private void ExportComposite_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null||CurrentOrSelectedComposite() is not { } composite)return;
        var folder=SelectFolder("选择完整 Figure 图片无损导出目录");
        if(folder is null)return;
        try
        {
            var file=_workspace.ExportComposite(composite,folder);
            System.Windows.MessageBox.Show(this,"已无损导出："+file);
        }
        catch(Exception ex){ShowError(ex);}
    }

    private void SetCurrentComposite_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null||_currentFigure is null||
            FigureFiles?.SelectedItem is not FigureRow{Item:FigureComposite c})return;
        try
        {
            _workspace.SetCurrentComposite(_currentFigure.Id,c.Id);
            RefreshFigureView();
        }
        catch(Exception ex){ShowError(ex);}
    }

    private void UnlinkPanelAsset_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null||FigureFiles?.SelectedItem is not FigureRow{Item:PanelAssetLink link})return;
        if(System.Windows.MessageBox.Show(this,"取消该 SHOW 素材与当前 Panel 的关联？\n原始科研文件不会删除。",
            "取消关联",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        try{_workspace.UnlinkPanelAsset(link.Id);LoadProjectTree();RefreshFigureView();}
        catch(Exception ex){ShowError(ex);}
    }

    private void FigureFiles_ContextMenuOpening(object sender,ContextMenuEventArgs e)
    {
        if(!_figureMode)return;
        var menu=new ContextMenu();
        if(_currentFigure is null)
        {
            menu.Items.Add(Action("新建 Figure",QuickFigure_Click));
            menu.Items.Add(Action("批量新建 Figure…",BatchFigure_Click));
        }
        else if(_currentPanel is not null)
        {
            menu.Items.Add(Action("关联 SHOW 素材…",LinkShowFromFigure_Click));
            menu.Items.Add(Action("取消关联（所选）",UnlinkPanelAsset_Click));
        }
        else
        {
            menu.Items.Add(Action("新建 Panel",QuickPanel_Click));
            menu.Items.Add(Action("批量新建 Panel…",BatchPanel_Click));
            menu.Items.Add(Action("关联 SHOW 素材…",LinkShowFromFigure_Click));
            menu.Items.Add(new Separator());
            menu.Items.Add(Action("导出全部组图素材…",ExportFigureMaterials_Click));
            menu.Items.Add(Action("导入完整 Figure…",ImportComposite_Click));
            menu.Items.Add(Action("导出完整 Figure…",ExportComposite_Click));
            menu.Items.Add(Action("设为当前正式成图（所选版本）",SetCurrentComposite_Click));
        }
        FigureFiles.ContextMenu=menu;
    }

    private bool HandleFigureShortcut(KeyEventArgs e)
    {
        if(_libraryMode || Keyboard.Modifiers!=(ModifierKeys.Control|ModifierKeys.Shift))
            return false;
        if(e.Key==Key.F&&_figureMode&&_currentFigure is null)
        {
            QuickFigure_Click(this,new RoutedEventArgs());return true;
        }
        if(e.Key==Key.P&&_figureMode&&_currentFigure is not null)
        {
            QuickPanel_Click(this,new RoutedEventArgs());return true;
        }
        return false;
    }
}
