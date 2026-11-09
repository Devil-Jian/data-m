using System.Security.Cryptography;
using PaperArchive.Core.Workspace;

static void Assert(bool condition,string description)
{
    if(!condition)throw new Exception("WORKSPACE TEST FAILED: " + description);
}

var root=Path.Combine(Path.GetTempPath(),"paperarchive-workspace-"+Guid.NewGuid().ToString("N"));
try
{
    var source=Path.Combine(root,"source","ROS检测");
    foreach(var stage in new[]{"raw_data","process_data","show_data"})
        Directory.CreateDirectory(Path.Combine(source,stage));
    byte[] original=[1,2,3,4,5,6,7];
    File.WriteAllBytes(Path.Combine(source,"raw_data","ROS01_raw.tif"),original);
    File.WriteAllBytes(Path.Combine(source,"process_data","ROS01_crop.tif"),[8,9]);
    File.WriteAllBytes(Path.Combine(source,"process_data","ROS01_adjusted.tif"),[10,11]);
    File.WriteAllBytes(Path.Combine(source,"show_data","ROS01_show.png"),[12,13,14]);
    File.WriteAllText(Path.Combine(source,"ROS统计.csv"),"group,mean\nA,3.4");
    Assert(ExperimentWorkspace.DetectPrefix("show_data/ROS01_show.png")=="ROS01","prefix show");
    Assert(ExperimentWorkspace.DetectPrefix("process_data/ROS_Control_01_adjusted_v2.tif")=="ROS_Control_01","compound prefix");
    Assert(ExperimentWorkspace.DetectStage("nested/process_data/ROS01_crop.tif")=="process","nested stage");

    var repo=ExperimentWorkspace.Open(Path.Combine(root,"warehouse"));
    var project=repo.CreateProject("实验项目 A");
    var imported=repo.ImportResult(project,source);
    Assert(imported.FileCount==5 && imported.DataGroupCount==2,"import metadata");
    var assets=repo.ListAssets(imported.ResultId);
    var ros=assets.Where(a=>a.GroupPrefix=="ROS01").ToArray();
    Assert(ros.Length==4,"group retains raw/process/show versions");
    Assert(ros.Select(x=>x.GroupId).Distinct().Count()==1,"stable group identity");
    Assert(ros.Count(x=>x.Stage=="process")==2,"multiple processed variants");
    var raw=ros.Single(x=>x.Stage=="raw");
    Assert(File.ReadAllBytes(repo.GetBlobPath(raw)).SequenceEqual(original),"byte perfect snapshot");
    Assert(File.ReadAllBytes(Path.Combine(source,"raw_data","ROS01_raw.tif")).SequenceEqual(original),"source unchanged");

    var show=ros.Single(x=>x.Stage=="show");
    var exports=Path.Combine(root,"figures");
    var usage=repo.ExportForFigure(show,"Paper A",2,"a",exports);
    Assert(usage.State=="exported" && usage.Panel=="A","usage created");
    Assert(File.ReadAllBytes(usage.ExportPath).SequenceEqual(new byte[] { 12, 13, 14 }),"export is lossless");
    var second=repo.ExportOriginal(show,exports);
    Assert(second!=usage.ExportPath && File.Exists(second),"no silent overwrite");
    repo.SetUsageState(usage.Id,"confirmed");
    Assert(repo.ListUsages(show.Id).Single().State=="confirmed","confirmation saved");
    Assert(ExperimentWorkspace.Open(repo.Root).ListProjects().Single().Id==project,"reopen identity");
    Assert(ExperimentWorkspace.Open(repo.Root).ListResults(project).Single().FileCount==5,"reopen assets");

    var source2=Path.Combine(root,"source","独立实验");
    Directory.CreateDirectory(Path.Combine(source2,"show_data"));
    File.WriteAllBytes(Path.Combine(source2,"show_data","ROS01_show.png"),[12,13,14]);
    var another=repo.ImportResult(project,source2);
    Assert(another.DataGroupCount==1 && repo.ListAssets(another.ResultId).Single().GroupId!=show.GroupId,
        "identical prefix in different results must remain distinct");
    // PRD v1.3: paper -> experiment -> result, stable move/copy IDs, trash and templates.
    var paperId=repo.CreatePaperProject("氧化应激论文工作区");
    var workingProject=repo.CreateProjectForPaper(paperId,"新实验项目");
    Assert(repo.ListProjectsForPaper(paperId).Single().Id==workingProject,"paper/project hierarchy");
    var figures=repo.CreateFigure(paperId);
    var fig=repo.ListFigures(paperId).Single();
    Assert(fig.Id==figures && fig.Number==1,"Figure auto numbering");
    repo.CreatePanel(fig.Id,"a");
    Assert(repo.ListPanels(fig.Id).Single().Label=="A","Panel template");
    var destination=repo.CreateResult(workingProject,"重新整理的 ROS");
    var moved=repo.MoveAssets(ros.Select(x=>x.Id),destination);
    Assert(moved==4 && repo.ListAssets(destination).Count==4,"logical move entire group");
    var movedRos=repo.ListAssets(destination);
    Assert(movedRos.Single(x=>x.Stage=="raw").Id==raw.Id &&
           movedRos.Single(x=>x.Stage=="show").Sha256==show.Sha256,
           "move preserves asset identity and contents");
    Assert(movedRos.All(x=>x.GroupId==show.GroupId),"complete group move preserves group id");
    var copiedResult=repo.CreateResult(workingProject,"复制实验结果");
    repo.CopyAssets(movedRos.Select(x=>x.Id),copiedResult);
    var copies=repo.ListAssets(copiedResult);
    Assert(copies.Count==4 && copies.All(x=>!movedRos.Any(y=>y.Id==x.Id)),
           "logical copy creates independent Asset IDs");
    Assert(copies.Select(x=>x.Sha256).Order().SequenceEqual(
           movedRos.Select(x=>x.Sha256).Order()),"logical copy shares immutable bytes");
    repo.RenameAsset(copies[0].Id,"renamed.tif");
    Assert(repo.ListAssets(copiedResult).Any(x=>x.Id==copies[0].Id&&x.FileName=="renamed.tif"),
           "rename preserves ID");
    repo.SetAssetDeleted([copies[0].Id],true);
    Assert(repo.ListAssets(copiedResult).Count==3 &&
           repo.ListDeletedAssets(copiedResult).Count==1,"soft delete");
    repo.SetAssetDeleted([copies[0].Id],false);
    Assert(repo.ListAssets(copiedResult).Count==4,"restore");
    repo.RenameResult(destination,"ROS 汇总");
    Assert(repo.ListResults(workingProject).Any(x=>x.Id==destination&&x.Name=="ROS 汇总"),
           "result rename");
    var copiedUnit=repo.CopyResult(destination,workingProject,"ROS 复制");
    Assert(repo.ListResults(workingProject).Any(x=>x.Id==copiedUnit),"result unit copy");
    Assert(repo.ListAssets(copiedUnit).Count==4,"result copy contents");
    repo.MoveResult(copiedUnit,project);
    Assert(repo.ListResults(project).Any(x=>x.Id==copiedUnit),"result reparent between projects");
    repo.SetResultDeleted(copiedUnit,true);
    Assert(repo.ListResults(project).All(x=>x.Id!=copiedUnit) &&
           repo.ListDeletedResults(project).Any(x=>x.Id==copiedUnit),"result soft delete");
    repo.SetResultDeleted(copiedUnit,false);
    Assert(repo.ListResults(project).Any(x=>x.Id==copiedUnit),"restore result");
    var blankProject=repo.CreateProjectForPaper(paperId,"暂存实验项目");
    repo.SetProjectDeleted(blankProject,true);
    Assert(repo.ListProjectsForPaper(paperId).All(x=>x.Id!=blankProject) &&
           repo.ListDeletedProjects(paperId).Any(x=>x.Id==blankProject),"project soft delete");
    repo.SetProjectDeleted(blankProject,false);
    Assert(repo.ListProjectsForPaper(paperId).Any(x=>x.Id==blankProject),"restore project");
    var scopedPaper=repo.ListScopeAssets(paperId:paperId);
    Assert(scopedPaper.Any(x=>x.Asset.ResultId==destination &&
        x.ResultName=="ROS 汇总"),"paper scope lists nested result assets");
    var scopedProject=repo.ListScopeAssets(projectId:workingProject);
    Assert(scopedProject.Count==repo.ListResults(workingProject).Sum(
        x=>repo.ListAssets(x.Id).Count),"project scope file count");
    Assert(repo.ListScopeAssets(resultId:destination).Count==4,"result scope");
    var pending=repo.GetOrCreateUnsortedResult(workingProject);
    Assert(pending==repo.GetOrCreateUnsortedResult(workingProject) &&
        repo.ListResults(workingProject).Any(x=>x.Id==pending&&x.Name=="待整理数据"),
        "project drop targets stable unsorted result");
    var pendingPaper=repo.ListScopeAssets(paperId:paperId);
    Assert(pendingPaper.All(x=>x.Asset.Sha256.Length==64),"scoped hashes");
    Assert(repo.ListPaperProjects().Any(x=>x.Id==paperId),"paper persists");
    repo.AssignUsageToPaper(usage.Id,paperId);
    Assert(repo.ListUsages(show.Id).Single().PaperTitle=="Paper A",
           "usage association retains prior Figure label");
    Console.WriteLine("PASS: V1.3 papers, figures, panels, move, copy, stable IDs, rename, delete/restore.");
    Console.WriteLine("PASS: workspace import, naming, SHA-256, source safety, no overwrite, Figure usage, reopen, group separation.");
}
finally
{
    try{Directory.Delete(root,recursive:true);}catch{}
}
