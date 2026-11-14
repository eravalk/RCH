using System.Text.RegularExpressions;
namespace RCHub.Core;

public static class SafeDiagnostic
{
    // Untrusted values are not retained. Only known failure phrases and status codes are copied.
    private static readonly Regex Failures=new(
        @"\b(?:HTTP(?:/\d\.\d)?\s+[45]\d\d|status(?: code)?[:= ]+[45]\d\d|[45]\d\d\s*-\s*[A-Za-z]+Error|GoneError|failed to list|couldn't list|error listing|failed to create file system|couldn't decrypt|invalid base64|illegal base64 data|unsupported protocol scheme|failed to authenticate|authStart|authInit|authComplete|invalid session|invalid_grant|unauthorized|forbidden|permission denied|access denied|timeout|connection refused|connection reset|no such host|certificate|TLS|not supported|doesn't support|not found|invalid credentials|missing icloud trust token|failed to send SMS code|failed to get trusted phone numbers|unexpected EOF)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    public static string Report(string raw,string context,int code)
    {
        var matches=Failures.Matches(raw).Select(m=>m.Value.ToLowerInvariant()).Distinct().Take(16);
        return $"{DateTimeOffset.Now:O} {context} exit={code}\nstderr (обезличено): "+string.Join(" · ",matches.DefaultIfEmpty("нет распознанных признаков"));
    }
    public static string Exception(Exception error)
    {
        var report=Report(error.Message,"exception="+error.GetType().Name,0);
        var frames=new System.Diagnostics.StackTrace(error,false).GetFrames()??[];
        return report+"\n"+string.Join("\n",frames.Take(12).Select(f=>f.GetMethod()).Where(m=>m!=null).Select(m=>m!.DeclaringType?.FullName+"."+m.Name));
    }
}
