using Microsoft.Win32;
namespace RCHub.App;
public static class WindowsOptions
{
    public static void ApplyAutoStart(bool enabled)
    {
        if(!OperatingSystem.IsWindows())return;
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if(enabled){var executable=Environment.ProcessPath??throw new IOException("Не найден путь приложения");key.SetValue("RoyalHub.RCH","\""+executable+"\"");}
        else key.DeleteValue("RoyalHub.RCH",false);
    }
}
