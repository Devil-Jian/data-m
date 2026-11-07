using System.Globalization;

namespace PaperArchive.Core;

public sealed record ScannedFile(
    string SourceRelativePath,
    string LogicalRelativePath,
    long Size,
    string Role,
    int? Figure,
    string? Panel,
    int? Sequence,
    string? Warning);

public sealed record PaperScan(string Root, string Title, IReadOnlyList<ScannedFile> Files)
{
    public int WarningCount => Files.Count(file => file.Warning is not null);
}

public static class PaperFolderScanner
{
    private static readonly HashSet<string> ManuscriptExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx" };
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".webp", ".czi", ".nd2", ".svs" };

    public static PaperScan Scan(string folder)
    {
        var root = Path.GetFullPath(folder);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var rows = new List<ScannedFile>();
        var pending = new Stack<(string Dir, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            var (directory, depth) = pending.Pop();
            if (depth > 12) throw new IOException("目录层级超过限制（12）。");
            foreach (var name in Directory.EnumerateFileSystemEntries(directory))
            {
                var attrs = File.GetAttributes(name);
                if (attrs.HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException("导入目录包含符号链接或目录联接：" + name);
                if (attrs.HasFlag(FileAttributes.Directory))
                {
                    pending.Push((name, depth + 1));
                    continue;
                }
                if (rows.Count >= 20_000) throw new IOException("单次归档最多 20000 个文件。");
                var relative = Path.GetRelativePath(root, name).Replace('\\', '/');
                rows.Add(Classify(relative, new FileInfo(name).Length));
            }
        }
        rows.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.SourceRelativePath, b.SourceRelativePath));
        var title = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)) ?? "未命名论文";
        return new PaperScan(root, title, rows);
    }

    public static ScannedFile Classify(string path, long size)
    {
        var bits = path.Replace('\\', '/').Split('/');
        var file = bits[^1];
        var stem = Path.GetFileNameWithoutExtension(file);
        var ext = Path.GetExtension(file);
        if (bits.Length == 1)
            return ManuscriptExtensions.Contains(ext)
                ? new(path, path, size, "manuscript", null, null, null, null)
                : new(path, path, size, "other", null, null, null, "根目录非论文正文，归入待分类资源");

        if (bits[0].Equals("Figure", StringComparison.OrdinalIgnoreCase))
        {
            var group = bits[1].ToLowerInvariant();
            var composite = group is "组合图" or "组图" or "composite" or "combined";
            var role = composite ? "composite"
                : group is "子图" or "展示子图" or "display" or "panels" ? "display"
                : group is "统计原始图" or "stat_images" or "stat" ? "stat_image"
                : "unspecified";
            if (composite)
            {
                var figure = ParseComposite(stem);
                return new(path, path, size, role, figure, null, null,
                    figure is null ? "组合图名称缺少 Figure 编号" : null);
            }
            var (number, panel, sequence) = ParsePanel(stem);
            return new(path, path, size, role, number, panel, sequence,
                number is null ? "图片名称缺少 Figure-Panel 编号" :
                role == "unspecified" ? "图片角色待确认" :
                !ImageExtensions.Contains(ext) ? "Figure 中的文件不是常见图像格式" : null);
        }
        if (bits[0].Equals("Table", StringComparison.OrdinalIgnoreCase))
        {
            var (figure, panel, sequence) = ParsePanel(stem);
            if (figure.HasValue)
                return new(path, path, size, "statistics", figure, panel, sequence, null);
            var table = ParseTable(stem);
            return new(path, path, size, table.HasValue ? "statistics" : "other",
                table, null, null, table is null ? "统计文件缺少 Figure-Panel 或 T 编号" : null);
        }
        return new(path, path, size, "other", null, null, null,
            bits[0].Equals("Others", StringComparison.OrdinalIgnoreCase) ? null : "非标准目录");
    }

    private static int? ParseComposite(string stem)
    {
        var text = stem.ToLowerInvariant();
        var prefix = text.StartsWith("figure", StringComparison.Ordinal) ? 6
            : text.StartsWith("fig", StringComparison.Ordinal) ? 3 : 0;
        if (prefix == 0) return null;
        return NumberAtStart(text[prefix..].TrimStart('-', '_', ' '));
    }

    private static int? ParseTable(string stem)
    {
        var text = stem.ToLowerInvariant();
        var prefix = text.StartsWith("table", StringComparison.Ordinal) ? 5
            : text.StartsWith('t') ? 1 : 0;
        return prefix == 0 ? null : NumberAtStart(text[prefix..].TrimStart('-', '_', ' '));
    }

    private static (int? Figure, string? Panel, int? Sequence) ParsePanel(string stem)
    {
        var offset = 0;
        while (offset < stem.Length && char.IsAsciiDigit(stem[offset])) offset++;
        if (offset == 0 || offset >= stem.Length || stem[offset] is not ('-' or '_')
            || !int.TryParse(stem[..offset], NumberStyles.None, CultureInfo.InvariantCulture, out var figure)
            || figure < 1) return (null, null, null);
        offset++;
        if (offset >= stem.Length || !char.IsAsciiLetter(stem[offset])) return (null, null, null);
        var panel = char.ToUpperInvariant(stem[offset]).ToString();
        offset++;
        if (offset < stem.Length && stem[offset] is '-' or '_') offset++;
        var begin = offset;
        while (offset < stem.Length && char.IsAsciiDigit(stem[offset])) offset++;
        int? sequence = offset > begin && int.TryParse(stem[begin..offset],
            NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : null;
        return (figure, panel, sequence);
    }

    private static int? NumberAtStart(string text)
    {
        var digits = 0;
        while (digits < text.Length && char.IsAsciiDigit(text[digits])) digits++;
        return digits > 0 && int.TryParse(text[..digits], NumberStyles.None,
            CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;
    }
}
