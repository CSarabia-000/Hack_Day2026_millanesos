using Mindie.Services;
using Mindie.Views;
using Mindie.Controls;

namespace Mindie.Windows;

public sealed class MemoryEditorWindow : Window
{
    private readonly AppHost _host;
    private readonly byte[] _png;
    private readonly MemoryItem? _original;
    private readonly TextBox _title = Ui.Input("","Título del recuerdo");
    private readonly TextBox _summary = Ui.Input("","Resumen",true);
    private readonly TextBox _ideas = Ui.Input("","Ideas, una por línea",true);
    internal TextBox ContextInput { get; } = Ui.Input("","Contexto del recuerdo",true);
    internal TextBox SourceInput { get; } = Ui.Input("","Enlace de origen");
    private readonly ComboBox _category = new();
    private readonly TextBlock _feedback = Ui.Text("",12,Ui.Muted);
    private readonly Button _save;
    private readonly Button _analyze;
    private readonly ThinkingOrb _orb=new() { Width=100,Height=100 };
    private readonly TextBlock _analysisStatus=Ui.Text("",13,Ui.Muted);
    private readonly Border _loading;
    private readonly ScrollViewer _form;
    private bool _analyzing;
    private bool _saving;
    private bool _deleted;
    private readonly bool _fullPage;
    private readonly string _pageTitle;
    private string _sourceText;
    private string _originalSourceText;
    private int _sourceCleanupVersion;
    private TextBox? _sourceReader;
    private TextBlock? _sourceHint;
    private Button? _sourceToggle;
    private bool _showOriginal;
    private readonly bool _textMode;
    public MemoryEditorWindow(AppHost host, byte[] png, MemoryItem? item = null, string context = "", string sourceUrl = "",bool fullPage=false,string pageTitle="",WebPageText? webText=null)
    {
        Ui.WindowStyle(this);
        _host=host; _png=png; _original=item; _textMode=webText is not null || item?.Kind=="web-text"; _sourceText=webText?.Text??item?.SourceText??""; _fullPage=fullPage || item?.Kind=="web"; _pageTitle=webText?.Title??item?.SourceName??pageTitle; Title = item is null ? "Mindie · Guardar en mente" : "Mindie · Recuerdo"; Width = 1020; Height = 760; MinWidth = 820; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _originalSourceText=webText?.OriginalText??item?.OriginalSourceText??""; _sourceCleanupVersion=webText?.CleanupVersion??item?.WebTextCleanupVersion??0;
        var page = new DockPanel { Margin = new(30) }; var heading = Ui.Heading("Un detalle que se queda.","Revisa el título y las ideas antes de guardar. Puedes editarlo todo."); DockPanel.SetDock(heading,Dock.Top); page.Children.Add(heading);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = new(27) }); grid.ColumnDefinitions.Add(new());
        if (_textMode)
        {
            _sourceReader=Ui.Input(_sourceText,"Texto guardado de la página",true); _sourceReader.IsReadOnly=true; _sourceReader.MaxLength=0; _sourceReader.BorderThickness=new(0); _sourceReader.Padding=new(2); _sourceReader.VerticalContentAlignment=VerticalAlignment.Top;
            _sourceHint=Ui.Text("",12,Ui.Muted); _sourceHint.Margin=new(0,8,0,0);
            _sourceToggle=Ui.Button("Ver texto original",()=> { _showOriginal=!_showOriginal; UpdateSourcePreview(); }); _sourceToggle.HorizontalAlignment=HorizontalAlignment.Left; _sourceToggle.FontSize=12; _sourceToggle.MinHeight=30; _sourceToggle.Padding=new(10,5,10,5); _sourceToggle.Margin=new(0,9,0,0);
            var preview=new DockPanel(); var label=Ui.Column(Ui.Badge("Página web · Solo texto"),Ui.Space(12),Ui.Text(_pageTitle,18,weight:FontWeights.SemiBold),_sourceHint,_sourceToggle,Ui.Space(15));
            DockPanel.SetDock(label,Dock.Top); preview.Children.Add(label); preview.Children.Add(_sourceReader); grid.Children.Add(Ui.Card(preview,18)); UpdateSourcePreview();
        }
        else
        {
            var picture=new Image { Source=Images.Decode(png),Stretch=Stretch.Uniform }; grid.Children.Add(new Border { Background=Ui.Brush("#EAEDEF"),CornerRadius=new(10),Padding=new(12),Child=_fullPage?Ui.Scroll(picture):picture });
        }
        _title.MaxLength = 100; _summary.Height = 74; _summary.MaxLength = 1500; _ideas.Height = 150; _ideas.MaxLength = 4000; foreach (var c in Pets.Categories) _category.Items.Add(c); _category.SelectedIndex = 0;
        _save = Ui.AsyncButton(item is null ? "Guardar recuerdo" : "Guardar cambios", SaveAsync,true,"\uE74E");
        _analyze = Ui.AsyncButton("Analizar con Gemma",async () => { if (_analyzing) host.Cancel(); else await AnalyzeAsync(); });
        var form = Ui.Column(); ContextInput.Text=item?.UserContext ?? context; ContextInput.Height=120; ContextInput.MaxLength=0;
        SourceInput.Text=item?.SourceUrl ?? sourceUrl;
        if (item?.IsPdf!=true && (host.Settings.GiveContext || ContextInput.Text.Length>0)) form.Children.Add(Ui.Field("Tu contexto",ContextInput,"Se guarda completo junto con la captura. Puedes modificarlo y volver a analizar."));
        foreach (var field in new UIElement[] { Ui.Field("Título",_title),Ui.Field("Enlace de origen (opcional)",SourceInput,"Pega el enlace de la página que estás guardando."),Ui.Field("En pocas palabras",_summary),Ui.Field("Ideas clave",_ideas,"Una idea por línea, hasta 8."),Ui.Field("Categoría",_category),_feedback,Ui.Space(14),_analyze,Ui.Space(12),_save }) form.Children.Add(field);
        if (item is not null)
        {
            Fill(item);
            var delete = Ui.AsyncButton("Eliminar recuerdo",async () => {
                if (host.IsBusy) { _feedback.Text="Termina o cancela la operación actual antes de eliminar."; return; }
                if (MessageBox.Show(this,$"¿Eliminar “{item.Title}” y {(item.IsPdf ? "su PDF y conversación" : "su captura")}? No podrás recuperarlos.","Eliminar recuerdo",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes) return;
                await host.DeleteMemoryAsync(item);
            });
            delete.Foreground=Ui.Brush("#A02D3C"); delete.Margin=new(0,12,0,0); form.Children.Add(delete);
            void UpdateDeletedState(object? sender,EventArgs args)
            {
                if (host.Store.Data.Memories.Any(m=>m.Id==item.Id)) return;
                _deleted=true; _save.IsEnabled=false; _analyze.IsEnabled=false; delete.IsEnabled=false;
                foreach (var input in new[] { _title,_summary,_ideas,ContextInput,SourceInput }) input.IsReadOnly=true;
                _category.IsEnabled=false; _feedback.Text="Recuerdo eliminado de Mis guardados. Puedes cerrar esta ventana cuando quieras.";
            }
            host.Changed+=UpdateDeletedState; Closed+=(_,_)=>host.Changed-=UpdateDeletedState;
        }
        _form=Ui.Scroll(form);
        var cancel=Ui.Button("Cancelar análisis",()=> { if (_analyzing) host.Cancel(); }); cancel.HorizontalAlignment=HorizontalAlignment.Center; cancel.Margin=new(0,24,0,0);
        _analysisStatus.TextAlignment=TextAlignment.Center; _analysisStatus.MaxWidth=320;
        var loadingCopy=Ui.Column(_orb,Ui.Space(22),Ui.Text("Dándole forma a tu recuerdo",21,weight:FontWeights.SemiBold),Ui.Space(10),_analysisStatus,
            Ui.Space(18),Ui.Text("Preparando el título, el resumen y las ideas clave.",12,Ui.Muted),cancel);
        loadingCopy.HorizontalAlignment=HorizontalAlignment.Center; loadingCopy.VerticalAlignment=VerticalAlignment.Center;
        foreach (var text in loadingCopy.Children.OfType<TextBlock>()) text.TextAlignment=TextAlignment.Center;
        _loading=Ui.Card(loadingCopy,24); _loading.Visibility=Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(_loading,"Generando los campos del recuerdo");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(_analysisStatus,System.Windows.Automation.AutomationLiveSetting.Polite);
        var analysisArea=new Grid(); analysisArea.Children.Add(_form); analysisArea.Children.Add(_loading);
        Grid.SetColumn(analysisArea,2); grid.Children.Add(analysisArea); page.Children.Add(grid); Content = page;
        void UpdateMotion(object? sender,EventArgs args)=>_orb.ReduceMotion=host.Settings.ReduceMotion;
        host.Changed+=UpdateMotion; UpdateMotion(null,EventArgs.Empty);
        Loaded += async (_,_) => { NativeMethods.FitToWorkArea(this,true); if (_original is null) await AnalyzeAsync(); };
        Closing += (_,e) => { if (_saving) { e.Cancel = true; return; } _orb.IsActive=false; if (_analyzing) host.Cancel(); };
        Closed+=(_,_)=>host.Changed-=UpdateMotion;
    }
    private void Fill(MemoryItem item)
    {
        _title.Text=item.Title; _summary.Text=item.Summary; _ideas.Text=string.Join(Environment.NewLine,item.Ideas); _category.SelectedItem=item.Category;
        if (_textMode && item.Kind=="web-text") {
            _sourceText=item.SourceText; _originalSourceText=item.OriginalSourceText; _sourceCleanupVersion=item.WebTextCleanupVersion; _showOriginal=false; UpdateSourcePreview();
        }
    }
    private void UpdateSourcePreview()
    {
        if (_sourceReader is null || _sourceHint is null || _sourceToggle is null) return;
        _sourceReader.Text=_showOriginal?_originalSourceText:_sourceText;
        _sourceHint.Text=_showOriginal?"Texto original, antes de la limpieza.":_sourceCleanupVersion>0?"Texto revisado por Gemma: se conserva el contenido del artículo.":"Gemma quitará publicidad, menús y fragmentos ajenos antes de analizar.";
        _sourceToggle.Visibility=_originalSourceText.Length>0?Visibility.Visible:Visibility.Collapsed;
        _sourceToggle.Content=_showOriginal?"Ver texto limpio":"Ver texto original";
        System.Windows.Automation.AutomationProperties.SetName(_sourceToggle,_showOriginal?"Ver texto limpio":"Ver texto original");
    }
    private async Task AnalyzeAsync()
    {
        if (_deleted || _analyzing) return;
        if (_host.IsBusy) { _feedback.Text="Termina o cancela la operación actual antes de analizar este recuerdo."; return; }
        if (_original?.IsPdf!=true && _host.Settings.GiveContext && string.IsNullOrWhiteSpace(ContextInput.Text)) { _feedback.Text="Escribe el contexto antes de analizar esta captura."; ContextInput.Focus(); return; }
        _analyzing = true; _save.IsEnabled = false; _analyze.Content = "Detener análisis";
        _analysisStatus.Text=_textMode?"Gemma está leyendo el texto del artículo…":_fullPage?"Gemma está leyendo la página completa…":_original?.IsPdf==true?"Gemma está leyendo el documento…":"Gemma está leyendo tu captura…";
        _form.Visibility=Visibility.Collapsed; _loading.Visibility=Visibility.Visible; _orb.IsActive=true;
        try
        {
            var progress=new Progress<string>(text=> { if (_analyzing) _analysisStatus.Text=text; });
            Fill(_original?.IsPdf==true ? await _host.SummarizeSavedPdfAsync(_original,progress) : _textMode ? await _host.AnalyzeWebTextAsync(new(_sourceText,LibraryStore.ValidateSourceUrl(SourceInput.Text),_pageTitle,CleanupVersion:_sourceCleanupVersion,OriginalText:_originalSourceText),ContextInput.Text,progress) : _fullPage ? await _host.AnalyzeWebAsync(_png,ContextInput.Text,progress) : await _host.AnalyzeAsync(_png,ContextInput.Text,progress));
            _feedback.Text = "Resumen listo. Revísalo antes de guardarlo.";
        }
        catch (OperationCanceledException) { _feedback.Text = "Análisis cancelado. Puedes completar el recuerdo a mano."; }
        catch (Exception ex) { _feedback.Text = ex.Message + " Puedes completarlo a mano."; }
        finally { _analyzing=false; _orb.IsActive=false; _loading.Visibility=Visibility.Collapsed; _form.Visibility=Visibility.Visible; _save.IsEnabled=!_deleted; _analyze.Content="Volver a analizar"; _analyze.IsEnabled=!_deleted; }
    }
    private async Task SaveAsync()
    {
        if (_deleted || _saving || _analyzing) return; var item = _original?.Copy() ?? new MemoryItem(); item.Title = _title.Text; item.Summary = _summary.Text; item.Ideas = _ideas.Text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries).ToList(); item.Category = (string)_category.SelectedItem; item.UserContext=ContextInput.Text; item.SourceUrl=SourceInput.Text;
        try
        {
            if (_fullPage) { item.Kind="web"; item.SourceName=_pageTitle; }
            if (_textMode) { item.Kind="web-text"; item.SourceName=_pageTitle; item.SourceText=_sourceText; item.OriginalSourceText=_originalSourceText; item.WebTextCleanupVersion=_sourceCleanupVersion; }
            LibraryStore.ValidateMemory(item); _saving = true; _save.IsEnabled = false;
            if (_original is null) await _host.Store.AddAsync(item,_textMode?WebTextThumbnail.Create(new(_sourceText,item.SourceUrl,_pageTitle)):_png);
            else { var index = _host.Store.Data.Memories.IndexOf(_original); if (index < 0) throw new InvalidOperationException("El recuerdo ya no existe."); _host.Store.Data.Memories[index] = item; try { await _host.Store.SaveAsync(); } catch { _host.Store.Data.Memories[index] = _original; throw; } }
            _saving = false; _host.Refresh(); _host.Report("Recuerdo guardado en tu equipo."); Close();
        }
        catch (Exception ex) { _feedback.Text = ex.Message; _saving = false; _save.IsEnabled = true; }
    }
}
