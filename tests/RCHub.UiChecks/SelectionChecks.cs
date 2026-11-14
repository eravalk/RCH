using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RCHub.App;
using RCHub.Core;

static class SelectionChecks
{
    public static void Run(string root)
    {
        var store=new StateStore(Path.Combine(root,"selection"));
        var state=new AppState{LocalRoot=root};state.Settings.FirstRunComplete=true;
        state.Accounts.Add(new(){Id="work",Name="work",Provider=ProviderKind.Rclone,AdaptiveRefresh=false,LastCheck=DateTimeOffset.Now});
        state.Entries.Add(new(){AccountId="work",Name="folder",Path="folder",IsFolder=true});
        state.Entries.Add(new(){AccountId="work",Name="child.txt",Path="folder/child.txt"});
        for(int i=0;i<100;i++)state.Entries.Add(new(){AccountId="work",Name=$"file{i:000}.txt",Path=$"file{i:000}.txt"});
        store.Save(state);
        var window=new Window{Width=1360,Height=860,Content=new MainView(store)};window.Show();Layout();
        void Layout(){Dispatcher.UIThread.RunJobs();window.Measure(new(1360,860));window.Arrange(new(0,0,1360,860));Dispatcher.UIThread.RunJobs();}
        DataGrid Table()=>window.GetVisualDescendants().OfType<DataGrid>().Single();
        DataGridRow Row(string name)=>Table().GetVisualDescendants().OfType<DataGridRow>().Single(r=>r.DataContext is CloudEntry f&&f.Name==name);
        void Mouse(Control target,bool twice=false)
        {
            // Separate independent single clicks from the preceding double-click gesture.
            Thread.Sleep(550);
            var p=target.TranslatePoint(new Point(target.Bounds.Width/2,target.Bounds.Height/2),window)??throw new Exception("Detached target");
            window.MouseMove(p);window.MouseDown(p,MouseButton.Left);window.MouseUp(p,MouseButton.Left);
            if(twice){window.MouseDown(p,MouseButton.Left);window.MouseUp(p,MouseButton.Left);}Layout();
        }
        void Selected(string name){if(Table().SelectedItem is not CloudEntry f||f.Name!=name)throw new Exception("Selection lost: "+name);}
        foreach(var name in new[]{"folder","file000.txt"})
        {
            for(int i=0;i<4;i++)
            {
                Mouse(Row(name).GetVisualDescendants().OfType<DataGridCell>().ElementAt(i));Selected(name);
                if(Table().ItemsSource.Cast<CloudEntry>().Count()!=101)throw new Exception("Single click navigated");
            }
            Mouse(Row(name).GetVisualDescendants().OfType<TextBlock>().First(t=>t.Text==name));Selected(name);
        }
        Console.WriteLine("PASS single clicks in name, type, date and size select files and folders without navigation");
        var table=Table();var item=table.ItemsSource.Cast<CloudEntry>().Single(f=>f.Name=="file060.txt");table.ScrollIntoView(item,null);Layout();
        var scroll=table.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>().Single(s=>s.Orientation==Avalonia.Layout.Orientation.Vertical);
        var before=scroll.Value;
        if(before<=0)throw new Exception("Scroll test never scrolled");
        Mouse(Row(item.Name).GetVisualDescendants().OfType<TextBlock>().First(t=>t.Text==item.Name));Selected(item.Name);
        if(!ReferenceEquals(table,Table())||scroll.Value!=before)throw new Exception("Single click rebuilt or scrolled the list");
        Mouse(Row(item.Name).GetVisualDescendants().OfType<TextBlock>().First(t=>t.Text==item.Name),true);Selected(item.Name);
        if(!ReferenceEquals(table,Table())||scroll.Value!=before)throw new Exception("File preview rebuilt or scrolled the list");
        if(!window.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="Предпросмотр"))throw new Exception("Double click did not open preview");
        Console.WriteLine("PASS scrolled file selection and double-click preview retain the table, offset and selected file");
        table.ScrollIntoView(table.ItemsSource.Cast<CloudEntry>().First(),null);Layout();
        Mouse(Row("folder").GetVisualDescendants().OfType<DataGridCell>().ElementAt(2),true);
        if(Table().ItemsSource.Cast<CloudEntry>().Single().Name!="child.txt")throw new Exception("Folder double click failed");
        Console.WriteLine("PASS folder opens on double click in a non-name column");
        window.Close();

        state.Settings.View="Grid";store.Save(state);
        window=new Window{Width=1360,Height=860,Content=new MainView(store)};window.Show();Layout();
        Border Tile()=>window.GetVisualDescendants().OfType<Border>().Single(b=>b.Tag is CloudEntry f&&f.Name=="folder");
        var tile=Tile();Mouse(tile);
        if(!ReferenceEquals(tile,Tile())||!window.GetVisualDescendants().OfType<Border>().Any(b=>b.Tag is CloudEntry f&&f.Name=="file000.txt"))throw new Exception("Grid single click navigated");
        Mouse(tile,true);
        if(!window.GetVisualDescendants().OfType<Border>().Any(b=>b.Tag is CloudEntry f&&f.Name=="child.txt"))throw new Exception("Grid double click did not navigate");
        Console.WriteLine("PASS grid single click selects and double click opens folders");window.Close();
    }
}
