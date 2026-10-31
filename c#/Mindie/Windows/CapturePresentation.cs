using System.Windows.Interop;

namespace Mindie.Windows;

/// <summary>Keeps a companion capture on its monitor without activating the library.</summary>
internal sealed class CapturePresentation
{
    private readonly PetWindow? _origin;
    private readonly MainWindow? _main;
    private readonly System.Drawing.Point _anchor;
    internal CapturePresentation(PetWindow? origin,MainWindow? main)
    {
        _origin=origin; _main=main;
        if(origin is not null && NativeMethods.GetWindowRect(new WindowInteropHelper(origin).Handle,out var bounds)) _anchor=new((bounds.Left+bounds.Right)/2,(bounds.Top+bounds.Bottom)/2);
        else if(NativeMethods.GetCursorPos(out var cursor)) _anchor=new(cursor.X,cursor.Y);
    }
    internal T Dialog<T>(T window) where T:Window
    {
        if(_origin is not null) {
            window.Owner=_origin; window.Topmost=true; window.ShowInTaskbar=false; Place(window);
        }
        else if(_main?.IsVisible==true) window.Owner=_main;
        return window;
    }
    internal T Editor<T>(T window,MainWindow? currentMain) where T:Window
    {
        if(_origin is not null) {
            // A modeless editor remains reachable if the companion is later hidden.
            window.Owner=null; window.ShowInTaskbar=true; Place(window);
        }
        else window.Owner=currentMain;
        return window;
    }
    private void Place(Window window)
    {
        window.WindowStartupLocation=WindowStartupLocation.Manual;
        window.SourceInitialized+=(_,_)=>NativeMethods.FitToWorkArea(window,true,_anchor);
        window.Loaded+=(_,_)=>NativeMethods.FitToWorkArea(window,true,_anchor);
    }
}
