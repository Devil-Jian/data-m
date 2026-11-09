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
    Console.WriteLine("PASS: workspace import, naming, SHA-256, source safety, no overwrite, Figure usage, reopen, group separation.");
}
finally
{
    try{Directory.Delete(root,recursive:true);}catch{}
}
