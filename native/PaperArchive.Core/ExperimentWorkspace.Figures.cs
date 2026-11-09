using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

public sealed record PanelAssetLink(
    string Id,string PanelId,string AssetId,string FileName,string GroupPrefix,
    string ResultName,string Stage,int Position,string Sha256,long Size);
public sealed record FigureComposite(
    string Id,string FigureId,string FileName,string Sha256,long Size,
    int Version,bool IsCurrent,string ImportedAt);
public sealed record FigureOverview(FigureTemplate Figure,int PanelCount,int LinkedAssetCount,
    int CompositeCount,string? CurrentFileName);
public sealed record PanelOverview(PanelTemplate Panel,int LinkedAssetCount);
public sealed record FigureMaterialExport(int ExportedCount,string TargetFolder,string ManifestPath);

public sealed partial class ExperimentWorkspace
{
    private void InitializeFigureWorkspace()
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS figure_panel_assets(
              id TEXT PRIMARY KEY,
              panel_id TEXT NOT NULL REFERENCES panel_templates(id) ON DELETE CASCADE,
              asset_id TEXT NOT NULL REFERENCES experiment_assets(id),
              position INTEGER NOT NULL,
              created_at TEXT NOT NULL,
              UNIQUE(panel_id,asset_id)
            );
            CREATE INDEX IF NOT EXISTS ix_figure_panel_assets_panel
              ON figure_panel_assets(panel_id,position);
            CREATE TABLE IF NOT EXISTS figure_composites(
              id TEXT PRIMARY KEY,
              figure_id TEXT NOT NULL REFERENCES figure_templates(id),
              filename TEXT NOT NULL,
              sha256 TEXT NOT NULL,
              size INTEGER NOT NULL,
              version INTEGER NOT NULL,
              is_current INTEGER NOT NULL DEFAULT 0,
              imported_at TEXT NOT NULL,
              UNIQUE(figure_id,version)
            );
            CREATE INDEX IF NOT EXISTS ix_figure_composites_figure
              ON figure_composites(figure_id,version);
            CREATE TABLE IF NOT EXISTS figure_composite_sources(
              composite_id TEXT NOT NULL REFERENCES figure_composites(id),
              link_id TEXT NOT NULL REFERENCES figure_panel_assets(id),
              PRIMARY KEY(composite_id,link_id)
            );
            CREATE TABLE IF NOT EXISTS paper_document_figures(
              document_id TEXT NOT NULL REFERENCES paper_documents(id),
              figure_id TEXT NOT NULL REFERENCES figure_templates(id),
              PRIMARY KEY(document_id,figure_id)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<FigureOverview> ListFigureOverview(string paperId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT f.id,f.paper_id,f.number,
                   (SELECT COUNT(*) FROM panel_templates p WHERE p.figure_id=f.id),
                   (SELECT COUNT(*) FROM figure_panel_assets a JOIN panel_templates p
                    ON a.panel_id=p.id WHERE p.figure_id=f.id),
                   (SELECT COUNT(*) FROM figure_composites c WHERE c.figure_id=f.id),
                   (SELECT filename FROM figure_composites c WHERE c.figure_id=f.id
                    AND c.is_current=1 LIMIT 1)
            FROM figure_templates f WHERE f.paper_id=$paper ORDER BY f.number
            """;
        cmd.Parameters.AddWithValue("$paper",paperId);
        using var rd=cmd.ExecuteReader();
        var list=new List<FigureOverview>();
        while(rd.Read())list.Add(new(
            new FigureTemplate(rd.GetString(0),rd.GetString(1),rd.GetInt32(2)),
            rd.GetInt32(3),rd.GetInt32(4),rd.GetInt32(5),
            rd.IsDBNull(6)?null:rd.GetString(6)));
        return list;
    }

    public IReadOnlyList<PanelOverview> ListPanelOverview(string figureId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT p.id,p.figure_id,p.label,
                   (SELECT COUNT(*) FROM figure_panel_assets a WHERE a.panel_id=p.id)
            FROM panel_templates p WHERE p.figure_id=$figure ORDER BY p.label
            """;
        cmd.Parameters.AddWithValue("$figure",figureId);
        using var rd=cmd.ExecuteReader();
        var list=new List<PanelOverview>();
        while(rd.Read())list.Add(new(new PanelTemplate(rd.GetString(0),rd.GetString(1),
            rd.GetString(2)),rd.GetInt32(3)));
        return list;
    }

    private static void EnsurePaper(SqliteConnection db,SqliteTransaction tx,string paperId)
    {
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="SELECT COUNT(*) FROM paper_projects WHERE id=$id";
        cmd.Parameters.AddWithValue("$id",paperId);
        if(Convert.ToInt64(cmd.ExecuteScalar())!=1)throw new KeyNotFoundException("论文项目不存在。");
    }

    public IReadOnlyList<FigureTemplate> CreateFiguresFast(string paperId,int count,
        int panelsPerFigure=0,int? startNumber=null)
    {
        if(count<1||count>100||panelsPerFigure<0||panelsPerFigure>26)
            throw new ArgumentOutOfRangeException(nameof(count),"批量创建数量超出安全范围。");
        using var db=Connect();
        using var tx=db.BeginTransaction();
        EnsurePaper(db,tx,paperId);
        var occupied=new HashSet<int>();
        using(var list=db.CreateCommand())
        {
            list.Transaction=tx;
            list.CommandText="SELECT number FROM figure_templates WHERE paper_id=$paper";
            list.Parameters.AddWithValue("$paper",paperId);
            using var rd=list.ExecuteReader();
            while(rd.Read())occupied.Add(rd.GetInt32(0));
        }
        int number=startNumber??(occupied.Count==0?1:occupied.Max()+1);
        if(number<1)throw new ArgumentOutOfRangeException(nameof(startNumber));
        var created=new List<FigureTemplate>();
        for(int i=0;i<count;i++)
        {
            while(occupied.Contains(number))
            {
                if(number==int.MaxValue)throw new InvalidOperationException("Figure 编号已达到上限。");
                number++;
            }
            var figure=new FigureTemplate(NewId(),paperId,number);
            using(var cmd=db.CreateCommand())
            {
                cmd.Transaction=tx;
                cmd.CommandText="INSERT INTO figure_templates(id,paper_id,number) VALUES($id,$paper,$number)";
                cmd.Parameters.AddWithValue("$id",figure.Id);
                cmd.Parameters.AddWithValue("$paper",paperId);
                cmd.Parameters.AddWithValue("$number",figure.Number);
                cmd.ExecuteNonQuery();
            }
            for(int panelIndex=0;panelIndex<panelsPerFigure;panelIndex++)
                InsertPanel(db,tx,figure.Id,((char)('A'+panelIndex)).ToString());
            occupied.Add(number);
            created.Add(figure);
            if(i<count-1)
            {
                if(number==int.MaxValue)throw new InvalidOperationException("Figure 编号已达到上限。");
                number++;
            }
        }
        tx.Commit();
        return created;
    }

    private static void InsertPanel(SqliteConnection db,SqliteTransaction tx,
        string figureId,string label)
    {
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="INSERT INTO panel_templates(id,figure_id,label) VALUES($id,$figure,$label)";
        cmd.Parameters.AddWithValue("$id",NewId());
        cmd.Parameters.AddWithValue("$figure",figureId);
        cmd.Parameters.AddWithValue("$label",label);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<PanelTemplate> CreatePanelsFast(string figureId,int count,
        string? startLabel=null)
    {
        if(count<1||count>26)throw new ArgumentOutOfRangeException(nameof(count));
        using var db=Connect();
        using var tx=db.BeginTransaction();
        using(var verify=db.CreateCommand())
        {
            verify.Transaction=tx;
            verify.CommandText="SELECT COUNT(*) FROM figure_templates WHERE id=$id";
            verify.Parameters.AddWithValue("$id",figureId);
            if(Convert.ToInt64(verify.ExecuteScalar())!=1)
                throw new KeyNotFoundException("Figure 不存在。");
        }
        var existing=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using(var query=db.CreateCommand())
        {
            query.Transaction=tx;
            query.CommandText="SELECT label FROM panel_templates WHERE figure_id=$figure";
            query.Parameters.AddWithValue("$figure",figureId);
            using var rd=query.ExecuteReader();
            while(rd.Read())existing.Add(rd.GetString(0));
        }
        var start=startLabel is null ? (existing.Count==0?'A':
            (char)(existing.Where(x=>x.Length==1 && x[0]>='A'&&x[0]<='Z')
                .Select(x=>x[0]).DefaultIfEmpty('@').Max()+1))
            : char.ToUpperInvariant(startLabel.Length==1?startLabel[0]:'\0');
        if(start<'A'||start>'Z')throw new InvalidOperationException("Panel A–Z 编号已用完。");
        var created=new List<PanelTemplate>();
        for(char candidate=start;candidate<='Z'&&created.Count<count;candidate++)
        {
            var label=candidate.ToString();
            if(existing.Contains(label))continue;
            InsertPanel(db,tx,figureId,label);
            // In the current transaction the inserted record can be looked up
            // consistently; never invent a Panel ID after insertion.
            using var read=db.CreateCommand();
            read.Transaction=tx;
            read.CommandText="SELECT id FROM panel_templates WHERE figure_id=$figure AND label=$label";
            read.Parameters.AddWithValue("$figure",figureId);
            read.Parameters.AddWithValue("$label",label);
            created.Add(new PanelTemplate((string)read.ExecuteScalar()!,figureId,label));
        }
        if(created.Count!=count)
            throw new InvalidOperationException("剩余 Panel 字母不足，未创建任何占位。");
        tx.Commit();
        return created;
    }


    public (int LinkedImages,int Composites) GetFigureDeletionImpact(string figureId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT (SELECT COUNT(*) FROM figure_panel_assets a
                    JOIN panel_templates p ON a.panel_id=p.id WHERE p.figure_id=$id),
                   (SELECT COUNT(*) FROM figure_composites WHERE figure_id=$id)
            WHERE EXISTS(SELECT 1 FROM figure_templates WHERE id=$id)
            """;
        cmd.Parameters.AddWithValue("$id",figureId);
        using var reader=cmd.ExecuteReader();
        if(!reader.Read())throw new KeyNotFoundException("Figure 不存在。");
        return(reader.GetInt32(0),reader.GetInt32(1));
    }

    public int GetPanelDeletionImpact(string panelId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT (SELECT COUNT(*) FROM figure_panel_assets WHERE panel_id=$id)
            WHERE EXISTS(SELECT 1 FROM panel_templates WHERE id=$id)
            """;
        cmd.Parameters.AddWithValue("$id",panelId);
        var count=cmd.ExecuteScalar();
        if(count is null)throw new KeyNotFoundException("Panel 不存在。");
        return Convert.ToInt32(count);
    }

    public void DeleteFigurePlaceholder(string figureId)
    {
        using var db=Connect();
        using var tx=db.BeginTransaction();
        void Execute(string sql)
        {
            using var command=db.CreateCommand();
            command.Transaction=tx;
            command.CommandText=sql;
            command.Parameters.AddWithValue("$id",figureId);
            command.ExecuteNonQuery();
        }
        Execute("DELETE FROM figure_composite_sources WHERE composite_id IN (SELECT id FROM figure_composites WHERE figure_id=$id)");
        Execute("DELETE FROM figure_composites WHERE figure_id=$id");
        Execute("DELETE FROM figure_panel_assets WHERE panel_id IN (SELECT id FROM panel_templates WHERE figure_id=$id)");
        Execute("DELETE FROM panel_templates WHERE figure_id=$id");
        Execute("DELETE FROM paper_document_figures WHERE figure_id=$id");
        using(var cmd=db.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText="DELETE FROM figure_templates WHERE id=$id";
            cmd.Parameters.AddWithValue("$id",figureId);
            if(cmd.ExecuteNonQuery()!=1)throw new KeyNotFoundException("Figure 不存在。");
        }
        tx.Commit();
    }

    public void DeletePanelPlaceholder(string panelId)
    {
        using var db=Connect();
        using var tx=db.BeginTransaction();
        using(var links=db.CreateCommand())
        {
            links.Transaction=tx;
            links.CommandText="DELETE FROM figure_panel_assets WHERE panel_id=$id";
            links.Parameters.AddWithValue("$id",panelId);
            links.ExecuteNonQuery();
        }
        using(var cmd=db.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText="DELETE FROM panel_templates WHERE id=$id";
            cmd.Parameters.AddWithValue("$id",panelId);
            if(cmd.ExecuteNonQuery()!=1)throw new KeyNotFoundException("Panel 不存在。");
        }
        tx.Commit();
    }

    public void RenameFigureNumber(string figureId,int number)
    {
        if(number<1)throw new ArgumentOutOfRangeException(nameof(number));
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="UPDATE figure_templates SET number=$number WHERE id=$id";
        cmd.Parameters.AddWithValue("$number",number);
        cmd.Parameters.AddWithValue("$id",figureId);
        if(cmd.ExecuteNonQuery()!=1)throw new KeyNotFoundException("Figure 不存在。");
    }

    public void RenamePanelLabel(string panelId,string label)
    {
        label=label.Trim().ToUpperInvariant();
        if(label.Length!=1||label[0]<'A'||label[0]>'Z')
            throw new ArgumentException("Panel 标签必须为 A–Z。",nameof(label));
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="UPDATE panel_templates SET label=$label WHERE id=$id";
        cmd.Parameters.AddWithValue("$label",label);
        cmd.Parameters.AddWithValue("$id",panelId);
        if(cmd.ExecuteNonQuery()!=1)throw new KeyNotFoundException("Panel 不存在。");
    }

    private static (string FigureId,string PaperId) GetPanelOwner(
        SqliteConnection db,SqliteTransaction? tx,string panelId)
    {
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            SELECT f.id,f.paper_id FROM panel_templates p
            JOIN figure_templates f ON f.id=p.figure_id WHERE p.id=$panel
            """;
        cmd.Parameters.AddWithValue("$panel",panelId);
        using var rd=cmd.ExecuteReader();
        if(!rd.Read())throw new KeyNotFoundException("Panel 不存在。");
        return(rd.GetString(0),rd.GetString(1));
    }

    public IReadOnlyList<PanelAssetLink> ListPanelAssets(string panelId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT l.id,l.panel_id,l.asset_id,a.filename,g.prefix,r.name,a.stage,
                   l.position,a.sha256,a.size
            FROM figure_panel_assets l JOIN experiment_assets a ON a.id=l.asset_id
            JOIN experiment_groups g ON g.id=a.group_id
            JOIN experiment_results r ON r.id=a.result_id
            WHERE l.panel_id=$panel ORDER BY l.position,l.created_at
            """;
        cmd.Parameters.AddWithValue("$panel",panelId);
        using var rd=cmd.ExecuteReader();
        var list=new List<PanelAssetLink>();
        while(rd.Read())list.Add(new(rd.GetString(0),rd.GetString(1),rd.GetString(2),
            rd.GetString(3),rd.GetString(4),rd.GetString(5),rd.GetString(6),
            rd.GetInt32(7),rd.GetString(8),rd.GetInt64(9)));
        return list;
    }

    public int LinkShowAssets(string panelId,IEnumerable<string> assetIds)
    {
        var ids=assetIds.Distinct(StringComparer.Ordinal).ToArray();
        if(ids.Length==0)return 0;
        using var db=Connect();
        using var tx=db.BeginTransaction();
        var owner=GetPanelOwner(db,tx,panelId);
        int position;
        using(var max=db.CreateCommand())
        {
            max.Transaction=tx;
            max.CommandText="SELECT COALESCE(MAX(position),0) FROM figure_panel_assets WHERE panel_id=$panel";
            max.Parameters.AddWithValue("$panel",panelId);
            position=Convert.ToInt32(max.ExecuteScalar());
        }
        int added=0;
        foreach(var id in ids)
        {
            using(var check=db.CreateCommand())
            {
                check.Transaction=tx;
                check.CommandText="""
                  SELECT COUNT(*) FROM experiment_assets a
                  JOIN experiment_results r ON r.id=a.result_id
                  JOIN experiment_projects p ON p.id=r.project_id
                  WHERE a.id=$asset AND a.stage='show' AND a.is_deleted=0
                    AND r.is_deleted=0 AND p.is_deleted=0 AND p.paper_id=$paper
                  """;
                check.Parameters.AddWithValue("$asset",id);
                check.Parameters.AddWithValue("$paper",owner.PaperId);
                if(Convert.ToInt64(check.ExecuteScalar())!=1)
                    throw new InvalidOperationException("只允许关联当前论文项目中的 SHOW 图片。");
            }
            using var cmd=db.CreateCommand();
            cmd.Transaction=tx;
            cmd.CommandText="""
              INSERT OR IGNORE INTO figure_panel_assets(id,panel_id,asset_id,position,created_at)
              VALUES($id,$panel,$asset,$position,$now)
              """;
            cmd.Parameters.AddWithValue("$id",NewId());
            cmd.Parameters.AddWithValue("$panel",panelId);
            cmd.Parameters.AddWithValue("$asset",id);
            cmd.Parameters.AddWithValue("$position",++position);
            cmd.Parameters.AddWithValue("$now",UtcNow());
            added+=cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return added;
    }

    public void UnlinkPanelAsset(string linkId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="DELETE FROM figure_panel_assets WHERE id=$id";
        cmd.Parameters.AddWithValue("$id",linkId);
        if(cmd.ExecuteNonQuery()!=1)throw new KeyNotFoundException("组图素材关联不存在。");
    }

    public IReadOnlyList<FigureComposite> ListFigureComposites(string figureId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
          SELECT id,figure_id,filename,sha256,size,version,is_current,imported_at
          FROM figure_composites WHERE figure_id=$figure ORDER BY version DESC
          """;
        cmd.Parameters.AddWithValue("$figure",figureId);
        using var rd=cmd.ExecuteReader();
        var list=new List<FigureComposite>();
        while(rd.Read())list.Add(new(rd.GetString(0),rd.GetString(1),rd.GetString(2),
            rd.GetString(3),rd.GetInt64(4),rd.GetInt32(5),rd.GetInt32(6)==1,rd.GetString(7)));
        return list;
    }

    // The Figure-level show_data view is a logical projection of its immutable
    // composite versions. No experiment_assets row or duplicate blob is created.
    public IReadOnlyList<FigureComposite> ListFigureShowData(string figureId) =>
        ListFigureComposites(figureId);

    public FigureComposite ImportFigureComposite(string figureId,string sourcePath)
    {
        var source=Path.GetFullPath(sourcePath);
        var extension=Path.GetExtension(source).ToLowerInvariant();
        if(extension is not (".png" or ".tif" or ".tiff" or ".jpg" or ".jpeg" or ".bmp" or ".webp"))
            throw new ArgumentException("请选择 PNG、TIFF、JPG、BMP 或 WebP 成图文件。");
        if(!File.Exists(source))throw new FileNotFoundException("成图文件不存在。",source);
        using(var db=Connect())
        using(var cmd=db.CreateCommand())
        {
            cmd.CommandText="SELECT COUNT(*) FROM figure_templates WHERE id=$id";
            cmd.Parameters.AddWithValue("$id",figureId);
            if(Convert.ToInt64(cmd.ExecuteScalar())!=1)throw new KeyNotFoundException("Figure 不存在。");
        }
        var staging=Path.Combine(_root,"staging",NewId()+".part");
        try
        {
            using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var output=new FileStream(staging,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(true);
            }
            string sha;
            using(var input=File.OpenRead(staging))
                sha=Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
            var target=Path.Combine(_root,"blobs","sha256",sha[..2],sha);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if(!File.Exists(target))File.Move(staging,target);
            else
            {
                using var existing=File.OpenRead(target);
                if(Convert.ToHexString(SHA256.HashData(existing)).ToLowerInvariant()!=sha)
                    throw new IOException("仓库中的同哈希文件校验失败。");
            }
            using var db=Connect();
            using var tx=db.BeginTransaction();
            int version;
            using(var query=db.CreateCommand())
            {
                query.Transaction=tx;
                query.CommandText="SELECT COALESCE(MAX(version),0)+1 FROM figure_composites WHERE figure_id=$figure";
                query.Parameters.AddWithValue("$figure",figureId);
                version=Convert.ToInt32(query.ExecuteScalar());
            }
            bool first=version==1;
            var result=new FigureComposite(NewId(),figureId,Path.GetFileName(source),
                sha,new FileInfo(target).Length,version,first,UtcNow());
            using(var cmd=db.CreateCommand())
            {
                cmd.Transaction=tx;
                cmd.CommandText="""
                  INSERT INTO figure_composites(id,figure_id,filename,sha256,size,version,is_current,imported_at)
                  VALUES($id,$figure,$name,$sha,$size,$version,$current,$now)
                  """;
                cmd.Parameters.AddWithValue("$id",result.Id);
                cmd.Parameters.AddWithValue("$figure",figureId);
                cmd.Parameters.AddWithValue("$name",result.FileName);
                cmd.Parameters.AddWithValue("$sha",result.Sha256);
                cmd.Parameters.AddWithValue("$size",result.Size);
                cmd.Parameters.AddWithValue("$version",result.Version);
                cmd.Parameters.AddWithValue("$current",first?1:0);
                cmd.Parameters.AddWithValue("$now",result.ImportedAt);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return result;
        }
        finally {if(File.Exists(staging))File.Delete(staging);}
    }

    public void RenameFigureComposite(string compositeId,string fileName)
    {
        fileName=Path.GetFileName(fileName.Trim());
        if(string.IsNullOrWhiteSpace(fileName) || fileName.Length>180 ||
            fileName is "." or ".." ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars())>=0)
            throw new ArgumentException("完整成图文件名无效。",nameof(fileName));
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="UPDATE figure_composites SET filename=$name WHERE id=$id";
        cmd.Parameters.AddWithValue("$name",fileName);
        cmd.Parameters.AddWithValue("$id",compositeId);
        if(cmd.ExecuteNonQuery()!=1)throw new KeyNotFoundException("完整成图版本不存在。");
    }

    public void DeleteFigureCompositeVersion(string compositeId)
    {
        using var db=Connect();
        using var tx=db.BeginTransaction();
        string figureId;
        bool wasCurrent;
        using(var query=db.CreateCommand())
        {
            query.Transaction=tx;
            query.CommandText="SELECT figure_id,is_current FROM figure_composites WHERE id=$id";
            query.Parameters.AddWithValue("$id",compositeId);
            using var reader=query.ExecuteReader();
            if(!reader.Read())throw new KeyNotFoundException("完整成图版本不存在。");
            figureId=reader.GetString(0);
            wasCurrent=reader.GetInt32(1)==1;
        }
        using(var sources=db.CreateCommand())
        {
            sources.Transaction=tx;
            sources.CommandText="DELETE FROM figure_composite_sources WHERE composite_id=$id";
            sources.Parameters.AddWithValue("$id",compositeId);
            sources.ExecuteNonQuery();
        }
        using(var remove=db.CreateCommand())
        {
            remove.Transaction=tx;
            remove.CommandText="DELETE FROM figure_composites WHERE id=$id";
            remove.Parameters.AddWithValue("$id",compositeId);
            remove.ExecuteNonQuery();
        }
        if(wasCurrent)
        {
            using var promote=db.CreateCommand();
            promote.Transaction=tx;
            promote.CommandText="""
                UPDATE figure_composites SET is_current=1
                WHERE id=(SELECT id FROM figure_composites
                          WHERE figure_id=$figure ORDER BY version DESC LIMIT 1)
                """;
            promote.Parameters.AddWithValue("$figure",figureId);
            promote.ExecuteNonQuery();
        }
        // The content-addressed blob is retained; another version or experiment
        // may still reference the same bytes.
        tx.Commit();
    }

    public void SetCurrentComposite(string figureId,string compositeId)
    {
        using var db=Connect();
        using var tx=db.BeginTransaction();
        using(var check=db.CreateCommand())
        {
            check.Transaction=tx;
            check.CommandText="SELECT COUNT(*) FROM figure_composites WHERE figure_id=$figure AND id=$id";
            check.Parameters.AddWithValue("$figure",figureId);
            check.Parameters.AddWithValue("$id",compositeId);
            if(Convert.ToInt64(check.ExecuteScalar())!=1)throw new KeyNotFoundException("成图版本不存在。");
        }
        using(var clear=db.CreateCommand())
        {
            clear.Transaction=tx;
            clear.CommandText="UPDATE figure_composites SET is_current=0 WHERE figure_id=$figure";
            clear.Parameters.AddWithValue("$figure",figureId);
            clear.ExecuteNonQuery();
        }
        using(var set=db.CreateCommand())
        {
            set.Transaction=tx;
            set.CommandText="UPDATE figure_composites SET is_current=1 WHERE id=$id";
            set.Parameters.AddWithValue("$id",compositeId);
            set.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public string ExportComposite(FigureComposite composite,string directory)
    {
        if(composite.Sha256.Length!=64 || composite.Sha256.Any(c=>!Uri.IsHexDigit(c)))
            throw new InvalidDataException("无效的成图哈希。");
        var source=Path.Combine(_root,"blobs","sha256",composite.Sha256[..2],composite.Sha256);
        if(!File.Exists(source))throw new FileNotFoundException("完整组图内容缺失。",source);
        return CopyVerifiedNoOverwrite(source,composite.FileName,composite.Sha256,directory);
    }

    private static string CopyVerifiedNoOverwrite(string source,string name,string hash,string directory)
    {
        Directory.CreateDirectory(directory);
        var stem=Path.GetFileNameWithoutExtension(name);
        var extension=Path.GetExtension(name);
        string target;
        var attempt=1;
        do
        {
            target=Path.Combine(directory,attempt==1?name:$"{stem} ({attempt}){extension}");
            attempt++;
        }while(File.Exists(target) && attempt<=10001);
        if(File.Exists(target))throw new IOException("导出目标目录重名文件过多。");
        using(var input=File.OpenRead(source))
        using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            input.CopyTo(output);
        using(var exported=File.OpenRead(target))
        {
            if(Convert.ToHexString(SHA256.HashData(exported)).ToLowerInvariant()!=hash)
            {
                File.Delete(target);
                throw new IOException("成图导出校验失败。");
            }
        }
        return target;
    }

    public FigureMaterialExport ExportFigureMaterials(string figureId,string destination)
    {
        // Copy original SHOW bytes; a CSV manifest maps exported filenames to
        // their Panel and scientific source. Exports do NOT confirm final usage.
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
           SELECT p.label,a.filename,a.sha256,a.id,g.prefix,r.name
           FROM panel_templates p
           JOIN figure_panel_assets l ON l.panel_id=p.id
           JOIN experiment_assets a ON a.id=l.asset_id
           JOIN experiment_groups g ON g.id=a.group_id
           JOIN experiment_results r ON r.id=a.result_id
           WHERE p.figure_id=$figure
           ORDER BY p.label,l.position
           """;
        cmd.Parameters.AddWithValue("$figure",figureId);
        using var rd=cmd.ExecuteReader();
        var rows=new List<(string Panel,string Name,string Hash,string Asset,string Group,string Result)>();
        while(rd.Read())rows.Add((rd.GetString(0),rd.GetString(1),
            rd.GetString(2),rd.GetString(3),rd.GetString(4),rd.GetString(5)));
        if(rows.Count==0)throw new InvalidOperationException("当前 Figure 尚未关联任何 SHOW 素材。");
        Directory.CreateDirectory(destination);
        var manifests=new List<string>{"Panel,ExportedFile,SourceName,Result,DataGroup,AssetID,SHA256"};
        foreach(var row in rows)
        {
            var source=Path.Combine(_root,"blobs","sha256",row.Hash[..2],row.Hash);
            if(!File.Exists(source))throw new FileNotFoundException("素材源文件缺失。",source);
            var exported=CopyVerifiedNoOverwrite(source,row.Name,row.Hash,
                Path.Combine(destination,"Panel_"+row.Panel));
            static string Csv(string s)=>"\""+s.Replace("\"","\"\"")+"\"";
            manifests.Add(string.Join(",",new[]{row.Panel,Path.GetFileName(exported),
                row.Name,row.Result,row.Group,row.Asset,row.Hash}.Select(Csv)));
        }
        var manifest=Path.Combine(destination,"Figure_素材清单.csv");
        for(int n=2;File.Exists(manifest);n++)
            manifest=Path.Combine(destination,$"Figure_素材清单 ({n}).csv");
        File.WriteAllLines(manifest,manifests,System.Text.Encoding.UTF8);
        return new FigureMaterialExport(rows.Count,destination,manifest);
    }
}
