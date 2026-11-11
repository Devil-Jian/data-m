using System.Windows;
using System.Windows.Controls;

namespace PaperArchive.Wpf;

public partial class MainWindow
{
    private void UndoFigureDrop_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace is null) return;
        var tag = (ProjectTree.SelectedItem as TreeViewItem)?.Tag;
        var paperId = tag switch
        {
            FigureNode figure => figure.PaperId,
            PanelNode panel => panel.PaperId,
            _ => _activePaper?.Id
        };
        var figureId = tag switch
        {
            FigureNode figure => figure.FigureId,
            PanelNode panel => panel.FigureId,
            _ => _currentFigure?.Id
        };
        if (figureId is null) return;
        try
        {
            if (!_workspace.UndoLastFigureDrop(figureId))
            {
                System.Windows.MessageBox.Show(this,
                    "此 Figure 没有可撤销的 SHOW 关联操作。", "撤销关联");
                return;
            }
            LoadProjectTree();
            if (paperId is not null)
                SelectTreeTag(new FigureNode(paperId,figureId));
            RefreshFigureView();
        }
        catch (Exception ex) { ShowError(ex); }
    }
}
