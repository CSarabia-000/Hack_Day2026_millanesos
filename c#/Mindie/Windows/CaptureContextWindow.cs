using Mindie.Services;
using Mindie.Views;

namespace Mindie.Windows;

public sealed class CaptureContextWindow : Window
{
    internal TextBox ContextInput { get; } = Ui.Input("","Contexto de la captura",true);
    public string ContextText => ContextInput.Text;
    internal TextBox SourceInput { get; } = Ui.Input("","Enlace de origen");
    public string SourceUrl => LibraryStore.ValidateSourceUrl(SourceInput.Text);
    public CaptureContextWindow(byte[] png,bool fullPage=false)
    {
        Ui.WindowStyle(this);
        Title="Mindie · Dar contexto"; Width=940; Height=680; MinWidth=620; MinHeight=450; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var page = new DockPanel { Margin=new(28) };
        var heading = Ui.Heading("Dale contexto a este recuerdo.","Explica qué estás viendo y qué te interesa guardar. Gemma lo tendrá en cuenta al analizar la selección.");
        DockPanel.SetDock(heading,Dock.Top); page.Children.Add(heading);
        var error=Ui.Text("",12,Ui.Brush("#A02D3C"));
        var next = Ui.Button("Analizar selección",()=> { try { _=SourceUrl; DialogResult=true; } catch (ArgumentException ex) { error.Text=ex.Message; } },true); next.IsEnabled=false;
        var cancel = Ui.Button("Cancelar",()=>DialogResult=false); cancel.Margin=new(0,0,10,0);
        var actions = Ui.Row(cancel,next); actions.HorizontalAlignment=HorizontalAlignment.Right; actions.Margin=new(0,18,0,0); DockPanel.SetDock(actions,Dock.Bottom); page.Children.Add(actions);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width=new(24) }); grid.ColumnDefinitions.Add(new());
        var picture=new Image { Source=Images.Decode(png),Stretch=Stretch.Uniform };
        grid.Children.Add(new Border { Background=Ui.Ground,CornerRadius=new(8),Padding=new(12),Child=fullPage?Ui.Scroll(picture):picture });
        var form = new DockPanel(); var hint = Ui.Text("Sin límite fijo de caracteres. Puedes pegar texto extenso; se procesará por partes si hace falta.",12,Ui.Muted); hint.Margin=new(0,0,0,12); DockPanel.SetDock(hint,Dock.Top); form.Children.Add(hint);
        var source=Ui.Column(Ui.Space(12),Ui.Field("Enlace de origen (opcional)",SourceInput,"Pégalo manualmente para volver a la página después."),error); DockPanel.SetDock(source,Dock.Bottom); form.Children.Add(source); form.Children.Add(ContextInput);
        ContextInput.AcceptsTab=true; ContextInput.MaxLength=0; ContextInput.VerticalContentAlignment=VerticalAlignment.Top;
        ContextInput.TextChanged+=(_,_)=>next.IsEnabled=!string.IsNullOrWhiteSpace(ContextInput.Text);
        Grid.SetColumn(form,2); grid.Children.Add(form); page.Children.Add(grid); Content=page;
        Loaded+=(_,_)=> { NativeMethods.FitToWorkArea(this,true); ContextInput.Focus(); };
        PreviewKeyDown+=(_,e)=> { if (e.Key==Key.Escape) { DialogResult=false; e.Handled=true; } };
    }
}
