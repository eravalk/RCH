namespace RCHub.Core;
public static class DemoData
{
    public static AppState Create()
    {
        var state = new AppState();
        state.Accounts.AddRange([
            new() { Id="demo-yandex", Provider=ProviderKind.Demo, Service="Яндекс Диск",Name="Работа",Group="Работа",UsedBytes=312_000_000_000,TotalBytes=1_000_000_000_000,LastCheck=DateTimeOffset.Now },
            new() { Id="demo-google", Provider=ProviderKind.Demo, Service="Google Диск",Name="Личное",Group="Личное",UsedBytes=48_000_000_000,TotalBytes=200_000_000_000,LastCheck=DateTimeOffset.Now },
            new() { Id="demo-archive", Provider=ProviderKind.Demo, Service="Google Диск",Name="Архив",Group="Архив",UsedBytes=612_000_000_000,TotalBytes=2_000_000_000_000,AdaptiveRefresh=false,LastCheck=DateTimeOffset.Now.AddDays(-5) },
            new() { Id="demo-server", Provider=ProviderKind.Demo, Service="Свой сервер",Name="Домашний",Group="Личное",UsedBytes=1_100_000_000_000,TotalBytes=4_000_000_000_000,LastCheck=DateTimeOffset.Now }]);
        var names = new (string Name,long Size,LocalStatus Status)[] {("Отчёт.pdf",4_800_000,LocalStatus.Current),("План проекта.docx",1_200_000,LocalStatus.Current),("Бюджет.xlsx",2_300_000,LocalStatus.Mismatch),("Setup_RC-Tools.exe",28_000_000,LocalStatus.NewVersion),("Материалы.zip",356_000_000,LocalStatus.CloudOnly),("Озеро в горах.jpg",5_800_000,LocalStatus.Current),("Заметки.txt",12_000,LocalStatus.RemovedFromDevice)};
        foreach (var a in state.Accounts)
        {
            foreach (var folder in new[] {"Документы","Фото","Архив"}) state.Entries.Add(new() { AccountId=a.Id,Name=folder,Path=folder,IsFolder=true,Modified=DateTimeOffset.Now.AddDays(-1) });
            foreach (var (name,size,status) in names) state.Entries.Add(new() { AccountId=a.Id,Name=name,Path=name,Size=size,Status=status,Modified=DateTimeOffset.Now.AddHours(-2),Downloaded=status==LocalStatus.CloudOnly?null:DateTimeOffset.Now.AddDays(-1),IsFavorite=name=="Отчёт.pdf" });
            state.Entries.Add(new() {AccountId=a.Id,Name="Смета.xlsx",Path="Документы/Смета.xlsx",Size=840_000,Status=LocalStatus.Mismatch,Modified=DateTimeOffset.Now});
            state.Entries.Add(new() {AccountId=a.Id,Name="Путешествие.jpg",Path="Фото/Путешествие.jpg",Size=4_200_000,Status=LocalStatus.CloudOnly,Modified=DateTimeOffset.Now});
        }
        return state;
    }
}
