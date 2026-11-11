using System.Windows;
using System.Windows.Controls;
using PaperArchive.Core;
using PaperArchive.Core.Workspace;

namespace PaperArchive.Wpf;

public partial class MainWindow
{
    private sealed record ArchiveDocumentChoice(PaperDocument Document)
    {
        public override string ToString()=>Document.FileName;
    }

    private void ArchivePaper_Click(object sender,RoutedEventArgs e)
    {
        if(_libraryMode||_workspace is null)return;
        var paper=_activePaper;
        if(paper is null)
        {
            System.Windows.MessageBox.Show(this,"请先在左侧项目树选择要归档的论文项目。","论文归档");
            return;
        }
        var documents=_workspace.ListPaperDocuments(paper.Id);
        if(documents.Count==0)
        {
            System.Windows.MessageBox.Show(this,"论文项目还没有导入论文全文，请先导入并确认 Figure 关联。","论文归档");
            return;
        }
        try
        {
            var choices=documents.Select(x=>new ArchiveDocumentChoice(x)).ToArray();
            var selected=ManuscriptList.SelectedItem is PaperDocument doc &&
                doc.PaperId==paper.Id?doc:documents[0];
            var select=new Window{Title="选择归档论文全文",Owner=this,Width=510,
                Height=230,ResizeMode=ResizeMode.NoResize,
                WindowStartupLocation=WindowStartupLocation.CenterOwner};
            var root=new StackPanel{Margin=new Thickness(16)};
            root.Children.Add(new TextBlock{Text="请选择本次归档使用的论文全文版本：",
                Margin=new Thickness(0,0,0,10)});
            var combo=new ComboBox{ItemsSource=choices,MinWidth=370,
                SelectedItem=choices.First(x=>x.Document.Id==selected.Id)};
            root.Children.Add(combo);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,
                HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};
            var ok=new Button{Content="开始检查",IsDefault=true,MinWidth=90};
            var cancel=new Button{Content="取消",IsCancel=true,MinWidth=75,
                Margin=new Thickness(8,0,0,0)};
            buttons.Children.Add(ok);buttons.Children.Add(cancel);root.Children.Add(buttons);
            select.Content=root;
            ok.Click+=(_,_)=>{select.DialogResult=true;select.Close();};
            if(select.ShowDialog()!=true||combo.SelectedItem is not ArchiveDocumentChoice chosen)return;
            _archive ??=ArchiveRepository.Open(_workspace.Root);
            var preview=_archive.PrepareWorkspaceArchive(_workspace,paper.Id,chosen.Document.Id);
            var blocking=preview.Checks.Count(x=>x.Level=="block");
            var reviews=preview.Checks.Count(x=>x.Level=="review");
            var report=new Window{Title="归档检查 · "+paper.Name,Owner=this,
                Width=650,Height=560,MinWidth=510,MinHeight=400,
                WindowStartupLocation=WindowStartupLocation.CenterOwner};
            var dock=new DockPanel{Margin=new Thickness(16)};
            var heading=new TextBlock{Text=$"论文：{paper.Name}\n全文：{chosen.Document.FileName}"+
                $"\n已绑定 Figure：{preview.FigureCount}\n归档资源记录：{preview.Resources.Count}"+
                $"\n阻断问题：{blocking}    待核对项：{reviews}",
                TextWrapping=TextWrapping.Wrap,LineHeight=24,
                Margin=new Thickness(0,0,0,12)};
            DockPanel.SetDock(heading,Dock.Top);dock.Children.Add(heading);
            var footer=new StackPanel{Orientation=Orientation.Horizontal,
                HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
            var publish=new Button{Content="确认归档",IsDefault=true,MinWidth=95,
                IsEnabled=blocking==0};
            var abort=new Button{Content="取消",MinWidth=75,IsCancel=true,
                Margin=new Thickness(8,0,0,0)};
            footer.Children.Add(publish);footer.Children.Add(abort);
            DockPanel.SetDock(footer,Dock.Bottom);dock.Children.Add(footer);
            var reviewAcknowledgement=new CheckBox{
                Content="我已检查待核对项，允许带明确标记的未完善来源归档",
                Visibility=reviews>0?Visibility.Visible:Visibility.Collapsed,
                IsChecked=reviews==0,Margin=new Thickness(0,5,0,8)};
            reviewAcknowledgement.Checked+=(_,_)=>publish.IsEnabled=blocking==0;
            reviewAcknowledgement.Unchecked+=(_,_)=>publish.IsEnabled=false;
            if(reviews>0)publish.IsEnabled=false;
            DockPanel.SetDock(reviewAcknowledgement,Dock.Bottom);
            dock.Children.Add(reviewAcknowledgement);
            var lines=preview.Checks.Count==0?
                new[]{"归档检查通过。工作台的图片和关联不会被移动或删除。"}:
                preview.Checks.Select(x=>$"[{(x.Level=="block"?"阻断":"待核对")}] {x.Message}").ToArray();
            var list=new ListBox{ItemsSource=lines};
            dock.Children.Add(list);report.Content=dock;
            publish.Click+=(_,_)=>{report.DialogResult=true;report.Close();};
            if(report.ShowDialog()!=true)return;
            // Guard against workspace edits while the confirmation window was open.
            var current=_archive.PrepareWorkspaceArchive(_workspace,paper.Id,chosen.Document.Id);
            if(current.ManifestSha256!=preview.ManifestSha256||
                current.Checks.Any(x=>x.Level=="block"))
                throw new InvalidOperationException("归档检查后论文关联或资源已变化，请重新检查。");
            var result=_archive.PublishWorkspaceArchive(current,
                acknowledgeReviews:reviews==0||reviewAcknowledgement.IsChecked==true);
            System.Windows.MessageBox.Show(this,
                result.AlreadyArchived?
                    $"相同内容已归档为 Revision {result.Revision}，未生成重复记录。":
                    $"论文已归档为 Revision {result.Revision}，共 {result.FileCount} 条资源记录。"+
                    "\n工作台数据和原始图片均已保留。",
                "论文归档",MessageBoxButton.OK,MessageBoxImage.Information);
            LibraryTab.IsChecked=true;
            // MainTab_Changed loads the library; select the newly archived paper.
            foreach(TreeViewItem item in ProjectTree.Items)
                if(item.Tag is PaperEntry entry&&entry.Id==result.PaperId)
                {
                    item.IsSelected=true;
                    item.BringIntoView();
                    break;
                }
        }
        catch(Exception ex){ShowError(ex);}
    }
}
