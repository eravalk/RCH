using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using RCHub.App;

namespace RCHub.Desktop;
internal static class Program
{
    [STAThread] public static void Main(string[] args)
    {
        using var instance = new Mutex(true, "RoyalHub.RCHub.Desktop", out var created);
        if (!created) return;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<DesktopApp>().UsePlatformDetect().WithInterFont().LogToTrace();
}
public sealed class DesktopApp : RCHub.App.AppBase
{
    private bool _exiting;
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if(desktop.Args?.Contains("--check-native-host")==true)
            {
                var probe=new NativeControlHost();
                var test=new Window{Width=320,Height=200,Content=probe};
                desktop.MainWindow=test;
                test.Opened+=(_,_)=>Avalonia.Threading.Dispatcher.UIThread.Post(()=>
                {
                    // NativeControlHost creates its child HWND during attachment/layout.
                    // Run in the real Windows executable so its embedded manifest is used.
                    test.UpdateLayout();
                    desktop.Shutdown(0);
                },Avalonia.Threading.DispatcherPriority.Background);
                base.OnFrameworkInitializationCompleted();return;
            }
            var view=new MainView();
            var window=new Window { Title="RCH",Icon=LogoRaster.WindowMark(), Width=1360,Height=860,MinWidth=960,MinHeight=640,Content=view,Background=Brushes.White };
            desktop.MainWindow=window;
            var menu=new NativeMenu();
            var open=new NativeMenuItem("Открыть RCH");open.Click+=(_,_)=>{window.Show();window.Activate();};menu.Items.Add(open);
            var journal=new NativeMenuItem("Журнал отладки");journal.Click+=(_,_)=>view.ShowDebug();menu.Items.Add(journal);
            var quit=new NativeMenuItem("Выйти");quit.Click+=(_,_)=>{_exiting=true;view.Save();desktop.Shutdown();};menu.Items.Add(quit);
            var tray=new TrayIcon { ToolTipText="RCH",Menu=menu };
            tray.Icon=LogoRaster.WindowMark();
            tray.Clicked+=(_,_)=>{window.Show();window.Activate();};
            TrayIcon.SetIcons(this,new TrayIcons {tray});
            window.Closing+=(_,e)=>{view.Save();if(!_exiting){e.Cancel=true;window.Hide();}};
            desktop.Exit+=(_,_)=>{view.Save();tray.Dispose();};
        }
        base.OnFrameworkInitializationCompleted();
    }
}
