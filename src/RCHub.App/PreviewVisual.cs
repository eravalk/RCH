using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
namespace RCHub.App;
// Load thumbnails only when their control enters the visible viewport.
internal sealed class PreviewVisual:Border
{
    private readonly Func<CancellationToken,Task<string>> load;
    private readonly int pixels;
    private readonly bool showErrors;
    private CancellationTokenSource? lifetime;
    private Bitmap? bitmap;
    private bool started;
    public PreviewVisual(Control fallback,Func<CancellationToken,Task<string>> load,int pixels=256,bool showErrors=false)
    {
        Child=fallback;this.load=load;this.pixels=pixels;this.showErrors=showErrors;
        EffectiveViewportChanged+=(_,e)=>{if(!started&&e.EffectiveViewport.Intersects(new Rect(Bounds.Size)))Begin();};
        DetachedFromVisualTree+=(_,_)=>{lifetime?.Cancel();bitmap?.Dispose();bitmap=null;started=false;};
    }
    private async void Begin()
    {
        started=true;var request=new CancellationTokenSource();lifetime=request;var ct=request.Token;
        try
        {
            var path=await load(ct);ct.ThrowIfCancellationRequested();if(!ReferenceEquals(request,lifetime))return;
            using var input=File.OpenRead(path);bitmap=Bitmap.DecodeToWidth(input,pixels);
            Child=new Image{Source=bitmap,Stretch=Stretch.Uniform,MaxHeight=280};
        }
        catch(OperationCanceledException){}
        catch(Exception error){ToolTip.SetTip(this,"Предпросмотр недоступен: "+error.Message);if(showErrors)Child=new TextBlock{Text="Предпросмотр недоступен: "+error.Message,TextWrapping=TextWrapping.Wrap,FontSize=12};}
    }
}
