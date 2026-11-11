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
