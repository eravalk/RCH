using System.Text.RegularExpressions;

namespace RCHub.Core;

public static class LoginDiagnostic
{
    // Reconstruct diagnostic lines from fixed vocabulary. Never copy arbitrary values.
    private static readonly Regex Signals = new(
        @"\b(?:authStart|authInit|authComplete|authFinish|signIn|accountLogin|accountLocked|invalid session|service unavailable|proxyconnect tcp|dial tcp|i/o timeout|certificate signed by unknown authority|invalid username|access denied|invalid response|failed to send SMS code|failed to get trusted phone numbers|invalid auth session state|failed to authenticate|incorrect password|invalid credentials|invalid verification code|unauthorized|forbidden|authenticate|authentication|SRP|challenge|timeout|TLS|certificate|connection refused|no such host|invalid character|unexpected EOF|bad request|invalid_grant|connection reset|two.factor|verification|decrypt|base64|HTTP(?:/\d\.\d)?\s+[45]\d\d|status(?: code)?[:= ]+[45]\d\d)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Report(string raw, string stage, int? exitCode)
    {
        var lines=raw.Split('\n',StringSplitOptions.RemoveEmptyEntries)
            .Select(line=>string.Join(" · ",Signals.Matches(line).Select(m=>m.Value.ToLowerInvariant())))
            .Where(line=>line.Length>0).Distinct().TakeLast(8).ToArray();
        return $"iCloud · {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}\nЭтап: {stage}\n" +
            (exitCode.HasValue ? $"Код завершения rclone: {exitCode}\n" : "") +
            "Диагностика: "+(lines.Length>0?string.Join("\n",lines):"rclone не передал распознанную причину. HTTP-статус не установлен.");
    }
}
