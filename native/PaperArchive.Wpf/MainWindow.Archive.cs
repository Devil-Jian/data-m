using System.Text.Json;
using System.IO;
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
    private sealed record ArchivePreviewTarget(string FileName,string Role,string Sha256);

    private void ViewArchivedProvenance_Click(object sender,RoutedEventArgs e)
    {
        if(_archive is null || ProjectTree.SelectedItem is not TreeViewItem{Tag:PaperEntry paper})
            return;
        try
        {
            var revisions=_archive.ListWorkspaceRevisions(paper.Id);
            if(revisions.Count==0)
            {
                System.Windows.MessageBox.Show(this,
                    "此论文是旧版目录归档，尚没有工作台数据关系快照。","归档溯源");return;
            }
            var window=new Window{Title="归档溯源 · "+paper.Title,Owner=this,
                Width=940,Height=650,MinWidth=670,MinHeight=430,
                WindowStartupLocation=WindowStartupLocation.CenterOwner};
            var dock=new DockPanel{Margin=new Thickness(12)};
            var top=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,10)};
            top.Children.Add(new TextBlock{Text="归档版本：",
                VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)});
            var chooser=new ComboBox{MinWidth=200,ItemsSource=revisions,
                DisplayMemberPath="Revision",SelectedIndex=0};
            top.Children.Add(chooser);
            var note=new TextBlock{Text="  历史快照只读，不跟随工作台变化。",
                VerticalAlignment=VerticalAlignment.Center};
            top.Children.Add(note);
            DockPanel.SetDock(top,Dock.Top);dock.Children.Add(top);
            var grid=new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(3,GridUnitType.Star)});
            grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(2,GridUnitType.Star)});
            var tree=new TreeView{Margin=new Thickness(0,0,10,0)};
            Grid.SetColumn(tree,0);grid.Children.Add(tree);
            var detail=new StackPanel{Margin=new Thickness(10,4,0,0)};
            var preview=new Image{Height=250,Stretch=System.Windows.Media.Stretch.Uniform};
            var label=new TextBlock{Text="选择 Figure 或素材查看归档详情。",
                TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,0)};
            detail.Children.Add(preview);detail.Children.Add(label);
            Grid.SetColumn(detail,1);grid.Children.Add(detail);
            dock.Children.Add(grid);window.Content=dock;

            static string Property(JsonElement item,string key)=>
                item.TryGetProperty(key,out var value)?value.ToString():"";
            static IEnumerable<JsonElement> Children(JsonElement item,string key)=>
                item.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.Array?
                    value.EnumerateArray().ToArray():[];
            TreeViewItem Add(TreeViewItem? parent,string title,ArchivePreviewTarget? target=null)
            {
                var child=new TreeViewItem{Header=title,Tag=target,IsExpanded=true};
                if(parent is null)tree.Items.Add(child);
                else parent.Items.Add(child);
                return child;
            }
            void AddAsset(TreeViewItem parent,JsonElement item,string prefix,string role,
                string shaField="Sha256")
            {
                var file=Property(item,"FileName");
                var sha=Property(item,shaField);
                var asset=Add(parent,prefix+" · "+file,new(file,role,sha));
                foreach(var process in Children(item,"process"))
                {
                    AddAsset(asset,process,"PROCESS","process");
                }
                foreach(var raw in Children(item,"raw"))
                {
                    AddAsset(asset,raw,"RAW","raw");
                }
            }
            void Refresh()
            {
                if(chooser.SelectedItem is not WorkspaceArchiveRevision revision)return;
                var manifest=_archive.GetWorkspaceArchiveManifest(paper.Id,revision.Revision);
                tree.Items.Clear();label.Text="请选择归档资源查看文件哈希与预览。";
                preview.Source=null;
                if(manifest is null)return;
                using var json=JsonDocument.Parse(manifest);
                var root=json.RootElement;
                var manuscript=root.GetProperty("document");
                var docName=Property(manuscript,"FileName");
                Add(null,"论文全文 · "+docName,
                    new(docName,"manuscript",Property(manuscript,"Sha256")));
                foreach(var fig in Children(root,"figures"))
                {
                    var number=Property(fig,"FigureNumber");
                    var version=Property(fig,"Version");
                    var file=Property(fig,"FileName");
                    var figureNode=Add(null,"Figure "+number+" · v"+version+" · "+file,
                        new(file,"composite",Property(fig,"Sha256")));
                    foreach(var source in Children(fig,"sources"))
                    {
                        var panel=Add(figureNode,"Panel "+Property(source,"PanelLabel")+
                            " ["+Property(source,"Status")+"]");
                        AddAsset(panel,source,"SHOW","show","AssetSha256");
                    }
                }
                label.Text="归档 Revision "+revision.Revision+
                    "\n归档时间："+revision.ArchivedAt+
                    "\nManifest SHA-256："+revision.ManifestSha256;
            }
            chooser.SelectionChanged+=(_,_)=>Refresh();
            tree.SelectedItemChanged+=(_,_)=>{
                if(tree.SelectedItem is not TreeViewItem selected)return;
                preview.Source=null;
                if(selected.Tag is ArchivePreviewTarget item)
                {
                    label.Text="文件："+item.FileName+"\n角色："+item.Role+
                        "\nSHA-256："+item.Sha256;
                    if(item.Sha256.Length==64&&item.Sha256.All(Uri.IsHexDigit))
                        preview.Source=PreviewFromFile(()=>_archive.ResolveOriginal(item.Sha256));
                }
                else label.Text=selected.Header?.ToString()??"";
            };
            Refresh();window.ShowDialog();
        }
        catch(Exception ex){ShowError(ex);}
    }

}
