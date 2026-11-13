using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PaperArchive.Core.Workspace;

namespace PaperArchive.Wpf;

/// <summary>
/// E3 is an alternative, folder-first workspace. Database IDs never appear
/// in visible text; only experiment names, Figure labels and file names do.
/// The legacy workspace remains available during the E3/E4 transition.
/// </summary>
public sealed class E3WorkspaceWindow : Window
{
    private readonly ExperimentWorkspace _repo;
    private readonly ListBox _papers=new();
    private readonly ComboBox _figures=new();
    private readonly WrapPanel _cards=new(){Orientation=Orientation.Horizontal};
    private readonly ListBox _experiments=new();
    private readonly ListBox _samples=new();
    private readonly ListBox _files=new();
    private readonly Image _preview=new(){Stretch=Stretch.Uniform};
    private readonly TextBlock _selection=new(){FontWeight=FontWeights.SemiBold,FontSize=17};
    private readonly TextBlock _status=new(){Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap};
    private readonly TextBox _search=new(){MinWidth=180};
    private PaperProject? _paper;
    private FigureTemplate? _figure;
    private PanelTemplate? _panel;
    private E1FolderIdentity? _experiment;
    private Point _dragStart;

    private sealed record FileItem(string Name,string Stage,string? Sample,
        string Sha256,string RelativePath)
    {
        public string Display=>Stage.ToUpperInvariant()+"   "+Name;
    }

    public E3WorkspaceWindow(ExperimentWorkspace workspace)
    {
        _repo=workspace;
        Title="PaperArchive · 论文工作台";
        Width=1360;Height=850;MinWidth=950;MinHeight=570;
        Background=new SolidColorBrush(Color.FromRgb(246,247,249));
        WindowStartupLocation=WindowStartupLocation.CenterOwner;

        var root=new Grid();
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition());
        Content=root;

        var top=new DockPanel{Margin=new Thickness(18,12,18,12),LastChildFill=false};
        var title=new TextBlock{Text="论文工作台",FontSize=23,FontWeight=FontWeights.SemiBold,
            Foreground=new SolidColorBrush(Color.FromRgb(35,47,65))};
        DockPanel.SetDock(title,Dock.Left);top.Children.Add(title);
        var hint=new TextBlock{Text="实验文件自动索引 · 子图与实验一对一",Margin=new Thickness(18,9,0,0),
            Foreground=Brushes.DimGray,VerticalAlignment=VerticalAlignment.Center};
        top.Children.Add(hint);
        Grid.SetRow(top,0);root.Children.Add(top);

        var body=newGrid();
        body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(238)});
        body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(450)});
        body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        Grid.SetRow(body,1);root.Children.Add(body);

        var paperPane=Pane();
        Grid.SetColumn(paperPane,0);body.Children.Add(paperPane);
        var paperContent=new DockPanel{Margin=new Thickness(12)};
        paperPane.Child=paperContent;
        var paperHeader=new StackPanel();
        paperHeader.Children.Add(Heading("论文"));
        paperHeader.Children.Add(Action("＋ 新建论文",(_,_)=>CreatePaper()));
        DockPanel.SetDock(paperHeader,Dock.Top);paperContent.Children.Add(paperHeader);
        _papers.DisplayMemberPath="Name";
        _papers.SelectionChanged+=(_,_)=>SelectPaper();
        _papers.BorderThickness=new Thickness(0);
        _papers.Margin=new Thickness(0,10,0,0);
        paperContent.Children.Add(_papers);

        var central=Pane();
        Grid.SetColumn(central,1);body.Children.Add(central);
        var c=new Grid{Margin=new Thickness(14)};
        c.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        c.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        c.RowDefinitions.Add(new RowDefinition{Height=new GridLength(285)});
        central.Child=c;
        var figBar=new StackPanel();
        figBar.Children.Add(Heading("Figure / 子图"));
        var commands=new WrapPanel{Margin=new Thickness(0,5,0,12)};
        commands.Children.Add(Action("＋ Figure",(_,_)=>CreateFigure()));
        commands.Children.Add(Action("＋ 子图",(_,_)=>CreatePanel()));
        commands.Children.Add(Action("导入实验文件夹",async (_,_)=>await ImportFolder()));
        figBar.Children.Add(commands);
        _figures.DisplayMemberPath="Number";
        _figures.MinWidth=180;
        _figures.SelectionChanged+=(_,_)=>SelectFigure();
        figBar.Children.Add(_figures);
        Grid.SetRow(figBar,0);c.Children.Add(figBar);
        var scroller=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        scroller.Content=_cards;
        Grid.SetRow(scroller,1);c.Children.Add(scroller);

        var experimentsPane=new DockPanel{Margin=new Thickness(0,12,0,0)};
        Grid.SetRow(experimentsPane,2);c.Children.Add(experimentsPane);
        var experimentsHeader=new StackPanel();
        experimentsHeader.Children.Add(Heading("实验资料库"));
        experimentsHeader.Children.Add(new TextBlock{
            Text="拖到 A / B / C，或选中子图后双击实验",
            FontSize=12,Foreground=Brushes.Gray});
        _search.Margin=new Thickness(0,7,0,7);
        _search.TextChanged+=(_,_)=>LoadExperiments();
        experimentsHeader.Children.Add(_search);
        DockPanel.SetDock(experimentsHeader,Dock.Top);
        experimentsPane.Children.Add(experimentsHeader);
        _experiments.DisplayMemberPath="ExperimentName";
        _experiments.BorderThickness=new Thickness(0);
        _experiments.SelectionChanged+=(_,_)=>SelectExperiment();
        _experiments.PreviewMouseLeftButtonDown+=(_,e)=>_dragStart=e.GetPosition(_experiments);
        _experiments.PreviewMouseMove+=ExperimentDrag;
        _experiments.MouseDoubleClick+=(_,_)=>{
            if(_panel is not null&&_experiments.SelectedItem is E1FolderIdentity experiment)
                AssignExperiment(_panel,experiment);
        };
        experimentsPane.Children.Add(_experiments);

        var details=Pane();
        Grid.SetColumn(details,2);body.Children.Add(details);
        var d=new Grid{Margin=new Thickness(16)};
        d.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        d.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        d.RowDefinitions.Add(new RowDefinition{Height=new GridLength(130)});
        d.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        d.RowDefinitions.Add(new RowDefinition{Height=new GridLength(230)});
        d.Children.Add(_selection);
        Grid.SetRow(_status,1);_status.Margin=new Thickness(0,7,0,11);
        d.Children.Add(_status);
        _samples.DisplayMemberPath="SampleName";
        _samples.SelectionChanged+=(_,_)=>FilterFiles();
        Grid.SetRow(_samples,2);d.Children.Add(_samples);
        _files.DisplayMemberPath="Display";
        _files.SelectionChanged+=(_,_)=>PreviewSelected();
        _files.MouseDoubleClick+=(_,_)=>OpenSelected();
        Grid.SetRow(_files,3);d.Children.Add(_files);
        var imageBorder=new Border{BorderBrush=new SolidColorBrush(Color.FromRgb(220,225,231)),
            BorderThickness=new Thickness(1),Background=Brushes.White,
            CornerRadius=new CornerRadius(6),Padding=new Thickness(4),Margin=new Thickness(0,10,0,0)};
        imageBorder.Child=_preview;
        Grid.SetRow(imageBorder,4);d.Children.Add(imageBorder);
        details.Child=d;

        ReloadPapers();
        LoadExperiments();
    }

    private static Grid newGrid()=>new();
    private static Border Pane()=>new(){Background=Brushes.White,
        BorderBrush=new SolidColorBrush(Color.FromRgb(225,229,235)),
        BorderThickness=new Thickness(0,0,1,0)};
    private static TextBlock Heading(string title)=>new(){Text=title,FontSize=16,
        FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,7)};
    private static Button Action(string title,RoutedEventHandler click)
    {
        var b=new Button{Content=title,Padding=new Thickness(10,6,10,6),
            Margin=new Thickness(0,0,7,7),MinHeight=32};
        b.Click+=click;return b;
    }

    private void ReloadPapers(string? preferred=null)
    {
        var papers=_repo.ListPaperProjects();
        _papers.ItemsSource=papers;
        _papers.SelectedItem=papers.FirstOrDefault(x=>x.Id==preferred)
            ??papers.FirstOrDefault(x=>x.Id==_paper?.Id)??papers.FirstOrDefault();
        if(papers.Count==0)
        {
            _paper=null;_figures.ItemsSource=null;_cards.Children.Clear();
            _selection.Text="新建论文后开始";_status.Text="选择“＋ 新建论文”";
        }
    }
    private void SelectPaper()
    {
        _paper=_papers.SelectedItem as PaperProject;
        _panel=null;_experiment=null;LoadFigures();
        LoadExperiments();
    }
    private void LoadFigures(string? preferred=null)
    {
        var figures=_paper is null?[]:_repo.ListFigures(_paper.Id);
        _figures.ItemsSource=figures;
        _figures.SelectedItem=figures.FirstOrDefault(x=>x.Id==preferred)
            ??figures.FirstOrDefault(x=>x.Id==_figure?.Id)
            ??figures.FirstOrDefault();
        if(figures.Count==0){_figure=null;_cards.Children.Clear();}
    }
    private void SelectFigure()
    {
        _figure=_figures.SelectedItem as FigureTemplate;
        _panel=null;LoadCards();ShowDetails();
    }

    private void LoadCards()
    {
        _cards.Children.Clear();
        if(_figure is null)return;
        foreach(var panel in _repo.ListPanels(_figure.Id))
        {
            var binding=_repo.GetPanelExperiment(panel.Id);
            var card=new Border{
                Width=191,MinHeight=118,Padding=new Thickness(14),
                Margin=new Thickness(0,0,12,12),
                CornerRadius=new CornerRadius(9),
                BorderThickness=new Thickness(panel.Id==_panel?.Id?2:1),
                BorderBrush=panel.Id==_panel?.Id?Brushes.SteelBlue:
                    new SolidColorBrush(Color.FromRgb(223,228,233)),
                Background=new SolidColorBrush(Color.FromRgb(251,252,254)),
                AllowDrop=true,Tag=panel,Cursor=Cursors.Hand};
            var stack=new StackPanel();
            stack.Children.Add(new TextBlock{Text=panel.Label,FontSize=24,
                FontWeight=FontWeights.Bold});
            stack.Children.Add(new TextBlock{Text=binding?.ExperimentName??"＋ 关联实验",
                FontSize=14,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,
                Margin=new Thickness(0,7,0,3)});
            var count=binding is null? "点击后选择实验":
                _repo.ListSampleUnits(experimentId:binding.ExperimentId).Count+" 个样本";
            stack.Children.Add(new TextBlock{Text=count,Foreground=Brushes.DimGray});
            card.Child=stack;
            card.MouseLeftButtonUp+=(_,_)=>SelectPanel(panel);
            card.DragOver+=(_,e)=>{
                e.Effects=e.Data.GetDataPresent("PaperArchive.E3.ExperimentId")?
                    DragDropEffects.Copy:DragDropEffects.None;
                e.Handled=true;
            };
            card.Drop+=(_,e)=>{
                if(e.Data.GetData("PaperArchive.E3.ExperimentId") is not string id)return;
                var experiment=AllExperiments().FirstOrDefault(x=>x.ExperimentId==id);
                if(experiment is not null)AssignExperiment(panel,experiment);
                e.Handled=true;
            };
            _cards.Children.Add(card);
        }
    }
    private void SelectPanel(PanelTemplate panel)
    {
        _panel=panel;
        var id=_repo.GetPanelExperiment(panel.Id)?.ExperimentId;
        _experiment=AllExperiments().FirstOrDefault(x=>x.ExperimentId==id);
        LoadCards();ShowDetails();
    }

    private E1FolderIdentity[] AllExperiments()=>
        _repo.ListProjects().SelectMany(p=>_repo.ListE1FolderIdentities(p.Id))
            .Where(x=>!string.IsNullOrEmpty(x.ExperimentId))
            .OrderBy(x=>x.ExperimentName,StringComparer.OrdinalIgnoreCase).ToArray();

    private void LoadExperiments()
    {
        var text=_search.Text.Trim();
        var items=AllExperiments().Where(x=>
            x.ExperimentName.Contains(text,StringComparison.OrdinalIgnoreCase))
            .ToArray();
        _experiments.ItemsSource=items;
    }
    private void SelectExperiment()
    {
        if(_experiments.SelectedItem is not E1FolderIdentity experiment)return;
        _experiment=experiment;ShowDetails();
    }
    private void ExperimentDrag(object sender,MouseEventArgs e)
    {
        if(e.LeftButton!=MouseButtonState.Pressed ||
            _experiments.SelectedItem is not E1FolderIdentity selected ||
            Math.Abs(e.GetPosition(_experiments).X-_dragStart.X)<SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(e.GetPosition(_experiments).Y-_dragStart.Y)<SystemParameters.MinimumVerticalDragDistance)
            return;
        DragDrop.DoDragDrop(_experiments,
            new DataObject("PaperArchive.E3.ExperimentId",selected.ExperimentId),
            DragDropEffects.Copy);
    }
    private void AssignExperiment(PanelTemplate panel,E1FolderIdentity experiment)
    {
        try
        {
            var old=_repo.GetPanelExperiment(panel.Id);
            var replace=old is not null&&old.ExperimentId!=experiment.ExperimentId;
            if(replace&&MessageBox.Show(this,
                "将 "+old!.ExperimentName+" 替换为 "+experiment.ExperimentName+"？",
                "更换子图实验",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)
                return;
            _repo.LinkPanelExperiment(panel.Id,experiment.ExperimentId,replace);
            _panel=panel;_experiment=experiment;LoadCards();ShowDetails();
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"无法关联实验",
            MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
    private void ShowDetails()
    {
        _samples.ItemsSource=null;_files.ItemsSource=null;_preview.Source=null;
        if(_experiment is null)
        {
            _selection.Text=_panel is null?"选择实验或子图":
                "Panel "+_panel.Label+" · 未关联";
            _status.Text="选择实验后双击，或直接拖入对应子图。";
            return;
        }
        _selection.Text=(_panel is null?"实验资料":("Panel "+_panel.Label))+
            " · "+_experiment.ExperimentName;
        var sampleUnits=_repo.ListSampleUnits(experimentId:_experiment.ExperimentId);
        _samples.ItemsSource=sampleUnits;
        _status.Text=sampleUnits.Count+" 个样本 · "+_experiment.ActiveFileCount+
            " 个文件，单击文件预览，双击打开。";
        _samples.SelectedItem=sampleUnits.FirstOrDefault();
        FilterFiles();
    }
    private void FilterFiles()
    {
        if(_experiment is null)return;
        var sample=_samples.SelectedItem as SampleUnit;
        var files=new List<FileItem>();
        if(sample is not null)
        {
            files.AddRange(_repo.ListSampleUnitAssets(sample.Id).Select(x=>
                new FileItem(x.FileName,x.Stage,sample.SampleName,x.Sha256,x.RelativePath)));
        }
        else if(_panel is not null&&
            _repo.GetPanelExperiment(_panel.Id)?.ExperimentId==_experiment.ExperimentId)
        {
            files.AddRange(_repo.ListPanelExperimentFiles(_panel.Id).Select(x=>
                new FileItem(x.FileName,x.Stage,x.SampleName,x.Sha256,x.RelativePath)));
        }
        else
        {
            foreach(var unit in _repo.ListSampleUnits(experimentId:_experiment.ExperimentId))
                files.AddRange(_repo.ListSampleUnitAssets(unit.Id).Select(x=>
                    new FileItem(x.FileName,x.Stage,unit.SampleName,x.Sha256,x.RelativePath)));
        }
        // The E1 root index also contains unclassified experiment documents;
        // don't lose them just because no stable sample name can be inferred.
        var indexed=_repo.ListE1FolderAssets(_experiment.Id);
        foreach(var asset in indexed)
            if(!files.Any(x=>x.RelativePath==asset.RelativePath&&
                              x.Sha256.Equals(asset.Sha256,StringComparison.OrdinalIgnoreCase)))
                files.Add(new FileItem(Path.GetFileName(asset.RelativePath),
                    "其他",null,asset.Sha256,asset.RelativePath));
        _files.ItemsSource=files.GroupBy(x=>x.RelativePath+"|"+x.Sha256,
            StringComparer.OrdinalIgnoreCase).Select(x=>x.First())
            .OrderBy(x=>x.Stage=="show"?0:x.Stage=="raw"?1:x.Stage=="process"?2:3)
            .ThenBy(x=>x.Name,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private void PreviewSelected()
    {
        _preview.Source=null;
        if(_files.SelectedItem is not FileItem file)return;
        if(!new[]{".png",".jpg",".jpeg",".bmp",".tif",".tiff",".gif",".webp"}
            .Contains(Path.GetExtension(file.Name),StringComparer.OrdinalIgnoreCase))
            return;
        try
        {
            if(file.Sha256.Length!=64||!file.Sha256.All(Uri.IsHexDigit))return;
            var blob=Path.Combine(_repo.Root,"blobs","sha256",file.Sha256[..2],file.Sha256);
            using var stream=File.OpenRead(blob);
            var bitmap=new BitmapImage();
            bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth=780;bitmap.StreamSource=stream;
            bitmap.EndInit();bitmap.Freeze();
            _preview.Source=bitmap;
        }
        catch { /* Keep unsupported images in the file list. */ }
    }
    private void OpenSelected()
    {
        if(_files.SelectedItem is not FileItem file)return;
        try
        {
            if(file.Sha256.Length!=64||!file.Sha256.All(Uri.IsHexDigit))return;
            var blob=Path.Combine(_repo.Root,"blobs","sha256",file.Sha256[..2],file.Sha256);
            if(!File.Exists(blob))throw new FileNotFoundException("未找到原始文件副本。");
            var location=Path.Combine(Path.GetTempPath(),"PaperArchive","E3Preview",
                file.Sha256[..12]);
            Directory.CreateDirectory(location);
            var output=Path.Combine(location,Path.GetFileName(file.Name));
            File.Copy(blob,output,true);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(output)
            {UseShellExecute=true});
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"文件打开失败");}
    }
    private static string? AskName(Window owner,string title)
    {
        var dialog=new Window{Title=title,Owner=owner,Width=390,Height=155,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            ResizeMode=ResizeMode.NoResize};
        var stack=new StackPanel{Margin=new Thickness(18)};
        var box=new TextBox{Margin=new Thickness(0,0,0,12)};
        stack.Children.Add(box);
        var button=new Button{Content="确定",MinWidth=85,HorizontalAlignment=HorizontalAlignment.Right};
        button.Click+=(_,_)=>dialog.DialogResult=!string.IsNullOrWhiteSpace(box.Text);
        stack.Children.Add(button);dialog.Content=stack;
        box.Focus();
        return dialog.ShowDialog()==true?box.Text.Trim():null;
    }
    private void CreatePaper()
    {
        var name=AskName(this,"新建论文");
        if(name is null)return;
        try{ReloadPapers(_repo.CreatePaperProject(name));}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"无法创建论文");}
    }
    private void CreateFigure()
    {
        if(_paper is null){MessageBox.Show(this,"请先选择或新建论文。");return;}
        try{var id=_repo.CreateFigure(_paper.Id);LoadFigures(id);}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"无法创建 Figure");}
    }
    private void CreatePanel()
    {
        if(_figure is null){MessageBox.Show(this,"请先创建或选择 Figure。");return;}
        try
        {
            var panel=_repo.CreatePanelsFast(_figure.Id,1).Single();
            _panel=panel;LoadCards();ShowDetails();
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"无法创建子图");}
    }
    private async Task ImportFolder()
    {
        if(_paper is null){MessageBox.Show(this,"请先选择或新建论文。");return;}
        var dialog=new Microsoft.Win32.OpenFolderDialog{
            Title="选择实验根文件夹",Multiselect=false};
        if(dialog.ShowDialog(this)!=true)return;
        var name=Path.GetFileName(Path.TrimEndingDirectorySeparator(dialog.FolderName));
        try
        {
            IsEnabled=false;
            var paperId=_paper.Id;
            var project=_repo.ListProjectsForPaper(paperId).FirstOrDefault();
            var projectId=project?.Id??_repo.CreateProjectForPaper(paperId,"实验资料");
            await Task.Run(()=>_repo.ImportResult(projectId,dialog.FolderName));
            LoadExperiments();
            var matched=AllExperiments().FirstOrDefault(x=>
                x.RootPath.Equals(Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(dialog.FolderName)).Replace('\\','/'),
                    StringComparison.OrdinalIgnoreCase));
            _experiment=matched;
            if(matched is not null)_experiments.SelectedItem=
                _experiments.Items.Cast<E1FolderIdentity>()
                    .FirstOrDefault(x=>x.Id==matched.Id);
            ShowDetails();
            _status.Text="已导入 "+name+"，可拖入任意空子图。";
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"导入失败");}
        finally{IsEnabled=true;}
    }
}
