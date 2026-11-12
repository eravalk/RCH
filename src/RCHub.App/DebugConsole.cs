using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;
using RCHub.Core;

namespace RCHub.App;
public sealed partial class MainView
{
    private static readonly HttpClient ReleasesClient=new(){Timeout=TimeSpan.FromSeconds(12),MaxResponseContentBufferSize=2_000_000};
    private bool checking;
    private Window? debugWindow;
    private TextBox? debugText;
    private readonly Queue<string> lines=new();
    private string LogDir=>Path.Combine(_store.DirectoryPath,"logs");
    private string LogPath=>Path.Combine(LogDir,$"RCH-{DateTime.Now:yyyy-MM-dd}.log");
    private readonly StackPanel updateBar=new(){Orientation=Orientation.Horizontal,Spacing=10,IsVisible=false,Margin=new Thickness(12,4)};
    // Callers supply event names, stages and classified diagnostics only, never remote paths or command lines.
    private void LogEvent(string value)
    {
        var line=$"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}  {value}";
        try {Directory.CreateDirectory(LogDir);File.AppendAllText(LogPath,line+Environment.NewLine);}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){line+=" [не удалось записать файл]";}
        lines.Enqueue(line);while(lines.Count>500)lines.Dequeue();
        if(debugText!=null){debugText.Text=string.Join(Environment.NewLine,lines);debugText.CaretIndex=debugText.Text.Length;}
    }
    private void AttachRoot()
    {
        if(updateBar.Parent is DockPanel old)old.Children.Remove(updateBar);
        var dock=new DockPanel();
        DockPanel.SetDock(updateBar,Dock.Top);dock.Children.Add(updateBar);dock.Children.Add(_root);Content=dock;
    }
    public void ShowDebug()
    {
        if(debugWindow!=null){debugWindow.Activate();return;}
        var output=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,
            FontFamily=new FontFamily("Consolas"),Text=string.Join(Environment.NewLine,lines)};
        debugText=output;
        var controls=Row(AsyncBtn("Проверить обновления",()=>CheckUpdates(true)),Btn("Папка журналов",()=>{
            Directory.CreateDirectory(LogDir);Process.Start(new ProcessStartInfo(LogDir){UseShellExecute=true});}),
            AsyncBtn("Скопировать",async()=>{if(debugWindow?.Clipboard is {} clipboard)await clipboard.SetTextAsync(output.Text);}));
        var grid=new Grid{RowDefinitions=new RowDefinitions("Auto,*,Auto"),Margin=new Thickness(14),RowSpacing=8};
        grid.Children.Add(controls);Grid.SetRow(output,1);grid.Children.Add(output);
        var path=Text(LogPath,11,Muted);Grid.SetRow(path,2);grid.Children.Add(path);
        var window=new Window{Title="RCH · Журнал отладки",Icon=LogoRaster.WindowMark(),Width=820,Height=500,Content=grid};
        debugWindow=window;window.Closed+=(_,_)=>{debugWindow=null;debugText=null;};window.Show();
    }
    private async Task CheckUpdates(bool manual)
    {
        if(checking)return;checking=true;
        LogEvent("GitHub Releases · проверка · установлена "+ReleaseCheck.DisplayVersion);
        try{
            var result=await ReleaseCheck.CheckAsync(ReleasesClient,ReleaseCheck.Installed);
            LogEvent(result.Message);
            if(result.Newer && result.Download is string link){
                updateBar.Children.Clear();updateBar.Children.Add(Text(result.Message,14,Ink,true));
                updateBar.Children.Add(Btn(link.EndsWith(".zip")?"Скачать обновление":"Открыть релиз",()=>LaunchUrl(link),true));
                updateBar.Children.Add(Btn("Позже",()=>updateBar.IsVisible=false));updateBar.IsVisible=true;
            }else if(manual)Notify(result.Message);
        }catch(Exception e) when(e is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException){
            var message=e is HttpRequestException { StatusCode: {} code }?$"GitHub Releases · HTTP {(int)code}":"Не удалось проверить релизы GitHub.";
            LogEvent(message);if(manual)Notify(message);
        }finally{checking=false;}
    }
}
