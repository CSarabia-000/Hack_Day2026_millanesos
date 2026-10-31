using Mindie.Services;
using Mindie.Views;
using Mindie.Controls;
using System.Windows.Automation;

namespace Mindie.Diagnostics;

public static partial class SelfTest
{
    private static async Task CheckPreferencesAsync(AppHost host,MainWindow window,string output,Action<bool,string> check)
    {
        var original=host.Settings.Copy(); window.Navigate("settings"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var view=new SettingsView(host,(_,_)=>Task.FromResult<IReadOnlyList<LocalModelInfo>>([new(original.Model,["completion","vision","thinking","audio"]),new("gemma4:12b-prueba",["completion"])])); window.PageHost.Content=view;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var picker=FindElements<LocalModelPicker>(view).Single(); await picker.Loading;
        check(!FindButtons(view).Any(b=>AutomationProperties.GetName(b)=="Guardar cambios"),"Configuración no requiere un botón para guardar cambios");
        var name=FindElements<TextBox>(view).Single(t=>AutomationProperties.GetName(t)=="Nombre del compañero"); name.Text="Nombre automático";
        await UntilAsync(()=>host.Settings.PetName=="Nombre automático"); await view.LastSave;
        var reloaded=new LibraryStore(host.Store.Root); await reloaded.LoadAsync();
        check(reloaded.Data.Settings.PetName=="Nombre automático","El nombre se guarda automáticamente en disco al terminar de escribir");
        var context=FindElements<CheckBox>(view).Single(t=>AutomationProperties.GetName(t)=="Dar contexto antes de analizar una captura"); context.IsChecked=!original.GiveContext; await view.LastSave;
        check(host.Settings.GiveContext!=original.GiveContext,"Los controles de Configuración aplican y guardan cada cambio sin confirmación adicional");
        var endpoint=FindElements<TextBox>(view).Single(t=>AutomationProperties.GetName(t)=="Dirección local de Ollama"); endpoint.Text="https://externo.example"; await Task.Delay(550); await view.LastSave;
        check(host.Settings.Endpoint==original.Endpoint && FindElements<TextBlock>(view).Any(t=>t.Text.Contains("dirección local")),"Una dirección inválida muestra una explicación y conserva la conexión guardada");
        var specific=FindElements<CheckBox>(view).Single(t=>AutomationProperties.GetName(t)=="Respuestas detalladas"); specific.IsChecked=!original.Specific; await view.LastSave;
        check(host.Settings.Specific!=original.Specific,"Un campo inválido no bloquea guardar otras preferencias válidas");
        endpoint.Text=original.Endpoint; await Task.Delay(550); await view.LastSave;
        SaveVisual(window,Path.Combine(output,"settings-autosave.png"));
        var size=FindElements<Slider>(view).Single(); size.Value=160; await Task.Delay(350); await view.LastSave;
        picker.Selector.SelectedItem=picker.Selector.Items.OfType<LocalModelInfo>().Single(m=>m.Name=="gemma4:12b-prueba"); await view.FlushPendingAsync();
        reloaded=new LibraryStore(host.Store.Root); await reloaded.LoadAsync();
        check(reloaded.Data.Settings.PetSize==160 && reloaded.Data.Settings.Model=="gemma4:12b-prueba","Tamaño y modelo se guardan automáticamente en disco sin perder otras preferencias");
        var scroll=(ScrollViewer)view.Content; scroll.ScrollToVerticalOffset(650); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); SaveVisual(window,Path.Combine(output,"settings-connection.png"));
        scroll.ScrollToEnd(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); SaveVisual(window,Path.Combine(output,"settings-data.png"));
        name.Text="Guardado al salir"; window.Navigate("saved"); await view.LastSave;
        check(host.Settings.PetName=="Guardado al salir","Salir de Configuración termina de guardar el texto pendiente");
        await host.SaveSettingsAsync(original);
        var saved=(SavedView)window.PageHost.Content; var search=FindElements<TextBox>(saved).Single(t=>AutomationProperties.GetName(t)=="Buscar recuerdos");
        search.Text="LLM"; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        SaveVisual(window,Path.Combine(output,"search-llm.png"));
        var with=SaveElement(search,Path.Combine(output,"search-visible-text.png")); search.Clear(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); var without=SaveElement(search,Path.Combine(output,"search-empty-text.png"));
        check(!SamePixels(with,without),"LLM produce texto visible dentro del buscador, sin recorte vertical");
        using var stream=System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Sprites/pollito.png")).Stream; using var buffer=new MemoryStream(); await stream.CopyToAsync(buffer);
        var sheet=await PetImportService.ValidateAsync(buffer.ToArray()); var pet=await host.Store.AddCustomPetAsync("Para eliminar","Prueba",sheet);
        host.Settings.Pet=pet.Id; host.Settings.PetName=pet.Name; await host.Store.SaveAsync(); window.Navigate("home");
        var confirmed=false; var home=new HomeView(host,_=>confirmed); window.PageHost.Content=home; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var trash=FindButtons(home).Single(b=>AutomationProperties.GetName(b)=="Eliminar mascota: Para eliminar");
        check(trash.ToolTip is string && trash.Width==34,"Las mascotas personalizadas incluyen una papelera compacta con etiqueta accesible");
        ((ScrollViewer)home.Content).ScrollToEnd(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); SaveVisual(window,Path.Combine(output,"pet-delete.png"));
        var memories=host.Store.Data.Memories.Count; trash.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(host.Settings.Pet==pet.Id && host.Store.Data.CustomPets.Contains(pet) && File.Exists(host.Store.PetPath(pet.Id)),"Cancelar la papelera conserva la mascota y su selección");
        confirmed=true; trash.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await UntilAsync(()=>!File.Exists(host.Store.PetPath(pet.Id)));
        reloaded=new LibraryStore(host.Store.Root); await reloaded.LoadAsync();
        check(!File.Exists(host.Store.PetPath(pet.Id)) && !reloaded.Data.CustomPets.Any(p=>p.Id==pet.Id) && host.Settings.Pet=="mindie" && host.Store.Data.Memories.Count==memories,"Eliminar la mascota activa borra su hoja y registro, selecciona Mindie y conserva todos los recuerdos");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
        SaveVisual(window,Path.Combine(output,"pet-after-delete.png"));
        check(window.IsVisible && window.CurrentPage=="home" && FindButtons(home).Any(b=>AutomationProperties.GetName(b)=="Elegir Pollito") && !FindButtons(home).Any(b=>AutomationProperties.GetName(b)=="Eliminar mascota: Para eliminar"),"La papelera actualiza la galería y mantiene la ventana abierta");
        try { await host.Store.DeleteCustomPetAsync("pollito"); check(false,"Proteger compañeros incluidos"); } catch(ArgumentException) { check(true,"Los compañeros incluidos no se borran por accidente"); }
        await host.SaveSettingsAsync(original);
    }
}
