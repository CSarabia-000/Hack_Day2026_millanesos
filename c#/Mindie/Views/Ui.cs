using System.Windows.Automation;

namespace Mindie.Views;

internal static class Ui
{
    public static void WindowStyle(Window window) { window.Style=(Style)System.Windows.Application.Current.FindResource(typeof(Window)); Services.ShellBranding.Attach(window); }
    public static readonly Brush Ink = Brush("#252B31"), Muted = Brush("#667078"), Line = Brush("#E2E6E9"), Accent = Brush("#52684B"), Ground = Brush("#F7F8FA"), Soft = Brush("#EDF1E9");
    public static SolidColorBrush Brush(string color) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); brush.Freeze(); return brush; }
    public static TextBlock Text(string text, double size = 14, Brush? color = null, FontWeight? weight = null) => new() { Text = text, FontSize = size, Foreground = color ?? Ink, FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.5 };
    public static TextBlock Glyph(string glyph, double size = 18) => new() { Text = glyph, FontFamily = new("Segoe MDL2 Assets"), FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
    public static void NavigationContent(Button button,string label,string path)
    {
        var icon=new System.Windows.Shapes.Path { Data=Geometry.Parse(path),Width=18,Height=18,Stretch=Stretch.Uniform,StrokeThickness=1.7,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Margin=new(0,0,10,0),VerticalAlignment=VerticalAlignment.Center };
        icon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,new System.Windows.Data.Binding("Foreground") { Source=button });
        icon.IsHitTestVisible=false; icon.Focusable=false;
        var text=Text(label,14); text.LineHeight=18; text.VerticalAlignment=VerticalAlignment.Center; text.TextWrapping=TextWrapping.NoWrap;
        text.SetBinding(TextBlock.ForegroundProperty,new System.Windows.Data.Binding("Foreground") { Source=button }); text.SetBinding(TextBlock.FontWeightProperty,new System.Windows.Data.Binding("FontWeight") { Source=button });
        button.Content=Row(icon,text); AutomationProperties.SetName(button,label);
    }
    public static StackPanel Column(params UIElement[] children) { var panel = new StackPanel(); foreach (var child in children) panel.Children.Add(child); return panel; }
    public static StackPanel Row(params UIElement[] children) { var panel = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var child in children) panel.Children.Add(child); return panel; }
    public static Border Space(double height) => new() { Height = height };
    public static Button Button(string text, Action action, bool primary = false, string? glyph = null)
    {
        var button = new Button { Content = text }; if (primary) button.Style = (Style)System.Windows.Application.Current.FindResource("Primary");
        if (glyph is not null) { var symbol = Glyph(glyph, 15); symbol.Margin = new(0, 0, 9, 0); symbol.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = button }); var label = Text(text, 13); label.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = button }); button.Content = Row(symbol, label); }
        AutomationProperties.SetName(button, text); button.Click += (_, _) => action(); return button;
    }
    public static Button AsyncButton(string text, Func<Task> action, bool primary = false, string? glyph = null) => Button(text, async () => await Safe(action), primary, glyph);
    public static Button Chip(string label,bool selected,Action action)
    {
        var button=Button(label,action); button.FontSize=12; button.MinHeight=32; button.Padding=new(12,6,12,6);
        button.Background=selected?Ink:Brushes.White; button.Foreground=selected?Brushes.White:Muted;
        button.BorderBrush=selected?Ink:Line; button.FontWeight=selected?FontWeights.SemiBold:FontWeights.Normal;
        AutomationProperties.SetHelpText(button,selected?"Filtro seleccionado":"Seleccionar filtro");
        return button;
    }
    public static Button IconButton(string label,string glyph,Action action)
    {
        var button=Button(label,action); var icon=Glyph(glyph,15); icon.SetBinding(TextBlock.ForegroundProperty,new System.Windows.Data.Binding("Foreground") { Source=button });
        button.Content=icon; button.Width=34; button.Height=34; button.MinHeight=34; button.Padding=new(7); button.ToolTip=label;
        return button;
    }
    public static Border Badge(string label,Brush? background=null)
        => new() { Child=Text(label,11,Accent,FontWeights.Medium),Background=background??Soft,CornerRadius=new(5),Padding=new(8,3,8,3),VerticalAlignment=VerticalAlignment.Center };
    public static Grid WithPlaceholder(TextBox input,string text)
    {
        var grid=new Grid(); var hint=Text(text,14,Muted);
        hint.Margin=input.AcceptsReturn?new(input.Padding.Left+1,input.Padding.Top+1,input.Padding.Right+1,0):new(input.Padding.Left+1,0,input.Padding.Right+1,0);
        hint.IsHitTestVisible=false; hint.VerticalAlignment=input.AcceptsReturn?VerticalAlignment.Top:VerticalAlignment.Center;
        void Update()=>hint.Visibility=input.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
        input.TextChanged+=(_,_)=>Update(); grid.Children.Add(input); grid.Children.Add(hint); Update(); return grid;
    }
    public static async Task Safe(Func<Task> action) { try { await action(); } catch (OperationCanceledException) { } catch (Exception ex) { App.Host?.Report(ex.Message); } }
    public static Border Card(UIElement child, int padding = 22) => new() { Background = Brushes.White, BorderBrush = Line, BorderThickness = new(1), CornerRadius = new(12), Padding = new(padding), Child = child };
    public static TextBox Input(string value, string name, bool multiline = false)
    {
        var input = new TextBox { Text = value, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden }; AutomationProperties.SetName(input, name); return input;
    }
    public static UIElement Field(string label, Control control, string? hint = null)
    {
        control.Margin = new(0, 7, 0, 0); var column = Column(Text(label, 13, weight: FontWeights.SemiBold), control);
        if (hint is not null) { var help = Text(hint, 12, Muted); help.Margin = new(0, 6, 0, 0); column.Children.Add(help); } column.Margin = new(0, 0, 0, 17); return column;
    }
    public static Grid Heading(string title, string subtitle, UIElement? action = null)
    {
        var grid = new Grid { Margin = new(0, 0, 0, 28) }; grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var h = Text(title, 30, weight: FontWeights.SemiBold); AutomationProperties.SetHeadingLevel(h, AutomationHeadingLevel.Level1);
        var sub = Text(subtitle, 14, Muted); sub.Margin = new(0, 8, 0, 0); grid.Children.Add(Column(h, sub));
        if (action is not null) { Grid.SetColumn(action, 1); (action as FrameworkElement)!.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(action); } return grid;
    }
    public static ScrollViewer Scroll(UIElement content) => new() { Content = content, Padding = new(0,0,8,0) };
}
