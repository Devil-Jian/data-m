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
    // PDF and Word manuscripts are stored independently of experimental results.
    var pdfPath=Path.Combine(root,"mock-manuscript.pdf");
    var wordPath=Path.Combine(root,"mock-revision.docx");
    File.WriteAllBytes(pdfPath,new byte[]{37,80,68,70,45,49,46,55});
    File.WriteAllBytes(wordPath,new byte[]{80,75,3,4,0});
    var pdfDoc=repo.ImportPaperDocument(paperId,pdfPath);
    var wordDoc=repo.ImportPaperDocument(paperId,wordPath);
    Assert(repo.ListPaperDocuments(paperId).Count==2,"manuscript storage independent of results");
    Assert(File.ReadAllBytes(repo.GetPaperDocumentPath(pdfDoc))
        .SequenceEqual(File.ReadAllBytes(pdfPath)),"PDF bytes preserved");
    Assert(File.ReadAllBytes(repo.GetPaperDocumentPath(wordDoc))
        .SequenceEqual(File.ReadAllBytes(wordPath)),"DOCX bytes preserved");
    var firstMoveResult=repo.CreateResult(workingProject,"CtrlZ target");
    var beforeUndo=repo.ListAssets(destination).First(x=>x.Stage=="raw");
    Assert(repo.MoveAssets(new[]{beforeUndo.Id},firstMoveResult)==1,"undo setup move");
    Assert(repo.CanUndoMove,"move is journaled");
    Assert(repo.UndoLastMove(),"Ctrl+Z can undo last move");
    Assert(repo.ListAssets(destination).Any(x=>x.Id==beforeUndo.Id),
           "undo restores original result and Asset ID");
    Assert(!repo.ListAssets(firstMoveResult).Any(x=>x.Id==beforeUndo.Id),
           "undo removes asset from target result");
    var wholeGroupResult=repo.CreateResult(workingProject,"Whole group undo target");
    var groupBefore=repo.ListAssets(destination).Where(x=>x.GroupPrefix=="ROS01").ToArray();
    var stableGroupId=groupBefore[0].GroupId;
    Assert(repo.MoveAssets(groupBefore.Select(x=>x.Id),wholeGroupResult)==4,
        "whole Data Group moved");
    Assert(repo.ListAssets(wholeGroupResult).All(x=>x.GroupId==stableGroupId),
        "whole-group move retains Data Group ID");
    Assert(repo.UndoLastMove(),"whole Data Group can be undone");
    Assert(repo.ListAssets(destination).Count(x=>x.GroupId==stableGroupId)==4,
        "whole Data Group restored with same ID");
    Assert(repo.ListAssets(wholeGroupResult).Count==0,"whole group target emptied by undo");
    var undoResultUnit=repo.CreateResult(workingProject,"Move result CtrlZ");
    repo.MoveResult(undoResultUnit,project);
    Assert(repo.UndoLastMove(),"undo result unit move");
    Assert(repo.ListResults(workingProject).Any(x=>x.Id==undoResultUnit),
           "result unit restored to project");
    // PRD V1.5: fixed group workspace, fast Figure/Panel placeholders, SHOW linking,
    // panel-scoped material export and immutable composite version history.
    var figuresNew=repo.CreateFiguresFast(paperId,3,2);
    Assert(figuresNew.Count==3 && figuresNew.All(f=>repo.ListPanels(f.Id).Count==2),
        "batch creation generates Figure/Panel placeholders");
    var latest=repo.CreateFiguresFast(paperId,1);
    Assert(latest.Single().Number==5,
        "quick Figure creation uses next available number without recycling an occupied one");
    var chosenNumbers=repo.CreateFiguresFast(paperId,2,1,startNumber:11);
    Assert(chosenNumbers.Select(x=>x.Number).SequenceEqual(new[]{11,12}),
        "custom starting Figure index");
    var skippedNumbers=repo.CreateFiguresFast(paperId,2,0,startNumber:11);
    Assert(skippedNumbers.Select(x=>x.Number).SequenceEqual(new[]{13,14}),
        "batch skips used Figure identifiers");
    var specifiedPanels=repo.CreatePanelsFast(chosenNumbers[0].Id,2,startLabel:"C");
    Assert(specifiedPanels.Select(x=>x.Label).SequenceEqual(new[]{"C","D"}),
        "custom starting Panel label");
    bool rollbackOnOverflow=false;
    try{repo.CreatePanelsFast(chosenNumbers[0].Id,26,startLabel:"Y");}
    catch(InvalidOperationException){rollbackOnOverflow=true;}
    Assert(rollbackOnOverflow&&repo.ListPanels(chosenNumbers[0].Id).Count==3,
        "invalid batch is completely rolled back");

    var extraPanels=repo.CreatePanelsFast(latest.Single().Id,3);
    Assert(string.Join(",",extraPanels.Select(p=>p.Label))=="A,B,C",
        "quick Panel creation uses next letter");
    var aPanel=repo.ListPanels(figuresNew[0].Id).First(x=>x.Label=="A");
    Assert(repo.LinkShowAssets(aPanel.Id,new[]{show.Id})==1,
        "SHOW asset association succeeds without file copy");
    Assert(repo.LinkShowAssets(aPanel.Id,new[]{show.Id})==0,
        "duplicate association is idempotent");
    var links=repo.ListPanelAssets(aPanel.Id);
    Assert(links.Count==1&&links[0].AssetId==show.Id&&links[0].Sha256==show.Sha256,
        "linked SHOW retains source hash and identity");
    bool rejectsRaw=false;
    try{repo.LinkShowAssets(aPanel.Id,new[]{raw.Id});}
    catch(InvalidOperationException){rejectsRaw=true;}
    Assert(rejectsRaw,"RAW asset cannot be bound as panel material");
    var exportRoot=Path.Combine(root,"figure-exports");
    var materialExport=repo.ExportFigureMaterials(figuresNew[0].Id,exportRoot);
    Assert(materialExport.ExportedCount==1 && File.Exists(materialExport.ManifestPath),
        "Figure material batch export includes manifest");
    Assert(File.ReadAllBytes(Path.Combine(exportRoot,"Panel_A",show.FileName))
        .SequenceEqual(File.ReadAllBytes(repo.GetBlobPath(show))),
        "SHOW material is exported byte-for-byte");
    var composPath=Path.Combine(root,"Figure1_final.tif");
    File.WriteAllBytes(composPath,new byte[]{73,73,42,0,1,2,3,4});
    var v1=repo.ImportFigureComposite(figuresNew[0].Id,composPath);
    File.WriteAllBytes(composPath,new byte[]{73,73,42,0,1,9,9,9});
    var v2=repo.ImportFigureComposite(figuresNew[0].Id,composPath);
    Assert(v1.Version==1 && v1.IsCurrent && v2.Version==2 && !v2.IsCurrent,
        "composites are versioned and original current persists");
    repo.SetCurrentComposite(figuresNew[0].Id,v2.Id);
    Assert(repo.ListFigureComposites(figuresNew[0].Id).Single(x=>x.IsCurrent).Id==v2.Id,
        "new composite can be chosen as current version");
    var compositeOut=repo.ExportComposite(v2,Path.Combine(root,"completed-figures"));
    Assert(File.ReadAllBytes(compositeOut).SequenceEqual(File.ReadAllBytes(composPath)),
        "composite export preserves original image bytes");
    Assert(repo.ListFigureOverview(paperId).Any(x=>x.Figure.Id==figuresNew[0].Id&&
        x.LinkedAssetCount==1&&x.CompositeCount==2),
        "Figure overview counts linked materials and full versions");
    var expectedNoFile=repo.CreateFiguresFast(paperId,1,1);
    Assert(repo.ListFigureComposites(expectedNoFile.Single().Id).Count==0 &&
        repo.ListPanelAssets(repo.ListPanels(expectedNoFile.Single().Id)[0].Id).Count==0,
        "Panel and Figure placeholders have no actual image files");
    Console.WriteLine("PASS: V1.5 figure placeholders, batch numbering, linking, material manifest, versioning, verified exports.");
    Console.WriteLine("PASS: Manuscript PDF/DOCX snapshot and persistent logical move undo.");
    Console.WriteLine("PASS: V1.3 papers, figures, panels, move, copy, stable IDs, rename, delete/restore.");
    Console.WriteLine("PASS: workspace import, naming, SHA-256, source safety, no overwrite, Figure usage, reopen, group separation.");
}
finally
{
    try{Directory.Delete(root,recursive:true);}catch{}
}
