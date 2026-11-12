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
    Assert(ExperimentWorkspace.DetectStage("quant_data/CV01_quant.tif")=="quant",
        "quant_data is a dedicated image stage");
    Assert(ExperimentWorkspace.DetectStage("process_data/CV01_quant_v2.tif")=="quant",
        "_quant suffix overrides a generic PROCESS folder");
    Assert(ExperimentWorkspace.DetectStage("mixed/CV01_quant.tif")=="quant",
        "_quant image suffix classifies mixed directories");
    Assert(ExperimentWorkspace.DetectStage("mixed/CV01_quant.csv")=="statistics",
        "statistics tables are distinct from quantification images");
    Assert(ExperimentWorkspace.DetectStage("quant_data/CV01_quant.csv")=="statistics",
        "a CSV inside quant_data is still a statistics table");
    Assert(ExperimentWorkspace.DetectPrefix("quant_data/CV01_quant_v2.tif")=="CV01",
        "quant images retain the sample identity");

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
    // D2: sample identities are stable, while experiment/file labels remain separate.
    var unitsBefore=repo.ListSampleUnits(projectId:project);
    var rosUnit=unitsBefore.Single(x=>x.ExperimentName=="ROS检测" &&
        x.SampleName=="ROS01");
    Assert(rosUnit.RawCount==1 && rosUnit.ProcessCount==2 &&
        rosUnit.ShowCount==1 && rosUnit.QuantCount==0,
        "D2 sample overview contains all stage assets from an experiment root");
    Assert(repo.ListSampleUnitAssets(rosUnit.Id).Count==4 &&
        repo.GetSampleUnitForAsset(raw.Id)?.Id==rosUnit.Id,
        "D2 new sample unit members and reverse asset lookup");
    var custom=repo.CreateCustomTag("批次3");
    Assert(repo.CreateCustomTag("批次3").Id==custom.Id,
        "custom tag names are unique and reusable");
    repo.SetEntityTag("sample",rosUnit.Id,custom.Id,true);
    repo.SetEntityNote("sample",rosUnit.Id,"ROS 样本，复查显微镜图像");
    Assert(repo.GetEntityAnnotation("sample",rosUnit.Id).Note.Contains("显微镜") &&
        repo.GetEntityAnnotation("sample",rosUnit.Id).Tags.Single().Id==custom.Id,
        "sample notes and custom tag assignments persist");
    var roExperiment=repo.ListExperimentUnits(project)
        .Single(x=>x.Name=="ROS检测");
    Assert(roExperiment.Id==rosUnit.ExperimentId &&
        roExperiment.SampleCount==1 && roExperiment.AssetCount==4,
        "experiment overview groups sample members by stable parent ID");
    repo.SetEntityNote("experiment",roExperiment.Id,"实验批次的总体备注");
    repo.SetEntityTag("asset",raw.Id,custom.Id,true);
    Assert(repo.GetEntityAnnotation("asset",raw.Id).Tags.Single().Id==custom.Id,
        "experiment, sample, asset annotations have independent scopes");
    Assert(repo.GetEntityAnnotation("experiment",roExperiment.Id)
        .Note=="实验批次的总体备注",
        "experiment note is separate from sample note");
    // A redundant sample-index rebuild cannot mint a new sample ID or drop notes.
    var d2Preview=repo.PreviewSampleIndexRebuild(project);
    repo.ApplySampleIndexRebuild(project,d2Preview.Fingerprint);
    Assert(repo.ListSampleUnits(projectId:project)
        .Single(x=>x.SampleName=="ROS01"&&x.ExperimentName=="ROS检测").Id==rosUnit.Id &&
        repo.GetEntityAnnotation("sample",rosUnit.Id).Tags.Single().Id==custom.Id,
        "sample ID and annotations survive deterministic sample-index rebuild");

    // V1.6-D1 parser: filename stages, arbitrary PROCESS subtype and versions.
    Assert(ExperimentWorkspace.ParseAssetName("ROS01_raw.tif").Stage=="raw" &&
        ExperimentWorkspace.ParseAssetName("ROS01_show_v2.png").FileVersion==2 &&
        ExperimentWorkspace.ParseAssetName("ROS01_quant.tif").SampleName=="ROS01" &&
        ExperimentWorkspace.ParseAssetName("ROS01_adjusted_v2.tif","ROS01")
            is {Stage:"process",ProcessSubtype:"adjusted",FileVersion:2} &&
        ExperimentWorkspace.ParseAssetName("ROS010_crop.tif","ROS01").ParseStatus=="unresolved",
        "name parser preserves boundaries, named PROCESS subtype and version");
    Assert(ExperimentWorkspace.DetectStage("ROS01_show.png")=="show" &&
        ExperimentWorkspace.DetectStage("ROS01_raw.tif")=="raw" &&
        ExperimentWorkspace.DetectStage("ROS01_process.tif")=="process",
        "explicit stage suffix works in experiment root directory");
    var rawTags=repo.GetAssetSystemTags(raw.Id);
    var showTags=repo.GetAssetSystemTags(show.Id);
    Assert(rawTags is {SampleName:"ROS01",Stage:"raw",ParseStatus:"resolved",
        SourceKind:"root_folder"} &&
        rawTags.ExperimentName=="ROS检测" &&
        showTags is {SampleName:"ROS01",Stage:"show",ParseStatus:"resolved"},
        "new imported assets acquire persistent experiment-folder and sample tags");
    var cropTag=repo.GetAssetSystemTags(ros.Single(x=>x.FileName=="ROS01_crop.tif").Id);
    Assert(cropTag is {SampleName:"ROS01",Stage:"process",ProcessSubtype:"crop"},
        "unknown PROCESS suffix is stored as an automatic subtype");
    var exports=Path.Combine(root,"figures");
    var usage=repo.ExportForFigure(show,"Paper A",2,"a",exports);
    Assert(usage.State=="exported" && usage.Panel=="A","usage created");
    Assert(File.ReadAllBytes(usage.ExportPath).SequenceEqual(new byte[] { 12, 13, 14 }),"export is lossless");
    var second=repo.ExportOriginal(show,exports);
    Assert(second!=usage.ExportPath && File.Exists(second),"no silent overwrite");
    repo.SetUsageState(usage.Id,"confirmed");
    Assert(repo.ListUsages(show.Id).Single().State=="confirmed","confirmation saved");
    // V1.6-C: global search must never depend on the selected UI scope.
    Assert(repo.SearchAssetsGlobal("ROS01").Count==4 &&
        repo.SearchAssetsGlobal("ROS01",2,0).Count==2 &&
        repo.SearchAssetsGlobal("ROS01",2,2).Count==2,
        "global asset search provides deterministic bounded pagination");
    Assert(repo.SearchAssetsGlobal("实验项目 A").Count==5 &&
        repo.SearchAssetsGlobal("ROS01_show").Single().Asset.Id==show.Id &&
        repo.SearchAssetsGlobal("ROS01_show").Single().ProjectId==project,
        "global lookup searches project and filename while returning navigation IDs");
    Assert(repo.SearchAssetsGlobal("%_").Count==0 &&
        repo.SearchAssetsGlobal("\\\\%").Count==0,
        "search wildcard characters are treated as literal text");
    Assert(repo.ListScopeUsageLabels(projectId:project).TryGetValue(show.Id,out var useLabel)
        && useLabel=="Fig 2A" &&
        !repo.ListScopeUsageLabels(resultId:imported.ResultId).ContainsKey(raw.Id),
        "batch presentation-use labels agree with confirmed usages");
    Assert(ExperimentWorkspace.Open(repo.Root).ListProjects().Single().Id==project,"reopen identity");
    Assert(ExperimentWorkspace.Open(repo.Root).ListResults(project).Single().FileCount==5,"reopen assets");
    // V1.6-B: quantitative images sit alongside SHOW but never become
    // presentation materials. Keep existing fixture counts unchanged.
    var quantSource=Path.Combine(root,"source","CV01 定量");
    foreach(var folder in new[]{"raw_data","process_data","show_data","quant_data"})
        Directory.CreateDirectory(Path.Combine(quantSource,folder));
    File.WriteAllBytes(Path.Combine(quantSource,"raw_data","CV01_raw.tif"),[23]);
    File.WriteAllBytes(Path.Combine(quantSource,"process_data","CV01_crop.tif"),[24]);
    File.WriteAllBytes(Path.Combine(quantSource,"process_data","CV01_unknownProcessing.tif"),[28]);
    File.WriteAllBytes(Path.Combine(quantSource,"show_data","CV01_show.png"),[25]);
    File.WriteAllBytes(Path.Combine(quantSource,"quant_data","CV01_quant.tif"),[26]);
    File.WriteAllBytes(Path.Combine(quantSource,"quant_data","CV01_quant_v2.tif"),[27]);
    File.WriteAllText(Path.Combine(quantSource,"CV01_quant.csv"),"measurement,value\\nA,5");
    var quantImport=repo.ImportResult(project,quantSource);
    var quantAssets=repo.ListAssets(quantImport.ResultId);
    Assert(quantImport.FileCount==7 && quantImport.DataGroupCount==1,
        "quant RAW PROCESS SHOW and spreadsheet share the sample group");
    Assert(quantAssets.Count(x=>x.Stage=="quant")==2 &&
        quantAssets.Count(x=>x.Stage=="show")==1 &&
        quantAssets.Count(x=>x.Stage=="process")==2 &&
        quantAssets.Count(x=>x.Stage=="statistics")==1 &&
        quantAssets.Single(x=>x.FileName=="CV01_unknownProcessing.tif").GroupPrefix=="CV01",
        "quantitative images and presentation images and statistic tables stay separate");
    var quant=quantAssets.First(x=>x.Stage=="quant");
    Assert(repo.ListSameGroupUpstreamCandidates(quant.Id).Count==3,
        "quant can suggest same-sample RAW and PROCESS upstream");
    var cvProcess=quantAssets.Single(x=>x.FileName=="CV01_crop.tif");
    repo.SetAssetProvenance(cvProcess.Id,quant.Id,"图像定量分析",true);
    Assert(repo.ListAssetUpstream(quant.Id).Single().SourceAssetId==cvProcess.Id,
        "PROCESS to QUANT provenance may be recorded explicitly");
    var cannotPublishQuant=false;
    try{repo.ExportForFigure(quant,"Test",1,"A",exports);}
    catch(InvalidOperationException){cannotPublishQuant=true;}
    Assert(cannotPublishQuant,"quantitative images cannot be exported as final SHOW material");
    Assert(ExperimentWorkspace.Open(repo.Root).ListAssets(quantImport.ResultId)
        .Count(x=>x.Stage=="quant")==2,"quant stage survives reopening");
    // Opening an older warehouse rebuilds the restrictive SQLite CHECK in a
    // transaction without changing any existing asset identity or usage FK.
    var oldWarehouse=Path.Combine(root,"warehouse-v15-schema");
    var oldDatabase=Path.Combine(oldWarehouse,"database","workspace.db");
    Directory.CreateDirectory(Path.GetDirectoryName(oldDatabase)!);
    using(var legacyDb=new Microsoft.Data.Sqlite.SqliteConnection(
        new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{
            DataSource=oldDatabase}.ToString()))
    {
        legacyDb.Open();
        using var command=legacyDb.CreateCommand();
        command.CommandText="""
            CREATE TABLE experiment_projects(
                id TEXT PRIMARY KEY,name TEXT NOT NULL,created_at TEXT NOT NULL);
            CREATE TABLE experiment_results(
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES experiment_projects(id),
                name TEXT NOT NULL,imported_at TEXT NOT NULL);
            CREATE TABLE experiment_groups(
                id TEXT PRIMARY KEY,
                result_id TEXT NOT NULL REFERENCES experiment_results(id),
                prefix TEXT NOT NULL COLLATE NOCASE,UNIQUE(result_id,prefix));
            CREATE TABLE experiment_assets(
                id TEXT PRIMARY KEY,
                result_id TEXT NOT NULL REFERENCES experiment_results(id),
                group_id TEXT NOT NULL REFERENCES experiment_groups(id),
                stage TEXT NOT NULL CHECK(stage IN ('raw','process','show','statistics','other')),
                relative_path TEXT NOT NULL,filename TEXT NOT NULL,
                size INTEGER NOT NULL,sha256 TEXT NOT NULL,imported_at TEXT NOT NULL,
                UNIQUE(result_id,relative_path));
            CREATE TABLE experiment_usages(
                id TEXT PRIMARY KEY,
                asset_id TEXT NOT NULL REFERENCES experiment_assets(id),
                paper_title TEXT NOT NULL,
                figure INTEGER NOT NULL CHECK(figure>0),panel TEXT NOT NULL,
                state TEXT NOT NULL CHECK(state IN ('exported','confirmed','cancelled')),
                export_path TEXT NOT NULL,created_at TEXT NOT NULL);
            INSERT INTO experiment_projects VALUES('old-project','旧实验','2020');
            INSERT INTO experiment_results VALUES('old-result','old-project','旧结果','2020');
            INSERT INTO experiment_groups VALUES('old-group','old-result','CV01');
            INSERT INTO experiment_assets VALUES(
                'old-asset','old-result','old-group','show','show_data/CV01_show.png',
                'CV01_show.png',42,
                'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa','2020');
            INSERT INTO experiment_usages VALUES(
                'old-usage','old-asset','旧论文',1,'A','confirmed','old-export','2020');
            """;
        command.ExecuteNonQuery();
    }
    var upgraded=ExperimentWorkspace.Open(oldWarehouse);
    var oldAsset=upgraded.ListAssets("old-result").Single();
    Assert(oldAsset.Id=="old-asset"&&oldAsset.GroupId=="old-group"&&
        oldAsset.Stage=="show"&&oldAsset.Size==42,
        "quant schema migration keeps existing asset and group metadata");
    Assert(upgraded.ListUsages("old-asset").Single().Id=="old-usage",
        "quant schema migration keeps existing figure-usage foreign keys");
    Assert(ExperimentWorkspace.Open(oldWarehouse).ListAssets("old-result").Single().Id=="old-asset",
        "quant stage migration is idempotent on repeated open");
    // Old databases are never silently regrouped. Operators can preview,
    // apply and undo an index-only rebuild without touching legacy identities.
    var oldPreview=upgraded.PreviewSampleIndexRebuild("old-project");
    Assert(oldPreview.IndexedCount==1 && oldPreview.Changes.Count==1,
        "old sample index rebuild has a read-only preview");
    var staleRejected=false;
    try{upgraded.ApplySampleIndexRebuild("old-project","invalid-preview");}
    catch(InvalidOperationException){staleRejected=true;}
    Assert(staleRejected &&
        upgraded.PreviewSampleIndexRebuild("old-project").Changes.Count==1,
        "unverified rebuild tokens must not write any rows");
    upgraded.ApplySampleIndexRebuild("old-project",oldPreview.Fingerprint);
    Assert(upgraded.PreviewSampleIndexRebuild("old-project").Changes.Count==0,
        "old sample index safely rebuilt");
    Assert(upgraded.UndoLastSampleIndexRebuild("old-project") &&
        upgraded.PreviewSampleIndexRebuild("old-project").Changes.Count==1,
        "manual sample index rebuild is reversible");
    Assert(upgraded.ListAssets("old-result").Single().Id=="old-asset" &&
        upgraded.ListAssets("old-result").Single().GroupId=="old-group" &&
        upgraded.ListAssets("old-result").Single().Sha256==oldAsset.Sha256 &&
        upgraded.ListUsages("old-asset").Single().State=="confirmed",
        "sample index rebuild never rewrites assets, hashes, group IDs or usage links");


    var source2=Path.Combine(root,"source","独立实验");
    Directory.CreateDirectory(Path.Combine(source2,"show_data"));
    File.WriteAllBytes(Path.Combine(source2,"show_data","ROS01_show.png"),[12,13,14]);
    var another=repo.ImportResult(project,source2);
    Assert(another.DataGroupCount==1 && repo.ListAssets(another.ResultId).Single().GroupId!=show.GroupId,
        "identical prefix in different results must remain distinct");
    var secondShow=repo.ListAssets(another.ResultId).Single();
    Assert(repo.ListSameSampleAssets(secondShow.Id).Count==4,
        "same sample index spans import batches without merging legacy groups");
    Assert(repo.ListSameGroupUpstreamCandidates(secondShow.Id).Count==2,
        "cross-import PROCESS upstream appears as an unconfirmed candidate");
    Assert(repo.ListAssetUpstream(secondShow.Id).Count==0,
        "automatic match never invents a confirmed processing edge");
    var differentSource=Path.Combine(root,"source","CV010");
    Directory.CreateDirectory(Path.Combine(differentSource,"raw_data"));
    Directory.CreateDirectory(Path.Combine(differentSource,"process_data"));
    File.WriteAllBytes(Path.Combine(differentSource,"raw_data","ROS010_raw.tif"),[30]);
    File.WriteAllBytes(Path.Combine(differentSource,"process_data","ROS010_unknownProcessing.tif"),[31]);
    var different=repo.ImportResult(project,differentSource);
    Assert(repo.ListAssets(different.ResultId).Count==2 &&
        !repo.ListSameSampleAssets(secondShow.Id)
          .Any(x=>x.ResultId==different.ResultId),
        "sample boundary prevents ROS01 from matching ROS010");
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
    // V1.6: dropping multiple SHOW assets on Figure creates consecutive
    // Panels atomically; data stays in its experiment and hashes are preserved.
    var newPanelShows=new[]{show.Id,copies.Single(x=>x.Stage=="show").Id};
    var droppedPanels=repo.LinkShowAssetsToNewPanels(latest.Single().Id,newPanelShows);
    Assert(droppedPanels.Select(x=>x.Label).SequenceEqual(new[]{"D","E"}) &&
        repo.ListPanels(latest.Single().Id).Count==5,
        "Figure drop creates consecutive Panel slots for multiple SHOW assets");
    Assert(droppedPanels.SelectMany(p=>repo.ListPanelAssets(p.Id))
        .Select(x=>x.AssetId).SequenceEqual(newPanelShows),
        "Figure drop makes stable references without copying SHOW bytes");
    var beforeFailedDrop=repo.ListPanels(latest.Single().Id).Count;
    var invalidDropRejected=false;
    try{repo.LinkShowAssetsToNewPanels(latest.Single().Id,new[]{show.Id,quant.Id});}
    catch(InvalidOperationException){invalidDropRejected=true;}
    Assert(invalidDropRejected &&
        repo.ListPanels(latest.Single().Id).Count==beforeFailedDrop,
        "mixed SHOW and QUANT drag does not leave an orphan Panel");
    Assert(repo.UndoLastFigureDrop(latest.Single().Id) &&
        repo.ListPanels(latest.Single().Id).Count==3 &&
        droppedPanels.All(p=>repo.ListPanels(latest.Single().Id).All(x=>x.Id!=p.Id)),
        "undoing a Figure drop removes only its new Panel links and placeholders");
    var otherPanel=repo.ListPanels(figuresNew[1].Id).First();
    Assert(repo.LinkShowAssets(otherPanel.Id,[show.Id])==1 &&
        repo.UndoLastFigureDrop(figuresNew[1].Id) &&
        repo.ListPanelAssets(otherPanel.Id).Count==0,
        "undoing a Panel drop removes the logical link only");
    Assert(repo.ListAssets(destination).Any(x=>x.Id==show.Id&&x.Sha256==show.Sha256),
        "Figure drop and undo never modify the original asset or SHA");
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
    var fixedReview=repo.PreviewCompositeSourceReview(v1.Id);
    Assert(!fixedReview.IsLivePanelCandidate && fixedReview.Sources.Count==1,
        "already-versioned composite previews its original immutable source snapshot");
    repo.ConfirmCompositeSourceReview(v1.Id,fixedReview.Fingerprint);
    Assert(repo.ListCompositeSources(v1.Id).Single().Status=="confirmed",
        "Figure source snapshot can be explicitly verified");
    // A legacy composite with no sources previews live Panel links without
    // writing them. One explicit confirmation captures a new immutable snapshot.
    var legacyComposite=repo.ImportFigureComposite(latest.Single().Id,composPath);
    Assert(repo.ListCompositeSources(legacyComposite.Id).Count==0,
        "empty historical composite starts without invented source facts");
    Assert(repo.PreviewCompositeSourceReview(legacyComposite.Id).Sources.Count==0,
        "an unlinked legacy version has no candidates");
    var initialPanel=repo.ListPanels(latest.Single().Id).First();
    repo.LinkShowAssets(initialPanel.Id,[show.Id]);
    var pendingReview=repo.PreviewCompositeSourceReview(legacyComposite.Id);
    Assert(pendingReview.IsLivePanelCandidate &&
        pendingReview.Sources.Count==1 &&
        repo.ListCompositeSources(legacyComposite.Id).Count==0,
        "read-only legacy preview does not silently create history");
    var extraShow=copies.Single(x=>x.Stage=="show");
    repo.LinkShowAssets(initialPanel.Id,[extraShow.Id]);
    var staleSourceReviewRejected=false;
    try{repo.ConfirmCompositeSourceReview(legacyComposite.Id,pendingReview.Fingerprint);}
    catch(InvalidOperationException){staleSourceReviewRejected=true;}
    Assert(staleSourceReviewRejected &&
        repo.ListCompositeSources(legacyComposite.Id).Count==0,
        "source preview cannot be confirmed after live Panel links change");
    var refreshedReview=repo.PreviewCompositeSourceReview(legacyComposite.Id);
    repo.ConfirmCompositeSourceReview(legacyComposite.Id,refreshedReview.Fingerprint);
    Assert(repo.ListCompositeSources(legacyComposite.Id).Count==2 &&
        repo.ListCompositeSources(legacyComposite.Id).All(x=>x.Status=="confirmed"),
        "legacy candidates become confirmed only after explicit guarded review");
    var latestLink=repo.ListPanelAssets(initialPanel.Id).First(x=>x.AssetId==extraShow.Id);
    repo.UnlinkPanelAsset(latestLink.Id);
    Assert(!repo.PreviewCompositeSourceReview(legacyComposite.Id).IsLivePanelCandidate &&
        repo.ListCompositeSources(legacyComposite.Id).Count==2,
        "confirmed legacy snapshot remains immutable after working Panel changes");
    var process=ros.First(x=>x.Stage=="process");
    repo.SetAssetProvenance(process.Id,show.Id,"图像处理输出",true);
    repo.SetAssetProvenance(raw.Id,process.Id,"原始图像裁剪",true);
    Assert(repo.ListAssetUpstream(show.Id).Single().SourceAssetId==process.Id &&
        repo.ListAssetUpstream(process.Id).Single().SourceAssetId==raw.Id,
        "explicit SHOW to PROCESS to RAW provenance");
    var downstreamCandidates=repo.ListSameGroupUpstreamCandidates(show.Id);
    Assert(downstreamCandidates.Count>=2 &&
        downstreamCandidates.Count(x=>x.Asset.GroupId==show.GroupId)==2 &&
        downstreamCandidates.All(x=>x.Relation.Contains("未确认")),
        "existing and cross-import PROCESS files remain unverified candidates");
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
    // V1.6-C: reverse lookup uses pinned immutable snapshots, not current
    // Panel links. RAW traces via explicitly confirmed provenance edges.
    var showRefs=repo.ListAssetPaperReferences(show.Id);
    var processRefs=repo.ListAssetPaperReferences(process.Id);
    var rawRefs=repo.ListAssetPaperReferences(raw.Id);
    Assert(showRefs.Any(x=>x.ReferenceKind=="snapshot" &&
        x.DocumentId==pdfDoc.Id && x.CompositeId==v1.Id &&
        x.PanelLabel=="A") &&
        processRefs.Any(x=>x.CompositeId==v1.Id && x.ProcessingStatus=="confirmed") &&
        rawRefs.Any(x=>x.CompositeId==v1.Id && x.ProcessingStatus=="confirmed"),
        "SHOW PROCESS RAW reverse trace locates the pinned manuscript version");
    Assert(!showRefs.Any(x=>x.ReferenceKind=="working" &&
        x.FigureId==figuresNew[0].Id),
        "a removed working Panel link is not invented from historical evidence");
    Assert(repo.ListDocumentAssetReferences(pdfDoc.Id).Any(x=>
        x.ShowAssetId==show.Id && x.CompositeId==v1.Id &&
        x.ReferenceKind=="snapshot"),
        "manuscript forward index identifies its exact historical SHOW snapshot");
    Assert(repo.ListDocumentAssetReferences(wordDoc.Id).Count==0,
        "an unrelated manuscript has no invented Figure references");
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
    Assert(archive.ListArchivedAssetReferences(show.Id).Any(x=>
        x.ArchivePaperId==archived.PaperId && x.Revision==1 &&
        x.Role=="show" && x.Location.Contains("Panel A")) &&
        archive.ListArchivedAssetReferences(raw.Id).Any(x=>
            x.Revision==1 && x.Role=="raw") &&
        archive.ListArchivedAssetReferences(quant.Id).Count==0,
        "archive reverse lookup includes recorded RAW and SHOW but excludes QUANT");
    Assert(archive.PublishWorkspaceArchive(preview).AlreadyArchived &&
        archive.ListWorkspaceRevisions(archived.PaperId).Count==1,
        "repeated archive of same manifest is idempotent");
    var v3=repo.ImportFigureComposite(figuresNew[0].Id,composPath);
    repo.SetPaperFigureLink(pdfDoc.Id,figuresNew[0].Id,v3.Id,true);
    var revisionPreview=archive.PrepareWorkspaceArchive(repo,paperId,pdfDoc.Id);
    var revision=archive.PublishWorkspaceArchive(revisionPreview,true);
    Assert(archive.ListArchivedAssetReferences(show.Id).Any(x=>x.Revision==1) &&
        archive.GetWorkspaceArchiveManifest(archived.PaperId,1)==preview.ManifestJson,
        "reverse index keeps archived revision 1 visible after later publication");
    Assert(revision.PaperId==archived.PaperId && revision.Revision==2 &&
        archive.ListWorkspaceRevisions(archived.PaperId).Count==2 &&
        archive.GetWorkspaceArchiveManifest(archived.PaperId,1)==preview.ManifestJson,
        "subsequent edits create revision 2 without changing revision 1");
    var historicalResources=archive.ListWorkspaceArchiveResources(archived.PaperId,1);
    var newerResources=archive.ListWorkspaceArchiveResources(archived.PaperId,2);
    Assert(historicalResources.Any(x=>x.DataId==v1.Id&&x.Role=="composite") &&
        !historicalResources.Any(x=>x.DataId==v3.Id&&x.Role=="composite") &&
        newerResources.Any(x=>x.DataId==v3.Id&&x.Role=="composite") &&
        !newerResources.Any(x=>x.DataId==v1.Id&&x.Role=="composite"),
        "inline archive revisions query independent stored composite versions");
    Assert(archive.ListWorkspaceArchiveResources(archived.PaperId)
        .Any(x=>x.DataId==v3.Id),
        "default archive resource query selects latest revision only");
    Assert(archive.ListWorkspaceRevisions(archived.PaperId)
        .Select(x=>x.Revision).SequenceEqual(new[]{2,1}),
        "revision picker orders newest snapshot before older snapshots");
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
    // A flat root folder requires no raw_data/show_data directory convention.
    var flat=Path.Combine(root,"source","平铺ROS实验");
    Directory.CreateDirectory(flat);
    File.WriteAllBytes(Path.Combine(flat,"ROS010_raw.tif"),[31]);
    File.WriteAllBytes(Path.Combine(flat,"ROS010_crop.tif"),[32]);
    File.WriteAllBytes(Path.Combine(flat,"ROS010_show.png"),[33]);
    File.WriteAllBytes(Path.Combine(flat,"ROS01_raw.tif"),[34]);
    File.WriteAllBytes(Path.Combine(flat,"ROS01_adjusted_v2.tif"),[35]);
    File.WriteAllBytes(Path.Combine(flat,"ROS01_quant.tif"),[36]);
    var flatResult=repo.ImportResult(project,flat);
    var flatAssets=repo.ListAssets(flatResult.ResultId);
    Assert(flatAssets.Count(x=>x.Stage=="process")==2 &&
        repo.GetAssetSystemTags(flatAssets.Single(x=>x.FileName=="ROS01_adjusted_v2.tif").Id)
        is {SampleName:"ROS01",ProcessSubtype:"adjusted",FileVersion:2} &&
        repo.GetAssetSystemTags(flatAssets.Single(x=>x.FileName=="ROS010_crop.tif").Id)
        is {SampleName:"ROS010",ProcessSubtype:"crop"} &&
        repo.GetAssetSystemTags(flatAssets.Single(x=>x.FileName=="ROS01_raw.tif").Id)
        ?.ExperimentName=="平铺ROS实验",
        "flat root stage inference respects ROS01 and ROS010 sample boundaries");
    var mismatchRoot=Path.Combine(root,"source","标签冲突实验");
    Directory.CreateDirectory(Path.Combine(mismatchRoot,"raw_data"));
    File.WriteAllBytes(Path.Combine(mismatchRoot,"raw_data","CHECK01_show.png"),[41,42]);
    var mismatchImport=repo.ImportResult(project,mismatchRoot);
    var mismatchAsset=repo.ListAssets(mismatchImport.ResultId).Single();
    Assert(mismatchAsset.Stage=="show" &&
        repo.GetAssetSystemTags(mismatchAsset.Id) is
            {SampleName:"CHECK01",Stage:"show",ParseStatus:"conflict",
             ExperimentName:"标签冲突实验"},
        "explicit SHOW suffix conflicting with RAW folder requires review");
    Assert(ExperimentWorkspace.Open(repo.Root)
        .GetAssetSystemTags(mismatchAsset.Id)?.ParseStatus=="conflict",
        "system tag conflict persists across workspace reopen");
    Assert(repo.ListUnresolvedSampleAssets(project)
        .Any(x=>x.AssetId==mismatchAsset.Id&&x.ParseStatus=="conflict") &&
        repo.GetSampleUnitForAsset(mismatchAsset.Id) is null,
        "conflicting asset stays in unresolved queue rather than a confirmed sample");
    // D2 cross-result grouping: same root inside one project shares a stable unit,
    // while an identically named experiment in another project remains distinct.
    var sameExperimentBatch=repo.ImportResult(project,source);
    var secondExperimentBatch=repo.ImportResult(project,source);
    var sampleAfterBatch=repo.ListSampleUnits(projectId:project)
        .Single(x=>x.ExperimentName=="ROS检测"&&x.SampleName=="ROS01");
    Assert(sampleAfterBatch.Id==rosUnit.Id && sampleAfterBatch.RawCount>=2 &&
        sampleAfterBatch.ShowCount>=2 && sampleAfterBatch.ProcessCount>=4 &&
        repo.GetEntityAnnotation("sample",rosUnit.Id).Note.Contains("显微镜"),
        "same experiment root across imports preserves sample ID and annotations");
    var memberCount=repo.ListSampleUnitAssets(rosUnit.Id).Count;
    Assert(memberCount>=8 &&
        repo.ListSampleUnitAssets(rosUnit.Id).Any(x=>x.ResultId==sameExperimentBatch.ResultId) &&
        repo.ListSampleUnitAssets(rosUnit.Id).Any(x=>x.ResultId==secondExperimentBatch.ResultId) &&
        repo.ListTaggedSampleIds(project,custom.Id).Contains(rosUnit.Id),
        "sample members span multiple imported results and tag filter retains original ID");
    var isolatedProject=repo.CreateProject("隔离实验项目");
    repo.ImportResult(isolatedProject,source);
    var isolatedUnit=repo.ListSampleUnits(projectId:isolatedProject)
        .Single(x=>x.SampleName=="ROS01");
    Assert(isolatedUnit.Id!=rosUnit.Id && isolatedUnit.ExperimentId!=rosUnit.ExperimentId &&
        repo.GetEntityAnnotation("sample",isolatedUnit.Id).Tags.Count==0 &&
        repo.ListTaggedSampleIds(isolatedProject,custom.Id).Count==0,
        "same experimental folder in another project does not share tags or sample identity");
    var freshShow=repo.ListAssets(sameExperimentBatch.ResultId)
        .Single(x=>x.FileName=="ROS01_show.png");
    repo.SetAssetDeleted([freshShow.Id],true);
    Assert(repo.ListSampleUnitAssets(rosUnit.Id).Count==memberCount-1 &&
        repo.GetEntityAnnotation("sample",rosUnit.Id).Tags.Single().Id==custom.Id,
        "soft-deleted asset leaves sample annotations intact");
    repo.SetAssetDeleted([freshShow.Id],false);
    Assert(repo.ListSampleUnitAssets(rosUnit.Id).Count==memberCount &&
        repo.GetSampleUnitForAsset(freshShow.Id)?.Id==rosUnit.Id,
        "restore rejoins the original sample unit without inventing a new ID");
    repo.SetEntityTag("sample",rosUnit.Id,custom.Id,false);
    Assert(repo.GetEntityAnnotation("sample",rosUnit.Id).Tags.Count==0 &&
        !repo.ListTaggedSampleIds(project,custom.Id).Contains(rosUnit.Id),
        "custom tag removal never changes automatic sample membership");
    repo.SetEntityTag("sample",rosUnit.Id,custom.Id,true);
    Assert(ExperimentWorkspace.Open(repo.Root).GetEntityAnnotation("sample",rosUnit.Id)
        .Tags.Single().Id==custom.Id,
        "custom metadata persists across repository reopening");
    var sharedTag=repo.CreateCustomTag("试验备用组");
    Assert(repo.AssignCustomTagBatch("sample",
        [rosUnit.Id,isolatedUnit.Id],sharedTag.Id)==2 &&
        repo.AssignCustomTagBatch("sample",
        [rosUnit.Id,isolatedUnit.Id],sharedTag.Id)==0 &&
        repo.ListTaggedSampleIds(project,sharedTag.Id).Contains(rosUnit.Id) &&
        repo.ListTaggedSampleIds(isolatedProject,sharedTag.Id).Contains(isolatedUnit.Id),
        "transactional bulk tagging is idempotent and project-filtered");
    var rollbackTag=repo.CreateCustomTag("需回滚批次");
    var denied=false;
    try{repo.AssignCustomTagBatch("sample",[rosUnit.Id,"not-a-sample"],rollbackTag.Id);}
    catch(KeyNotFoundException){denied=true;}
    Assert(denied &&
        repo.GetEntityAnnotation("sample",rosUnit.Id).Tags
            .All(x=>x.Id!=rollbackTag.Id),
        "invalid bulk entity rolls back every assignment");
    Console.WriteLine("PASS: V1.6-D2 stable units, cross-result aggregation, tag scopes, notes and restore.");
    Console.WriteLine("PASS: V1.6-D1 import-root labels, process subtypes, file versions and conflict flags.");
    Console.WriteLine("PASS: V1.5 figure placeholders, batch numbering, linking, material manifest, versioning, verified exports.");
    Console.WriteLine("PASS: Manuscript PDF/DOCX snapshot and persistent logical move undo.");
    Console.WriteLine("PASS: V1.3 papers, figures, panels, move, copy, stable IDs, rename, delete/restore.");
    Console.WriteLine("PASS: workspace import, naming, SHA-256, source safety, no overwrite, Figure usage, reopen, group separation.");
}
finally
{
    try{Directory.Delete(root,recursive:true);}catch{}
}
