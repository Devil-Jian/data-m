using System.Security.Cryptography;

namespace PaperArchive.Core.Workspace;

public sealed record E6VerifiedMove(
    string RootId,string ExperimentId,string ExperimentName,
    string OldPath,string NewPath);

public sealed partial class ExperimentWorkspace
{
    /// <summary>
    /// Detect a physical move conservatively: old E1 root has disappeared,
    /// basename matches exactly, and EVERY tracked current asset is found at
    /// the same relative path with identical SHA-256 in the new directory.
    /// Same-name copies with the old root still present are NOT assumed moves.
    /// </summary>
    public IReadOnlyList<E6VerifiedMove> PreviewE6VerifiedMoves(string researchDirectory)
    {
        var preview=PreviewE6ResearchImport(researchDirectory);
        var oldRoots=ListProjects()
            .SelectMany(project=>ListE1FolderIdentities(project.Id))
            .Where(x=>!Directory.Exists(x.RootPath))
            .ToArray();
        var moves=new List<E6VerifiedMove>();
        foreach(var child in preview.ExperimentFolders)
        {
            var path=E6Normalized(Path.Combine(preview.RootPath,child));
            if(E6FindRootByPath(path) is not null)continue;
            var candidates=new List<E1FolderIdentity>();
            foreach(var root in oldRoots.Where(x=>
                Path.GetFileName(x.RootPath).Equals(child,
                    StringComparison.OrdinalIgnoreCase)))
            {
                var assets=ListE1FolderAssets(root.Id);
                if(assets.Count==0)continue;
                var match=true;
                foreach(var asset in assets)
                {
                    var relative=asset.RelativePath.Replace('/',Path.DirectorySeparatorChar);
                    var filename=Path.GetFullPath(Path.Combine(path,relative));
                    if(!E6Normalized(filename).StartsWith(path+"/",
                        StringComparison.OrdinalIgnoreCase)||!File.Exists(filename))
                    {match=false;break;}
                    if(File.GetAttributes(filename).HasFlag(FileAttributes.ReparsePoint))
                    {match=false;break;}
                    using var data=File.OpenRead(filename);
                    var sha=Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
                    if(!sha.Equals(asset.Sha256,StringComparison.OrdinalIgnoreCase))
                    {match=false;break;}
                }
                if(match)candidates.Add(root);
            }
            if(candidates.Count>1)
                throw new InvalidOperationException(
                    "存在多个与移动后文件完全一致的旧实验，请先核对旧目录："+child);
            if(candidates.Count==1)
            {
                var root=candidates[0];
                moves.Add(new(root.Id,root.ExperimentId,root.ExperimentName,
                    root.RootPath,path));
            }
        }
        return moves;
    }
}
