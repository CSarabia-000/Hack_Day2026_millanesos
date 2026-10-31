using Mindie.Services;
using Mindie.Views;
using Mindie.Windows;
using Forms = System.Windows.Forms;
using Microsoft.Win32;

namespace Mindie;

public sealed partial class AppHost : Observable, IDisposable
{
    public LibraryStore Store { get; }
    public OllamaService Ollama { get; }
    public SpeechService Speech { get; } = new();
    public CaptureService Capture { get; } = new();
    public Settings Settings => Store.Data.Settings;
    public PetWindow? PetWindow { get; private set; }
    public MainWindow? MainWindow { get; set; }
    private CancellationTokenSource? _request;
    private Forms.NotifyIcon? _tray;
    private bool _busy;
    private bool _capturing;
    private QuickPromptWindow? _quickPrompt;
    private QuickChatWindow? _quickConversation;
    internal bool IsQuickPromptOpen=>_quickPrompt?.IsVisible==true;
    private string _connection = "Comprobando conexión…";
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) Changed?.Invoke(this, EventArgs.Empty); } }
    public string Connection { get => _connection; private set => Set(ref _connection, value); }
    public string ProcessingLabel => Settings.IsNvidia ? "IA remota con NVIDIA · Biblioteca en tu equipo." : "Procesado localmente con Ollama.";
    public OllamaStatus? Status { get; private set; }
    public event EventHandler? Changed;
    public event EventHandler<string>? Notice;
    public bool Exiting { get; private set; }
    public AppHost(LibraryStore store):this(store,new OllamaService()) { }
    internal AppHost(LibraryStore store,OllamaService ollama) { Store=store; Ollama=ollama; store.EnsureConversations(); CurrentConversation=store.Data.Conversations.FirstOrDefault(c=>c.Id==store.Data.ActiveConversationId)??new(); }
    public void Report(string message) => Notice?.Invoke(this, message);
    public void Refresh() => Changed?.Invoke(this, EventArgs.Empty);
    public async Task CheckConnectionAsync(Settings? settings = null)
    {
        var selected=(settings??Settings).Copy(); var version=++_connectionVersion;
        Connection = "Comprobando conexión…";
        try { var status = await Ollama.StatusAsync(selected); if(version!=_connectionVersion || selected.Provider!=Settings.Provider || selected.Model!=Settings.Model || selected.Endpoint!=Settings.Endpoint) return; Status=status; Connection = status.Available ? (selected.IsNvidia?"NVIDIA · Clave configurada":"Ollama conectado") : (selected.IsNvidia?"NVIDIA · Configura tu clave":"Modelo no instalado"); }
        catch (Exception ex) { if(version!=_connectionVersion || selected.Provider!=Settings.Provider) return; Status = null; Connection = selected.IsNvidia?"NVIDIA sin conexión":"Ollama sin conexión"; Report(ex.Message); }
        Notify(nameof(Status));
    }
    private int _connectionVersion;
    public async Task SaveSettingsAsync(Settings settings)
    {
        LibraryStore.ValidateSettings(settings); var old = Store.Data.Settings; Store.Data.Settings = settings;
        try { await Store.SaveAsync(); } catch { Store.Data.Settings = old; throw; }
        if(settings.StartWithWindows!=old.StartWithWindows) {
            using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (settings.StartWithWindows) run.SetValue("Mindie", $"\"{Environment.ProcessPath}\" --minimized"); else run.DeleteValue("Mindie", false);
        }
        if(settings.Pet!=old.Pet || settings.PetSize!=old.PetSize || settings.AlwaysOnTop!=old.AlwaysOnTop || settings.ReduceMotion!=old.ReduceMotion || settings.PetLeft!=old.PetLeft || settings.PetTop!=old.PetTop) PetWindow?.ApplySettings();
        Notify(nameof(Settings)); Notify(nameof(ProcessingLabel)); Refresh();
    }
    public void ShowPet()
    {
        if (PetWindow is null) PetWindow = new(this);
        PetWindow.Show(); PetWindow.Touch(); Refresh();
    }
    public void HidePet() { PetWindow?.Hide(); Refresh(); }
    public void Open(string page = "home") { if (MainWindow is null) MainWindow = new(this); MainWindow.Show(); if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal; MainWindow.Activate(); MainWindow.Navigate(page); }
    public void QuickChat()
    {
        ShowPet(); PetWindow!.CloseActions(); _quickConversation?.Hide();
        if(_quickPrompt is not null) { _quickPrompt.Show(); _quickPrompt.Activate(); _quickPrompt.FocusInput(); return; }
        NewConversation();
        var input=new QuickPromptWindow(this,PetWindow); _quickPrompt=input;
        input.Closed+=(_,_)=> { if(ReferenceEquals(_quickPrompt,input)) _quickPrompt=null; };
        input.Show(); input.Activate();
    }
    internal void ShowQuickConversation(PetWindow origin,ChatConversation? conversation=null)
    {
        conversation??=CurrentConversation;
        if(_quickConversation is not null && _quickConversation.Conversation!=conversation) { _quickConversation.Close(); _quickConversation=null; }
        if(_quickConversation is null) {
            var chat=new QuickChatWindow(this,origin,conversation); _quickConversation=chat;
            chat.Closed+=(_,_)=> { if(ReferenceEquals(_quickConversation,chat)) _quickConversation=null; };
        }
        _quickConversation.Show(); if(_quickConversation.WindowState==WindowState.Minimized) _quickConversation.WindowState=WindowState.Normal;
        _quickConversation.Activate(); _quickConversation.FocusInput();
    }
    internal async Task<PreparedChatImage> CaptureScreenForChatAsync(Window origin,CancellationToken cancellation)
    {
        var status=await Ollama.StatusAsync(Settings.Copy(),cancellation);
        if(!status.Available) throw new InvalidOperationException("Configura una clave NVIDIA o elige un modelo instalado en Configuración → Inteligencia artificial.");
        if(!status.Capabilities.Contains("vision")) throw new InvalidOperationException("El modelo seleccionado no admite imágenes. Elige uno con Imágenes en Configuración → Inteligencia artificial.");
        return await Capture.ScreenAsync(origin,cancellation);
    }
    internal void SetModelCatalog(IReadOnlyList<LocalModelInfo> models)
    {
        if(Settings.IsNvidia) return;
        var selected=models.FirstOrDefault(m=>m.Name==Settings.Model);
        Status=new(selected is not null,models.Select(m=>m.Name).ToArray(),selected?.Capabilities??[]);
        Connection=selected is not null?"Ollama conectado":"Modelo no instalado"; Notify(nameof(Status));
    }
    public async Task SendAsync(string prompt,IReadOnlyList<PreparedChatImage>? images=null,CancellationToken cancellation=default,ChatConversation? conversation=null)
    {
        if (IsBusy) throw new InvalidOperationException("Espera a que termine la respuesta o pulsa Detener.");
        images??=[]; prompt = prompt.Trim(); if(prompt.Length==0 && images.Count>0) prompt="Describe brevemente estas imágenes.";
        if (prompt.Length is 0 or > 8000) throw new ArgumentException("Escribe un mensaje de hasta 8000 caracteres.");
        conversation??=CurrentConversation; var history=conversation.Messages;
        if(conversation.IsDeleted) throw new InvalidOperationException("Esta conversación se eliminó del Historial. Inicia una nueva para continuar.");
        var previous=history.ToArray(); var previousStored=Store.Data.Messages; var imagesSaved=false;
        var previousScope=conversation.Scope; var previousTitle=conversation.Title; var previousDate=conversation.UpdatedAt; var added=false;
        var previousScreenImages=conversation.ScreenImages;
        _busyConversationId=conversation.Id; _busyMemoryId=conversation.MemoryId;
        var settings=Settings.Copy();
        IsBusy = true; _request = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        try
        {
            await Store.SaveChatImagesAsync(images,_request.Token); imagesSaved=true; _request.Token.ThrowIfCancellationRequested();
            var message = new ChatMessage { Content = prompt,Images=images.Select(i=>i.Image).ToList() }; history.Add(message);
            if(images.Any(i=>i.Image.IsScreenCapture)) { conversation.Scope="screen"; conversation.ScreenImages=images.Where(i=>i.Image.IsScreenCapture).Select(i=>i.Image).ToList(); }
            ChatMessage response;
            if(conversation.Scope=="memory") {
                var memory=ConversationMemory(conversation)??throw new InvalidOperationException("Este recuerdo ya no está en Mis guardados.");
                byte[]? image=null;
                if(memory.SourceText.Length==0 && File.Exists(Store.CapturePath(memory.Id)) && (await Ollama.StatusAsync(settings,_request.Token)).Capabilities.Contains("vision")) image=ChatImageService.Prepare(Images.Decode(await File.ReadAllBytesAsync(Store.CapturePath(memory.Id),_request.Token)),"Recuerdo.png").Png;
                response=await Ollama.AskMemoryAsync(settings,memory,history,image,_request.Token);
            }
            else response = await Ollama.ChatAsync(settings, history, _request.Token,Store.ReadChatImageAsync,conversation);
            _request.Token.ThrowIfCancellationRequested(); history.Add(response); while (history.Count > 100) history.RemoveAt(0);
            if(previous.Length==0 && conversation.Scope!="memory") conversation.Title=prompt.Length>64?prompt[..64]+"…":prompt;
            conversation.UpdatedAt=DateTimeOffset.Now;
            if(!Store.Data.Conversations.Contains(conversation)) { Store.Data.Conversations.Add(conversation); added=true; }
            Store.Data.ActiveConversationId=CurrentConversation.Id; Store.Data.Messages = CurrentConversation.Messages.ToList(); await Store.SaveAsync(); if (Settings.Speak) Speech.Speak(response.Content);
            var retained=Store.Data.Conversations.SelectMany(c=>c.Messages.SelectMany(m=>m.Images).Concat(c.ScreenImages)).Select(i=>i.Id).ToHashSet(StringComparer.Ordinal);
            Store.DeleteChatImages(previous.SelectMany(m=>m.Images).Concat(previousScreenImages).Where(i=>!retained.Contains(i.Id)));
        }
        catch {
            history.Clear(); foreach(var message in previous) history.Add(message); Store.Data.Messages=previousStored;
            conversation.Scope=previousScope; conversation.ScreenImages=previousScreenImages; conversation.Title=previousTitle; conversation.UpdatedAt=previousDate; if(added) Store.Data.Conversations.Remove(conversation);
            if(imagesSaved) Store.DeleteChatImages(images.Select(i=>i.Image)); throw;
        }
        finally { _request.Dispose(); _request = null; _busyConversationId=null; _busyMemoryId=null; IsBusy = false; }
    }
    public async Task<MemoryItem> AnalyzeAsync(byte[] png, string context = "", IProgress<string>? progress = null)
    {
        if (IsBusy) throw new InvalidOperationException("Termina o cancela la respuesta actual antes de analizar otra captura.");
        IsBusy = true; _request = new();
        try { return await Ollama.AnalyzeAsync(Settings, png, _request.Token, context, progress); }
        finally { _request.Dispose(); _request = null; IsBusy = false; }
    }
    public void Cancel() => _request?.Cancel();
    public async Task<MemoryItem> AnalyzeWebTextAsync(WebPageText page,string context,IProgress<string>? progress=null)
    {
        if (IsBusy) throw new InvalidOperationException("Termina o cancela la operación actual antes de analizar el texto.");
        IsBusy=true; _request=new();
        try { return await Ollama.AnalyzeWebTextAsync(Settings,page,context,_request.Token,progress); }
        finally { _request.Dispose(); _request=null; IsBusy=false; }
    }
    public async Task<MemoryItem> AnalyzeWebAsync(byte[] png,string context,IProgress<string>? progress=null)
    {
        if (IsBusy) throw new InvalidOperationException("Termina o cancela la operación actual antes de analizar la página.");
        IsBusy=true; _request=new();
        try { return await Ollama.AnalyzeWebAsync(Settings,png,context,_request.Token,progress); }
        finally { _request.Dispose(); _request=null; IsBusy=false; }
    }
    public async Task ExportAsync()
    {
        var dialog = new SaveFileDialog { Title = "Exportar recuerdos", Filter = "Biblioteca JSON|*.json", FileName = "mis-recuerdos-mindie.json" };
        if (dialog.ShowDialog(MainWindow) == true) { await Store.ExportAsync(dialog.FileName); Report("Biblioteca exportada con sus capturas."); }
    }
    public void CreateTray()
    {
        using var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Mindie.ico")).Stream;
        _tray = new Forms.NotifyIcon { Text = "Mindie · compañero de escritorio", Icon = new System.Drawing.Icon(iconStream), Visible = true };
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("Abrir Mindie", null, (_, _) => Open()); menu.Items.Add("Mostrar compañero", null, (_, _) => ShowPet());
        menu.Items.Add("Guardar en mente", null, async (_, _) => await Ui.Safe(CaptureAsync)); menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("Salir", null, (_, _) => Exit());
        _tray.ContextMenuStrip = menu; _tray.DoubleClick += (_, _) => Open();
    }
    internal void CloseWindows() { Exiting=true; Cancel(); _quickPrompt?.Close(); _quickConversation?.Close(); foreach(var window in System.Windows.Application.Current.Windows.OfType<MemoryChatWindow>().ToArray()) if(Store.Data.Conversations.Contains(window.Conversation) || window.Conversation.MemoryId is string id && Store.Data.Memories.Any(m=>m.Id==id)) window.Close(); PetWindow?.Close(); MainWindow?.Close(); }
    public async void Exit() { if(Exiting) return; Exiting = true; if(MainWindow?.PageHost.Content is SettingsView settings) await settings.FlushPendingAsync(); CloseWindows(); Dispose(); System.Windows.Application.Current.Shutdown(); }
    public void Dispose() { _tray?.Dispose(); Speech.Dispose(); Ollama.Dispose(); }
}
