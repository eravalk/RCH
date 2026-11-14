using RCHub.Core;

internal static class RcloneRoundTrip
{
    public static async Task RunAsync(string executable, string root)
    {
        string folderA = Directory.CreateDirectory(Path.Combine(root,"remote-a")).FullName;
        string folderB = Directory.CreateDirectory(Path.Combine(root,"remote-b")).FullName;
        string config = Path.Combine(root,"profiles.conf");
        await File.WriteAllTextAsync(config,$"[work]\ntype = alias\nremote = {folderA}\n[home]\ntype = alias\nremote = {folderB}\n");
        using var first = new RcloneProvider(new Account{Endpoint="work"},executable,config,settings:new Settings{DownloadBytesPerSecond=1_000_000,UploadBytesPerSecond=1_000_000});
        using var second = new RcloneProvider(new Account{Endpoint="home"},executable,config,settings:new Settings{DownloadBytesPerSecond=1_000_000,UploadBytesPerSecond=1_000_000});
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        foreach (var (provider,bytes) in new[]{(first,new byte[]{1,2,3}),(second,new byte[]{4,5,6})})
        {
            await provider.CreateFolderAsync("Фото",ct);
            using var source = new MemoryStream(bytes);
            await provider.UploadAsync("Фото/test.bin",source,ct);
            var file = (await provider.ListAsync("Фото",ct)).Single();
            using var target = new MemoryStream();
            await provider.DownloadAsync(file.Path,target,null,ct);
            if (!bytes.SequenceEqual(target.ToArray())) throw new Exception("Incorrect remote content");
        }
        var streamed=0;
        var indexed=await new CatalogService().ScanAsync(first,null,ct,4,0,b=>streamed+=b.Count);
        if(!indexed.Entries.Any(f=>f.Path=="Фото/test.bin")||streamed<2)throw new Exception("Recursive rclone scan did not stream files");
        await first.MoveAsync("Фото/test.bin","Фото/новый.bin",ct);
        await first.DeleteAsync("Фото/новый.bin",ct);
        if ((await first.ListAsync("Фото",ct)).Count!=0 || (await second.ListAsync("Фото",ct)).Count!=1)
            throw new Exception("Account isolation failed");
    }
}
