namespace RCHub.Core;
public enum ExistingFileAction { Replace, RenameOld, ArchiveOld }
public sealed class LocalFiles
{
    public async Task<string> DownloadAsync(Account account, CloudEntry file, ICloudProvider provider, ExistingFileAction action, IProgress<long>? progress, CancellationToken ct)
    {
        var destination = Paths.LocalPath(account.LocalFolder, file.Path); var parent = Path.GetDirectoryName(destination)!;
        RejectLinks(parent, account.LocalFolder); Directory.CreateDirectory(parent);
        var partial = Path.Combine(parent, ".rchub-" + Guid.NewGuid().ToString("N") + ".part");
        try
        {
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, true))
                await provider.DownloadAsync(file.Path, output, progress, ct);
            ct.ThrowIfCancellationRequested();
            if (new FileInfo(partial).Length != file.Size) throw new IOException("Размер файла изменился на сервере. Обновите каталог и повторите скачивание.");
            if (File.Exists(destination))
            {
                if (action == ExistingFileAction.Replace) File.Move(partial, destination, true);
                else
                {
                    var oldPath = action == ExistingFileAction.ArchiveOld ? Path.Combine(parent, "Предыдущие версии") : parent;
                    Directory.CreateDirectory(oldPath); RejectLinks(oldPath, account.LocalFolder);
                    var oldName = Path.GetFileNameWithoutExtension(destination) + "_old_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N")[..6] + Path.GetExtension(destination);
                    var archived = Path.Combine(oldPath, oldName);
                    File.Move(destination, archived);
                    try { File.Move(partial, destination); } catch { File.Move(archived, destination); throw; }
                }
            }
            else File.Move(partial, destination);
            file.Status = LocalStatus.Current; file.Downloaded = DateTimeOffset.Now; file.SavedETag = file.ETag; file.SuppressAutoDownload = false;
            return destination;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
    private static void RejectLinks(string directory, string root)
    {
        var item = new DirectoryInfo(directory); var fullRoot = Path.GetFullPath(root);
        while (item is not null)
        {
            if (item.LinkTarget is not null) throw new IOException("Выберите локальную папку без символических ссылок и junction");
            if (string.Equals(item.FullName, fullRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) break;
            item = item.Parent;
        }
    }
}
public sealed record StorageCategory(string Name, long Bytes, string Color);
public static class StorageStatistics
{
    public static IReadOnlyList<StorageCategory> Read(AppState state, string dataDirectory)
    {
        string[] colors = ["#3976E7", "#8B72C7", "#42A294", "#DBA04C", "#C66E8A", "#7A9CA9"];
        var result = new List<StorageCategory>(); var roots = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var a in state.Accounts)
        {
            if (a.IsDemo || string.IsNullOrWhiteSpace(a.LocalFolder) || !Directory.Exists(a.LocalFolder)) continue;
            var root = Path.GetFullPath(a.LocalFolder).TrimEnd(Path.DirectorySeparatorChar);
            if (!roots.Add(root)) continue;
            result.Add(new(a.Title, DirectorySize(root), colors[result.Count % colors.Length]));
        }
        var cache = Path.Combine(dataDirectory, "cache"); var cacheBytes = DirectorySize(cache);
        result.Add(new("Кэш предпросмотра", cacheBytes, "#A6B1C2"));
        result.Add(new("Индекс и данные приложения", Math.Max(0, DirectorySize(dataDirectory) - cacheBytes), "#67768E"));
        return result;
    }
    public static long DirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        return Directory.EnumerateFiles(path, "*", options).Sum(f => { try { return new FileInfo(f).Length; } catch (IOException) { return 0L; } });
    }
}
