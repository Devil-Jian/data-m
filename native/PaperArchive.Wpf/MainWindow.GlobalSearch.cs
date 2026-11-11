using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PaperArchive.Core.Workspace;

namespace PaperArchive.Wpf;

public partial class MainWindow
{
    private sealed record UnassignedProjectsNode();
    private sealed record GlobalSearchRow(GlobalAssetSearchHit Hit)
    {
        public string FileName=>Hit.Asset.FileName;
        public string Stage=>Hit.Asset.Stage.ToUpperInvariant();
        public string Sample=>Hit.Asset.GroupPrefix;
        public string Paper=>Hit.PaperName??"未关联论文";
        public string Location=>Hit.ProjectName+" / "+Hit.ResultName;
        public string RelativePath=>Hit.Asset.RelativePath;
    }

    private void GlobalSearch_Click(object sender,RoutedEventArgs e)
    {
        if(_workspace is null)return;
        var dialog=new Window{Owner=this,Title="全库查找 · 跨论文实验素材",
            Width=1020,Height=635,MinWidth=720,MinHeight=420,
            WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var root=new DockPanel{Margin=new Thickness(14)};
        var head=new DockPanel{LastChildFill=true,Margin=new Thickness(0,0,0,10)};
        var go=new Button{Content="查找",MinWidth=86,Margin=new Thickness(8,0,0,0)};
        DockPanel.SetDock(go,Dock.Right);head.Children.Add(go);
        var input=new TextBox{Text=SearchBox.Text.Trim(),MinHeight=31,
            VerticalContentAlignment=VerticalAlignment.Center,
            ToolTip="按文件名、样本、路径、实验项目、实验结果、论文名或 Asset ID 查找"};
        head.Children.Add(input);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
        var footer=new DockPanel{Margin=new Thickness(0,10,0,0),LastChildFill=false};
        var prev=new Button{Content="上一页",MinWidth=82,IsEnabled=false,Margin=new Thickness(6,0,0,0)};
        var next=new Button{Content="下一页",MinWidth=82,IsEnabled=false,Margin=new Thickness(6,0,0,0)};
        var locate=new Button{Content="定位选中文件",MinWidth=130,IsEnabled=false,Margin=new Thickness(12,0,0,0)};
        foreach(var button in new[]{locate,next,prev}){DockPanel.SetDock(button,Dock.Right);footer.Children.Add(button);}
        var status=new TextBlock{Text="输入至少两个字符，按 Enter 开始跨论文查找。",
            VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
        footer.Children.Add(status);DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var list=new ListView{SelectionMode=SelectionMode.Single};
        var view=new GridView();
        foreach(var (header,path,width) in new[]{
            ("文件名","FileName",215d),("阶段","Stage",80d),
            ("样本","Sample",120d),("论文","Paper",145d),
            ("实验项目 / 结果","Location",185d),("路径","RelativePath",230d)})
            view.Columns.Add(new GridViewColumn{Header=header,DisplayMemberBinding=
                new System.Windows.Data.Binding(path),Width=width});
        list.View=view;root.Children.Add(list);
        dialog.Content=root;
        const int pageSize=100;
        var page=0;
        var requestId=0;
        async System.Threading.Tasks.Task Search(int desiredPage)
        {
            var query=input.Text.Trim();
            if(query.Length<2){status.Text="至少输入 2 个字符。";return;}
            if(query.Length>200){status.Text="搜索词不能超过 200 个字符。";return;}
            var request=++requestId;
            go.IsEnabled=false;prev.IsEnabled=false;next.IsEnabled=false;
            status.Text="查询中…";
            try
            {
                // A new connection is opened on the worker thread. UI controls
                // and observable collections are touched only after await.
                var workspace=_workspace;
                if(workspace is null)return;
                var results=await System.Threading.Tasks.Task.Run(()=>
                    workspace.SearchAssetsGlobal(query,pageSize+1,desiredPage*pageSize));
                if(request!=requestId || !dialog.IsVisible)return;
                page=desiredPage;
                list.ItemsSource=results.Take(pageSize).Select(x=>new GlobalSearchRow(x)).ToArray();
                prev.IsEnabled=page>0;
                next.IsEnabled=results.Count>pageSize;
                status.Text=$"“{query}” · 第 {page+1} 页 · 本页 {Math.Min(results.Count,pageSize)} 条"+
                    (results.Count>pageSize?" · 还有下一页":" · 已到末页");
            }
            catch(Exception ex)
            {
                if(request==requestId)status.Text="查询失败："+ex.Message;
            }
            finally{if(request==requestId)go.IsEnabled=true;}
        }
        go.Click+=async (_,_)=>await Search(0);
        prev.Click+=async (_,_)=>await Search(Math.Max(0,page-1));
        next.Click+=async (_,_)=>await Search(page+1);
        input.KeyDown+=async (_,args)=>{
            if(args.Key==Key.Enter){args.Handled=true;await Search(0);}
        };
        list.SelectionChanged+=(_,_)=>locate.IsEnabled=list.SelectedItem is GlobalSearchRow;
        GlobalAssetSearchHit? selected=null;
        locate.Click+=(_,_)=>{
            if(list.SelectedItem is not GlobalSearchRow row)return;
            selected=row.Hit;dialog.DialogResult=true;
        };
        list.MouseDoubleClick+=(_,_)=>{
            if(list.SelectedItem is not GlobalSearchRow row)return;
            selected=row.Hit;dialog.DialogResult=true;
        };
        dialog.Loaded+=async (_,_)=>{
            input.Focus();input.SelectAll();
            if(input.Text.Trim().Length>=2)await Search(0);
        };
        if(dialog.ShowDialog()==true && selected is not null)
            LocateGlobalSearchHit(selected);
    }

    private void LocateGlobalSearchHit(GlobalAssetSearchHit hit)
    {
        if(_workspace is null)return;
        if(_libraryMode)WorkspaceTab.IsChecked=true;
        SearchBox.Clear();
        _columnFilters.Clear();
        FolderList.SelectedIndex=0;
        var result=_workspace.ListResults(hit.ProjectId)
            .FirstOrDefault(x=>x.Id==hit.Asset.ResultId);
        if(result is null||!SelectTreeTag(result))
        {
            System.Windows.MessageBox.Show(this,"搜索结果已变化，请重新查找。","全库查找");
            return;
        }
        var row=_visible.FirstOrDefault(x=>x.Asset.Id==hit.Asset.Id);
        if(row is null)
        {
            // The selected result may have been updated since the query.
            LoadScopeAssets();
            row=_visible.FirstOrDefault(x=>x.Asset.Id==hit.Asset.Id);
        }
        if(row is null)return;
        FileList.SelectedItem=row;
        FileList.ScrollIntoView(row);
        ThumbnailList.SelectedItem=row;
        UpdateDetails();
    }
}
