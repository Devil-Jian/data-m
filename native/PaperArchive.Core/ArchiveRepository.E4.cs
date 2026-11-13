using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PaperArchive.Core.Workspace;

namespace PaperArchive.Core;

public sealed partial class ArchiveRepository
{
    /// <summary>
    /// V1.6-E4 archive: the entire experiment attached to each Figure A/B/C
    /// is a searchable provenance set. No PROCESS/RAW causal edges, SHOW
    /// selection or extra processing confirmations are required.
    /// Existing schema 1/2 archives and their publishing API remain unchanged.
    /// </summary>
    public WorkspaceArchivePreview PrepareWorkspaceArchiveE4(
        ExperimentWorkspace workspace,string paperId,string documentId)
    {
        if(!Path.GetFullPath(workspace.Root).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("论文工作台和归档必须共用同一个数据仓库。");
        var paper=workspace.ListPaperProjects().SingleOrDefault(x=>x.Id==paperId)
            ??throw new KeyNotFoundException("论文不存在。");
        var document=workspace.ListPaperDocuments(paperId)
            .SingleOrDefault(x=>x.Id==documentId)
            ??throw new InvalidOperationException("请选择此论文下的全文文件。");

        var checks=new List<ArchiveCheck>();
        var resources=new List<ArchiveResourceSnapshot>();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        void Add(string id,string name,string role,string location,string sha,long size)
        {
            if(seen.Add(id+"|"+role+"|"+location))
                resources.Add(new(id,name,role,location,sha,size));
        }
        Add(document.Id,document.FileName,"manuscript","论文全文",
            document.Sha256,document.Size);

        var figures=new List<object>();
        var allFigures=workspace.ListFigures(paperId);
        if(allFigures.Count==0)
            checks.Add(new("no_figure","block","请先为论文添加 Figure。"));
        foreach(var figure in allFigures)
        {
            var label="Figure "+figure.Number;
            var composite=workspace.ListFigureShowData(figure.Id)
                .FirstOrDefault(x=>x.IsCurrent);
            if(composite is null)
                checks.Add(new("missing_composite","block",
                    label+" 尚未导入完整 Figure 图片。"));
            else
                Add(composite.Id,composite.FileName,"composite",
                    label+" / v"+composite.Version,composite.Sha256,composite.Size);

            var panels=workspace.ListPanels(figure.Id);
            if(panels.Count==0)
                checks.Add(new("missing_panels","block",label+" 尚未创建子图。"));
            var sources=new List<object>();
            foreach(var panel in panels)
            {
                var panelLocation=label+" / Panel "+panel.Label;
                var binding=workspace.GetPanelExperiment(panel.Id);
                if(binding is null)
                {
                    checks.Add(new("missing_experiment","block",
                        panelLocation+" 尚未关联实验。"));
                    continue;
                }
                var assets=workspace.ListE4PanelAssets(panel.Id);
                if(assets.Count==0)
                    checks.Add(new("empty_experiment","block",
                        panelLocation+" 的实验没有可归档文件。"));
                foreach(var asset in assets)
                    Add(asset.AssetId,asset.FileName,asset.Stage,
                        panelLocation,asset.Sha256,asset.Size);
                var samples=assets.Where(x=>x.SampleId is not null)
                    .GroupBy(x=>x.SampleId!,StringComparer.Ordinal)
                    .OrderBy(x=>x.First().SampleName,StringComparer.OrdinalIgnoreCase)
                    .Select(group=>new{
                        SampleUnitId=group.Key,
                        SampleName=group.First().SampleName,
                        assets=group.Select(x=>new{
                            x.AssetId,x.FileName,x.Stage,x.Sha256,x.Size,x.RelativePath
                        }).ToArray()
                    }).ToArray();
                sources.Add(new{
                    PanelId=panel.Id,PanelLabel=panel.Label,
                    ExperimentId=binding.ExperimentId,
                    ExperimentName=binding.ExperimentName,
                    sampleUnits=samples,
                    assets=assets.Select(x=>new{
                        x.AssetId,x.FileName,x.Stage,x.SampleId,x.SampleName,
                        x.Sha256,x.Size,x.RelativePath
                    }).ToArray()
                });
            }
            figures.Add(new{
                FigureId=figure.Id,FigureNumber=figure.Number,
                Id=composite?.Id,Version=composite?.Version,
                FileName=composite?.FileName,Sha256=composite?.Sha256,
                sources
            });
        }
        // Actual bytes and expected size are authoritative. Missing or
        // corrupted blobs block archiving; missing PROCESS ancestry does not.
        foreach(var resource in resources)
        {
            if(resource.Sha256.Length!=64||
                resource.Sha256.Any(c=>!Uri.IsHexDigit(c)))
            {
                checks.Add(new("invalid_sha","block",
                    resource.FileName+" 的 SHA-256 不合法。"));
                continue;
            }
            var blob=Path.Combine(_root,"blobs","sha256",
                resource.Sha256[..2],resource.Sha256);
            if(!File.Exists(blob))
            {
                checks.Add(new("missing_blob","block",
                    resource.FileName+" 的原始内容丢失。"));
                continue;
            }
            using var stream=File.OpenRead(blob);
            var actual=Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if(!actual.Equals(resource.Sha256,StringComparison.OrdinalIgnoreCase)
               ||stream.Length!=resource.Size)
                checks.Add(new("corrupt_blob","block",
                    resource.FileName+" 的内容校验失败。"));
        }
        var sorted=resources.OrderBy(x=>x.Location,StringComparer.Ordinal)
            .ThenBy(x=>x.Role,StringComparer.Ordinal)
            .ThenBy(x=>x.SourceId,StringComparer.Ordinal).ToArray();
        var manifest=JsonSerializer.Serialize(new{
            schemaVersion=3,paperId,paper.Name,
            document=new{document.Id,document.FileName,document.Sha256},
            figures,resources=sorted
        });
        var hash=Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
        return new(paperId,paper.Name,documentId,sorted,checks,manifest,hash,figures.Count);
    }

    public WorkspaceArchiveResult PublishWorkspaceArchiveE4(
        ExperimentWorkspace workspace,WorkspaceArchivePreview preview)
    {
        if(!preview.CanPublish)
            throw new InvalidOperationException("归档仍有需要修复的文件或关联。");
        using(var document=JsonDocument.Parse(preview.ManifestJson))
        {
            if(!document.RootElement.TryGetProperty("schemaVersion",out var version)
                ||version.GetInt32()!=3)
                throw new InvalidOperationException("请使用 E4 新版归档预检。");
        }
        var now=PrepareWorkspaceArchiveE4(workspace,preview.PaperId,preview.DocumentId);
        if(!now.CanPublish||now.ManifestSha256!=preview.ManifestSha256)
            throw new InvalidOperationException(
                "确认期间实验文件、Figure 或关联发生变化，请重新检查。");
        return PublishWorkspaceArchive(now);
    }

    public WorkspaceArchiveResult ArchiveWorkspacePaperE4(
        ExperimentWorkspace workspace,string paperId,string documentId)
    {
        var preview=PrepareWorkspaceArchiveE4(workspace,paperId,documentId);
        return PublishWorkspaceArchiveE4(workspace,preview);
    }
}
