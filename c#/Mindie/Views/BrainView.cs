using Mindie.Controls;
using Mindie.Services;
using Mindie.Windows;
using System.Windows.Automation;

namespace Mindie.Views;

public sealed class BrainView:UserControl
{
    private readonly AppHost _host;
    internal KnowledgeGraphCanvas Scene { get; }=new();
    private readonly TextBox _search=Ui.Input("","Buscar en el cerebro digital");
    private readonly StackPanel _details=new();
    private readonly WrapPanel _legend=new();
    private readonly TextBlock _count=Ui.Text("",12,Ui.Muted);
    private readonly TextBlock _empty=Ui.Text("Aquí empieza tu conocimiento.\nGuarda una captura, un artículo o un PDF.",15,Ui.Muted);
    private CancellationTokenSource? _build;
    private MemorySnapshot? _inputs;
    private Dictionary<string,KnowledgeNodePosition> _positions=[];
    private bool _attached,_compact,_drawerOpen;
    private Action? _adaptLayout;
    public BrainView(AppHost host)
    {
        _host=host;
        var page=new Grid();
        page.RowDefinitions.Add(new() { Height=GridLength.Auto });
        page.RowDefinitions.Add(new() { Height=GridLength.Auto });
        page.RowDefinitions.Add(new());
        page.RowDefinitions.Add(new() { Height=GridLength.Auto });
        var title=Ui.Row(Ui.Text("Cerebro digital",30,weight:FontWeights.SemiBold),new Border { Background=Ui.Soft,CornerRadius=new(5),Padding=new(8,2,8,2),Margin=new(12,0,0,0),VerticalAlignment=VerticalAlignment.Center,Child=Ui.Text("3D",11,Ui.Accent,FontWeights.SemiBold) });
        var heading=Ui.Column(title,Ui.Space(5),Ui.Text("Un espacio para descubrir cómo se conectan tus ideas.",14,Ui.Muted)); heading.Margin=new(0,0,0,20); page.Children.Add(heading);
        var tools=new Grid { Margin=new(0,0,0,14) };
        tools.RowDefinitions.Add(new() { Height=GridLength.Auto }); tools.RowDefinitions.Add(new() { Height=GridLength.Auto });
        _search.Height=40; _search.Padding=new(13,8,13,8);
        tools.Children.Add(Ui.WithPlaceholder(_search,"Busca un recuerdo, una idea o un tema…"));
        Button? rotate=null,pan=null;
        void Mode(bool move) {
            Scene.PanMode=move;
            foreach(var (button,active) in new[] { (rotate,!move),(pan,move) }) if(button is not null) {
                button.Background=active?Ui.Ink:Brushes.Transparent; button.Foreground=active?Brushes.White:Ui.Muted;
                button.BorderBrush=active?Ui.Ink:Brushes.Transparent;
                AutomationProperties.SetItemStatus(button,active?"Seleccionado":"");
            }
        }
        rotate=SmallButton("Girar",()=>Mode(false),"\uE7AD"); pan=SmallButton("Mover",()=>Mode(true),"\uE7C2"); Mode(false);
        var zoomOut=Ui.IconButton("Alejar el grafo","\uE738",()=>Scene.ChangeZoom(1/1.2));
        var zoomIn=Ui.IconButton("Acercar el grafo","\uE710",()=>Scene.ChangeZoom(1.2));
        var center=SmallButton("Centrar",Scene.Fit);
        var reset=Ui.AsyncButton("Reorganizar",async()=> { await host.Store.ResetGraphPositionsAsync(); await RebuildAsync(); Scene.Fit(); });
        reset.ToolTip="Devuelve los recuerdos a sus zonas y conserva el contenido."; Compact(reset);
        var detailsToggle=SmallButton("Detalles",()=> { _drawerOpen=!_drawerOpen; _adaptLayout?.Invoke(); });
        var actions=new WrapPanel { Margin=new(0,10,0,0) };
        foreach(var button in new[] { rotate,pan,zoomOut,zoomIn,center,reset,detailsToggle }) { button.Margin=new(0,0,6,4); actions.Children.Add(button); }
        Grid.SetRow(actions,1); tools.Children.Add(actions); Grid.SetRow(tools,1); page.Children.Add(tools);

        var body=new Grid();
        body.ColumnDefinitions.Add(new()); body.ColumnDefinitions.Add(new() { Width=new(22) }); body.ColumnDefinitions.Add(new() { Width=new(262) });
        var stage=new Grid { Background=Brushes.Black,ClipToBounds=true };
        stage.Children.Add(Scene);
        _empty.TextAlignment=TextAlignment.Center; _empty.HorizontalAlignment=HorizontalAlignment.Center; _empty.VerticalAlignment=VerticalAlignment.Center; _empty.MaxWidth=310; _empty.Margin=new(24); _empty.IsHitTestVisible=false; stage.Children.Add(_empty);
        _empty.Foreground=Ui.Brush("#D8D1E4"); _empty.Background=Ui.Brush("#D9000000"); _empty.Padding=new(12);
        var cameraTools=Ui.Row();
        foreach(var (name,glyph,x,y) in new[] { ("Girar vista a la izquierda","\uE76B",-18.0,0.0),("Girar vista hacia arriba","\uE70E",0.0,15.0),("Girar vista hacia abajo","\uE70D",0.0,-15.0),("Girar vista a la derecha","\uE76C",18.0,0.0) }) {
            var button=Ui.IconButton(name,glyph,()=>Scene.Orbit(x,y)); button.Margin=new(0,0,3,0); StageButton(button); cameraTools.Children.Add(button);
        }
        Button? animate=null;
        animate=Ui.IconButton("Pausar esfera","\uE769",()=> {
            Scene.AnimateShell=!Scene.AnimateShell;
            var name=Scene.AnimateShell?"Pausar esfera":"Animar esfera";
            AutomationProperties.SetName(animate!,name); animate!.ToolTip=name; animate.Content=Ui.Glyph(Scene.AnimateShell?"\uE769":"\uE768",15);
        }); StageButton(animate); animate.Margin=new(8,0,0,0); cameraTools.Children.Add(animate);
        var orbitControls=new Border { Child=cameraTools,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom,Margin=new(14,0,0,14) };
        stage.Children.Add(orbitControls);
        var surface=new Border { Background=Brushes.Black,CornerRadius=new(14),Child=stage,ClipToBounds=true };
        surface.SizeChanged+=(_,_)=>surface.Clip=new RectangleGeometry(new Rect(0,0,surface.ActualWidth,surface.ActualHeight),14,14);
        body.Children.Add(surface);
        var inspector=new Border { Background=Brushes.White,BorderThickness=new(1),BorderBrush=Ui.Line,CornerRadius=new(14),Padding=new(18),Child=Ui.Scroll(_details) };
        Grid.SetColumn(inspector,2); body.Children.Add(inspector);
        void Adapt() {
            _compact=ActualWidth>0 && ActualWidth<860;
            Grid.SetColumnSpan(surface,_compact?3:1); Grid.SetColumn(inspector,_compact?0:2); Grid.SetColumnSpan(inspector,_compact?3:1);
            inspector.Width=_compact?280:double.NaN; inspector.HorizontalAlignment=_compact?HorizontalAlignment.Right:HorizontalAlignment.Stretch;
            inspector.Margin=_compact?new(0,0,0,45):new(0); inspector.Visibility=_compact && !_drawerOpen?Visibility.Collapsed:Visibility.Visible;
            detailsToggle.Visibility=_compact?Visibility.Visible:Visibility.Collapsed; detailsToggle.Content=_drawerOpen?"Cerrar detalles":"Detalles";
            AutomationProperties.SetName(detailsToggle,_drawerOpen?"Cerrar detalles":"Detalles");
        }
        _adaptLayout=Adapt; SizeChanged+=(_,_)=>Adapt(); Adapt(); Grid.SetRow(body,2); page.Children.Add(body);
        var footer=Ui.Column(_legend,Ui.Space(4),_count,Ui.Space(5),Ui.Text("Arrastra para girar · Shift + arrastrar para desplazar · Rueda para acercar",11,Ui.Muted));
        footer.Margin=new(0,10,0,0); Grid.SetRow(footer,3); page.Children.Add(footer); Content=page;
        _search.TextChanged+=(_,_)=> { if(_compact && _search.Text.Length>0) _drawerOpen=true; Scene.SetQuery(_search.Text); Scene.Select(null); DrawDetails(); };
        Scene.SelectionChanged+=(_,_)=> { if(_compact && Scene.Selected is not null) _drawerOpen=true; DrawDetails(); _adaptLayout?.Invoke(); };
        Scene.PositionCommitted+=async(_,node)=>await Ui.Safe(()=>host.Store.SaveGraphPositionAsync(node.Id,node.SpatialPosition,node.Category));
        Scene.OpenRequested+=(_,node)=>_ = Ui.Safe(()=>OpenAsync(node));
        Loaded+=async(_,_)=> { if(!_attached) { host.Changed+=HostChanged; _attached=true; } await Ui.Safe(RebuildAsync); };
        Unloaded+=(_,_)=> { if(_attached) { host.Changed-=HostChanged; _attached=false; } _build?.Cancel(); };
        DrawDetails();
    }
    private static Button SmallButton(string text,Action action,string? glyph=null) { var button=Ui.Button(text,action,glyph:glyph); Compact(button); return button; }
    private static void Compact(Button button) { button.FontSize=12; button.MinHeight=34; button.Padding=new(11,6,11,6); }
    private static void StageButton(Button button) { button.Background=Ui.Brush("#17141F"); button.Foreground=Ui.Brush("#D9D2EA"); button.BorderBrush=Ui.Brush("#35303E"); }
    private void HostChanged(object? sender,EventArgs e)
    {
        Scene.ReduceMotion=_host.Settings.ReduceMotion;
        if(_inputs?.Matches(_host.Store.Data.Memories,true)==true && _positions.Count==_host.Store.Data.GraphPositions.Count && _positions.All(p=>_host.Store.Data.GraphPositions.TryGetValue(p.Key,out var value) && value==p.Value)) return;
        _ = Ui.Safe(RebuildAsync);
    }
    internal async Task RebuildAsync()
    {
        _build?.Cancel(); var request=new CancellationTokenSource(); _build=request;
        Scene.ReduceMotion=_host.Settings.ReduceMotion;
        var memories=_host.Store.Data.Memories.Select(m=>m.Copy()).ToArray(); var positions=new Dictionary<string,KnowledgeNodePosition>(_host.Store.Data.GraphPositions);
        try {
            var graph=await Task.Run(()=>KnowledgeGraphBuilder.Build(memories,positions,request.Token),request.Token);
            if(_build!=request || !IsLoaded) return;
            Scene.SetGraph(graph); Scene.SetQuery(_search.Text);
            _inputs=new(memories); _positions=positions;
            _empty.Visibility=memories.Length==0?Visibility.Visible:Visibility.Collapsed;
            _count.Text=$"{memories.Length} {(memories.Length==1?"recuerdo":"recuerdos")} · {graph.Nodes.Count(n=>!n.IsMemory && !n.IsRoot)} zonas · {graph.Links.Count(e=>!e.IsTopic)} conexiones por afinidad";
            _legend.Children.Clear();
            foreach(var category in graph.Nodes.Where(n=>!n.IsMemory && !n.IsRoot)) {
                var dot=new System.Windows.Shapes.Ellipse { Width=7,Height=7,Fill=KnowledgeGraphCanvas.ZoneBrush(category.Category),Margin=new(0,0,6,0),VerticalAlignment=VerticalAlignment.Center };
                var chip=SmallButton(category.Label,()=>Scene.Select(category.Id)); chip.Content=Ui.Row(dot,Ui.Text(category.Label,11,Ui.Muted)); chip.MinHeight=26; chip.Padding=new(6,3,6,3); chip.BorderThickness=new(0); chip.Background=Brushes.Transparent; chip.Margin=new(0,0,5,0); _legend.Children.Add(chip);
            }
            DrawDetails();
        }
        catch(OperationCanceledException) { }
        finally { if(_build==request) _build=null; request.Dispose(); }
    }
    private void DrawDetails()
    {
        _details.Children.Clear(); var selected=Scene.Selected;
        if(_search.Text.Trim().Length>0 && selected is null) {
            var matches=Scene.Graph.Nodes.Where(n=>n.IsMemory && Scene.Matches(n)).ToArray();
            _details.Children.Add(Ui.Text("Resultados",18,weight:FontWeights.SemiBold)); _details.Children.Add(Ui.Text($"{matches.Length} recuerdos encontrados",12,Ui.Muted)); _details.Children.Add(Ui.Space(15));
            foreach(var node in matches.Take(30)) _details.Children.Add(NodeButton(node));
            if(matches.Length==0) _details.Children.Add(Ui.Text("Prueba otra palabra del título, resumen o categoría.",13,Ui.Muted));
            return;
        }
        if(selected?.IsMemory==true) {
            var memory=selected.Memory!;
            _details.Children.Add(Ui.Badge(memory.Category)); _details.Children.Add(Ui.Space(12)); _details.Children.Add(Ui.Text(memory.Title,18,weight:FontWeights.SemiBold)); _details.Children.Add(Ui.Space(10));
            _details.Children.Add(Ui.Text(memory.Summary,13,Ui.Muted)); _details.Children.Add(Ui.Space(16));
            var open=Ui.AsyncButton(memory.IsPdf?"Preguntar al PDF":"Abrir recuerdo",()=>OpenAsync(selected),true); open.HorizontalAlignment=HorizontalAlignment.Stretch; _details.Children.Add(open);
            Divider(); _details.Children.Add(Ui.Text("Posición en el espacio",12,weight:FontWeights.SemiBold));
            var directions=Ui.Row();
            foreach(var (label,glyph,delta) in new[] { ("Mover nodo a la izquierda","\uE72B",new Vector(-25,0)),("Mover nodo arriba","\uE74A",new Vector(0,-25)),("Mover nodo abajo","\uE74B",new Vector(0,25)),("Mover nodo a la derecha","\uE72A",new Vector(25,0)) }) {
                var move=Ui.IconButton(label,glyph,()=>Scene.NudgeSelected(delta)); move.Margin=new(0,8,5,0); directions.Children.Add(move);
            }
            _details.Children.Add(directions);
            var closer=SmallButton("Acercar nodo",()=>Scene.NudgeDepth(.18)); var farther=SmallButton("Alejar nodo",()=>Scene.NudgeDepth(-.18));
            closer.Margin=new(0,8,5,0); farther.Margin=new(0,8,0,0);
            var depth=new WrapPanel(); depth.Children.Add(closer); depth.Children.Add(farther); _details.Children.Add(depth);
            var hint=Ui.Text("También puedes usar Ctrl + flechas y RePág / AvPág.",11,Ui.Muted); hint.Margin=new(0,8,0,0); _details.Children.Add(hint);
            Divider();
            var related=Scene.Graph.Connections(selected).Where(e=>!e.IsTopic).OrderByDescending(e=>e.Weight).ToArray();
            _details.Children.Add(Ui.Text($"Conectado con · {related.Length}",13,weight:FontWeights.SemiBold)); _details.Children.Add(Ui.Space(10));
            foreach(var edge in related) { _details.Children.Add(NodeButton(edge.From==selected?edge.To:edge.From)); var reason=Ui.Text(edge.Reason,11,Ui.Muted); reason.Margin=new(0,0,0,12); _details.Children.Add(reason); }
            if(related.Length==0) _details.Children.Add(Ui.Text("Más recuerdos sobre este tema crearán nuevas conexiones.",12,Ui.Muted));
            return;
        }
        if(selected is not null && !selected.IsRoot) {
            var nodes=Scene.Graph.Nodes.Where(n=>n.IsMemory && n.Category==selected.Category).OrderByDescending(n=>n.Memory!.CreatedAt).ToArray();
            _details.Children.Add(Ui.Text(selected.Label,21,KnowledgeGraphCanvas.ZoneBrush(selected.Category),FontWeights.SemiBold)); _details.Children.Add(Ui.Text($"{nodes.Length} recuerdos en esta zona",12,Ui.Muted)); Divider();
            foreach(var node in nodes.Take(30)) _details.Children.Add(NodeButton(node));
            if(nodes.Length>30) _details.Children.Add(Ui.Text("Usa la búsqueda para encontrar más recuerdos.",12,Ui.Muted));
            return;
        }
        _details.Children.Add(Ui.Text("Ideas que se conectan",18,weight:FontWeights.SemiBold)); _details.Children.Add(Ui.Space(10));
        _details.Children.Add(Ui.Text("Tus recuerdos son los puntos destacados de la esfera. Selecciona uno para explorar su contenido y sus conexiones.",13,Ui.Muted)); _details.Children.Add(Ui.Space(16));
        var capture=Ui.AsyncButton("Guardar en mente",_host.CaptureAsync,true); _details.Children.Add(capture);
        var recent=Scene.Graph.Nodes.Where(n=>n.IsMemory).OrderByDescending(n=>n.Memory!.CreatedAt).Take(3).ToArray();
        if(recent.Length>0) { Divider(); _details.Children.Add(Ui.Text("Últimos recuerdos",12,weight:FontWeights.SemiBold)); _details.Children.Add(Ui.Space(10)); foreach(var node in recent) _details.Children.Add(NodeButton(node)); }
    }
    private void Divider()=>_details.Children.Add(new Border { Height=1,Background=Ui.Line,Margin=new(0,17,0,17) });
    private Button NodeButton(KnowledgeNode node)
    {
        var button=Ui.Button(node.Label,()=>Scene.Select(node.Id)); button.HorizontalContentAlignment=HorizontalAlignment.Left; button.Padding=new(10); button.Margin=new(0,0,0,7); button.BorderThickness=new(0); button.Background=Ui.Ground;
        button.Content=Ui.Text(node.Label,12); AutomationProperties.SetName(button,"Seleccionar recuerdo: "+node.Label); return button;
    }
    private async Task OpenAsync(KnowledgeNode node)
    {
        var item=_host.Store.Data.Memories.FirstOrDefault(m=>m.Id==node.Id); if(item is null) return;
        if(item.IsPdf) await _host.OpenPdfAsync(item);
        else new MemoryEditorWindow(_host,await File.ReadAllBytesAsync(_host.Store.CapturePath(item.Id)),item) { Owner=_host.MainWindow }.Show();
    }
}
