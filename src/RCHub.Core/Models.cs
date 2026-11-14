using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RCHub.Core;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; PropertyChanged?.Invoke(this, new(name)); return true;
    }
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public enum ProviderKind { Demo, WebDav, GoogleDrive, Yandex, OneDrive, Dropbox, MailRu, ICloud, Mega, TeraBox, Sftp, S3, Smb, Rclone }
public enum LocalStatus { CloudOnly, Current, Mismatch, NewVersion, RemovedFromDevice }
public enum IconLayout { Service, AvatarWithService, ServiceWithAvatar, Custom }
public sealed class Account
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ProviderKind Provider { get; set; }
    public string Service { get; set; } = "Свой сервер";
    public string Name { get; set; } = "Аккаунт";
    public string Group { get; set; } = "Без группы";
    public string Endpoint { get; set; } = "";
    public string UserName { get; set; } = "";
    public string LocalFolder { get; set; } = "";
    public bool AdaptiveRefresh { get; set; } = true;
    public bool SaveLocally { get; set; }
    public long MaxFileBytes { get; set; } = 50_000_000;
    public long LocalLimitBytes { get; set; } = 30_000_000_000;
    public bool KeepDeletedCopies { get; set; }
    public DateTimeOffset? LastCheck { get; set; }
    public DateTimeOffset? LastUsed { get; set; }
    public long? FreeBytes {get;set;}
    public long? DriveUsedBytes {get;set;}
    public long? OtherBytes {get;set;}
    public long? TrashBytes {get;set;}
    public DateTimeOffset? QuotaChecked {get;set;}
    public string QuotaError {get;set;}="";
    [JsonIgnore] public bool QuotaKnown=>QuotaChecked!=null && UsedBytes.HasValue && TotalBytes is >0;
    [JsonIgnore] public string QuotaDescription => "Квота аккаунта, данные сервиса. Единицы квоты: ГиБ = 1 073 741 824 байта." +
        (QuotaChecked is {} time?" Проверено: "+time.ToLocalTime().ToString("dd.MM HH:mm:ss"):" Ещё не проверено.") +
        (DriveUsedBytes is {} used?" Файлы: "+Format.Quota(used)+".":"") +
        (FreeBytes is {} free?" Свободно: "+Format.Quota(free)+".":"") +
        (OtherBytes is >0?" Другие сервисы: "+Format.Quota(OtherBytes.Value)+".":"") +
        (TrashBytes is >0?" Корзина: "+Format.Quota(TrashBytes.Value)+".":"") +
        (TotalBytes is {} exact?" Всего точно: "+exact.ToString("N0",System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))+" байт.":"") +
        (QuotaError.Length>0?" Последняя проверка: "+QuotaError:"");
    public long? UsedBytes { get; set; }
    public long? TotalBytes { get; set; }
    public IconLayout IconLayout { get; set; }
    public string IconPath { get; set; } = "";
    public string ProfileEmail { get; set; } = "";
    public string AvatarPath { get; set; } = "";
    public string Accent { get; set; } = "#71A9F3";
    public bool IsDemo => Provider == ProviderKind.Demo;
    public string Title => Service + " · " + Name;
    public string CapacityLabel => (QuotaChecked==null?"Объём ещё не получен":
        UsedBytes is long used && TotalBytes is long total?$"{Format.Quota(used)} из {Format.Quota(total)}":
        UsedBytes is long partial?$"Занято: {Format.Quota(partial)} · лимит неизвестен":
        FreeBytes is long free?$"Свободно: {Format.Quota(free)} · лимит неизвестен":
        TotalBytes is long limit?$"Всего: {Format.Quota(limit)} · занято неизвестно":"Квота недоступна") +
        (QuotaError.Length>0?QuotaChecked!=null?" · устарело":" · нет данных":"");
    public double UsedPercent => QuotaKnown?Math.Clamp(UsedBytes!.Value*100d/TotalBytes!.Value,0,100):0;
    public string CatalogError {get;set;}="";
    public string CheckLabel => (LastCheck is null ? "Ещё не проверено" : "Проверено: " + LastCheck.Value.ToLocalTime().ToString("dd.MM, HH:mm")) + (CatalogError.Length>0?" · каталог прочитан не полностью":"");
}
public sealed class CloudEntry : Observable
{
    public string? ProviderId {get;set;}
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AccountId { get; set; } = "";
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsFolder { get; set; }
    public long Size { get; set; }
    public DateTimeOffset? Modified { get; set; }
    public string? ETag { get; set; }
    public string? SavedETag { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? Downloaded { get; set; }
    public bool SuppressAutoDownload { get; set; }
    private LocalStatus _status;
    public LocalStatus Status { get => _status; set { if (Set(ref _status, value)) Notify(nameof(StatusLabel)); } }
    [JsonIgnore] public string StatusLabel => IsFolder ? "Папка" : Status switch { LocalStatus.Current => "Файл актуален", LocalStatus.Mismatch => "Не совпадает", LocalStatus.NewVersion => "Новая версия", LocalStatus.RemovedFromDevice => "Удалён с устройства", _ => "Только в облаке" };
    [JsonIgnore] public string SizeLabel => IsFolder ? "—" : Format.Bytes(Size);
    [JsonIgnore] public string ModifiedLabel => Modified?.ToLocalTime().ToString("dd.MM.yyyy, HH:mm") ?? "—";
    [JsonIgnore] public string Parent => Paths.Parent(Path);
    [JsonIgnore] public string Kind => IsFolder ? "Папки" : System.IO.Path.GetExtension(Name).ToLowerInvariant() switch { ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" => "Изображения", ".mp4" or ".mkv" or ".webm" => "Видео", ".mp3" or ".flac" or ".wav" or ".ogg" => "Музыка", ".zip" or ".rar" or ".7z" => "Архивы", ".exe" or ".msi" or ".apk" => "Программы", _ => "Документы" };
    [JsonIgnore] public string Glyph => IsFolder ? "▰" : Kind switch { "Изображения" => "▧", "Музыка" => "♫", "Видео" => "▷", "Архивы" => "▤", "Программы" => "▦", _ => "▱" };
}
public sealed class TabState
{
    public string AccountId { get; set; } = "";
    public string Path { get; set; } = "";
    public string Page { get; set; } = "files";
}
public sealed class Settings
{
    public string Theme { get; set; } = "Light";
    public string Accent { get; set; } = "#3976E7";
    public string View { get; set; } = "List";
    public bool AutoStart { get; set; }
    public bool Notifications { get; set; } = true;
    public bool FirstRunComplete { get; set; }
    public double TextSize { get; set; } = 13;
    public double IconSize { get; set; } = 24;
    public double RowHeight { get; set; } = 62;
    public double ThumbnailSize { get; set; } = 150;
    public long DownloadBytesPerSecond { get; set; }
    public long UploadBytesPerSecond { get; set; }
}
public sealed class AppState
{
    public string LocalRoot { get; set; } = "";
    public List<PendingDownload> Downloads { get; set; } = [];
    public List<Account> Accounts { get; set; } = [];
    public List<CloudEntry> Entries { get; set; } = [];
    public List<TabState> Tabs { get; set; } = [];
    public List<string> Groups { get; set; } = ["Без группы", "Работа", "Личное", "Архив"];
    public Settings Settings { get; set; } = new();
}
public static class Format
{
    public static string Quota(long value)=>Units(value,1024,["Б","КиБ","МиБ","ГиБ","ТиБ"]);
    public static string Bytes(long value)=>Units(value,1000,["Б","КБ","МБ","ГБ","ТБ"]);
    private static string Units(long value,int divisor,string[] units)
    {
        double size = value; var unit = 0;
        while (size >= divisor && unit < units.Length - 1) { size /= divisor; unit++; }
        return size.ToString(size >= 100 ? "0" : "0.#", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) + " " + units[unit];
    }
}
public static class Paths
{
    public static string Normalize(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(p => p is "." or ".." || p.Contains('\0'))) throw new ArgumentException("Недопустимый путь");
        return string.Join('/', parts);
    }
    public static string Parent(string path) { var p = Normalize(path); var i = p.LastIndexOf('/'); return i < 0 ? "" : p[..i]; }
    public static string Combine(string folder, string name) => Normalize(folder + "/" + name);
    public static string LocalPath(string root, string cloudPath)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("Сначала укажите локальную папку аккаунта");
        var fullRoot = System.IO.Path.GetFullPath(root);
        var relative = Normalize(cloudPath);
        if (relative.Split('/').Any(p => p.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || p.Contains(':'))) throw new ArgumentException("Имя файла несовместимо с локальной файловой системой");
        var candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(fullRoot, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidate.StartsWith(fullRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, comparison)) throw new ArgumentException("Выход за пределы локальной папки запрещён");
        return candidate;
    }
}

public sealed record PendingDownload(string AccountId, string Path, string LocalRoot, string? Revision, ExistingFileAction Action);
