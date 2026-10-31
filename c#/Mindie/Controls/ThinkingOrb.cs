using System.Windows.Automation;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Mindie.Controls;

/// <summary>Native, indeterminate analysis indicator. Only transforms and opacity are animated.</summary>
public sealed class ThinkingOrb : Grid
{
    public static readonly DependencyProperty IsActiveProperty=DependencyProperty.Register(nameof(IsActive),typeof(bool),typeof(ThinkingOrb),new PropertyMetadata(false,Changed));
    public static readonly DependencyProperty ReduceMotionProperty=DependencyProperty.Register(nameof(ReduceMotion),typeof(bool),typeof(ThinkingOrb),new PropertyMetadata(false,Changed));
    private readonly RotateTransform _rotation=new();
    private readonly TranslateTransform _blue=new(), _aqua=new(), _light=new();
    private readonly Canvas _flow;
    private readonly Ellipse _glow;
    private Window? _owner;
    private bool _running, _subscribed;
    public bool IsActive { get=>(bool)GetValue(IsActiveProperty); set=>SetValue(IsActiveProperty,value); }
    public bool ReduceMotion { get=>(bool)GetValue(ReduceMotionProperty); set=>SetValue(ReduceMotionProperty,value); }
    internal bool IsAnimating=>_running;
    internal double MotionAngle=>_rotation.Angle;
    public ThinkingOrb()
    {
        Width=112; Height=112; IsHitTestVisible=false; Focusable=false;
        AutomationProperties.SetName(this,"Gemma está analizando");
        var sphere=new Grid { Width=112,Height=112,Clip=new EllipseGeometry(new Point(56,56),55,55) };
        sphere.Children.Add(new Ellipse { Fill=new RadialGradientBrush {
            Center=new(.4,.35),GradientOrigin=new(.3,.25),RadiusX=.85,RadiusY=.85,
            GradientStops=new() { new(Color.FromRgb(208,255,248),0),new(Color.FromRgb(106,228,231),.5),new(Color.FromRgb(128,196,237),.8),new(Color.FromRgb(181,233,233),1) }
        } });
        _flow=new Canvas { Width=112,Height=112,RenderTransform=_rotation,RenderTransformOrigin=new(.5,.5) };
        var blue=Blob(108,"#A294EC",.92); Canvas.SetLeft(blue,17); Canvas.SetTop(blue,-22); blue.RenderTransform=_blue; _flow.Children.Add(blue);
        var aqua=Blob(100,"#45F3D9",.95); Canvas.SetLeft(aqua,-29); Canvas.SetTop(aqua,37); aqua.RenderTransform=_aqua; _flow.Children.Add(aqua);
        _glow=Blob(84,"#FFFFFF",.95); Canvas.SetLeft(_glow,36); Canvas.SetTop(_glow,45); _glow.RenderTransform=_light; _flow.Children.Add(_glow);
        sphere.Children.Add(_flow);
        var reflection=Blob(62,"#FFFFFF",.46); reflection.HorizontalAlignment=HorizontalAlignment.Left; reflection.VerticalAlignment=VerticalAlignment.Top; reflection.Margin=new(6,2,0,0); sphere.Children.Add(reflection);
        Children.Add(new Viewbox { Child=sphere,Stretch=Stretch.Uniform });
        Loaded+=(_,_)=> {
            if (!_subscribed) { SystemParameters.StaticPropertyChanged+=SystemChanged; _subscribed=true; }
            _owner=Window.GetWindow(this); if (_owner is not null) _owner.StateChanged+=OwnerStateChanged;
            UpdateMotion();
        };
        Unloaded+=(_,_)=> { Stop(); if (_subscribed) { SystemParameters.StaticPropertyChanged-=SystemChanged; _subscribed=false; } if (_owner is not null) _owner.StateChanged-=OwnerStateChanged; _owner=null; };
        IsVisibleChanged+=(_,_)=>UpdateMotion();
    }
    private static Ellipse Blob(double size,string color,double opacity)
    {
        var tint=(Color)ColorConverter.ConvertFromString(color);
        var transparent=Color.FromArgb(0,tint.R,tint.G,tint.B);
        return new() { Width=size,Height=size,Opacity=opacity,Fill=new RadialGradientBrush {
            GradientStops=new() { new(tint,0),new(Color.FromArgb(210,tint.R,tint.G,tint.B),.32),new(transparent,1) }
        } };
    }
    private static void Changed(DependencyObject sender,DependencyPropertyChangedEventArgs args)=>((ThinkingOrb)sender).UpdateMotion();
    private void SystemChanged(object? sender,PropertyChangedEventArgs e) { if (e.PropertyName==nameof(SystemParameters.ClientAreaAnimation)) UpdateMotion(); }
    private void OwnerStateChanged(object? sender,EventArgs e)=>UpdateMotion();
    private void UpdateMotion()
    {
        var animate=IsLoaded && IsVisible && IsActive && _owner?.WindowState!=WindowState.Minimized && !ReduceMotion && SystemParameters.ClientAreaAnimation;
        if (!animate) { Stop(); return; }
        if (_running) return; _running=true;
        _rotation.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(0,360,TimeSpan.FromSeconds(6)) { RepeatBehavior=RepeatBehavior.Forever });
        Drift(_blue,20,-8); Drift(_aqua,-10,18); Drift(_light,-24,-14);
        var shimmer=new DoubleAnimation(.6,.95,TimeSpan.FromSeconds(2.4)) { AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever };
        _glow.BeginAnimation(OpacityProperty,shimmer);
    }
    private static void Drift(TranslateTransform transform,double x,double y)
    {
        transform.BeginAnimation(TranslateTransform.XProperty,Wave(x));
        transform.BeginAnimation(TranslateTransform.YProperty,Wave(y));
    }
    private static DoubleAnimationUsingKeyFrames Wave(double peak)
    {
        var animation=new DoubleAnimationUsingKeyFrames { Duration=TimeSpan.FromSeconds(4.8),RepeatBehavior=RepeatBehavior.Forever };
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(peak,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.4)),new KeySpline(.77,0,.175,1)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.8)),new KeySpline(.77,0,.175,1)));
        return animation;
    }
    private void Stop()
    {
        _rotation.BeginAnimation(RotateTransform.AngleProperty,null);
        foreach (var transform in new[] { _blue,_aqua,_light }) {
            transform.BeginAnimation(TranslateTransform.XProperty,null); transform.BeginAnimation(TranslateTransform.YProperty,null);
        }
        _glow.BeginAnimation(OpacityProperty,null); _running=false;
    }
}
