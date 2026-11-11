using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

/// <summary>Read-only system-generated labels. This is not a processing provenance edge.</summary>
public sealed record AssetSystemTags(string AssetId,string ExperimentName,string? SampleName,
    string Stage,string? ProcessSubtype,int FileVersion,string ParseStatus,string SourceKind,
    int ParserVersion);

public sealed record ParsedAssetName(string? SampleName,string Stage,string? ProcessSubtype,
    int FileVersion,string ParseStatus);

public sealed partial class ExperimentWorkspace
{
    private const int SystemTagParserVersion=1;
    private static readonly Regex TagVersionSuffix=new(
        @"(?i)[_-]v([1-9][0-9]*)$",RegexOptions.Compiled|RegexOptions.CultureInvariant);
    private static readonly Regex TagFixedSuffix=new(
        @"(?i)^(.+?)[_-](raw|show|quant|process)$",
        RegexOptions.Compiled|RegexOptions.CultureInvariant);
    private static readonly HashSet<string> TagImageExtensions=
        new(StringComparer.OrdinalIgnoreCase)
        {".tif",".tiff",".png",".jpg",".jpeg",".bmp",".gif",".webp"};

    /// <summary>
    /// Only an explicit filename suffix fixes a stage. PROCESS subtype uses a
    /// known sample anchor; without one the result remains unresolved.
    /// </summary>
    public static ParsedAssetName ParseAssetName(string fileName,string? sampleAnchor=null)
    {
        var stem=Path.GetFileNameWithoutExtension(fileName).Trim();
        if(stem.Length==0)return new(null,"other",null,1,"unresolved");
        var version=1;
        var suffix=TagVersionSuffix.Match(stem);
        if(suffix.Success)
        {
            if(!int.TryParse(suffix.Groups[1].Value,out version))
                return new(null,"other",null,1,"unresolved");
            stem=stem[..suffix.Index];
        }
        var fixedStage=TagFixedSuffix.Match(stem);
        if(fixedStage.Success)
        {
            var name=fixedStage.Groups[1].Value.TrimEnd('_','-',' ');
            var stage=fixedStage.Groups[2].Value.ToLowerInvariant();
            return name.Length==0?
                new(null,stage,null,version,"unresolved"):
                new(name,stage,null,version,"resolved");
        }
        if(!string.IsNullOrWhiteSpace(sampleAnchor))
        {
            var name=sampleAnchor.Trim();
            if(stem.StartsWith(name+"_",StringComparison.OrdinalIgnoreCase)||
               stem.StartsWith(name+"-",StringComparison.OrdinalIgnoreCase))
            {
                var subtype=stem[(name.Length+1)..].Trim();
                return subtype.Length==0?
                    new(null,"process",null,version,"unresolved"):
                    new(name,"process",subtype,version,"resolved");
            }
        }
        return new(null,"other",null,version,"unresolved");
    }

    private void InitializeSystemTags()
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS experiment_result_labels(
              result_id TEXT PRIMARY KEY REFERENCES experiment_results(id),
              experiment_name TEXT NOT NULL,
              source_kind TEXT NOT NULL CHECK(source_kind IN ('root_folder','legacy_fallback'))
            );
            CREATE TABLE IF NOT EXISTS asset_system_tags(
              asset_id TEXT PRIMARY KEY REFERENCES experiment_assets(id),
              experiment_name TEXT NOT NULL,
              sample_name TEXT,
              stage TEXT NOT NULL,
              process_subtype TEXT,
              file_version INTEGER NOT NULL CHECK(file_version>=1),
              parse_status TEXT NOT NULL CHECK(parse_status IN ('resolved','unresolved','conflict')),
              source_kind TEXT NOT NULL CHECK(source_kind IN ('root_folder','legacy_fallback')),
              parser_version INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_asset_system_tags_sample
              ON asset_system_tags(experiment_name,sample_name,stage);
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Called in the same SQLite transaction as the import / sample-index
    /// update. Do not call on warehouse open: legacy reindex is explicit.
    /// </summary>
    private static void RefreshSystemTags(SqliteConnection db,SqliteTransaction tx,string projectId)
    {
        using var query=db.CreateCommand();
        query.Transaction=tx;
        query.CommandText="""
            SELECT a.id,a.filename,a.relative_path,a.stage,
                   COALESCE(s.sample_key,''),
                   COALESCE(labels.experiment_name,r.name),
                   COALESCE(labels.source_kind,'legacy_fallback')
            FROM experiment_assets a
            JOIN experiment_results r ON r.id=a.result_id
            LEFT JOIN sample_asset_index s ON s.asset_id=a.id
            LEFT JOIN experiment_result_labels labels ON labels.result_id=r.id
            WHERE r.project_id=$project AND r.is_deleted=0 AND a.is_deleted=0
            ORDER BY a.id
            """;
        query.Parameters.AddWithValue("$project",projectId);
        var records=new List<(string Id,string Name,string Path,string Stage,
            string Sample,string Experiment,string Source)>();
        using(var rd=query.ExecuteReader())
            while(rd.Read())
                records.Add((rd.GetString(0),rd.GetString(1),rd.GetString(2),
                    rd.GetString(3),rd.GetString(4),rd.GetString(5),rd.GetString(6)));
        using(var delete=db.CreateCommand())
        {
            delete.Transaction=tx;
            delete.CommandText="""
                DELETE FROM asset_system_tags WHERE asset_id IN(
                  SELECT a.id FROM experiment_assets a
                  JOIN experiment_results r ON r.id=a.result_id
                  WHERE r.project_id=$project)
                """;
            delete.Parameters.AddWithValue("$project",projectId);
            delete.ExecuteNonQuery();
        }
        foreach(var item in records)
        {
            var parsed=ParseAssetName(item.Name,
                item.Sample.Length==0?null:item.Sample);
            // Known explicit suffixes take priority in the tag projection.
            // Existing asset.stage is preserved for compatibility with old data.
            var explicitStage=parsed.Stage is "raw" or "show" or "quant" or "process";
            var stage=explicitStage?parsed.Stage:item.Stage;
            var sample=parsed.SampleName??(item.Sample.Length==0?null:item.Sample);
            var processSubtype=stage=="process"?parsed.ProcessSubtype:null;
            var status=sample is null?"unresolved":"resolved";
            if(parsed.ParseStatus=="unresolved" && item.Sample.Length==0)
                status="unresolved";
            if(explicitStage && item.Stage is "raw" or "process" or "show" or "quant"
                && stage!=item.Stage)status="conflict";
            if(explicitStage && item.Stage=="statistics")status="conflict";
            // An explicit filename tag that disagrees with a stage directory
            // needs review; never hide the contradictory import evidence.
            var folders=item.Path.Replace('\\','/').Split('/');
            var folderStage=folders.Take(Math.Max(0,folders.Length-1))
                .Select(x=>x.ToLowerInvariant() switch
                {
                    "raw_data"=>"raw","process_data"=>"process",
                    "show_data"=>"show","quant_data"=>"quant",_=>null
                }).FirstOrDefault(x=>x is not null);
            if(explicitStage && folderStage is not null && stage!=folderStage)
                status="conflict";
            using var insert=db.CreateCommand();
            insert.Transaction=tx;
            insert.CommandText="""
                INSERT INTO asset_system_tags(asset_id,experiment_name,sample_name,
                    stage,process_subtype,file_version,parse_status,source_kind,parser_version)
                VALUES($id,$experiment,$sample,$stage,$subtype,$version,$status,$source,$parser)
                """;
            insert.Parameters.AddWithValue("$id",item.Id);
            insert.Parameters.AddWithValue("$experiment",item.Experiment);
            insert.Parameters.AddWithValue("$sample",(object?)sample??DBNull.Value);
            insert.Parameters.AddWithValue("$stage",stage);
            insert.Parameters.AddWithValue("$subtype",(object?)processSubtype??DBNull.Value);
            insert.Parameters.AddWithValue("$version",parsed.FileVersion);
            insert.Parameters.AddWithValue("$status",status);
            insert.Parameters.AddWithValue("$source",item.Source);
            insert.Parameters.AddWithValue("$parser",SystemTagParserVersion);
            insert.ExecuteNonQuery();
        }
    }

    public AssetSystemTags? GetAssetSystemTags(string assetId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT asset_id,experiment_name,sample_name,stage,process_subtype,
                   file_version,parse_status,source_kind,parser_version
            FROM asset_system_tags WHERE asset_id=$id
            """;
        cmd.Parameters.AddWithValue("$id",assetId);
        using var rd=cmd.ExecuteReader();
        return rd.Read()?new AssetSystemTags(rd.GetString(0),rd.GetString(1),
            rd.IsDBNull(2)?null:rd.GetString(2),rd.GetString(3),
            rd.IsDBNull(4)?null:rd.GetString(4),rd.GetInt32(5),rd.GetString(6),
            rd.GetString(7),rd.GetInt32(8)):null;
    }
}
