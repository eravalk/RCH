using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RCHub.Core;
namespace RCHub.App;
public sealed partial class MainView
{
    private readonly Dictionary<string,CancellationTokenSource> _refreshing=new();
    private readonly Dictionary<string,DateTimeOffset> _lastAttempt=new();
    private DispatcherTimer? _refreshTimer;
    private int _activeTransfers;
    private bool _checkingDue;
    public Action<string,string>? DesktopNotification {get;set;}
    private void SystemNotice(string title,string body){if(_state.Settings.Notifications)DesktopNotification?.Invoke(title,body);}
    private void StartRefreshTimer()
    {
        if(_refreshTimer!=null)return;
        _refreshTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(30)};
        _refreshTimer.Tick+=async(_,_)=>await RefreshDueAccounts();_refreshTimer.Start();
        DetachedFromVisualTree+=(_,_)=>{_refreshTimer.Stop();foreach(var token in _refreshing.Values)token.Cancel();};
    }
    private async Task RefreshDueAccounts()
    {
        if(_checkingDue)return;_checkingDue=true;
        try{
        var due=_state.Accounts.Where(a=>!a.IsDemo && RefreshPolicy.Due(a,DateTimeOffset.Now) && !_refreshing.ContainsKey(a.Id)
            && (!_lastAttempt.TryGetValue(a.Id,out var last)||DateTimeOffset.Now-last>TimeSpan.FromMinutes(1))).ToArray();
        using var gates=new SemaphoreSlim(Math.Clamp(_state.Settings.AccountConcurrency,1,4));
        await Task.WhenAll(due.Select(async account=>
        {
            await gates.WaitAsync();
            try{await Refresh(account);}
            catch(OperationCanceledException){}
            catch(Exception error){LogEvent(SafeDiagnostic.Exception(error));Notify(account.Title+" · "+error.Message);}
            finally{gates.Release();}
        }));
        }finally{_checkingDue=false;}
    }
    private async Task Refresh(Account account)
    {
        if(_refreshing.ContainsKey(account.Id))return;
        if(account.IsDemo){account.LastCheck=DateTimeOffset.Now;Save();RefreshCatalogView();return;}
        using var cancellation=new CancellationTokenSource();_refreshing.Add(account.Id,cancellation);_lastAttempt[account.Id]=DateTimeOffset.Now;
        var scannedPaths=new HashSet<string>(StringComparer.Ordinal);var ct=cancellation.Token;var started=System.Diagnostics.Stopwatch.StartNew();
        var operation=Guid.NewGuid().ToString("N");var known=_state.Entries.Where(f=>f.AccountId==account.Id).Select(f=>f.Path).ToHashSet(StringComparer.Ordinal);
        account.Activity="Чтение каталога";Notify(account.Title+" · "+account.Activity);
        try
        {
            using var provider=Provider(account);
            var scan=await new CatalogService().ScanAsync(provider,null,ct,_state.Settings.ScanConcurrency,_state.Settings.RetryCount,batch=>
            {
                if(!_state.Accounts.Contains(account)){cancellation.Cancel();ct.ThrowIfCancellationRequested();}
                foreach(var file in batch)if(known.Add(file.Path))_state.Entries.Add(file);
                foreach(var file in batch)scannedPaths.Add(file.Path);account.Activity="Чтение каталога · найдено "+scannedPaths.Count;UpdateSidebar(account);
                _message.Text=account.Title+" · "+account.Activity;
                if(_state.Accounts.Contains(account))RefreshCatalogView();
            },account.ExcludedFolders);
            ct.ThrowIfCancellationRequested();if(!_state.Accounts.Contains(account))return;
            CatalogService.Merge(_state,account,scan);Save();RefreshCatalogView();
            account.Activity="Каталог обновлён · "+scan.Entries.Count+" объектов";
            Notify(account.Title+" · "+account.Activity+(scan.FailedFolders.Count>0?" · не прочитано папок: "+scan.FailedFolders.Count:""));
            account.Activity="";UpdateSidebar(account);
            LogEvent($"Индексация · account={account.Id} op={operation} · объектов {scan.Entries.Count} · папок с ошибкой {scan.FailedFolders.Count} · {started.Elapsed.TotalSeconds:0.0} с");
            // Quota/profile and local copies no longer block showing files or scanning the next disk.
            _ = Run(()=>RefreshAccountMetadata(account));
            _ = Run(()=>SaveLocalCopies(account));
        }
        catch(OperationCanceledException){account.Activity="Проверка отменена";Notify(account.Title+" · "+account.Activity);throw;}
        catch(Exception error)
        {
            account.CatalogError="Ошибка чтения каталога: "+error.Message;account.Activity="Не удалось обновить";
            Save();Notify(account.Title+" · "+account.CatalogError);LogEvent($"Индексация · account={account.Id} op={operation} · "+SafeDiagnostic.Exception(error));throw;
        }
        finally{_refreshing.Remove(account.Id);}
    }
    private async Task RefreshAccountMetadata(Account account)
    {
        using var provider=Provider(account);await RefreshQuota(account,provider);
        if(account.Service=="Google Диск")try{await GoogleProfile.LoadAsync(account,RcloneConfig,Path.Combine(_store.DirectoryPath,"icons"));Save();}catch(Exception error){LogEvent(SafeDiagnostic.Exception(error));}
        UpdateSidebar(account);
    }
    private void UpdateSidebar(Account account)
    {
        foreach(var text in this.GetVisualDescendants().OfType<TextBlock>())
        {
            if(Equals(text.Tag,"quota:"+account.Id)){text.Text=account.CapacityLabel;ToolTip.SetTip(text,account.QuotaDescription);}
            if(Equals(text.Tag,"status:"+account.Id))text.Text=account.AdaptiveRefresh?account.CheckLabel:"Вручную · "+account.CheckLabel;
        }
        foreach(var bar in this.GetVisualDescendants().OfType<ProgressBar>().Where(b=>Equals(b.Tag,"quota:"+account.Id)))
        {bar.Value=account.UsedPercent;bar.IsVisible=account.QuotaKnown;}
    }
    private void RefreshCatalogView()
    {
        if(_modal!=null||_tabs.Count==0)return;
        var selected=_selected?.Id;
        if(_fileArea.Children.OfType<DataGrid>().FirstOrDefault() is {} table && table.ItemsSource is ObservableCollection<CloudEntry> items)
        {
            var desired=Visible().ToList();var wanted=desired.Select(f=>f.Id).ToHashSet();
            var bar=table.GetVisualDescendants().OfType<ScrollBar>().FirstOrDefault(s=>s.Orientation==Orientation.Vertical);var offset=bar?.Value;
            for(int i=items.Count-1;i>=0;i--)if(!wanted.Contains(items[i].Id))items.RemoveAt(i);
            var indexes=items.Select((f,i)=>(f.Id,i)).ToDictionary(x=>x.Id,x=>x.i);
            foreach(var file in desired)
                if(indexes.TryGetValue(file.Id,out var i)){if(!ReferenceEquals(items[i],file))items[i]=file;}else items.Add(file);
            if(selected!=null)table.SelectedItem=items.FirstOrDefault(f=>f.Id==selected);
            if(bar!=null&&offset.HasValue)Dispatcher.UIThread.Post(()=>bar.Value=offset.Value);
        }
        else RenderFiles();
    }
    private async Task WaitForTransfer(CancellationToken jobToken)
    {
        while(_activeTransfers>=Math.Clamp(_state.Settings.TransferConcurrency,1,8))await Task.Delay(100,jobToken);
        _activeTransfers++;
    }
}
