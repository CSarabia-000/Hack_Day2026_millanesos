using Mindie.Services;
using Mindie.Views;
using System.Windows.Automation;

namespace Mindie.Windows;

public sealed class WebTextCaptureWindow:Window
{
    internal TextBox AddressInput { get; }=Ui.Input("","Enlace para leer solo texto");
    internal TextBox ContextInput { get; }=Ui.Input("","Contexto de la página",true);
    internal TextBox TextPreview { get; }=Ui.Input("","Texto extraído de la página",true);
    private readonly WebTextService _reader;
    private readonly TextBlock _status=Ui.Text("Pega el enlace de un artículo. Se leerá sin cargar imágenes, videos ni scripts.",12,Ui.Muted);
    private readonly Button _read, _analyze, _paste;
    private readonly bool _giveContext;
    private CancellationTokenSource? _request;
    private WebPageText? _page;
    private bool _closed;
    public WebPageText? Result { get; private set; }
    public string ContextText=>ContextInput.Text;
    public WebTextCaptureWindow(bool giveContext=false):this(new WebTextService(),giveContext) { }
    internal WebTextCaptureWindow(WebTextService reader,bool giveContext=false)
    {
        Ui.WindowStyle(this); _reader=reader; _giveContext=giveContext;
        Title="Mindie · Página web: solo texto"; Width=900; Height=760; MinWidth=640; MinHeight=520; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var page=new DockPanel { Margin=new(26) };
        var heading=Ui.Heading("Lee la página, con menos carga.","Solo texto · Recomendado para artículos y lecturas. Revisa el contenido antes de analizarlo."); DockPanel.SetDock(heading,Dock.Top); page.Children.Add(heading);
        var tools=new DockPanel { Margin=new(0,0,0,12) };
        _read=Ui.Button("Leer página",async()=>await ReadAsync(),true); _read.Margin=new(10,0,0,0); DockPanel.SetDock(_read,Dock.Right); tools.Children.Add(_read); tools.Children.Add(AddressInput);
        DockPanel.SetDock(tools,Dock.Top); page.Children.Add(tools);
        _status.Margin=new(0,0,0,14); AutomationProperties.SetLiveSetting(_status,AutomationLiveSetting.Polite); DockPanel.SetDock(_status,Dock.Top); page.Children.Add(_status);
        var bottom=Ui.Column();
        if (giveContext) { ContextInput.Height=90; ContextInput.MaxLength=0; ContextInput.VerticalContentAlignment=VerticalAlignment.Top; bottom.Children.Add(Ui.Space(14)); bottom.Children.Add(Ui.Field("Tu contexto",ContextInput,"Explica qué quieres recordar antes de analizar. Sin límite fijo de caracteres.")); }
        _paste=Ui.Button("Pegar texto",PasteText); _paste.ToolTip="Si la página requiere JavaScript o sesión, copia el artículo en tu navegador y pégalo aquí.";
        var cancel=Ui.Button("Cancelar",()=>DialogResult=false); cancel.Margin=new(8,0,8,0);
        _analyze=Ui.Button("Analizar texto",()=> { if (_page is not null && (!_giveContext || !string.IsNullOrWhiteSpace(ContextInput.Text))) { Result=_page; DialogResult=true; } },true); _analyze.IsEnabled=false;
        var actions=new DockPanel { Margin=new(0,15,0,0) }; DockPanel.SetDock(_paste,Dock.Left); actions.Children.Add(_paste);
        var right=Ui.Row(cancel,_analyze); right.HorizontalAlignment=HorizontalAlignment.Right; actions.Children.Add(right); bottom.Children.Add(actions);
        DockPanel.SetDock(bottom,Dock.Bottom); page.Children.Add(bottom);
        TextPreview.IsReadOnly=true; TextPreview.MaxLength=0; TextPreview.VerticalContentAlignment=VerticalAlignment.Top; TextPreview.Padding=new(16); TextPreview.FontSize=14;
        page.Children.Add(Ui.WithPlaceholder(TextPreview,"El texto del artículo aparecerá aquí. No se descarga ningún recurso visual de la página.")); Content=page;
        AddressInput.PreviewKeyDown+=async(_,e)=> { if (e.Key==Key.Enter) { e.Handled=true; await ReadAsync(); } };
        AddressInput.TextChanged+=(_,_)=> { _page=null; TextPreview.Clear(); UpdateAnalyze(); };
        ContextInput.TextChanged+=(_,_)=>UpdateAnalyze();
        Loaded+=(_,_)=> { NativeMethods.FitToWorkArea(this,true); AddressInput.Focus(); };
        Closed+=(_,_)=> { _closed=true; _request?.Cancel(); _reader.Dispose(); };
    }
    private void UpdateAnalyze()=>_analyze.IsEnabled=_page is not null && _request is null && (!_giveContext || !string.IsNullOrWhiteSpace(ContextInput.Text));
    private void PasteText()
    {
        _page=null; TextPreview.Clear();
        try
        {
            var text=Clipboard.ContainsText()?Clipboard.GetText():"";
            _page=WebTextService.FromPlainText(text,AddressInput.Text,"");
            TextPreview.Text=_page.Text; _status.Text=$"Texto pegado · {_page.Text.Length:N0} caracteres. Comprueba el contenido antes de analizar.";
        }
        catch(Exception ex) { _status.Text=ex.Message; }
        UpdateAnalyze();
    }
    internal async Task ReadAsync()
    {
        if (_request is not null) { _request.Cancel(); return; }
        _page=null; TextPreview.Clear(); _request=new(); _read.Content="Detener lectura"; AddressInput.IsReadOnly=true; _paste.IsEnabled=false; UpdateAnalyze();
        _status.Text="Descargando únicamente el HTML y extrayendo el artículo…";
        try
        {
            var result=await _reader.ReadAsync(AddressInput.Text,_request.Token);
            if (_closed) return; AddressInput.Text=result.Url; _page=result; TextPreview.Text=result.Text;
            _status.Text=$"{result.Title} · {result.Text.Length:N0} caracteres · {Math.Ceiling(result.DownloadedBytes/1024.0):N0} KB descargados. Revisa que el artículo esté completo.";
        }
        catch(OperationCanceledException) { if (!_closed) _status.Text="Lectura cancelada. Puedes intentar de nuevo."; }
        catch(Exception ex) { if (!_closed) _status.Text=ex.Message; }
        finally { _request.Dispose(); _request=null; if (!_closed) { _read.Content="Leer página"; AddressInput.IsReadOnly=false; _paste.IsEnabled=true; UpdateAnalyze(); } }
    }
}
