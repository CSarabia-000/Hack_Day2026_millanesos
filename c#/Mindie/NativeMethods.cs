using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Mindie;

internal static partial class NativeMethods
{
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] internal static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern void SHChangeNotify(uint change,uint flags,string item,IntPtr other);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int Index;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)] public string Type;
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path,uint attributes,out ShellFileInfo info,uint size,uint flags);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern void SHUpdateImage(string path,int index,uint flags,int imageIndex);
    internal static void RefreshApplicationIcon()
    {
        // Refresh only this executable's existing system image entries and shell item.
        if(Environment.ProcessPath is not string path) return;
        var size=(uint)Marshal.SizeOf<ShellFileInfo>();
        if(SHGetFileInfo(path,0,out var location,size,0x1000)!=IntPtr.Zero && !string.IsNullOrEmpty(location.Name)) {
            foreach(var flags in new uint[] { 0x4000,0x4001 }) {
                if(SHGetFileInfo(path,0,out var item,size,flags)!=IntPtr.Zero && item.Index>=0) SHUpdateImage(location.Name,location.Index,0,item.Index);
            }
        }
        SHChangeNotify(0x2000,0x1005,path,IntPtr.Zero);
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd,uint command);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] internal static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);
    internal static void ExcludeFromCapture(Window window) => SetWindowDisplayAffinity(new WindowInteropHelper(window).Handle, 0x11);
    internal static void ToolWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var style = GetWindowLongPtrW(hwnd, -20).ToInt64();
        SetWindowLongPtrW(hwnd, -20, new IntPtr((style | 0x80) & ~0x40000L));
    }
    internal static void FitToWorkArea(Window window, bool center = false,System.Drawing.Point? anchor=null)
    {
        if (window.WindowState != WindowState.Normal) return;
        var hwnd = new WindowInteropHelper(window).Handle; if (hwnd == IntPtr.Zero) return;
        var screen=anchor is { } point?System.Windows.Forms.Screen.FromPoint(point):System.Windows.Forms.Screen.FromHandle(hwnd);
        var work=screen.WorkingArea;
        if(anchor is not null && screen.DeviceName!=System.Windows.Forms.Screen.FromHandle(hwnd).DeviceName)
            SetWindowPos(hwnd,IntPtr.Zero,work.Left+12,work.Top+12,0,0,0x0015);
        var dpi = GetDpiForWindow(hwnd); var scale = dpi > 0 ? dpi / 96.0 : 1.0; var margin = (int)Math.Round(12 * scale);
        GetWindowRect(hwnd, out var bounds);
        var width = Math.Min(center ? (int)Math.Round(window.Width * scale) : bounds.Right - bounds.Left, work.Width - margin * 2);
        var height = Math.Min(center ? (int)Math.Round(window.Height * scale) : bounds.Bottom - bounds.Top, work.Height - margin * 2);
        window.MinWidth = Math.Min(window.MinWidth, width / scale); window.MinHeight = Math.Min(window.MinHeight, height / scale);
        window.Width = width / scale; window.Height = height / scale;
        var left = center ? work.Left + (work.Width-width)/2 : Math.Clamp(bounds.Left, work.Left+margin, work.Right-width-margin);
        var top = center ? work.Top + (work.Height-height)/2 : Math.Clamp(bounds.Top, work.Top+margin, work.Bottom-height-margin);
        SetWindowPos(hwnd,IntPtr.Zero,left,top,width,height,0x0014);
    }
}
