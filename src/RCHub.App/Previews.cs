using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using RCHub.Core;
namespace RCHub.App;
public sealed partial class MainView
{
    private readonly SemaphoreSlim _previewSlots=new(2);
    private readonly SemaphoreSlim _selectionPreviewSlot=new(1);
    private PreviewCache? _previewCache;
    private PreviewCache Cache=>_previewCache??=new(Path.Combine(_store.DirectoryPath,"cache","previews"));
    private async Task<string> PreviewPath(CloudEntry entry,CancellationToken ct,bool priority=false)
    {
        var account=_state.Accounts.First(a=>a.Id==entry.AccountId);
        if(!string.IsNullOrWhiteSpace(account.LocalFolder))
        {
            var local=Paths.LocalPath(account.LocalFolder,entry.Path);
            if(File.Exists(local)&&entry.Status==LocalStatus.Current)return local;
        }
        var slots=priority?_selectionPreviewSlot:_previewSlots;await slots.WaitAsync(ct);
        try{using var provider=Provider(account);return await Cache.GetAsync(account,entry,provider,_state.Settings.PreviewFileBytes,_state.Settings.PreviewCacheBytes,ct);}
        finally{slots.Release();}
    }
    private Control Thumbnail(CloudEntry entry,double size)
    {
        var fallback=FileSymbol.Create(entry,size);
        var extension=Path.GetExtension(entry.Name).ToLowerInvariant();
        if(!entry.IsFolder&&_state.Settings.CustomFileIcons.TryGetValue(extension,out var custom)&&File.Exists(custom))
            return new PreviewVisual(fallback,_=>Task.FromResult(custom),Math.Max(96,(int)(size*2))){Width=size,Height=size};
        if(!_state.Settings.ShowThumbnails||entry.IsFolder||!PreviewCache.IsImage(entry.Name))return fallback;
        return new PreviewVisual(fallback,ct=>PreviewPath(entry,ct),Math.Max(96,(int)(size*2))){Width=size,Height=size};
    }
    private Control CloudPreview(CloudEntry entry)
    {
        if(entry.IsFolder)return Card(FileSymbol.Create(entry,80),new Thickness(35));
        if(PreviewCache.IsImage(entry.Name))
        {
            var image=new PreviewVisual(Card(FileSymbol.Create(entry,80)),ct=>PreviewPath(entry,ct,true),800,true){MinHeight=130};
            image.DoubleTapped+=(_,_)=>_ = Run(()=>ImageViewer(entry));
            return Stack(image,AsyncBtn("Открыть изображение · масштаб",()=>ImageViewer(entry)));
        }
        if(PreviewCache.IsText(entry.Name))
        {
            var text=new TextBox{Text="Загрузка текста…",IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxHeight=280};
            var token=new CancellationTokenSource();text.DetachedFromVisualTree+=(_,_)=>token.Cancel();
            _ = ReadText();
            async Task ReadText()
            {
                try{var path=await PreviewPath(entry,token.Token,true);using var reader=new StreamReader(path);var buffer=new char[50000];var count=await reader.ReadBlockAsync(buffer.AsMemory(),token.Token);text.Text=new string(buffer,0,count)+(count<buffer.Length?"":"\n… Показаны первые 50 000 символов");}
                catch(OperationCanceledException){}catch(Exception error){text.Text="Не удалось получить текст: "+error.Message;}
            }
            return text;
        }
        if(Path.GetExtension(entry.Name).Equals(".pdf",StringComparison.OrdinalIgnoreCase))return Stack(FileSymbol.Create(entry,80),AsyncBtn("Открыть PDF",()=>PdfViewer(entry)));
        return Card(Thumbnail(entry,80),new Thickness(35));
    }
    private async Task ImageViewer(CloudEntry first)
    {
        var images=Visible().Where(f=>!f.IsFolder&&PreviewCache.IsImage(f.Name)).ToList();
        if(!images.Any(f=>f.Id==first.Id))images.Add(first);
        var index=images.FindIndex(f=>f.Id==first.Id);Bitmap? bitmap=null;
        var lifetime=new CancellationTokenSource();var generation=0;
        var picture=new Image{Stretch=Stretch.Uniform};var scroll=new ScrollViewer{Content=picture,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto};
        var title=Text("Загрузка…",13);var zoom=new Slider{Minimum=10,Maximum=400,Value=100,Width=200};
        var viewer=new Window{Title=first.Name+" · RCH",Width=1050,Height=780,Background=Paper};
        void Scale(){if(bitmap!=null){picture.Width=bitmap.Size.Width*zoom.Value/100;picture.Height=bitmap.Size.Height*zoom.Value/100;}}
        zoom.PropertyChanged+=(_,e)=>{if(e.Property==Slider.ValueProperty)Scale();};
        async Task Load(int next)
        {
            index=(next+images.Count)%images.Count;var ticket=++generation;var file=images[index];title.Text="Загрузка: "+file.Name;
            try
            {
                var path=await PreviewPath(file,lifetime.Token,true);if(ticket!=generation||lifetime.IsCancellationRequested)return;
                using var input=File.OpenRead(path);var nextBitmap=Bitmap.DecodeToWidth(input,2048);
                bitmap?.Dispose();bitmap=nextBitmap;picture.Source=bitmap;zoom.Value=Math.Clamp(Math.Min((viewer.ClientSize.Width-40)/bitmap.Size.Width,(viewer.ClientSize.Height-90)/bitmap.Size.Height)*100,10,100);Scale();scroll.Offset=default;
                title.Text=$"{index+1} / {images.Count} · {file.Name}";viewer.Title=file.Name+" · RCH";
            }catch(OperationCanceledException){}catch(Exception error){if(ticket==generation)title.Text=error.Message;}
        }
        var bar=Row(AsyncBtn("←",()=>Load(index-1)),AsyncBtn("→",()=>Load(index+1)),title,Text("Масштаб"),zoom);
        var grid=new Grid{RowDefinitions=new RowDefinitions("Auto,*")};grid.Children.Add(bar);Grid.SetRow(scroll,1);grid.Children.Add(scroll);viewer.Content=grid;
        Point? drag=null;Vector origin=default;
        picture.PointerPressed+=(_,e)=>{if(e.GetCurrentPoint(picture).Properties.IsLeftButtonPressed){drag=e.GetPosition(scroll);origin=scroll.Offset;e.Pointer.Capture(picture);}};
        picture.PointerMoved+=(_,e)=>{if(drag is {} point)scroll.Offset=origin-(e.GetPosition(scroll)-point);};
        picture.PointerReleased+=(_,e)=>{drag=null;e.Pointer.Capture(null);};
        viewer.KeyDown+=(_,e)=>{if(e.Key==Key.Left)_ = Load(index-1);if(e.Key==Key.Right)_ = Load(index+1);if(e.Key==Key.Escape)viewer.Close();};
        viewer.Closed+=(_,_)=>{lifetime.Cancel();picture.Source=null;bitmap?.Dispose();};
        if(TopLevel.GetTopLevel(this) is Window owner)viewer.Show(owner);else viewer.Show();await Load(index);
    }
    private async Task PdfViewer(CloudEntry entry)
    {
        var path=await PreviewPath(entry,CancellationToken.None,true);
        var message=Text("",12);var host=new CloudWebView(new Uri("https://rch-preview.local/"+Uri.EscapeDataString(Path.GetFileName(path))),Path.Combine(_store.DirectoryPath,"web-profiles","pdf"),error=>message.Text=error,Path.GetDirectoryName(path));
        host.SizeChanged+=(_,_)=>Avalonia.Threading.Dispatcher.UIThread.Post(host.UpdateSize);
        var content=new Grid{RowDefinitions=new RowDefinitions("Auto,*")};content.Children.Add(message);Grid.SetRow(host,1);content.Children.Add(host);
        var viewer=new Window{Title=entry.Name+" · PDF · RCH",Width=1000,Height=760,Content=content};
        if(TopLevel.GetTopLevel(this) is Window owner)viewer.Show(owner);else viewer.Show();
    }
}
