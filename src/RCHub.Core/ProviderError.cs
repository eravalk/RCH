using System.Text.RegularExpressions;
namespace RCHub.Core;

public static class ProviderError
{
    public static string Describe(string service, string operation, string raw, int exitCode)
    {
        var status = Regex.Match(raw, @"(?i)(?:\[|HTTP(?:/\d\.\d)?\s+|status(?:\s+code)?[=: ]+)([45]\d\d)\b");
        var code = status.Groups[1].Value;
        var reason = code switch
        {
            "410" => "Сервис сообщает, что ресурс больше недоступен. Проверьте, открывается ли диск в браузере под этим аккаунтом.",
            "401" => "Сессия отклонена. Войдите в аккаунт повторно.",
            "403" => "Сервис запретил доступ. Проверьте разрешения приложения в аккаунте.",
            "429" => "Слишком много запросов. Повторите позже.",
            _ when raw.Contains("invalid_client",StringComparison.OrdinalIgnoreCase) => "Сервис отклонил OAuth-клиент приложения (invalid_client).",
            _ when raw.Contains("invalid_grant",StringComparison.OrdinalIgnoreCase) => "Код входа или сессия недействительны (invalid_grant). Повторите вход.",
            _ when raw.Contains("access_denied",StringComparison.OrdinalIgnoreCase) => "Разрешение на доступ отклонено (access_denied).",
            _ when raw.Contains("failed to send SMS",StringComparison.OrdinalIgnoreCase) => "Apple отклонила запрос отправки SMS.",
            _ when raw.Contains("trusted phone numbers",StringComparison.OrdinalIgnoreCase) => "Apple не вернула список доверенных номеров.",
            _ when raw.Contains("session state",StringComparison.OrdinalIgnoreCase) => "Сессия подтверждения Apple недействительна. Начните вход заново.",
            _ when raw.Contains("invalid verification",StringComparison.OrdinalIgnoreCase) => "Apple отклонила код подтверждения.",
            _ when raw.Contains("invalid username",StringComparison.OrdinalIgnoreCase) || raw.Contains("invalid password",StringComparison.OrdinalIgnoreCase) => "Сервис отклонил логин или пароль.",
            _ when raw.Contains("no such host",StringComparison.OrdinalIgnoreCase) => "Не удалось определить адрес сервера.",
            _ when raw.Contains("timeout",StringComparison.OrdinalIgnoreCase) => "Истекло время ожидания ответа.",
            _ => "Сервис не завершил операцию."
        };
        var tag = code.Length>0 ? "HTTP "+code : "код "+exitCode;
        return $"{service} · {operation} ({tag}). {reason}";
    }
}
