using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace RCHub.Core;

public static class ReleaseDownload
{
    public static string DownloadsFolder()
    {
        if(OperatingSystem.IsWindows())
        {
            var id=new Guid("374DE290-123F-4565-9164-39C4925E467B");
            if(SHGetKnownFolderPath(ref id,0,IntPtr.Zero,out var pointer)==0)
            {
                try{return Marshal.PtrToStringUni(pointer)!;}finally{Marshal.FreeCoTaskMem(pointer);}
            }
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads");
    }
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid id,uint flags,IntPtr token,out IntPtr path);
    public static async Task<string> SaveAsync(HttpClient client,string link,string folder,string? digest,IProgress<long>? progress,CancellationToken ct)
    {
        if(!Uri.TryCreate(link,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.Host!="github.com" ||
            !System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath,@"^/eravalk/RCH/releases/download/v\d+\.\d+\.\d+/RCH-\d+\.\d+\.\d+-win-x64\.zip$"))
            throw new ArgumentException("Неверный адрес пакета RCH");
        Directory.CreateDirectory(folder);
        var destination=Path.Combine(folder,Path.GetFileName(uri.LocalPath));
        if(File.Exists(destination))destination=Path.Combine(folder,Path.GetFileNameWithoutExtension(destination)+"-"+Guid.NewGuid().ToString("N")[..8]+".zip");
        var partial=destination+".part";
        try {
            using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct);response.EnsureSuccessStatusCode();
            const long maximum=512L*1024*1024;
            if(response.Content.Headers.ContentLength is >maximum)throw new IOException("Пакет превышает допустимый размер");
            long bytes=0;
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using(var source=await response.Content.ReadAsStreamAsync(ct))
            await using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))
            {
                var buffer=new byte[81920];int read;
                while((read=await source.ReadAsync(buffer,ct))>0) {
                    bytes+=read;if(bytes>maximum)throw new IOException("Пакет превышает допустимый размер");
                    await output.WriteAsync(buffer.AsMemory(0,read),ct);hash.AppendData(buffer,0,read);progress?.Report(bytes);
                }
            }
            if(response.Content.Headers.ContentLength is long expected && expected!=bytes)throw new IOException("Пакет скачан не полностью");
            var actual=Convert.ToHexString(hash.GetHashAndReset());
            if(digest!=null && !string.Equals(digest,"sha256:"+actual,StringComparison.OrdinalIgnoreCase))throw new IOException("Контрольная сумма пакета не совпала с GitHub");
            using(var zip=ZipFile.OpenRead(partial))
                if(!zip.Entries.Any(e=>e.FullName=="RCHub.exe" && e.Length>0))throw new IOException("В архиве отсутствует приложение RCH");
            File.Move(partial,destination);return destination;
        }finally{if(File.Exists(partial))File.Delete(partial);}
    }
}
