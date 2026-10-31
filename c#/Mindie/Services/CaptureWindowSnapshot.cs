using System.Windows.Interop;

namespace Mindie.Services;

internal sealed class CaptureWindowSnapshot
{
    private readonly Window _window;
    private readonly IntPtr _handle,_above;
    private readonly bool _activate;
    internal CaptureWindowSnapshot(Window window)
    {
        _window=window; _handle=new WindowInteropHelper(window).Handle;
        _above=NativeMethods.GetWindow(_handle,3); _activate=window.IsActive && window.ShowActivated;
    }
    internal void Hide()=>_window.Hide();
    internal void Restore()
    {
        if(!NativeMethods.IsWindow(_handle)) return;
        var activate=_window.ShowActivated;
        try { _window.ShowActivated=false; _window.Show(); }
        finally { _window.ShowActivated=activate; }
        var above=_window.Topmost?new IntPtr(-1):_above;
        if(!_window.Topmost && (above==IntPtr.Zero || !NativeMethods.IsWindow(above) || (NativeMethods.GetWindowLongPtrW(above,-20).ToInt64()&8)!=0)) above=new IntPtr(_window is MainWindow && !_activate?1:-2);
        NativeMethods.SetWindowPos(_handle,above,0,0,0,0,0x0213);
    }
    internal void RestoreActivation() { if(_activate && _window.IsVisible) _window.Activate(); }
}
