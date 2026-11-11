using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PaperArchive.Core.Workspace;

namespace PaperArchive.Wpf;

public partial class MainWindow
{
    private sealed record PaperDocumentsNode(string PaperId);
    private sealed record PaperExperimentsNode(string PaperId);
    private bool _showManuscripts;
    private readonly List<PaperDocument> _paperDocs=[];

    private void LoadManuscripts(string paperId)
    {
        if(_workspace is null)return;
        _showManuscripts=true;
        _activeProject=null;
        _activeResult=null;
        _paperDocs.Clear();
        _paperDocs.AddRange(_workspace.ListPaperDocuments(paperId));
        ManuscriptArea.Visibility=Visibility.Visible;
        FileList.Visibility=Visibility.Collapsed;
        ThumbnailList.Visibility=Visibility.Collapsed;
        FolderList.IsEnabled=false;
        UpdateManuscriptList();
        PreviewImage.Source=null;
        DetailName.Text="论文全文";
        DetailMeta.Text="PDF / DOC / DOCX 文稿独立存放在论文项目中。双击文稿调用系统默认应用打开。";
        RelatedFiles.ItemsSource=null;
        UsageList.ItemsSource=null;
    }

    private void CloseManuscripts()
    {
        _showManuscripts=false;
        if(ManuscriptArea is not null)ManuscriptArea.Visibility=Visibility.Collapsed;
        if(FolderList is not null)FolderList.IsEnabled=true;
        if(_libraryMode)return;
        FileList.Visibility=_thumbnailMode?Visibility.Collapsed:Visibility.Visible;
        ThumbnailList.Visibility=_thumbnailMode?Visibility.Visible:Visibility.Collapsed;
    }

    private void UpdateManuscriptList()
    {
        if(ManuscriptList is null)return;
        var term=SearchBox?.Text.Trim()??"";
        ManuscriptList.ItemsSource=_paperDocs.Where(x=>
            x.FileName.Contains(term,StringComparison.OrdinalIgnoreCase)).ToArray();
        CurrentLocation.Text=CurrentScopePath();
        UpdateStatus();
    }

    private void ImportPaperDocument_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null)return;
        if(_activePaper is null)
        {
            System.Windows.MessageBox.Show(this,"请先选择或新建论文项目。","论文全文");
            return;
        }
        var dialog=new Microsoft.Win32.OpenFileDialog
        {
            Title="导入论文全文（PDF / Word）",
            Filter="论文全文 (*.pdf;*.doc;*.docx)|*.pdf;*.doc;*.docx|PDF (*.pdf)|*.pdf|Word (*.doc;*.docx)|*.doc;*.docx",
            Multiselect=true
        };
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            foreach(var path in dialog.FileNames)
            {
                var doc=_workspace.ImportPaperDocument(_activePaper.Id,path);
                // DOCX references are review candidates, never confirmed links.
                try{_workspace.SuggestDocumentFigures(doc);}
                catch(System.IO.InvalidDataException){/* damaged DOCX remains importable */}
                catch(System.Xml.XmlException){/* malformed document text is not trusted */}
            }
            LoadProjectTree();
            LoadManuscripts(_activePaper.Id);
            SelectTreeTag(new PaperDocumentsNode(_activePaper.Id));
        }
        catch(Exception ex){ShowError(ex);}
    }

    private void Manuscript_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(ManuscriptList.SelectedItem is PaperDocument doc)
        {
            DetailName.Text=doc.FileName;
            DetailMeta.Text=$"大小：{doc.Size:N0} 字节\n导入：{doc.ImportedAt}\n文档 ID：{doc.Id}\nSHA-256：{doc.Sha256}";
            PreviewImage.Source=null;
            var links=_workspace?.ListPaperFigureLinks(doc.Id)??[];
            var confirmed=links.Count(x=>x.Status=="confirmed");
            DetailMeta.Text+=$"\n论文关联：{links.Count} 个 Figure（已确认 {confirmed}）";
            RelatedFiles.ItemsSource=links.Select(x=>
                $"Figure {x.FigureNumber} · {(x.Version is null?"未指定版本":"v"+x.Version)} · "+
                (x.Status=="confirmed"?"已确认":"待核对")).ToArray();
            UsageList.ItemsSource=(_workspace?.DescribeDocumentProvenance(doc.Id)??"")
                .Split(Environment.NewLine,StringSplitOptions.RemoveEmptyEntries);
        }
        else
        {
            RelatedFiles.ItemsSource=null;
            UsageList.ItemsSource=null;
        }
        UpdateStatus();
    }

    private sealed record DocumentVersionChoice(string Name,string? Id)
    {
        public override string ToString()=>Name;
    }

    private void LinkManuscriptFigures_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null||ManuscriptList.SelectedItem is not PaperDocument doc)return;
        try
        {
            var figures=_workspace.ListFigures(doc.PaperId);
            if(figures.Count==0)
            {
                System.Windows.MessageBox.Show(this,"当前论文项目还没有 Figure。","论文关联");return;
            }
            var old=_workspace.ListPaperFigureLinks(doc.Id).ToDictionary(x=>x.FigureId);
            var window=new Window{Title="论文 Figure 关联 · "+doc.FileName,Owner=this,
                Width=560,Height=540,MinWidth=460,MinHeight=320,
                WindowStartupLocation=WindowStartupLocation.CenterOwner};
            var layout=new DockPanel{Margin=new Thickness(16)};
            var explanation=new TextBlock{
                Text="请选择论文实际使用的 Figure 及完整成图版本。未指定版本的关联只能标记为待核对。",
                TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
            DockPanel.SetDock(explanation,Dock.Top);layout.Children.Add(explanation);
            var footer=new StackPanel{Orientation=Orientation.Horizontal,
                HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
            var save=new Button{Content="保存关联",IsDefault=true,MinWidth=95,Margin=new Thickness(5,0,0,0)};
            var cancel=new Button{Content="取消",IsCancel=true,MinWidth=75,Margin=new Thickness(5,0,0,0)};
            footer.Children.Add(cancel);footer.Children.Add(save);
            DockPanel.SetDock(footer,Dock.Bottom);layout.Children.Add(footer);
            var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            var rows=new StackPanel();
            var inputs=new List<(FigureTemplate Figure,CheckBox Enabled,ComboBox Versions)>();
            foreach(var figure in figures)
            {
                old.TryGetValue(figure.Id,out var existing);
                var line=new DockPanel{Margin=new Thickness(0,4,0,10)};
                var box=new CheckBox{Content="Figure "+figure.Number,
                    IsChecked=existing is not null,Width=145,VerticalAlignment=VerticalAlignment.Center};
                DockPanel.SetDock(box,Dock.Left);line.Children.Add(box);
                var choices=new List<DocumentVersionChoice>{
                    new("尚未指定成图版本（待核对）",null)};
                choices.AddRange(_workspace.ListFigureComposites(figure.Id)
                    .Select(v=>new DocumentVersionChoice(
                        $"v{v.Version} · {v.FileName}"+(v.IsCurrent?"（工作区当前）":""),v.Id)));
                var versions=new ComboBox{ItemsSource=choices,MinWidth=240,
                    SelectedItem=choices.FirstOrDefault(v=>v.Id==existing?.CompositeId)??choices[0]};
                line.Children.Add(versions);rows.Children.Add(line);
                inputs.Add((figure,box,versions));
            }
            scroll.Content=rows;layout.Children.Add(scroll);window.Content=layout;
            save.Click+=(_,_)=>{window.DialogResult=true;window.Close();};
            if(window.ShowDialog()!=true)return;
            foreach(var input in inputs)
            {
                if(input.Enabled.IsChecked==true)
                {
                    var chosen=(DocumentVersionChoice)input.Versions.SelectedItem;
                    _workspace.SetPaperFigureLink(doc.Id,input.Figure.Id,chosen.Id,
                        confirmed:chosen.Id is not null);
                }
                else if(old.ContainsKey(input.Figure.Id))
                    _workspace.RemovePaperFigureLink(doc.Id,input.Figure.Id);
            }
            Manuscript_SelectionChanged(ManuscriptList,
                new SelectionChangedEventArgs(
                    System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,
                    new System.Collections.IList[0],new System.Collections.IList[0]));
        }
        catch(Exception ex){ShowError(ex);}
    }

    private void ViewManuscriptProvenance_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null||ManuscriptList.SelectedItem is not PaperDocument document)return;
        try
        {
            var window=new Window{Title="论文数据溯源 · "+document.FileName,
                Owner=this,Width=860,Height=590,MinWidth=640,MinHeight=400,
                WindowStartupLocation=WindowStartupLocation.CenterOwner};
            var dock=new DockPanel{Margin=new Thickness(12)};
            var explanation=new TextBlock{Text="已确认关系与同组候选严格区分；选择素材可预览仓库原始字节。",
                Margin=new Thickness(4,0,4,10)};
            DockPanel.SetDock(explanation,Dock.Top);dock.Children.Add(explanation);
            var close=new Button{Content="关闭",IsCancel=true,MinWidth=75,
                HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0)};
            close.Click+=(_,_)=>window.Close();
            DockPanel.SetDock(close,Dock.Bottom);dock.Children.Add(close);
            var grid=new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(3,GridUnitType.Star)});
            grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(2,GridUnitType.Star)});
            var tree=new TreeView{Margin=new Thickness(3),BorderThickness=new Thickness(1)};
            Grid.SetColumn(tree,0);grid.Children.Add(tree);
            var details=new StackPanel{Margin=new Thickness(15,4,4,4)};
            var image=new Image{Height=230,Stretch=System.Windows.Media.Stretch.Uniform};
            var text=new TextBlock{Text="选择左侧 Figure、Panel 或素材。",
                TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};
            details.Children.Add(image);details.Children.Add(text);
            Grid.SetColumn(details,1);grid.Children.Add(details);
            TreeViewItem Add(TreeViewItem? parent,string label,object? item=null)
            {
                var node=new TreeViewItem{Header=label,Tag=item,IsExpanded=true};
                if(parent is null)tree.Items.Add(node);
                else parent.Items.Add(node);
                return node;
            }
            foreach(var link in _workspace.ListPaperFigureLinks(document.Id))
            {
                var figure=Add(null,$"Figure {link.FigureNumber} · "+
                    (link.Version is null?"未指定版本":"v"+link.Version)+
                    (link.Status=="confirmed"?" [已确认]":" [待核对]"));
                if(link.CompositeId is null)continue;
                var composite=_workspace.ListFigureComposites(link.FigureId)
                    .FirstOrDefault(x=>x.Id==link.CompositeId);
                var version=Add(figure,composite is null?"成图版本不可用":
                    composite.FileName+" [SHA-256 已归档]",composite);
                foreach(var snapshot in _workspace.ListCompositeSources(link.CompositeId))
                {
                    var panel=Add(version,$"Panel {snapshot.PanelLabel}"+
                        (snapshot.Status=="confirmed"?" [已确认来源]":" [来源候选]"));
                    var show=_workspace.GetProvenanceAsset(snapshot.AssetId);
                    var showNode=Add(panel,show is null?"SHOW [来源记录缺失]":
                        "SHOW · "+show.FileName,show);
                    var processes=_workspace.ListAssetUpstream(snapshot.AssetId);
                    foreach(var process in processes)
                    {
                        var processAsset=_workspace.GetProvenanceAsset(process.SourceAssetId);
                        var processNode=Add(showNode,processAsset is null?"PROCESS [缺失]":
                            "PROCESS · "+processAsset.FileName+
                            (process.Status=="confirmed"?" [已确认]":" [待核对]"),processAsset);
                        foreach(var raw in _workspace.ListAssetUpstream(process.SourceAssetId))
                        {
                            var rawAsset=_workspace.GetProvenanceAsset(raw.SourceAssetId);
                            Add(processNode,rawAsset is null?"RAW [缺失]":
                                "RAW · "+rawAsset.FileName+
                                (raw.Status=="confirmed"?" [已确认]":" [待核对]"),rawAsset);
                        }
                    }
                    if(processes.Count==0)
                        foreach(var candidate in _workspace.ListSameGroupUpstreamCandidates(snapshot.AssetId))
                        {
                            var processNode=Add(showNode,"? PROCESS · "+candidate.Asset.FileName+
                                " [同组候选]",candidate.Asset);
                            foreach(var raw in _workspace.ListSameGroupUpstreamCandidates(candidate.Asset.Id))
                                Add(processNode,"? RAW · "+raw.Asset.FileName+" [同组候选]",raw.Asset);
                        }
                }
            }
            if(tree.Items.Count==0)Add(null,"尚未关联 Figure。请使用“关联 Figure…”设置实际引用。");
            tree.SelectedItemChanged+=(_,_)=>{
                if(tree.SelectedItem is not TreeViewItem selected)return;
                image.Source=null;
                if(selected.Tag is ExperimentAsset asset)
                {
                    text.Text=$"文件：{asset.FileName}\n阶段：{asset.Stage}\n数据组：{asset.GroupPrefix}"+
                        $"\nAsset ID：{asset.Id}\nSHA-256：{asset.Sha256}";
                    image.Source=PreviewFromFile(()=>_workspace.GetBlobPath(asset));
                }
                else if(selected.Tag is FigureComposite composite)
                {
                    text.Text=$"完整成图：{composite.FileName}\n版本：v{composite.Version}"+
                        $"\nSHA-256：{composite.Sha256}";
                    image.Source=PreviewFromFile(()=>Path.Combine(_workspace.Root,"blobs",
                        "sha256",composite.Sha256[..2],composite.Sha256));
                }
                else text.Text=selected.Header?.ToString()??"";
            };
            dock.Children.Add(grid);window.Content=dock;window.ShowDialog();
        }
        catch(Exception ex){ShowError(ex);}
    }

    private void Manuscript_DoubleClick(object sender,MouseButtonEventArgs e)
    {
        if(_workspace is null||e.OriginalSource is not DependencyObject source)return;
        var container=ItemsControl.ContainerFromElement(ManuscriptList,source) as ListViewItem;
        if(container?.Content is not PaperDocument doc)return;
        e.Handled=true;
        try
        {
            var directory=Path.Combine(Path.GetTempPath(),"PaperArchive","Manuscripts",doc.Id);
            Directory.CreateDirectory(directory);
            var target=Path.Combine(directory,doc.FileName);
            File.Copy(_workspace.GetPaperDocumentPath(doc),target,true);
            Process.Start(new ProcessStartInfo(target){UseShellExecute=true});
        }
        catch(Exception ex){ShowError(ex);}
    }

    private bool SelectTreeTag(object target)
    {
        foreach(TreeViewItem root in ProjectTree.Items)
            if(SelectTreeNode(root,target))return true;
        return false;
    }

    private static bool SelectTreeNode(TreeViewItem node,object target)
    {
        if(node.Tag is not null && node.Tag.GetType()==target.GetType())
        {
            bool equal=(node.Tag,target) switch
            {
                (PaperProject x,PaperProject y)=>x.Id==y.Id,
                (ExperimentProject x,ExperimentProject y)=>x.Id==y.Id,
                (ExperimentResult x,ExperimentResult y)=>x.Id==y.Id,
                (PaperDocumentsNode x,PaperDocumentsNode y)=>x.PaperId==y.PaperId,
                (PaperExperimentsNode x,PaperExperimentsNode y)=>x.PaperId==y.PaperId,
                (PaperFiguresNode x,PaperFiguresNode y)=>x.PaperId==y.PaperId,
                (FigureNode x,FigureNode y)=>x.FigureId==y.FigureId,
                (PanelNode x,PanelNode y)=>x.PanelId==y.PanelId,
                (FigureCompositesNode x,FigureCompositesNode y)=>x.FigureId==y.FigureId,
                _=>false
            };
            if(equal){node.IsSelected=true;node.BringIntoView();return true;}
        }
        foreach(TreeViewItem child in node.Items)
            if(SelectTreeNode(child,target)){node.IsExpanded=true;return true;}
        return false;
    }

    private void UndoMove_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null)return;
        try
        {
            if(!_workspace.UndoLastMove())
            {
                System.Windows.MessageBox.Show(this,"当前没有可撤销的移动记录。","撤销移动");
                return;
            }
            LoadProjectTree();
            if(!_showManuscripts)LoadScopeAssets();
            UpdateStatus();
        }
        catch(Exception ex){ShowError(ex);}
    }
}
