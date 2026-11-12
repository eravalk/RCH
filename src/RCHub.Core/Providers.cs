using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
namespace RCHub.Core;

public interface ICloudProvider
{
    Task<IReadOnlyList<CloudEntry>> ListAsync(string folder, CancellationToken ct);
    Task DownloadAsync(string path, Stream destination, IProgress<long>? progress, CancellationToken ct);
    Task UploadAsync(string path, Stream source, CancellationToken ct);
    Task CreateFolderAsync(string path, CancellationToken ct);
    Task DeleteAsync(string path, CancellationToken ct);
    Task MoveAsync(string source, string destination, CancellationToken ct);
}
public sealed class WebDavProvider : IAccountProvider
{
    private static readonly XNamespace Dav = "DAV:";
    private readonly HttpClient _http;
    private readonly Uri _root;
    private readonly string _account;
    public WebDavProvider(Account account, string password, HttpMessageHandler? handler = null)
    {
        if (!Uri.TryCreate(account.Endpoint.TrimEnd('/') + "/", UriKind.Absolute, out var root) || root.Scheme is not ("https" or "http")) throw new ArgumentException("Укажите адрес WebDAV с https://");
        if (!string.IsNullOrEmpty(root.UserInfo) || !string.IsNullOrEmpty(root.Query) || !string.IsNullOrEmpty(root.Fragment)) throw new ArgumentException("Адрес сервера не должен содержать логин, пароль или параметры");
        if (root.Scheme == "http" && !root.IsLoopback) throw new ArgumentException("Для удалённого сервера требуется HTTPS");
        _root = root; _account = account.Id;
        _http = handler is null ? new(new HttpClientHandler { AllowAutoRedirect = false }) : new(handler);
        _http.Timeout = TimeSpan.FromMinutes(15);
        if (!string.IsNullOrEmpty(account.UserName)) _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(account.UserName + ":" + password)));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("RCHub/0.1");
    }
    private Uri Url(string path) => new(_root, string.Join('/', Paths.Normalize(path).Split('/').Select(Uri.EscapeDataString)) + (path.EndsWith('/') && Paths.Normalize(path).Length>0 ? "/" : ""));
    public async Task<IReadOnlyList<CloudEntry>> ListAsync(string folder, CancellationToken ct)
    {
        var requestUrl = Url(folder.TrimEnd('/') + "/");
        using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), requestUrl);
        request.Headers.Add("Depth", "1");
        request.Content = new StringContent("<?xml version=\"1.0\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:displayname/><d:resourcetype/><d:getcontentlength/><d:getlastmodified/><d:getetag/></d:prop></d:propfind>", Encoding.UTF8, "application/xml");
        using var response = await _http.SendAsync(request, ct); Ensure(response);
        var xml = await response.Content.ReadAsStringAsync(ct);
        var document = XDocument.Parse(xml); var result = new List<CloudEntry>();
        foreach (var resource in document.Descendants(Dav + "response"))
        {
            var href = resource.Element(Dav + "href")?.Value;
            if (!Uri.TryCreate(requestUrl, href, out var uri)) continue;
            if (uri.Scheme != _root.Scheme || uri.Authority != _root.Authority || !_root.IsBaseOf(uri)) continue;
            var relative = Uri.UnescapeDataString(_root.MakeRelativeUri(uri).OriginalString);
            string path; try { path = Paths.Normalize(relative); } catch (ArgumentException) { continue; }
            if (path == Paths.Normalize(folder) || Paths.Parent(path) != Paths.Normalize(folder)) continue;
            var propstat = resource.Elements(Dav + "propstat").FirstOrDefault(p => p.Element(Dav + "status")?.Value.Contains(" 200 ") == true);
            var properties = propstat?.Element(Dav + "prop"); if (properties is null) continue;
            var name = path.Split('/').Last();
            result.Add(new() { AccountId = _account, Path = path, Name = name,
                IsFolder = properties.Element(Dav + "resourcetype")?.Element(Dav + "collection") is not null,
                Size = long.TryParse(properties.Element(Dav + "getcontentlength")?.Value, out var length) ? length : 0,
                Modified = DateTimeOffset.TryParse(properties.Element(Dav + "getlastmodified")?.Value, out var modified) ? modified : null,
                ETag = properties.Element(Dav + "getetag")?.Value, Status = LocalStatus.CloudOnly });
        }
        return result;
    }
    public async Task DownloadAsync(string path, Stream destination, IProgress<long>? progress, CancellationToken ct)
    {
        using var response = await _http.GetAsync(Url(path), HttpCompletionOption.ResponseHeadersRead, ct); Ensure(response);
        await using var stream = await response.Content.ReadAsStreamAsync(ct); var buffer = new byte[81_920]; long total = 0;
        int read; while ((read = await stream.ReadAsync(buffer, ct)) > 0) { await destination.WriteAsync(buffer.AsMemory(0, read), ct); total += read; progress?.Report(total); }
        if (response.Content.Headers.ContentLength is long expected && expected != total) throw new IOException("Скачанный размер не совпадает с ответом сервера");
    }
    public async Task UploadAsync(string path, Stream source, CancellationToken ct)
    {
        using var content = new StreamContent(source); content.Headers.ContentLength = source.Length - source.Position;
        using var request = new HttpRequestMessage(HttpMethod.Put, Url(path)) { Content = content };
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        using var response = await _http.SendAsync(request, ct); Ensure(response);
    }
    public async Task CreateFolderAsync(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(new HttpMethod("MKCOL"), Url(path));
        using var response = await _http.SendAsync(request, ct); Ensure(response);
    }
    public async Task DeleteAsync(string path, CancellationToken ct) { using var r = await _http.DeleteAsync(Url(path), ct); Ensure(r); }
    public async Task MoveAsync(string source, string destination, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(new HttpMethod("MOVE"), Url(source));
        request.Headers.Add("Destination", Url(destination).AbsoluteUri); request.Headers.Add("Overwrite", "F");
        using var response = await _http.SendAsync(request, ct); Ensure(response);
    }
    private static void Ensure(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new UnauthorizedAccessException("Требуется вход. Проверьте логин и пароль приложения.");
        if ((int)response.StatusCode == 507) throw new IOException("В хранилище нет места. Процесс приостановлен; продолжите его кнопкой после освобождения места.");
        response.EnsureSuccessStatusCode();
    }
    public void Dispose() => _http.Dispose();
}
public sealed class CatalogService
{
    public async Task<IReadOnlyList<CloudEntry>> IndexAsync(ICloudProvider provider, IProgress<int>? progress, CancellationToken ct)
    {
        var result = new List<CloudEntry>(); var folders = new Queue<string>(); var visited = new HashSet<string>(StringComparer.Ordinal); folders.Enqueue("");
        while (folders.TryDequeue(out var path))
        {
            ct.ThrowIfCancellationRequested(); if (!visited.Add(path)) continue;
            var children = await provider.ListAsync(path, ct); result.AddRange(children); progress?.Report(result.Count);
            foreach (var f in children.Where(f => f.IsFolder)) folders.Enqueue(f.Path);
            await Task.Delay(125, ct); // One request at a time; avoid bursts against a personal server.
        }
        return result;
    }
    public static void Merge(AppState state, Account account, IReadOnlyList<CloudEntry> incoming)
    {
        var old = state.Entries.Where(f => f.AccountId == account.Id).ToDictionary(f => f.Path, StringComparer.Ordinal);
        foreach (var file in incoming)
        {
            if (old.TryGetValue(file.Path, out var previous))
            {
                file.Id = previous.Id; file.IsFavorite = previous.IsFavorite; file.Downloaded = previous.Downloaded;
                file.SuppressAutoDownload = previous.SuppressAutoDownload; file.SavedETag = previous.SavedETag; file.Status = previous.Status;
                if (!file.IsFolder && previous.Downloaded is not null && !string.IsNullOrWhiteSpace(account.LocalFolder))
                {
                    var local = Paths.LocalPath(account.LocalFolder, file.Path);
                    if (!File.Exists(local)) { file.Status = LocalStatus.RemovedFromDevice; file.SuppressAutoDownload = true; }
                    else if (previous.SavedETag is not null && previous.SavedETag != file.ETag) file.Status = file.Kind == "Программы" ? LocalStatus.NewVersion : LocalStatus.Mismatch;
                    else if (new FileInfo(local).Length != file.Size) file.Status = LocalStatus.Mismatch;
                }
            }
        }
        // Replace only after a complete, successful walk. Offline/error never empties the saved catalog.
        state.Entries.RemoveAll(f => f.AccountId == account.Id); state.Entries.AddRange(incoming);
        account.LastCheck = DateTimeOffset.Now;
    }
}
