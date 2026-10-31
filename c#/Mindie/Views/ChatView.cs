using System.Collections.Specialized;
using System.Windows.Automation;
using Mindie.Services;

namespace Mindie.Views;

public sealed class ChatView : UserControl
{
    private readonly AppHost _host;
    private readonly ObservableCollection<ChatMessage> _history;
    private readonly MemoryItem? _document;
    private readonly PdfKnowledge? _knowledge;
    private readonly ChatConversation? _conversation;
    private readonly StackPanel _messages = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBox _prompt = Ui.Input("", "Tu mensaje", true);
    private readonly Button _send, _voice, _brief, _specific;
    private readonly TextBlock _error = Ui.Text("",12,Ui.Brush("#A02D3C"));
    private readonly TextBlock _subtitle;
    private readonly TextBlock _footnote;
    private readonly Border _sourceBadge;
    private readonly bool _compact;
    private CancellationTokenSource? _listening;
    private readonly Dictionary<ChatMessage,(string Role,string Text,ChatImage[] Images,Border Bubble)> _bubbles=[];
    private ChatMessage[] _displayed=[];
    private readonly ContentControl _thinking=new();
    private string _displayPet="",_displayName="";
    public ChatView(AppHost host, bool compact = false, MemoryItem? document = null, PdfKnowledge? knowledge = null,ChatConversation? conversation=null)
    {
        _host=host; _compact=compact; _document=document; _knowledge=knowledge;
        _conversation=document is null?conversation??host.CurrentConversation:null;
        _history=document is null?_conversation!.Messages:new(knowledge?.Messages??[]);
        var page=new Grid(); page.RowDefinitions.Add(new() { Height=GridLength.Auto });
        page.RowDefinitions.Add(new()); page.RowDefinitions.Add(new() { Height=GridLength.Auto });
        var heading=Ui.Text(document is null?(_conversation?.Scope=="memory"?"Conversa con tu recuerdo":"Conversación"):"Conversa con tu PDF",compact?19:28,weight:FontWeights.SemiBold);
        _subtitle=Ui.Text("",12,Ui.Muted);
        var header=new DockPanel { Margin=new(0,0,0,14) };
        if(document is null && !compact) { var create=Ui.Button("Nueva conversación",()=> { host.NewConversation(); host.MainWindow?.Navigate("chat"); }); DockPanel.SetDock(create,Dock.Right); header.Children.Add(create); }
        _sourceBadge=Ui.Badge(""); _sourceBadge.Margin=new(12,0,0,0); DockPanel.SetDock(_sourceBadge,Dock.Right); header.Children.Add(_sourceBadge);
        header.Children.Add(Ui.Column(heading,_subtitle));
        page.Children.Add(new Border { Child=header,BorderBrush=Ui.Line,BorderThickness=new(0,0,0,1),Margin=new(0,0,0,12) });
        _scroll=Ui.Scroll(_messages); _scroll.Padding=new(0,0,8,16); _messages.MaxWidth=800;
        _messages.HorizontalAlignment=HorizontalAlignment.Stretch; Grid.SetRow(_scroll,1); page.Children.Add(_scroll);
        var bottom=Ui.Column(); bottom.MaxWidth=820; bottom.HorizontalAlignment=HorizontalAlignment.Stretch;
        Grid.SetRow(bottom,2); page.Children.Add(bottom);
        _error.Margin=new(3,0,0,8); bottom.Children.Add(_error);
        _prompt.Height=compact?70:78; _prompt.MaxLength=8000; _prompt.BorderThickness=new(0);
        _prompt.Padding=new(2,7,2,7); _prompt.VerticalContentAlignment=VerticalAlignment.Top;
        var composer=Ui.Column(Ui.WithPlaceholder(_prompt,document is not null?"Pregunta algo sobre este documento…":_conversation?.Scope=="memory"?"Pregunta sobre este recuerdo…":_conversation?.Scope=="screen"?"Pregunta sobre lo visible en la captura…":"Escribe una pregunta o una idea…"));
        var tools=new DockPanel { Margin=new(0,8,0,0) };
        _voice=Ui.IconButton("Dictar","\uE720",async()=>await DictateAsync()); _voice.Margin=new(0,0,8,0);
        _send=Ui.Button("Enviar",async()=>await SendAsync(),true); _send.Padding=new(16,6,16,6); _send.MinHeight=34; _send.FontSize=13;
        var actions=Ui.Row(_voice,_send); DockPanel.SetDock(actions,Dock.Right); tools.Children.Add(actions);
        _brief=Ui.Chip("Breve",!host.Settings.Specific,()=>_ = Ui.Safe(()=>SetSpecificAsync(false)));
        _specific=Ui.Chip("Detallada",host.Settings.Specific,()=>_ = Ui.Safe(()=>SetSpecificAsync(true)));
        _brief.Margin=new(0,0,5,0);
        var modes=Ui.Row(_brief,_specific); modes.HorizontalAlignment=HorizontalAlignment.Left; tools.Children.Add(modes);
        composer.Children.Add(tools); var surface=Ui.Card(composer,14); surface.CornerRadius=new(14); surface.BorderBrush=Ui.Brush("#CDD6C8"); bottom.Children.Add(surface);
        _footnote=Ui.Text("",11,Ui.Muted);
        _footnote.Margin=new(3,9,0,0); bottom.Children.Add(_footnote);
        Content=page;
        _prompt.PreviewKeyDown+=async(_,e)=> { if (e.Key==Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { e.Handled=true; if (!host.IsBusy) await SendAsync(); } };
        _history.CollectionChanged+=MessagesChanged; host.Changed+=HostChanged;
        Unloaded+=(_,_)=> { _history.CollectionChanged-=MessagesChanged; host.Changed-=HostChanged; _listening?.Cancel(); };
        Draw();
    }
    public void FocusInput()=>_prompt.Focus();
    private async Task SetSpecificAsync(bool value)
    {
        var settings=_host.Settings.Copy(); settings.Specific=value; await _host.SaveSettingsAsync(settings);
    }
    private void MessagesChanged(object? sender,NotifyCollectionChangedEventArgs e)=>Draw(true);
    private void HostChanged(object? sender,EventArgs e)=>Draw();
    private void Draw(bool follow=false)
    {
        ((TextBlock)_sourceBadge.Child).Text=_document is not null?"Respuestas con páginas":_conversation?.Scope=="memory"?"Solo este recuerdo":_conversation?.Scope=="screen"?"Solo esta captura":_host.Settings.IsNvidia?"NVIDIA API":"En tu equipo";
        _footnote.Text=$"{_host.Settings.ActiveModel} · {_host.Settings.ProviderName}\nEnter para enviar · Shift + Enter para nueva línea";
        var atEnd=_scroll.ScrollableHeight-_scroll.VerticalOffset<60;
        var ownBusy=_host.IsChatBusy(_conversation,_document);
        _send.IsEnabled=_listening is null && (!_host.IsBusy || ownBusy); _send.Content=ownBusy?"Detener":"Enviar";
        AutomationProperties.SetName(_send,ownBusy?"Detener":"Enviar");
        _subtitle.Text=_document is not null?_document.SourceName:_conversation?.Scope=="memory"?$"Solo este recuerdo · {_host.ConversationMemory(_conversation)?.Title??"Recuerdo eliminado"}":_conversation?.Scope=="screen"?"Respuestas limitadas a la última captura analizada.":$"Tu espacio para pensar con {_host.Settings.PetName}.";
        foreach (var pair in new[] { (_brief,!_host.Settings.Specific),(_specific,_host.Settings.Specific) }) {
            pair.Item1.Background=pair.Item2?Ui.Ink:Brushes.White; pair.Item1.Foreground=pair.Item2?Brushes.White:Ui.Muted;
            pair.Item1.BorderBrush=pair.Item2?Ui.Ink:Ui.Line;
            AutomationProperties.SetHelpText(pair.Item1,pair.Item2?"Filtro seleccionado":"Seleccionar filtro");
        }
        var deleted=_conversation?.IsDeleted==true || (_document is not null && !_host.Store.Data.Memories.Any(m=>m.Id==_document.Id)) || (_conversation?.Scope=="memory" && _host.ConversationMemory(_conversation) is null);
        if (deleted) { _listening?.Cancel(); _send.IsEnabled=false; _voice.IsEnabled=false; _prompt.IsReadOnly=true; _error.Text=_conversation?.IsDeleted==true?"Esta conversación se eliminó del Historial. Inicia una nueva para continuar.":"Este recuerdo se eliminó de Mis guardados. La conversación permanece visible hasta que cierres la ventana."; }
        _error.Visibility=string.IsNullOrEmpty(_error.Text)?Visibility.Collapsed:Visibility.Visible;
        var petChanged=_displayPet!=_host.Settings.Pet || _displayName!=_host.Settings.PetName;
        var changed=petChanged || !_displayed.SequenceEqual(_history) || _history.Any(m=>!_bubbles.TryGetValue(m,out var value) || value.Role!=m.Role || value.Text!=m.Content || !value.Images.SequenceEqual(m.Images));
        if(changed) {
        if(petChanged) { _bubbles.Clear(); _thinking.Content=null; }
        _messages.Children.Clear();
        if (_history.Count==0) _messages.Children.Add(Welcome());
        foreach (var message in _history)
        {
            if(_bubbles.TryGetValue(message,out var cached) && cached.Role==message.Role && cached.Text==message.Content && cached.Images.SequenceEqual(message.Images)) { _messages.Children.Add(cached.Bubble); continue; }
            var own=message.Role=="user";
            var name=Ui.Text(own?"Tú":_host.Settings.PetName,12,own?Ui.Accent:Ui.Ink,FontWeights.SemiBold);
            var header=new DockPanel();
            if (!own) {
                var copy=Ui.IconButton("Copiar respuesta","\uE8C8",()=>Clipboard.SetText(message.Content));
                copy.Width=28; copy.Height=28; copy.MinHeight=28; copy.BorderThickness=new(0); copy.Background=Brushes.Transparent;
                copy.HorizontalAlignment=HorizontalAlignment.Right; DockPanel.SetDock(copy,Dock.Right); header.Children.Add(copy);
            }
            header.Children.Add(name);
            FrameworkElement text=own?Ui.Text(message.Content,14):Controls.MarkdownMessage.Create(message.Content,_host.OpenSource);
            text.Margin=new(0,5,0,0);
            var body=Ui.Column(header); if(message.Images.Count>0) body.Children.Add(ImageAttachments(message)); body.Children.Add(text);
            var bubble=new Border { Child=body,Background=own?Ui.Soft:Brushes.White,
                BorderBrush=Ui.Line,BorderThickness=own?new(0):new(1),CornerRadius=new(own?12:4),
                Padding=new(18,12,18,16),HorizontalAlignment=own?HorizontalAlignment.Right:HorizontalAlignment.Stretch,
                MaxWidth=own?650:800,Margin=new(0,0,0,16) };
            _bubbles[message]=(message.Role,message.Content,message.Images.ToArray(),bubble); _messages.Children.Add(bubble);
        }
        _messages.Children.Add(_thinking); _displayed=_history.ToArray(); _displayPet=_host.Settings.Pet; _displayName=_host.Settings.PetName;
        var retained=_displayed.ToHashSet(); foreach(var message in _bubbles.Keys.Where(m=>!retained.Contains(m)).ToArray()) _bubbles.Remove(message);
        }
        if (ownBusy && _thinking.Content is null) {
            var thinking=Ui.Row(new Controls.SpritePlayer { PetId=_host.Settings.Pet,State=PetState.Think,Width=34,Height=34 },
                Ui.Text($"{_host.Settings.PetName} está pensando…",12,Ui.Muted));
            thinking.Margin=new(0,0,0,12); _thinking.Content=thinking;
        }
        _thinking.Visibility=ownBusy?Visibility.Visible:Visibility.Collapsed;
        if (follow || atEnd) Dispatcher.BeginInvoke(()=>_scroll.ScrollToEnd(),DispatcherPriority.Background);
    }
    private FrameworkElement ImageAttachments(ChatMessage message)
    {
        var panel=new WrapPanel { Margin=new(0,9,0,4) };
        foreach(var attachment in message.Images) {
            var path=_host.Store.ChatImagePath(attachment.Id); var thumbnail=ThumbnailCache.Load(path);
            var button=Ui.AsyncButton("Ver imagen: "+attachment.Name,async()=> {
                try {
                    var image=Images.Decode(await File.ReadAllBytesAsync(path));
                    var window=new Window { Title="Mindie · "+attachment.Name,Width=760,Height=600,MinWidth=320,MinHeight=250,Owner=Window.GetWindow(this),WindowStartupLocation=WindowStartupLocation.CenterOwner,
                        Content=new Image { Source=image,Stretch=Stretch.Uniform,Margin=new(16) } };
                    Ui.WindowStyle(window); window.Loaded+=(_,_)=>NativeMethods.FitToWorkArea(window); window.Show();
                }
                catch(Exception ex) { _error.Text="No pude abrir la imagen: "+ex.Message; _error.Visibility=Visibility.Visible; }
            });
            button.Content=Ui.Column(new Image { Source=thumbnail,Width=106,Height=76,Stretch=Stretch.Uniform },
                new TextBlock { Text=thumbnail is null?"Imagen no disponible":attachment.Name,Foreground=Ui.Muted,FontSize=11,MaxWidth=106,TextTrimming=TextTrimming.CharacterEllipsis,TextWrapping=TextWrapping.NoWrap,Margin=new(0,4,0,0) });
            button.ToolTip=attachment.Name; button.Padding=new(7); button.Margin=new(0,0,7,7); button.IsEnabled=thumbnail is not null; panel.Children.Add(button);
        }
        return panel;
    }
    private FrameworkElement Welcome()
    {
        var pet=new Controls.SpritePlayer { PetId=_host.Settings.Pet,Width=_compact?64:88,Height=_compact?64:88,HorizontalAlignment=HorizontalAlignment.Left };
        var memory=_conversation?.Scope=="memory";
        var welcome=Ui.Column(pet,Ui.Space(10),
            Ui.Text(memory?"Explora lo que guardaste.":_document is null?"Un lugar para tus ideas.":"Vamos al detalle.",_compact?23:30,weight:FontWeights.SemiBold),
            Ui.Space(7),Ui.Text(memory?"Pregunta por sus ideas o aclara un detalle. Las respuestas se basan únicamente en este recuerdo; si no contiene la información, te lo diré.":_document is null?"Cuéntame qué estás pensando. Podemos aclarar una duda, ordenar una idea o encontrar el siguiente paso.":"Explora el documento con preguntas. Puedes pedir explicaciones, comparar conceptos o localizar datos con sus páginas.",14,Ui.Muted),Ui.Space(22));
        welcome.MaxWidth=600; welcome.HorizontalAlignment=HorizontalAlignment.Left; welcome.Margin=new(12,_compact?8:25,12,20);
        var starters=memory?new[] { ("Resumir el recuerdo","Resume el contenido de este recuerdo."),("Ver ideas clave","¿Cuáles son las ideas clave de este recuerdo?"),("Aclarar un detalle","Explícame este punto del recuerdo: ") }:_document is null?new[] { ("Ordenar una idea","Ayúdame a ordenar esta idea: "),("Pensar el siguiente paso","Quiero avanzar con esto. Ayúdame a elegir el siguiente paso: "),("Explicar algo","Explícame de forma sencilla: ") }:
            new[] { ("Resumir el documento","Resume las ideas principales de este documento e indica sus páginas."),("Encontrar lo esencial","¿Cuáles son los conceptos más importantes del documento? Cita sus páginas."),("Entender una sección","Explícame esta sección del documento: ") };
        var options=new WrapPanel();
        foreach (var (label,prompt) in starters) {
            var button=Ui.Button(label,()=> { _prompt.Text=prompt; _prompt.CaretIndex=_prompt.Text.Length; _prompt.Focus(); });
            button.FontSize=12; button.Margin=new(0,0,8,8); button.Padding=new(12,9,12,9); options.Children.Add(button);
        }
        welcome.Children.Add(options); return welcome;
    }
    private async Task SendAsync()
    {
        if (_host.IsBusy) { if(_host.IsChatBusy(_conversation,_document)) _host.Cancel(); return; }
        var text=_prompt.Text.Trim(); if (text.Length==0 || _listening is not null) return;
        _prompt.Clear(); _error.Text=""; _error.Visibility=Visibility.Collapsed; ChatMessage? pending=null;
        try {
            if (_document is null) await _host.SendAsync(text,conversation:_conversation);
            else { pending=new() { Content=text }; _history.Add(pending); _history.Add(await _host.AskPdfAsync(_document,_knowledge!,_history)); while (_history.Count>100) _history.RemoveAt(0); }
        }
        catch (OperationCanceledException) { if (pending is not null) _history.Remove(pending); _prompt.Text=text; _error.Text="Respuesta cancelada."; }
        catch (Exception ex) { if (pending is not null) _history.Remove(pending); _prompt.Text=text; _error.Text=ex.Message; }
        _error.Visibility=string.IsNullOrEmpty(_error.Text)?Visibility.Collapsed:Visibility.Visible; _prompt.Focus();
    }
    public async Task DictateAsync()
    {
        if (_listening is not null) { _listening.Cancel(); return; }
        if (_host.IsBusy) return; _listening=new(); _voice.ToolTip="Detener escucha"; _error.Text="Te escucho…"; _error.Visibility=Visibility.Visible; _prompt.IsReadOnly=true; _send.IsEnabled=false;
        try { var text=await _host.Speech.ListenAsync(_listening.Token); _prompt.Text=(_prompt.Text+" "+text).Trim(); _error.Text="Revisa el texto antes de enviarlo."; }
        catch (OperationCanceledException) { _error.Text="Escucha cancelada."; }
        catch (Exception ex) { _error.Text=ex.Message; }
        finally { _listening.Dispose(); _listening=null; _voice.ToolTip="Dictar"; var deleted=_conversation?.IsDeleted==true || (_document is not null && !_host.Store.Data.Memories.Any(m=>m.Id==_document.Id)) || (_conversation?.Scope=="memory" && _host.ConversationMemory(_conversation) is null); _prompt.IsReadOnly=deleted; _send.IsEnabled=!deleted; _prompt.Focus(); }
    }
}
