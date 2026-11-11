using System.Windows;
using PaperArchive.Core.Workspace;

namespace PaperArchive.Wpf;

public partial class MainWindow
{
    /// <summary>
    /// Navigate to an actual snapshot or current Panel reference; never turn a
    /// same-sample candidate into a confirmed manuscript usage.
    /// </summary>
    private void LocateAssetUsage_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null || SelectedAsset() is not { } selected)return;
        try
        {
            var refs=_workspace.ListAssetPaperReferences(selected.Asset.Id);
            var target=refs
                .OrderByDescending(x=>x.ReferenceKind=="snapshot" &&
                    x.DocumentLinkStatus=="confirmed")
                .ThenByDescending(x=>x.ReferenceKind=="snapshot")
                .ThenByDescending(x=>x.CompositeVersion)
                .FirstOrDefault();
            if(target is null)return;
            var paper=_workspace.ListPaperProjects().FirstOrDefault(p=>
                _workspace.ListFigures(p.Id).Any(f=>f.Id==target.FigureId));
            if(paper is null)
            {
                System.Windows.MessageBox.Show(this,
                    "该 Figure 的论文项目已被删除或不可访问，历史引用仍保留。","定位来源");
                return;
            }
            if(target.CompositeId is not null)
            {
                SelectTreeTag(new FigureCompositesNode(paper.Id,target.FigureId));
                var match=FigureFiles.Items.OfType<FigureRow>()
                    .FirstOrDefault(x=>x.Item is FigureComposite c && c.Id==target.CompositeId);
                if(match is not null)
                {
                    FigureFiles.SelectedItem=match;
                    FigureFiles.ScrollIntoView(match);
                }
            }
            else
                SelectTreeTag(new PanelNode(paper.Id,target.FigureId,target.PanelId));
        }
        catch(Exception ex){ShowError(ex);}
    }
}
