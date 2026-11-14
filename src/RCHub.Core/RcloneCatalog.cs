using System.Diagnostics;
using System.Text.Json;
namespace RCHub.Core;
public interface IRecursiveCatalogProvider
{
    Task<CatalogScan> ScanCatalogAsync(Action<IReadOnlyList<CloudEntry>>? discovered,IProgress<int>? progress,int concurrency,CancellationToken ct);
}
public sealed partial class RcloneProvider:IRecursiveCatalogProvider
{
    public async Task<CatalogScan> ScanCatalogAsync(Action<IReadOnlyList<CloudEntry>>? discovered,IProgress<int>? progress,int concurrency,CancellationToken ct)
    {
        var start=Command(executable,config,"lsjson",Target(""),"--recursive","--fast-list","--no-mimetype","--checkers",Math.Clamp(concurrency,1,8).ToString());
        start.RedirectStandardOutput=start.RedirectStandardError=true;start.CreateNoWindow=true;start.StandardErrorEncoding=System.Text.Encoding.UTF8;
        var op=Guid.NewGuid().ToString("N");var context=$"account={account.Id} service={account.Service} op={op} command=lsjson-recursive";
        trace?.Invoke(context+" started");
        using var process=Process.Start(start)??throw new IOException("Не удалось запустить rclone");
        var entries=new List<CloudEntry>();var batch=new List<CloudEntry>();var elapsed=Stopwatch.StartNew();
        var errors=process.StandardError.ReadToEndAsync(ct);
        try
        {
            await foreach(var item in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(process.StandardOutput.BaseStream,cancellationToken:ct))
            {
                var path=Paths.Normalize(item.GetProperty("Path").GetString()??"");if(path.Length==0)continue;
                var size=item.GetProperty("Size").GetInt64();var modified=item.GetProperty("ModTime").GetDateTimeOffset();
                var file=new CloudEntry{AccountId=account.Id,Path=path,Name=path.Split('/').Last(),IsFolder=item.GetProperty("IsDir").GetBoolean(),
                    ProviderId=item.TryGetProperty("ID",out var id)?id.GetString():null,Size=Math.Max(0,size),Modified=modified.Year==2000&&modified.Month==1&&modified.Day==1&&modified.TimeOfDay==TimeSpan.Zero?null:modified,ETag=$"{modified.UtcTicks}:{size}"};
                entries.Add(file);batch.Add(file);
                if(batch.Count>=200||elapsed.ElapsedMilliseconds>=250){discovered?.Invoke(batch.ToArray());progress?.Report(entries.Count);batch.Clear();elapsed.Restart();}
            }
            if(batch.Count>0){discovered?.Invoke(batch.ToArray());progress?.Report(entries.Count);}
            await process.WaitForExitAsync(ct);var error=await errors;
            trace?.Invoke(context+" exit="+process.ExitCode);
            if(process.ExitCode!=0)
            {
                var report=SafeDiagnostic.Report(error,context,process.ExitCode);trace?.Invoke(report);
                if(account.Service=="iCloud Drive")
                {var dir=Path.Combine(Path.GetDirectoryName(config)!,"diagnostics");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"icloud-last.txt"),report);}
                if(entries.Count==0)throw new IOException(ProviderError.Describe(account.Service,"Чтение каталога",error,process.ExitCode)+"\n"+report);
                // The backend did not identify every unreadable subtree. Preserve all missing cached entries.
                return new(entries,[""]);
            }
            return new(entries,[]);
        }
        finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}}
    }
}
