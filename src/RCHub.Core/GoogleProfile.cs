using System.Net.Http.Headers;
using System.Text.Json;
namespace RCHub.Core;

public static class GoogleProfile
{
    private static readonly HttpClient Client=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(8),MaxResponseContentBufferSize=2_000_000};
    public static async Task LoadAsync(Account account,string config,string directory)
    {
        if(account.Provider!=ProviderKind.Rclone || account.Service!="Google Диск")return;
        // Read only this remote's Drive token; never forward it to an image host.
        bool section=false,drive=false;
        string? token=null;
        foreach(var line in await File.ReadAllLinesAsync(config))
        {
            if(line.StartsWith('[')){section=line.Trim()=="["+account.Endpoint+"]";continue;}
            if(!section)continue;
            var pair=line.Split('=',2);if(pair.Length!=2)continue;
            if(pair[0].Trim()=="type")drive=pair[1].Trim()=="drive";
            if(pair[0].Trim()=="token"){using var json=JsonDocument.Parse(pair[1]);token=json.RootElement.GetProperty("access_token").GetString();}
        }
        if(!drive || string.IsNullOrEmpty(token))return;
        using var request=new HttpRequestMessage(HttpMethod.Get,"https://www.googleapis.com/drive/v3/about?fields=user(displayName,emailAddress,photoLink)");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        using var response=await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var profile=JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var user=profile.RootElement.GetProperty("user");
        if(user.TryGetProperty("emailAddress",out var mail))account.ProfileEmail=mail.GetString()??"";
        if(!string.IsNullOrEmpty(account.AvatarPath) || !user.TryGetProperty("photoLink",out var photo)
            || !Uri.TryCreate(photo.GetString(),UriKind.Absolute,out var url) || url.Scheme!="https"
            || !(url.Host.EndsWith(".googleusercontent.com",StringComparison.OrdinalIgnoreCase)||url.Host.EndsWith(".google.com",StringComparison.OrdinalIgnoreCase)))return;
        var bytes=await Client.GetByteArrayAsync(url);
        Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,Guid.NewGuid().ToString("N")+".img");
        await File.WriteAllBytesAsync(path,bytes);account.AvatarPath=path;
    }
}
