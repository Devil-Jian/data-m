using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PaperArchive.Core.Workspace;

namespace PaperArchive.Wpf;

public partial class MainWindow
{
    private sealed record SampleExplorerRow(SampleUnit Unit)
    {
        public string Experiment=>Unit.ExperimentName;
        public string Sample=>Unit.SampleName;
        public int RAW=>Unit.RawCount;
        public int PROCESS=>Unit.ProcessCount;
        public int SHOW=>Unit.ShowCount;
        public int QUANT=>Unit.QuantCount;
        public string Summary=>Unit.ShowCount==0?"缺少 SHOW":
            Unit.ShowCount>1?"多个 SHOW 版本":"可选 SHOW";
    }
    private sealed record SampleExplorerAsset(SampleUnitAsset Asset)
    {
        public string Stage=>Asset.Stage.ToUpperInvariant();
        public string Name=>Asset.FileName;
        public string Subtype=>Asset.ProcessSubtype??"—";
        public string Version=>"v"+Asset.FileVersion;
        public string RelativePath=>Asset.RelativePath;
    }

    private void SampleUnitExplorer_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null)return;
        var workspace=_workspace;
        var projects=_activePaper is not null?
            workspace.ListProjectsForPaper(_activePaper.Id):
            workspace.ListProjects();
        if(projects.Count==0)
        {
            System.Windows.MessageBox.Show(this,"当前没有可浏览的实验项目。","样本单元");
            return;
        }
        var window=new Window{Owner=this,Title="样本实验单元 · 标签与备注",
            Width=1100,Height=725,MinWidth=840,MinHeight=520,
            WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var layout=new DockPanel{Margin=new Thickness(12)};
        var toolbar=new StackPanel{Orientation=Orientation.Horizontal,
            Margin=new Thickness(0,0,0,10)};
        toolbar.Children.Add(new TextBlock{Text="实验项目",
            VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,7,0)});
        var projectPicker=new ComboBox{Width=225,DisplayMemberPath="Name",
            ItemsSource=projects,SelectedItem=projects.FirstOrDefault(p=>p.Id==_activeProject?.Id)
                ??projects[0]};
        toolbar.Children.Add(projectPicker);
        toolbar.Children.Add(new TextBlock{Text="   筛选",
            VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4,0,6,0)});
        var filter=new TextBox{Width=190,MinHeight=29,VerticalContentAlignment=
            VerticalAlignment.Center,ToolTip="输入实验名或样本名"};
        toolbar.Children.Add(filter);
        toolbar.Children.Add(new TextBlock{Text="   标签",
            VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4,0,6,0)});
        var tagFilter=new ComboBox{Width=155,DisplayMemberPath="Name"};
        toolbar.Children.Add(tagFilter);
        DockPanel.SetDock(toolbar,Dock.Top);layout.Children.Add(toolbar);

        var foot=new TextBlock{Text="同一实验名称在同一实验项目下聚合。自动样本关联不代表已确认真实加工顺序。",
            Margin=new Thickness(4,9,4,0),Foreground=System.Windows.Media.Brushes.DimGray,
            TextWrapping=TextWrapping.Wrap};
        DockPanel.SetDock(foot,Dock.Bottom);layout.Children.Add(foot);

        var columns=new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(43,GridUnitType.Star)});
        columns.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(6)});
        columns.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(57,GridUnitType.Star)});
        var left=new DockPanel();
        var sampleTitle=new TextBlock{Text="样本单元",FontWeight=FontWeights.SemiBold,
            Margin=new Thickness(5,5,5,9)};
        DockPanel.SetDock(sampleTitle,Dock.Top);left.Children.Add(sampleTitle);
        var sampleList=new ListView{SelectionMode=SelectionMode.Single,
            BorderThickness=new Thickness(1),BorderBrush=System.Windows.Media.Brushes.LightGray};
        VirtualizingPanel.SetIsVirtualizing(sampleList,true);
        VirtualizingPanel.SetVirtualizationMode(sampleList,VirtualizationMode.Recycling);
        var sampleView=new GridView();
        foreach(var (header,path,width) in new[]{
            ("实验","Experiment",125d),("样本","Sample",100d),
            ("RAW","RAW",48d),("PROC","PROCESS",55d),
            ("SHOW","SHOW",55d),("QUANT","QUANT",60d)})
            sampleView.Columns.Add(new GridViewColumn{Header=header,
                DisplayMemberBinding=new System.Windows.Data.Binding(path),Width=width});
        sampleList.View=sampleView;
        left.Children.Add(sampleList);
        Grid.SetColumn(left,0);columns.Children.Add(left);
        var splitter=new GridSplitter{Width=6,Background=System.Windows.Media.Brushes.Gainsboro,
            HorizontalAlignment=HorizontalAlignment.Stretch};
        Grid.SetColumn(splitter,1);columns.Children.Add(splitter);

        var right=new DockPanel{Margin=new Thickness(12,0,0,0)};
        var heading=new TextBlock{Text="选择样本查看四阶段素材",FontSize=15,
            FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,5,0,9)};
        DockPanel.SetDock(heading,Dock.Top);right.Children.Add(heading);
        var editor=new StackPanel{Margin=new Thickness(0,10,0,0)};
        var noteLabel=new TextBlock{Text="样本备注",FontWeight=FontWeights.SemiBold,
            Margin=new Thickness(0,0,0,4)};
        editor.Children.Add(noteLabel);
        var noteBox=new TextBox{Height=69,TextWrapping=TextWrapping.Wrap,
            AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        editor.Children.Add(noteBox);
        var noteButton=new Button{Content="保存备注",MinWidth=92,
            HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,5,0,8)};
        editor.Children.Add(noteButton);
        var assignedTags=new TextBlock{Text="自定义标签：—",TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,0,0,7)};
        editor.Children.Add(assignedTags);
        var tagControls=new StackPanel{Orientation=Orientation.Horizontal};
        var tagPicker=new ComboBox{Width=175,DisplayMemberPath="Name"};
        var addTag=new Button{Content="添加标签",Margin=new Thickness(6,0,0,0)};
        var removeTag=new Button{Content="移除标签",Margin=new Thickness(6,0,0,0)};
        var createTag=new Button{Content="新建…",Margin=new Thickness(6,0,0,0)};
        tagControls.Children.Add(tagPicker);tagControls.Children.Add(addTag);
        tagControls.Children.Add(removeTag);tagControls.Children.Add(createTag);
        editor.Children.Add(tagControls);
        var batchTag=new Button{Content="给当前筛选结果批量添加此标签…",
            Margin=new Thickness(0,8,0,0),HorizontalAlignment=HorizontalAlignment.Left};
        editor.Children.Add(batchTag);
        var experimentNote=new Button{Content="编辑当前实验备注…",
            HorizontalAlignment=HorizontalAlignment.Left,
            Margin=new Thickness(0,10,0,0)};
        editor.Children.Add(experimentNote);
        DockPanel.SetDock(editor,Dock.Bottom);right.Children.Add(editor);

        var fileList=new ListView{SelectionMode=SelectionMode.Single,
            BorderThickness=new Thickness(1),BorderBrush=System.Windows.Media.Brushes.LightGray};
        VirtualizingPanel.SetIsVirtualizing(fileList,true);
        VirtualizingPanel.SetVirtualizationMode(fileList,VirtualizationMode.Recycling);
        var filesView=new GridView();
        foreach(var (header,path,width) in new[]{
            ("阶段","Stage",72d),("文件名","Name",220d),("PROCESS 子标签","Subtype",120d),
            ("版本","Version",55d),("所在目录","RelativePath",205d)})
            filesView.Columns.Add(new GridViewColumn{Header=header,
                DisplayMemberBinding=new System.Windows.Data.Binding(path),Width=width});
        fileList.View=filesView;
        var locate=new Button{Content="定位选中素材",Margin=new Thickness(0,8,0,0),
            HorizontalAlignment=HorizontalAlignment.Left,IsEnabled=false};
        DockPanel.SetDock(locate,Dock.Bottom);right.Children.Add(locate);
        right.Children.Add(fileList);
        Grid.SetColumn(right,2);columns.Children.Add(right);
        layout.Children.Add(columns);window.Content=layout;
        SampleUnit? chosenUnit=null;
        SampleUnitAsset? targetAsset=null;
        string? targetProject=null;

        string? Ask(string title,string description,string initial="",bool multiline=false)
        {
            var prompt=new Window{Owner=window,Title=title,Width=510,
                Height=multiline?310:195,ResizeMode=ResizeMode.NoResize,
                WindowStartupLocation=WindowStartupLocation.CenterOwner};
            var body=new DockPanel{Margin=new Thickness(15)};
            var actions=new StackPanel{Orientation=Orientation.Horizontal,
                HorizontalAlignment=HorizontalAlignment.Right,
                Margin=new Thickness(0,12,0,0)};
            var okay=new Button{Content="确定",MinWidth=80,IsDefault=true};
            var cancel=new Button{Content="取消",MinWidth=80,IsCancel=true,
                Margin=new Thickness(8,0,0,0)};
            actions.Children.Add(okay);actions.Children.Add(cancel);
            DockPanel.SetDock(actions,Dock.Bottom);body.Children.Add(actions);
            var hint=new TextBlock{Text=description,TextWrapping=TextWrapping.Wrap,
                Margin=new Thickness(0,0,0,10)};
            DockPanel.SetDock(hint,Dock.Top);body.Children.Add(hint);
            var field=new TextBox{Text=initial,TextWrapping=multiline?
                TextWrapping.Wrap:TextWrapping.NoWrap,AcceptsReturn=multiline,
                VerticalScrollBarVisibility=multiline?ScrollBarVisibility.Auto:
                    ScrollBarVisibility.Disabled};
            body.Children.Add(field);prompt.Content=body;
            okay.Click+=(_,_)=>prompt.DialogResult=true;
            prompt.Loaded+=(_,_)=>{field.Focus();field.SelectAll();};
            return prompt.ShowDialog()==true?field.Text.Trim():null;
        }

        void ReloadTagChoices()
        {
            var selected=(tagPicker.SelectedItem as CustomTag)?.Id;
            var filterSelected=(tagFilter.SelectedItem as CustomTag)?.Id;
            var tags=workspace.ListCustomTags();
            tagPicker.ItemsSource=tags;
            tagPicker.SelectedItem=tags.FirstOrDefault(x=>x.Id==selected)
                ??tags.FirstOrDefault();
            tagFilter.ItemsSource=new[]{new CustomTag("","全部标签")}.Concat(tags).ToArray();
            tagFilter.SelectedItem=tagFilter.Items.Cast<CustomTag>()
                .FirstOrDefault(x=>x.Id==filterSelected)??tagFilter.Items[0];
        }
        void ReloadSamples(string? keepId=null)
        {
            if(projectPicker.SelectedItem is not ExperimentProject project)return;
            var all=workspace.ListSampleUnits(projectId:project.Id);
            var term=filter.Text.Trim();
            var tag=(tagFilter.SelectedItem as CustomTag)?.Id;
            var tagged=string.IsNullOrEmpty(tag)?null:
                workspace.ListTaggedSampleIds(project.Id,tag);
            var rows=all.Where(x=>
                (term.Length==0 || x.SampleName.Contains(term,StringComparison.OrdinalIgnoreCase)
                    || x.ExperimentName.Contains(term,StringComparison.OrdinalIgnoreCase))
                && (tagged is null || tagged.Contains(x.Id)))
                .Select(x=>new SampleExplorerRow(x)).ToArray();
            sampleList.ItemsSource=rows;
            sampleTitle.Text=$"样本单元 · {rows.Length} 组";
            sampleList.SelectedItem=rows.FirstOrDefault(x=>x.Unit.Id==keepId)
                ??rows.FirstOrDefault();
        }
        void ReloadSelection()
        {
            chosenUnit=(sampleList.SelectedItem as SampleExplorerRow)?.Unit;
            fileList.ItemsSource=null;
            noteBox.Text="";
            assignedTags.Text="自定义标签：—";
            noteButton.IsEnabled=chosenUnit is not null;
            addTag.IsEnabled=chosenUnit is not null;
            removeTag.IsEnabled=chosenUnit is not null;
            experimentNote.IsEnabled=chosenUnit is not null;
            if(chosenUnit is null)
            {heading.Text="选择样本查看四阶段素材";return;}
            heading.Text=chosenUnit.ExperimentName+" / "+chosenUnit.SampleName;
            fileList.ItemsSource=workspace.ListSampleUnitAssets(chosenUnit.Id)
                .Select(x=>new SampleExplorerAsset(x)).ToArray();
            var annotation=workspace.GetEntityAnnotation("sample",chosenUnit.Id);
            noteBox.Text=annotation.Note;
            assignedTags.Text="自定义标签："+(annotation.Tags.Count==0?"—":
                string.Join("、",annotation.Tags.Select(x=>x.Name)));
        }
        sampleList.SelectionChanged+=(_,_)=>ReloadSelection();
        projectPicker.SelectionChanged+=(_,_)=>ReloadSamples();
        filter.TextChanged+=(_,_)=>ReloadSamples();
        tagFilter.SelectionChanged+=(_,_)=>ReloadSamples(chosenUnit?.Id);
        noteButton.Click+=(_,_)=>{
            if(chosenUnit is null)return;
            try{workspace.SetEntityNote("sample",chosenUnit.Id,noteBox.Text);
                foot.Text="备注已保存。";}catch(Exception ex){ShowError(ex);}
        };
        addTag.Click+=(_,_)=>{
            if(chosenUnit is null||tagPicker.SelectedItem is not CustomTag tag)return;
            try{workspace.SetEntityTag("sample",chosenUnit.Id,tag.Id,true);
                ReloadSelection();}catch(Exception ex){ShowError(ex);}
        };
        removeTag.Click+=(_,_)=>{
            if(chosenUnit is null||tagPicker.SelectedItem is not CustomTag tag)return;
            try{workspace.SetEntityTag("sample",chosenUnit.Id,tag.Id,false);
                ReloadSelection();}catch(Exception ex){ShowError(ex);}
        };
        batchTag.Click+=(_,_)=>{
            if(tagPicker.SelectedItem is not CustomTag tag ||
                sampleList.ItemsSource is not IEnumerable<SampleExplorerRow> visible)return;
            var ids=visible.Select(x=>x.Unit.Id).Distinct(StringComparer.Ordinal).ToArray();
            if(ids.Length==0)return;
            if(System.Windows.MessageBox.Show(window,
                $"将标签“{tag.Name}”应用到当前筛选的 {ids.Length} 个样本？"+
                "\\n不会修改系统标签、文件或加工来源。",
                "批量添加自定义标签",MessageBoxButton.YesNo,
                MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            try
            {
                var added=workspace.AssignCustomTagBatch("sample",ids,tag.Id);
                foot.Text=$"已为 {added} 个样本新增标签；原有相同标签保持不变。";
                var id=chosenUnit?.Id;
                ReloadSamples(id);
            }
            catch(Exception ex){ShowError(ex);}
        };
        createTag.Click+=(_,_)=>{
            var name=Ask("新建自定义标签","标签名称（可复用于多个样本）");
            if(name is null)return;
            try{var tag=workspace.CreateCustomTag(name);
                ReloadTagChoices();tagPicker.SelectedItem=workspace.ListCustomTags()
                    .First(x=>x.Id==tag.Id);
            }catch(Exception ex){ShowError(ex);}
        };
        experimentNote.Click+=(_,_)=>{
            if(chosenUnit is null)return;
            try
            {
                var existing=workspace.GetEntityAnnotation("experiment",chosenUnit.ExperimentId).Note;
                var note=Ask("实验备注 · "+chosenUnit.ExperimentName,
                    "填写当前实验单元的整体备注",existing,multiline:true);
                if(note is not null)
                    workspace.SetEntityNote("experiment",chosenUnit.ExperimentId,note);
            }
            catch(Exception ex){ShowError(ex);}
        };
        fileList.SelectionChanged+=(_,_)=>locate.IsEnabled=
            fileList.SelectedItem is SampleExplorerAsset;
        void Navigate()
        {
            if(fileList.SelectedItem is not SampleExplorerAsset item ||
                projectPicker.SelectedItem is not ExperimentProject project)return;
            targetAsset=item.Asset;
            targetProject=project.Id;
            window.DialogResult=true;
        }
        locate.Click+=(_,_)=>Navigate();
        fileList.MouseDoubleClick+=(_,_)=>Navigate();
        ReloadTagChoices();
        ReloadSamples();
        if(window.ShowDialog()!=true || targetAsset is null || targetProject is null)return;
        if(_libraryMode)WorkspaceTab.IsChecked=true;
        SearchBox.Clear();_columnFilters.Clear();FolderList.SelectedIndex=0;
        var result=workspace.ListResults(targetProject)
            .FirstOrDefault(x=>x.Id==targetAsset.ResultId);
        if(result is null||!SelectTreeTag(result))return;
        var row=_visible.FirstOrDefault(x=>x.Asset.Id==targetAsset.AssetId);
        if(row is null){LoadScopeAssets();
            row=_visible.FirstOrDefault(x=>x.Asset.Id==targetAsset.AssetId);}
        if(row is null)return;
        FileList.SelectedItem=row;ThumbnailList.SelectedItem=row;
        FileList.ScrollIntoView(row);
        UpdateDetails();
    }
}
