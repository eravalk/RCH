using System.Diagnostics;
using System.Text.Json;

namespace RCHub.Core;

public interface IAccountProvider : ICloudProvider, IDisposable { }

public sealed partial class RcloneProvider(Account account, string executable, string config, Action<string>? trace=null,Settings? settings=null) : IAccountProvider, IQuotaProvider
{
    private string Target(string path)
    {
        var remote = account.Endpoint;
        if (string.IsNullOrWhiteSpace(remote) || remote.StartsWith('-') || remote.Any(c =>
            !char.IsLetterOrDigit(c) && c is not ('_' or '-' or ' ')))
            throw new ArgumentException("Недопустимое имя подключения");
        return remote + ":" + Paths.Normalize(path);
    }

    public static ProcessStartInfo Command(string executable, string config, params string[] args)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        start.ArgumentList.Add("--config");
        start.ArgumentList.Add(config);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return start;
    }

    private async Task RunAsync(string[] args, Stream? output, CancellationToken ct)
    {
        var start = Command(executable, config, args);
        var rate=args[0]=="copyto"?settings?.UploadBytesPerSecond:args[0]=="cat"?settings?.DownloadBytesPerSecond:0;
        if(rate is >0){start.ArgumentList.Add("--bwlimit");start.ArgumentList.Add(rate.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)+"B");}
        start.RedirectStandardError = start.RedirectStandardOutput = true;
        start.CreateNoWindow = true;
        start.StandardErrorEncoding = System.Text.Encoding.UTF8;
        var op=Guid.NewGuid().ToString("N");
        var context=$"account={account.Id} service={account.Service} op={op} command={args[0]}";
        trace?.Invoke(context+" started");
        using var process = Process.Start(start) ?? throw new IOException("Не удалось запустить rclone");
        try
        {
            var diagnostic = process.StandardError.ReadToEndAsync(ct);
            await process.StandardOutput.BaseStream.CopyToAsync(output ?? Stream.Null, ct);
            await process.WaitForExitAsync(ct);
            var error = await diagnostic;
            trace?.Invoke(context+" exit="+process.ExitCode);
            if (process.ExitCode != 0)
            {
                var report=SafeDiagnostic.Report(error,context,process.ExitCode);
                trace?.Invoke(report);
                if(account.Service=="iCloud Drive")
                {
                    var dir=Path.Combine(Path.GetDirectoryName(config)!,"diagnostics");
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir,"icloud-last.txt"),report);
                }
                throw new IOException(ProviderError.Describe(account.Service,args[0]=="lsjson"?"Чтение каталога":"Операция с файлами",error,process.ExitCode)+"\n"+report);
            }
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
        }
    }

    public async Task<IReadOnlyList<CloudEntry>> ListAsync(string folder, CancellationToken ct)
    {
        using var output = new MemoryStream();
        await RunAsync(["lsjson", Target(folder)], output, ct);
        using var json = JsonDocument.Parse(output.ToArray());
        var entries = new List<CloudEntry>();
        foreach (var item in json.RootElement.EnumerateArray())
        {
            var name = item.GetProperty("Name").GetString()!;
            if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(['/', '\\']) >= 0) continue;
            var modified = item.GetProperty("ModTime").GetDateTimeOffset();
            var size = item.GetProperty("Size").GetInt64();
            entries.Add(new CloudEntry { ProviderId=item.TryGetProperty("ID",out var id)?id.GetString():null, Name = name, Path = Paths.Combine(folder, name),
                AccountId = account.Id, IsFolder = item.GetProperty("IsDir").GetBoolean(),
                Size = Math.Max(0, size), Modified = modified.Year==2000&&modified.Month==1&&modified.Day==1&&modified.TimeOfDay==TimeSpan.Zero?null:modified, ETag = $"{modified.UtcTicks}:{size}" });
        }
        return entries;
    }

    public async Task<CloudQuota> GetQuotaAsync(CancellationToken ct)
    {
        using var output=new MemoryStream();
        await RunAsync(["about",Target(""),"--json"],output,ct);
        return CloudQuota.Parse(output.ToArray());
    }
    public async Task DownloadAsync(string path, Stream destination, IProgress<long>? progress, CancellationToken ct)
    {
        using var reporting=new RateLimitedStream(destination,0,progress);
        await RunAsync(["cat",Target(path)],reporting,ct);
    }

    public async Task UploadAsync(string path, Stream source, CancellationToken ct)
    {
        var temporary = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".rchub");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await source.CopyToAsync(file, ct);
            await RunAsync(["copyto", "--immutable", temporary, Target(path)], null, ct);
        }
        finally { File.Delete(temporary); }
    }
    public Task CreateFolderAsync(string path, CancellationToken ct) => RunAsync(["mkdir",Target(path)],null,ct);
    public Task DeleteAsync(string path, CancellationToken ct) => RunAsync(["deletefile",Target(path)],null,ct);
    public Task MoveAsync(string source,string destination,CancellationToken ct) =>
        RunAsync(["moveto","--immutable",Target(source),Target(destination)],null,ct);
    public void Dispose() { }
}
