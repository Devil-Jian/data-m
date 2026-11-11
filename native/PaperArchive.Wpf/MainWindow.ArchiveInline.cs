using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using PaperArchive.Core;

namespace PaperArchive.Wpf;

public partial class MainWindow
{
    private sealed record ArchiveRevisionChoice(int Revision, string Display, string ArchivedAt);
    private bool _loadingLibraryRevision;
    private PaperEntry? _selectedArchivePaper;

    private void LoadLibraryRevisions(PaperEntry paper)
    {
        if(_archive is null)return;
        _selectedArchivePaper=paper;
        _loadingLibraryRevision=true;
        try
        {
            var revisions=_archive.ListWorkspaceRevisions(paper.Id);
            LibraryRevisionPicker.ItemsSource=revisions
                .Select(x=>new ArchiveRevisionChoice(x.Revision,
                    $"Revision {x.Revision} · {x.ArchivedAt}",x.ArchivedAt)).ToArray();
            LibraryRevisionPicker.IsEnabled=revisions.Count>0;
            LibraryRevisionPicker.Visibility=revisions.Count>0?
                Visibility.Visible:Visibility.Collapsed;
            LibrarySourceTree.Items.Clear();
            if(revisions.Count==0)
            {
                // Classic folder archives have no immutable workspace manifest.
                // Show their existing resources without asserting invented lineage.
                LibraryFiles.ItemsSource=_archive.ListResources(paper.Id);
                LibraryRevisionSummary.Text="旧版目录归档 · 无 Revision 历史来源快照";
            }
            else LibraryRevisionPicker.SelectedIndex=0;
        }
        finally{_loadingLibraryRevision=false;}
        if(LibraryRevisionPicker.SelectedItem is ArchiveRevisionChoice selected)
            DisplayLibraryRevision(selected.Revision);
    }

    private void LibraryRevisionPicker_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(_loadingLibraryRevision||_archive is null||_selectedArchivePaper is null)return;
        if(LibraryRevisionPicker.SelectedItem is ArchiveRevisionChoice revision)
            DisplayLibraryRevision(revision.Revision);
    }

    private void LibraryShowProvenance_Click(object sender,RoutedEventArgs e)
    {
        // No modal dialog: expand the current revision's source hierarchy inline.
        if(LibrarySourceTree.Items.Count==0)
        {
            if(_selectedArchivePaper is not null)
                System.Windows.MessageBox.Show(this,
                    "此归档没有工作台历史来源快照，可在右侧浏览原始归档资源。",
                    "归档来源",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }
        foreach(TreeViewItem item in LibrarySourceTree.Items)item.IsExpanded=true;
        if(LibrarySourceTree.Items[0] is TreeViewItem first)
        {
            first.IsSelected=true;
            first.BringIntoView();
        }
    }

    private static string ManifestText(JsonElement item,string key)
    {
        return item.ValueKind==JsonValueKind.Object &&
            item.TryGetProperty(key,out var value)?value.ToString():"";
    }
    private static IEnumerable<JsonElement> ManifestChildren(JsonElement item,string key)
    {
        return item.ValueKind==JsonValueKind.Object &&
            item.TryGetProperty(key,out var value) && value.ValueKind==JsonValueKind.Array?
                value.EnumerateArray().ToArray():[];
    }

    private void DisplayLibraryRevision(int revision)
    {
        if(_archive is null||_selectedArchivePaper is null)return;
        var paper=_selectedArchivePaper;
        try
        {
            // GetWorkspaceArchiveManifest verifies the original stored SHA-256.
            var manifest=_archive.GetWorkspaceArchiveManifest(paper.Id,revision);
            LibrarySourceTree.Items.Clear();
            LibraryFiles.ItemsSource=_archive.ListWorkspaceArchiveResources(paper.Id,revision);
            PreviewImage.Source=null;
            RelatedFiles.ItemsSource=null;
            UsageList.ItemsSource=null;
            DetailName.Text=paper.Title+" · Revision "+revision;
            var selected=LibraryRevisionPicker.SelectedItem as ArchiveRevisionChoice;
            LibraryRevisionSummary.Text=$"Revision {revision} · {selected?.ArchivedAt??""}"+
                " · 归档快照只读，不跟随工作台当前文件或 Panel 变化";
            if(manifest is null)return;
            using var json=JsonDocument.Parse(manifest);
            var root=json.RootElement;
            if(root.ValueKind!=JsonValueKind.Object)
                throw new InvalidDataException("归档 Manifest 格式无效。");
            TreeViewItem Add(TreeViewItem? parent,string title,ArchivePreviewTarget? target=null)
            {
                var item=new TreeViewItem{Header=title,Tag=target,IsExpanded=true};
                if(parent is null)LibrarySourceTree.Items.Add(item);
                else parent.Items.Add(item);
                return item;
            }
            void AddAsset(TreeViewItem parent,JsonElement asset,string stage,
                string role,string hashKey="Sha256")
            {
                var name=ManifestText(asset,"FileName");
                var sha=ManifestText(asset,hashKey);
                var status=ManifestText(asset,"Status");
                var node=Add(parent,stage+" · "+name+
                    (status==""?"":" ["+status+"]"),new(name,role,sha));
                foreach(var process in ManifestChildren(asset,"process"))
                    AddAsset(node,process,"PROCESS","process");
                foreach(var raw in ManifestChildren(asset,"raw"))
                    AddAsset(node,raw,"RAW","raw");
            }
            if(root.TryGetProperty("document",out var document))
            {
                var name=ManifestText(document,"FileName");
                Add(null,"论文全文 · "+name,
                    new(name,"manuscript",ManifestText(document,"Sha256")));
            }
            foreach(var figure in ManifestChildren(root,"figures"))
            {
                var name=ManifestText(figure,"FileName");
                var version=ManifestText(figure,"Version");
                var number=ManifestText(figure,"FigureNumber");
                var node=Add(null,$"Figure {number} · v{version} · {name}",
                    new(name,"composite",ManifestText(figure,"Sha256")));
                foreach(var source in ManifestChildren(figure,"sources"))
                {
                    var label=ManifestText(source,"PanelLabel");
                    var status=ManifestText(source,"Status");
                    var panel=Add(node,$"Panel {label} · "+
                        (status=="confirmed"?"来源已确认":"来源待核对"));
                    AddAsset(panel,source,"SHOW","show","AssetSha256");
                }
            }
            var manifestHash=_archive.ListWorkspaceRevisions(paper.Id)
                .First(x=>x.Revision==revision).ManifestSha256;
            DetailMeta.Text="归档资源与来源关系以当前 Revision 的 Manifest 为准。"+
                "\nManifest SHA-256："+manifestHash;
        }
        catch(Exception ex)
        {
            LibrarySourceTree.Items.Clear();
            LibraryFiles.ItemsSource=null;
            LibraryRevisionSummary.Text="归档历史快照校验失败，已停止显示。";
            ShowError(ex);
        }
        UpdateStatus();
    }

    private void LibrarySourceTree_SelectedItemChanged(object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if(_archive is null||LibrarySourceTree.SelectedItem is not TreeViewItem node)return;
        if(node.Tag is not ArchivePreviewTarget item)
        {
            DetailName.Text=node.Header?.ToString()??"归档来源";
            DetailMeta.Text="历史来源节点 · 只读";
            PreviewImage.Source=null;
            return;
        }
        DetailName.Text=item.FileName;
        DetailMeta.Text=$"归档角色：{item.Role}\n来源版本："+
            (LibraryRevisionPicker.SelectedItem is ArchiveRevisionChoice selected?
                "Revision "+selected.Revision:"旧版归档")+
            $"\nSHA-256：{item.Sha256}\n此关系来自归档时保存的 Manifest。";
        PreviewImage.Source=item.Sha256.Length==64&&item.Sha256.All(Uri.IsHexDigit)?
            PreviewFromFile(()=>_archive.ResolveOriginal(item.Sha256)):null;
        var resource=LibraryFiles.Items.OfType<ResourceEntry>().FirstOrDefault(x=>
            x.Role==item.Role && x.Sha256.Equals(item.Sha256,StringComparison.OrdinalIgnoreCase));
        if(resource is not null)
        {
            LibraryFiles.SelectedItem=resource;
            LibraryFiles.ScrollIntoView(resource);
        }
    }
}
