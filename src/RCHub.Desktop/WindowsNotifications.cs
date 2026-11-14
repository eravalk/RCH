using System.Runtime.InteropServices;
namespace RCHub.Desktop;
internal sealed class WindowsNotifications(IntPtr window):IDisposable
{
    private bool added;private int generation;
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct IconData
    {
        public uint Size;public IntPtr Window;public uint Id,Flags,Callback;public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip;
        public uint State,StateMask;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string Title;
        public uint InfoFlags;public Guid Guid;public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern bool Shell_NotifyIcon(uint message,ref IconData data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr LoadIcon(IntPtr instance,IntPtr name);
    private IconData Data()=>new(){Size=(uint)Marshal.SizeOf<IconData>(),Window=window,Id=8741,Tip="RCH",Info="",Title=""};
    public async void Show(string title,string text)
    {
        if(!OperatingSystem.IsWindows())return;
        var ticket=++generation;var data=Data();data.Flags=2|4|16;data.Icon=LoadIcon(IntPtr.Zero,new IntPtr(32512));
        data.Info=text.Length>255?text[..255]:text;data.Title=title.Length>63?title[..63]:title;data.InfoFlags=1;data.Timeout=10000;
        added=Shell_NotifyIcon(added?1u:0u,ref data);
        await Task.Delay(15000);if(ticket==generation)Dispose();
    }
    public void Dispose(){generation++;if(!added||!OperatingSystem.IsWindows())return;var data=Data();Shell_NotifyIcon(2,ref data);added=false;}
}
