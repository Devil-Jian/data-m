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
                _workspace.ImportPaperDocument(_activePaper.Id,path);
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
        }
        UpdateStatus();
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
