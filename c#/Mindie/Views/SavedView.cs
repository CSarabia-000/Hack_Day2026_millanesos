using Mindie.Services;
using Mindie.Windows;
using System.Windows.Automation;

namespace Mindie.Views;

public sealed class SavedView : UserControl
{
    private readonly AppHost _host;
    private readonly TextBox _search=Ui.Input("","Buscar recuerdos");
    private readonly WrapPanel _labels=new();
    private readonly ListBox _results=new() { BorderThickness=new(0),Background=Brushes.Transparent,Padding=new(0),HorizontalContentAlignment=HorizontalAlignment.Stretch };
    private readonly ContentControl _empty=new();
    private MemorySnapshot? _snapshot;
    private Controls.SpritePlayer? _emptySprite;
    private readonly TextBlock _count=Ui.Text("",12,Ui.Muted);
    private readonly Button _clear, _favorite;
    private readonly Func<MemoryItem,bool> _confirmDelete;
    private string? _category;
    private bool _favorites;
    public SavedView(AppHost host):this(host,item=>MessageBox.Show(host.MainWindow,$"¿Eliminar “{item.Title}” y {(item.IsPdf?"su PDF y conversación":"su captura")}? No podrás recuperarlos.","Eliminar recuerdo",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes) { }
    internal SavedView(AppHost host,Func<MemoryItem,bool> confirmDelete)
    {
        _host=host; _confirmDelete=confirmDelete;
        var page=new DockPanel();
        var header=Ui.Column(Ui.Text("Mis guardados",28,weight:FontWeights.SemiBold),
            Ui.Space(4),Ui.Text("Un espacio para volver a lo que te interesa.",14,Ui.Muted));
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center };
        var capture=Ui.AsyncButton("Guardar en mente",host.CaptureAsync,true,"\uE710"); capture.Margin=new(0,0,8,6);
        var pdf=Ui.Button("Añadir PDF",host.ChoosePdf,glyph:"\uE8E5"); pdf.Margin=new(0,0,0,6);
        actions.Children.Add(capture); actions.Children.Add(pdf);
        var top=new Grid { Margin=new(0,0,0,22) }; top.ColumnDefinitions.Add(new()); top.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        top.RowDefinitions.Add(new() { Height=GridLength.Auto }); top.RowDefinitions.Add(new() { Height=GridLength.Auto });
        top.Children.Add(header); Grid.SetColumn(actions,1); top.Children.Add(actions);
        void AdaptHeader() {
            var stacked=ActualWidth>0 && ActualWidth<830;
            Grid.SetColumn(actions,stacked?0:1); Grid.SetRow(actions,stacked?1:0);
            actions.HorizontalAlignment=stacked?HorizontalAlignment.Left:HorizontalAlignment.Right; actions.Margin=new(stacked?0:20,stacked?16:0,0,0);
            Grid.SetColumnSpan(header,stacked?2:1);
        }
        SizeChanged+=(_,_)=>AdaptHeader(); AdaptHeader();
        DockPanel.SetDock(top,Dock.Top); page.Children.Add(top);
        var filters=Ui.Column();
        _search.Padding=new(35,0,38,0); _search.Height=46; _search.FontSize=14; _search.ToolTip="Buscar por título, etiqueta, resumen o ideas · Ctrl K";
        var search=Ui.WithPlaceholder(_search,"Busca un recuerdo, una idea o una etiqueta…");
        var icon=Ui.Glyph("\uE721",16); icon.Foreground=Ui.Muted; icon.Margin=new(12,0,0,0); icon.HorizontalAlignment=HorizontalAlignment.Left; icon.IsHitTestVisible=false; search.Children.Add(icon);
        var reset=Ui.IconButton("Borrar búsqueda","\uE711",()=>_search.Clear()); reset.Background=Brushes.Transparent; reset.BorderThickness=new(0); reset.HorizontalAlignment=HorizontalAlignment.Right; reset.Margin=new(0,0,5,0);
        search.Children.Add(reset); filters.Children.Add(search);
        var tagFilters=new DockPanel();
        _favorite=Ui.Chip("Favoritos",false,()=> { _favorites=!_favorites; Draw(); }); _favorite.Margin=new(12,0,0,7); _favorite.VerticalAlignment=VerticalAlignment.Top;
        DockPanel.SetDock(_favorite,Dock.Right); tagFilters.Children.Add(_favorite); tagFilters.Children.Add(_labels);
        var tags=Ui.Column(Ui.Text("Etiquetas",12,Ui.Muted,FontWeights.SemiBold),Ui.Space(8),tagFilters);
        tags.Margin=new(0,16,0,12); filters.Children.Add(tags);
        var info=new DockPanel { Margin=new(0,0,0,14) };
        _clear=Ui.Button("Limpiar filtros",()=> { _category=null; _favorites=false; _search.Clear(); Draw(); });
        _clear.FontSize=12; _clear.Padding=new(5,2,5,2); _clear.MinHeight=24; _clear.BorderThickness=new(0); _clear.Background=Brushes.Transparent;
        DockPanel.SetDock(_clear,Dock.Right); info.Children.Add(_clear); _count.VerticalAlignment=VerticalAlignment.Center; info.Children.Add(_count); filters.Children.Add(info);
        DockPanel.SetDock(filters,Dock.Top); page.Children.Add(filters);
        VirtualizingPanel.SetIsVirtualizing(_results,true); VirtualizingPanel.SetVirtualizationMode(_results,VirtualizationMode.Recycling); VirtualizingPanel.SetScrollUnit(_results,ScrollUnit.Pixel);
        _results.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        ScrollViewer.SetCanContentScroll(_results,true); ScrollViewer.SetHorizontalScrollBarVisibility(_results,ScrollBarVisibility.Disabled);
        var itemStyle=new Style(typeof(ListBoxItem)); itemStyle.Setters.Add(new Setter(HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch)); itemStyle.Setters.Add(new Setter(FocusableProperty,false));
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter)); var template=new ControlTemplate(typeof(ListBoxItem)) { VisualTree=presenter }; itemStyle.Setters.Add(new Setter(TemplateProperty,template)); _results.ItemContainerStyle=itemStyle;
        _results.ItemTemplate=new DataTemplate { VisualTree=new FrameworkElementFactory(typeof(MemoryCardPresenter)) };
        _results.Margin=new(0,0,8,0); var body=new Grid(); body.Children.Add(_results); body.Children.Add(_empty); page.Children.Add(body); Content=page;
        _search.TextChanged+=(_,_)=> { reset.Visibility=_search.Text.Length>0?Visibility.Visible:Visibility.Collapsed; Draw(); }; reset.Visibility=Visibility.Collapsed;
        host.Changed+=OnChanged; Unloaded+=(_,_)=>host.Changed-=OnChanged; Draw();
    }
    public void FocusSearch()=>_search.Focus();
    private void OnChanged(object? sender,EventArgs e)
    {
        if(_snapshot?.Matches(_host.Store.Data.Memories)!=true) Draw();
        else {
            foreach(var presenter in RealizedCards()) foreach(var button in presenter.DeleteButtons) button.IsEnabled=!_host.IsBusy;
            if(_emptySprite is not null) _emptySprite.PetId=_host.Settings.Pet;
        }
    }
    private IEnumerable<MemoryCardPresenter> RealizedCards()
    {
        for(var i=0;i<_results.Items.Count;i++) if(_results.ItemContainerGenerator.ContainerFromIndex(i) is DependencyObject container) {
            var content=FindPresenter(container); if(content is not null) yield return content;
        }
        static MemoryCardPresenter? FindPresenter(DependencyObject? element) {
            if(element is null) return null;
            if(element is MemoryCardPresenter card) return card;
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(element);i++) if(FindPresenter(VisualTreeHelper.GetChild(element,i)) is { } found) return found;
            return null;
        }
    }
    private void Draw()
    {
        var memories=_host.Store.Data.Memories;
        _snapshot=new(memories);
        // These are the labels already saved on the memories, not a fixed list of empty categories.
        var labels=memories.GroupBy(m=>m.Category,StringComparer.CurrentCultureIgnoreCase).OrderBy(g=>g.Key,StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (_category is not null && !labels.Any(g=>string.Equals(g.Key,_category,StringComparison.CurrentCultureIgnoreCase))) _category=null;
        _labels.Children.Clear();
        AddTag("Todas",memories.Count,null);
        foreach (var group in labels) AddTag(group.Key,group.Count(),group.Key);
        _favorite.Background=_favorites?Ui.Soft:Brushes.White; _favorite.Foreground=_favorites?Ui.Accent:Ui.Muted; _favorite.BorderBrush=_favorites?Ui.Accent:Ui.Line;
        AutomationProperties.SetHelpText(_favorite,_favorites?"Filtro seleccionado":"Seleccionar filtro");
        var query=_search.Text.Trim();
        var items=memories.Where(m=>(_category is null || string.Equals(m.Category,_category,StringComparison.CurrentCultureIgnoreCase))
            && (!_favorites || m.Favorite)
            && (query.Length==0 || new[] { m.Title,m.Summary,m.Category,m.SourceName,m.UserContext,m.SourceUrl }.Concat(m.Ideas).Any(text=>text.Contains(query,StringComparison.CurrentCultureIgnoreCase))))
            .OrderByDescending(m=>m.CreatedAt).ToList();
        _count.Text=$"{items.Count} {(items.Count==1?"recuerdo":"recuerdos")}"+(_category is null?"":$" en {_category}")+" · Más recientes primero";
        var filtered=query.Length>0 || _category is not null || _favorites;
        _clear.Visibility=filtered?Visibility.Visible:Visibility.Collapsed;
        Func<MemoryItem,FrameworkElement> render=MemoryCard;
        _results.ItemsSource=items.Select(item=>new CardData(item,render)).ToArray(); _empty.Visibility=items.Count==0?Visibility.Visible:Visibility.Collapsed; _results.Visibility=items.Count==0?Visibility.Collapsed:Visibility.Visible;
        if (items.Count==0) {
            _emptySprite=new Controls.SpritePlayer { PetId=_host.Settings.Pet,Width=90,Height=90,HorizontalAlignment=HorizontalAlignment.Left };
            var empty=Ui.Column(_emptySprite,
                Ui.Space(10),Ui.Text(filtered?"No hay recuerdos con estos filtros.":"Tu primer recuerdo tiene un lugar aquí.",22,weight:FontWeights.SemiBold),
                Ui.Space(8),Ui.Text(filtered?"Prueba otra etiqueta o borra la búsqueda para ver más.":"Guarda un área de pantalla, una página completa o un PDF. Aquí podrás encontrarlo después.",14,Ui.Muted));
            empty.MaxWidth=550; empty.Margin=new(20,20,20,20); _empty.Content=empty; return;
        }
        _empty.Content=null; _emptySprite=null;
    }
    private void AddTag(string label,int count,string? category)
    {
        var selected=string.Equals(_category,category,StringComparison.CurrentCultureIgnoreCase);
        var chip=Ui.Chip($"{label}   {count}",selected,()=> { _category=category; Draw(); });
        chip.Margin=new(0,0,7,7); AutomationProperties.SetName(chip,category is null?"Todas las etiquetas":"Filtrar etiqueta "+category);
        AutomationProperties.SetAutomationId(chip,"MemoryTag-"+(category??"all")); _labels.Children.Add(chip);
    }
    private FrameworkElement MemoryCard(MemoryItem item)
    {
        var row=new Grid(); row.ColumnDefinitions.Add(new() { Width=new(118) }); row.ColumnDefinitions.Add(new());
        var image=new Image { Source=ThumbnailCache.Load(_host.Store.CapturePath(item.Id)),Stretch=Stretch.Uniform,MaxHeight=158,Margin=new(7) };
        var preview=new Border { Child=image,Background=Ui.Ground,CornerRadius=new(8),Margin=new(0,0,18,0),VerticalAlignment=VerticalAlignment.Top,MinHeight=125 };
        row.Children.Add(preview);
        var column=Ui.Column(); Grid.SetColumn(column,1); row.Children.Add(column);
        var top=new DockPanel();
        var star=Ui.IconButton(item.Favorite?"Quitar de favoritos":"Guardar en favoritos",item.Favorite?"\uE735":"\uE734",async()=>await Ui.Safe(async()=> {
            var old=item.Favorite; item.Favorite=!old; try { await _host.Store.SaveAsync(); } catch { item.Favorite=old; throw; } Draw();
        })); star.Foreground=item.Favorite?Ui.Accent:Ui.Muted; star.Margin=new(0,0,6,0);
        var delete=Ui.IconButton("Eliminar recuerdo","\uE74D",async()=>await Ui.Safe(async()=> { if (_confirmDelete(item)) await _host.DeleteMemoryAsync(item); }));
        delete.Foreground=Ui.Brush("#A02D3C"); delete.IsEnabled=!_host.IsBusy;
        var bin=new System.Windows.Shapes.Path { Width=16,Height=16,Stretch=Stretch.Uniform,StrokeThickness=1.7,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Data=Geometry.Parse("M 4,7 H 20 M 9,7 V 4 H 15 V 7 M 6,7 L 7,21 H 17 L 18,7 M 10,10 V 18 M 14,10 V 18") };
        bin.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,new System.Windows.Data.Binding("Foreground") { Source=delete }); delete.Content=bin;
        AutomationProperties.SetName(delete,"Eliminar recuerdo: "+item.Title); AutomationProperties.SetAutomationId(delete,"DeleteMemory-"+item.Id);
        var utilities=Ui.Row(star,delete); DockPanel.SetDock(utilities,Dock.Right); top.Children.Add(utilities);
        var labels=new WrapPanel();
        var kind=Ui.Badge(item.IsPdf?"PDF":item.Kind=="web-text"?"Web · Solo texto":item.Kind=="web"?"Página web":"Captura",Ui.Ground); kind.Margin=new(0,3,7,3); labels.Children.Add(kind);
        var tag=Ui.Chip(item.Category,false,()=> { _category=item.Category; Draw(); }); tag.Background=Ui.Soft; tag.Foreground=Ui.Accent; tag.BorderThickness=new(0); tag.Margin=new(0,3,7,3); tag.Padding=new(8,3,8,3); tag.MinHeight=26;
        labels.Children.Add(tag); top.Children.Add(labels); column.Children.Add(top);
        var title=Ui.Text(item.Title,18,weight:FontWeights.SemiBold); title.Margin=new(0,9,0,6); column.Children.Add(title);
        var summary=Ui.Text(item.Summary,13,Ui.Muted); summary.MaxHeight=59; summary.TextTrimming=TextTrimming.CharacterEllipsis; column.Children.Add(summary);
        if (item.Ideas.Count>0) {
            var ideas=Ui.Text(string.Join("\n",item.Ideas.Take(2).Select(i=>"•  "+i)),12,Ui.Muted);
            ideas.MaxHeight=55; ideas.TextTrimming=TextTrimming.CharacterEllipsis; ideas.Margin=new(0,10,0,0); column.Children.Add(ideas);
        }
        var bottom=new DockPanel { Margin=new(0,16,0,0) };
        var open=Ui.AsyncButton(item.IsPdf?"Preguntar al PDF":"Abrir recuerdo",async()=> {
            if (item.IsPdf) await _host.OpenPdfAsync(item);
            else new MemoryEditorWindow(_host,File.ReadAllBytes(_host.Store.CapturePath(item.Id)),item) { Owner=_host.MainWindow }.Show();
        }); open.FontSize=12; open.MinHeight=32; open.Padding=new(10,5,10,5);
        var links=new WrapPanel(); open.Margin=new(0,0,7,5); links.Children.Add(open);
        if(!item.IsPdf) { var ask=Ui.AsyncButton("Preguntar al recuerdo",()=>_host.OpenMemoryChatAsync(item)); ask.FontSize=12; ask.MinHeight=32; ask.Padding=new(10,5,10,5); ask.Margin=new(0,0,7,5); links.Children.Add(ask); }
        if (item.SourceUrl.Length>0) { var source=Ui.Button("Abrir enlace",()=>_host.OpenSource(item.SourceUrl)); source.FontSize=12; source.MinHeight=32; source.Padding=new(10,5,10,5); source.ToolTip=item.SourceUrl; source.Margin=new(0,0,0,5); links.Children.Add(source); }
        DockPanel.SetDock(links,Dock.Right); bottom.Children.Add(links);
        var meta=Ui.Text(item.IsPdf?$"{item.DateLabel}\n{item.PageCount} páginas":item.DateLabel,11,Ui.Muted); meta.VerticalAlignment=VerticalAlignment.Center; meta.Margin=new(0,0,10,0); bottom.Children.Add(meta); column.Children.Add(bottom);
        var card=Ui.Card(row,18); card.CornerRadius=new(10); card.Margin=new(0,0,0,12); AutomationProperties.SetAutomationId(card,"MemoryCard-"+item.Id); return card;
    }
    private sealed record CardData(MemoryItem Item,Func<MemoryItem,FrameworkElement> Render);
    public sealed class MemoryCardPresenter:ContentControl
    {
        internal IReadOnlyList<Button> DeleteButtons { get; private set; }=[];
        public MemoryCardPresenter() { HorizontalContentAlignment=HorizontalAlignment.Stretch; DataContextChanged+=(_,_)=>Render(); }
        private void Render()
        {
            Content=DataContext is CardData data?data.Render(data.Item):null;
            DeleteButtons=Content is DependencyObject root?Buttons(root).Where(b=>AutomationProperties.GetAutomationId(b).StartsWith("DeleteMemory-",StringComparison.Ordinal)).ToArray():[];
            static IEnumerable<Button> Buttons(DependencyObject? root) { if(root is null) yield break; if(root is Button b) yield return b; for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) foreach(var child in Buttons(VisualTreeHelper.GetChild(root,i))) yield return child; }
        }
    }
}
