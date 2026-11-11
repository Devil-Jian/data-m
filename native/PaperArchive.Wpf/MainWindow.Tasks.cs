using System.IO;
using System.Windows;
using System.Windows.Controls;
using PaperArchive.Core;
using PaperArchive.Core.Workspace;

namespace PaperArchive.Wpf;

public partial class MainWindow
{
    private string? _paperHomeId;

    private void HidePaperTaskHome()
    {
        _paperHomeId=null;
        if(PaperHomeArea is not null)PaperHomeArea.Visibility=Visibility.Collapsed;
        if(PaperHomeToolbarHint is not null)PaperHomeToolbarHint.Visibility=Visibility.Collapsed;
        if(!_libraryMode)
        {
            FileCommands.Visibility=Visibility.Visible;
            ConfirmUsageButton.Visibility=Visibility.Visible;
            SearchBox.Visibility=Visibility.Visible;
        }
    }

    private void ShowPaperTaskHome(string paperId)
    {
        if(_workspace is null||_libraryMode)return;
        _activePaper=_workspace.ListPaperProjects().FirstOrDefault(x=>x.Id==paperId);
        if(_activePaper is null)return;
        _activeProject=null;_activeResult=null;
        CloseFigures();
        CloseManuscripts();
        _paperHomeId=paperId;
        FigureArea.Visibility=Visibility.Collapsed;
        ManuscriptArea.Visibility=Visibility.Collapsed;
        FileList.Visibility=Visibility.Collapsed;
        ThumbnailList.Visibility=Visibility.Collapsed;
        PaperHomeArea.Visibility=Visibility.Visible;
        PaperHomeToolbarHint.Visibility=Visibility.Visible;
        FileCommands.Visibility=Visibility.Collapsed;
        ConfirmUsageButton.Visibility=Visibility.Collapsed;
        SearchBox.Visibility=Visibility.Collapsed;
        FolderList.IsEnabled=false;
        RefreshPaperTaskHome();
    }

    private void RefreshPaperTaskHome()
    {
        if(_workspace is null||_paperHomeId is null||_libraryMode)return;
        try
        {
            var summary=_workspace.GetPaperWorkSummary(_paperHomeId);
            PaperHomeTitle.Text=summary.Title;
            PaperHomeProgress.Text=
                $"论文全文      {summary.DocumentCount} 篇\n"+
                $"完整 Figure   {summary.FinishedFigureCount} / {summary.FigureCount}\n"+
                $"论文关联      {summary.ConfirmedFigureCount} / {summary.LinkedFigureCount} 已确认\n"+
                $"实验素材      {summary.AssetCount} 个";
            PaperHomePrimary.Text=summary.PrimaryDocument is null?
                "主文稿：尚未确定（有多份文稿时只需选择一次）":
                "主文稿："+summary.PrimaryDocument.FileName;
            var archive=(_archive??=ArchiveRepository.Open(_workspace.Root))
                .GetLatestWorkspaceArchive(_paperHomeId);
            var archiveLabel=archive is null?"尚未归档":
                $"已有归档 Revision {archive.Value.Revision.Revision}（可检查更新）";
            PaperHomeStatus.Text=summary.BlockingCount>0?
                $"待处理 · {summary.BlockingCount} 项必须处理，{summary.ReviewCount} 项建议核对 · {archiveLabel}":
                summary.ReviewCount>0?
                    $"可以继续 · {summary.ReviewCount} 项来源待核对 · {archiveLabel}":
                    $"关联就绪 · 归档时自动校验文件 · {archiveLabel}";
            PaperHomeTasks.ItemsSource=summary.Tasks
                .OrderBy(x=>x.BlocksArchive?0:1).ToArray();
            PaperHomeEmpty.Visibility=summary.Tasks.Count==0?
                Visibility.Visible:Visibility.Collapsed;
            PaperHomeArchiveAction.Content=archive is null?"归档论文…":"检查并更新归档…";
            PaperHomeArchiveAction.IsEnabled=summary.PrimaryDocument is not null &&
                summary.BlockingCount==0;
            CurrentLocation.Text="归档前工作台  >  "+summary.Title+"  >  任务概览";
            DetailName.Text=summary.Title;
            DetailMeta.Text=
                $"主文稿：{summary.PrimaryDocument?.FileName??"未指定"}\n"+
                $"Figure：{summary.FigureCount}（成图 {summary.FinishedFigureCount}）\n"+
                $"论文关联：{summary.ConfirmedFigureCount} 已确认\n"+
                $"实验素材：{summary.AssetCount}\n"+
                $"必须处理：{summary.BlockingCount}，建议核对：{summary.ReviewCount}\n"+
                archiveLabel;
            PreviewImage.Source=null;
            RelatedHeading.Text="需要处理";
            UsageHeading.Text="归档进度";
            RelatedFiles.ItemsSource=summary.Tasks.Where(x=>x.BlocksArchive)
                .Select(x=>x.Title).ToArray();
            UsageList.ItemsSource=new[]{archiveLabel};
            StatusText.Text=PaperHomeStatus.Text;
        }
        catch(Exception ex){ShowError(ex);}
    }

    private void PaperHomeRefresh_Click(object sender,RoutedEventArgs e)=>
        RefreshPaperTaskHome();

    private void PaperHomeDocuments_Click(object sender,RoutedEventArgs e)
    {
        if(_paperHomeId is string paperId)
            SelectTreeTag(new PaperDocumentsNode(paperId));
    }

    private void PaperHomeFigures_Click(object sender,RoutedEventArgs e)
    {
        if(_paperHomeId is string paperId)
            SelectTreeTag(new PaperFiguresNode(paperId));
    }

    private void PaperHomeImportDocument_Click(object sender,RoutedEventArgs e)
    {
        if(_activePaper is null)return;
        ImportPaperDocument_Click(sender,e);
    }

    // One explicit primary choice persists; importing or archiving does not
    // prompt again unless the user deliberately changes the selected manuscript.
    private bool ChoosePrimaryManuscript(string paperId)
    {
        if(_workspace is null)return false;
        var documents=_workspace.ListPaperDocuments(paperId);
        if(documents.Count==0)return false;
        if(documents.Count==1)
        {
            _workspace.SetPrimaryPaperDocument(paperId,documents[0].Id);
            RefreshPaperTaskHome();
            return true;
        }
        var existing=_workspace.GetPrimaryPaperDocument(paperId);
        var dialog=new Window{Title="选择主文稿",Owner=this,Width=530,Height=220,
            ResizeMode=ResizeMode.NoResize,
            WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var layout=new StackPanel{Margin=new Thickness(17)};
        layout.Children.Add(new TextBlock{
            Text="选择归档所使用的论文全文。以后归档会记住这一选择。",
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        var select=new ComboBox{ItemsSource=documents,
            DisplayMemberPath="FileName",
            SelectedItem=documents.FirstOrDefault(x=>x.Id==existing?.Id)??documents[0]};
        layout.Children.Add(select);
        var save=new Button{Content="使用此文稿",MinWidth=105,IsDefault=true,
            HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0)};
        save.Click+=(_,_)=>{dialog.DialogResult=true;dialog.Close();};
        layout.Children.Add(save);
        dialog.Content=layout;
        if(dialog.ShowDialog()!=true||select.SelectedItem is not PaperDocument selected)
            return false;
        _workspace.SetPrimaryPaperDocument(paperId,selected.Id);
        RefreshPaperTaskHome();
        return true;
    }

    private void PaperHomeTask_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not Button{Tag:PaperWorkTask task}||_workspace is null||
            _paperHomeId is not string paperId)return;
        try
        {
            switch(task.Code)
            {
                case "import_manuscript":
                    ImportPaperDocument_Click(sender,e);
                    break;
                case "select_primary":
                    ChoosePrimaryManuscript(paperId);
                    break;
                case "link_figures":
                    if(_workspace.ListFigures(paperId).Count==0)
                    {
                        SelectTreeTag(new PaperFiguresNode(paperId));
                        QuickFigure_Click(sender,e);
                    }
                    else NavigatePaperFigureLinks(paperId,task.DocumentId);
                    break;
                case "figure_version":
                    if(task.FigureId is string figureId &&
                        _workspace.ListFigureComposites(figureId).Count==0)
                        SelectTreeTag(new FigureCompositesNode(paperId,figureId));
                    else NavigatePaperFigureLinks(paperId,task.DocumentId);
                    break;
                case "review_sources":
                    if(task.FigureId is string reviewFigure)
                        SelectTreeTag(new FigureCompositesNode(paperId,reviewFigure));
                    break;
                case "import_experiment":
                    PaperHomeImportExperiment_Click(sender,e);
                    break;
            }
        }
        catch(Exception ex){ShowError(ex);}
    }

    private void NavigatePaperFigureLinks(string paperId,string? documentId)
    {
        SelectTreeTag(new PaperDocumentsNode(paperId));
        if(documentId is not null)
            ManuscriptList.SelectedItem=ManuscriptList.Items.OfType<PaperDocument>()
                .FirstOrDefault(x=>x.Id==documentId);
        LinkManuscriptFigures_Click(this,new RoutedEventArgs());
    }

    // The folder name becomes the experiment project name. No extra "create
    // project" dialog is needed for a first import; original bytes stay intact.
    private async void PaperHomeImportExperiment_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null||_activePaper is null)return;
        var folder=SelectFolder("选择实验数据文件夹");
        if(folder is null)return;
        var paperId=_activePaper.Id;
        try
        {
            PaperHomeStatus.Text="正在导入实验数据并建立索引…";
            var name=Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
            var existing=_workspace.ListProjectsForPaper(paperId)
                .FirstOrDefault(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase));
            var projectId=existing?.Id??_workspace.CreateProjectForPaper(paperId,name);
            var imported=await Task.Run(()=>_workspace.ImportUnsorted(projectId,folder));
            _activeProject=_workspace.ListProjectsForPaper(paperId)
                .FirstOrDefault(x=>x.Id==projectId);
            _activeResult=_workspace.ListResults(projectId)
                .FirstOrDefault(x=>x.Id==imported.ResultId);
            LoadProjectTree();
            if(_activeResult is not null)SelectTreeTag(_activeResult);
        }
        catch(Exception ex){ShowError(ex);RefreshPaperTaskHome();}
    }
}
