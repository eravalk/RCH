using RCHub.Core;
using System.Diagnostics;
using System.Text;
static class RefreshPreviewChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var scanProvider=new TreeProvider();var batches=0;var early=false;
        var scan=await new CatalogService().ScanAsync(scanProvider,null,default,4,0,b=>{batches++;if(scanProvider.Completed==0&&b.Count>0)early=true;},"skip");
        check(scanProvider.Peak>1&&scanProvider.Peak<=4,"Catalog scan is parallel and respects the configured concurrency");
        check(early&&batches>1&&scan.Entries.Count==24&&!scanProvider.Visited.Contains("skip"),"Catalog publishes root before nested folders and excludes configured subtrees");
        scanProvider=new TreeProvider{FailOnce=true};scan=await new CatalogService().ScanAsync(scanProvider,null,default,2,1);
        check(scan.FailedFolders.Count==0&&scanProvider.Retried,"Transient folder failure is retried and recovered");
        using(var ct=new CancellationTokenSource(20))
        {
            try{await new CatalogService().ScanAsync(new TreeProvider(),null,ct.Token,4,0);throw new Exception("Missing cancellation");}catch(OperationCanceledException){}
            check(true,"Catalog scan cancellation interrupts in-flight requests");
        }
        var partialAccount=new Account{Id="partial",LastCheck=DateTimeOffset.Now.AddDays(-1)};var oldCheck=partialAccount.LastCheck;
        var partialState=new AppState{Accounts=[partialAccount],Entries=[new(){AccountId="partial",Path="unreadable/keep.txt",Name="keep.txt"}]};
        CatalogService.Merge(partialState,partialAccount,new CatalogScan([new(){AccountId="partial",Path="readable.txt",Name="readable.txt"}],[""]));
        check(partialState.Entries.Count==2&&partialAccount.LastCheck==oldCheck,"Partial recursive listing preserves missing cached records without marking a full check successful");
        var syncing=new Account{Id="sync"};var downloading=new CloudEntry{AccountId="sync",Name="file.txt",Path="file.txt",ETag="v1"};
        var syncingState=new AppState{Accounts=[syncing],Entries=[downloading]};
        CatalogService.Merge(syncingState,syncing,(IReadOnlyList<CloudEntry>)[new(){AccountId="sync",Name="file.txt",Path="file.txt",ETag="v1"}]);
        downloading.SavedETag="v1";downloading.Downloaded=DateTimeOffset.Now;downloading.Status=LocalStatus.Current;CatalogService.RecordDownload(syncingState,downloading);
        check(syncingState.Entries.Single().Status==LocalStatus.Current&&syncingState.Entries.Single().Downloaded!=null,"Download completion updates the current catalog record after concurrent refresh");
        CatalogService.Merge(syncingState,syncing,(IReadOnlyList<CloudEntry>)[new(){AccountId="sync",Name="file.txt",Path="file.txt",ETag="v2"}]);CatalogService.RecordDownload(syncingState,downloading);
        check(syncingState.Entries.Single().Status==LocalStatus.Mismatch&&syncingState.Entries.Single().SavedETag=="v1","A newer cloud revision is not falsely marked synchronized by an older in-flight download");
        var account=new Account();var entry=new CloudEntry{Name="sample.png",Path="sample.png",Size=3,ETag="1"};
        var cache=new PreviewCache(Path.Combine(root,"preview-tests"));var downloads=new BlobProvider("abc");
        var paths=await Task.WhenAll(Enumerable.Range(0,4).Select(_=>cache.GetAsync(account,entry,downloads,10,20,default)));
        check(downloads.Calls==1&&paths.Distinct().Count()==1&&File.ReadAllText(paths[0])=="abc","Concurrent preview requests share one download and cached bytes");
        entry.ETag="2";var updated=await cache.GetAsync(account,entry,new BlobProvider("def"),10,20,default);
        check(updated!=paths[0]&&File.ReadAllText(updated)=="def","Cloud revision invalidates preview cache");
        entry.Size=0;entry.ETag="3";
        try{await cache.GetAsync(account,entry,new BlobProvider("this is too large"),5,20,default);throw new Exception("Missing limit");}catch(IOException){}
        check(!Directory.EnumerateFiles(cache.DirectoryPath,"*.part").Any(),"Unknown-size previews enforce a streaming cap and remove failed partial files");
        cache.Trim(3);check(StorageStatistics.DirectorySize(cache.DirectoryPath)<=3,"Preview cache eviction respects its total byte limit");
        cache.Clear();check(!Directory.EnumerateFiles(cache.DirectoryPath).Any(),"Preview cache can be cleared without touching local copies");
        using(var output=new MemoryStream())using(var limited=new RateLimitedStream(output,1000))
        {
            var watch=Stopwatch.StartNew();await limited.WriteAsync(new byte[150]);check(watch.ElapsedMilliseconds>=125&&output.Length==150,"Transfer rate limit actually delays writes");
        }
        using(var output=new MemoryStream())using(var limited=new RateLimitedStream(output,1))using(var ct=new CancellationTokenSource(30))
        {try{await limited.WriteAsync(new byte[100],ct.Token);throw new Exception("Missing rate cancellation");}catch(OperationCanceledException){}check(output.Length==0,"Throttled transfer cancellation does not write buffered data");}
        var settings=new Settings{ScanConcurrency=6,PreviewFileBytes=5_000_000,PreviewCacheBytes=40_000_000,TransferConcurrency=3};
        var store=new StateStore(Path.Combine(root,"new-settings"));var restored=store.ImportSettings(store.ExportSettings(settings));
        check(restored.ScanConcurrency==6&&restored.TransferConcurrency==3&&restored.PreviewFileBytes==5_000_000,"New synchronization and preview settings survive export/import");
        var a=new Account{AdaptiveRefresh=true,RefreshMinutes=3,LastUsed=DateTimeOffset.Now,LastCheck=DateTimeOffset.Now.AddMinutes(-4)};
        check(RefreshPolicy.Due(a,DateTimeOffset.Now)&&RefreshPolicy.Interval(a,DateTimeOffset.Now)==TimeSpan.FromMinutes(3),"Per-account refresh interval is used by background scheduling");
    }
    sealed class BlobProvider(string content):ICloudProvider
    {
        public int Calls;
        public async Task DownloadAsync(string p,Stream s,IProgress<long>? progress,CancellationToken ct){Interlocked.Increment(ref Calls);await Task.Delay(20,ct);await s.WriteAsync(Encoding.UTF8.GetBytes(content),ct);}
        public Task<IReadOnlyList<CloudEntry>> ListAsync(string p,CancellationToken ct)=>throw new NotSupportedException();
        public Task UploadAsync(string p,Stream s,CancellationToken ct)=>throw new NotSupportedException();public Task CreateFolderAsync(string p,CancellationToken ct)=>throw new NotSupportedException();public Task DeleteAsync(string p,CancellationToken ct)=>throw new NotSupportedException();public Task MoveAsync(string p,string q,CancellationToken ct)=>throw new NotSupportedException();
    }
    sealed class TreeProvider:ICloudProvider
    {
        private int active;public int Peak,Completed;public bool FailOnce,Retried;public HashSet<string> Visited=new();
        public async Task<IReadOnlyList<CloudEntry>> ListAsync(string p,CancellationToken ct)
        {
            lock(Visited)Visited.Add(p);
            if(p=="")return Enumerable.Range(0,12).Select(i=>new CloudEntry{Name="d"+i,Path="d"+i,IsFolder=true}).Append(new(){Name="skip",Path="skip",IsFolder=true}).ToList();
            if(FailOnce&&p=="d0"&&!Retried){Retried=true;throw new IOException("Temporary failure");}
            var current=Interlocked.Increment(ref active);Peak=Math.Max(Peak,current);
            try{await Task.Delay(40,ct);Interlocked.Increment(ref Completed);return [new(){Name="item.txt",Path=p+"/item.txt"}];}finally{Interlocked.Decrement(ref active);}
        }
        public Task DownloadAsync(string p,Stream s,IProgress<long>? progress,CancellationToken ct)=>throw new NotSupportedException();public Task UploadAsync(string p,Stream s,CancellationToken ct)=>throw new NotSupportedException();public Task CreateFolderAsync(string p,CancellationToken ct)=>throw new NotSupportedException();public Task DeleteAsync(string p,CancellationToken ct)=>throw new NotSupportedException();public Task MoveAsync(string p,string q,CancellationToken ct)=>throw new NotSupportedException();
    }
}
