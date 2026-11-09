using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

public sealed record PaperProject(string Id, string Name);
public sealed record FigureTemplate(string Id, string PaperId, int Number);
public sealed record PanelTemplate(string Id, string FigureId, string Label);

public sealed partial class ExperimentWorkspace
{
    // Schema v1.3 is additive: archive.db and its legacy migrations are unchanged.
    private void InitializeV13()
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS paper_projects(
                id TEXT PRIMARY KEY, name TEXT NOT NULL, created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS figure_templates(
                id TEXT PRIMARY KEY, paper_id TEXT NOT NULL REFERENCES paper_projects(id),
                number INTEGER NOT NULL CHECK(number>0), UNIQUE(paper_id,number)
            );
            CREATE TABLE IF NOT EXISTS panel_templates(
                id TEXT PRIMARY KEY, figure_id TEXT NOT NULL REFERENCES figure_templates(id),
                label TEXT NOT NULL, UNIQUE(figure_id,label)
            );
            CREATE TABLE IF NOT EXISTS workspace_audit(
                id TEXT PRIMARY KEY, kind TEXT NOT NULL, detail TEXT NOT NULL, created_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
        AddColumn(db,"experiment_projects","paper_id","TEXT REFERENCES paper_projects(id)");
        AddColumn(db,"experiment_assets","is_deleted","INTEGER NOT NULL DEFAULT 0");
        AddColumn(db,"experiment_usages","paper_id","TEXT REFERENCES paper_projects(id)");
        using var count=db.CreateCommand();
        count.CommandText="SELECT COUNT(*) FROM experiment_projects WHERE paper_id IS NULL";
        if(Convert.ToInt64(count.ExecuteScalar())>0)
        {
            using var tx=db.BeginTransaction();
            var legacyPaper=NewId();
            using(var ins=db.CreateCommand())
            {
                ins.Transaction=tx;
                ins.CommandText="INSERT INTO paper_projects(id,name,created_at) VALUES($id,$name,$now)";
                ins.Parameters.AddWithValue("$id",legacyPaper);
                ins.Parameters.AddWithValue("$name","历史工作区（待分配）");
                ins.Parameters.AddWithValue("$now",UtcNow());
                ins.ExecuteNonQuery();
            }
            using(var adopt=db.CreateCommand())
            {
                adopt.Transaction=tx;
                adopt.CommandText="UPDATE experiment_projects SET paper_id=$paper WHERE paper_id IS NULL";
                adopt.Parameters.AddWithValue("$paper",legacyPaper);
                adopt.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    private static void AddColumn(SqliteConnection db,string table,string column,string definition)
    {
        using var query=db.CreateCommand();
        query.CommandText=$"PRAGMA table_info({table})";
        using(var rd=query.ExecuteReader())
            while(rd.Read()) if(rd.GetString(1)==column)return;
        using var alter=db.CreateCommand();
        alter.CommandText=$"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    public IReadOnlyList<PaperProject> ListPaperProjects()
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT id,name FROM paper_projects ORDER BY created_at DESC";
        using var rd=cmd.ExecuteReader();
        var result=new List<PaperProject>();
        while(rd.Read())result.Add(new(rd.GetString(0),rd.GetString(1)));
        return result;
    }

    public string CreatePaperProject(string name)
    {
        name=CheckedName(name);
        var id=NewId();
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="INSERT INTO paper_projects(id,name,created_at) VALUES($id,$name,$now)";
        cmd.Parameters.AddWithValue("$id",id);
        cmd.Parameters.AddWithValue("$name",name);
        cmd.Parameters.AddWithValue("$now",UtcNow());
        cmd.ExecuteNonQuery();
        return id;
    }

    private static string CheckedName(string value)
    {
        if(string.IsNullOrWhiteSpace(value)||value.Trim().Length>180||
           value.IndexOfAny(['/', '\\', '\0'])>=0)
            throw new ArgumentException("名称不能为空、过长或包含路径分隔符。");
        return value.Trim();
    }

    public IReadOnlyList<ExperimentProject> ListProjectsForPaper(string paperId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT id,name FROM experiment_projects WHERE paper_id=$id AND is_deleted=0 ORDER BY name COLLATE NOCASE";
        cmd.Parameters.AddWithValue("$id",paperId);
        using var rd=cmd.ExecuteReader();
        var result=new List<ExperimentProject>();
        while(rd.Read())result.Add(new(rd.GetString(0),rd.GetString(1)));
        return result;
    }

    public string CreateProjectForPaper(string paperId,string name)
    {
        var id=CreateProject(CheckedName(name));
        AssignProjectToPaper(id,paperId);
        return id;
    }

    public void AssignProjectToPaper(string projectId,string paperId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="UPDATE experiment_projects SET paper_id=$paper WHERE id=$project AND EXISTS(SELECT 1 FROM paper_projects WHERE id=$paper)";
        cmd.Parameters.AddWithValue("$paper",paperId);
        cmd.Parameters.AddWithValue("$project",projectId);
        if(cmd.ExecuteNonQuery()!=1)throw new InvalidOperationException("论文或实验项目不存在。");
    }

    public void RenameProject(string id,string name) =>
        RenameEntity("experiment_projects",id,CheckedName(name));

    public void RenamePaperProject(string id,string name) =>
        RenameEntity("paper_projects",id,CheckedName(name));

    public void RenameResult(string id,string name) =>
        RenameEntity("experiment_results",id,CheckedName(name));

    private void RenameEntity(string table,string id,string name)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText=$"UPDATE {table} SET name=$name WHERE id=$id";
        cmd.Parameters.AddWithValue("$name",name);
        cmd.Parameters.AddWithValue("$id",id);
        if(cmd.ExecuteNonQuery()!=1)throw new KeyNotFoundException("项目不存在。");
    }

    public string CreateResult(string projectId,string name)
    {
        name=CheckedName(name);
        var id=NewId();
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="INSERT INTO experiment_results(id,project_id,name,imported_at) VALUES($id,$project,$name,$now)";
        cmd.Parameters.AddWithValue("$id",id);
        cmd.Parameters.AddWithValue("$project",projectId);
        cmd.Parameters.AddWithValue("$name",name);
        cmd.Parameters.AddWithValue("$now",UtcNow());
        cmd.ExecuteNonQuery();
        return id;
    }

    public WorkspaceImportResult ImportUnsorted(string projectId,string sourceDirectory)
    {
        var imported=ImportResult(projectId,sourceDirectory);
        RenameResult(imported.ResultId,"待整理数据");
        return imported;
    }

    public void RenameAsset(string assetId,string newName)
    {
        newName=CheckedName(newName);
        using var db=Connect();
        using var tx=db.BeginTransaction();
        string path;
        using(var get=db.CreateCommand())
        {
            get.Transaction=tx;
            get.CommandText="SELECT relative_path FROM experiment_assets WHERE id=$id AND is_deleted=0";
            get.Parameters.AddWithValue("$id",assetId);
            path=get.ExecuteScalar() as string ?? throw new KeyNotFoundException("文件不存在。");
        }
        var slash=path.LastIndexOf('/');
        var newRelative=(slash<0?"":path[..(slash+1)])+newName;
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="UPDATE experiment_assets SET filename=$name,relative_path=$path WHERE id=$id";
        cmd.Parameters.AddWithValue("$name",newName);
        cmd.Parameters.AddWithValue("$path",newRelative);
        cmd.Parameters.AddWithValue("$id",assetId);
        cmd.ExecuteNonQuery();
        tx.Commit();
        // Rename is logical; hash, Asset ID and Data Group ID never change.
    }

    public void SetAssetDeleted(IEnumerable<string> ids,bool deleted)
    {
        var selected=ids.Distinct(StringComparer.Ordinal).ToArray();
        if(selected.Length==0)return;
        using var db=Connect();
        using var tx=db.BeginTransaction();
        foreach(var id in selected)
        {
            if(deleted)
            {
                using var check=db.CreateCommand();
                check.Transaction=tx;
                check.CommandText="""
                    SELECT COUNT(*) FROM experiment_usages
                    WHERE asset_id=$id AND state='confirmed'
                    """;
                check.Parameters.AddWithValue("$id",id);
                if(Convert.ToInt64(check.ExecuteScalar())>0)
                    throw new InvalidOperationException("包含已确认论文引用的文件不可直接删除，请先处理使用关系。");
            }
            using var cmd=db.CreateCommand();
            cmd.Transaction=tx;
            cmd.CommandText="UPDATE experiment_assets SET is_deleted=$deleted WHERE id=$id";
            cmd.Parameters.AddWithValue("$deleted",deleted?1:0);
            cmd.Parameters.AddWithValue("$id",id);
            if(cmd.ExecuteNonQuery()!=1)throw new KeyNotFoundException("文件不存在。");
        }
        tx.Commit();
    }

    public IReadOnlyList<ExperimentAsset> ListDeletedAssets(string resultId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT a.id,a.result_id,a.group_id,g.prefix,a.stage,a.relative_path,a.filename,a.size,a.sha256
            FROM experiment_assets a JOIN experiment_groups g ON g.id=a.group_id
            WHERE a.result_id=$id AND a.is_deleted=1 ORDER BY a.relative_path
            """;
        cmd.Parameters.AddWithValue("$id",resultId);
        using var rd=cmd.ExecuteReader();
        var list=new List<ExperimentAsset>();
        while(rd.Read())list.Add(new(rd.GetString(0),rd.GetString(1),rd.GetString(2),rd.GetString(3),
            rd.GetString(4),rd.GetString(5),rd.GetString(6),rd.GetInt64(7),rd.GetString(8)));
        return list;
    }

    // Transactional logical move; immutable blobs and Asset IDs never change.
    // Colliding group prefixes require explicit consent to merge groups.
    public int MoveAssets(IEnumerable<string> assetIds,string targetResultId,bool mergeGroups=false)
    {
        var ids=assetIds.Distinct(StringComparer.Ordinal).ToArray();
        if(ids.Length==0)return 0;
        using var db=Connect();
        using var tx=db.BeginTransaction();
        using(var target=db.CreateCommand())
        {
            target.Transaction=tx;
            target.CommandText="SELECT COUNT(*) FROM experiment_results WHERE id=$id";
            target.Parameters.AddWithValue("$id",targetResultId);
            if(Convert.ToInt64(target.ExecuteScalar())!=1)throw new KeyNotFoundException("目标实验结果单元不存在。");
        }
        var inputs=new List<(string Id,string Result,string Group,string Prefix,string Relative)>();
        foreach(var id in ids)
        {
            using var get=db.CreateCommand();
            get.Transaction=tx;
            get.CommandText="""
                SELECT a.result_id,a.group_id,g.prefix,a.relative_path
                FROM experiment_assets a JOIN experiment_groups g ON g.id=a.group_id
                WHERE a.id=$id AND a.is_deleted=0
                """;
            get.Parameters.AddWithValue("$id",id);
            using var rd=get.ExecuteReader();
            if(!rd.Read())throw new KeyNotFoundException("源文件不存在："+id);
            inputs.Add((id,rd.GetString(0),rd.GetString(1),rd.GetString(2),rd.GetString(3)));
        }
        inputs.RemoveAll(x=>x.Result==targetResultId);
        if(inputs.Count==0)return 0;
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in inputs)
        {
            if(!paths.Add(item.Relative))throw new IOException("所选文件之间存在重名路径："+item.Relative);
            using var conflict=db.CreateCommand();
            conflict.Transaction=tx;
            conflict.CommandText="SELECT COUNT(*) FROM experiment_assets WHERE result_id=$result AND relative_path=$path";
            conflict.Parameters.AddWithValue("$result",targetResultId);
            conflict.Parameters.AddWithValue("$path",item.Relative);
            if(Convert.ToInt64(conflict.ExecuteScalar())>0)
                throw new IOException("目标实验结果已有同名路径："+item.Relative);
        }
        var oldOwners=inputs.Select(x=>x.Group).Distinct().ToDictionary(g=>g,g=>GetGroupOwner(db,tx,g));
        foreach(var group in inputs.GroupBy(x=>x.Group,StringComparer.Ordinal))
        {
            var original=group.First();
            string? existing;
            using(var get=db.CreateCommand())
            {
                get.Transaction=tx;
                get.CommandText="SELECT id FROM experiment_groups WHERE result_id=$result AND prefix=$prefix COLLATE NOCASE";
                get.Parameters.AddWithValue("$result",targetResultId);
                get.Parameters.AddWithValue("$prefix",original.Prefix);
                existing=get.ExecuteScalar() as string;
            }
            if(existing is not null&&!mergeGroups)
                throw new InvalidOperationException("目标已有数据组 "+original.Prefix+"；请明确选择合并。");
            string targetGroup;
            if(existing is not null) targetGroup=existing;
            else
            {
                using var count=db.CreateCommand();
                count.Transaction=tx;
                count.CommandText="SELECT COUNT(*) FROM experiment_assets WHERE group_id=$group";
                count.Parameters.AddWithValue("$group",original.Group);
                var allCount=Convert.ToInt64(count.ExecuteScalar());
                if(allCount==group.Count())
                {
                    using var relocate=db.CreateCommand();
                    relocate.Transaction=tx;
                    relocate.CommandText="UPDATE experiment_groups SET result_id=$target WHERE id=$id";
                    relocate.Parameters.AddWithValue("$target",targetResultId);
                    relocate.Parameters.AddWithValue("$id",original.Group);
                    relocate.ExecuteNonQuery();
                    targetGroup=original.Group;
                }
                else
                {
                    targetGroup=NewId();
                    using var create=db.CreateCommand();
                    create.Transaction=tx;
                    create.CommandText="INSERT INTO experiment_groups(id,result_id,prefix) VALUES($id,$result,$prefix)";
                    create.Parameters.AddWithValue("$id",targetGroup);
                    create.Parameters.AddWithValue("$result",targetResultId);
                    create.Parameters.AddWithValue("$prefix",original.Prefix);
                    create.ExecuteNonQuery();
                }
            }
            foreach(var item in group)
            {
                using var move=db.CreateCommand();
                move.Transaction=tx;
                move.CommandText="UPDATE experiment_assets SET result_id=$result,group_id=$group WHERE id=$id";
                move.Parameters.AddWithValue("$result",targetResultId);
                move.Parameters.AddWithValue("$group",targetGroup);
                move.Parameters.AddWithValue("$id",item.Id);
                move.ExecuteNonQuery();
            }
        }
        var moved=inputs.Select(x=>
        {
            using var locate=db.CreateCommand();
            locate.Transaction=tx;
            locate.CommandText="SELECT result_id,group_id FROM experiment_assets WHERE id=$id";
            locate.Parameters.AddWithValue("$id",x.Id);
            using var rd=locate.ExecuteReader();
            if(!rd.Read())throw new InvalidDataException("移动记录丢失。");
            return new UndoAssetMove(x.Id,x.Result,x.Group,rd.GetString(0),rd.GetString(1),x.Relative);
        }).ToArray();
        var owners=oldOwners.Where(x=>GetGroupOwner(db,tx,x.Key)!=x.Value)
            .Select(x=>new UndoGroupOwner(x.Key,x.Value)).ToArray();
        RecordUndo(db,tx,"assets",System.Text.Json.JsonSerializer.Serialize(
            new UndoAssetBatch(moved,owners)));
        tx.Commit();
        return inputs.Count;
    }

    public int CopyAssets(IEnumerable<string> assetIds,string targetResultId,bool mergeGroups=false)
    {
        var ids=assetIds.Distinct(StringComparer.Ordinal).ToArray();
        if(ids.Length==0)return 0;
        using var db=Connect();
        using var tx=db.BeginTransaction();
        var copies=new List<(string Group,string Prefix,string Stage,string Path,string Name,long Size,string Sha)>();
        foreach(var id in ids)
        {
            using var cmd=db.CreateCommand();
            cmd.Transaction=tx;
            cmd.CommandText="""
                SELECT a.group_id,g.prefix,a.stage,a.relative_path,a.filename,a.size,a.sha256
                FROM experiment_assets a JOIN experiment_groups g ON g.id=a.group_id
                WHERE a.id=$id AND a.is_deleted=0
                """;
            cmd.Parameters.AddWithValue("$id",id);
            using var rd=cmd.ExecuteReader();
            if(!rd.Read())throw new KeyNotFoundException("源文件不存在："+id);
            copies.Add((rd.GetString(0),rd.GetString(1),rd.GetString(2),rd.GetString(3),
                rd.GetString(4),rd.GetInt64(5),rd.GetString(6)));
        }
        var groupIds=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var copy in copies)
        {
            if(!groupIds.TryGetValue(copy.Group,out var groupId))
            {
                using var find=db.CreateCommand();
                find.Transaction=tx;
                find.CommandText="SELECT id FROM experiment_groups WHERE result_id=$id AND prefix=$prefix COLLATE NOCASE";
                find.Parameters.AddWithValue("$id",targetResultId);
                find.Parameters.AddWithValue("$prefix",copy.Prefix);
                groupId=find.ExecuteScalar() as string ?? "";
                if(groupId!=""&&!mergeGroups)throw new InvalidOperationException("目标存在同名数据组，请确认合并。");
                if(groupId=="")
                {
                    groupId=NewId();
                    using var add=db.CreateCommand();
                    add.Transaction=tx;
                    add.CommandText="INSERT INTO experiment_groups(id,result_id,prefix) VALUES($id,$result,$prefix)";
                    add.Parameters.AddWithValue("$id",groupId);
                    add.Parameters.AddWithValue("$result",targetResultId);
                    add.Parameters.AddWithValue("$prefix",copy.Prefix);
                    add.ExecuteNonQuery();
                }
                groupIds[copy.Group]=groupId;
            }
            using var ins=db.CreateCommand();
            ins.Transaction=tx;
            ins.CommandText="""
                INSERT INTO experiment_assets(id,result_id,group_id,stage,relative_path,filename,size,sha256,imported_at)
                VALUES($id,$result,$group,$stage,$path,$name,$size,$sha,$now)
                """;
            ins.Parameters.AddWithValue("$id",NewId());
            ins.Parameters.AddWithValue("$result",targetResultId);
            ins.Parameters.AddWithValue("$group",groupId);
            ins.Parameters.AddWithValue("$stage",copy.Stage);
            ins.Parameters.AddWithValue("$path",copy.Path);
            ins.Parameters.AddWithValue("$name",copy.Name);
            ins.Parameters.AddWithValue("$size",copy.Size);
            ins.Parameters.AddWithValue("$sha",copy.Sha);
            ins.Parameters.AddWithValue("$now",UtcNow());
            ins.ExecuteNonQuery();
        }
        tx.Commit();
        return copies.Count;
    }

    public IReadOnlyList<FigureTemplate> ListFigures(string paperId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT id,paper_id,number FROM figure_templates WHERE paper_id=$id ORDER BY number";
        cmd.Parameters.AddWithValue("$id",paperId);
        using var rd=cmd.ExecuteReader();
        var list=new List<FigureTemplate>();
        while(rd.Read())list.Add(new(rd.GetString(0),rd.GetString(1),rd.GetInt32(2)));
        return list;
    }

    public string CreateFigure(string paperId,int? number=null)
    {
        using var db=Connect();
        int numberValue=number ?? (ListFigures(paperId).Select(x=>x.Number).DefaultIfEmpty(0).Max()+1);
        if(numberValue<=0)throw new ArgumentException("Figure 编号必须大于零。");
        var id=NewId();
        using var cmd=db.CreateCommand();
        cmd.CommandText="INSERT INTO figure_templates(id,paper_id,number) VALUES($id,$paper,$number)";
        cmd.Parameters.AddWithValue("$id",id);
        cmd.Parameters.AddWithValue("$paper",paperId);
        cmd.Parameters.AddWithValue("$number",numberValue);
        cmd.ExecuteNonQuery();
        return id;
    }

    public IReadOnlyList<PanelTemplate> ListPanels(string figureId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT id,figure_id,label FROM panel_templates WHERE figure_id=$id ORDER BY label";
        cmd.Parameters.AddWithValue("$id",figureId);
        using var rd=cmd.ExecuteReader();
        var result=new List<PanelTemplate>();
        while(rd.Read())result.Add(new(rd.GetString(0),rd.GetString(1),rd.GetString(2)));
        return result;
    }

    public string CreatePanel(string figureId,string label)
    {
        label=CheckedName(label).ToUpperInvariant();
        if(label.Length>12)throw new ArgumentException("Panel 标签过长。");
        var id=NewId();
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="INSERT INTO panel_templates(id,figure_id,label) VALUES($id,$figure,$label)";
        cmd.Parameters.AddWithValue("$id",id);
        cmd.Parameters.AddWithValue("$figure",figureId);
        cmd.Parameters.AddWithValue("$label",label);
        cmd.ExecuteNonQuery();
        return id;
    }

    public void AssignUsageToPaper(string usageId,string paperId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="UPDATE experiment_usages SET paper_id=$paper WHERE id=$id";
        cmd.Parameters.AddWithValue("$paper",paperId);
        cmd.Parameters.AddWithValue("$id",usageId);
        if(cmd.ExecuteNonQuery()!=1)throw new KeyNotFoundException("使用记录不存在。");
    }
}
