using System.Windows.Automation;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ShapePath = System.Windows.Shapes.Path;
using Mindie.Views;

namespace Mindie.Controls;

/// <summary>The compact action capsule shown next to the desktop companion.</summary>
public sealed class WidgetActionMenu : Grid
{
    public const double CapsuleWidth = 112;
    public const double CapsuleHeight = 36;
    private readonly TranslateTransform _offset = new();
    private int _transition;
    public bool IsClosing { get; private set; }
    public Button WriteButton { get; }
    public Button CaptureButton { get; }
    public Button LibraryButton { get; }

    public WidgetActionMenu(Action write, Action capture, Action library)
    {
        Width = CapsuleWidth + 16; Height = CapsuleHeight + 16;
        RenderTransform = _offset;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        var actions = new Grid();
        for (var index = 0; index < 5; index++)
            actions.ColumnDefinitions.Add(new() { Width = index % 2 == 0 ? new(1, GridUnitType.Star) : new(1) });

        WriteButton = ActionButton("Escribir", WriteIcon(), write);
        CaptureButton = ActionButton("Guardar en mente", CaptureIcon(), capture);
        LibraryButton = ActionButton("Mis guardados", LibraryIcon(), library);
        var buttons = new[] { WriteButton, CaptureButton, LibraryButton };
        for (var index = 0; index < buttons.Length; index++)
        {
            Grid.SetColumn(buttons[index], index * 2); actions.Children.Add(buttons[index]);
            if (index == buttons.Length - 1) continue;
            var separator = new Border { Width = 1, Height = 15, Background = Ui.Brush("#30FFFFFF"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(separator, index * 2 + 1); actions.Children.Add(separator);
        }
        Children.Add(new Border {
            Background = Ui.Brush("#E055595F"), BorderBrush = Ui.Brush("#48FFFFFF"), BorderThickness = new(1),
            CornerRadius = new(CapsuleHeight / 2), Margin = new(8), Child = actions,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 13, ShadowDepth = 3, Opacity = .18 }
        });
    }

    private static Button ActionButton(string name, UIElement glyph, Action action)
    {
        var button = new Button { Content = glyph, Width = 34, Height = 30, MinHeight = 30, Padding = new(0), ToolTip = name, Cursor = Cursors.Hand };
        AutomationProperties.SetName(button, name);
        var surface = new FrameworkElementFactory(typeof(Border), "Halo");
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(15));
        surface.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        surface.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        surface.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        surface.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = surface };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Ui.Brush("#25FFFFFF"), "Halo"));
        var press = new Trigger { Property = Button.IsPressedProperty, Value = true };
        press.Setters.Add(new Setter(Border.BackgroundProperty, Ui.Brush("#42FFFFFF"), "Halo"));
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, Ui.Brush("#C0FFFFFF"), "Halo"));
        template.Triggers.Add(hover); template.Triggers.Add(press); template.Triggers.Add(focus);
        button.Template = template;
        button.Click += (_, _) => action();
        return button;
    }

    private static UIElement WriteIcon() => new ShapePath {
        Width = 16, Height = 16, Stretch = Stretch.Uniform, Stroke = Ui.Brush("#FAFBFC"), StrokeThickness = 1.65,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        Data = Geometry.Parse("M 10,4 H 6 C 4.3,4 3,5.3 3,7 V 18 C 3,19.7 4.3,21 6,21 H 17 C 18.7,21 20,19.7 20,18 V 13 M 10,14 L 11,10 L 18,3 L 21,6 L 14,13 Z M 17,4 L 20,7")
    };

    private static UIElement CaptureIcon() => new ShapePath {
        Width = 16, Height = 16, Stretch = Stretch.Uniform, Stroke = Ui.Brush("#FAFBFC"), StrokeThickness = 1.7,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        Data = Geometry.Parse("M 8,3 H 3 V 8 M 16,3 H 21 V 8 M 21,16 V 21 H 16 M 8,21 H 3 V 16 M 8,12 H 16 M 12,8 V 16")
    };

    private static UIElement LibraryIcon() => new ShapePath {
        Width = 16, Height = 16, Stretch = Stretch.Uniform, Stroke = Ui.Brush("#FAFBFC"), StrokeThickness = 1.7,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        Data = Geometry.Parse("M 6,3 H 18 V 21 L 12,17 L 6,21 Z")
    };

    public void Reveal(bool reducedMotion, bool reset = false)
    {
        _transition++; IsClosing = false;
        if (reset) { BeginAnimation(OpacityProperty, null); _offset.BeginAnimation(TranslateTransform.YProperty, null); Opacity = 0; _offset.Y = 4; }
        if (reducedMotion) { BeginAnimation(OpacityProperty, null); _offset.BeginAnimation(TranslateTransform.YProperty, null); Opacity = 1; _offset.Y = 0; return; }
        Animate(this, OpacityProperty, Opacity, 1, 160);
        Animate(_offset, TranslateTransform.YProperty, _offset.Y, 0, 160);
    }

    public void Dismiss(bool reducedMotion, Action completed)
    {
        if (IsClosing) return;
        IsClosing = true; var transition = ++_transition;
        if (reducedMotion) { completed(); return; }
        Animate(this, OpacityProperty, Opacity, 0, 125, () => { if (_transition == transition) completed(); });
        Animate(_offset, TranslateTransform.YProperty, _offset.Y, 4, 125);
    }

    private static void Animate(DependencyObject target, DependencyProperty property, double from, double to, int milliseconds, Action? completed = null)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(milliseconds) };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds)), new KeySpline(.23, 1, .32, 1)));
        if (completed is not null) animation.Completed += (_, _) => completed();
        if (target is UIElement element) element.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        else ((Animatable)target).BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
