using System.Text.Json;
namespace RCHub.Core;

public sealed record CloudQuota(long? Total,long? Used,long? Free,long? Other,long? Trash)
{
    public static CloudQuota Parse(ReadOnlySpan<byte> data)
    {
        using var json=JsonDocument.Parse(data.ToArray());
        long? Read(string key)=>json.RootElement.TryGetProperty(key,out var value) &&
            value.ValueKind==JsonValueKind.Number && value.TryGetInt64(out var number) && number>=0?number:null;
        var result=new CloudQuota(Read("total"),Read("used"),Read("free"),Read("other"),Read("trashed"));
        if(result.Total is null && result.Used is null && result.Free is null)throw new IOException("Сервис не предоставил квоту.");
        return result;
    }
    public void Apply(Account account)
    {
        // Free is the provider's remaining quota. Used alone may exclude other services or trash.
        account.TotalBytes=Total;account.FreeBytes=Free;
        account.UsedBytes=Total.HasValue && Free.HasValue && Free<=Total?Total-Free:Used;
        account.DriveUsedBytes=Used;account.OtherBytes=Other;account.TrashBytes=Trash;
        account.QuotaChecked=DateTimeOffset.Now;account.QuotaError="";
    }
}
public interface IQuotaProvider
{
    Task<CloudQuota> GetQuotaAsync(CancellationToken ct);
}
