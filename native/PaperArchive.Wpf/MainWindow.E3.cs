using System.Windows;
namespace PaperArchive.Wpf;

public partial class MainWindow
{
    private void E3Launch_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null)return;
        var window=new E3WorkspaceWindow(_workspace){Owner=this};
        window.Closed+=(_,_)=>{
            // Refresh the legacy shell after the separate E3 workspace
            // imported experiments or changed Figure/Panel assignments.
            LoadProjectTree();
        };
        window.Show();
    }
}
