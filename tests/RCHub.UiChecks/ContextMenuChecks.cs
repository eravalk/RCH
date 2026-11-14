using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RCHub.App;
using RCHub.Core;

static class ContextMenuChecks
{
    public static void Run(string root)
    {
        var store=new StateStore(Path.Combine(root,"context"));
        var state=new AppState{LocalRoot=root};state.Settings.FirstRunComplete=true;
        foreach(var id in new[]{"work","home"})state.Accounts.Add(new(){Id=id,Name=id,Provider=ProviderKind.Rclone,AdaptiveRefresh=false,LastCheck=DateTimeOffset.Now});
        foreach(var name in new[]{"alpha.txt","bravo.txt","folder"})state.Entries.Add(new(){AccountId="work",Name=name,Path=name,IsFolder=name=="folder"});
        store.Save(state);
        var view=new MainView(store);var window=new Window{Width=1360,Height=860,Content=view};window.Show();Layout();
        void Layout(){Dispatcher.UIThread.RunJobs();window.Measure(new(1360,860));window.Arrange(new(0,0,1360,860));Dispatcher.UIThread.RunJobs();}
        DataGrid Table()=>window.GetVisualDescendants().OfType<DataGrid>().Single();
        DataGridRow Row(string name)=>Table().GetVisualDescendants().OfType<DataGridRow>().Single(r=>r.DataContext is CloudEntry f&&f.Name==name);
        Point PointAt(Control control)=>control.TranslatePoint(new Point(control.Bounds.Width/2,control.Bounds.Height/2),window)??throw new Exception("Detached target");
        void Mouse(Control target,MouseButton button){var p=PointAt(target);window.MouseMove(p);window.MouseDown(p,button);window.MouseUp(p,button);Layout();}
        ContextMenu OpenMenu()=>window.GetVisualDescendants().OfType<Control>().Where(c=>c.ContextMenu?.IsOpen==true).Select(c=>c.ContextMenu!).Distinct().Single();
        void Right(Control target){Mouse(target,MouseButton.Right);if(!OpenMenu().Items.OfType<MenuItem>().Any())throw new Exception("Context menu has no actions");}
        void Close(){OpenMenu().Close();Layout();}
        void Click(MenuItem item){item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Layout();}
        void AssertSelected(string name){if(Table().SelectedItem is not CloudEntry selected||selected.Name!=name)throw new Exception("Context menu selected the wrong file");}
        for(int i=0;i<4;i++)
        {
            Table().SelectedItem=Table().ItemsSource.Cast<CloudEntry>().First(f=>f.Name=="alpha.txt");
            Right(Row("bravo.txt").GetVisualDescendants().OfType<DataGridCell>().ElementAt(i));AssertSelected("bravo.txt");Close();
        }
        Console.WriteLine("PASS real right mouse input opens context menu in all four file columns and selects the clicked row");
        Right(Row("bravo.txt").GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"Только в облаке")));
        Click(OpenMenu().Items.OfType<MenuItem>().Single(m=>Equals(m.Header,"В избранное")));
        if(store.Load()!.Entries.Single(f=>f.Name=="bravo.txt").IsFavorite!=true || store.Load()!.Entries.Single(f=>f.Name=="alpha.txt").IsFavorite)throw new Exception("Menu action changed another file");
        Console.WriteLine("PASS right click on status and menu action affect only the clicked file");
        Right(Row("folder").GetVisualDescendants().OfType<DataGridCell>().First());Close();
        if(Table().ItemsSource.Cast<CloudEntry>().Count()!=3)throw new Exception("Right click navigated into a folder");
        Console.WriteLine("PASS folder right click does not navigate");
        var dots=Row("bravo.txt").GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"⋯"));Mouse(dots,MouseButton.Left);
        if(!OpenMenu().Items.OfType<MenuItem>().Any(m=>Equals(m.Header,"Убрать из избранного")))throw new Exception("Ellipsis has stale menu actions");Close();
        Console.WriteLine("PASS ellipsis uses the same current file actions");
        Table().SelectedItem=Table().ItemsSource.Cast<CloudEntry>().Single(f=>f.Name=="bravo.txt");window.Activate();if(!Table().Focus())throw new Exception("Could not focus file table");
        window.KeyPressQwerty(PhysicalKey.F10,RawInputModifiers.Shift);window.KeyReleaseQwerty(PhysicalKey.F10,RawInputModifiers.Shift);Layout();
        if(!OpenMenu().Items.OfType<MenuItem>().Any(m=>Equals(m.Header,"Переименовать…")))throw new Exception("Keyboard context menu missing");Close();
        Console.WriteLine("PASS Shift+F10 opens the selected file context menu");
        var home=window.GetVisualDescendants().OfType<Button>().Single(b=>b.Tag is Account a&&a.Id=="home");Right(home);
        Click(OpenMenu().Items.OfType<MenuItem>().Single(m=>Equals(m.Header,"Настройки аккаунта")));
        if(!window.GetVisualDescendants().OfType<TextBox>().Any(t=>t.Text=="home"))throw new Exception("Sidebar menu opened another account");
        window.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"Закрыть")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout();
        if(Table().ItemsSource.Cast<CloudEntry>().Count()!=3)throw new Exception("Sidebar right click switched account");
        Console.WriteLine("PASS sidebar context targets the clicked account without switching catalogs");
        window.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"▦ Сетка")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout();
        var tile=window.GetVisualDescendants().OfType<Border>().Single(b=>b.Tag is CloudEntry f&&f.Name=="bravo.txt");Right(tile);
        if(!OpenMenu().Items.OfType<MenuItem>().Any(m=>Equals(m.Header,"Убрать из избранного")))throw new Exception("Grid menu targets another file");Close();
        Console.WriteLine("PASS grid tile context menu opens with the correct file actions");
        window.Close();
    }
}
