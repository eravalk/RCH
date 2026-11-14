using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
namespace RCHub.Core;

public sealed class PreviewCache(string directory)
{
    private readonly ConcurrentDictionary<string,SemaphoreSlim> locks=new();
    public string DirectoryPath=>directory;
    public static bool IsImage(string name)=>Path.GetExtension(name).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp";
    public static bool IsText(string name)=>Path.GetExtension(name).ToLowerInvariant() is ".txt" or ".md" or ".log" or ".json" or ".xml" or ".csv" or ".yaml" or ".yml" or ".ini" or ".cfg" or ".py" or ".cs" or ".js" or ".ts" or ".html" or ".css" or ".shader" or ".hlsl";
    public async Task<string> GetAsync(Account account,CloudEntry entry,ICloudProvider provider,long maxFile,long totalLimit,CancellationToken ct)
    {
        if(entry.IsFolder)throw new IOException("Выберите файл");
        if(entry.Size>maxFile)throw new IOException("Файл превышает лимит предпросмотра. Измените лимит в настройках или откройте веб-просмотр.");
        Directory.CreateDirectory(directory);
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account.Id+"\0"+entry.Path+"\0"+entry.ETag+"\0"+entry.Modified+"\0"+entry.Size)));
        var path=Path.Combine(directory,key+".preview");
        var gate=locks.GetOrAdd(key,_=>new(1));await gate.WaitAsync(ct);
        try
        {
            if(File.Exists(path)){if(new FileInfo(path).Length>maxFile)throw new IOException("Файл превышает текущий лимит предпросмотра");File.SetLastWriteTimeUtc(path,DateTime.UtcNow);return path;}
            var partial=path+"."+Guid.NewGuid().ToString("N")+".part";
            try
            {
                await using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))
                using(var bounded=new LimitedWriteStream(output,Math.Min(maxFile,totalLimit)))
                    await provider.DownloadAsync(entry.Path,bounded,null,ct);
                ct.ThrowIfCancellationRequested();File.Move(partial,path,true);Trim(totalLimit,path);return path;
            }
            finally{if(File.Exists(partial))File.Delete(partial);}
        }
        finally{gate.Release();}
    }
    public void Trim(long limit,string? keep=null)
    {
        if(!Directory.Exists(directory))return;
        var files=new DirectoryInfo(directory).GetFiles("*.preview").OrderBy(f=>f.LastWriteTimeUtc).ToArray();var size=files.Sum(f=>f.Length);
        foreach(var file in files){if(size<=limit)break;if(file.FullName==keep)continue;try{var length=file.Length;file.Delete();size-=length;}catch(IOException){}}
    }
    public void Clear(){if(Directory.Exists(directory))foreach(var file in Directory.EnumerateFiles(directory,"*.preview"))try{File.Delete(file);}catch(IOException){}}
}
public sealed class LimitedWriteStream(Stream inner,long limit):Stream
{
    private long written;
    public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;
    public override long Length=>written;public override long Position{get=>written;set=>throw new NotSupportedException();}
    public override void Flush()=>inner.Flush();
    public override void Write(byte[] buffer,int offset,int count){if(count>limit-written)throw new IOException("Превышен лимит предпросмотра");inner.Write(buffer,offset,count);written+=count;}
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken ct=default){if(buffer.Length>limit-written)throw new IOException("Превышен лимит предпросмотра");await inner.WriteAsync(buffer,ct);written+=buffer.Length;}
    public override Task WriteAsync(byte[] buffer,int offset,int count,CancellationToken ct)=>WriteAsync(buffer.AsMemory(offset,count),ct).AsTask();
    public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();
}
