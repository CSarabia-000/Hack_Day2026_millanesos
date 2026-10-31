using Mindie.Services;
using Mindie.Views;
using System.Windows.Automation;
using System.Windows.Data;

namespace Mindie.Controls;

internal sealed class LocalModelPicker:UserControl
{
    private readonly AppHost _host;
    private readonly Func<string,Task> _save;
    private readonly Func<Task> _flush;
    private readonly Func<string> _endpoint;
    private readonly Func<Settings,CancellationToken,Task<IReadOnlyList<LocalModelInfo>>> _discover;
    private readonly TextBlock _connection=Ui.Text("Buscando modelos instalados…",12,Ui.Muted);
    private readonly Button _refresh;
    private CancellationTokenSource? _request;
    private bool _updating,_loaded;
    private int _selectionVersion;
    private string _catalogEndpoint;
    internal ComboBox Selector { get; }=new() { IsEditable=false,MinHeight=62,MaxDropDownHeight=340,HorizontalContentAlignment=HorizontalAlignment.Stretch };
    internal Task Loading { get; private set; }=Task.CompletedTask;
    internal LocalModelPicker(AppHost host,Func<string,Task> save,Func<Task> flush,Func<string> endpoint,Func<Settings,CancellationToken,Task<IReadOnlyList<LocalModelInfo>>>? discover=null)
    {
        _host=host; _save=save; _flush=flush; _endpoint=endpoint; _discover=discover??host.Ollama.ListLocalModelsAsync; _catalogEndpoint=host.Settings.Endpoint;
        AutomationProperties.SetName(Selector,"Modelo de Ollama"); AutomationProperties.SetLiveSetting(_connection,AutomationLiveSetting.Polite);
        Selector.Style=(Style)System.Windows.Application.Current.FindResource("ModelSelect");
        var stack=new FrameworkElementFactory(typeof(StackPanel)); stack.SetValue(MarginProperty,new Thickness(2,3,12,3));
        var name=new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty,new Binding(nameof(LocalModelInfo.Name))); name.SetValue(TextBlock.ForegroundProperty,Ui.Ink); name.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold); name.SetValue(TextBlock.FontSizeProperty,13.0); stack.AppendChild(name);
        var caps=new FrameworkElementFactory(typeof(TextBlock)); caps.SetBinding(TextBlock.TextProperty,new Binding(nameof(LocalModelInfo.CapabilityText))); caps.SetValue(TextBlock.ForegroundProperty,Ui.Muted); caps.SetValue(TextBlock.FontSizeProperty,12.0); caps.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap); stack.AppendChild(caps);
        Selector.ItemTemplate=new DataTemplate { VisualTree=stack };
        var items=new Style(typeof(ComboBoxItem)); items.Setters.Add(new Setter(IsEnabledProperty,new Binding(nameof(LocalModelInfo.CanChat)))); items.Setters.Add(new Setter(HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch)); Selector.ItemContainerStyle=items;
        _refresh=Ui.AsyncButton("Actualizar modelos",async()=>await RefreshAsync());
        Content=Ui.Column(Ui.Field("Nombre del modelo",Selector,"Modelos instalados en este servidor. Las opciones sin conversación aparecen desactivadas."),Ui.Space(4),_refresh,Ui.Space(8),_connection,
            Ui.Space(8),Ui.Text("Capacidades reportadas por Ollama. El dictado de Mindie utiliza el reconocimiento de voz de Windows.",12,Ui.Muted));
        Selector.SelectionChanged+=async(_,_)=> {
            if(_updating || Selector.SelectedItem is not LocalModelInfo model || !model.CanChat) return;
            var version=++_selectionVersion;
            try {
                await _save(model.Name); if(version!=_selectionVersion || !_loaded) return;
                if(_host.Settings.Model!=model.Name) throw new InvalidOperationException("No se pudo guardar el modelo seleccionado.");
                _host.SetModelCatalog(Selector.Items.OfType<LocalModelInfo>().Where(m=>m.Installed).ToArray()); _connection.Text=_host.Connection;
            }
            catch(Exception ex) { if(version==_selectionVersion && _loaded) _connection.Text=ex.Message; }
        };
        Loaded+=(_,_)=> { if(!_loaded) { _loaded=true; _host.PropertyChanged+=HostChanged; Loading=RefreshAsync(); } };
        Unloaded+=(_,_)=> { _loaded=false; _selectionVersion++; _host.PropertyChanged-=HostChanged; _request?.Cancel(); };
    }
    private void HostChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(AppHost.Settings) && _host.Settings.Endpoint!=_catalogEndpoint) { _catalogEndpoint=_host.Settings.Endpoint; Loading=RefreshAsync(); }
    }
    internal async Task RefreshAsync()
    {
        _request?.Cancel(); var request=new CancellationTokenSource(); _request=request; _refresh.IsEnabled=false; Selector.IsEnabled=false;
        try {
            await _flush(); var settings=_host.Settings.Copy(); settings.Endpoint=_endpoint().Trim(); _=LibraryStore.ValidateEndpoint(settings.Endpoint);
            _connection.Text="Leyendo modelos y capacidades…";
            var models=await _discover(settings,request.Token); request.Token.ThrowIfCancellationRequested();
            _updating=true;
            var choices=models.ToList(); var current=choices.FirstOrDefault(m=>m.Name==_host.Settings.Model);
            if(current is null) { current=new(_host.Settings.Model,[],false,false); choices.Insert(0,current); }
            Selector.ItemsSource=choices; Selector.SelectedItem=current;
            Selector.IsEnabled=models.Any(m=>m.CanChat);
            _host.SetModelCatalog(models); _connection.Text=models.Count==0?"No hay modelos locales instalados. Instala uno en Ollama y actualiza esta lista.":$"{models.Count} modelos locales · {_host.Connection}";
        }
        catch(OperationCanceledException) { }
        catch(Exception ex) { _connection.Text="No se pudieron cargar los modelos. "+ex.Message+" Usa Actualizar modelos para reintentar."; }
        finally { if(ReferenceEquals(_request,request)) { _refresh.IsEnabled=true; _updating=false; _request=null; } request.Dispose(); }
    }
}
