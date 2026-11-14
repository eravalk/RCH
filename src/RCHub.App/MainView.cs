using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RCHub.Core;

namespace RCHub.App;

public sealed partial class MainView : UserControl
{
    private readonly StateStore _store;
    private AppState _state;
    private readonly List<TabState> _tabs=[];
    private int _active;
    private List<TabState> _previous;
    private CloudEntry? _selected;
    private string _query="",_kind="Все",_statusFilter="Все состояния";
    private bool _wholeAccount,_preview,_queueOpen=true;
    private readonly HashSet<string> _expanded=[];
    private readonly HashSet<string> _collapsedGroups=[];
    private readonly Dictionary<string,string> _sessionSecrets=[];
    private readonly Dictionary<string,Bitmap> _bitmaps=[];
    private Grid _root=new();
    private Grid _fileArea=new();
    private StackPanel _details=new();
    private StackPanel _queuePanel=new();
    private Border? _modal;
    private TextBlock _message=new();
    private readonly HashSet<(string AccountId, string Path)> _downloading = [];
    private readonly HashSet<string> _savingLocally = [];
    private readonly ObservableCollection<Job> _jobs=[];
    private object? _dragObject;
    private Point _dragStart;
    private Border? _dragGhost;
    private bool _dragging;
    private int _modalGeneration;
    private IBrush Surface => B(IsDark?"#1D2633":"#F5F7FB");
    private IBrush Paper => B(IsDark?"#252F3D":"#FFFFFF");
    private IBrush Ink => B(IsDark?"#E4ECF7":"#243246");
    private IBrush Muted => B(IsDark?"#9BAAC1":"#78869A");
    private IBrush Line => B(IsDark?"#394659":"#E5EAF2");
    private bool IsDark => (_state.Settings.Theme=="Dark" || (_state.Settings.Theme=="System" && Application.Current?.ActualThemeVariant==ThemeVariant.Dark));
    private TabState Current => _tabs[Math.Clamp(_active,0,_tabs.Count-1)];
    private Account CurrentAccount => _state.Accounts.First(a=>a.Id==Current.AccountId);
    private static IBrush B(string color)=>Brush.Parse(color);
    public MainView() : this(new StateStore()) { }
    public MainView(StateStore store)
    {
        _store=store;_state=_store.Load()??new AppState();
        var demos = _state.Accounts.Where(a => a.IsDemo).Select(a => a.Id).ToHashSet();
        _state.Accounts.RemoveAll(a => a.IsDemo);
        _state.Entries.RemoveAll(f => demos.Contains(f.AccountId));
        _previous=_state.Tabs.Where(t=>_state.Accounts.Any(a=>a.Id==t.AccountId)).ToList();
        if (_state.Accounts.Count > 0) _tabs.Add(new(){AccountId=_state.Accounts[0].Id});
        Render();
        AddHandler(PointerMovedEvent,DragMoved,RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent,DragReleased,RoutingStrategies.Tunnel);
        Loaded += OnFirstLoad;
    }
    private async void OnFirstLoad(object? sender, RoutedEventArgs args)
    {
        Loaded -= OnFirstLoad;
        LogEvent("RCH "+ReleaseCheck.DisplayVersion+" · запуск");
        _ = CheckUpdates(false);
        if (string.IsNullOrWhiteSpace(_state.LocalRoot))
        {
            Welcome();
            return;
        }

        await RefreshDueAccounts();
    }
    private async Task RefreshDueAccounts()
    {
        foreach (var account in _state.Accounts.ToArray())
        {
            if (account.IsDemo || !RefreshPolicy.Due(account, DateTimeOffset.Now)) continue;
            try
            {
                await Refresh(account);
            }
            catch (Exception error)
            {
                LogEvent(ProviderError.Describe(account.Provider.ToString(),"Индексация",error.Message,1));
                _message.Text=account.Title + " · Не удалось обновить каталог. " + error.Message;
            }
        }
    }

    public void Save()
    {
        _state.Tabs=_previous.Count>0?_previous:_tabs.ToList();
        _store.Save(_state);
    }
    private TextBlock Text(string text,double size=12,IBrush? color=null,bool bold=false)=>new(){Text=text,FontSize=size,Foreground=color??Ink,FontWeight=bold?FontWeight.SemiBold:FontWeight.Normal,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
    private static StackPanel Stack(params Control[] controls){var p=new StackPanel{Spacing=8};foreach(var c in controls)p.Children.Add(c);return p;}
    private static StackPanel Row(params Control[] controls){var p=new StackPanel{Orientation=Orientation.Horizontal,Spacing=7,VerticalAlignment=VerticalAlignment.Center};foreach(var c in controls)p.Children.Add(c);return p;}
    private Button Btn(string title,Action action,bool primary=false,string? tip=null)
    {
        var b=new Button{Content=title,VerticalAlignment=VerticalAlignment.Center};if(primary)b.Classes.Add("primary");b.Click+=(_,_)=>{try{action();}catch(Exception e){LogEvent(SafeDiagnostic.Exception(e));Notify(e.Message);}};if(tip is not null)ToolTip.SetTip(b,tip);return b;
    }
    private Button AsyncBtn(string title,Func<Task> action,bool primary=false)=>Btn(title,()=>_ = Run(action),primary);
    private async Task Run(Func<Task> action)
    {
        try{await action();}catch(OperationCanceledException){Notify("Операция приостановлена");}catch(Exception e){LogEvent(SafeDiagnostic.Exception(e));Notify(e.Message);}
    }
    private TextBlock? _modalMessage;
    private void Notify(string message){_message.Text=message;if(_modalMessage!=null)_modalMessage.Text=message;}
    private Border Card(Control child,Thickness? padding=null,IBrush? bg=null)=>new(){Child=child,Padding=padding??new Thickness(12),Background=bg??Surface,CornerRadius=new CornerRadius(6)};
    private void Render()
    {
        var s=_state.Settings;
        if(Application.Current is {} app)app.RequestedThemeVariant=s.Theme switch{"Dark"=>ThemeVariant.Dark,"System"=>ThemeVariant.Default,_=>ThemeVariant.Light};
        FontSize=s.TextSize;Background=Paper;
        _root=new Grid{ColumnDefinitions=new ColumnDefinitions("244,*"),Background=Paper};
        if(string.IsNullOrWhiteSpace(_state.LocalRoot)){AttachRoot();return;}
        if (_state.Accounts.Count == 0)
        {
            _message = Text("",12,Muted);
            var welcome = Stack(Text("RCH",28,Ink,true), Text("Подключите первое хранилище",18),
                Btn("Подключить диск",Connect,true), Btn("Развернуть свой сервер",Guide),_message);
            welcome.HorizontalAlignment=HorizontalAlignment.Center;
            welcome.VerticalAlignment=VerticalAlignment.Center;
            Grid.SetColumnSpan(welcome,2);
            _root.Children.Add(welcome);
            AttachRoot();
            return;
        }
        _root.Children.Add(Sidebar());
        var main=new Grid{RowDefinitions=new RowDefinitions("Auto,Auto,*,Auto,Auto"),Background=Paper};Grid.SetColumn(main,1);_root.Children.Add(main);
        main.Children.Add(Tabs());var toolbar=Toolbar();Grid.SetRow(toolbar,1);main.Children.Add(toolbar);
        _fileArea=new Grid{ColumnDefinitions=new ColumnDefinitions(_preview?"*,290":"*")};Grid.SetRow(_fileArea,2);main.Children.Add(_fileArea);RenderFiles();
        var queue=new Border{Background=Surface,BorderBrush=Line,BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(22,7)};
        _queuePanel=new StackPanel{Spacing=5};queue.Child=_queuePanel;Grid.SetRow(queue,3);main.Children.Add(queue);RenderQueue();
        _message=Text(CurrentAccount.IsDemo?"ДЕМО-АККАУНТ · Реальные облака не затрагиваются":"Локальный каталог · "+CurrentAccount.CheckLabel,10,Muted);
        var foot=new Border{BorderBrush=Line,BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(22,8),Child=_message};Grid.SetRow(foot,4);main.Children.Add(foot);
        AttachRoot();
    }
    private Control Sidebar()
    {
        var side=new Grid{RowDefinitions=new RowDefinitions("Auto,*,Auto"),Background=new LinearGradientBrush{StartPoint=new RelativePoint(0,0,RelativeUnit.Relative),EndPoint=new RelativePoint(1,1,RelativeUnit.Relative),GradientStops=new GradientStops{new GradientStop(Color.Parse("#0B142F"),0),new GradientStop(Color.Parse("#142952"),1)}}};
        var brand=Row(LogoRaster.Create(44),Text("RCH",24,Brushes.White,true));brand.Margin=new Thickness(20,23,15,23);side.Children.Add(brand);
        var links=new StackPanel{Spacing=3,Margin=new Thickness(12,0)};
        foreach(var (label,page) in new[]{("▤  Все файлы","all"),("☆  Избранное","favorites"),("♲  Корзина","trash")}){var b=Btn(label,()=>{Current.Page=page;_previous.Clear();_selected=null;Render();Save();});b.Classes.Add("nav");b.HorizontalAlignment=HorizontalAlignment.Stretch;links.Children.Add(b);}
        links.Children.Add(Text("ХРАНИЛИЩА",10,B("#91A2BB")));
        foreach(var group in _state.Groups.Concat(_state.Accounts.Select(a=>a.Group)).Distinct())
        {
            var accounts=_state.Accounts.Where(a=>a.Group==group).ToList();if(accounts.Count==0)continue;
            var heading=Btn((_collapsedGroups.Contains(group)?"▸ ":"▾ ")+group,()=>{if(!_collapsedGroups.Add(group))_collapsedGroups.Remove(group);Render();});heading.Classes.Add("nav");heading.FontSize=10;links.Children.Add(heading);
            if(_collapsedGroups.Contains(group))continue;
            foreach(var a in accounts)
            {
                var icon=AccountIcon(a,30);var name=Text(a.Name==a.Service?a.Name:a.Service+" · "+a.Name,13,Brushes.White,true);var title=Row(icon,name);var p=new StackPanel{Spacing=5};p.Children.Add(title);
                var usageBar=new ProgressBar{Minimum=0,Maximum=100,Value=a.UsedPercent,Height=3,Foreground=B(a.Accent),Background=B("#41516A"),Margin=new Thickness(32,4,0,0)};usageBar.IsVisible=a.QuotaKnown;p.Children.Add(usageBar);
                var cap=Text(a.CapacityLabel,10,B("#B7C6DC"));ToolTip.SetTip(cap,a.QuotaDescription);cap.Margin=new Thickness(32,0,0,0);p.Children.Add(cap);
                var check=Text(a.AdaptiveRefresh?a.CheckLabel:"Вручную · "+a.CheckLabel,9,B("#8FA3BF"));check.Margin=new Thickness(32,0,0,0);p.Children.Add(check);
                var button=Btn("",()=>Navigate(a.Id,""));button.Content=p;button.Classes.Add("nav");button.HorizontalAlignment=HorizontalAlignment.Stretch;button.Tag=a;
                if(Current.AccountId==a.Id&&Current.Page=="files")button.Background=B("#204A92");
                button.PointerPressed+=(_,e)=>StartDrag(a,e);button.ContextMenu=new ContextMenu{ItemsSource=new[]{Menu("Настройки аккаунта",()=>AccountSettings(a)),Menu("Отключить…",()=>Disconnect(a))}};
                links.Children.Add(button);
            }
        }
        side.Children.Add(new ScrollViewer{Content=links});Grid.SetRow(side.Children[^1],1);
        var bottom=Stack(Btn("＋  Подключить диск",Connect),Btn("▧  Развернуть сервер",Guide),Btn("Журнал отладки",ShowDebug),Btn("⚙  Настройки",SettingsDialog));bottom.Margin=new Thickness(12,12);foreach(var c in bottom.Children.OfType<Button>()){c.Classes.Add("nav");c.HorizontalAlignment=HorizontalAlignment.Stretch;}Grid.SetRow(bottom,2);side.Children.Add(bottom);return side;
    }
    private Control AccountIcon(Account a,double size)
    {
        Control ImageOrMark(string file,double width)
        {
            if(File.Exists(file))try {
                if(!_bitmaps.TryGetValue(file,out var bmp)){bmp=new Bitmap(file);_bitmaps[file]=bmp;}
                return new Image{Source=bmp,Width=width,Height=width,Stretch=Stretch.UniformToFill};
            }catch { }
            return ServiceMark.Create(a.Service,width);
        }
        bool hasAvatar=File.Exists(a.AvatarPath);
        var panel=new Grid{Width=size,Height=size};
        var file=a.IconLayout==IconLayout.Custom?a.IconPath:a.IconLayout==IconLayout.AvatarWithService?a.AvatarPath:"";
        panel.Children.Add(new Border{Background=Brushes.White,CornerRadius=new CornerRadius(5),ClipToBounds=true,Child=ImageOrMark(file,size)});
        if(hasAvatar && a.IconLayout is IconLayout.AvatarWithService or IconLayout.ServiceWithAvatar)
        {
            double small=size*.52;
            panel.Children.Add(new Border{Background=Brushes.White,CornerRadius=new CornerRadius(small/2),ClipToBounds=true,
                VerticalAlignment=VerticalAlignment.Bottom,HorizontalAlignment=HorizontalAlignment.Right,
                Child=ImageOrMark(a.IconLayout==IconLayout.ServiceWithAvatar?a.AvatarPath:"",small)});
        }
        return panel;
    }
    private Control Tabs()
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=3,Margin=new Thickness(12,8,12,0)};
        for(int i=0;i<_tabs.Count;i++)
        {
            var index=i;var t=_tabs[i];var title=t.Page switch{"all"=>"Все файлы","favorites"=>"Избранное","trash"=>"Корзина",_=>_state.Accounts.First(a=>a.Id==t.AccountId).Service.Split(' ')[0]+" · "+(t.Path.Length==0?_state.Accounts.First(a=>a.Id==t.AccountId).Name:t.Path.Split('/').Last())};
            var open=Btn(title,()=>{_active=index;_selected=null;_query="";Render();Save();});open.Classes.Add("quiet");open.Tag=t;open.PointerPressed+=(_,e)=>StartDrag(t,e);
            var close=Btn("×",()=>{_tabs.RemoveAt(index);if(_tabs.Count==0 && _state.Accounts.Count>0)_tabs.Add(new(){AccountId=_state.Accounts[0].Id});_active=Math.Clamp(_active,0,_tabs.Count-1);Render();Save();});close.Classes.Add("quiet");close.Padding=new Thickness(5);
            row.Children.Add(new Border{Background=i==_active?Paper:Brushes.Transparent,CornerRadius=new CornerRadius(6,6,0,0),Child=Row(open,close),BorderBrush=Line,BorderThickness=new Thickness(i==_active?1:0)});
        }
        row.Children.Add(Btn("＋",()=>{_previous.Clear();_tabs.Add(new(){AccountId=Current.AccountId});_active=_tabs.Count-1;Render();Save();}));
        return new Border{Background=Surface,Child=new ScrollViewer{Content=row,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled}};
    }
    private Control Toolbar()
    {
        var root=new StackPanel{Spacing=8,Margin=new Thickness(16,10,16,8)};
        if(_previous.Count>0){var restore=Row(Text("Предыдущая сессия",11,Muted),Btn($"Восстановить вкладки ({_previous.Count})",()=>{_tabs.Clear();_tabs.AddRange(_previous);_previous=[];_active=0;Render();Save();}),Btn("Скрыть",()=>{_previous=[];Render();Save();}));root.Children.Add(restore);}
        var top=new Grid{ColumnDefinitions=new ColumnDefinitions("Auto,*,280")};top.Children.Add(Btn("←",()=>Navigate(Current.AccountId,Paths.Parent(Current.Path))));
        var path=Text(Current.Page=="files"?CurrentAccount.Title+"  /  "+(Current.Path.Length==0?"Мои файлы":Current.Path):Current.Page=="favorites"?"Избранное":Current.Page=="trash"?"Корзина":"Все хранилища",12,Muted);path.Margin=new Thickness(12,0);var crumb=new Border{Child=path,Background=Surface,BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(5),Margin=new Thickness(8,0)};Grid.SetColumn(crumb,1);top.Children.Add(crumb);
        var search=new TextBox{Watermark="Поиск по всем хранилищам",Text=_query};Grid.SetColumn(search,2);top.Children.Add(search);search.TextChanged+=(_,_)=>{_query=search.Text??"";RenderFiles();};root.Children.Add(top);
        var commands=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto")};var actions=Row(AsyncBtn("↑  Загрузить",Upload,true),Btn("＋  Папка",NewFolder),AsyncBtn("↻  Обновить",()=>Refresh(CurrentAccount)));if(WebLocation.For(CurrentAccount)!=null)actions.Children.Add(Btn("Веб-просмотр",()=>ShowWeb(CurrentAccount)));actions.Children.Add(Btn("⋯",()=>AccountSettings(CurrentAccount)));commands.Children.Add(actions);
        var views=Row(Btn(_state.Settings.View=="List"?"▦ Сетка":"☷ Список",()=>{_state.Settings.View=_state.Settings.View=="List"?"Grid":"List";Render();Save();}),Btn("◧ Просмотр",()=>{_preview=!_preview;Render();}));Grid.SetColumn(views,1);commands.Children.Add(views);root.Children.Add(commands);
        var filter=Row();var kinds=Combo(new[]{"Все","Изображения","Видео","Музыка","Документы","Архивы","Программы"},_kind,x=>{_kind=x;RenderFiles();});kinds.MinWidth=145;filter.Children.Add(kinds);
        filter.Children.Add(Combo(new[]{"Все состояния","Файл актуален","Только в облаке","Не совпадает","Новая версия","Удалён с устройства"},_statusFilter,x=>{_statusFilter=x;RenderFiles();}));
        var scope=new CheckBox{Content="Во всём аккаунте",IsChecked=_wholeAccount,FontSize=11,Margin=new Thickness(10,0)};scope.IsCheckedChanged+=(_,_)=>{_wholeAccount=scope.IsChecked==true;RenderFiles();};filter.Children.Add(scope);var filters=new Expander{Header="Фильтры",Content=filter,IsExpanded=_kind!="Все"||_statusFilter!="Все состояния"||_wholeAccount};root.Children.Add(filters);return root;
    }
    private ComboBox Combo(IEnumerable<string> options,string selected,Action<string> change)
    {
        var c=new ComboBox{ItemsSource=options.ToList(),SelectedItem=selected};c.SelectionChanged+=(_,_)=>{if(c.SelectedItem is string value)change(value);};return c;
    }
    private IEnumerable<CloudEntry> Visible()
    {
        var t=Current;return _state.Entries.Where(f=>t.Page=="trash"?f.IsDeleted:!f.IsDeleted)
            .Where(f=>_query.Length>0?f.Name.Contains(_query,StringComparison.CurrentCultureIgnoreCase):t.Page switch{"all"=>true,"favorites"=>f.IsFavorite,"trash"=>true,_=>f.AccountId==t.AccountId&&(_wholeAccount||f.Parent==t.Path)})
            .Where(f=>_kind=="Все"||f.Kind==_kind).Where(f=>_statusFilter=="Все состояния"||f.StatusLabel==_statusFilter)
            .OrderByDescending(f=>f.IsFolder).ThenBy(f=>f.Name,StringComparer.CurrentCultureIgnoreCase);
    }
    private void RenderFiles()
    {
        _fileArea.Children.Clear();var entries=Visible().ToList();
        if(_state.Settings.View=="Grid")
        {
            var wrap=new WrapPanel{Orientation=Orientation.Horizontal,ItemWidth=_state.Settings.ThumbnailSize+25,Margin=new Thickness(18,0)};
            foreach(var f in entries)
            {
                var content=Stack(FileSymbol.Create(f,54),Text(f.Name,12),StatusButton(f));
                var tile=new Border{Child=content,Padding=new Thickness(12),Margin=new Thickness(5),Background=Surface,CornerRadius=new CornerRadius(6),MinHeight=_state.Settings.ThumbnailSize,Tag=f};
                tile.Tapped+=(_,_)=>Open(f);tile.PointerPressed+=(_,e)=>StartDrag(f,e);tile.ContextMenu=FileMenu(f);wrap.Children.Add(tile);
            }
            _fileArea.Children.Add(new ScrollViewer{Content=wrap});
        }
        else
        {
            var table=new DataGrid{ItemsSource=entries,AutoGenerateColumns=false,RowHeight=_expanded.Count>0?double.NaN:_state.Settings.RowHeight,Margin=new Thickness(22,0,14,0),Background=Paper,Foreground=Ink,HorizontalGridLinesBrush=Line};
            table.Columns.Add(new DataGridTemplateColumn{Header="Название",Width=new DataGridLength(1,DataGridLengthUnitType.Star),CellTemplate=new FuncDataTemplate<CloudEntry>((f,_)=>f is null?new TextBlock():FileCell(f))});
            table.Columns.Add(new DataGridTextColumn{Header="Тип",Binding=new Avalonia.Data.Binding("Kind"),Width=new DataGridLength(125)});
            table.Columns.Add(new DataGridTextColumn{Header="Изменён",Binding=new Avalonia.Data.Binding("ModifiedLabel"),Width=new DataGridLength(180)});
            table.Columns.Add(new DataGridTextColumn{Header="Размер",Binding=new Avalonia.Data.Binding("SizeLabel"),Width=new DataGridLength(110)});
            table.Columns.Add(new DataGridTemplateColumn{Header="",Width=new DataGridLength(40),CellTemplate=new FuncDataTemplate<CloudEntry>((f,_)=>{var b=Btn("⋯",()=>{});b.Classes.Add("quiet");b.Padding=new Thickness(5);if(f is not null){b.Click+=(_,_)=>{b.ContextMenu=FileMenu(f);b.ContextMenu.Open(b);};}return b;})});
            table.SelectionChanged+=(_,_)=>{_selected=table.SelectedItem as CloudEntry;if(_preview)RenderDetail();};
            if(_selected is not null)table.SelectedItem=entries.FirstOrDefault(f=>f.Id==_selected.Id);
            _fileArea.Children.Add(table);
        }
        if(Current.Page=="files" && CurrentAccount.CatalogError.Length>0)_fileArea.Children.Add(new Border{Background=Surface,Padding=new Thickness(10),VerticalAlignment=VerticalAlignment.Bottom,Child=Text(CurrentAccount.CatalogError,12,Ink)});
        if(entries.Count==0 && !(Current.Page=="files"&&CurrentAccount.CatalogError.Length>0))_fileArea.Children.Add(new Border{Child=Stack(Text("Пока нет файлов",21,Muted),Text(_query.Length>0?"Измените поисковый запрос":"Перетащите файлы или нажмите «Загрузить»",12,Muted)),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});
        if(_preview)RenderDetail();
    }
    private Control FileMark(CloudEntry f)=>FileSymbol.Create(f,_state.Settings.IconSize+10);
    private void ShowWeb(Account account,CloudEntry? file=null)
    {
        var uri=WebLocation.For(account,file);
        if(uri==null)return;
        var bar=new DockPanel{Margin=new Thickness(12,8)};
        var external=Btn("Открыть в браузере ↗",()=>LaunchUrl(uri.AbsoluteUri));
        DockPanel.SetDock(external,Dock.Right);bar.Children.Add(external);
        bar.Children.Add(Row(AccountIcon(account,24),Text(account.Name+"   /   "+uri.Host,13,Ink,true)));
        var message=Text("",12,Muted);
        var grid=new Grid{RowDefinitions=new RowDefinitions("Auto,Auto,*"),Background=Paper};
        grid.Children.Add(bar);Grid.SetRow(message,1);grid.Children.Add(message);
        var profile=Path.Combine(_store.DirectoryPath,"web-profiles",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(account.Id))));
        var host=new CloudWebView(uri,profile,error=>message.Text=error);
        host.SizeChanged+=(_,_)=>Dispatcher.UIThread.Post(host.UpdateSize);
        Grid.SetRow(host,2);grid.Children.Add(host);
        var viewer=new Window{Icon=LogoRaster.WindowMark(),Title=(file?.Name??account.Service)+" · RCH",Width=1040,Height=740,MinWidth=560,MinHeight=360,Content=grid};
        if(TopLevel.GetTopLevel(this) is Window owner)viewer.Show(owner);else viewer.Show();
    }
    private Control FileCell(CloudEntry f)
    {
        var p=new StackPanel{Spacing=2};var row=Row(FileMark(f),Stack(Text(f.Name,_state.Settings.TextSize),StatusButton(f)));p.Children.Add(row);
        if(_expanded.Contains(f.Id))
        {
            p.Children.Add(Text("Скачан: "+(f.Downloaded?.ToLocalTime().ToString("dd.MM.yyyy, HH:mm")??"не скачан"),10,Muted));
            p.Children.Add(Row(AsyncBtn("Заменить",()=>Download(f,ExistingFileAction.Replace)),AsyncBtn("Сохранить обе",()=>Download(f,ExistingFileAction.RenameOld)),AsyncBtn("В папку версий",()=>Download(f,ExistingFileAction.ArchiveOld))));
        }
        var b=new Border{Child=p,Padding=new Thickness(8,4),Tag=f,ContextMenu=FileMenu(f)};b.Tapped+=(_,_)=>{if(!_dragging)Open(f);};b.PointerPressed+=(_,e)=>StartDrag(f,e);return b;
    }
    private Control StatusButton(CloudEntry f)
    {
        var b=Btn(f.StatusLabel,()=>{if(f.IsFolder)return;if(!_expanded.Add(f.Id))_expanded.Remove(f.Id);RenderFiles();});b.Classes.Add("quiet");b.Padding=new Thickness(0);b.FontSize=10;b.MinHeight=18;b.HorizontalAlignment=HorizontalAlignment.Left;b.Foreground=f.Status switch{LocalStatus.Current=>B("#2F9C6D"),LocalStatus.Mismatch=>B("#BF8934"),LocalStatus.NewVersion=>B(_state.Settings.Accent),_=>Muted};b.Tapped+=(_,e)=>e.Handled=true;return b;
    }
    private void Open(CloudEntry f)
    {
        _selected=f;if(f.IsFolder){Navigate(f.AccountId,f.Path);return;}_preview=true;Render();
    }
    private MenuItem Menu(string title,Action action){var item=new MenuItem{Header=title};item.Click+=(_,_)=>{try{action();}catch(Exception e){LogEvent(SafeDiagnostic.Exception(e));Notify(e.Message);}};return item;}
    private ContextMenu FileMenu(CloudEntry f)
    {
        var items=new List<MenuItem>{Menu("Предпросмотр",()=>Open(f)),Menu("Открыть в новой вкладке",()=>{_tabs.Add(new(){AccountId=f.AccountId,Path=f.IsFolder?f.Path:f.Parent});_active=_tabs.Count-1;Render();Save();}),Menu("Скачать",()=>_ = Run(()=>Download(f,ExistingFileAction.RenameOld))),Menu("Открыть в Windows…",()=>OpenExternal(f)),Menu("Копировать в…",()=>TransferDialog(f,false)),Menu("Переместить в…",()=>TransferDialog(f,true)),Menu("Переименовать…",()=>Rename(f)),Menu(f.IsFavorite?"Убрать из избранного":"В избранное",()=>{f.IsFavorite=!f.IsFavorite;Save();RenderFiles();}),Menu(f.IsDeleted?"Восстановить":"Удалить…",()=>Delete(f))};
        var account=_state.Accounts.First(a=>a.Id==f.AccountId);
        if(WebLocation.For(account,f)!=null)items.Insert(1,Menu("Веб-просмотр файла",()=>ShowWeb(account,f)));
        return new ContextMenu{ItemsSource=items};
    }
    private void RenderDetail()
    {
        foreach(var c in _fileArea.Children.Where(c=>Grid.GetColumn(c)==1).ToArray())_fileArea.Children.Remove(c);
        _details=new StackPanel{Spacing=12};_details.Children.Add(Row(Text("Предпросмотр",13,Ink,true),Btn("×",()=>{_preview=false;Render();})));
        var f=_selected;
        if(f is null)_details.Children.Add(Text("Выберите файл",12,Muted));
        else
        {
            Control preview=Card(FileSymbol.Create(f,80),new Thickness(35));
            var a=_state.Accounts.First(x=>x.Id==f.AccountId);
            if(!a.IsDemo&&!string.IsNullOrWhiteSpace(a.LocalFolder))
            {
                try{var local=Paths.LocalPath(a.LocalFolder,f.Path);if(File.Exists(local)&&f.Kind=="Изображения"){if(!_bitmaps.ContainsKey(local))_bitmaps[local]=new Bitmap(local);preview=new Image{Source=_bitmaps[local],MaxHeight=230,Stretch=Stretch.Uniform};}else if(File.Exists(local)&&Path.GetExtension(f.Name).Equals(".xlsx",StringComparison.OrdinalIgnoreCase)){
                    var rows=SheetPreview.Read(local);
                    if(rows.Count>0){
                        var sheet=new StackPanel();
                        for(int i=0;i<rows.Count;i++){
                            var line=new Grid{ColumnDefinitions=new ColumnDefinitions("*,*,*"),Background=i==0?B("#24774E"):i%2==1?Surface:Paper};
                            for(int col=0;col<3;col++){
                                var value=Text(rows[i][col]??"",11,i==0?Brushes.White:Ink,i==0);
                                value.Margin=new Thickness(4,5);Grid.SetColumn(value,col);line.Children.Add(value);
                            }
                            sheet.Children.Add(line);
                        }
                        preview=new Border{BorderThickness=new Thickness(1),BorderBrush=Line,Child=sheet};
                    }
                }else if(File.Exists(local)&&Path.GetExtension(f.Name)==".txt"&&f.Size<100_000)preview=Card(Text(File.ReadAllText(local),12));}catch(Exception e){LogEvent(SafeDiagnostic.Exception(e));Notify(e.Message);}
            }
            _details.Children.Add(preview);_details.Children.Add(Text(f.Name,15,Ink,true));_details.Children.Add(StatusButton(f));
            foreach(var (label,value) in new[]{("Размер",f.SizeLabel),("Тип",f.Kind),("Хранилище",a.Title),("Папка",f.Parent.Length==0?"Корень":f.Parent),("Скачан",f.Downloaded?.ToLocalTime().ToString("dd.MM.yyyy, HH:mm")??"Не скачан")}){var pair=new Grid{ColumnDefinitions=new ColumnDefinitions("83,*")};pair.Children.Add(Text(label,11,Muted));var item=Text(value,12);Grid.SetColumn(item,1);pair.Children.Add(item);_details.Children.Add(pair);}
            if(WebLocation.For(a,f)!=null)_details.Children.Add(Btn("Веб-просмотр",()=>ShowWeb(a,f),true));
            _details.Children.Add(AsyncBtn("↓ Скачать локально",()=>Download(f,ExistingFileAction.RenameOld),true));_details.Children.Add(Btn("Открыть в…",()=>OpenExternal(f)));
        }
        var border=new Border{Child=new ScrollViewer{Content=_details},Padding=new Thickness(18),BorderBrush=Line,BorderThickness=new Thickness(1,0,0,0)};Grid.SetColumn(border,1);_fileArea.Children.Add(border);
    }
    private void Navigate(string account,string folder)
    {
        _previous.Clear();Current.AccountId=account;Current.Path=folder;Current.Page="files";_selected=null;_query="";_state.Accounts.First(a=>a.Id==account).LastUsed=DateTimeOffset.Now;Render();Save();
    }
    private string RcloneExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "rclone.exe" : "rclone");
    private string RcloneConfig => Path.Combine(_store.DirectoryPath, "connections.conf");
    private IAccountProvider Provider(Account a)
    {
        if (a.Provider == ProviderKind.Rclone) return new RcloneProvider(a, RcloneExe, RcloneConfig,LogEvent);
        var password=_sessionSecrets.GetValueOrDefault(a.Id);
        if(password is null&&OperatingSystem.IsWindows())password=new WindowsSecretStore(_store.DirectoryPath).Read(a.Id);
        if(password is null&&a.UserName.Length>0)throw new UnauthorizedAccessException("Введите пароль в настройках подключения");
        return new WebDavProvider(a,password??"");
    }
    private async Task Refresh(Account a)
    {
        if(a.IsDemo){a.LastCheck=DateTimeOffset.Now;Save();Render();Notify("Демо-каталог обновлён. Внешние сервисы не запрашивались.");return;}
        var operation=Guid.NewGuid().ToString("N");
        LogEvent($"Индексация · account={a.Id} op={operation} service={a.Service} · начало");
        using var provider=Provider(a);
        await RefreshQuota(a,provider);
        if(a.Service=="Google Диск")try{await GoogleProfile.LoadAsync(a,RcloneConfig,Path.Combine(_store.DirectoryPath,"icons"));Save();}catch(Exception e) when(e is HttpRequestException or IOException or TaskCanceledException or JsonException or KeyNotFoundException) { }
        _message.Text="Индексация: "+a.Title;
        CatalogScan incoming;
        try { incoming=await new CatalogService().ScanAsync(provider,new Progress<int>(n=>_message.Text="Индексация: "+a.Name+" · "+n+" объектов"),CancellationToken.None); }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or HttpRequestException or JsonException) {
            a.CatalogError="Не удалось прочитать корневой каталог. "+e.Message;Save();if(_modal==null)Render();throw;
        }
        LogEvent($"Индексация · account={a.Id} op={operation} · завершена · объектов {incoming.Entries.Count} · недоступно папок {incoming.FailedFolders.Count}");
        CatalogService.Merge(_state,a,incoming);Save();if(_modal==null)Render();_message.Text=a.CatalogError.Length>0?a.CatalogError:"Каталог проверен: "+incoming.Entries.Count+" объектов";await SaveLocalCopies(a);
    }
    private async Task RefreshQuota(Account account,IAccountProvider provider)
    {
        if(provider is not IQuotaProvider quota){
            account.QuotaError="Подключение не поддерживает запрос квоты";Save();return;
        }
        try{
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var value=await quota.GetQuotaAsync(timeout.Token);
            value.Apply(account);Save();
            LogEvent($"Квота · account={account.Id} service={account.Service} · обновлена");
        }catch(Exception e) when(e is IOException or System.Text.Json.JsonException or HttpRequestException or OperationCanceledException){
            account.QuotaError="Запрос квоты не завершён";Save();
            LogEvent($"Квота · account={account.Id} · "+SafeDiagnostic.Exception(e));
        }
    }
    private async Task SaveLocalCopies(Account account)
    {
        if (account.IsDemo || !account.SaveLocally || string.IsNullOrWhiteSpace(account.LocalFolder)
            || !_savingLocally.Add(account.Id)) return;
        try
        {
            var used = await Task.Run(() => StorageStatistics.DirectorySize(account.LocalFolder));
            foreach (var file in _state.Entries.Where(f => f.AccountId == account.Id && !f.IsFolder && !f.IsDeleted).ToArray())
            {
                if (!account.SaveLocally || !_state.Accounts.Contains(account)) break;
                if (file.SuppressAutoDownload || file.Status != LocalStatus.CloudOnly || file.Size > account.MaxFileBytes) continue;
                var path = Paths.LocalPath(account.LocalFolder, file.Path);
                if (File.Exists(path) || _state.Downloads.Any(d => d.AccountId == account.Id && d.Path == file.Path)) continue;
                if (file.Size > account.LocalLimitBytes - used)
                {
                    _message.Text=account.Title + " · Достигнут лимит. Ручное скачивание доступно.";
                    break;
                }
                await Download(file, ExistingFileAction.RenameOld);
                used += file.Size;
            }
        }
        finally { _savingLocally.Remove(account.Id); }
    }

    private async Task Download(CloudEntry f,ExistingFileAction mode)
    {
        var a=_state.Accounts.First(a=>a.Id==f.AccountId);if(f.IsFolder){Notify("В alpha 0.1 скачивание папок ещё не подключено");return;}
        if(string.IsNullOrWhiteSpace(a.LocalFolder)){AccountSettings(a);Notify("Сначала укажите папку на ПК. Скачивание без пути недоступно.");return;}
        if(a.IsDemo){await DemoTransfer(f.Name,"ДЕМО · "+a.Title+" → ПК");f.Status=LocalStatus.Current;f.Downloaded=DateTimeOffset.Now;Save();RenderFiles();return;}
        using var provider=Provider(a);
        var key = (a.Id, f.Path);
        if (!_downloading.Add(key)) return;
        var pending = new PendingDownload(a.Id, f.Path, a.LocalFolder, f.ETag, mode);
        _state.Downloads.RemoveAll(d => d.AccountId == a.Id && d.Path == f.Path);
        _state.Downloads.Add(pending);
        Save();
        var job=new Job(f.Name,a.Title+" → ПК");_jobs.Add(job);RenderQueue();
        try{await new LocalFiles().DownloadAsync(a,f,provider,mode,new Progress<long>(n=>{job.Percent=f.Size==0?0:n*100d/f.Size;job.State=Format.Bytes(n)+" из "+f.SizeLabel;RenderQueue();}),job.Token.Token);job.Percent=100;job.State="Готово";_state.Downloads.Remove(pending);Save();RenderFiles();}
        catch(Exception e){job.State=e is OperationCanceledException?"Приостановлено · повтор начнётся заново":"Ошибка: "+e.Message;throw;}
        finally{_downloading.Remove(key);RenderQueue();}
    }
    private async Task DemoTransfer(string name,string route)
    {
        var job=new Job(name,route);_jobs.Add(job);RenderQueue();for(int i=0;i<=100;i+=5){await Task.Delay(70);job.Token.Token.ThrowIfCancellationRequested();job.Percent=i;job.State="Демонстрация · "+i+"%";RenderQueue();}job.State="Демо завершено";RenderQueue();
    }
    private void RenderQueue()
    {
        _queuePanel.Children.Clear();_queuePanel.Children.Add(Row(Btn((_queueOpen?"▾ ":"▸ ")+"Текущие передачи · "+_jobs.Count,()=>{_queueOpen=!_queueOpen;RenderQueue();}),Btn("Приостановить всё",()=>{foreach(var j in _jobs.Where(j=>j.Percent<100)){j.Token.Cancel();j.State="Приостановлено";}RenderQueue();})));
        if(!_queueOpen)return;
        foreach (var pending in _state.Downloads.Where(d => !_downloading.Contains((d.AccountId,d.Path))).ToArray())
        {
            _queuePanel.Children.Add(Row(Text(pending.Path,12), AsyncBtn("Продолжить", async () =>
            {
                var account = _state.Accounts.SingleOrDefault(a => a.Id == pending.AccountId);
                var file = _state.Entries.SingleOrDefault(f => f.AccountId == pending.AccountId && f.Path == pending.Path);
                if (account == null || file == null || file.ETag != pending.Revision || account.LocalFolder != pending.LocalRoot)
                    throw new IOException("Путь или версия изменились. Выберите файл в каталоге.");
                await Download(file,pending.Action);
            }), Btn("Убрать", () => { _state.Downloads.Remove(pending); Save(); RenderQueue(); })));
        }
        if(_jobs.Count==0 && _state.Downloads.Count==0){_queuePanel.Children.Clear();_queuePanel.Children.Add(Text("⇅   Текущие передачи   ·   0",12,Muted));return;}
        foreach(var job in _jobs.TakeLast(3))
        {
            var grid=new Grid{ColumnDefinitions=new ColumnDefinitions("*,200,Auto"),Margin=new Thickness(0,3)};grid.Children.Add(Stack(Text(job.Name,12),Text(job.Route+" · "+job.State,10,Muted)));
            var progress=new ProgressBar{Value=job.Percent,Maximum=100,Minimum=0,Height=4,Foreground=B(_state.Settings.Accent),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(progress,1);grid.Children.Add(progress);
            var cancel=Btn("×",()=>{job.Token.Cancel();_jobs.Remove(job);RenderQueue();});Grid.SetColumn(cancel,2);grid.Children.Add(cancel);_queuePanel.Children.Add(grid);
        }
    }
    private void OpenExternal(CloudEntry f)
    {
        var a=_state.Accounts.First(a=>a.Id==f.AccountId);if(a.IsDemo){Notify("Это демо-файл. Реального локального файла нет.");return;}
        var path=Paths.LocalPath(a.LocalFolder,f.Path);if(!File.Exists(path)){Notify("Сначала скачайте файл на ПК");return;}
        if(OperatingSystem.IsWindows())Process.Start(new ProcessStartInfo("rundll32.exe"){ArgumentList={"shell32.dll,OpenAs_RunDLL",path},UseShellExecute=false});
        else Process.Start(new ProcessStartInfo("xdg-open"){ArgumentList={path},UseShellExecute=false});
    }
    private async Task Upload()
    {
        if(Current.Page!="files"){Notify("Сначала откройте нужную папку хранилища");return;}
        var storage=TopLevel.GetTopLevel(this)?.StorageProvider;if(storage is null)return;
        var a=CurrentAccount;var folder=Current.Path;
        var picked=await storage.OpenFilePickerAsync(new FilePickerOpenOptions{Title="Загрузить файлы в облако",AllowMultiple=true});
        foreach(var file in picked)
        {
            var destination=Paths.Combine(folder,file.Name);
            if(_state.Entries.Any(f=>!f.IsDeleted&&f.AccountId==a.Id&&f.Path==destination)){Notify("Файл "+file.Name+" уже существует. В alpha перезапись при загрузке отключена.");continue;}
            if(a.IsDemo){await DemoTransfer(file.Name,"ДЕМО · устройство → "+a.Title);_state.Entries.Add(new(){AccountId=a.Id,Name=file.Name,Path=destination,Size=0,Modified=DateTimeOffset.Now});}
            else{using var provider=Provider(a);await using var stream=await file.OpenReadAsync();await provider.UploadAsync(destination,stream,CancellationToken.None);}
        }
        if(!a.IsDemo)await Refresh(a);Save();RenderFiles();
    }
    private void NewFolder()
    {
        var input=new TextBox{Watermark="Название папки"};ShowModal("Новая папка",Field("Название",input),AsyncBtn("Создать",async()=>{var name=ValidName(input.Text);var a=CurrentAccount;var path=Paths.Combine(Current.Path,name);if(a.IsDemo)_state.Entries.Add(new(){AccountId=a.Id,Name=name,Path=path,IsFolder=true});else{using var p=Provider(a);await p.CreateFolderAsync(path,CancellationToken.None);await Refresh(a);}Save();CloseModal();RenderFiles();},true));
    }
    private static string ValidName(string? name)
    {
        var value=name?.Trim()??"";if(value.Length==0||value.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|'])>=0||value is "." or "..")throw new ArgumentException("Введите допустимое название");return value;
    }
    private void Rename(CloudEntry f)
    {
        var input=new TextBox{Text=f.Name};ShowModal("Переименовать",Field("Название",input),AsyncBtn("Сохранить",async()=>{var name=ValidName(input.Text);var a=_state.Accounts.First(a=>a.Id==f.AccountId);var destination=Paths.Combine(f.Parent,name);if(a.IsDemo){if(f.IsFolder){var old=f.Path;foreach(var child in _state.Entries.Where(x=>x.AccountId==a.Id&&x.Path.StartsWith(old+"/",StringComparison.Ordinal)))child.Path=destination+child.Path[old.Length..];}f.Name=name;f.Path=destination;}else{using var p=Provider(a);await p.MoveAsync(f.Path,destination,CancellationToken.None);await Refresh(a);}Save();CloseModal();RenderFiles();},true));
    }
    private void Delete(CloudEntry f)
    {
        var a=_state.Accounts.First(a=>a.Id==f.AccountId);
        if(f.IsDeleted){f.IsDeleted=false;Save();RenderFiles();return;}
        var warning=a.IsDemo?"Демо-файл будет перемещён в корзину макета.":"У этого WebDAV-подключения нет поддерживаемой корзины. Файл на сервере будет удалён БЕЗВОЗВРАТНО.";
        ShowModal("Удалить «"+f.Name+"»?",Text(warning,13),AsyncBtn(a.IsDemo?"В корзину":"Удалить безвозвратно",async()=>{if(a.IsDemo)f.IsDeleted=true;else{using var p=Provider(a);await p.DeleteAsync(f.Path,CancellationToken.None);await Refresh(a);}Save();CloseModal();RenderFiles();},true));
    }
    private void TransferDialog(CloudEntry f,bool move)
    {
        var targets=_state.Accounts.Select(a=>a.Title).ToList();var combo=new ComboBox{ItemsSource=targets,SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};var path=new TextBox{Watermark="Папка назначения, пусто — корень"};
        ShowModal(move?"Переместить в…":"Копировать в…",Stack(Text(f.Name,14,Ink,true),Field("Аккаунт",combo),Field("Папка",path)),AsyncBtn(move?"Переместить":"Копировать",async()=>{var account=_state.Accounts[combo.SelectedIndex];await Transfer(f,account,Paths.Normalize(path.Text??""),move);CloseModal();},true));
    }
    private async Task Transfer(CloudEntry f,Account target,string folder,bool move)
    {
        var source=_state.Accounts.First(a=>a.Id==f.AccountId);if (!source.IsDemo && !target.IsDemo)
        {
            var toPath = Paths.Combine(folder,f.Name);
            if (f.IsFolder) throw new IOException("Выберите отдельный файл");
            if (source.Id==target.Id && f.Path==toPath) throw new IOException("Файл уже в этой папке");
            using var from = Provider(source);
            using var to = Provider(target);
            if ((await to.ListAsync(folder,default)).Any(entry=>entry.Path==toPath))
                throw new IOException("В папке назначения уже есть файл с таким именем");
            if (move && source.Id==target.Id)
                await from.MoveAsync(f.Path,toPath,default);
            else
            {
                if (move) throw new IOException("Для разных хранилищ выберите копирование. Перемещение с проверкой версии источника пока недоступно.");
                var staging = Path.Combine(Path.GetTempPath(),"rchub-"+Guid.NewGuid().ToString("N"));
                await using var bytes = new FileStream(staging,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,81920,FileOptions.DeleteOnClose|FileOptions.Asynchronous);
                await from.DownloadAsync(f.Path,bytes,null,default);
                if (bytes.Length!=f.Size) throw new IOException("Файл изменился. Обновите каталог.");
                bytes.Position=0;
                await to.UploadAsync(toPath,bytes,default);
            }
            await Refresh(target);
            return;
        }
        
        var destination=Paths.Combine(folder,f.Name);if(source.Id==target.Id&&(destination==f.Path||f.IsFolder&&destination.StartsWith(f.Path+"/",StringComparison.Ordinal)))throw new IOException("Нельзя копировать объект в себя");
        if(_state.Entries.Any(x=>!x.IsDeleted&&x.AccountId==target.Id&&x.Path==destination))throw new IOException("Такой файл уже существует. Выберите другую папку.");
        await DemoTransfer(f.Name,"ДЕМО · "+source.Name+" → "+target.Name);
        var copy=new CloudEntry{AccountId=target.Id,Name=f.Name,Path=destination,Size=f.Size,IsFolder=f.IsFolder,Modified=f.Modified};_state.Entries.Add(copy);
        if(move)_state.Entries.Remove(f);Save();RenderFiles();Notify("Демонстрационная передача завершена. Оригинал "+(move?"удалён":"сохранён"));
    }
    private void StartDrag(object source,PointerPressedEventArgs e)
    {
        if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;_dragObject=source;_dragStart=e.GetPosition(this);_dragging=false;
    }
    private void DragMoved(object? sender,PointerEventArgs e)
    {
        if(_dragObject is null)return;var point=e.GetPosition(this);if(!_dragging&&Math.Sqrt(Math.Pow(point.X-_dragStart.X,2)+Math.Pow(point.Y-_dragStart.Y,2))<9)return;_dragging=true;
        if(_dragGhost is null){_dragGhost=Card(Text(_dragObject is CloudEntry f?f.Name:_dragObject is Account a?a.Name:"Переместить вкладку",12),new Thickness(10),Surface);_dragGhost.IsHitTestVisible=false;_dragGhost.HorizontalAlignment=HorizontalAlignment.Left;_dragGhost.VerticalAlignment=VerticalAlignment.Top;Grid.SetColumnSpan(_dragGhost,2);_root.Children.Add(_dragGhost);}
        _dragGhost.Margin=new Thickness(Math.Min(point.X+12,Bounds.Width-230),point.Y+12,0,0);
    }
    private void DragReleased(object? sender,PointerReleasedEventArgs e)
    {
        var source=_dragObject;_dragObject=null;if(_dragGhost is not null){_root.Children.Remove(_dragGhost);_dragGhost=null;}if(!_dragging)return;e.Handled=true;
        var p=e.GetPosition(_root);var tags=_root.GetVisualsAt(p).OfType<Control>().Select(c=>c.Tag).Where(t=>t is Account or TabState).ToList();var target=tags.FirstOrDefault();
        if(source is CloudEntry f){if(target is Account a)_ = Run(()=>Transfer(f,a,"",false));else if(target is TabState t)_ = Run(()=>Transfer(f,_state.Accounts.First(a=>a.Id==t.AccountId),t.Path,false));}
        else if(source is Account from&&target is Account to){_state.Accounts.Remove(from);_state.Accounts.Insert(_state.Accounts.IndexOf(to),from);from.Group=to.Group;Save();Render();}
        else if(source is TabState tab&&target is TabState destination){var i=_tabs.IndexOf(destination);_tabs.Remove(tab);_tabs.Insert(Math.Clamp(i,0,_tabs.Count),tab);_active=_tabs.IndexOf(tab);Save();Render();}
        Dispatcher.UIThread.Post(()=>_dragging=false);
    }
    private Control Field(string label,Control input)=>Stack(Text(label,11,Muted),input);
    private void ShowModal(string title,Control body,params Button[] actions)
    {
        CloseModal();_modalGeneration++;var stack=new StackPanel{Spacing=18};stack.Children.Add(Text(title,20,Ink,true));stack.Children.Add(body);_modalMessage=Text("",12,Muted);stack.Children.Add(_modalMessage);
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=8};if(!string.IsNullOrWhiteSpace(_state.LocalRoot))footer.Children.Add(Btn("Закрыть",CloseModal));foreach(var b in actions)footer.Children.Add(b);var layout=new Grid{RowDefinitions=new RowDefinitions("*,Auto")};layout.Children.Add(new ScrollViewer{Content=stack});Grid.SetRow(footer,1);footer.Margin=new Thickness(0,12,0,0);layout.Children.Add(footer);
        var box=new Border{Background=Paper,CornerRadius=new CornerRadius(10),Padding=new Thickness(25),Width=Math.Min(760,Math.Max(500,Bounds.Width-100)),MaxHeight=Math.Max(500,Bounds.Height-70),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Child=layout};
        _modal=new Border{Background=B("#990C192B"),Child=box};Grid.SetColumnSpan(_modal,2);_root.Children.Add(_modal);
    }
    private void CloseModal(){if(_modal is not null)_root.Children.Remove(_modal);_modal=null;_modalMessage=null;}
    private void Welcome()
    {
        var folder=new TextBox {Watermark="Выберите папку на компьютере",IsReadOnly=true};
        var select=AsyncBtn("Выбрать папку…",async()=>{
            var picker=TopLevel.GetTopLevel(this)?.StorageProvider;
            if(picker==null)return;
            var selected=await picker.OpenFolderPickerAsync(new FolderPickerOpenOptions{Title="Корневая папка локальных копий"});
            if(selected.Count>0)folder.Text=selected[0].TryGetLocalPath();
        });
        var next=AsyncBtn("Продолжить",async()=>{
            var root=folder.Text;
            if(string.IsNullOrWhiteSpace(root))return;
            using(var probe=new FileStream(Path.Combine(root,".rchub-"+Guid.NewGuid().ToString("N")),FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose))
                probe.WriteByte(0);
            foreach(var account in _state.Accounts.Where(a=>string.IsNullOrWhiteSpace(a.LocalFolder)))
                account.LocalFolder=AccountFolder(root,account);
            _state.LocalRoot=root;_state.Settings.FirstRunComplete=true;Save();CloseModal();Render();
            if(_state.Accounts.Count==0)Connect();else await RefreshDueAccounts();
        },true);
        next.IsEnabled=false;
        folder.TextChanged+=(_,_)=>next.IsEnabled=!string.IsNullOrWhiteSpace(folder.Text);
        ShowModal("Где хранить локальные копии?",Stack(Text("Для каждого аккаунта RCH создаст отдельную папку.",14),folder,select),next);
    }
    private static string AccountFolder(string root,Account account)
    {
        var label=account.Service+" ("+account.Name+")";
        foreach(var c in Path.GetInvalidFileNameChars())label=label.Replace(c,'_');
        var result=Path.Combine(root,label[..Math.Min(label.Length,90)]+" "+account.Id[..8]);
        Directory.CreateDirectory(result);
        return result;
    }
    private void SettingsDialog()
    {
        var s=_state.Settings;var theme=new ComboBox{ItemsSource=new[]{"Light","Dark","System"},SelectedItem=s.Theme};
        var accent=new TextBox{Text=s.Accent,Watermark="#3976E7"};
        var textSize=new Slider{Minimum=10,Maximum=24,Value=s.TextSize,TickFrequency=1,IsSnapToTickEnabled=true};
        var iconSize=new Slider{Minimum=16,Maximum=48,Value=s.IconSize,TickFrequency=1,IsSnapToTickEnabled=true};
        var rowHeight=new Slider{Minimum=40,Maximum=100,Value=s.RowHeight,TickFrequency=1,IsSnapToTickEnabled=true};
        var thumbnails=new Slider{Minimum=80,Maximum=260,Value=s.ThumbnailSize,TickFrequency=5,IsSnapToTickEnabled=true};
        var notify=new CheckBox{Content="Уведомления Windows (адаптер появится в следующей сборке)",IsChecked=s.Notifications};
        var auto=new CheckBox{Content="Автозапуск Windows (адаптер появится в следующей сборке)",IsChecked=s.AutoStart};
        var download=new NumericUpDown{Minimum=0,Maximum=10000,Value=s.DownloadBytesPerSecond/1_000_000m,Increment=1};
        var upload=new NumericUpDown{Minimum=0,Maximum=10000,Value=s.UploadBytesPerSecond/1_000_000m,Increment=1};
        var appearance=Stack(Field("Тема оформления",theme),Field("Акцентный цвет",accent),SliderField("Размер текста",textSize),SliderField("Размер значков",iconSize),SliderField("Высота строк",rowHeight),SliderField("Размер миниатюр",thumbnails));
        var general=Stack(auto,notify,Text("Крестик сворачивает приложение в трей. Для выхода используйте меню значка в трее.",12,Muted),Text("Обычная и portable-версия используют общие данные:",12),Text(_store.DirectoryPath,11,Muted),Field("Лимит скачивания, МБ/с · 0 — без ограничений",download),Field("Лимит загрузки, МБ/с · 0 — без ограничений",upload),Text("В alpha ограничения скорости сохраняются, но регулятор передачи ещё не подключён.",11,Muted));
        var configs=Stack(Btn("Журнал отладки",ShowDebug),AsyncBtn("Проверить обновления",()=>CheckUpdates(true)),Text("Конфигурация содержит только оформление, лимиты и настройки. Аккаунты и пароли в неё не входят.",12,Muted),AsyncBtn("Экспорт конфигурации",ExportConfig),AsyncBtn("Импорт конфигурации",ImportConfig),Btn("Предыдущие вкладки",()=>{CloseModal();_previous=_state.Tabs.ToList();Render();}),Btn("Статистика места на ПК",()=>_ = Run(StorageDialog)),Btn("Группы аккаунтов",Groups));
        var tabs=new TabControl{ItemsSource=new[]{new TabItem{Header="Оформление",Content=appearance},new TabItem{Header="Приложение",Content=general},new TabItem{Header="Данные",Content=configs}}};
        ShowModal("Настройки RCH",tabs,Btn("Сохранить",()=>{try{_ = Color.Parse(accent.Text??"");}catch{Notify("Неверный цвет. Пример: #3976E7");return;}s.Theme=theme.SelectedItem as string??"Light";s.Accent=accent.Text!;s.TextSize=textSize.Value;s.IconSize=iconSize.Value;s.RowHeight=rowHeight.Value;s.ThumbnailSize=thumbnails.Value;s.AutoStart=auto.IsChecked==true;s.Notifications=notify.IsChecked==true;s.DownloadBytesPerSecond=(long)((download.Value??0)*1_000_000);s.UploadBytesPerSecond=(long)((upload.Value??0)*1_000_000);Save();CloseModal();Render();},true));
    }
    private Control SliderField(string label,Slider slider)
    {
        var value=Text(slider.Value.ToString("0"),11,Muted);slider.PropertyChanged+=(_,e)=>{if(e.Property==Slider.ValueProperty)value.Text=slider.Value.ToString("0");};return Stack(Row(Text(label,12),value),slider);
    }
    private async Task ExportConfig()
    {
        var storage=TopLevel.GetTopLevel(this)?.StorageProvider;if(storage is null)return;
        var file=await storage.SaveFilePickerAsync(new FilePickerSaveOptions{Title="Экспорт настроек",SuggestedFileName="RC-Hub-settings.json"});if(file is null)return;
        await using var stream=await file.OpenWriteAsync();using var writer=new StreamWriter(stream);await writer.WriteAsync(_store.ExportSettings(_state.Settings));Notify("Конфигурация сохранена без аккаунтов и паролей");
    }
    private async Task ImportConfig()
    {
        var storage=TopLevel.GetTopLevel(this)?.StorageProvider;if(storage is null)return;
        var picked=await storage.OpenFilePickerAsync(new FilePickerOpenOptions{Title="Импорт настроек",AllowMultiple=false,FileTypeFilter=new[]{new FilePickerFileType("JSON"){Patterns=new[]{"*.json"}}}});if(picked.Count==0)return;
        await using var stream=await picked[0].OpenReadAsync();using var reader=new StreamReader(stream);_state.Settings=_store.ImportSettings(await reader.ReadToEndAsync());Save();CloseModal();Render();Notify("Настройки импортированы");
    }
    private void AccountSettings(Account a)
    {
        var name=new TextBox{Text=a.Name};var groups=new ComboBox{ItemsSource=_state.Groups,SelectedItem=a.Group};
        var adaptive=new CheckBox{Content="Проверять каталог при запуске с учётом активности",IsChecked=a.AdaptiveRefresh};
        var local=new CheckBox{Content="Сохранять файлы локально",IsChecked=a.SaveLocally};
        var path=new TextBox{Text=a.LocalFolder,Watermark="Папка на ПК обязательна для скачивания"};
        var maxFile=new NumericUpDown{Minimum=1,Maximum=10_000_000,Value=a.MaxFileBytes/1_000_000m};
        var maxTotal=new NumericUpDown{Minimum=1,Maximum=1_000_000,Value=a.LocalLimitBytes/1_000_000_000m};
        var keep=new CheckBox{Content="Оставлять локальные копии после удаления в облаке",IsChecked=a.KeepDeletedCopies};
        var layouts=new[]{"Значок сервиса","Аватарка + сервис в углу","Сервис + аватарка в углу","Свой значок"};var layout=new ComboBox{ItemsSource=layouts,SelectedIndex=(int)a.IconLayout};
        var password=new TextBox{PasswordChar='●',Watermark="Новый пароль приложения · пусто — не менять"};
        var selectedIcon=a.IconPath;var selectedAvatar=a.AvatarPath;
        var options=new WrapPanel{Orientation=Orientation.Horizontal};
        var preview=new ContentControl();
        var optionButtons=new List<Button>();
        void UpdatePreview()
        {
            preview.Content=AccountIcon(new Account{Service=a.Service,IconLayout=(IconLayout)layout.SelectedIndex,AvatarPath=selectedAvatar,IconPath=selectedIcon},44);
            for(int i=0;i<optionButtons.Count;i++)
            {
                var button=optionButtons[i];
                button.BorderThickness=new Thickness(i==layout.SelectedIndex?2:1);
                button.BorderBrush=i==layout.SelectedIndex?B(_state.Settings.Accent):Line;
                button.Content=Stack(AccountIcon(new Account{Service=a.Service,IconLayout=(IconLayout)i,AvatarPath=selectedAvatar,IconPath=selectedIcon},29),
                    Text(new[]{"Сервис","Фото + сервис","Сервис + фото","Своя картинка"}[i],11));
            }
        }
        for(int i=0;i<layouts.Length;i++)
        {
            int choice=i;
            var button=AsyncBtn("",async()=>{
                if(choice==3&&!File.Exists(selectedIcon)){var image=await PickImage();if(image==null)return;selectedIcon=image;}
                if(choice is 1 or 2 && !File.Exists(selectedAvatar)){var image=await PickImage();if(image==null)return;selectedAvatar=image;}
                layout.SelectedIndex=choice;UpdatePreview();
            });
            button.Margin=new Thickness(2);optionButtons.Add(button);options.Children.Add(button);
        }
        layout.SelectionChanged+=(_,_)=>UpdatePreview();
        UpdatePreview();
        var content=Stack(Text(a.Provider==ProviderKind.WebDav?a.Service+" · WebDAV":a.Service,12,Muted),Field("Название",name),Field("Группа",groups),adaptive,Text(a.CheckLabel,10,Muted),Text(a.QuotaDescription,11,Muted),Text(a.CatalogError,12,Muted),local,Field("Локальная папка",path),Field("Один файл, МБ",maxFile),Field("Общий объём, ГБ",maxTotal),keep,Field("Значок аккаунта",Row(preview,options)),Row(AsyncBtn("Загрузить свой значок",async()=>{if(await PickImage() is string f){selectedIcon=f;layout.SelectedIndex=(int)IconLayout.Custom;UpdatePreview();}}),AsyncBtn("Загрузить аватарку",async()=>{if(await PickImage() is string f){selectedAvatar=f;layout.SelectedIndex=(int)IconLayout.AvatarWithService;UpdatePreview();}})),Text(a.ProfileEmail,12,Muted));
        if(a.Service=="Google Диск")content.Children.Add(AsyncBtn("Получить данные Google-профиля",async()=>{
            await GoogleProfile.LoadAsync(a,RcloneConfig,Path.Combine(_store.DirectoryPath,"icons"));Save();
            selectedAvatar=a.AvatarPath;UpdatePreview();Notify(a.ProfileEmail.Length>0?a.ProfileEmail:"Фото недоступно");
        }));
        if(a.Provider==ProviderKind.WebDav)content.Children.Add(Field("Пароль приложения (сохраняется защищённо в Windows)",password));
        content.Children.Add(Row(Btn("↑ Выше",()=>{var i=_state.Accounts.IndexOf(a);if(i>0){_state.Accounts.RemoveAt(i);_state.Accounts.Insert(i-1,a);Save();Notify("Порядок изменён");}}),Btn("↓ Ниже",()=>{var i=_state.Accounts.IndexOf(a);if(i<_state.Accounts.Count-1){_state.Accounts.RemoveAt(i);_state.Accounts.Insert(i+1,a);Save();Notify("Порядок изменён");}}),Btn("Отключить…",()=>Disconnect(a))));
        ShowModal("Настройки аккаунта",content,AsyncBtn("Сохранить",async()=>{if(string.IsNullOrWhiteSpace(path.Text)){path.Text=AccountFolder(_state.LocalRoot,a);}a.Name=ValidName(name.Text);a.Group=groups.SelectedItem as string??"Без группы";a.AdaptiveRefresh=adaptive.IsChecked==true;a.SaveLocally=local.IsChecked==true;a.LocalFolder=path.Text?.Trim()??"";a.MaxFileBytes=(long)((maxFile.Value??50)*1_000_000);a.LocalLimitBytes=(long)((maxTotal.Value??30)*1_000_000_000);a.KeepDeletedCopies=keep.IsChecked==true;a.IconLayout=(IconLayout)layout.SelectedIndex;a.IconPath=selectedIcon;a.AvatarPath=selectedAvatar;
            if(!string.IsNullOrEmpty(password.Text)){_sessionSecrets[a.Id]=password.Text;if(OperatingSystem.IsWindows())new WindowsSecretStore(_store.DirectoryPath).Save(a.Id,password.Text);}Save();CloseModal();Render();await SaveLocalCopies(a);},true));
    }
    private async Task<string?> PickImage()
    {
        var storage=TopLevel.GetTopLevel(this)?.StorageProvider;if(storage is null)return null;
        var picked=await storage.OpenFilePickerAsync(new FilePickerOpenOptions{Title="Выберите изображение",AllowMultiple=false,FileTypeFilter=new[]{new FilePickerFileType("Изображения"){Patterns=new[]{"*.png","*.jpg","*.jpeg","*.webp"}}}});if(picked.Count==0)return null;
        await using var input=await picked[0].OpenReadAsync();using var buffer=new MemoryStream();await input.CopyToAsync(buffer);if(buffer.Length>10_000_000)throw new IOException("Выберите изображение до 10 МБ");buffer.Position=0;using var bitmap=new Bitmap(buffer);var folder=Path.Combine(_store.DirectoryPath,"icons");Directory.CreateDirectory(folder);var path=Path.Combine(folder,Guid.NewGuid().ToString("N")+".png");bitmap.Save(path);Notify("Изображение выбрано. Нажмите «Сохранить».");return path;
    }
    private void Groups()
    {
        var name=new TextBox{Watermark="Название группы"};var p=Stack(Field("Новая группа",name),Btn("Добавить",()=>{var n=ValidName(name.Text);if(!_state.Groups.Contains(n))_state.Groups.Add(n);Save();Groups();}));
        foreach(var group in _state.Groups.Where(g=>g!="Без группы").ToList())p.Children.Add(Row(Text(group,13),Btn("Удалить группу",()=>{foreach(var a in _state.Accounts.Where(a=>a.Group==group))a.Group="Без группы";_state.Groups.Remove(group);Save();Groups();})));
        ShowModal("Группы аккаунтов",p,Btn("Готово",()=>{CloseModal();Render();},true));
    }
    private void Disconnect(Account a)
    {
        
        var leave=new CheckBox{Content="Оставить локальные копии на ПК",IsChecked=true};ShowModal("Отключить «"+a.Name+"»?",Stack(Text("Файлы в облаке останутся. Доступ и индекс этого подключения будут удалены из RCH.",13),leave),Btn("Отключить",()=>{if(leave.IsChecked!=true){Notify("Безопасная очистка локальных копий ещё не подключена. Оставьте копии или удалите нужные вручную.");return;}_state.Accounts.Remove(a);_state.Entries.RemoveAll(f=>f.AccountId==a.Id);_tabs.RemoveAll(t=>t.AccountId==a.Id);if(_tabs.Count==0 && _state.Accounts.Count>0)_tabs.Add(new(){AccountId=_state.Accounts[0].Id});_active=0;_sessionSecrets.Remove(a.Id);if(OperatingSystem.IsWindows())new WindowsSecretStore(_store.DirectoryPath).Delete(a.Id);Save();CloseModal();Render();},true));
    }
    private void Connect()
    {
        var service=new ComboBox{ItemsSource=new[]{"Свой сервер · WebDAV","Google Диск","Яндекс Диск","OneDrive","Dropbox","Облако Mail.ru","iCloud Drive","MEGA","Свой сервер · SFTP","Свой сервер · S3","Свой сервер · SMB"},SelectedIndex=0};
        var name=new TextBox{Watermark="Например, Nextcloud дома"};var endpoint=new TextBox{Watermark="https://server/remote.php/dav/files/user/"};var user=new TextBox{Watermark="Имя пользователя"};var password=new TextBox{PasswordChar='●',Watermark="Пароль приложения"};var path=new TextBox{Watermark="По умолчанию — подпапка выбранного хранилища"};
        var adaptive=new CheckBox{Content="Проверять каталог при запуске с учётом активности",IsChecked=true};var local=new CheckBox{Content="Сохранять файлы локально",IsChecked=false};
        var remoteId = "rc_" + Guid.NewGuid().ToString("N");
        string[] backends = ["webdav","drive","yandex","onedrive","dropbox","mailru","iclouddrive","mega","sftp","s3","smb"];
        var loginLifetime = new CancellationTokenSource();
        var questions = new StackPanel { Spacing = 8 };
        bool signingIn = false;
        int signedService = -1;
        async Task<string> Ask(LoginQuestion question, CancellationToken token)
        {
            var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => result.TrySetCanceled(token));
            var label = question.Key switch
            {
                "client_id"=>"Client ID приложения Яндекса","client_secret"=>"Client secret приложения Яндекса","host" => "Адрес сервера", "user" => "Логин или почта", "port" => "Порт",
                "pass" or "password" => "Пароль", "key_file" => "Путь к SSH-ключу (необязательно)",
                "key_file_pass" => "Пароль SSH-ключа", "domain" => "Домен", "region" => "Регион",
                "endpoint" => "Адрес сервиса", "provider" => "Поставщик S3", "2fa" => "Код подтверждения",
                "config_2fa"=>"Код с доверенного устройства Apple", "config_2fa_sms"=>"Код из SMS", "config_2fa_phone"=>"Доверенный номер для SMS",
                "apple_id" => "Почта Apple Account", "access_key_id" => "Идентификатор ключа",
                "secret_access_key" => "Секретный ключ", "config_type" => "Тип диска OneDrive",
                "config_driveid" => "Выберите диск", _ => question.Help.Split('\n')[0]
            };
            var input = new TextBox { Text=question.DefaultValue };
            if (question.Secret) input.PasswordChar='●';
            var menu = new ComboBox { ItemsSource=question.Choices,SelectedItem=question.DefaultValue };
            bool list = question.Exclusive && question.Choices.Count>0;
            questions.Children.Clear();
            questions.Children.Add(Field(label,list ? menu : input));
            questions.Children.Add(Btn("Далее",() =>
            {
                var value=list ? menu.SelectedItem as string : input.Text;
                if (question.Required && string.IsNullOrEmpty(value)) {Notify("Заполните поле");return;}
                result.TrySetResult(value ?? "");
                input.Text="";
            },true));
            if(question.Key=="config_2fa")
                questions.Children.Add(Btn("Получить код по SMS",()=>result.TrySetResult("sms")));
            if (!list) input.Focus();
            return await result.Task;
        }
        var providers = new[]{"Свой сервер · WebDAV","Google Диск","Яндекс Диск","OneDrive","Dropbox","Облако Mail.ru","iCloud Drive","MEGA","Свой сервер · SFTP","Свой сервер · S3","Свой сервер · SMB"};
        var clouds = new Avalonia.Controls.Primitives.UniformGrid {Columns=3};
        var servers = new Avalonia.Controls.Primitives.UniformGrid {Columns=3};
        var picker=Stack(Text("Облачные сервисы",16,Ink,true),clouds,Text("Свой сервер",16,Ink,true),servers);
        var heading=Text("",22,Ink,true);
        var instructions=Text("",13,Muted);
        var status=Text("",12,Ink);
        var failure=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,IsVisible=false};
        var copy=AsyncBtn("Скопировать ошибку",async()=>{if(TopLevel.GetTopLevel(this)?.Clipboard is {} clipboard)await clipboard.SetTextAsync(failure.Text);});
        copy.IsVisible=false;
        var alternative=new CheckBox {Content="Использовать WebDAV с паролем приложения", IsVisible=false};
        var ownClient=new CheckBox{Content="Тест с собственным OAuth-приложением Яндекса",IsVisible=false};
        ownClient.IsCheckedChanged+=(_,_)=>signedService=-1;
        var davHelp=Stack(Text("Подтвердите вход в Яндекс ID и создайте пароль приложения для Диска. Вставьте его в поле ниже.",12,Muted),
            Btn("Открыть пароли приложений в Яндекс ID",()=>LaunchUrl("https://id.yandex.ru/security/app-passwords")));
        davHelp.IsVisible=false;
        var dav=Stack(Field("Адрес WebDAV",endpoint),Field("Логин",user),Field("Пароль приложения",password));
        var preferences=new Expander {Header="Название и локальные копии", Content=Stack(Field("Название аккаунта",name),Field("Локальная папка",path),adaptive,local)};
        var back=Btn("←  Все хранилища",()=>{});
        var page=Stack(back,heading,instructions,alternative,ownClient,davHelp,dav,questions,preferences,status,failure,copy);
        page.IsVisible=false;
        var confirm=AsyncBtn("Подключить",async()=>{
            if(signingIn)return;
            var index=service.SelectedIndex;
            LogEvent("Подключение · "+backends[index]+" · начало");
            signingIn=true;failure.IsVisible=copy.IsVisible=false;
            back.IsEnabled=false;alternative.IsEnabled=false;ownClient.IsEnabled=false;davHelp.IsEnabled=false;
            try
            {
                var useDav=index==0 || (index==2 && alternative.IsChecked==true);
                if(!useDav && signedService!=index)
                {
                    remoteId="rc_"+Guid.NewGuid().ToString("N");
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(loginLifetime.Token);
                    timeout.CancelAfter(TimeSpan.FromMinutes(10));
                    status.Text=index is >=1 and <=4?"Ожидаем подтверждения в браузере…":"Введите данные аккаунта";
                    await AccountLogin.ConfigureAsync(RcloneExe,RcloneConfig,remoteId,backends[index],Ask,timeout.Token,index==2 && ownClient.IsChecked==true,LogEvent);
                    signedService=index;LogEvent("Авторизация · "+backends[index]+" · завершена");
                }
                questions.Children.Clear();
                status.Text="Проверяем доступ к файлам…";
            
            var a=new Account{Provider=ProviderKind.WebDav,Name=ValidName(name.Text),Endpoint=endpoint.Text?.Trim()??"",UserName=user.Text??"",LocalFolder=path.Text?.Trim()??"",AdaptiveRefresh=adaptive.IsChecked==true,SaveLocally=local.IsChecked==true,Service="Свой сервер"};
            if (useDav && index==2) { a.Endpoint="https://webdav.yandex.ru/";a.Service="Яндекс Диск"; }
            if (!useDav)
            {
                a.Provider = ProviderKind.Rclone;
                a.Endpoint = remoteId;
                a.Service = service.SelectedItem?.ToString() ?? "Облако";
                a.UserName = "";
            }
            using IAccountProvider provider = a.Provider == ProviderKind.Rclone
                ? new RcloneProvider(a, RcloneExe, RcloneConfig,LogEvent) : new WebDavProvider(a,password.Text??"");
            LogEvent($"Подключение · account={a.Id} · проверка корневого каталога");
            await provider.ListAsync("",CancellationToken.None);
            await RefreshQuota(a,provider);
            if(string.IsNullOrWhiteSpace(a.LocalFolder))a.LocalFolder=AccountFolder(_state.LocalRoot,a);
            if(a.Service=="Google Диск")a.IconLayout=IconLayout.ServiceWithAvatar;
            _state.Accounts.Add(a);if(_tabs.Count==0)_tabs.Add(new(){AccountId=a.Id});_sessionSecrets[a.Id]=password.Text??"";if(a.Provider==ProviderKind.WebDav && OperatingSystem.IsWindows())new WindowsSecretStore(_store.DirectoryPath).Save(a.Id,password.Text??"");Save();CloseModal();Navigate(a.Id,"");await Refresh(a);
            }
            catch(Exception e) {LogEvent("Подключение · "+backends[index]+" · "+(index==6 && e.Message.StartsWith("iCloud ·",StringComparison.Ordinal)?e.Message:ProviderError.Describe(backends[index],"Ошибка",e.Message,1)));status.Text="Подключение не завершено";failure.Text=e.Message;failure.IsVisible=copy.IsVisible=true;}
            finally {signingIn=false;back.IsEnabled=true;alternative.IsEnabled=true;ownClient.IsEnabled=true;davHelp.IsEnabled=true;questions.Children.Clear();}
        },true);
        page.Children.Add(confirm);
        davHelp.Children.Add(AsyncBtn("Войти с двухфакторным подтверждением через браузер",()=>{
            alternative.IsChecked=false;signedService=-1;
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return Task.CompletedTask;
        }));
        alternative.IsCheckedChanged+=(_,_)=>{
            if(service.SelectedIndex!=2)return;
            bool useDav=alternative.IsChecked==true;
            ownClient.IsVisible=!useDav;davHelp.IsVisible=useDav;dav.IsVisible=useDav;endpoint.Text=useDav?"https://webdav.yandex.ru/":"";
            endpoint.IsReadOnly=useDav;
            confirm.Content=useDav?"Подключить":"Войти через браузер";
            instructions.Text=useDav?"Введите логин Яндекса и пароль приложения для Диска.":"Разрешите доступ в браузере.";
        };
        back.Click+=(_,_)=>{page.IsVisible=false;picker.IsVisible=true;Notify("");status.Text="";signedService=-1;password.Text="";failure.IsVisible=copy.IsVisible=false;};
        for(int i=0;i<providers.Length;i++)
        {
            int index=i;
            var tile=Btn(providers[i],()=>{
                alternative.IsChecked=false;alternative.IsVisible=index==2;ownClient.IsChecked=false;ownClient.IsVisible=index==2;davHelp.IsVisible=false;endpoint.IsReadOnly=false;endpoint.Text="";
                service.SelectedIndex=index;heading.Text=providers[index];name.Text=providers[index];
                instructions.Text=index switch {0=>"Укажите адрес сервера и данные для доступа к файлам.",
                    6=>"Для iCloud нужен обычный пароль Apple Account и код с доверенного устройства.",
                    >=1 and <=4=>"Разрешите доступ к файлам в браузере. Затем RCH автоматически проверит диск.",
                    _=>"Введите данные подключения в этом окне."};
                dav.IsVisible=index==0;picker.IsVisible=false;page.IsVisible=true;
                confirm.Content=index is >=1 and <=4?"Войти через браузер":"Подключить";
            });
            tile.Content=Row(ServiceMark.Create(providers[index],31),Text(providers[index].Replace("Свой сервер · ",""),14,Ink,true));
            tile.Background=Surface;tile.BorderBrush=Line;tile.BorderThickness=new Thickness(1);
            tile.Margin=new Thickness(4);tile.MinHeight=54;tile.HorizontalAlignment=HorizontalAlignment.Stretch;
            tile.HorizontalContentAlignment=HorizontalAlignment.Left;(index is >=1 and <=7?clouds:servers).Children.Add(tile);
        }
        var body=Stack(picker,page);
        body.Unloaded+=(_,_)=>loginLifetime.Cancel();
        ShowModal("Добавить хранилище",body);
    }
    private void Guide()
    {
        ShowModal("Развернуть своё хранилище",Stack(Field("Операционная система",new ComboBox{ItemsSource=new[]{"Windows Server","Ubuntu","Debian","Fedora","Arch Linux"},SelectedIndex=1}),Text("Пошаговые гайды под выбранную ОС ещё готовятся. Официальные сайты:",12,Muted),Btn("Nextcloud · официальный сайт",()=>LaunchUrl("https://nextcloud.com/install/")),Btn("ownCloud · официальный сайт",()=>LaunchUrl("https://owncloud.com/download/")),Btn("Seafile · официальный сайт",()=>LaunchUrl("https://www.seafile.com/")),Text("RCH не устанавливает сервер автоматически и не подключается к нему по SSH для установки.",11,Muted)));
    }
    private static void LaunchUrl(string url)=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
    private async Task StorageDialog()
    {
        Notify("Подсчёт локальных файлов…");var categories=(await Task.Run(()=>StorageStatistics.Read(_state,_store.DirectoryPath))).ToList();
        var chart=new StorageRing{Width=260,Height=260,Categories=categories};var legend=new StackPanel{Spacing=12};
        foreach(var c in categories)legend.Children.Add(Row(new Border{Width=9,Height=9,CornerRadius=new CornerRadius(3),Background=B(c.Color)},Text(c.Name,11),Text(Format.Bytes(c.Bytes),11,Muted)));
        var total=categories.Sum(c=>c.Bytes);var summary=Stack(Text("Общее хранилище",18,Ink,true),Text("Данные RCH: "+Format.Bytes(total),12,Muted));
        var drives=_state.Accounts.Where(a=>!a.IsDemo&&!string.IsNullOrWhiteSpace(a.LocalFolder)).Select(a=>Path.GetPathRoot(Path.GetFullPath(a.LocalFolder))).Append(Path.GetPathRoot(_store.DirectoryPath)).Where(p=>p is not null).Distinct(StringComparer.OrdinalIgnoreCase).Select(p=>new DriveInfo(p!)).Where(d=>d.IsReady).ToList();
        long capacity=drives.Sum(d=>d.TotalSize),free=drives.Sum(d=>d.AvailableFreeSpace);var used=Math.Max(0,capacity-free);var bar=new Grid{Height=7,Background=Surface,ColumnDefinitions=new ColumnDefinitions(string.Join(',',categories.Select(c=>Math.Max(1,c.Bytes).ToString()+"*").Concat(new[]{Math.Max(1,used-total)+"*",Math.Max(1,free)+"*"})))};
        for(int i=0;i<categories.Count;i++){var segment=new Border{Background=B(categories[i].Color)};Grid.SetColumn(segment,i);bar.Children.Add(segment);}var other=new Border{Background=B("#CED6E2")};Grid.SetColumn(other,categories.Count);bar.Children.Add(other);
        summary.Children.Add(bar);summary.Children.Add(Text("Занято: "+Format.Bytes(used)+" · Свободно: "+Format.Bytes(free)+" · Всего: "+Format.Bytes(capacity),11,Muted));summary.Children.Add(Row(chart,legend));summary.Children.Add(Text("Круг: данные RCH без свободного места. Полоска также учитывает остальные файлы на используемых дисках.",10,Muted));
        summary.Children.Add(Btn("Очистить кэш вручную",()=>{var cache=Path.Combine(_store.DirectoryPath,"cache");if(Directory.Exists(cache))Directory.Delete(cache,true);Notify("Кэш очищен. Скачанные файлы не затронуты.");CloseModal();}));
        summary.Children.Add(Text("Кэш без лимита. Автоматической очистки нет.",11,Muted));
        chart.CategoryClicked+=index=>{if(index>=categories.Count)return;var c=categories[index];var a=_state.Accounts.FirstOrDefault(a=>a.Title==c.Name);if(a is not null)LocalFileList(a);};ShowModal("Место на компьютере",summary);
    }
    private void LocalFileList(Account a)
    {
        var list=new StackPanel{Spacing=8};foreach(var f in _state.Entries.Where(f=>f.AccountId==a.Id&&!f.IsFolder&&f.Downloaded is not null).ToList())
        {
            var path=Paths.LocalPath(a.LocalFolder,f.Path);if(!File.Exists(path))continue;
            list.Children.Add(Row(Text(f.Name+" · "+Format.Bytes(new FileInfo(path).Length),12),Btn("Удалить только с ПК",()=>{ShowModal("Удалить локальную копию?",Text(f.Name+"\nОригинал в облаке сохранится. Автоматически файл скачиваться повторно не будет.",13),Btn("Удалить с ПК",()=>{File.Delete(path);f.Status=LocalStatus.RemovedFromDevice;f.SuppressAutoDownload=true;Save();LocalFileList(a);},true));})));
        }
        if(list.Children.Count==0)list.Children.Add(Text("Нет скачанных копий, зарегистрированных RCH",12,Muted));ShowModal(a.Title+" · локальные файлы",list);
    }
    private sealed class Job(string name,string route)
    {
        public string Name { get; }=name;
        public string Route { get; }=route;
        public double Percent { get; set; }
        public string State { get; set; }="В очереди";
        public CancellationTokenSource Token { get; }=new();
    }
}
