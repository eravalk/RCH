using Microsoft.Data.Sqlite;
using System.Text.Json;
namespace RCHub.Core;

public sealed class StateStore
{
    public string DirectoryPath { get; }
    private readonly string _connectionString;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public StateStore(string? directory = null)
    {
        DirectoryPath = directory ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoyalHub", "RCHub");
        Directory.CreateDirectory(DirectoryPath);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = System.IO.Path.Combine(DirectoryPath, "catalog.db"), Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
        using var c = Open(); using var command = c.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS state (id INTEGER PRIMARY KEY CHECK(id=1), schema_version INTEGER NOT NULL, json TEXT NOT NULL);";
        command.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var c = new SqliteConnection(_connectionString); c.Open(); return c; }
    public AppState? Load()
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT json FROM state WHERE id=1";
        var json = cmd.ExecuteScalar() as string; return json is null ? null : JsonSerializer.Deserialize<AppState>(json);
    }
    public void Save(AppState state)
    {
        using var c = Open(); using var transaction = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = transaction;
        cmd.CommandText = "INSERT INTO state(id,schema_version,json) VALUES(1,1,$json) ON CONFLICT(id) DO UPDATE SET json=excluded.json,schema_version=excluded.schema_version";
        cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(state, Json)); cmd.ExecuteNonQuery(); transaction.Commit();
    }
    public string ExportSettings(Settings settings) => JsonSerializer.Serialize(new SettingsConfig("RC-Hub-settings", 1, settings), Json);
    public Settings ImportSettings(string json)
    {
        var value = JsonSerializer.Deserialize<SettingsConfig>(json) ?? throw new ArgumentException("Пустая конфигурация");
        if (value.Format != "RC-Hub-settings" || value.Version != 1) throw new ArgumentException("Неизвестный формат конфигурации");
        var s = value.Settings ?? throw new ArgumentException("Настройки отсутствуют");
        if (s.Theme is not ("Light" or "Dark" or "System") || !double.IsFinite(s.TextSize) || !double.IsFinite(s.RowHeight) || !double.IsFinite(s.IconSize) || !double.IsFinite(s.ThumbnailSize) || s.TextSize is < 10 or > 24 || s.RowHeight is < 40 or > 100 || s.IconSize is < 16 or > 48 || s.ThumbnailSize is < 80 or > 260 || s.DownloadBytesPerSecond < 0 || s.UploadBytesPerSecond < 0) throw new ArgumentException("Недопустимые настройки");
        return s;
    }
    private sealed record SettingsConfig(string Format, int Version, Settings Settings);
}
public static class RefreshPolicy
{
    public static TimeSpan Interval(Account account, DateTimeOffset now)
    {
        var inactive = now - (account.LastUsed ?? now);
        return inactive.TotalDays switch { >= 90 => TimeSpan.FromDays(30), >= 30 => TimeSpan.FromDays(7), >= 7 => TimeSpan.FromDays(1), _ => TimeSpan.FromHours(1) };
    }
    public static bool Due(Account account, DateTimeOffset now) => account.LastCheck is null || account.AdaptiveRefresh && now - account.LastCheck >= Interval(account, now);
}
