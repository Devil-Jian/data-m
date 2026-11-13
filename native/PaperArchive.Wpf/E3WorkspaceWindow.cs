using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PaperArchive.Core.Workspace;
using PaperArchive.Core;

namespace PaperArchive.Wpf;

/// <summary>
/// E3 is an alternative, folder-first workspace. Database IDs never appear
/// in visible text; only experiment names, Figure labels and file names do.
/// The legacy workspace remains available during the E3/E4 transition.
/// </summary>
public sealed partial class E3WorkspaceWindow : Window
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
        commands.Children.Add(Action("导入论文全文",(_,_)=>ImportDocument()));
        commands.Children.Add(Action("归档论文",(_,_)=>ArchivePaper()));
        commands.Children.Add(Action("导入完整 Figure",(_,_)=>ImportComposite()));
        commands.Children.Add(Action("解除子图实验",(_,_)=>UnlinkPanel()));
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
        EnableE5Virtualization(_experiments);
        _experiments.DisplayMemberPath="ExperimentName";
        _experiments.AllowDrop=true;
        _experiments.DragOver+=(_,e)=>{
            if(e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects=DragDropEffects.Copy;e.Handled=true;
            }
        };
        _experiments.Drop+=async (_,e)=>{
            if(e.Data.GetData(DataFormats.FileDrop) is string[] paths &&
                paths.Length==1&&Directory.Exists(paths[0]))
                await ImportFolder(paths[0]);
            e.Handled=true;
        };
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
        EnableE5Virtualization(_samples);
        _samples.DisplayMemberPath="SampleName";
        _samples.SelectionChanged+=(_,_)=>FilterFiles();
        Grid.SetRow(_samples,2);d.Children.Add(_samples);
        EnableE5Virtualization(_files);
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

        InitializeE5Shell(root,top,body,c,figBar,scroller,experimentsPane);

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
        _panel=null;_experiment=null;LoadCards();ShowDetails();
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
        _e5ExperimentCache??=_repo.ListProjects()
            .SelectMany(p=>_repo.ListE1FolderIdentities(p.Id))
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
            _selection.Text=_panel is null?"Figure 成图":
                "Panel "+_panel.Label+" · 未关联";
            _status.Text=_panel is null?"选择子图或实验。也可以导入完整 Figure 成图。":
                "选择实验后双击，或直接拖入对应子图。";
            PreviewFigureImage();
            return;
        }
        var isBound=_panel is not null&&
            _repo.GetPanelExperiment(_panel.Id)?.ExperimentId==_experiment.ExperimentId;
        _selection.Text=(isBound?"Panel "+_panel!.Label+" · ":"实验资料 · ")+
            _experiment.ExperimentName;
        var sampleUnits=_repo.ListSampleUnits(experimentId:_experiment.ExperimentId);
        _samples.ItemsSource=sampleUnits;
        _status.Text=sampleUnits.Count+" 个样本 · "+_experiment.ActiveFileCount+
            " 个文件，单击文件预览，双击打开。";
        // Experiment view defaults to ALL files, including unclassified
        // attachments. Clicking a sample narrows the list.
        _samples.SelectedIndex=-1;
        FilterFiles();
    }
    private void FilterFiles()
    {
        if(_experiment is null)
        {
            _files.ItemsSource=null;
            return;
        }
        var sample=_samples.SelectedItem as SampleUnit;
        // Files are read from the current E1 source membership. An earlier
        // imported version belongs to its historical Revision, not here.
        var files=_repo.ListE5CurrentExperimentFiles(_experiment.ExperimentId)
            .Where(x=>sample is null || x.SampleId==sample.Id)
            .GroupBy(x=>x.AssetId,StringComparer.Ordinal)
            .Select(x=>x.First())
            .Select(x=>new FileItem(x.FileName,x.Stage,x.SampleName,
                x.Sha256,x.RelativePath))
            .OrderBy(x=>x.Stage=="show"?0:x.Stage=="raw"?1:
                x.Stage=="process"?2:x.Stage=="quant"?3:4)
            .ThenBy(x=>x.Sample,StringComparer.OrdinalIgnoreCase)
            .ThenBy(x=>x.Name,StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _files.ItemsSource=files;
    }
    private async void PreviewSelected()
    {
        _preview.Source=null;
        _e5PreviewCancel?.Cancel();
        if(_files.SelectedItem is not FileItem file)return;
        if(!E5IsPreviewable(file.Name))return;
        using var cancellation=new CancellationTokenSource();
        _e5PreviewCancel=cancellation;
        try
        {
            var bitmap=await LoadE5BitmapAsync(file.Sha256,cancellation.Token);
            if(!cancellation.IsCancellationRequested)_preview.Source=bitmap;
        }
        catch(OperationCanceledException) { }
        catch(IOException) { _status.Text="图片原始副本不可用。"; }
        catch(NotSupportedException) { _status.Text="此图片格式暂不支持内嵌预览，可双击打开。"; }
        catch(System.IO.FileFormatException) {
            _status.Text="图片格式无法预览，可双击打开。";
        }
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
    private async void PreviewFigureImage()
    {
        if(_figure is null)return;
        var current=_repo.ListFigureShowData(_figure.Id)
            .FirstOrDefault(x=>x.IsCurrent);
        if(current is null)return;
        _status.Text="完整 Figure · "+current.FileName;
        _e5PreviewCancel?.Cancel();
        var cancellation=new CancellationTokenSource();
        _e5PreviewCancel=cancellation;
        try
        {
            var image=await LoadE5BitmapAsync(current.Sha256,cancellation.Token);
            if(!cancellation.IsCancellationRequested)_preview.Source=image;
        }
        catch(OperationCanceledException) { }
        catch(IOException) { _status.Text="完整 Figure 的原始副本缺失。"; }
        catch(NotSupportedException) { _status.Text="此 Figure 格式暂不支持预览。"; }
        catch(System.IO.FileFormatException) { _status.Text="此 Figure 图片无法解码。"; }
    }
    private void ImportComposite()
    {
        if(_figure is null){MessageBox.Show(this,"请先选择 Figure。");return;}
        var dialog=new Microsoft.Win32.OpenFileDialog{
            Title="导入完整 Figure "+_figure.Number,
            Filter="科研图片|*.tif;*.tiff;*.png;*.jpg;*.jpeg;*.bmp;*.webp",
            Multiselect=false};
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            _repo.ImportFigureComposite(_figure.Id,dialog.FileName);
            _experiment=null;_panel=null;
            LoadCards();ShowDetails();
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"导入完整 Figure 失败");}
    }
    private void UnlinkPanel()
    {
        if(_panel is null||_repo.GetPanelExperiment(_panel.Id) is null)return;
        if(MessageBox.Show(this,"解除子图与实验的关联？实验文件不会删除。",
            "解除实验关联",MessageBoxButton.YesNo,MessageBoxImage.Question)
            !=MessageBoxResult.Yes)return;
        try
        {
            _repo.UnlinkPanelExperiment(_panel.Id);
            _experiment=null;LoadCards();ShowDetails();
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"解除失败");}
    }

    private void ImportDocument()
    {
        if(_paper is null){MessageBox.Show(this,"请先选择论文。");return;}
        var dialog=new Microsoft.Win32.OpenFileDialog{
            Title="选择论文全文",
            Filter="论文全文 (*.pdf;*.doc;*.docx)|*.pdf;*.doc;*.docx",
            Multiselect=false};
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            _repo.ImportPaperDocument(_paper.Id,dialog.FileName);
            _status.Text="论文全文已导入："+Path.GetFileName(dialog.FileName);
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"导入论文失败");}
    }

    private void ArchivePaper()
    {
        if(_paper is null){MessageBox.Show(this,"请先选择论文。");return;}
        try
        {
            var docs=_repo.ListPaperDocuments(_paper.Id);
            if(docs.Count==0){MessageBox.Show(this,"请先导入论文全文。");return;}
            var document=_repo.GetPrimaryPaperDocument(_paper.Id)??docs
                .OrderByDescending(x=>x.ImportedAt).First();
            var archive=ArchiveRepository.Open(_repo.Root);
            var preview=archive.PrepareWorkspaceArchiveE4(_repo,_paper.Id,document.Id);
            var blockers=preview.Checks.Where(x=>x.Level=="block")
                .DistinctBy(x=>x.Code+"|"+x.Message).ToArray();
            if(blockers.Length>0)
            {
                MessageBox.Show(this,"请先完成以下内容：\n\n"+
                    string.Join("\n",blockers.Take(8).Select(x=>"· "+x.Message))+
                    (blockers.Length>8?"\n…":""),
                    "归档检查",MessageBoxButton.OK,MessageBoxImage.Warning);
                return;
            }
            var answer=MessageBox.Show(this,
                "归档论文："+_paper.Name+"\n论文全文："+document.FileName+
                "\nFigure："+preview.FigureCount+" 张"+
                "\n文件："+preview.Resources.Count+" 条"+
                "\n\n归档后历史版本将保持不变。",
                "确认归档",MessageBoxButton.YesNo,MessageBoxImage.Question);
            if(answer!=MessageBoxResult.Yes)return;
            var published=archive.PublishWorkspaceArchiveE4(_repo,preview);
            MessageBox.Show(this,
                published.AlreadyArchived?
                    "论文内容未变化，仍为 Revision "+published.Revision:
                    "归档完成：Revision "+published.Revision+
                    " · "+published.FileCount+" 条文件。",
                "论文已归档");
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"归档失败");}
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
    private async Task ImportFolder(string? selectedPath=null)
    {
        if(selectedPath is null)
        {
            var dialog=new Microsoft.Win32.OpenFolderDialog{
                Title="选择实验根文件夹",Multiselect=false};
            if(dialog.ShowDialog(this)!=true)return;
            selectedPath=dialog.FolderName;
        }
        var name=Path.GetFileName(Path.TrimEndingDirectorySeparator(selectedPath));
        try
        {
            IsEnabled=false;
            // An experiment is a reusable source folder; importing it must
            // not require creating a paper or Figure beforehand.
            string projectId;
            if(_paper is not null)
            {
                var project=_repo.ListProjectsForPaper(_paper.Id).FirstOrDefault();
                projectId=project?.Id??_repo.CreateProjectForPaper(_paper.Id,"实验资料");
            }
            else
            {
                var standalone=_repo.ListProjects()
                    .FirstOrDefault(x=>x.Name=="独立实验资料库");
                projectId=standalone?.Id??_repo.CreateProject("独立实验资料库");
            }
            await Task.Run(()=>_repo.ImportResult(projectId,selectedPath));
            _e5ExperimentCache=null;
            LoadExperiments();
            var matched=AllExperiments().FirstOrDefault(x=>
                x.RootPath.Equals(Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(selectedPath)).Replace('\\','/'),
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
