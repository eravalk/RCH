using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using RCHub.Core;

internal static class WebDavRoundTrip
{
    public static async Task RunAsync(string temporaryDirectory)
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var endpoint = $"http://127.0.0.1:{port}/dav/";
        using var server = new HttpListener();
        server.Prefixes.Add(endpoint);
        server.Start();
        var files = new Dictionary<string, byte[]>();
        var folders = new HashSet<string>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var serving = Task.Run(async () =>
        {
            while (!timeout.IsCancellationRequested)
            {
                var context = await server.GetContextAsync().WaitAsync(timeout.Token);
                var request = context.Request;
                var response = context.Response;
                var name = Uri.UnescapeDataString(request.Url!.AbsolutePath[5..]).Trim('/');
                switch (request.HttpMethod)
                {
                    case "MKCOL": folders.Add(name); break;
                    case "PUT":
                        using (var body = new MemoryStream())
                        {
                            await request.InputStream.CopyToAsync(body);
                            files.Add(name, body.ToArray());
                        }
                        break;
                    case "GET":
                        await response.OutputStream.WriteAsync(files[name]);
                        break;
                    case "MOVE":
                        var destination = new Uri(request.Headers["Destination"]!);
                        files.Add(Uri.UnescapeDataString(destination.AbsolutePath[5..]), files[name]);
                        files.Remove(name);
                        break;
                    case "DELETE": files.Remove(name); break;
                    case "PROPFIND":
                        XNamespace d = "DAV:";
                        var listing = new XElement(d+"multistatus");
                        foreach (var path in folders.Concat(files.Keys).Where(p => Paths.Parent(p)==name))
                        {
                            listing.Add(new XElement(d+"response",new XElement(d+"href","/dav/"+path),
                                new XElement(d+"propstat",new XElement(d+"prop",
                                    new XElement(d+"resourcetype",folders.Contains(path)?new XElement(d+"collection"):null),
                                    new XElement(d+"getcontentlength",files.GetValueOrDefault(path)?.Length??0)),
                                    new XElement(d+"status","HTTP/1.1 200 OK"))));
                        }
                        response.StatusCode=207;
                        await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(listing.ToString()));
                        break;
                    default: response.StatusCode=405; break;
                }
                response.Close();
            }
        });
        try
        {
            var ct=timeout.Token;
            var account=new Account{Endpoint=endpoint,LocalFolder=temporaryDirectory};
            using var provider=new WebDavProvider(account,"");
            await provider.CreateFolderAsync("Фото",ct);
            var original=Encoding.UTF8.GetBytes("Проверка RC Hub");
            await provider.UploadAsync("Фото/a.txt",new MemoryStream(original),ct);
            await provider.MoveAsync("Фото/a.txt","Фото/b.txt",ct);
            var catalog=await new CatalogService().IndexAsync(provider,null,ct);
            var file=catalog.Single(entry=>entry.Path=="Фото/b.txt");
            var saved=await new LocalFiles().DownloadAsync(account,file,provider,ExistingFileAction.Replace,null,ct);
            if (!(await File.ReadAllBytesAsync(saved,ct)).SequenceEqual(original))
                throw new IOException("Round trip changed bytes");
            await provider.DeleteAsync(file.Path,ct);
            if ((await provider.ListAsync("Фото",ct)).Count!=0) throw new IOException("Deletion failed");
        }
        finally
        {
            timeout.Cancel();
            try {await serving;} catch(OperationCanceledException){}
        }
    }
}
