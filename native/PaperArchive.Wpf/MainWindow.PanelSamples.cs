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

    private sealed record PanelSampleChoice(SampleUnit Unit)
    {
        public override string ToString()=>$"{Unit.ExperimentName} / {Unit.SampleName}"+
            $"  [SHOW {Unit.ShowCount}]";
    }
    private sealed record UsedSampleChoice(PanelSampleUse Use)
    {
        public override string ToString()=>
            $"{Use.ExperimentName} / {Use.SampleName} → {Use.ShowFileName}";
    }

    private void ManagePanelSamples_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null||_activePaper is null)return;
        var panel=CurrentOrSelectedPanel()??ChoosePanel(_activePaper.Id);
        if(panel is null)return;
        var workspace=_workspace;
        var window=new Window{Title="Panel "+panel.Label+" · 样本用途",
            Owner=this,Width=700,Height=510,MinWidth=530,MinHeight=400,
            WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var root=new DockPanel{Margin=new Thickness(13)};
        var heading=new TextBlock{Text="上方选择实验样本并添加；下方查看本 Panel 实际采用的 SHOW。"+
            "多 SHOW 版本必须明确选择。",TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,0,0,12)};
        DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,
            HorizontalAlignment=HorizontalAlignment.Right,
            Margin=new Thickness(0,10,0,0)};
        var add=new Button{Content="关联选中样本",MinWidth=115};
        var remove=new Button{Content="解除下方选中用途",MinWidth=145,
            Margin=new Thickness(9,0,0,0)};
        var close=new Button{Content="关闭",MinWidth=75,
            Margin=new Thickness(15,0,0,0)};
        actions.Children.Add(add);actions.Children.Add(remove);
        actions.Children.Add(close);
        DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var panes=new Grid();
        panes.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        panes.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        panes.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        var available=new ListBox();
        var selected=new ListBox();
        var validSamples=workspace.ListProjectsForPaper(_activePaper.Id)
            .SelectMany(x=>workspace.ListSampleUnits(projectId:x.Id))
            .Where(x=>x.ShowCount>0).Select(x=>new PanelSampleChoice(x))
            .OrderBy(x=>x.Unit.ExperimentName,StringComparer.OrdinalIgnoreCase)
            .ThenBy(x=>x.Unit.SampleName,StringComparer.OrdinalIgnoreCase).ToArray();
        available.ItemsSource=validSamples;
        available.ToolTip="仅显示至少有一张 SHOW 的样本；不表示已确认 PROCESS 加工顺序。";
        var label=new TextBlock{Text="已关联样本 / 所用 SHOW",
            Margin=new Thickness(3,9,3,7),FontWeight=FontWeights.SemiBold};
        Grid.SetRow(available,0);panes.Children.Add(available);
        Grid.SetRow(label,1);panes.Children.Add(label);
        Grid.SetRow(selected,2);panes.Children.Add(selected);
        root.Children.Add(panes);window.Content=root;
        void Reload()
        {
            selected.ItemsSource=workspace.ListPanelSampleUses(panel.Id)
                .Select(x=>new UsedSampleChoice(x)).ToArray();
            RefreshFigureView();
        }
        add.Click+=(_,_)=>{
            if(available.SelectedItem is not PanelSampleChoice sample)return;
            if(AttachSampleToPanel(panel.Id,sample.Unit.Id))
                Reload();
        };
        available.MouseDoubleClick+=(_,_)=>add.RaiseEvent(
            new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        remove.Click+=(_,_)=>{
            if(selected.SelectedItem is not UsedSampleChoice choice)return;
            if(System.Windows.MessageBox.Show(window,
                $"解除 {choice.Use.ExperimentName} / {choice.Use.SampleName} "+
                $"在 Panel {panel.Label} 的工作区关联？"+
                "\n历史成图快照和归档 Revision 不会修改。",
                "解除样本用途",MessageBoxButton.YesNo,MessageBoxImage.Question)
                !=MessageBoxResult.Yes)return;
            try
            {
                workspace.UnlinkSampleFromPanel(panel.Id,choice.Use.SampleUnitId);
                LoadProjectTree();
                Reload();
            }
            catch(Exception ex){ShowError(ex);}
        };
        close.Click+=(_,_)=>window.Close();
        Reload();
        window.ShowDialog();
        SelectTreeTag(new PanelNode(_activePaper.Id,
            _currentFigure?.Id??workspace.ListFigures(_activePaper.Id)
                .First(f=>workspace.ListPanels(f.Id).Any(p=>p.Id==panel.Id)).Id,panel.Id));
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
