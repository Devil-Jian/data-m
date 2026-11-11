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
        if(_libraryMode||_workspace is null||_activePaper is null)return;
        var paper=_activePaper;
        try
        {
            var manuscript=_workspace.GetPrimaryPaperDocument(paper.Id);
            if(manuscript is null)
            {
                if(_workspace.ListPaperDocuments(paper.Id).Count==0)
                {
                    SelectTreeTag(paper);
                    System.Windows.MessageBox.Show(this,
                        "尚未导入论文全文。请先完成首页的“导入全文”任务。","归档准备");
                    return;
                }
                // Multiple drafts require a single persisted choice, not a
                // document-selection dialog on every archive operation.
                if(!ChoosePrimaryManuscript(paper.Id))return;
                manuscript=_workspace.GetPrimaryPaperDocument(paper.Id);
                if(manuscript is null)return;
            }
            _archive??=ArchiveRepository.Open(_workspace.Root);
            var preview=_archive.PrepareWorkspaceArchive(_workspace,paper.Id,manuscript.Id);
            var blocked=preview.Checks.Where(x=>x.Level=="block").ToArray();
            if(blocked.Length>0)
            {
                SelectTreeTag(paper);
                var issues=string.Join("\n",blocked.Take(4).Select(x=>"• "+x.Message));
                System.Windows.MessageBox.Show(this,
                    $"发现 {blocked.Length} 项必须处理的问题：\n\n{issues}"+
                    (blocked.Length>4?"\n…":"")+
                    "\n\n点击论文项目首页的任务操作即可定位处理。",
                    "暂时不能归档",MessageBoxButton.OK,MessageBoxImage.Warning);
                return;
            }
            var previous=_archive.GetLatestWorkspaceArchive(paper.Id);
            if(previous is not null&&previous.Value.Revision.ManifestSha256==preview.ManifestSha256)
            {
                OpenArchivedPaper(previous.Value.ArchivePaperId,
                    $"论文内容没有变化，仍为 Revision {previous.Value.Revision.Revision}。");
                return;
            }
            var nextRevision=(previous?.Revision.Revision??0)+1;
            var reviews=preview.Checks.Where(x=>x.Level=="review").ToArray();
            bool confirmed;
            if(reviews.Length==0)
            {
                confirmed=System.Windows.MessageBox.Show(this,
                    $"论文：{paper.Name}\n主文稿：{manuscript.FileName}\n"+
                    $"Figure：{preview.FigureCount} 张\n归档资源：{preview.Resources.Count} 条\n"+
                    $"本次创建：Revision {nextRevision}\n\n"+
                    "归档后工作台仍可编辑，历史版本不会被修改。",
                    "确认归档",MessageBoxButton.YesNo,
                    MessageBoxImage.Question)==MessageBoxResult.Yes;
            }
            else
            {
                var dialog=new Window{Title="归档确认 · 待核对来源",Owner=this,
                    Width=630,Height=440,MinWidth=490,MinHeight=350,
                    WindowStartupLocation=WindowStartupLocation.CenterOwner};
                var root=new DockPanel{Margin=new Thickness(17)};
                var header=new TextBlock{
                    Text=$"{paper.Name} · Revision {nextRevision}\n"+
                        $"论文及 Figure 版本已通过检查，仍有 {reviews.Length} 项来源待核对。"+
                        "这些关系不会被当作已确认的科研加工事实。",
                    TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
                DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
                var bottom=new StackPanel{Margin=new Thickness(0,10,0,0)};
                var acknowledgement=new CheckBox{
                    Content="允许将以下未核实来源明确标记为待核对并归档",
                    Margin=new Thickness(0,0,0,12)};
                bottom.Children.Add(acknowledgement);
                var buttons=new StackPanel{Orientation=Orientation.Horizontal,
                    HorizontalAlignment=HorizontalAlignment.Right};
                var publish=new Button{Content="确认归档",IsEnabled=false,MinWidth=95,IsDefault=true};
                var cancel=new Button{Content="取消",IsCancel=true,MinWidth=75,
                    Margin=new Thickness(8,0,0,0)};
                acknowledgement.Checked+=(_,_)=>publish.IsEnabled=true;
                acknowledgement.Unchecked+=(_,_)=>publish.IsEnabled=false;
                buttons.Children.Add(publish);buttons.Children.Add(cancel);
                bottom.Children.Add(buttons);
                DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
                root.Children.Add(new ListBox{ItemsSource=reviews.Select(x=>x.Message).ToArray()});
                publish.Click+=(_,_)=>{dialog.DialogResult=true;dialog.Close();};
                dialog.Content=root;
                confirmed=dialog.ShowDialog()==true;
            }
            if(!confirmed)return;
            // Revalidate in case files or relationships changed while the
            // confirmation was shown. SHA-256 checking also runs at commit.
            var current=_archive.PrepareWorkspaceArchive(_workspace,paper.Id,manuscript.Id);
            if(current.ManifestSha256!=preview.ManifestSha256||!current.CanPublish)
                throw new InvalidOperationException("归档确认期间论文或科研数据已变化，请重新检查。");
            var result=_archive.PublishWorkspaceArchive(current,
                acknowledgeReviews:reviews.Length>0);
            OpenArchivedPaper(result.PaperId,
                result.AlreadyArchived?
                    $"内容未变化，仍为 Revision {result.Revision}。":
                    $"已归档 Revision {result.Revision} · {result.FileCount} 条资源，工作台原件保留。");
        }
        catch(Exception ex){ShowError(ex);}
    }

    private void OpenArchivedPaper(string archivePaperId,string message)
    {
        LibraryTab.IsChecked=true;
        foreach(TreeViewItem item in ProjectTree.Items)
            if(item.Tag is PaperEntry entry&&entry.Id==archivePaperId)
            {
                item.IsSelected=true;
                item.BringIntoView();
                break;
            }
        StatusText.Text=message;
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
