using Mindie.Services;
using Mindie.Views;
using System.Windows.Automation;

namespace Mindie.Controls;

internal sealed class AiProviderSettings:UserControl
{
    private readonly AppHost _host;
    private readonly TextBlock _keyState=Ui.Text("",12,Ui.Muted);
    private readonly PasswordBox _key=new() { MinHeight=44,Padding=new(12,10,12,10),FontSize=14,MaxLength=2000 };
    private readonly DispatcherTimer _keyTimer=new() { Interval=TimeSpan.FromMilliseconds(500) };
    private readonly CancellationTokenSource _lifetime=new();
    private bool _clearing,_closed;
    internal ComboBox ProviderPicker { get; }=new() { MinHeight=48,IsEditable=false };
    internal AiProviderSettings(AppHost host,UIElement local,Func<Action<Settings>,Task> save)
    {
        _host=host;
        ProviderPicker.Style=(Style)System.Windows.Application.Current.FindResource("ModelSelect");
        var ollama=new ComboBoxItem { Content="Ollama local · modelos instalados",Tag="ollama" };
        var nvidia=new ComboBoxItem { Content="NVIDIA API · Gemma 4",Tag="nvidia" };
        ProviderPicker.Items.Add(ollama); ProviderPicker.Items.Add(nvidia); ProviderPicker.SelectedItem=host.Settings.IsNvidia?nvidia:ollama;
        AutomationProperties.SetName(ProviderPicker,"Proveedor de inteligencia artificial");
        AutomationProperties.SetName(_key,"Clave API de NVIDIA");
        var remove=Ui.Button("Eliminar clave",()=> {
            _keyTimer.Stop();
            try { host.Ollama.Keys.Delete(); _clearing=true; _key.Clear(); _clearing=false; UpdateKeyState(); _=host.CheckConnectionAsync(); }
            catch(Exception ex) { ShowKeyError(ex.Message); }
        });
        var test=Ui.AsyncButton("Probar conexión con NVIDIA",async()=> {
            FlushPending();
            if(_key.Password.Length>0) return;
            _keyState.Text="Comprobando NVIDIA con una consulta de prueba…";
            try { await host.Ollama.TestNvidiaAsync(_lifetime.Token); _keyState.Text="Conexión verificada · Gemma 4 disponible"; _keyState.Foreground=Ui.Accent; await host.CheckConnectionAsync(); }
            catch(OperationCanceledException) { }
            catch(Exception ex) { ShowKeyError(ex.Message); }
        });
        remove.Margin=new(0,0,10,0);
        var remote=Ui.Column(
            Ui.Card(Ui.Column(Ui.Text("Gemma 4 · NVIDIA API",15,weight:FontWeights.SemiBold),Ui.Space(5),Ui.Text(Settings.NvidiaModel,12,Ui.Muted),Ui.Space(5),Ui.Text("Texto · Imágenes · Razonamiento",12,Ui.Accent)),16),
            Ui.Space(14),Ui.Text("La inferencia se realiza en NVIDIA. Se envían tu pregunta y el texto o las imágenes necesarios para la acción. Requiere Internet y está sujeto a los límites de tu cuenta. El dictado sigue utilizando Windows.",12,Ui.Muted),
            Ui.Space(14),Ui.Field("Clave API",_key,"Pega solo la clave. Se guarda automáticamente en el Administrador de credenciales de Windows; nunca en la biblioteca ni en sus exportaciones."),
            _keyState,Ui.Space(12),Ui.Row(remove,test));
        var localHost=new ContentControl();
        void UpdatePanels() { localHost.Content=host.Settings.IsNvidia?null:local; remote.Visibility=host.Settings.IsNvidia?Visibility.Visible:Visibility.Collapsed; }
        UpdatePanels(); UpdateKeyState();
        ProviderPicker.SelectionChanged+=async(_,_)=> {
            if(ProviderPicker.SelectedItem is not ComboBoxItem { Tag:string provider } || provider==host.Settings.Provider) return;
            ProviderPicker.IsEnabled=false;
            try {
                FlushPending(); await save(s=>s.Provider=provider);
                if(host.Settings.Provider!=provider) { ProviderPicker.SelectedItem=host.Settings.IsNvidia?nvidia:ollama; return; }
                UpdatePanels(); await host.CheckConnectionAsync();
            }
            catch(Exception ex) { ShowKeyError(ex.Message); }
            finally { ProviderPicker.IsEnabled=true; }
        };
        _key.PasswordChanged+=(_,_)=> { if(_clearing) return; _keyTimer.Stop(); if(_key.Password.Length>0) { _keyState.Text="Guardando la nueva clave al terminar de escribir…"; _keyTimer.Start(); } };
        _keyTimer.Tick+=(_,_)=>FlushPending(); _key.LostKeyboardFocus+=(_,_)=>FlushPending();
        Content=Ui.Column(Ui.Field("Proveedor",ProviderPicker,"Elige dónde se procesa tu consulta. Puedes cambiar sin perder tus recuerdos ni conversaciones."),localHost,remote);
        Unloaded+=(_,_)=> { FlushPending(); _closed=true; _lifetime.Cancel(); };
    }
    internal void FlushPending()
    {
        _keyTimer.Stop(); if(_clearing || _key.Password.Length==0) return;
        try {
            _host.Ollama.Keys.Save(_key.Password);
            _clearing=true; _key.Clear(); _clearing=false; UpdateKeyState();
            if(!_closed && _host.Settings.IsNvidia) _= _host.CheckConnectionAsync();
        }
        catch(Exception ex) { _clearing=false; ShowKeyError(ex.Message); }
    }
    private void UpdateKeyState()
    {
        try { _keyState.Text=string.IsNullOrWhiteSpace(_host.Ollama.Keys.Read())?"Aún no hay una clave configurada.":"Clave guardada en Windows. Pega otra para reemplazarla."; _keyState.Foreground=Ui.Muted; }
        catch(Exception ex) { ShowKeyError(ex.Message); }
    }
    private void ShowKeyError(string error) { _keyState.Text=error; _keyState.Foreground=Ui.Brush("#A02D3C"); }
}
