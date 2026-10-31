using System.Windows.Interop;
using Forms=System.Windows.Forms;

namespace Mindie.Services;

public sealed partial class CaptureService
{
    public async Task<PreparedChatImage> ScreenAsync(Window origin,CancellationToken cancellation)
        =>await ScreenAsync(origin,cancellation,ReadDesktop);
    internal async Task<PreparedChatImage> ScreenAsync(Window origin,CancellationToken cancellation,Func<System.Drawing.Rectangle,byte[]> readDesktop)
    {
        if(_active) throw new InvalidOperationException("Ya hay una captura en curso. Espera a que termine.");
        cancellation.ThrowIfCancellationRequested(); _active=true;
        var windows=System.Windows.Application.Current.Windows.Cast<Window>().Where(w=>w.IsVisible).Select(w=>new CaptureWindowSnapshot(w)).ToArray();
        var screen=Forms.Screen.FromHandle(new WindowInteropHelper(origin).Handle).Bounds;
        try {
            foreach(var window in windows) window.Hide();
            await Task.Delay(200,cancellation);
            return await Task.Run(()=> {
                cancellation.ThrowIfCancellationRequested();
                var png=readDesktop(screen);
                cancellation.ThrowIfCancellationRequested();
                return ChatImageService.Prepare(Images.Decode(png),$"Pantalla {DateTime.Now:yyyy-MM-dd HH-mm-ss}.png");
            },cancellation);
        }
        finally { foreach(var window in windows) window.Restore(); foreach(var window in windows) window.RestoreActivation(); _active=false; }
    }
    private static byte[] ReadDesktop(System.Drawing.Rectangle screen)
    {
        using var bitmap=new System.Drawing.Bitmap(screen.Width,screen.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using(var graphics=System.Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen(screen.Location,System.Drawing.Point.Empty,screen.Size,System.Drawing.CopyPixelOperation.SourceCopy);
        using var stream=new MemoryStream(); bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Png); return stream.ToArray();
    }
}
