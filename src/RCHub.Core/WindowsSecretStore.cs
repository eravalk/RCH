using System.Runtime.InteropServices;
using System.Text;
namespace RCHub.Core;
// Desktop adapter. Never referenced by shared catalog, UI models or future Android code.
public sealed class WindowsSecretStore(string directory)
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError=true, CharSet=CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static byte[] Transform(byte[] bytes, bool encrypt)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Защищённое сохранение паролей в этой сборке доступно только в Windows");
        var input = new Blob { Size=bytes.Length, Data=Marshal.AllocHGlobal(bytes.Length) }; Blob output=default;
        try
        {
            Marshal.Copy(bytes,0,input.Data,bytes.Length);
            var success = encrypt ? CryptProtectData(ref input,"RC Hub",IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output) : CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if (!success) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Size]; Marshal.Copy(output.Data,result,0,output.Size); return result;
        }
        finally { Marshal.FreeHGlobal(input.Data); if(output.Data!=IntPtr.Zero)LocalFree(output.Data); }
    }
    private string PathFor(string id)
    {
        if (id.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-')) throw new ArgumentException("Недопустимый ID");
        return Path.Combine(directory,"credentials",id+".bin");
    }
    public void Save(string id,string secret) { var path=PathFor(id); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path,Transform(Encoding.UTF8.GetBytes(secret),true)); }
    public string? Read(string id) { var path=PathFor(id);return File.Exists(path)?Encoding.UTF8.GetString(Transform(File.ReadAllBytes(path),false)):null; }
    public void Delete(string id) { var path=PathFor(id);if(File.Exists(path))File.Delete(path); }
}
