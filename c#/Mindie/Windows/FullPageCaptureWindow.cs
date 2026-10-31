using Mindie.Services;
using Mindie.Views;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mindie.Windows;

public sealed class FullPageCaptureWindow : Window
{
    internal WebView2 Browser { get; }=new();
    internal TextBox AddressInput { get; }=Ui.Input("","Dirección de la página web");
    public WebPageCapture? Result { get; private set; }
    private readonly string _profile;
    private readonly Button _open;
    private readonly Button _capture;
    private readonly TextBlock _status=Ui.Text("Preparando la vista de la página…",12,Ui.Muted);
    private CancellationTokenSource? _request;
    private bool _closed;
    internal Task Initialization { get; private set; }=Task.CompletedTask;
    internal CoreWebView2Environment? BrowserEnvironment { get; private set; }
    public FullPageCaptureWindow(string root)
    {
        Ui.WindowStyle(this); _profile=Path.Combine(root,"web-profile"); Title="Mindie · Página web completa"; Width=1150; Height=820; MinWidth=720; MinHeight=500; WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var page=new DockPanel { Margin=new(22) }; var heading=Ui.Heading("Guarda la página completa.","Pega la URL, abre la página y revisa su contenido. Cuando esté lista, captura todo, incluido lo que queda fuera de la vista."); DockPanel.SetDock(heading,Dock.Top); page.Children.Add(heading);
        var tools=new Grid { Margin=new(0,0,0,12) }; tools.ColumnDefinitions.Add(new()); tools.ColumnDefinitions.Add(new() { Width=GridLength.Auto }); tools.ColumnDefinitions.Add(new() { Width=GridLength.Auto }); tools.Children.Add(AddressInput);
        _open=Ui.Button("Abrir página",()=>Navigate()); _open.Margin=new(10,0,10,0); _open.IsEnabled=false; Grid.SetColumn(_open,1); tools.Children.Add(_open);
        _capture=Ui.Button("Capturar todo",async()=>await CaptureAsync(),true); _capture.IsEnabled=false; Grid.SetColumn(_capture,2); tools.Children.Add(_capture); DockPanel.SetDock(tools,Dock.Top); page.Children.Add(tools);
        _status.Margin=new(0,0,0,12); DockPanel.SetDock(_status,Dock.Top); page.Children.Add(_status); page.Children.Add(Browser); Content=page;
        AddressInput.PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Enter && _open.IsEnabled) { Navigate(); e.Handled=true; } };
        Loaded+=async(_,_)=> { NativeMethods.FitToWorkArea(this,true); Initialization=InitializeAsync(); await Initialization; };
        Closed+=(_,_)=> { _closed=true; _request?.Cancel(); Browser.Dispose(); };
    }
    private async Task InitializeAsync()
    {
        try
        {
            BrowserEnvironment=await CoreWebView2Environment.CreateAsync(null,_profile,new CoreWebView2EnvironmentOptions("--disable-gpu"));
            if (_closed) return;
            await Browser.EnsureCoreWebView2Async(BrowserEnvironment);
            Browser.CoreWebView2.Settings.AreDevToolsEnabled=false; Browser.CoreWebView2.Settings.IsStatusBarEnabled=false;
            Browser.CoreWebView2.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;
            Browser.CoreWebView2.DownloadStarting+=(_,e)=> { e.Cancel=true; _status.Text="Este espacio sirve para capturar páginas. Para un PDF, usa Añadir PDF en Mis guardados."; };
            Browser.CoreWebView2.NewWindowRequested+=(_,e)=> { e.Handled=true; if (IsWebUrl(e.Uri)) Browser.CoreWebView2.Navigate(e.Uri); };
            Browser.CoreWebView2.NavigationStarting+=(_,e)=> { if (!IsWebUrl(e.Uri) && e.Uri!="about:blank") e.Cancel=true; _capture.IsEnabled=false; };
            Browser.CoreWebView2.NavigationCompleted+=(_,e)=> { _capture.IsEnabled=e.IsSuccess && IsWebUrl(Browser.CoreWebView2.Source); _status.Text=e.IsSuccess?"Revisa la página; puedes cerrar avisos o iniciar sesión aquí antes de capturar.":"No pude abrir la página. Revisa la dirección o vuelve a intentarlo."; };
            _open.IsEnabled=true; _status.Text="Pega la dirección de la página que quieres guardar."; AddressInput.Focus();
        }
        catch (Exception ex) { _status.Text=ex is WebView2RuntimeNotFoundException ? "Se necesita Microsoft Edge WebView2 para capturar páginas completas. Instala su runtime y vuelve a abrir esta opción." : "No pude preparar la página: "+ex.Message; }
    }
    private static bool IsWebUrl(string value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri) && (uri.Scheme=="http" || uri.Scheme=="https") && string.IsNullOrEmpty(uri.UserInfo);
    private void Navigate()
    {
        try { var url=LibraryStore.ValidateSourceUrl(AddressInput.Text); if (url.Length==0) throw new ArgumentException("Escribe la URL de la página."); Browser.CoreWebView2.Navigate(url); _status.Text="Abriendo la página…"; }
        catch (Exception ex) { _status.Text=ex.Message; }
    }
    private async Task CaptureAsync()
    {
        if (_request is not null) { _request.Cancel(); return; }
        _request=new(); _open.IsEnabled=false; AddressInput.IsReadOnly=true; Browser.IsEnabled=false; _capture.Content="Detener captura";
        try { Result=await new FullPageCaptureService(Browser.CoreWebView2).CaptureAsync(_request.Token,new Progress<string>(text=>_status.Text=text)); if (!_closed) DialogResult=true; }
        catch (OperationCanceledException) { _status.Text="Captura cancelada. Puedes volver a intentarlo."; }
        catch (Exception ex) { _status.Text=ex.Message; }
        finally { _request.Dispose(); _request=null; if (!_closed) { _open.IsEnabled=true; AddressInput.IsReadOnly=false; Browser.IsEnabled=true; _capture.Content="Capturar todo"; } }
    }
}
