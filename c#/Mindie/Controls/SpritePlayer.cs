namespace Mindie.Controls;

public static class SpriteCatalog
{
    private static readonly Dictionary<string, BitmapSource[,]> Cache = [];
    private static readonly Dictionary<string,BitmapSource> Previews=[];
    private static readonly Dictionary<string,string> CustomFiles=[];
    internal static void RegisterCustom(string id,string path,BitmapSource[,] frames) { CustomFiles[id]=path; Cache[id]=frames; }
    internal static void UnregisterCustom(string id) { CustomFiles.Remove(id); Cache.Remove(id); }
    public static bool HasAnimation(string pet) { if (CustomFiles.TryGetValue(pet,out var path)) return File.Exists(path); try { using var stream=System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/Sprites/{pet}.png"))?.Stream; return stream is not null; } catch (IOException) { return false; } }
    private static BitmapSource Preview(string pet)
    {
        if(CustomFiles.ContainsKey(pet)) return Frame(pet,PetState.Idle,0);
        if(!Previews.TryGetValue(pet,out var preview)) Previews[pet]=preview=Load($"Assets/Previews/{pet}.png");
        return preview;
    }
    internal static BitmapSource Still(string pet,PetState state)=>state==PetState.Idle?Preview(pet):Frame(pet,state,0);
    public static BitmapSource Frame(string pet, PetState state, int frame)
    {
        if (!Cache.TryGetValue(pet, out var frames))
        {
            var sheet = CustomFiles.TryGetValue(pet,out var path) ? Services.Images.Decode(File.ReadAllBytes(path)) : Load($"Assets/Sprites/{pet}.png");
            // Detect each connected character silhouette instead of cutting through a foot at
            // an approximate generated grid line. Normalize only the render canvas; keep the
            // original PNG atlas unchanged. All frames share one scale and one ground baseline.
            frames = PrepareFrames(sheet);
            Cache[pet] = frames;
        }
        return frames[(int)state, Math.Clamp(frame, 0, 3)];
    }
    private static BitmapSource Load(string name)
    {
        var image = new BitmapImage(); image.BeginInit(); image.UriSource = new Uri("pack://application:,,,/" + name); image.CacheOption = BitmapCacheOption.OnLoad; image.EndInit(); image.Freeze(); return image;
    }
    internal static BitmapSource[,] PrepareFrames(BitmapSource sheet,CancellationToken cancellation=default)
    {
        var source = new FormatConvertedBitmap(sheet, PixelFormats.Bgra32, null, 0);
        var width = source.PixelWidth; var height = source.PixelHeight; var pixels = new byte[width * height * 4]; source.CopyPixels(pixels, width * 4, 0);
        var visited = new bool[width * height]; var queue = new int[width * height]; var labels = new int[width * height]; var component = 0;
        var bounds = new Int32Rect[6,4]; var sizes = new int[6,4]; var primary = new int[6,4];
        for (var index = 0; index < visited.Length; index++)
        {
            if (index%4096==0) cancellation.ThrowIfCancellationRequested();
            if (visited[index] || pixels[index * 4 + 3] < 12) continue; component++;
            var read = 0; var write = 1; queue[0] = index; visited[index] = true;
            var minX = width; var minY = height; var maxX = 0; var maxY = 0; long sumX = 0, sumY = 0;
            while (read < write)
            {
                if (read%4096==0) cancellation.ThrowIfCancellationRequested();
                var current = queue[read++]; labels[current] = component; var x = current % width; var y = current / width;
                minX = Math.Min(minX,x); minY = Math.Min(minY,y); maxX = Math.Max(maxX,x); maxY = Math.Max(maxY,y); sumX += x; sumY += y;
                for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = x + dx; var ny = y + dy; if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    var next = ny * width + nx; if (visited[next] || pixels[next * 4 + 3] < 12) continue; visited[next] = true; queue[write++] = next;
                }
            }
            if (write < 300) continue;
            var column = Math.Clamp((int)((double)sumX / write / width * 4),0,3); var row = Math.Clamp((int)((double)sumY / write / height * 6),0,5);
            if (write <= sizes[row,column]) continue; sizes[row,column] = write; primary[row,column] = component; bounds[row,column] = new(minX,minY,maxX-minX+1,maxY-minY+1);
        }
        var maximum = 0;
        for (var row = 0; row < 6; row++) for (var col = 0; col < 4; col++) { if (sizes[row,col] == 0) throw new InvalidDataException("Falta un fotograma en la hoja de animación."); maximum = Math.Max(maximum,Math.Max(bounds[row,col].Width,bounds[row,col].Height)); }
        var scale = 222.0 / maximum; var frames = new BitmapSource[6,4];
        for (var row = 0; row < 6; row++) for (var col = 0; col < 4; col++)
        {
            var rect = bounds[row,col]; var clean = new byte[rect.Width * rect.Height * 4];
            for (var y = 0; y < rect.Height; y++) for (var x = 0; x < rect.Width; x++) { var original = (rect.Y+y)*width+rect.X+x; if (labels[original] == primary[row,col]) Buffer.BlockCopy(pixels,original*4,clean,(y*rect.Width+x)*4,4); }
            var crop = BitmapSource.Create(rect.Width,rect.Height,96,96,PixelFormats.Bgra32,null,clean,rect.Width*4); crop.Freeze(); var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) drawing.DrawImage(crop,new Rect((256-rect.Width*scale)/2,244-rect.Height*scale,rect.Width*scale,rect.Height*scale));
            var frame = new RenderTargetBitmap(256,256,96,96,PixelFormats.Pbgra32); frame.Render(visual); frame.Freeze(); var ready = new FormatConvertedBitmap(frame,PixelFormats.Bgra32,null,0); ready.Freeze(); frames[row,col] = ready;
        }
        return frames;
    }
}
public sealed class SpritePlayer : Image
{
    private static readonly HashSet<SpritePlayer> Players = [];
    private static readonly DispatcherTimer Clock = new(DispatcherPriority.Render);
    static SpritePlayer() { Clock.Tick += (_,_) => { Clock.Stop(); foreach (var player in Players.ToArray()) player.Draw(); Schedule(); }; }
    private static void Schedule()
    {
        Clock.Stop(); if(Players.Count==0) return;
        Clock.Interval=TimeSpan.FromMilliseconds(Math.Clamp(Players.Min(p=>p._nextChange)-Environment.TickCount64,16,4400)); Clock.Start();
    }
    private string _pet = "mindie";
    private PetState _state;
    private long _start = Environment.TickCount64 - Random.Shared.Next(0, 3500);
    private int _lastFrame = -1;
    private bool _reduceMotion;
    private bool _animate;
    private long _nextChange;
    public event EventHandler? FrameChanged;
    public string PetId { get => _pet; set { if(_pet==value && Source is not null) return; _pet = value; _lastFrame = -1; Draw(); Schedule(); } }
    public PetState State { get => _state; set { if (_state == value) return; _state = value; _start = Environment.TickCount64; _lastFrame = -1; Draw(); Schedule(); } }
    public bool ReduceMotion { get => _reduceMotion; set { if(_reduceMotion==value) return; _reduceMotion = value; RefreshMotion(); } }
    public bool Animate { get => _animate; set { if(_animate==value) return; _animate = value; RefreshMotion(); } }
    public SpritePlayer()
    {
        Stretch = Stretch.Uniform; SnapsToDevicePixels = true;
        Loaded += (_, _) => { SystemParameters.StaticPropertyChanged+=SystemChanged; RefreshMotion(); };
        Unloaded += (_, _) => { SystemParameters.StaticPropertyChanged-=SystemChanged; Players.Remove(this); Schedule(); };
        IsVisibleChanged+=(_,_)=>RefreshMotion();
    }
    private void SystemChanged(object? sender,PropertyChangedEventArgs e) { if(e.PropertyName==nameof(SystemParameters.ClientAreaAnimation)) RefreshMotion(); }
    private void RefreshMotion()
    {
        if(IsLoaded && IsVisible && Animate && !ReduceMotion && SystemParameters.ClientAreaAnimation) Players.Add(this); else Players.Remove(this);
        if(IsLoaded && IsVisible) { _lastFrame=-1; Draw(); }
        Schedule();
    }
    private void Draw()
    {
        var elapsed = Environment.TickCount64 - _start; var now=Environment.TickCount64;
        var frame = 0;
        if (Animate && !ReduceMotion && SystemParameters.ClientAreaAnimation)
        {
            var duration = _state switch { PetState.Move => 170, PetState.Think => 500, PetState.Drowsy => 650, PetState.Sleep => 1100, PetState.Eat => 280, _ => 140 };
            var phase=elapsed%4400;
            frame = _state == PetState.Idle ? (phase < 3980 ? 0 : Math.Clamp(1 + (int)(phase - 3980) / 140, 1, 3)) : (int)(elapsed / duration % 4);
            _nextChange=now+(_state==PetState.Idle?(phase<3980?3980-phase:Math.Min(4400,3980+((phase-3980)/140+1)*140)-phase):duration-elapsed%duration);
        }
        if (frame == _lastFrame) return; _lastFrame = frame; Source = Animate?SpriteCatalog.Frame(_pet, _state, frame):SpriteCatalog.Still(_pet,_state); FrameChanged?.Invoke(this, EventArgs.Empty);
    }
    public bool HitOpaque(Point point)
    {
        if (Source is not BitmapSource source || ActualWidth <= 0 || ActualHeight <= 0) return false;
        var scale = Math.Min(ActualWidth / source.PixelWidth, ActualHeight / source.PixelHeight);
        var x = (int)((point.X - (ActualWidth - source.PixelWidth * scale) / 2) / scale); var y = (int)((point.Y - (ActualHeight - source.PixelHeight * scale) / 2) / scale);
        if (x < 0 || y < 0 || x >= source.PixelWidth || y >= source.PixelHeight) return false;
        var bgra = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4]; bgra.CopyPixels(new(x, y, 1, 1), pixel, 4, 0); return pixel[3] > 24;
    }
}
