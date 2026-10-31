using Mindie.Controls;
using System.Windows.Automation;

namespace Mindie.Views;

public sealed class HomeView : UserControl
{
    private readonly AppHost _host;
    private readonly Func<PetInfo,bool> _confirmDelete;
    private readonly StackPanel _page = new();
    private bool _creating;
    private string _shownPet="",_shownName="";
    private bool _shownWidget;
    private (string Id,string Name,string Description)[] _shownCustom=[];
    public HomeView(AppHost host):this(host,pet=>MessageBox.Show(host.MainWindow,$"¿Eliminar a {pet.Name}? Si está en uso, te acompañará Mindie. Tus recuerdos se conservan.","Eliminar mascota",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes) { }
    internal HomeView(AppHost host,Func<PetInfo,bool> confirmDelete) { _host = host; _confirmDelete=confirmDelete; Content = Ui.Scroll(_page); Draw(); host.Changed += OnChanged; Unloaded += (_, _) => host.Changed -= OnChanged; }
    private void OnChanged(object? sender, EventArgs e) {
        if (!_host.IsBusy && !_creating && (_shownPet!=_host.Settings.Pet || _shownName!=_host.Settings.PetName || _shownWidget!=(_host.PetWindow?.IsVisible==true) || !_shownCustom.SequenceEqual(_host.Store.Data.CustomPets.Select(p=>(p.Id,p.Name,p.Description))))) Draw();
    }
    private void Draw()
    {
        _creating=false; _page.Children.Clear(); var selected = Pets.Find(_host.Settings.Pet);
        _shownPet=_host.Settings.Pet; _shownName=_host.Settings.PetName; _shownWidget=_host.PetWindow?.IsVisible==true; _shownCustom=_host.Store.Data.CustomPets.Select(p=>(p.Id,p.Name,p.Description)).ToArray();
        var activateTop = Ui.AsyncButton(_host.PetWindow?.IsVisible == true ? "Mostrar compañero" : "Llevar al escritorio", async () => { var s = _host.Settings.Copy(); s.Onboarded = true; await _host.SaveSettingsAsync(s); _host.ShowPet(); },true,"\uE8A7");
        var create=Ui.Button("Crear mascota",()=> { _creating=true; _page.Children.Clear(); _page.Children.Add(new PetCreatorView(_host,Draw)); }); create.Margin=new(0,0,10,0);
        _page.Children.Add(Ui.Heading("Un compañero para tu día.", "Elige quién te acompaña. Tus ideas y recuerdos, siempre a mano.",Ui.Row(create,activateTop)));
        var available=Pets.All.Concat(_host.Store.Data.CustomPets.Where(p=>SpriteCatalog.HasAnimation(p.Id)).Select(p=>p.Info)).ToArray();
        var grid = new Grid(); for (var i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new());
        for (var row=0;row<(available.Length+2)/3;row++) grid.RowDefinitions.Add(new() { Height=GridLength.Auto });
        for (var i = 0; i < available.Length; i++)
        {
            var pet = available[i]; var isSelected = pet.Id == selected.Id; var sprite = new SpritePlayer { PetId = pet.Id, Width = 130, Height = 130, Animate = false, State = PetState.Idle };
            var title = Ui.Text(pet.Name, 16, weight: FontWeights.SemiBold); var description = Ui.Text(pet.Description, 12, Ui.Muted); description.Margin = new(0, 4, 0, 0); description.MinHeight = 37;
            var top = new Grid(); var indicator = Ui.Text(isSelected ? "Seleccionado" : "", 11, Ui.Accent); indicator.HorizontalAlignment = HorizontalAlignment.Right; top.Children.Add(indicator);
            if(pet.Id=="mindie") top.Children.Add(Ui.Text("Principal",11,Ui.Accent,FontWeights.SemiBold));
            var contents = Ui.Column(top, sprite, title, description);
            var button = new Button { Content = contents, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(19, 12, 19, 17), Margin = new(0, 0, 14, 14), Background = isSelected ? Ui.Brush("#F0F3ED") : Brushes.White, BorderBrush = isSelected ? Ui.Brush("#819578") : Ui.Line, BorderThickness = new(isSelected ? 1.5 : 1) };
            AutomationProperties.SetName(button, $"Elegir {pet.Name}");
            button.Click += async (_, _) => await Ui.Safe(async () => { var s = _host.Settings.Copy(); if (s.Pet != pet.Id) { s.Pet = pet.Id; s.PetName = pet.Nickname; } await _host.SaveSettingsAsync(s); });
            var cell=new Grid(); cell.Children.Add(button);
            if(pet.Id.StartsWith("custom-",StringComparison.Ordinal)) {
                var delete=Ui.IconButton("Eliminar mascota: "+pet.Name,"\uE74D",async()=>await Ui.Safe(async()=> {
                    if(!_confirmDelete(pet)) return;
                    await _host.Store.DeleteCustomPetAsync(pet.Id); _host.PetWindow?.ApplySettings(); _host.Refresh();
                }));
                delete.Foreground=Ui.Brush("#B03A48"); delete.HorizontalAlignment=HorizontalAlignment.Right; delete.VerticalAlignment=VerticalAlignment.Top; delete.Margin=new(0,10,24,0); cell.Children.Add(delete);
                top.Margin=new(0,0,35,0);
            }
            Grid.SetRow(cell,i/3); Grid.SetColumn(cell,i%3); grid.Children.Add(cell);
        }
        grid.Margin = new(0,0,-14,0); _page.Children.Add(grid);
        var strip = new Grid(); strip.ColumnDefinitions.Add(new()); strip.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var name = Ui.Text(selected.Id.StartsWith("custom-")?$"{_host.Settings.PetName}, creado por ti":selected.Id=="mindie"?$"{_host.Settings.PetName}, tu compañero":$"{_host.Settings.PetName}, tu {selected.Name.ToLowerInvariant()}", 16, weight: FontWeights.SemiBold); var detail = Ui.Text(selected.Detail, 12, Ui.Muted); detail.Margin = new(0,6,0,0); strip.Children.Add(Ui.Column(name, detail));
        var badge = Ui.Text("Animado en tu escritorio",12,Ui.Muted); badge.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(badge,1); strip.Children.Add(badge); _page.Children.Add(Ui.Card(strip));
        var hint = Ui.Text("Arrastra al compañero para colocarlo. Acerca el mouse para escribir, dictar desde el input o guardar en mente.", 12, Ui.Muted); hint.Margin = new(0,12,0,15); _page.Children.Add(hint);
        foreach (var warning in _host.Store.PetWarnings) _page.Children.Add(Ui.Text(warning,12,Ui.Muted));
    }
}
