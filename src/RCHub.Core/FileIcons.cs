using System.Text.Json;
namespace RCHub.Core;
public sealed record FileIcon(string Key,string Title,string[] Extensions,string Color,string Symbol);
public static class FileIcons
{
    public static IReadOnlyList<FileIcon> All {get;}=Load();
    private static IReadOnlyList<FileIcon> Load()
    {
        using var stream=typeof(FileIcons).Assembly.GetManifestResourceStream("RCHub.Core.Assets.file-icons.json")!;
        return JsonSerializer.Deserialize<List<FileIcon>>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    }
    public static FileIcon For(string name,bool folder=false)=>All.FirstOrDefault(i=>folder?i.Key=="folder":i.Extensions.Contains(Path.GetExtension(name).TrimStart('.'),StringComparer.OrdinalIgnoreCase))??All.Single(i=>i.Key=="unknown");
}
