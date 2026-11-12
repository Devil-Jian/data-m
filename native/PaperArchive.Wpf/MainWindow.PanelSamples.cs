using System.Windows;
using System.Windows.Controls;
using PaperArchive.Core.Workspace;

namespace PaperArchive.Wpf;

public partial class MainWindow
{
    private sealed record SampleUnitDrag(string Session,string SampleUnitId);

    private SampleUnitDrag? ReadSampleDrag(DragEventArgs e)
    {
        if(!e.Data.GetDataPresent(typeof(SampleUnitDrag)))return null;
        return e.Data.GetData(typeof(SampleUnitDrag)) is SampleUnitDrag data &&
            data.Session==_dragSession?data:null;
    }

    private string? ChooseShowForSample(string sampleId)
    {
        if(_workspace is null)return null;
        var shows=_workspace.ListSelectableSampleShows(sampleId);
        if(shows.Count==0)
            throw new InvalidOperationException(
                "该样本没有已识别的 SHOW；请检查文件名、系统标签与实验归属。");
        if(shows.Count==1)return shows[0].Id;
        var dialog=new Window{Owner=this,Title="选择本次 Panel 使用的 SHOW 版本",
            Width=580,Height=350,MinWidth=420,MinHeight=260,
            WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var root=new DockPanel{Margin=new Thickness(14)};
        var info=new TextBlock{Text="检测到多张 SHOW。请选择实际展示版本；不会自动推断最新版本。",
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};
        DockPanel.SetDock(info,Dock.Top);root.Children.Add(info);
        var button=new Button{Content="确认选用此 SHOW",Margin=new Thickness(0,10,0,0),
            HorizontalAlignment=HorizontalAlignment.Right,IsEnabled=false};
        DockPanel.SetDock(button,Dock.Bottom);root.Children.Add(button);
        var list=new ListBox{ItemsSource=shows,DisplayMemberPath="FileName"};
        list.SelectionChanged+=(_,_)=>button.IsEnabled=list.SelectedItem is ExperimentAsset;
        list.MouseDoubleClick+=(_,_)=>{if(list.SelectedItem is ExperimentAsset)
            dialog.DialogResult=true;};
        button.Click+=(_,_)=>dialog.DialogResult=true;
        root.Children.Add(list);
        dialog.Content=root;
        return dialog.ShowDialog()==true?
            (list.SelectedItem as ExperimentAsset)?.Id:null;
    }

    private bool AttachSampleToPanel(string panelId,string sampleId)
    {
        if(_workspace is null)return false;
        try
        {
            var show=ChooseShowForSample(sampleId);
            if(show is null)return false;
            var use=_workspace.LinkSampleToPanel(panelId,sampleId,show);
            StatusText.Text=$"{use.ExperimentName} / {use.SampleName} → Panel · {use.ShowFileName}";
            LoadProjectTree();
            return true;
        }
        catch(Exception ex){ShowError(ex);return false;}
    }
}
