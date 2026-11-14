using RCHub.Core;
using System.Net;
using System.Text;
var root=Path.Combine(Path.GetTempPath(),"rchub-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
var count=0;
void Check(bool valid,string name){if(!valid)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
void Reject(Action action,string name){try{action();}catch(ArgumentException){Check(true,name);return;}throw new Exception(name);}
try{
Reject(()=>Paths.LocalPath(root,"../escape"),"Traversal rejected");
Reject(()=>Paths.LocalPath(root,"x:stream"),"Alternate stream rejected");
Check(Paths.LocalPath(root,"Фото/снимок.jpg").StartsWith(root),"Unicode local path");
var a=new Account{LocalFolder=Path.Combine(root,"files"),AdaptiveRefresh=false};var now=DateTimeOffset.UtcNow;
Check(RefreshPolicy.Due(a,now),"Initial indexing in manual mode");a.LastCheck=now.AddDays(-90);Check(!RefreshPolicy.Due(a,now),"Manual mode does not poll");
a.AdaptiveRefresh=true;a.LastUsed=now.AddDays(-100);Check(RefreshPolicy.Interval(a,now)==TimeSpan.FromDays(30),"Inactive account reduces polling");
var store=new StateStore(Path.Combine(root,"state"));var state=new AppState{Accounts=[a]};store.Save(state);Check(store.Load()!.Accounts[0].Id==a.Id,"SQLite persistence");
Check(!store.ExportSettings(state.Settings).Contains(a.Id),"Settings export excludes accounts");Reject(()=>store.ImportSettings("{}"),"Invalid configuration rejected");
var xml="<d:multistatus xmlns:d='DAV:'>"+Resource("/dav/файл.txt")+Resource("https://evil.example/dav/steal")+Resource("/elsewhere/no")+"</d:multistatus>";
a.Endpoint="https://example.org/dav/";
using(var provider=new WebDavProvider(a,"secret",new Handler(xml))){var entries=await provider.ListAsync("",default);Check(entries.Count==1&&entries[0].Name=="файл.txt","WebDAV restricts returned resources to its root");}
Directory.CreateDirectory(a.LocalFolder);var entry=new CloudEntry{Path="test.txt",Name="test.txt",Size=3};var destination=Paths.LocalPath(a.LocalFolder,entry.Path);await File.WriteAllTextAsync(destination,"old");
var downloader=new LocalFiles();
try{await downloader.DownloadAsync(a,entry,new FakeProvider("partial",true),ExistingFileAction.Replace,null,default);throw new Exception("Expected failure");}catch(IOException){}
Check(await File.ReadAllTextAsync(destination)=="old"&&!Directory.EnumerateFiles(a.LocalFolder,"*.part").Any(),"Interrupted download preserves existing file and removes staging");
try{await downloader.DownloadAsync(a,entry,new FakeProvider("wrong"),ExistingFileAction.Replace,null,default);throw new Exception("Expected failure");}catch(IOException){}
Check(await File.ReadAllTextAsync(destination)=="old","Size mismatch never replaces existing file");
await downloader.DownloadAsync(a,entry,new FakeProvider("new"),ExistingFileAction.RenameOld,null,default);
Check(await File.ReadAllTextAsync(destination)=="new"&&Directory.EnumerateFiles(a.LocalFolder,"*_old_*").Any(),"New version preserves old copy");
Check(entry.Status==LocalStatus.Current&&entry.Downloaded!=null,"Successful download metadata");
await WebDavRoundTrip.RunAsync(root);Check(true,"HTTP WebDAV create, upload, rename, index, download, delete");
if (args.Length>0) {await RcloneRoundTrip.RunAsync(args[0],root);Check(true,"Two real rclone processes isolate upload, listing, download, rename and delete by account");}
if (args.Length>0)
{
    foreach (var type in new[]{"sftp","smb","s3","mega"})
    {
        var questions=new List<string>();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await AccountLogin.ConfigureAsync(args[0],Path.Combine(root,"forms.conf"),"form"+type,type,(question,ct)=>
        {
            questions.Add(question.Key);
            return Task.FromResult(question.Key switch {
                "user"=>"sample","host"=>"localhost","provider"=>"Other","pass"=>"local-test-value",
                "access_key_id"=>"example","secret_access_key"=>"local-test-value",_=>question.DefaultValue
            });
        },timeout.Token);
        if(type=="sftp")Check(!File.ReadAllText(Path.Combine(root,"forms.conf")).Contains("pass = local-test-value"),"Backend password uses rclone config encoding");
        Check(questions.Contains(type=="s3"?"provider":type=="mega"?"user":"host"),"In-app login form: "+type);
    }
}
var denied=ProviderError.Describe("Яндекс Диск","Чтение каталога","NOTICE: log\nERROR: listing: [410 - GoneError] Gone",1);
Check(denied.Contains("HTTP 410") && denied.Contains("Чтение каталога") && !denied.Contains("NOTICE"),"Yandex 410 listing diagnostic");
var sampleReport=LoginDiagnostic.Report("FATAL: failed to send SMS code: HTTP 403; user=private@example.com; password=secret-value; code=123456; Authorization: Bearer TOPSECRET",
    "Проверка SMS",2);
Check(sampleReport.Contains("http 403") && sampleReport.Contains("failed to send sms code") && new[]{"private@example.com","secret-value","123456","TOPSECRET"}.All(t=>!sampleReport.Contains(t)), "iCloud diagnostic keeps failure and strips secret values");
if(args.Length>0)
{
    var seen=new List<string>();
    try
    {
        await AccountLogin.ConfigureAsync(args[0],Path.Combine(root,"trial.conf"),"trial","yandex",(question,ct)=>{
            seen.Add(question.Key);
            if(question.Key=="client_secret"){
                if(!question.Secret||!question.Required)throw new Exception("OAuth secret field not protected");
                throw new OperationCanceledException();
            }
            return Task.FromResult("example-id");
        },default,true);
    }
    catch(OperationCanceledException){}
    Check(seen.SequenceEqual(new[]{"client_id","client_secret"}),"Custom Yandex client prompts before authorization");
}
var drive=new Account{Service="Google Диск"};
Check(WebLocation.For(drive,new CloudEntry{ProviderId="ab/c?",Path="old"})!.AbsoluteUri.EndsWith("ab%2Fc%3F"),"Drive web view uses escaped provider ID");
Check(WebLocation.For(drive,new CloudEntry{Path="old"})==null,"Legacy entry without provider ID has no incorrect file link");
Check(WebLocation.For(new Account{Service="Свой сервер"})==null,"No guessed web interface for private servers");
var releaseJson="""{"tag_name":"v0.2.0","draft":false,"prerelease":false,"assets":[{"name":"RCH-0.2.0-win-x64.zip","browser_download_url":"https://github.com/eravalk/RCH/releases/download/v0.2.0/RCH-0.2.0-win-x64.zip"}]}""";
using(var client=new HttpClient(new ReleaseReply(200,releaseJson))){
    var newer=await ReleaseCheck.CheckAsync(client,new Version(0,1,9,0));
    Check(newer.Newer&&newer.Download!.EndsWith(".zip"),"New stable release links to matching Windows package");
    Check(!(await ReleaseCheck.CheckAsync(client,new Version(0,2,0,0))).Newer,"Matching release does not notify");
}
using(var client=new HttpClient(new ReleaseReply(404,"{}"))){
    var missing=await ReleaseCheck.CheckAsync(client,new Version(0,1,9));
    Check(!missing.Newer && missing.Message.Contains("не предоставил"),"Missing or private release is reported distinctly");
}
var quotaAccount=new Account{UsedBytes=7,TotalBytes=10};
CloudQuota.Parse(System.Text.Encoding.UTF8.GetBytes("""{"total":1000,"used":100,"free":600,"other":300}""")).Apply(quotaAccount);
Check(quotaAccount.UsedBytes==400 && quotaAccount.DriveUsedBytes==100 && quotaAccount.FreeBytes==600,"Quota occupancy uses total minus free including other services");
CloudQuota.Parse(System.Text.Encoding.UTF8.GetBytes("""{"used":101}""")).Apply(quotaAccount);
Check(quotaAccount.TotalBytes==null && !quotaAccount.QuotaKnown && quotaAccount.CapacityLabel.Contains("лимит неизвестен"),"Partial quota never leaves a previous total or zero bar");
quotaAccount.QuotaError="Ошибка запроса";
Check(quotaAccount.CapacityLabel.Contains("устарело"),"Failed quota refresh is marked stale");
var legacy=new Account{UsedBytes=100,TotalBytes=1000};
Check(!legacy.QuotaKnown && legacy.CapacityLabel=="Объём ещё не получен","Legacy quota without timestamp is not presented as fresh");
var safe=SafeDiagnostic.Report("failed to create file system for private/file: missing icloud trust token; cookie=SECRET; password=123456; https://x.test/?code=123456","account=synthetic command=lsjson",1);
Check(safe.Contains("missing icloud trust token") && !safe.Contains("SECRET") && !safe.Contains("123456") && !safe.Contains("private/file"),"Post-login stderr retains known error phrase and strips private values");
Console.WriteLine($"{count} checks passed");
}finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
static string Resource(string href)=>$"<d:response><d:href>{href}</d:href><d:propstat><d:prop><d:getcontentlength>3</d:getcontentlength></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>";
sealed class Handler(string xml):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.MultiStatus){Content=new StringContent(xml)});}
sealed class FakeProvider(string content,bool fail=false):ICloudProvider{
public async Task DownloadAsync(string path,Stream destination,IProgress<long>? progress,CancellationToken ct){await destination.WriteAsync(Encoding.UTF8.GetBytes(content),ct);if(fail)throw new IOException("interrupted");}
public Task<IReadOnlyList<CloudEntry>> ListAsync(string folder,CancellationToken ct)=>throw new NotSupportedException();public Task UploadAsync(string path,Stream source,CancellationToken ct)=>throw new NotSupportedException();public Task CreateFolderAsync(string path,CancellationToken ct)=>throw new NotSupportedException();public Task DeleteAsync(string path,CancellationToken ct)=>throw new NotSupportedException();public Task MoveAsync(string source,string destination,CancellationToken ct)=>throw new NotSupportedException();}

sealed class ReleaseReply(int status,string json):HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)status){Content=new StringContent(json)}); }
