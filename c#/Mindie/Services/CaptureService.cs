using System.Drawing;
using System.Drawing.Imaging;
using Forms = System.Windows.Forms;
using System.Windows.Interop;
using WpfPoint = System.Windows.Point;
using WpfImage = System.Windows.Controls.Image;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace Mindie.Services;

public static class Images
{
    public static BitmapSource Decode(byte[] bytes) { using var stream = new MemoryStream(bytes); var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image; }
    public static byte[] Encode(BitmapSource image) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray(); }
    public static BitmapSource? Load(string path) { try { return File.Exists(path) ? Decode(File.ReadAllBytes(path)) : null; } catch { return null; } }
    public static Int32Rect CropRect(Rect selection, double width, double height, int pixelsX, int pixelsY)
    {
        if (!double.IsFinite(selection.X) || !double.IsFinite(selection.Y) || selection.Width < 8 || selection.Height < 8 || width <= 0 || height <= 0) throw new ArgumentException("Selecciona un área de al menos 8 × 8 píxeles.");
        var left = Math.Clamp((int)Math.Round(selection.Left * pixelsX / width), 0, pixelsX - 1); var top = Math.Clamp((int)Math.Round(selection.Top * pixelsY / height), 0, pixelsY - 1);
        var right = Math.Clamp((int)Math.Round(selection.Right * pixelsX / width), left + 1, pixelsX); var bottom = Math.Clamp((int)Math.Round(selection.Bottom * pixelsY / height), top + 1, pixelsY);
        return new(left, top, right - left, bottom - top);
    }
}
public sealed partial class CaptureService
{
    private bool _active;
    public async Task<byte[]?> SelectAsync()
    {
        if (_active) return null; _active = true;
        var visible = System.Windows.Application.Current.Windows.Cast<Window>().Where(w => w.IsVisible).Select(w=>new CaptureWindowSnapshot(w)).ToList();
        var overlays = new List<CaptureOverlay>(); var completion = new TaskCompletionSource<byte[]?>();
        try
        {
            foreach (var window in visible) window.Hide();
            await Task.Delay(300); // Let DWM remove Mindie before freezing the desktop.
            foreach (var screen in Forms.Screen.AllScreens)
            {
                using var bitmap = new Bitmap(screen.Bounds.Width, screen.Bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(screen.Bounds.Location, System.Drawing.Point.Empty, screen.Bounds.Size, CopyPixelOperation.SourceCopy);
                using var png = new MemoryStream(); bitmap.Save(png, ImageFormat.Png);
                var overlay = new CaptureOverlay(Images.Decode(png.ToArray()), screen.Bounds, image => completion.TrySetResult(image)); overlays.Add(overlay);
            }
            foreach (var overlay in overlays) overlay.Show();
            return await completion.Task;
        }
        finally { foreach (var overlay in overlays) overlay.Close(); foreach (var window in visible) window.Restore(); foreach(var window in visible) window.RestoreActivation(); _active = false; }
    }
}
internal sealed class CaptureOverlay : Window
{
    private readonly BitmapSource _screenshot;
    private readonly Canvas _canvas = new();
    private readonly WpfRectangle _box = new() { Stroke = new SolidColorBrush(ColorFrom("#B9D39B")), StrokeThickness = 2, Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(8, 255, 255, 255)), Visibility = Visibility.Collapsed };
    private WpfPoint? _start;
    private readonly Action<byte[]?> _finish;
    private static System.Windows.Media.Color ColorFrom(string hex) => (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
    public CaptureOverlay(BitmapSource screenshot, System.Drawing.Rectangle bounds, Action<byte[]?> finish)
    {
        _screenshot = screenshot; _finish = finish;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; WindowStartupLocation = WindowStartupLocation.Manual; Cursor = Cursors.Cross;
        var grid = new Grid(); grid.Children.Add(new WpfImage { Source = screenshot, Stretch = Stretch.Fill }); grid.Children.Add(new Border { Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(65, 12, 20, 26)), IsHitTestVisible = false }); grid.Children.Add(_canvas); _canvas.Children.Add(_box);
        var instruction = new Border { Background = System.Windows.Media.Brushes.White, CornerRadius = new(9), Padding = new(20, 13, 20, 13), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new(20, 24, 20, 0), Child = new TextBlock { Text = "Arrastra para seleccionar un área     ·     Esc para cancelar", FontSize = 14 } };
        grid.Children.Add(instruction); instruction.IsHitTestVisible = false; Content = grid;
        SourceInitialized += (_, _) => { NativeMethods.ToolWindow(this); NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0040); };
        Loaded += (_, _) => { NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0040); Activate(); Focus(); };
        MouseLeftButtonDown += (_, e) => { _start = e.GetPosition(_canvas); _canvas.CaptureMouse(); _box.Visibility = Visibility.Visible; _box.Width = _box.Height = 0; };
        MouseMove += (_, e) => { if (_start is not { } start) return; var end = Clamp(e.GetPosition(_canvas)); Canvas.SetLeft(_box, Math.Min(start.X, end.X)); Canvas.SetTop(_box, Math.Min(start.Y, end.Y)); _box.Width = Math.Abs(end.X - start.X); _box.Height = Math.Abs(end.Y - start.Y); };
        MouseLeftButtonUp += (_, e) => { if (_start is not { } start) return; var end = Clamp(e.GetPosition(_canvas)); _start = null; _canvas.ReleaseMouseCapture(); var rect = new Rect(start, end); if (rect.Width < 8 || rect.Height < 8) { _box.Visibility = Visibility.Collapsed; return; } var crop = new CroppedBitmap(_screenshot, Images.CropRect(rect, _canvas.ActualWidth, _canvas.ActualHeight, _screenshot.PixelWidth, _screenshot.PixelHeight)); _finish(Images.Encode(crop)); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; _finish(null); } };
    }
    private WpfPoint Clamp(WpfPoint point) => new(Math.Clamp(point.X, 0, _canvas.ActualWidth), Math.Clamp(point.Y, 0, _canvas.ActualHeight));
}
