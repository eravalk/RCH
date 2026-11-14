using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RCHub.App;
using RCHub.Core;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
AppBuilder.Configure<AppBase>().WithInterFont().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).SetupWithoutStarting();
foreach(var icon in FileIcons.All)FileSymbol.Create(new CloudEntry{Name="sample."+icon.Extensions.FirstOrDefault(),IsFolder=icon.Key=="folder"},32);
Console.WriteLine("PASS all 24 file icon geometries load");
var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
var store=new StateStore(root);
var view=new MainView(store);var window=new Window{Width=1360,Height=860,Content=view};window.Show();
Dispatcher.UIThread.RunJobs();
var next=window.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"Продолжить"));
if(next.IsEnabled || window.GetVisualDescendants().OfType<Button>().Any(b=>Equals(b.Content,"Закрыть")))throw new Exception("First launch can bypass folder selection");
var field=window.GetVisualDescendants().OfType<TextBox>().Single();field.Text=root;Dispatcher.UIThread.RunJobs();
next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Dispatcher.UIThread.RunJobs();
if(store.Load()?.LocalRoot!=root)throw new Exception("Local root not saved");
Console.WriteLine("PASS mandatory first-launch folder");
window.Measure(new Size(1360,860));window.Arrange(new Rect(0,0,1360,860));
using var image=new RenderTargetBitmap(new PixelSize(1360,860));image.Render(window);image.Save(args.Length>0?args[0]:"ui-check.png");
Console.WriteLine("PASS native window constructed, laid out and rendered");
window.Close();

var state = new AppState{LocalRoot=root};
state.Settings.FirstRunComplete=true;
foreach (var id in new[]{"work","home"})
{
    state.Accounts.Add(new Account{Id=id,Name=id,Provider=ProviderKind.Rclone,AdaptiveRefresh=false,LastCheck=DateTimeOffset.UtcNow});
    state.Entries.Add(new CloudEntry{AccountId=id,Name=id+".txt",Path=id+".txt"});
}
store.Save(state);
var secondWindow=new Window{Width=1360,Height=860,Content=new MainView(store)};
secondWindow.Show();
Dispatcher.UIThread.RunJobs();
foreach(var id in new[]{"home","work"})
{
    var button=secondWindow.GetVisualDescendants().OfType<Button>().Single(b=>b.Tag is Account account && account.Id==id);
    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    var rows=secondWindow.GetVisualDescendants().OfType<DataGrid>().Single().ItemsSource.Cast<CloudEntry>().ToList();
    if(rows.Count!=1 || rows[0].AccountId!=id)throw new Exception("Incorrect files after account switch");
}
ContextMenuChecks.Run(root);
SelectionChecks.Run(root);
CloudPreviewChecks.Run(root);
var main=(MainView)secondWindow.Content!;
main.ShowDebug();
Dispatcher.UIThread.RunJobs();
if(!Directory.EnumerateFiles(Path.Combine(root,"logs"),"RCH-*.log").Any(f=>File.ReadAllText(f).Contains("запуск")))throw new Exception("Separate debug log missing");
Console.WriteLine("PASS debug window opens and startup is written to a separate log");
secondWindow.Close();
if(args.Length>1)
{
    var sample=DemoData.Create();
    sample.Settings.Theme="Dark";sample.LocalRoot=root;sample.Settings.FirstRunComplete=true;sample.Settings.RowHeight=47;
    foreach(var account in sample.Accounts){account.Provider=ProviderKind.Rclone;account.AdaptiveRefresh=false;account.LastCheck=DateTimeOffset.UtcNow;}
    sample.Accounts[0].LocalFolder=root;
    using(var zip=System.IO.Compression.ZipFile.Open(Path.Combine(root,"Бюджет.xlsx"),System.IO.Compression.ZipArchiveMode.Create))
    {
        using var output=new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open());
        output.Write("<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData>");
        var cells=new[]{new[]{"Статья","План","Факт"},new[]{"Оборудование","1 200","1 150"},new[]{"Материалы","800","780"},new[]{"Работы","500","530"},new[]{"Итого","2 500","2 460"}};
        for(int i=0;i<cells.Length;i++){
            output.Write("<row>");for(int j=0;j<3;j++)output.Write($"<c r='{(char)('A'+j)}{i+1}' t='inlineStr'><is><t>{cells[i][j]}</t></is></c>");output.Write("</row>");
        }
        output.Write("</sheetData></worksheet>");
    }
    store.Save(sample);
    var screen=new Window{Width=1500,Height=900,Content=new MainView(store)};
    screen.Show();Dispatcher.UIThread.RunJobs();
    var button=screen.GetVisualDescendants().OfType<Button>().First(b=>b.Content?.ToString()?.Contains("◧ Просмотр")==true);
    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    var table=screen.GetVisualDescendants().OfType<DataGrid>().Single();
    table.SelectedItem=table.ItemsSource.Cast<CloudEntry>().First(f=>f.Name=="Бюджет.xlsx");
    screen.Measure(new Size(1500,900));screen.Arrange(new Rect(0,0,1500,900));Dispatcher.UIThread.RunJobs();
    using var shot=new RenderTargetBitmap(new PixelSize(1500,900));shot.Render(screen);shot.Save(args[1]);screen.Close();
}

Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
Directory.Delete(root,true);
Console.WriteLine("PASS sidebar switches between account catalogs");
