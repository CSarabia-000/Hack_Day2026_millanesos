using Mindie.Services;
using Microsoft.Win32;

namespace Mindie.Views;

public sealed class PetCreatorView : UserControl
{
    internal const string DefaultIdea="Un pequeño dragón de arcilla verde, con alas redondas y una taza de té.";
    internal TextBox PromptInput { get; }=Ui.Input("","Prompt para ChatGPT",true);
    private readonly TextBox _name=Ui.Input("Mi mascota","Nombre de la mascota");
    private readonly TextBox _description=Ui.Input("Mi compañero personalizado.","Descripción de la mascota");
    private readonly Image _preview=new() { Height=180,Stretch=Stretch.Uniform };
    private readonly TextBlock _feedback=Ui.Text("",12,Ui.Muted);
    private readonly TextBlock _placeholder=Ui.Text("Arrastra aquí tu hoja PNG\n24 poses · 4 × 6 · Fondo transparente",13,Ui.Muted);
    private readonly Button _create;
    internal ComboBox StateInput { get; }=new();
    private ValidatedPetSheet? _sheet;
    private CancellationTokenSource? _validation;
    public PetCreatorView(AppHost host,Action done)
    {
        var back=Ui.Button("Volver a compañeros",done); back.HorizontalAlignment=HorizontalAlignment.Left; back.FontSize=12; back.MinHeight=32; back.Padding=new(10,6,10,6); back.Margin=new(0,0,0,18);
        var page=Ui.Column(back,Ui.Text("Crea tu propio compañero",28,weight:FontWeights.SemiBold),Ui.Space(6),Ui.Text("De tu imaginación al escritorio, en dos pasos.",14,Ui.Muted),Ui.Space(24));
        var grid=new Grid(); grid.ColumnDefinitions.Add(new() { Width=new(1.15,GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width=new(20) }); grid.ColumnDefinitions.Add(new());
        PromptInput.Height=330; PromptInput.IsReadOnly=true; PromptInput.VerticalContentAlignment=VerticalAlignment.Top; PromptInput.FontSize=12; PromptInput.Padding=new(14); PromptInput.Background=Ui.Ground;
        _name.MaxLength=24; _description.MaxLength=200; PromptInput.Text=PetImportService.Prompt(DefaultIdea);
        var copied=Ui.Text("",12,Ui.Accent); copied.Margin=new(0,7,0,0);
        var copy=Ui.Button("Copiar prompt",()=> { Clipboard.SetText(PromptInput.Text); copied.Text="Copiado. Pégalo en ChatGPT para generar la imagen."; },true,"\uE8C8"); copy.Margin=new(0,0,8,6);
        var open=Ui.Button("Abrir ChatGPT",()=>host.OpenSource("https://chatgpt.com/"));
        open.Margin=new(0,0,0,6); var promptActions=new WrapPanel(); promptActions.Children.Add(copy); promptActions.Children.Add(open);
        var instructions=Ui.Column(Ui.Badge("Paso 1 · En ChatGPT"),Ui.Space(14),Ui.Text("Imagina y genera",21,weight:FontWeights.SemiBold),Ui.Space(8),
            Ui.Text("Copia este prompt y pégalo en ChatGPT. Allí puedes cambiar «pequeño dragón de arcilla verde» por la mascota que quieras. También puedes subir una imagen a ChatGPT y sustituir la descripción del personaje por «inspírate en la imagen».",13,Ui.Muted),
            Ui.Space(18),promptActions,copied,Ui.Space(12),Ui.Field("Prompt listo para copiar",PromptInput),Ui.Text("Descarga el PNG original. Debe incluir 24 poses en una cuadrícula de 4 × 6, con fondo transparente real. Conserva las instrucciones técnicas al personalizar el personaje.",12,Ui.Muted));
        var left=Ui.Card(instructions,20); left.VerticalAlignment=VerticalAlignment.Top; grid.Children.Add(left);
        var picture=new Grid(); _placeholder.TextAlignment=TextAlignment.Center; _placeholder.VerticalAlignment=VerticalAlignment.Center; picture.Children.Add(_preview); picture.Children.Add(_placeholder);
        var drop=new Border { Child=picture,Height=190,Padding=new(14),Background=Ui.Soft,BorderBrush=Ui.Brush("#B9C7B2"),BorderThickness=new(1),CornerRadius=new(10),AllowDrop=true };
        drop.DragOver+=(_,e)=> { var files=e.Data.GetData(DataFormats.FileDrop) as string[]??[]; e.Effects=files.Length==1 && Path.GetExtension(files[0]).Equals(".png",StringComparison.OrdinalIgnoreCase)?DragDropEffects.Copy:DragDropEffects.None; e.Handled=true; };
        drop.Drop+=async(_,e)=> { e.Handled=true; if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length:1 } files) await ImportSheetAsync(files[0]); };
        var browse=Ui.Button("Elegir hoja PNG",async()=> { var picker=new OpenFileDialog { Title="Hoja de animación de tu mascota",Filter="Imagen PNG|*.png" }; if (picker.ShowDialog(host.MainWindow)==true) await ImportSheetAsync(picker.FileName); }); browse.Margin=new(0,12,0,12);
        foreach (var state in Enum.GetValues<PetState>()) StateInput.Items.Add(Pets.StateName(state)); StateInput.SelectedIndex=0;
        StateInput.SelectionChanged+=(_,_)=> { if (_sheet is not null && StateInput.SelectedIndex>=0) _preview.Source=_sheet.Frames[StateInput.SelectedIndex,0]; };
        _create=Ui.AsyncButton("Crear y usar mascota",()=>CreateAsync(host,done),true); _create.IsEnabled=false; _create.Margin=new(0,18,0,0);
        var upload=Ui.Column(Ui.Badge("Paso 2 · En Mindie"),Ui.Space(14),Ui.Text("Importa y dale vida",21,weight:FontWeights.SemiBold),Ui.Space(8),Ui.Text("Elige un nombre e importa la hoja completa. Revisaremos las poses antes de crear tu compañero.",13,Ui.Muted),Ui.Space(18),Ui.Field("Nombre",_name),Ui.Field("Descripción",_description),browse,drop,Ui.Space(12),Ui.Field("Vista estática de cada estado",StateInput),_feedback,_create,Ui.Space(12),Ui.Text("Las animaciones se reproducen solo en la mascota del escritorio. Tu PNG se conserva en este equipo.",12,Ui.Muted));
        var right=Ui.Card(upload,20); right.VerticalAlignment=VerticalAlignment.Top; Grid.SetColumn(right,2); grid.Children.Add(right);
        void Adapt() {
            var stacked=ActualWidth>0 && ActualWidth<760;
            grid.RowDefinitions.Clear(); grid.RowDefinitions.Add(new() { Height=GridLength.Auto }); grid.RowDefinitions.Add(new() { Height=GridLength.Auto });
            Grid.SetColumnSpan(left,stacked?3:1); Grid.SetRow(right,stacked?1:0); Grid.SetColumn(right,stacked?0:2); Grid.SetColumnSpan(right,stacked?3:1); right.Margin=new(0,stacked?20:0,0,0);
        }
        SizeChanged+=(_,_)=>Adapt(); Adapt(); page.Children.Add(grid); Content=page;
        Unloaded+=(_,_)=>_validation?.Cancel();
    }
    private async Task CreateAsync(AppHost host,Action done)
    {
        if (_sheet is null) return; _create.IsEnabled=false;
        try { var pet=await host.Store.AddCustomPetAsync(_name.Text,_description.Text,_sheet); var settings=host.Settings.Copy(); settings.Pet=pet.Id; settings.PetName=pet.Name; settings.Onboarded=true; await host.SaveSettingsAsync(settings); host.ShowPet(); done(); host.Report("Tu nueva mascota ya está en el escritorio."); }
        catch (Exception ex) { _feedback.Text=ex.Message; _create.IsEnabled=true; }
    }
    internal async Task ImportSheetAsync(string path)
    {
        _validation?.Cancel(); var request=new CancellationTokenSource(); _validation=request; _sheet=null; _preview.Source=null; _placeholder.Visibility=Visibility.Visible; _create.IsEnabled=false; _feedback.Text="Comprobando transparencia, cuadrícula y animaciones…";
        try { var sheet=await PetImportService.ReadAsync(path,request.Token); if (_validation!=request) return; _sheet=sheet; StateInput.SelectedIndex=0; _preview.Source=sheet.Frames[0,0]; _placeholder.Visibility=Visibility.Collapsed; _feedback.Text="Hoja válida: 24 poses y 6 estados. Ya puedes crear la mascota."; _create.IsEnabled=true; }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (_validation==request) _feedback.Text=ex.Message; }
        finally { if (_validation==request) _validation=null; request.Dispose(); }
    }
}
