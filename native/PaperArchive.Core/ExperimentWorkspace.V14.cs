using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

/// <summary>An asset with its containing experiment result, used for project-wide browsing.</summary>
public sealed record ScopedExperimentAsset(ExperimentAsset Asset,string ResultName);

public sealed partial class ExperimentWorkspace
{
    public IReadOnlyList<ScopedExperimentAsset> ListScopeAssets(
        string? paperId=null,string? projectId=null,string? resultId=null)
    {
        if(paperId is null && projectId is null && resultId is null)
            return [];
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT a.id,a.result_id,a.group_id,g.prefix,a.stage,a.relative_path,
                   a.filename,a.size,a.sha256,r.name
            FROM experiment_assets a
            JOIN experiment_groups g ON g.id=a.group_id
            JOIN experiment_results r ON r.id=a.result_id
            JOIN experiment_projects p ON p.id=r.project_id
            WHERE a.is_deleted=0 AND r.is_deleted=0 AND p.is_deleted=0
              AND ($result IS NULL OR r.id=$result)
              AND ($project IS NULL OR p.id=$project)
              AND ($paper IS NULL OR p.paper_id=$paper)
            ORDER BY r.name COLLATE NOCASE,a.relative_path COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("$result",(object?)resultId??DBNull.Value);
        cmd.Parameters.AddWithValue("$project",(object?)projectId??DBNull.Value);
        cmd.Parameters.AddWithValue("$paper",(object?)paperId??DBNull.Value);
        using var rd=cmd.ExecuteReader();
        var list=new List<ScopedExperimentAsset>();
        while(rd.Read())
        {
            list.Add(new(new ExperimentAsset(
                rd.GetString(0),rd.GetString(1),rd.GetString(2),rd.GetString(3),
                rd.GetString(4),rd.GetString(5),rd.GetString(6),rd.GetInt64(7),
                rd.GetString(8)),rd.GetString(9)));
        }
        return list;
    }

    /// <summary>
    /// Drop on an experiment project targets its existing "待整理数据" unit,
    /// creating that unit only when needed. Never affects an original disk path.
    /// </summary>
    public string GetOrCreateUnsortedResult(string projectId)
    {
        using var db=Connect();
        using var check=db.CreateCommand();
        check.CommandText="SELECT 1 FROM experiment_projects WHERE id=$id AND is_deleted=0";
        check.Parameters.AddWithValue("$id",projectId);
        if(check.ExecuteScalar() is null)
            throw new KeyNotFoundException("目标实验项目不存在。");
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT id FROM experiment_results
            WHERE project_id=$project AND name='待整理数据' AND is_deleted=0
            ORDER BY imported_at LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$project",projectId);
        if(cmd.ExecuteScalar() is string existing)return existing;
        return CreateResult(projectId,"待整理数据");
    }
}
