using System.Diagnostics;
using System.Windows.Media.Media3D;
using Mindie.Views;

namespace Mindie.Controls;

/// <summary>Decorative star shell. It never creates knowledge nodes, links or hit targets.</summary>
internal sealed class AstralShellBackdrop:FrameworkElement
{
    internal const double Radius=2.35;
    internal static readonly Point3D[] Stars=MakeStars();
    internal static readonly (int A,int B,bool Chord)[] Filaments=MakeFilaments();
    private static readonly Pen[] Threads=Enumerable.Range(0,24).Select(i=>Pen((byte)(12+i*5),"#9784F5",.65)).ToArray();
    private static readonly Pen[] Chords=Enumerable.Range(0,24).Select(i=>Pen((byte)(5+i*2),"#7963DF",.55)).ToArray();
    private static readonly Brush[] Dots=Enumerable.Range(0,24).Select(i=>Brush((byte)(32+i*9),"#D4CAFF")).ToArray();
    private static readonly Brush Glow=MakeGlow();
    private readonly KnowledgeGraphCanvas _scene;
    private readonly DispatcherTimer _timer=new(DispatcherPriority.Background) { Interval=TimeSpan.FromMilliseconds(1000.0/24) };
    private readonly Stopwatch _elapsed=new();
    private readonly Point[] _projected=new Point[Stars.Length];
    private readonly double[] _depth=new double[Stars.Length];
    private Window? _window;
    private bool _enabled=true,_reducedMotion;
    private double _angle;
    internal bool IsAnimating=>_timer.IsEnabled;
    internal double Angle=>_angle;
    internal bool Enabled { get=>_enabled; set { _enabled=value; RefreshMotion(); } }
    internal bool ReducedMotion { get=>_reducedMotion; set { _reducedMotion=value; RefreshMotion(); } }
    internal AstralShellBackdrop(KnowledgeGraphCanvas scene)
    {
        _scene=scene; IsHitTestVisible=false;
        _timer.Tick+=(_,_)=> {
            var seconds=Math.Min(.1,_elapsed.Elapsed.TotalSeconds); _elapsed.Restart();
            _angle=(_angle+seconds*Math.PI*2/180)%(Math.PI*2); InvalidateVisual();
        };
        Loaded+=(_,_)=> {
            _window=Window.GetWindow(this);
            if(_window is not null) { _window.Activated+=WindowChanged; _window.Deactivated+=WindowChanged; _window.StateChanged+=WindowChanged; }
            SystemParameters.StaticPropertyChanged+=SystemChanged; RefreshMotion();
        };
        Unloaded+=(_,_)=> {
            _timer.Stop(); _elapsed.Stop();
            if(_window is not null) { _window.Activated-=WindowChanged; _window.Deactivated-=WindowChanged; _window.StateChanged-=WindowChanged; _window=null; }
            SystemParameters.StaticPropertyChanged-=SystemChanged;
        };
        IsVisibleChanged+=(_,_)=>RefreshMotion();
    }
    private void WindowChanged(object? sender,EventArgs e)=>RefreshMotion();
    private void SystemChanged(object? sender,PropertyChangedEventArgs e) { if(e.PropertyName==nameof(SystemParameters.ClientAreaAnimation)) RefreshMotion(); }
    internal void RefreshMotion()
    {
        var run=IsLoaded && IsVisible && _enabled && !_reducedMotion && SystemParameters.ClientAreaAnimation && _window?.IsActive==true && _window.WindowState!=WindowState.Minimized && !_scene.IsInteracting;
        if(run==_timer.IsEnabled) return;
        if(run) { _elapsed.Restart(); _timer.Start(); } else { _timer.Stop(); _elapsed.Stop(); }
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing); if(ActualWidth<1 || ActualHeight<1) return;
        var cos=Math.Cos(_angle); var sin=Math.Sin(_angle); var centerDepth=_scene.ViewDepth(new());
        for(var i=0;i<Stars.Length;i++) {
            var star=Stars[i]; var world=new Point3D(star.X*cos+star.Z*sin,star.Y,-star.X*sin+star.Z*cos);
            _projected[i]=_scene.ToView(world); _depth[i]=Math.Clamp((centerDepth-_scene.ViewDepth(world))/Radius*.5+.5,0,1);
        }
        foreach(var (a,b,chord) in Filaments) {
            var depth=(_depth[a]+_depth[b])*.5;
            var intensity=Math.Clamp((int)(Math.Pow(depth,1.7)*23),0,23);
            if(_projected[a].X<-1000 || _projected[b].X<-1000) continue;
            drawing.DrawLine(chord?Chords[intensity]:Threads[intensity],_projected[a],_projected[b]);
        }
        for(var i=0;i<Stars.Length;i++) {
            var point=_projected[i]; if(point.X<-15 || point.X>ActualWidth+15 || point.Y<-15 || point.Y>ActualHeight+15) continue;
            var depth=_depth[i]; var level=Math.Clamp((int)(Math.Pow(depth,1.4)*23),0,23);
            if(i%17==0 && depth>.5) drawing.DrawEllipse(Glow,null,point,5,5);
            var size=(i%17==0?1.2:.65)*(.65+depth*.55); drawing.DrawEllipse(Dots[level],null,point,size,size);
        }
    }
    private static Point3D[] MakeStars()
    {
        var random=new Random(72413); var result=new Point3D[960]; const int groups=40;
        for(var i=0;i<result.Length;i++) {
            var group=i%groups; var y=1-2*(group+.5)/groups; var angle=group*Math.PI*(3-Math.Sqrt(5));
            var center=new Vector3D(Math.Sqrt(1-y*y)*Math.Cos(angle),y,Math.Sqrt(1-y*y)*Math.Sin(angle));
            var right=Vector3D.CrossProduct(center,Math.Abs(y)<.9?new(0,1,0):new Vector3D(1,0,0)); right.Normalize(); var up=Vector3D.CrossProduct(right,center);
            var position=center+right*((random.NextDouble()-.5)*.56)+up*((random.NextDouble()-.5)*.56); position.Normalize();
            result[i]=(Point3D)(position*Radius);
        }
        return result;
    }
    private static (int,int,bool)[] MakeFilaments()
    {
        var pairs=new HashSet<(int,int)>(); var result=new List<(int,int,bool)>();
        for(var i=0;i<Stars.Length;i++) {
            var best=new (double Distance,int Index)[3]; Array.Fill(best,(double.MaxValue,-1));
            for(var j=0;j<Stars.Length;j++) {
                if(i==j) continue; var distance=(Stars[i]-Stars[j]).LengthSquared; if(distance>.62*.62 || distance>=best[2].Distance) continue;
                best[2]=(distance,j); for(var slot=2;slot>0 && best[slot].Distance<best[slot-1].Distance;slot--) (best[slot],best[slot-1])=(best[slot-1],best[slot]);
            }
            foreach(var (_,j) in best) if(j>=0 && pairs.Add((Math.Min(i,j),Math.Max(i,j)))) result.Add((i,j,false));
        }
        var random=new Random(6181);
        for(var i=0;i<110;i++) { var a=random.Next(Stars.Length); var b=random.Next(Stars.Length); if((Stars[a]-Stars[b]).Length>Radius && pairs.Add((Math.Min(a,b),Math.Max(a,b)))) result.Add((a,b,true)); }
        return result.ToArray();
    }
    private static Pen Pen(byte alpha,string color,double thickness) { var pen=new Pen(Brush(alpha,color),thickness); pen.Freeze(); return pen; }
    private static Brush Brush(byte alpha,string color) { var c=((SolidColorBrush)Ui.Brush(color)).Color; var brush=new SolidColorBrush(Color.FromArgb(alpha,c.R,c.G,c.B)); brush.Freeze(); return brush; }
    private static Brush MakeGlow() { var brush=new RadialGradientBrush(Color.FromArgb(80,161,141,255),Colors.Transparent); brush.Freeze(); return brush; }
}
