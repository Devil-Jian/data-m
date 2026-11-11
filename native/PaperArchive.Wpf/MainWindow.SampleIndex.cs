using System.Windows;
using System.Windows.Controls;
using PaperArchive.Core.Workspace;

namespace PaperArchive.Wpf;

public partial class MainWindow
{
    private void RebuildSampleIndex_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace is null ||
            ProjectTree.SelectedItem is not TreeViewItem { Tag: ExperimentProject project })
            return;
        try
        {
            var preview = _workspace.PreviewSampleIndexRebuild(project.Id);
            if (preview.Changes.Count == 0)
            {
                System.Windows.MessageBox.Show(this,
                    $"已索引 {preview.IndexedCount} 张图片；" +
                    $"有 {preview.UnresolvedProcessCount} 张 PROCESS 图片没有可靠的样本锚点。\n" +
                    "当前索引无须重建。未确认的图片不会被强行匹配。",
                    "样本索引");
                return;
            }
            var examples = string.Join("\n", preview.Changes.Take(10).Select(x =>
                $"• {x.FileName}: {x.PreviousSample ?? "未索引"} → {x.ProposedSample ?? "未索引"}"));
            var suffix = preview.Changes.Count > 10
                ? $"\n（另有 {preview.Changes.Count - 10} 项变更）" : "";
            var decision = System.Windows.MessageBox.Show(this,
                $"实验项目：{project.Name}\n" +
                $"拟索引：{preview.IndexedCount} 张图片\n" +
                $"索引变化：{preview.Changes.Count} 项\n" +
                $"暂不匹配：{preview.UnresolvedProcessCount} 张 PROCESS 图片\n\n" +
                examples + suffix +
                "\n\n仅更新可撤销的样本检索索引，不会修改文件、数据组、已确认来源或归档。\n" +
                "同样本不代表已确认的加工关系。是否应用？",
                "预览并重建样本索引",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (decision != MessageBoxResult.Yes) return;
            _workspace.ApplySampleIndexRebuild(project.Id, preview.Fingerprint);
            LoadSelectedResult();
            RebuildFolderList();
            UpdateDetails();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void UndoSampleIndex_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace is null ||
            ProjectTree.SelectedItem is not TreeViewItem { Tag: ExperimentProject project })
            return;
        try
        {
            var response = System.Windows.MessageBox.Show(this,
                "撤销该实验项目最近一次手动样本索引重建？\n" +
                "仅恢复索引，不改动原始文件、原数据组和历史来源。",
                "撤销样本索引重建",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (response != MessageBoxResult.Yes) return;
            if (!_workspace.UndoLastSampleIndexRebuild(project.Id))
            {
                System.Windows.MessageBox.Show(this, "没有可撤销的手动索引重建。", "样本索引");
                return;
            }
            LoadSelectedResult();
            RebuildFolderList();
            UpdateDetails();
        }
        catch (Exception ex) { ShowError(ex); }
    }
}
