using System.Security.Cryptography;
using PaperArchive.Core.Workspace;
using PaperArchive.Core;

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
    // V15-15: document -> exact composite -> immutable Panel/SHOW -> PROCESS -> RAW.
    var capturedV1=repo.ListCompositeSources(v1.Id);
    Assert(capturedV1.Count==1 && capturedV1[0].AssetId==show.Id &&
        capturedV1[0].Status=="candidate",
        "Figure import snapshots candidate SHOW sources");
    repo.ConfirmCompositeSources(v1.Id);
    Assert(repo.ListCompositeSources(v1.Id).Single().Status=="confirmed",
        "Figure source snapshot can be explicitly verified");
    var process=ros.First(x=>x.Stage=="process");
    repo.SetAssetProvenance(process.Id,show.Id,"图像处理输出",true);
    repo.SetAssetProvenance(raw.Id,process.Id,"原始图像裁剪",true);
    Assert(repo.ListAssetUpstream(show.Id).Single().SourceAssetId==process.Id &&
        repo.ListAssetUpstream(process.Id).Single().SourceAssetId==raw.Id,
        "explicit SHOW to PROCESS to RAW provenance");
    Assert(repo.ListSameGroupUpstreamCandidates(show.Id).Count==2,
        "same-group PROCESS files remain unverified candidates");
    repo.SetPaperFigureLink(pdfDoc.Id,figuresNew[0].Id,v1.Id,true);
    var pinned=repo.ListPaperFigureLinks(pdfDoc.Id).Single();
    Assert(pinned.Version==1 && pinned.Status=="confirmed",
        "manuscript pins exact final Figure version");
    Assert(repo.DescribeDocumentProvenance(pdfDoc.Id).Contains("Panel A") &&
        repo.DescribeDocumentProvenance(pdfDoc.Id).Contains(process.FileName),
        "manuscript provenance traverses full Figure to RAW source");
    var rejectedCrossPaper=false;
    try{repo.SetPaperFigureLink(pdfDoc.Id,repo.CreateFiguresFast(
        repo.CreatePaperProject("另一篇论文"),1)[0].Id,null,false);}
    catch(InvalidOperationException){rejectedCrossPaper=true;}
    Assert(rejectedCrossPaper,"cross-paper Figure links are rejected");
    var detectedPath=Path.Combine(root,"figure-reference-test.docx");
    using(var package=System.IO.Compression.ZipFile.Open(detectedPath,
        System.IO.Compression.ZipArchiveMode.Create))
    {
        var docEntry=package.CreateEntry("word/document.xml");
        using var writer=new StreamWriter(docEntry.Open());
        writer.Write("<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:r><w:t>As shown in Figure 3A and Figure 2.</w:t></w:r></w:p></w:body></w:document>");
    }
    var detectedDoc=repo.ImportPaperDocument(paperId,detectedPath);
    Assert(repo.SuggestDocumentFigures(detectedDoc)==2 &&
        repo.ListPaperFigureLinks(detectedDoc.Id).All(x=>x.Status=="candidate"&&x.CompositeId is null),
        "DOCX Figure references are candidate-only, not asserted final versions");
    repo.SetCurrentComposite(figuresNew[0].Id,v2.Id);
    Assert(repo.ListPaperFigureLinks(pdfDoc.Id).Single().CompositeId==v1.Id,
        "workspace current version does not rewrite manuscript's pinned version");
    var rejectedDelete=false;
    try{repo.DeleteFigureCompositeVersion(v1.Id);}
    catch(InvalidOperationException){rejectedDelete=true;}
    Assert(rejectedDelete,"document-referenced composite versions cannot be deleted");
    Assert(ExperimentWorkspace.Open(repo.Root).ListCompositeSources(v1.Id).Count==1 &&
        ExperimentWorkspace.Open(repo.Root).ListPaperFigureLinks(pdfDoc.Id).Single().CompositeId==v1.Id,
        "verified sources and document version links persist after reopening");
    // V15-14: Figure show_data is the same versioned source of truth,
    // not a copied experiment SHOW asset or a newly owned experiment result.
    var showData=repo.ListFigureShowData(figuresNew[0].Id);
    Assert(showData.Count==2 && showData[0].Id==v2.Id &&
        showData.Single(x=>x.IsCurrent).Id==v2.Id,
        "completed Figure imported into logical show_data with current version");
    Assert(repo.ListPanelAssets(aPanel.Id).Single().AssetId==show.Id &&
        repo.ListPanelAssets(aPanel.Id).Single().Sha256==show.Sha256,
        "Figure import does not replace source Panel SHOW image");
    Assert(ExperimentWorkspace.Open(repo.Root).ListFigureShowData(figuresNew[0].Id)
        .Select(x=>x.Id).SequenceEqual(showData.Select(x=>x.Id)),
        "Figure show_data versions survive workspace reopen");
    var compositeOut=repo.ExportComposite(v2,Path.Combine(root,"completed-figures"));
    Assert(File.ReadAllBytes(compositeOut).SequenceEqual(File.ReadAllBytes(composPath)),
        "composite export preserves original image bytes");
    Assert(repo.ListFigureOverview(paperId).Any(x=>x.Figure.Id==figuresNew[0].Id&&
        x.LinkedAssetCount==1&&x.CompositeCount==2),
        "Figure overview counts linked materials and full versions");
    // V15-14 composite filename changes are metadata-only; removal promotes
    // the previous version without touching shared content-addressed bytes.
    var sourceHash=v2.Sha256;
    repo.RenameFigureComposite(v2.Id,"Figure1_revision.tif");
    Assert(repo.ListFigureShowData(figuresNew[0].Id).Single(x=>x.Id==v2.Id)
        .FileName=="Figure1_revision.tif",
        "Figure composite rename updates only display filename");
    var storedBlob=Path.Combine(repo.Root,"blobs","sha256",sourceHash[..2],sourceHash);
    repo.DeleteFigureCompositeVersion(v2.Id);
    Assert(repo.ListFigureShowData(figuresNew[0].Id).Count==1 &&
        repo.ListFigureShowData(figuresNew[0].Id).Single().Id==v1.Id &&
        repo.ListFigureShowData(figuresNew[0].Id).Single().IsCurrent,
        "delete current Figure version promotes the remaining version");
    Assert(repo.ListFigureOverview(paperId).Any(x=>x.Figure.Id==figuresNew[0].Id &&
        x.CompositeCount==1), "Figure overview updates after version removal");
    Assert(File.Exists(storedBlob) && repo.ListPanelAssets(aPanel.Id).Single().AssetId==show.Id,
        "version removal preserves blob and original Panel association");
    repo.UnlinkPanelAsset(links.Single().Id);
    Assert(repo.ListPanelAssets(aPanel.Id).Count==0 &&
        repo.ListCompositeSources(v1.Id).Single().AssetId==show.Id &&
        repo.ListCompositeSources(v1.Id).Single().Status=="confirmed",
        "unlinking current Panel SHOW preserves confirmed historical composite source");
    Assert(repo.DescribeDocumentProvenance(pdfDoc.Id).Contains(show.FileName),
        "manuscript lineage remains readable after live Panel unlink");
    // V15-16: archive only the exact confirmed manuscript Figure graph.
    var archive=ArchiveRepository.Open(repo.Root);
    var preview=archive.PrepareWorkspaceArchive(repo,paperId,pdfDoc.Id);
    Assert(preview.CanPublish && preview.FigureCount==1 &&
        preview.Resources.Any(x=>x.Role=="manuscript") &&
        preview.Resources.Any(x=>x.Role=="composite") &&
        preview.Resources.Any(x=>x.Role=="show") &&
        preview.Resources.Any(x=>x.Role=="process") &&
        preview.Resources.Any(x=>x.Role=="raw"),
        "archive preflight includes a complete verified paper-to-RAW graph");
    var archived=archive.PublishWorkspaceArchive(preview);
    Assert(!archived.AlreadyArchived && archived.Revision==1 &&
        archive.ListPapers().Any(p=>p.Id==archived.PaperId&&p.Status=="archived"),
        "workspace paper published in resource library");
    Assert(archive.ListResources(archived.PaperId).Count==preview.Resources.Count &&
        archive.GetWorkspaceArchiveManifest(archived.PaperId,1)==preview.ManifestJson,
        "archive has immutable resource list and exact relation manifest");
    Assert(archive.PublishWorkspaceArchive(preview).AlreadyArchived &&
        archive.ListWorkspaceRevisions(archived.PaperId).Count==1,
        "repeated archive of same manifest is idempotent");
    var v3=repo.ImportFigureComposite(figuresNew[0].Id,composPath);
    repo.SetPaperFigureLink(pdfDoc.Id,figuresNew[0].Id,v3.Id,true);
    var revisionPreview=archive.PrepareWorkspaceArchive(repo,paperId,pdfDoc.Id);
    var revision=archive.PublishWorkspaceArchive(revisionPreview,true);
    Assert(revision.PaperId==archived.PaperId && revision.Revision==2 &&
        archive.ListWorkspaceRevisions(archived.PaperId).Count==2 &&
        archive.GetWorkspaceArchiveManifest(archived.PaperId,1)==preview.ManifestJson,
        "subsequent edits create revision 2 without changing revision 1");
    var incomplete=archive.PrepareWorkspaceArchive(repo,paperId,wordDoc.Id);
    Assert(!incomplete.CanPublish,"manuscript without Figure links blocks archive");
    bool blocked=false;
    try{archive.PublishWorkspaceArchive(incomplete);}catch(InvalidOperationException){blocked=true;}
    Assert(blocked,"blocked archive cannot be published");
    Assert(ArchiveRepository.Open(repo.Root).ListWorkspaceArchiveResources(archived.PaperId,1).Count==
        preview.Resources.Count,"archived revision persists after reopen");
    // V1.6-A: task state derives from existing objects instead of a manual
    // completion checkbox. One-time primary document choice survives reopen.
    var beforePrimary=repo.GetPaperWorkSummary(paperId);
    Assert(beforePrimary.PrimaryDocument is null &&
        beforePrimary.Tasks.Any(x=>x.Code=="select_primary"&&x.BlocksArchive),
        "multiple manuscript drafts require one explicit primary selection");
    repo.SetPrimaryPaperDocument(paperId,pdfDoc.Id);
    var taskSummary=repo.GetPaperWorkSummary(paperId);
    Assert(taskSummary.PrimaryDocument?.Id==pdfDoc.Id &&
        taskSummary.DocumentCount==3 &&
        taskSummary.ConfirmedFigureCount==1 &&
        taskSummary.BlockingCount==0,
        "task engine follows the confirmed paper graph without manual completion flags");
    Assert(ExperimentWorkspace.Open(repo.Root).GetPrimaryPaperDocument(paperId)?.Id==pdfDoc.Id,
        "primary manuscript choice is persistent across workspace reopen");
    var invalidPrimary=false;
    try{repo.SetPrimaryPaperDocument(paperId,"invalid-document");}
    catch(InvalidOperationException){invalidPrimary=true;}
    Assert(invalidPrimary && repo.GetPrimaryPaperDocument(paperId)?.Id==pdfDoc.Id,
        "cross-paper or missing manuscript cannot become primary");
    var unfinished=repo.CreatePaperProject("V16 未完成论文");
    Assert(repo.GetPaperWorkSummary(unfinished).Tasks.Any(x=>x.Code=="import_manuscript"),
        "missing manuscript becomes an actionable derived task");
    var singleDoc=repo.ImportPaperDocument(unfinished,pdfPath);
    Assert(repo.GetPrimaryPaperDocument(unfinished)?.Id==singleDoc.Id,
        "a project with one manuscript needs no manual primary selection");
    var noFigureTask=repo.GetPaperWorkSummary(unfinished);
    Assert(noFigureTask.Tasks.Any(x=>x.Code=="link_figures"&&x.BlocksArchive),
        "single manuscript without Figure bindings gets a focused task");
    var draftFigure=repo.CreateFiguresFast(unfinished,1).Single();
    repo.SetPaperFigureLink(singleDoc.Id,draftFigure.Id,null,false,"detected");
    var unfinishedFigure=repo.GetPaperWorkSummary(unfinished);
    Assert(unfinishedFigure.Tasks.Any(x=>x.Code=="figure_version"&&x.FigureId==draftFigure.Id),
        "document Figure candidate creates an actionable version task");

    var expectedNoFile=repo.CreateFiguresFast(paperId,1,1);
    Assert(repo.ListFigureComposites(expectedNoFile.Single().Id).Count==0 &&
        repo.ListPanelAssets(repo.ListPanels(expectedNoFile.Single().Id)[0].Id).Count==0,
        "Panel and Figure placeholders have no actual image files");
    // V15-13: empty Figure/Panel structures, safe deletion and stable IDs.
    var emptyPaper=repo.CreatePaperProject("V15-13 验收项目");
    var five=repo.CreateFiguresFast(emptyPaper,5,3,startNumber:1);
    Assert(five.Count==5 && repo.ListFigureOverview(emptyPaper).Count==5 &&
        five.All(f=>repo.ListPanelOverview(f.Id).Count==3), "5 Figures and 15 Panels");
    Assert(five.SelectMany(f=>repo.ListPanels(f.Id)).All(p=>repo.ListPanelAssets(p.Id).Count==0),
        "15 placeholders contain zero image references");
    var firstFigure=five[0];
    var firstPanel=repo.ListPanels(firstFigure.Id)[0];
    repo.RenamePanelLabel(firstPanel.Id,"D");
    Assert(repo.ListPanels(firstFigure.Id).Any(p=>p.Id==firstPanel.Id&&p.Label=="D"),
        "Panel rename preserves identity");
    repo.RenameFigureNumber(firstFigure.Id,7);
    Assert(repo.ListFigures(emptyPaper).Any(f=>f.Id==firstFigure.Id&&f.Number==7),
        "Figure renumber preserves identity");
    repo.DeletePanelPlaceholder(firstPanel.Id);
    Assert(repo.ListPanels(firstFigure.Id).Count==2,"Panel deletion removes only placeholder");
    repo.DeleteFigurePlaceholder(firstFigure.Id);
    Assert(repo.ListFigures(emptyPaper).Count==4,"Figure deletion removes its panels");
    var afterDelete=repo.CreateFiguresFast(emptyPaper,1);
    Assert(afterDelete.Single().Number==6,
        "next Figure uses max current number after deleting renamed Figure 7");
    Console.WriteLine("PASS: V1.5 figure placeholders, batch numbering, linking, material manifest, versioning, verified exports.");
    Console.WriteLine("PASS: Manuscript PDF/DOCX snapshot and persistent logical move undo.");
    Console.WriteLine("PASS: V1.3 papers, figures, panels, move, copy, stable IDs, rename, delete/restore.");
    Console.WriteLine("PASS: workspace import, naming, SHA-256, source safety, no overwrite, Figure usage, reopen, group separation.");
}
finally
{
    try{Directory.Delete(root,recursive:true);}catch{}
}
