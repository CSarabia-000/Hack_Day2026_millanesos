using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Interop;

namespace Mindie.Services;

internal static class ShellBranding
{
    internal const string AppId="Mindie.Desktop";
    internal static string? IconPath { get; private set; }
    internal static string PrepareIcon(string folder)
    {
        using var stream=System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Mindie.ico")).Stream;
        using var buffer=new MemoryStream(); stream.CopyTo(buffer); var bytes=buffer.ToArray();
        var name="mindie-orb-"+Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant()+".ico";
        Directory.CreateDirectory(folder); var path=Path.Combine(folder,name);
        if(!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) File.WriteAllBytes(path,bytes);
        return path;
    }
    internal static void Initialize()
    {
        try {
            IconPath=PrepareIcon(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Mindie","branding"));
            if(Environment.ProcessPath is not string executable) return;
            var pinned=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Microsoft","Internet Explorer","Quick Launch","User Pinned","TaskBar");
            if(Directory.Exists(pinned)) foreach(var file in Directory.EnumerateFiles(pinned,"*.lnk",SearchOption.TopDirectoryOnly)) {
                try { RepairShortcut(file,executable,IconPath); }
                catch(Exception ex) when(ex is COMException or IOException or UnauthorizedAccessException or ArgumentException) { /* A stale shortcut must not stop the app. */ }
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or COMException) { IconPath=null; }
    }
    internal static bool RepairShortcut(string shortcut,string executable,string icon)
    {
        if(!File.Exists(shortcut) || !File.Exists(icon)) return false;
        var type=Type.GetTypeFromProgID("WScript.Shell"); if(type is null) return false;
        object shell=Activator.CreateInstance(type)!; object? link=null;
        try {
            link=((dynamic)shell).CreateShortcut(shortcut);
            var target=(string)((dynamic)link).TargetPath;
            if(string.IsNullOrWhiteSpace(target) || !Path.GetFullPath(target).Equals(Path.GetFullPath(executable),StringComparison.OrdinalIgnoreCase)) return false;
            var desired=icon+",0";
            if(string.Equals((string)((dynamic)link).IconLocation,desired,StringComparison.OrdinalIgnoreCase)) return false;
            ((dynamic)link).IconLocation=desired; ((dynamic)link).Save();
            NotifyItem(shortcut); return true;
        }
        finally { if(link is not null) Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
    }
    internal static void Attach(Window window)
    {
        window.SourceInitialized+=(_,_)=> {
            if(IconPath is not string icon || !window.ShowInTaskbar || Environment.ProcessPath is not string executable) return;
            try { ApplyWindow(new WindowInteropHelper(window).Handle,executable,icon); }
            catch(COMException) { /* The native/WPF icon remains available if shell properties fail. */ }
        };
    }
    internal static void ApplyWindow(IntPtr hwnd,string executable,string icon)
    {
        var guid=typeof(IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd,ref guid,out var store));
        try {
            Set(store,2,"\""+executable+"\"");
            Set(store,4,"Mindie");
            Set(store,3,icon+",0");
            // Set the window-level ID after all relaunch properties are present.
            Set(store,5,AppId);
        }
        finally { Marshal.FinalReleaseComObject(store); }
    }
#if MINDIE_DIAGNOSTICS
    internal static string? ReadWindowProperty(IntPtr hwnd,uint id)
    {
        var guid=typeof(IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd,ref guid,out var store));
        var key=new PropertyKey(id); var value=new PropVariant();
        try { Marshal.ThrowExceptionForHR(store.GetValue(ref key,out value)); return value.Type==31?Marshal.PtrToStringUni(value.Pointer):null; }
        finally { PropVariantClear(ref value); Marshal.FinalReleaseComObject(store); }
    }
#endif
    private static void Set(IPropertyStore store,uint id,string text)
    {
        var key=new PropertyKey(id); var value=new PropVariant { Type=31,Pointer=Marshal.StringToCoTaskMemUni(text) };
        try { Marshal.ThrowExceptionForHR(store.SetValue(ref key,ref value)); }
        finally { PropVariantClear(ref value); }
    }
    private static void NotifyItem(string path)=>SHChangeNotify(0x2000,0x1005,path,IntPtr.Zero);
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey(uint id)
    {
        public Guid Format=new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
        public uint Id=id;
    }
    [StructLayout(LayoutKind.Explicit,Size=24)] private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }
    [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index,out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key,out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key,ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd,ref Guid iid,[MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern void SHChangeNotify(uint change,uint flags,string path,IntPtr other);
}
