namespace RCHub.Core;
public static class WebLocation
{
    public static Uri? For(Account account,CloudEntry? file=null)
    {
        var path=file==null?"":string.Join("/",file.Path.Split('/').Select(Uri.EscapeDataString));
        var parent=file==null?"":string.Join("/",file.Parent.Split('/').Select(Uri.EscapeDataString));
        var service=account.Service;
        string? address=service switch {
            "Google Диск" when file==null => "https://drive.google.com/drive/my-drive",
            "Google Диск" when !string.IsNullOrWhiteSpace(file?.ProviderId) => "https://drive.google.com/open?id="+Uri.EscapeDataString(file.ProviderId),
            "Яндекс Диск" when file==null||file.IsFolder => "https://disk.yandex.ru/client/disk/"+path,
            "Dropbox" when file==null||file.IsFolder => "https://www.dropbox.com/home/"+path,
            "Dropbox" => "https://www.dropbox.com/home/"+parent+"?preview="+Uri.EscapeDataString(file!.Name),
            "OneDrive" when file==null => "https://onedrive.live.com/",
            "iCloud Drive" when file==null => "https://www.icloud.com/iclouddrive/",
            "MEGA" when file==null => "https://mega.nz/fm",
            "Облако Mail.ru" when file==null => "https://cloud.mail.ru/home/",
            _=>null};
        return address==null?null:new Uri(address);
    }
}
