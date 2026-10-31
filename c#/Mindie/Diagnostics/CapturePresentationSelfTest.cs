using Mindie.Services;
using Mindie.Windows;
using System.Windows.Automation;
using System.Windows.Interop;

namespace Mindie.Diagnostics;

public static partial class SelfTest
{
    private static async Task CheckCapturePresentationAsync(AppHost host,MainWindow main,string output,Action<bool,string> check)
    {
        var handler=new KnowledgeHandler(); using var captureHost=new AppHost(host.Store,new OllamaService(handler)) { MainWindow=main };
        var pet=new PetWindow(captureHost); var content=new Window { Title="Contenido actual · Prueba Mindie",Width=1100,Height=740,Background=new SolidColorBrush(Color.FromRgb(33,55,70)),Content=new TextBlock { Text="Contenido de prueba\nEsta ventana permanece detrás del menú de captura.",Foreground=Brushes.White,FontSize=26,Margin=new(50) } };
        var originalPage=main.CurrentPage; var activations=0;
        void MainActivated(object? sender,EventArgs e)=>activations++;
        main.Activated+=MainActivated;
        try
        {
            main.Show(); main.WindowState=WindowState.Normal; content.Show(); content.Activate(); pet.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); activations=0;
            check(IsAbove(content,main),"La prueba sitúa contenido independiente delante de la biblioteca antes de abrir el menú");
            async Task CancelChoice(Action invoke,string description)
            {
                var correct=false; var foregroundKept=false; var fitted=false; Exception? error=null;
                var callback=Dispatcher.CurrentDispatcher.BeginInvoke(()=> {
                    var choice=System.Windows.Application.Current.Windows.OfType<CaptureModeWindow>().SingleOrDefault(w=>w.IsVisible);
                    try {
                        if(choice is null) throw new InvalidOperationException("No apareció el selector de captura.");
                        correct=choice.Owner==pet && choice.Topmost && !choice.ShowInTaskbar;
                        foregroundKept=activations==0 && !main.IsActive && IsAbove(content,main);
                        NativeMethods.GetWindowRect(new WindowInteropHelper(choice).Handle,out var bounds);
                        var screen=System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(pet).Handle).WorkingArea;
                        fitted=bounds.Left>=screen.Left && bounds.Top>=screen.Top && bounds.Right<=screen.Right && bounds.Bottom<=screen.Bottom && ButtonsFit(choice);
                        SaveVisual(choice,Path.Combine(output,"capture-from-pet.png"));
                    }
                    catch(Exception ex) { error=ex; }
                    finally { choice?.Close(); }
                },DispatcherPriority.ApplicationIdle);
                invoke(); await callback; if(error is not null) throw error;
                check(correct && foregroundKept,description);
                check(fitted,"El menú del compañero queda dentro de su monitor y conserva visibles todas las opciones");
                check(main.CurrentPage==originalPage && activations==0,"Cancelar el menú del compañero no activa ni cambia la sección de la biblioteca");
            }
            await CancelChoice(()=>pet.ActionMenu.CaptureButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),"El icono de la mascota abre un menú independiente sobre el contenido actual, sin elevar la biblioteca");
            content.Activate(); activations=0;
            var context=pet.Sprite.ContextMenu!;
            await CancelChoice(()=>context.Items.OfType<MenuItem>().Single(i=>(string)i.Header=="Guardar en mente").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)),"Guardar en mente del clic derecho usa la misma apertura independiente de la mascota");
            main.WindowState=WindowState.Minimized; content.Activate(); activations=0;
            await CancelChoice(()=>pet.ActionMenu.CaptureButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),"El menú de la mascota se abre sin restaurar la biblioteca minimizada");
            check(main.WindowState==WindowState.Minimized,"Cancelar mantiene la biblioteca minimizada");
            main.Hide(); content.Activate(); activations=0;
            await CancelChoice(()=>pet.ActionMenu.CaptureButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),"El menú de la mascota funciona cuando la biblioteca está oculta");
            check(!main.IsVisible,"Abrir y cancelar desde la mascota no muestra una biblioteca oculta");
            main.WindowState=WindowState.Normal; main.ShowActivated=false; main.Show(); main.ShowActivated=true; content.Activate();
            NativeMethods.SetWindowPos(new WindowInteropHelper(content).Handle,IntPtr.Zero,0,0,0,0,0x0213);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); activations=0;
            check(IsAbove(content,main),"La prueba de restauración parte del contenido delante de la biblioteca");
            var snapshots=new[] { new CaptureWindowSnapshot(main),new CaptureWindowSnapshot(pet) };
            foreach(var snapshot in snapshots) snapshot.Hide(); foreach(var snapshot in snapshots) snapshot.Restore(); foreach(var snapshot in snapshots) snapshot.RestoreActivation();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(main.IsVisible && main.ShowActivated && !main.IsActive && IsAbove(content,main) && pet.IsVisible,"Restaurar ventanas tras seleccionar pantalla conserva la biblioteca detrás del contenido y muestra de nuevo la mascota");
            pet.Topmost=false; NativeMethods.SetWindowPos(new WindowInteropHelper(pet).Handle,IntPtr.Zero,0,0,0,0,0x0213);
            var normalPet=new CaptureWindowSnapshot(pet); normalPet.Hide(); normalPet.Restore();
            check(!pet.Topmost && IsAbove(pet,content) && IsAbove(content,main),"Restaurar un compañero sin Siempre encima conserva su lugar entre las ventanas normales sin elevar la biblioteca");
            pet.Topmost=true;

            using var server=new TextPageServer(); var routed=false; Exception? routeError=null;
            _=Dispatcher.CurrentDispatcher.BeginInvoke(()=> {
                var choice=System.Windows.Application.Current.Windows.OfType<CaptureModeWindow>().Single(w=>w.IsVisible);
                Dispatcher.CurrentDispatcher.BeginInvoke(async()=> {
                    var reader=System.Windows.Application.Current.Windows.OfType<WebTextCaptureWindow>().SingleOrDefault(w=>w.IsVisible);
                    try {
                        if(reader is null) throw new InvalidOperationException("No apareció el lector de texto.");
                        routed=reader.Owner==pet && reader.Topmost && activations==0 && IsAbove(content,main);
                        reader.AddressInput.Text=server.Url; reader.ContextInput.Text="Contexto sintético del artículo."; await reader.ReadAsync();
                        if(reader.TextPreview.Text.Length==0) throw new InvalidOperationException("No se leyó el artículo de prueba.");
                        FindButtons(reader).Single(b=>AutomationProperties.GetName(b)=="Analizar texto").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch(Exception ex) { routeError=ex; reader?.Close(); }
                },DispatcherPriority.ApplicationIdle);
                FindButtons(choice).Single(b=>AutomationProperties.GetName(b)=="Página web: solo texto").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            },DispatcherPriority.ApplicationIdle);
            await captureHost.CaptureFromPetAsync(pet);
            if(routeError is not null) throw routeError;
            check(routed,"El lector de texto continúa sobre el contenido actual sin activar la biblioteca");
            await UntilAsync(()=>handler.Requests.Count>0 && !captureHost.IsBusy);
            var editor=System.Windows.Application.Current.Windows.OfType<MemoryEditorWindow>().Single(w=>w.IsVisible);
            check(editor.Owner is null && editor.ShowInTaskbar && main.CurrentPage==originalPage && activations==0,"El editor del recuerdo se abre independientemente sin navegar ni activar la biblioteca");
            pet.Hide(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(editor.IsVisible,"Ocultar la mascota no oculta el editor independiente ni pierde el recuerdo en preparación");
            var ids=host.Store.Data.Memories.Select(m=>m.Id).ToHashSet();
            FindButtons(editor).Single(b=>AutomationProperties.GetName(b)=="Guardar recuerdo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilAsync(()=>host.Store.Data.Memories.Any(m=>!ids.Contains(m.Id)) && !editor.IsVisible);
            var saved=host.Store.Data.Memories.Single(m=>!ids.Contains(m.Id));
            check(saved.Kind=="web-text" && saved.SourceUrl==server.Url && main.CurrentPage==originalPage && activations==0,"Guardar desde la mascota añade el recuerdo sin traer ni cambiar la biblioteca");
            await host.Store.DeleteAsync(saved);
        }
        finally {
            main.Activated-=MainActivated;
            foreach(var dialog in System.Windows.Application.Current.Windows.OfType<MemoryEditorWindow>().ToArray()) dialog.Close();
            pet.Close(); content.Close(); main.Show(); main.WindowState=WindowState.Normal; main.Activate();
        }
    }
    private static bool IsAbove(Window upper,Window lower)
    {
        var target=new WindowInteropHelper(lower).Handle; var current=new WindowInteropHelper(upper).Handle; var seen=new HashSet<IntPtr>();
        while(current!=IntPtr.Zero && seen.Count<512 && seen.Add(current)) { current=NativeMethods.GetWindow(current,2); if(current==target) return true; }
        return false;
    }
}
