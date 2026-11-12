using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Web.WebView2.Core;
using System.Drawing;
using System.Runtime.InteropServices;

namespace RCHub.App;
internal sealed class CloudWebView(Uri location,string profile,Action<string> showError):NativeControlHost
{
    private CoreWebView2Controller? controller;
    private int generation;
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr handle,out NativeRect bounds);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
    private IntPtr window;
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle=base.CreateNativeControlCore(parent);
        window=handle.Handle;
        var ticket=++generation;
        Dispatcher.UIThread.Post(()=>Start(ticket));
        return handle;
    }
    private async void Start(int ticket)
    {
        try{
            if(ticket!=generation)return;
            var environment=await CoreWebView2Environment.CreateAsync(null,profile);
            if(ticket!=generation)return;
            var created=await environment.CreateCoreWebView2ControllerAsync(window);
            if(ticket!=generation){created.Close();return;}
            controller=created;
            var web=created.CoreWebView2;
            web.Settings.IsWebMessageEnabled=false;
            web.Settings.AreHostObjectsAllowed=false;
            web.NavigationStarting+=(_,e)=>{if(!Uri.TryCreate(e.Uri,UriKind.Absolute,out var uri)||uri.Scheme!="https")e.Cancel=true;};
            web.NewWindowRequested+=(_,e)=>{e.Handled=true;if(Uri.TryCreate(e.Uri,UriKind.Absolute,out var uri)&&uri.Scheme=="https")web.Navigate(uri.AbsoluteUri);};
            created.IsVisible=true;
            UpdateSize();web.Navigate(location.AbsoluteUri);
        }catch(Exception){if(ticket==generation)showError("Не удалось запустить веб-просмотр. Откройте страницу в браузере или проверьте установку Microsoft Edge WebView2 Runtime.");}
    }
    public void UpdateSize()
    {
        if(controller!=null && GetClientRect(window,out var r))
            controller.Bounds=new Rectangle(0,0,r.Right,r.Bottom);
    }
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        generation++;controller?.Close();controller=null;window=IntPtr.Zero;
        base.DestroyNativeControlCore(control);
    }
}
