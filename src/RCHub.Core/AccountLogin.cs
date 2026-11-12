using System.Diagnostics;
using System.Text.Json;

namespace RCHub.Core;

public sealed record LoginQuestion(string Key, string Help, string DefaultValue, bool Secret,
    bool Required, bool Exclusive, IReadOnlyList<string> Choices);

public static class AccountLogin
{
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    {
        "host","user","pass","port","domain","key_file","key_file_pass","apple_id","password","2fa",
        "provider","region","endpoint","access_key_id","secret_access_key"
    };

    public static async Task ConfigureAsync(string executable, string config, string remote, string backend,
        Func<LoginQuestion,CancellationToken,Task<string>> ask, CancellationToken ct, bool ownYandexClient=false, Action<string>? trace=null)
    {
        string[] args = ["config","create",remote,backend,"--all","--non-interactive"];
        string? answer = null;
        var stage="Начало настройки";
        IOException Failure(string raw,int code)
        {
            if(backend!="iclouddrive")return new IOException(ProviderError.Describe(backend,"Вход",raw,code));
            var report=LoginDiagnostic.Report(raw,stage,code);
            try
            {
                var path=Path.Combine(Path.GetDirectoryName(config)!,"diagnostics");
                Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path,"icloud-last.txt"),report);
            }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
            return new IOException(report);
        }
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var start = RcloneProvider.Command(executable,config,args);
            start.CreateNoWindow = true;
            start.StandardErrorEncoding = start.StandardOutputEncoding = System.Text.Encoding.UTF8;
            start.RedirectStandardInput = start.RedirectStandardOutput = start.RedirectStandardError = true;
            if (answer != null) start.Environment["RCLONE_RESULT"] = answer;
            Process Launch()
            {
                try { return Process.Start(start) ?? throw new IOException("Не удалось запустить подключение."); }
                catch(System.ComponentModel.Win32Exception e)
                {
                    trace?.Invoke("Запуск rclone · код Windows "+e.NativeErrorCode);
                    throw;
                }
            }
            trace?.Invoke("rclone · "+backend+" · "+stage);
            using var process = Launch();
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(ct); }
            finally
            {
                if (!process.HasExited) {process.Kill(true); await process.WaitForExitAsync();}
                await errors;
            }
            var response = await output;
            trace?.Invoke("rclone · "+backend+" · код завершения "+process.ExitCode);
            if (process.ExitCode != 0) throw Failure(await errors,process.ExitCode);
            using var json = JsonDocument.Parse(response);
            var root = json.RootElement;
            var state = root.GetProperty("State").GetString();
            if (root.TryGetProperty("Error",out var err) && !string.IsNullOrEmpty(err.GetString()) && (!root.TryGetProperty("Option",out var retry) || retry.ValueKind != JsonValueKind.Object))
                throw Failure(err.GetString()!,0);
            if (string.IsNullOrEmpty(state)) return;
            var option = root.GetProperty("Option");
            var key = option.GetProperty("Name").GetString()!;
            var fallback = option.GetProperty("DefaultStr").GetString() ?? "";
            var required = option.GetProperty("Required").GetBoolean();
            if(backend=="iclouddrive")stage=key switch
            {
                "apple_id"=>"Ввод Apple Account","password"=>"После ввода пароля, до подтверждения",
                "config_2fa"=>"Подтверждение второго фактора","config_2fa_phone"=>"Выбор доверенного номера",
                "config_2fa_sms"=>"Проверка SMS-кода",_=>stage
            };
            answer = key switch
            {
                "config_shared_client_id" or "config_is_local" => "true",
                "config_change_team_drive" or "config_fs_advanced" => "false",
                "client_id" or "client_secret" when backend=="yandex" && ownYandexClient => null,
                "scope" when backend=="drive" => "drive",
                _ when state.StartsWith("*all-set",StringComparison.Ordinal) && !Fields.Contains(key) &&
                    (!required || fallback.Length>0) => fallback,
                _ => null
            };
            if (answer == null)
            {
                var choices = option.TryGetProperty("Examples",out var examples) && examples.ValueKind==JsonValueKind.Array
                    ? examples.EnumerateArray().Select(x=>x.GetProperty("Value").GetString()!).ToArray() : [];
                answer = await ask(new LoginQuestion(key,option.GetProperty("Help").GetString() ?? key,
                    fallback,option.GetProperty("IsPassword").GetBoolean() || key=="client_secret" || key is "config_2fa" or "config_2fa_sms" or "secret_access_key" or "2fa" or "cookies" or "trust_token",
                    required || (ownYandexClient && backend=="yandex" && key is ("client_id" or "client_secret")), option.GetProperty("Exclusive").GetBoolean(),choices),ct);
            }
            // The --all option editor persists this continuation result verbatim.
            // Backend passwords use rclone's config encoding. Verification codes stay literal.
            if(state.StartsWith("*all-set",StringComparison.Ordinal) && option.GetProperty("IsPassword").GetBoolean() && !string.IsNullOrEmpty(answer))
            {
                var encode=RcloneProvider.Command(executable,config,"obscure","-");
                encode.CreateNoWindow=true;
                encode.RedirectStandardInput=encode.RedirectStandardOutput=encode.RedirectStandardError=true;
                encode.StandardInputEncoding=new System.Text.UTF8Encoding(false);
                using var child=Process.Start(encode) ?? throw new IOException("Не удалось подготовить пароль.");
                using var kill=ct.Register(()=>{try{if(!child.HasExited)child.Kill(true);}catch(InvalidOperationException){}});
                var result=child.StandardOutput.ReadToEndAsync();
                var discard=child.StandardError.BaseStream.CopyToAsync(Stream.Null);
                await child.StandardInput.WriteLineAsync(answer);
                child.StandardInput.Close();
                await child.WaitForExitAsync(ct);await discard;
                if(child.ExitCode!=0)throw new IOException("Не удалось подготовить пароль.");
                answer=(await result).TrimEnd();
            }
            args = ["config","update",remote,"--continue","--state",state,"--non-interactive"];
        }
    }
}
