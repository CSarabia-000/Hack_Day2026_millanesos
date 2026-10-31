namespace Mindie.Views;

public sealed class HistoryView:UserControl
{
    private readonly AppHost _host;
    private readonly StackPanel _list=new();
    private readonly TextBox _search=Ui.Input("","Buscar conversaciones");
    private readonly Func<ChatConversation,bool> _confirmDelete;
    private readonly Dictionary<string,Button> _deleteButtons=[];
    private readonly TextBlock _error=Ui.Text("",12,Ui.Brush("#A02D3C"));
    private bool _deleting;
    private string _signature="";
    private int _limit=100;
    public HistoryView(AppHost host):this(host,conversation=>MessageBox.Show(host.MainWindow,$"¿Eliminar “{conversation.Title}” y sus adjuntos del Historial? Los recuerdos de Mis guardados se conservarán.","Eliminar conversación",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes) { }
    internal HistoryView(AppHost host,Func<ChatConversation,bool> confirmDelete)
    {
        _host=host; _confirmDelete=confirmDelete; _search.Padding=new(12,8,12,8);
        var heading=Ui.Heading("Historial","Vuelve a una conversación y continúa donde la dejaste.");
        var create=Ui.Button("Nueva conversación",()=> { host.NewConversation(); host.Open("chat"); },true); create.HorizontalAlignment=HorizontalAlignment.Left; create.Margin=new(0,0,0,20);
        _error.Visibility=Visibility.Collapsed;
        var body=Ui.Column(heading,create,Ui.WithPlaceholder(_search,"Busca por título o por lo que conversaste…"),Ui.Space(12),_error,Ui.Space(8),_list);
        Content=Ui.Scroll(body); _search.TextChanged+=(_,_)=> { _limit=100; Draw(true); };
        host.Changed+=Changed; Unloaded+=(_,_)=>host.Changed-=Changed; Draw(true);
    }
    private void Changed(object? sender,EventArgs e)=>Draw();
    private void Draw(bool force=false)
    {
        foreach(var button in _deleteButtons.Values) button.IsEnabled=!_host.IsBusy && !_deleting;
        var entries=_host.Store.Data.Conversations.Where(c=>c.Messages.Count>0).OrderByDescending(c=>c.UpdatedAt).ToArray();
        var signature=string.Join("|",entries.Select(c=>$"{c.Id}:{c.UpdatedAt.UtcTicks}:{c.Title}")); if(!force && signature==_signature) return; _signature=signature;
        var query=_search.Text.Trim(); var matches=entries.Where(c=>query.Length==0 || c.Title.Contains(query,StringComparison.CurrentCultureIgnoreCase) || c.Messages.Any(m=>m.Content.Contains(query,StringComparison.CurrentCultureIgnoreCase))).ToArray();
        _list.Children.Clear(); _deleteButtons.Clear(); _list.Children.Add(Ui.Text($"{matches.Length} {(matches.Length==1?"conversación":"conversaciones")} · Más recientes primero",12,Ui.Muted)); _list.Children.Add(Ui.Space(15));
        foreach(var conversation in matches.Take(_limit)) {
            var open=Ui.Button("Continuar",()=>_host.OpenConversation(conversation)); open.VerticalAlignment=VerticalAlignment.Center; open.Margin=new(18,0,0,0);
            var delete=Ui.IconButton("Eliminar conversación: "+conversation.Title,"\uE74D",async()=> {
                if(_deleting || _host.IsBusy || !_confirmDelete(conversation)) return;
                _deleting=true; _error.Visibility=Visibility.Collapsed; Draw();
                try { await _host.DeleteConversationAsync(conversation); }
                catch(Exception ex) { _error.Text="No se pudo eliminar la conversación: "+ex.Message; _error.Visibility=Visibility.Visible; }
                finally { _deleting=false; Draw(); }
            });
            delete.Foreground=Ui.Brush("#A02D3C"); delete.Margin=new(8,0,0,0); delete.VerticalAlignment=VerticalAlignment.Center; delete.IsEnabled=!_host.IsBusy && !_deleting;
            System.Windows.Automation.AutomationProperties.SetAutomationId(delete,"DeleteConversation-"+conversation.Id); _deleteButtons[conversation.Id]=delete;
            var actions=Ui.Row(open,delete); actions.VerticalAlignment=VerticalAlignment.Center;
            var row=new DockPanel(); DockPanel.SetDock(actions,Dock.Right); row.Children.Add(actions);
            var preview=conversation.Messages.LastOrDefault(m=>m.Role=="user")?.Content??""; if(preview.Length>140) preview=preview[..140]+"…";
            var kind=conversation.Scope switch { "screen"=>"CAPTURA DE PANTALLA","memory"=>"RECUERDO",_=>"CONVERSACIÓN" };
            row.Children.Add(Ui.Column(Ui.Text(kind+" · "+conversation.UpdatedAt.ToLocalTime().ToString("d MMM yyyy · HH:mm",CultureInfo.GetCultureInfo("es-MX")),11,Ui.Accent),Ui.Space(5),Ui.Text(conversation.Title,17,weight:FontWeights.SemiBold),Ui.Space(5),Ui.Text(preview,13,Ui.Muted)));
            var card=Ui.Card(row,18); card.Margin=new(0,0,0,12); _list.Children.Add(card);
        }
        if(matches.Length==0) _list.Children.Add(Ui.Text(query.Length==0?"Aún no hay conversaciones guardadas. Escribe a tu compañero para empezar.":"No encontré conversaciones con esa búsqueda.",14,Ui.Muted));
        if(matches.Length>_limit) _list.Children.Add(Ui.Button("Ver más",()=> { _limit+=100; Draw(true); }));
    }
}
