using System.Diagnostics;
namespace RCHub.Core;
// Leaves the underlying stream open. Applies a per-transfer byte rate in either direction.
public sealed class RateLimitedStream(Stream inner,long bytesPerSecond,IProgress<long>? progress=null):Stream
{
    private readonly Stopwatch clock=Stopwatch.StartNew();private long bytes;
    private async ValueTask Pace(int count,CancellationToken ct)
    {
        bytes+=count;if(bytesPerSecond<=0)return;
        var delay=TimeSpan.FromSeconds(bytes/(double)bytesPerSecond)-clock.Elapsed;
        if(delay>TimeSpan.Zero)await Task.Delay(delay,ct);
    }
    public override bool CanRead=>inner.CanRead;public override bool CanSeek=>inner.CanSeek;public override bool CanWrite=>inner.CanWrite;
    public override long Length=>inner.Length;public override long Position{get=>inner.Position;set=>inner.Position=value;}
    public override void Flush()=>inner.Flush();public override Task FlushAsync(CancellationToken ct)=>inner.FlushAsync(ct);
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default){var count=await inner.ReadAsync(buffer,ct);await Pace(count,ct);return count;}
    public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken ct)=>ReadAsync(buffer.AsMemory(offset,count),ct).AsTask();
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken ct=default){await Pace(buffer.Length,ct);await inner.WriteAsync(buffer,ct);progress?.Report(bytes);}
    public override Task WriteAsync(byte[] buffer,int offset,int count,CancellationToken ct)=>WriteAsync(buffer.AsMemory(offset,count),ct).AsTask();
    public override int Read(byte[] buffer,int offset,int count){var read=inner.Read(buffer,offset,count);Pace(read,default).AsTask().GetAwaiter().GetResult();return read;}
    public override void Write(byte[] buffer,int offset,int count){Pace(count,default).AsTask().GetAwaiter().GetResult();inner.Write(buffer,offset,count);progress?.Report(bytes);}
    public override long Seek(long offset,SeekOrigin origin)=>inner.Seek(offset,origin);public override void SetLength(long value)=>inner.SetLength(value);
}
public sealed class TransferProvider(IAccountProvider inner,Settings settings):IAccountProvider,IQuotaProvider
{
    public Task<IReadOnlyList<CloudEntry>> ListAsync(string path,CancellationToken ct)=>inner.ListAsync(path,ct);
    public async Task DownloadAsync(string path,Stream destination,IProgress<long>? progress,CancellationToken ct){using var stream=new RateLimitedStream(destination,settings.DownloadBytesPerSecond);await inner.DownloadAsync(path,stream,progress,ct);}
    public async Task UploadAsync(string path,Stream source,CancellationToken ct){using var stream=new RateLimitedStream(source,settings.UploadBytesPerSecond);await inner.UploadAsync(path,stream,ct);}
    public Task CreateFolderAsync(string path,CancellationToken ct)=>inner.CreateFolderAsync(path,ct);
    public Task DeleteAsync(string path,CancellationToken ct)=>inner.DeleteAsync(path,ct);
    public Task MoveAsync(string from,string to,CancellationToken ct)=>inner.MoveAsync(from,to,ct);
    public Task<CloudQuota> GetQuotaAsync(CancellationToken ct)=>inner is IQuotaProvider quota?quota.GetQuotaAsync(ct):throw new IOException("Сервис не предоставляет квоту");
    public void Dispose()=>inner.Dispose();
}
