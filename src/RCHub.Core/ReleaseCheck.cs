using System.Net;
using System.Text.Json;

namespace RCHub.Core;

public sealed record ReleaseStatus(bool Newer,string Message,string? Download=null);
public static class ReleaseCheck
{
    public const string Releases="https://github.com/eravalk/RCH/releases";
    public static Version Installed => typeof(ReleaseCheck).Assembly.GetName().Version!;
    public static string DisplayVersion=>Installed.ToString(3);
    public static async Task<ReleaseStatus> CheckAsync(HttpClient client,Version installed,CancellationToken ct=default)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.github.com/repos/eravalk/RCH/releases/latest");
        request.Headers.UserAgent.ParseAdd("RCH/"+DisplayVersion);
        using var response=await client.SendAsync(request,ct);
        if(response.StatusCode==HttpStatusCode.NotFound)
            return new(false,"GitHub не предоставил релиз. Репозиторий может быть закрыт или релиз ещё не опубликован.");
        response.EnsureSuccessStatusCode();
        using var json=JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
        var root=json.RootElement;
        if(root.GetProperty("draft").GetBoolean()||root.GetProperty("prerelease").GetBoolean())
            return new(false,"Стабильный релиз недоступен.");
        var tag=root.GetProperty("tag_name").GetString()??"";
        if(!Version.TryParse(tag.TrimStart('v','V'),out var parsed))
            return new(false,"Не удалось определить номер версии релиза.");
        static Version Normalize(Version v)=>new(v.Major,v.Minor,Math.Max(0,v.Build),Math.Max(0,v.Revision));
        if(Normalize(parsed)<=Normalize(installed))return new(false,"Установлена актуальная версия RCH.");
        var name=$"RCH-{parsed}-win-x64.zip";
        var expected=$"{Releases}/download/{tag}/{name}";
        var asset=root.GetProperty("assets").EnumerateArray().Any(a=>a.GetProperty("name").GetString()==name && a.GetProperty("browser_download_url").GetString()==expected);
        return new(true,"Доступна новая версия RCH "+parsed,asset?expected:Releases+"/tag/"+Uri.EscapeDataString(tag));
    }
}
