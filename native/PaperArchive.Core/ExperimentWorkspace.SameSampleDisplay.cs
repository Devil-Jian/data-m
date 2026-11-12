namespace PaperArchive.Core.Workspace;

/// <summary>
/// A presentation-only summary: distinct Asset records are never deleted.
/// Same-stage, same-named files with identical bytes collapse into one row.
/// Same-named files with different hashes remain separate explicit variants.
/// </summary>
public sealed record SameSampleDisplayRow(
    string Stage,string FileName,string Sha256,
    int AssetCount,IReadOnlyList<string> AssetIds,int ContentVariant,int ContentVariantCount)
{
    public string DisplayText
    {
        get
        {
            var version=ContentVariantCount>1
                ?$" · 不同内容 {ContentVariant}/{ContentVariantCount} [{Sha256[..Math.Min(8,Sha256.Length)]}]"
                :"";
            var copies=AssetCount>1?$" · {AssetCount} 条 Asset 引用（同一内容）":"";
            return $"{Stage.ToUpperInvariant()}  {FileName}{version}{copies}";
        }
    }
}

public static class SameSampleDisplay
{
    public static IReadOnlyList<SameSampleDisplayRow> Group(
        IEnumerable<ExperimentAsset> assets)
    {
        var distinctAssets=assets
            .GroupBy(x=>x.Id,StringComparer.Ordinal)
            .Select(x=>x.First())
            .ToArray();
        var grouped=distinctAssets
            .GroupBy(x=>(Stage:x.Stage.ToLowerInvariant(),
                Name:x.FileName.ToUpperInvariant(),
                Sha:x.Sha256.ToUpperInvariant()))
            .Select(x=>new{
                x.Key.Stage,x.Key.Name,x.Key.Sha,
                Items=x.OrderBy(a=>a.Id,StringComparer.Ordinal).ToArray()
            })
            .GroupBy(x=>(x.Stage,x.Name))
            .SelectMany(group=>{
                var variants=group.OrderBy(x=>x.Sha,StringComparer.Ordinal).ToArray();
                return variants.Select((variant,index)=>new SameSampleDisplayRow(
                    variant.Stage,variant.Items[0].FileName,variant.Sha,
                    variant.Items.Length,variant.Items.Select(x=>x.Id).ToArray(),
                    index+1,variants.Length));
            });
        var order=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase)
        {["raw"]=0,["process"]=1,["show"]=2,["quant"]=3};
        return grouped.OrderBy(x=>order.GetValueOrDefault(x.Stage,4))
            .ThenBy(x=>x.FileName,StringComparer.OrdinalIgnoreCase)
            .ThenBy(x=>x.ContentVariant)
            .ToArray();
    }
}
