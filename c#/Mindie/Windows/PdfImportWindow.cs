using Mindie.Views;
using Mindie.Controls;

namespace Mindie.Windows;

public sealed class PdfImportWindow : Window
{
    private readonly AppHost _host;
    private readonly string _path;
    private readonly TextBlock _status = Ui.Text("Preparando el documento…",14);
    private readonly ThinkingOrb _orb=new() { Width=72,Height=72,Margin=new(0,0,18,0) };
    private readonly Button _retry;
    private bool _running;
    private bool _closed;
    public PdfImportWindow(AppHost host,string path)
    {
        Ui.WindowStyle(this);
        _host=host; _path=path; Title="Mindie · Añadir PDF"; Width=620; Height=380; MinWidth=450; MinHeight=300; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var cancel = Ui.Button("Cancelar",Close); _retry=Ui.Button("Volver a intentar",async()=>await RunAsync(),true); _retry.Visibility=Visibility.Collapsed; _retry.Margin=new(0,0,10,0);
        var actions=Ui.Row(_retry,cancel); actions.HorizontalAlignment=HorizontalAlignment.Right;
        var progress=new DockPanel { Margin=new(0,0,0,18) }; DockPanel.SetDock(_orb,Dock.Left); progress.Children.Add(_orb); _status.VerticalAlignment=VerticalAlignment.Center; progress.Children.Add(_status);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(_status,System.Windows.Automation.AutomationLiveSetting.Polite);
        Content=Ui.Scroll(Ui.Column(Ui.Heading("Un PDF para pensar juntos.",Path.GetFileName(path)),progress,Ui.Text("Se conservará una copia en Mis guardados. Los documentos largos o escaneados pueden tardar; puedes cancelar.",12,Ui.Muted),Ui.Space(20),actions));
        ((FrameworkElement)Content).Margin=new(28);
        void UpdateMotion(object? sender,EventArgs args)=>_orb.ReduceMotion=host.Settings.ReduceMotion;
        host.Changed+=UpdateMotion; UpdateMotion(null,EventArgs.Empty);
        Loaded+=async(_,_)=> { NativeMethods.FitToWorkArea(this,true); await RunAsync(); };
        Closing+=(_,_)=> { _closed=true; _orb.IsActive=false; if (_running) host.Cancel(); };
        Closed+=(_,_)=>host.Changed-=UpdateMotion;
    }
    private async Task RunAsync()
    {
        if (_running) return; _running=true; _retry.Visibility=Visibility.Collapsed; _orb.Visibility=Visibility.Visible; _orb.IsActive=true;
        try
        {
            var result=await _host.ImportPdfAsync(_path,new Progress<string>(text=>_status.Text=text));
            if (!_closed) { await _host.OpenPdfAsync(result.Item); Close(); }
            _host.Report("PDF guardado. Ya puedes hacer preguntas sobre él.");
        }
        catch (OperationCanceledException) { _status.Text="Importación cancelada."; }
        catch (Exception ex) { _status.Text=ex.Message; }
        finally { _running=false; _orb.IsActive=false; _orb.Visibility=Visibility.Collapsed; _retry.Visibility=Visibility.Visible; }
    }
}
