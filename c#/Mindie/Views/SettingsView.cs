using Mindie.Controls;
using System.Windows.Automation;

namespace Mindie.Views;

public sealed class SettingsView:UserControl
{
    private readonly AppHost _host;
    private readonly SemaphoreSlim _gate=new(1);
    private readonly TextBlock _status=Ui.Text("Los cambios se guardan automáticamente.",12,Ui.Accent);
    private readonly List<(DispatcherTimer Timer,Action Commit)> _pending=[];
    private readonly AiProviderSettings _ai;
    internal Task LastSave { get; private set; }=Task.CompletedTask;
    public SettingsView(AppHost host):this(host,null) { }
    internal SettingsView(AppHost host,Func<Settings,CancellationToken,Task<IReadOnlyList<Services.LocalModelInfo>>>? discover)
    {
        _host=host; var s=host.Settings;
        AutomationProperties.SetLiveSetting(_status,AutomationLiveSetting.Polite);
        var heading=Ui.Heading("Configuración","Haz que Mindie se adapte a ti.");
        var page=Ui.Column(heading); var notice=Ui.Card(_status,14); notice.Margin=new(0,0,0,22); page.Children.Add(notice);
        var name=Ui.Input(s.PetName,"Nombre del compañero"); name.MaxLength=24;
        var nameField=Preference("Nombre",name,null,v=> { if(string.IsNullOrWhiteSpace(v)) throw new ArgumentException("El nombre no puede estar vacío."); return x=>x.PetName=v; });
        var preview=new SpritePlayer { PetId=s.Pet,Width=84,Height=84,Animate=false };
        var sizeLabel=Ui.Text($"{s.PetSize:0} px",12,Ui.Muted);
        var size=new Slider { Minimum=Settings.MinPetSize,Maximum=Settings.MaxPetSize,Value=s.PetSize,TickFrequency=8,IsSnapToTickEnabled=true,Margin=new(0,8,0,8) };
        AutomationProperties.SetName(size,"Tamaño del animal en el escritorio");
        var sizeTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(250) };
        void SizeCommit() { var value=size.Value; Queue(x=>x.PetSize=value); }
        sizeTimer.Tick+=(_,_)=> { sizeTimer.Stop(); SizeCommit(); }; _pending.Add((sizeTimer,SizeCommit));
        size.ValueChanged+=(_,_)=> { sizeLabel.Text=$"{size.Value:0} px"; sizeTimer.Stop(); sizeTimer.Start(); _status.Text="Ajustando tamaño…"; };
        page.Children.Add(Section("Tu compañero","Nombre, tamaño y presencia en el escritorio.",Ui.Column(
            nameField,Ui.Field("Tamaño",UiControl(size,sizeLabel),"Pequeño: 88 px · Grande: 224 px"),
            Toggle("Siempre encima de otras ventanas","Mantiene visible a la mascota mientras trabajas.",s.AlwaysOnTop,x=>v=>v.AlwaysOnTop=x),
            Toggle("Reducir el movimiento","Usa poses estáticas y pausa el ambiente del cerebro.",s.ReduceMotion,x=>v=>v.ReduceMotion=x)),preview));
        page.Children.Add(Section("Conversación y capturas","Elige cómo quieres recibir ayuda.",Ui.Column(
            Toggle("Respuestas detalladas","Hasta 150 palabras. Desactivado: respuestas breves.",s.Specific,x=>v=>v.Specific=x),
            Toggle("Leer respuestas en voz alta","Mindie utiliza la voz disponible en Windows.",s.Speak,x=>v=>v.Speak=x),
            Toggle("Dar contexto antes de analizar una captura","Añade lo que estás viendo y qué quieres recordar. Sin límite fijo de texto.",s.GiveContext,x=>v=>v.GiveContext=x))));
        var endpoint=Ui.Input(s.Endpoint,"Dirección local de Ollama");
        var endpointField=Preference("Servidor de Ollama",endpoint,"Normalmente http://127.0.0.1:11434",v=> { _=Services.LibraryStore.ValidateEndpoint(v.Trim()); return x=>x.Endpoint=v.Trim(); });
        var models=new LocalModelPicker(host,async value=> { Queue(x=>x.Model=value); await LastSave; },FlushPendingAsync,()=>endpoint.Text,discover);
        _ai=new AiProviderSettings(host,Ui.Column(endpointField,models),change=> { Queue(change); return LastSave; });
        page.Children.Add(Section("Inteligencia artificial","Ollama en tu equipo o Gemma 4 mediante NVIDIA API.",_ai));
        page.Children.Add(Section("Windows y voz","Preferencias de inicio y dictado.",Ui.Column(
            Toggle("Abrir Mindie al iniciar Windows","El compañero estará disponible desde la bandeja del sistema.",s.StartWithWindows,x=>v=>v.StartWithWindows=x),
            Ui.Text(host.Speech.Available?"Dictado en español disponible.":"Para dictar, instala reconocimiento de voz en español desde los ajustes de Voz de Windows.",12,Ui.Muted))));
        var folder=Ui.Button("Abrir carpeta de datos",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",host.Store.Root) { UseShellExecute=true }));
        var export=Ui.AsyncButton("Exportar biblioteca",host.ExportAsync); export.Margin=new(0,8,0,0);
        page.Children.Add(Section("Tus datos",$"{host.Store.Data.Memories.Count} recuerdos guardados en este equipo.",Ui.Column(Ui.Text(host.Store.Root,12,Ui.Muted),Ui.Space(12),folder,export)));
        Content=Ui.Scroll(page);
        Unloaded+=(_,_)=>FlushPending();
    }
    internal void FlushPending() { foreach(var (timer,commit) in _pending) if(timer.IsEnabled) { timer.Stop(); commit(); } _ai.FlushPending(); }
    internal async Task FlushPendingAsync() { FlushPending(); await LastSave; }
    private static Control UiControl(Slider slider,TextBlock label)=>new ContentControl { Content=Ui.Column(slider,label) };
    private UIElement Preference(string label,TextBox input,string? hint,Func<string,Action<Settings>> update)
    {
        var error=Ui.Text("",12,Ui.Brush("#A02D3C")); error.Visibility=Visibility.Collapsed; error.Margin=new(0,-9,0,14); AutomationProperties.SetLiveSetting(error,AutomationLiveSetting.Assertive);
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(450) };
        void Commit() {
            try { var change=update(input.Text); error.Visibility=Visibility.Collapsed; input.ClearValue(TextBox.BorderBrushProperty); Queue(change); }
            catch(Exception ex) { error.Text=ex.Message; error.Visibility=Visibility.Visible; input.BorderBrush=Ui.Brush("#A02D3C"); _status.Text="Hay un campo pendiente de corregir. Los demás cambios se guardan."; _status.Foreground=Ui.Brush("#A02D3C"); }
        }
        timer.Tick+=(_,_)=> { timer.Stop(); Commit(); }; _pending.Add((timer,Commit));
        input.TextChanged+=(_,_)=> { timer.Stop(); timer.Start(); _status.Text="Guardando al terminar de escribir…"; };
        input.LostKeyboardFocus+=(_,_)=> { if(timer.IsEnabled) { timer.Stop(); Commit(); } };
        return Ui.Column(Ui.Field(label,input,hint),error);
    }
    private void Queue(Action<Settings> update)=>LastSave=SaveAsync(update);
    private async Task SaveAsync(Action<Settings> update)
    {
        await _gate.WaitAsync();
        try {
            _status.Text="Guardando…"; var next=_host.Settings.Copy(); update(next);
            await _host.SaveSettingsAsync(next); _status.Text="Guardado automáticamente"; _status.Foreground=Ui.Accent;
        }
        catch(Exception ex) { _status.Text="No se pudo guardar: "+ex.Message; _status.Foreground=Ui.Brush("#A02D3C"); }
        finally { _gate.Release(); }
    }
    private UIElement Toggle(string title,string description,bool value,Func<bool,Action<Settings>> change)
    {
        var toggle=new CheckBox { IsChecked=value,Style=(Style)System.Windows.Application.Current.FindResource("SettingsToggle"),Content=Ui.Column(Ui.Text(title,14,weight:FontWeights.SemiBold),Ui.Text(description,12,Ui.Muted)) };
        AutomationProperties.SetName(toggle,title);
        AutomationProperties.SetHelpText(toggle,description);
        toggle.Checked+=(_,_)=>Queue(change(true)); toggle.Unchecked+=(_,_)=>Queue(change(false));
        return toggle;
    }
    private static UIElement Section(string title,string description,UIElement form,UIElement? preview=null)
    {
        var header=Ui.Column(Ui.Text(title,19,weight:FontWeights.SemiBold),Ui.Space(3),Ui.Text(description,13,Ui.Muted));
        var grid=new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width=GridLength.Auto }); grid.Children.Add(header);
        if(preview is not null) { Grid.SetColumn(preview,1); grid.Children.Add(preview); }
        var content=Ui.Column(grid,Ui.Space(16),form); var card=Ui.Card(content,22); card.Margin=new(0,0,0,18); return card;
    }
}
