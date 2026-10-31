using System.Runtime.InteropServices;

namespace Mindie.Controls;

// Observe clicks without capturing or consuming input intended for other apps.
internal sealed class OutsideClickDismissal : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Func<NativeMethods.Point,bool> _contains;
    private readonly Action _dismiss;
    private readonly MouseHook _callback;
    private IntPtr _hook;
    internal bool IsActive=>_hook!=IntPtr.Zero;

    internal OutsideClickDismissal(Dispatcher dispatcher,Func<NativeMethods.Point,bool> contains,Action dismiss)
    {
        _dispatcher=dispatcher; _contains=contains; _dismiss=dismiss; _callback=OnMouse;
        _hook=SetWindowsHookExW(14,_callback,GetModuleHandleW(null),0);
        if(_hook==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private IntPtr OnMouse(int code,IntPtr message,IntPtr data)
    {
        if(code>=0 && message.ToInt64() is 0x201 or 0x204 or 0x207 or 0x20B)
            ObserveClick(Marshal.PtrToStructure<NativeMethods.Point>(data));
        return CallNextHookEx(IntPtr.Zero,code,message,data);
    }
    internal void ObserveClick(NativeMethods.Point point)
    {
        if(IsActive && !_contains(point)) _dispatcher.BeginInvoke(()=> { if(IsActive) _dismiss(); },DispatcherPriority.Input);
    }
    internal static bool HitsWindow(NativeMethods.Point point,IntPtr hwnd)
    {
        if(hwnd==IntPtr.Zero || !IsWindowVisible(hwnd)) return false;
        var target=GetAncestor(WindowFromPoint(point),2);
        while(target!=IntPtr.Zero) {
            if(target==hwnd) return true;
            target=NativeMethods.GetWindow(target,4);
        }
        return false;
    }
    public void Dispose()
    {
        if(_hook==IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook); _hook=IntPtr.Zero;
    }
    private delegate IntPtr MouseHook(int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr SetWindowsHookExW(int id,MouseHook callback,IntPtr module,uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativeMethods.Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr GetModuleHandleW(string? module);
}
