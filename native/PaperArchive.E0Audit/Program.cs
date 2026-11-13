using System.Text.Json;
using PaperArchive.Core.Workspace;

if(args.Length!=1)
{
    Console.Error.WriteLine("用法：dotnet run --project native/PaperArchive.E0Audit -- <现有工作区根目录>");
    Console.Error.WriteLine("结果以 JSON 输出到标准输出；可使用 PowerShell 重定向至文件。");
    return 2;
}
try
{
    var repo=ExperimentWorkspace.OpenForE0Audit(args[0]);
    var preview=repo.PreviewE0Migration();
    Console.WriteLine(JsonSerializer.Serialize(preview,new JsonSerializerOptions
    {
        WriteIndented=true
    }));
    return 0;
}
catch(Exception ex)
{
    Console.Error.WriteLine("E0 只读审计失败："+ex.Message);
    Console.Error.WriteLine("请先核对已有数据库的版本；审计工具不会升级或修复数据库。");
    return 1;
}
