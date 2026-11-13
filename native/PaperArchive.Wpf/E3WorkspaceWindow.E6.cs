using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using PaperArchive.Core.Workspace;

namespace PaperArchive.Wpf;

/// <summary>
/// E6 research-project folder is organizational; E2 Panel linking still uses
/// exactly one stable Experiment ID, never a research-project container ID.
/// </summary>
public sealed partial class E3WorkspaceWindow
{
    private readonly ComboBox _researchSelector=new()
    {
        DisplayMemberPath="Name",MinHeight=30,
        Margin=new Thickness(0,6,0,6)
    };
    private readonly TextBlock _e6LibraryHint=new()
    {
        Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap,
        Margin=new Thickness(0,0,0,5),FontSize=12
    };
    private bool _e6Updating;

    private void InitializeE6Library(StackPanel header)
    {
        var actions=new WrapPanel{Margin=new Thickness(0,3,0,4)};
        actions.Children.Add(Action("导入研究项目",
            async (_,_)=>await ImportE6ResearchFolder()));
        actions.Children.Add(Action("导入单个实验",
            async (_,_)=>await ImportFolder()));
        header.Children.Add(actions);
        _e6LibraryHint.Text="按研究项目分组；可导入完整研究文件夹，也可单独导入实验。";
        header.Children.Add(_e6LibraryHint);
        var researchCaption=new TextBlock{Text="研究项目文件 / 说明资料",
            FontSize=12,Foreground=Brushes.Gray};
        header.Children.Add(researchCaption);
        _researchSelector.SelectionChanged+=(_,_)=>{
            if(_e6Updating)return;
            if(_researchSelector.SelectedItem is E6ResearchProject research)
                ShowE6ResearchAttachments(research);
        };
        header.Children.Add(_researchSelector);
        RefreshE6Projects();
    }

    private void ConfigureE6ExperimentGroups()
    {
        var groupStyle=new GroupStyle();
        var title=new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty,new Binding("Name"));
        title.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);
        title.SetValue(TextBlock.ForegroundProperty,Brushes.SteelBlue);
        title.SetValue(TextBlock.MarginProperty,new Thickness(4,9,0,4));
        groupStyle.HeaderTemplate=new DataTemplate{VisualTree=title};
        _experiments.GroupStyle.Add(groupStyle);
        _experiments.SetValue(VirtualizingPanel.IsVirtualizingWhenGroupingProperty,true);
    }

    private void RefreshE6Projects()
    {
        var selectedId=(_researchSelector.SelectedItem as E6ResearchProject)?.Id;
        _e6Updating=true;
        try
        {
            var projects=_repo.ListE6ResearchProjects();
            _researchSelector.ItemsSource=projects;
            _researchSelector.SelectedItem=projects.FirstOrDefault(x=>x.Id==selectedId);
        }
        finally{_e6Updating=false;}
    }

    private E1FolderIdentity[] DecorateE6Folders(E1FolderIdentity[] roots)
    {
        var research=_repo.ListE6ResearchExperiments()
            .ToDictionary(x=>x.RootId,x=>x.ResearchName,
                StringComparer.Ordinal);
        return roots.Select(root=>root with
        {
            ResearchName=research.GetValueOrDefault(root.Id,"未分组实验")
        }).OrderBy(x=>x.ResearchName,StringComparer.OrdinalIgnoreCase)
          .ThenBy(x=>x.ExperimentName,StringComparer.OrdinalIgnoreCase)
          .ToArray();
    }

    private void LoadE6Experiments(string query)
    {
        var folders=AllExperiments().Where(x=>
            x.ExperimentName.Contains(query,StringComparison.OrdinalIgnoreCase)||
            x.ResearchName.Contains(query,StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var view=CollectionViewSource.GetDefaultView(folders);
        view.GroupDescriptions.Add(new PropertyGroupDescription("ResearchName"));
        _experiments.ItemsSource=view;
    }

    private void ShowE6ResearchAttachments(E6ResearchProject research)
    {
        _experiment=null;
        _panel=null;
        _samples.ItemsSource=null;
        _preview.Source=null;
        _selection.Text="研究项目 · "+research.Name;
        _status.Text=research.ExperimentCount+" 个实验 / "+
            research.AttachmentCount+" 份研究项目资料。选取下方实验即可查看样本。";
        _files.ItemsSource=_repo.ListE6ResearchAttachments(research.Id)
            .Select(file=>new FileItem(file.RelativePath,"研究资料",null,
                file.Sha256,file.RelativePath)).ToArray();
    }

    private async Task ImportE6ResearchFolder(string? path=null)
    {
        if(path is null)
        {
            var dialog=new Microsoft.Win32.OpenFolderDialog
            {
                Title="选择研究项目文件夹（里面包含 ROS、WB 等实验文件夹）",
                Multiselect=false
            };
            if(dialog.ShowDialog(this)!=true)return;
            path=dialog.FolderName;
        }
        E6ResearchImportPreview plan;
        try{plan=await Task.Run(()=>_repo.PreviewE6ResearchImport(path));}
        catch(Exception error)
        {
            MessageBox.Show(this,error.Message,"研究目录无法导入",
                MessageBoxButton.OK,MessageBoxImage.Warning);
            return;
        }
        var summary="研究项目："+plan.Name+"\n"+
            "识别到 "+plan.ExperimentFolders.Count+" 个实验：\n"+
            string.Join("\n",plan.ExperimentFolders.Take(12)
                .Select(name=>"· "+name))+
            (plan.ExperimentFolders.Count>12?"\n…":"")+
            "\n研究项目级附件："+plan.ResearchFiles.Count+" 份"+
            "\n\n确认导入并建立关联索引？";
        if(MessageBox.Show(this,summary,"导入研究项目",
            MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)
            return;
        try
        {
            IsEnabled=false;
            var result=await Task.Run(()=>_repo.ImportE6ResearchProject(path));
            _e5ExperimentCache=null;
            RefreshE6Projects();
            LoadExperiments();
            var selected=AllExperiments().FirstOrDefault(x=>
                x.ResearchName==result.Name);
            if(selected is not null)
                _experiments.SelectedItem=_experiments.Items.Cast<E1FolderIdentity>()
                    .FirstOrDefault(x=>x.Id==selected.Id);
            _e6LibraryHint.Text="已导入 "+result.Name+" · "+
                result.ExperimentCount+" 个实验 · "+result.ImportedFiles+
                " 个新增文件。原实验/样本 ID 保持不变。";
        }
        catch(Exception error)
        {
            MessageBox.Show(this,"导入未完全完成："+error.Message+
                "\n此前成功导入的实验已安全保留；可修复问题后再次导入，系统会增量补齐。",
                "导入研究项目",MessageBoxButton.OK,MessageBoxImage.Warning);
            _e5ExperimentCache=null;
            RefreshE6Projects();LoadExperiments();
        }
        finally{IsEnabled=true;}
    }

    private async Task ImportE6DroppedFolder(string path)
    {
        var children=Directory.EnumerateDirectories(path).ToArray();
        var stageFolders=new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "raw_data","process_data","show_data","quant_data",
            "raw","process","show","quant"
        };
        var possibleResearch=children.Length>=2&&
            children.All(x=>!stageFolders.Contains(Path.GetFileName(x)))&&
            children.Count(x=>Directory.EnumerateFiles(x,"*",
                SearchOption.AllDirectories).Any())>=2;
        if(!possibleResearch)
        {
            await ImportFolder(path);
            return;
        }
        var choice=MessageBox.Show(this,
            "检测到多个实验子目录。\n"+
            "【是】按研究项目导入（每个直接子目录是独立实验）\n"+
            "【否】按一个实验导入整个文件夹。",
            "选择导入层级",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
        if(choice==MessageBoxResult.Yes)
            await ImportE6ResearchFolder(path);
        else if(choice==MessageBoxResult.No)
            await ImportFolder(path);
    }
}
