using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RCHub.App;
using RCHub.Core;
static class CloudPreviewChecks
{
    public static void Run(string root)
    {
        var reserve=new TcpListener(IPAddress.Loopback,0);reserve.Start();var port=((IPEndPoint)reserve.LocalEndpoint).Port;reserve.Stop();
        using var server=new HttpListener();server.Prefixes.Add($"http://127.0.0.1:{port}/");server.Start();
        using var image=new RenderTargetBitmap(new PixelSize(32,32));using var bytes=new MemoryStream();image.Save(bytes);var png=bytes.ToArray();
        var getCount=0;var listCount=0;var releaseFolder=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handling=Task.Run(async()=>
        {
            try{while(server.IsListening){var request=await server.GetContextAsync();_ = Handle(request);}}catch(HttpListenerException){}catch(ObjectDisposedException){}
        });
        async Task Handle(HttpListenerContext context)
        {
            try
            {
                if(context.Request.HttpMethod=="PROPFIND")
                {
                    Interlocked.Increment(ref listCount);
                    var folder=context.Request.Url!.AbsolutePath;
                    if(folder=="/slow/")await releaseFolder.Task;
                    var xml="<d:multistatus xmlns:d='DAV:'>"+(folder=="/"?Resource("image.png",false)+Resource("slow/",true):Resource("slow/child.txt",false))+"</d:multistatus>";
                    var data=Encoding.UTF8.GetBytes(xml);context.Response.StatusCode=207;await context.Response.OutputStream.WriteAsync(data);
                }
                else{Interlocked.Increment(ref getCount);context.Response.ContentLength64=png.Length;await context.Response.OutputStream.WriteAsync(png);}
            }catch(HttpListenerException){}finally{context.Response.Close();}
        }
        string Resource(string name,bool folder)=>$"<d:response><d:href>/{name}</d:href><d:propstat><d:prop><d:resourcetype>{(folder?"<d:collection/>":"")}</d:resourcetype><d:getcontentlength>{png.Length}</d:getcontentlength><d:getetag>test</d:getetag></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>";
        var state=new AppState{LocalRoot=root};state.Settings.FirstRunComplete=true;
        state.Accounts.Add(new(){Id="preview",Name="preview",Endpoint=$"http://127.0.0.1:{port}/",Provider=ProviderKind.WebDav,LocalFolder=Path.Combine(root,"never-downloaded"),AdaptiveRefresh=true});
        var store=new StateStore(Path.Combine(root,"preview-ui"));store.Save(state);
        var view=new MainView(store);var window=new Window{Width=1360,Height=860,Content=view};window.Show();Layout();
        void Layout(){Dispatcher.UIThread.RunJobs();window.Measure(new(1360,860));window.Arrange(new(0,0,1360,860));Dispatcher.UIThread.RunJobs();}
        void Until(Func<bool> ready,string error)
        {
            var timeout=System.Diagnostics.Stopwatch.StartNew();while(!ready()&&timeout.Elapsed<TimeSpan.FromSeconds(10)){Thread.Sleep(20);Layout();}
            if(!ready())throw new Exception(error);
        }
        DataGrid Table()=>window.GetVisualDescendants().OfType<DataGrid>().Single();
        Until(()=>Table().ItemsSource.Cast<CloudEntry>().Any(f=>f.Name=="image.png"),"New account did not show root files during initial indexing");
        if(releaseFolder.Task.IsCompleted)throw new Exception("Early indexing check was not blocked");
        Console.WriteLine("PASS first automatic scan displays root files before a slow nested folder finishes");
        var preview=window.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"◧ Просмотр"));preview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout();
        Table().SelectedItem=Table().ItemsSource.Cast<CloudEntry>().Single(f=>f.Name=="image.png");Layout();
        var details=(StackPanel)typeof(MainView).GetField("_details",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(view)!;
        Until(()=>details.GetVisualDescendants().OfType<Image>().Any(i=>i.Source is Bitmap),"Cloud-only image did not appear in preview panel");
        if(Directory.Exists(state.Accounts[0].LocalFolder)||getCount!=1)throw new Exception("Preview modified local copies or downloaded repeatedly");
        Console.WriteLine("PASS cloud image renders from bounded cache without downloading to the account local folder");
        var imageEntry=Table().ItemsSource.Cast<CloudEntry>().Single(f=>f.Name=="image.png");
        var viewerTask=(Task)typeof(MainView).GetMethod("ImageViewer",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(view,[imageEntry])!;
        Until(()=>viewerTask.IsCompleted,"Image viewer did not load");viewerTask.GetAwaiter().GetResult();
        var imageWindow=window.OwnedWindows.Single();
        if(!imageWindow.GetVisualDescendants().OfType<Slider>().Any()||!imageWindow.GetVisualDescendants().OfType<Image>().Any(i=>i.Source is Bitmap))throw new Exception("Image viewer has no image or zoom control");
        imageWindow.Close();Console.WriteLine("PASS enlarged image viewer opens cached image with zoom controls");
        releaseFolder.TrySetResult();
        Until(()=>store.Load()?.Accounts[0].LastCheck!=null,"Initial automatic indexing did not complete");
        var live=(AppState)typeof(MainView).GetField("_state",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(view)!;
        live.Accounts[0].LastCheck=DateTimeOffset.Now.AddHours(-1);live.Accounts[0].RefreshMinutes=1;
        ((Dictionary<string,DateTimeOffset>)typeof(MainView).GetField("_lastAttempt",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(view)!).Clear();
        var task=(Task)typeof(MainView).GetMethod("RefreshDueAccounts",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(view,null)!;
        Until(()=>task.IsCompleted,"Background refresh failed");task.GetAwaiter().GetResult();
        if(listCount<4)throw new Exception("Background refresh did not rescan the account");
        live.Accounts[0].AdaptiveRefresh=false;live.Accounts[0].LastCheck=DateTimeOffset.Now.AddHours(-1);var before=listCount;
        task=(Task)typeof(MainView).GetMethod("RefreshDueAccounts",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(view,null)!;
        Until(()=>task.IsCompleted,"Manual refresh policy failed");if(listCount!=before)throw new Exception("Disabled background refresh still fetched files");
        Console.WriteLine("PASS background scheduling refreshes due accounts and respects manual-only mode");
        window.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"⚙  Настройки")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout();
        var tabs=window.GetVisualDescendants().OfType<TabControl>().Single();
        if(!tabs.Items.OfType<TabItem>().Any(t=>Equals(t.Header,"Синхронизация"))||!tabs.Items.OfType<TabItem>().Any(t=>Equals(t.Header,"Предпросмотр")))throw new Exception("New settings tabs missing");
        Console.WriteLine("PASS synchronization and preview settings are exposed in the application");window.Close();server.Stop();handling.GetAwaiter().GetResult();
    }
}
