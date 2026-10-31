using Mindie.Controls;
using Mindie.Services;
using Mindie.Views;
using Mindie.Windows;
using System.Reflection;
using System.Windows.Automation;

namespace Mindie.Diagnostics;

public static partial class SelfTest
{
    private static async Task CheckOptimizationsAsync(AppHost host,MainWindow window,string output,Action<bool,string> check)
    {
        foreach(var pet in Pets.All) {
            var preview=new FormatConvertedBitmap(SpriteCatalog.Still(pet.Id,PetState.Idle),PixelFormats.Bgra32,null,0);
            check(SamePixels(preview,SpriteCatalog.Frame(pet.Id,PetState.Idle,0)),pet.Name+": la miniatura preparada conserva exactamente la primera pose normalizada");
        }
        var original=host.Store.Data.Memories;
        try {
            var items=Enumerable.Range(0,400).Select(i=>new MemoryItem { Title=$"Tarjeta de prueba {i}",Summary="Lectura para explorar ideas",Category=Pets.Categories[i%5],CreatedAt=DateTimeOffset.UtcNow.AddMinutes(-i) }).ToList();
            host.Store.Data.Memories=items; var png=MakeFixture();
            await File.WriteAllBytesAsync(host.Store.CapturePath(items[0].Id),png); await File.WriteAllBytesAsync(host.Store.CapturePath(items[^1].Id),png);
            window.Navigate("saved"); string? attempted=null;
            var view=new SavedView(host,item=> { attempted=item.Id; return false; }); window.PageHost.Content=view;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var list=FindElements<ListBox>(view).Single();
            Border[] Cards()=>FindElements<Border>(view).Where(b=>AutomationProperties.GetAutomationId(b).StartsWith("MemoryCard-",StringComparison.Ordinal)).ToArray();
            check(list.Items.Count==400 && Cards().Length is >0 and <20,"La biblioteca conserva los 400 recuerdos y materializa solo las tarjetas cercanas a la pantalla");
            var first=Cards().Single(b=>AutomationProperties.GetAutomationId(b)=="MemoryCard-"+items[0].Id); host.Refresh(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(ReferenceEquals(first,Cards().Single(b=>AutomationProperties.GetAutomationId(b)=="MemoryCard-"+items[0].Id)),"Una actualización sin cambios conserva la tarjeta y su imagen en lugar de reconstruirlas");
            items[0].Summary="Un resumen actualizado que debe verse"; host.Refresh(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(FindElements<TextBlock>(view).Any(t=>t.Text==items[0].Summary),"Editar los metadatos invalida la vista y presenta el nuevo resumen");
            var image=FindElements<Image>(view).Select(i=>i.Source).OfType<BitmapSource>().First();
            check(Math.Max(image.PixelWidth,image.PixelHeight)<=ThumbnailCache.MaximumSide && File.ReadAllBytes(host.Store.CapturePath(items[0].Id)).SequenceEqual(png),"Las miniaturas se decodifican a tamaño limitado y la captura original permanece intacta");
            list.ScrollIntoView(list.Items[^1]); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            check(Cards().Length<20 && Cards().Any(b=>AutomationProperties.GetAutomationId(b)=="MemoryCard-"+items[^1].Id),"El desplazamiento llega al último recuerdo manteniendo acotada la cantidad de tarjetas");
            FindButtons(view).Single(b=>AutomationProperties.GetAutomationId(b)=="DeleteMemory-"+items[^1].Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(attempted==items[^1].Id && items.Count==400,"Una tarjeta reutilizada dirige la papelera al recuerdo correcto y cancelar lo conserva");
            SaveVisual(window,Path.Combine(output,"saved-virtualized-last.png"));
            window.Navigate("brain"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); var brain=(BrainView)window.PageHost.Content; await brain.RebuildAsync();
            var scene=brain.Scene.Graph; host.Refresh(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(ReferenceEquals(scene,brain.Scene.Graph),"Cambiar el estado de la aplicación no reconstruye un grafo cuyos datos permanecen iguales");
        }
        finally { host.Store.Data.Memories=original; window.Navigate("home"); }

        var history=host.Messages.ToArray();
        try {
            host.Messages.Clear(); host.Messages.Add(new() { Role="assistant",Content="Una **respuesta** que debe conservar su selección." });
            window.Navigate("chat"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var rich=FindElements<RichTextBox>(window.PageHost).Single(); rich.SelectAll(); var selected=rich.Selection.Text;
            host.Refresh(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(ReferenceEquals(rich,FindElements<RichTextBox>(window.PageHost).Single()) && rich.Selection.Text==selected,"Una actualización de estado conserva el Markdown renderizado y la selección de la respuesta");
            host.Messages.Add(new() { Role="assistant",Content="Otra respuesta." }); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(FindElements<RichTextBox>(window.PageHost).Contains(rich),"Añadir una respuesta reutiliza las anteriores en vez de volver a interpretar su Markdown");
        }
        finally { window.Navigate("home"); host.Messages.Clear(); foreach(var message in history) host.Messages.Add(message); }

        var widget=new PetWindow(host); widget.Sprite.ReduceMotion=false; widget.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); widget.Touch(); widget.CloseActions();
        var players=(HashSet<SpritePlayer>)typeof(SpritePlayer).GetField("Players",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        widget.Hide(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(!players.Contains(widget.Sprite),"Ocultar la mascota la retira del reloj de animación");
        widget.Show(); widget.Sprite.ReduceMotion=true; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(!players.Contains(widget.Sprite),"Reducir el movimiento evita mantener un reloj activo para una pose estática");
        widget.Sprite.ReduceMotion=false; widget.Sprite.State=PetState.Move; widget.Touch(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var due=(long)typeof(SpritePlayer).GetField("_nextChange",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(widget.Sprite)!;
        check(!SystemParameters.ClientAreaAnimation || players.Contains(widget.Sprite) && due-Environment.TickCount64>3000,"La animación reanuda al mostrarse y el reposo programa el próximo parpadeo sin sondeos cada 80 ms"); widget.Close();

        var document=new PdfKnowledge { Pages=[new() { Number=1,Text="El dato de referencia es ALCORNOQUE-421." }] };
        var evidence=KnowledgeText.Evidence(document,"dato de referencia");
        check(KnowledgeText.Evidence(document,"dato de referencia")==evidence,"Reutilizar el índice de PDF conserva la evidencia y las citas de página");
        document.Pages[0].Text="El dato de referencia es NOGAL-927.";
        check(KnowledgeText.Evidence(document,"dato de referencia").Contains("NOGAL-927") && !KnowledgeText.Evidence(document,"dato de referencia").Contains("ALCORNOQUE-421"),"Cambiar el texto de un PDF invalida el índice anterior");
        using(var cancel=new CancellationTokenSource()) { cancel.Cancel(); try { KnowledgeText.Evidence(document,"dato",cancel.Token); check(false,"Cancelar recuperación"); } catch(OperationCanceledException) { check(true,"La recuperación de evidencia respeta la cancelación incluso con índice preparado"); } }

        var store=new LibraryStore(Path.Combine(output,"queued-save-"+Guid.NewGuid().ToString("N"))); await store.LoadAsync();
        store.Data.Memories.Add(new() { Title="Primer estado",SourceText=new string('a',200000) }); var saving=store.SaveAsync();
        store.Data.Memories[0].Title="Último estado"; var last=store.SaveAsync(); await Task.WhenAll(saving,last);
        var loaded=new LibraryStore(store.Root); await loaded.LoadAsync();
        check(loaded.Data.Memories[0].Title=="Último estado" && loaded.Data.Memories[0].SourceText.Length==200000 && !File.Exists(Path.Combine(store.Root,"library.json.tmp")),"Los guardados en cola escriben el estado más reciente sin perder texto ni dejar un archivo temporal");
        check(!(await File.ReadAllTextAsync(Path.Combine(store.Root,"library.json"))).Contains("dateLabel",StringComparison.OrdinalIgnoreCase),"La biblioteca no almacena etiquetas de fecha que la interfaz puede calcular");
        var json=await File.ReadAllTextAsync(Path.Combine(store.Root,"library.json")); await File.WriteAllTextAsync(Path.Combine(store.Root,"library.json"),json,Encoding.UTF8);
        loaded=new(store.Root); await loaded.LoadAsync();
        check(loaded.Data.Memories[0].Title=="Último estado","La lectura por flujo sigue abriendo las bibliotecas UTF-8 con BOM de versiones anteriores");
        var export=Path.Combine(store.Root,"export.json"); await File.WriteAllTextAsync(export,"exportación anterior"); store.Data.Memories.Add(new() { Title="PDF ausente de prueba",Kind="pdf" });
        try { await store.ExportAsync(export); check(false,"Fallar exportación"); } catch(IOException) { check(await File.ReadAllTextAsync(export)=="exportación anterior" && !Directory.GetFiles(store.Root,"export.json.*.tmp").Any(),"Una exportación fallida conserva el destino anterior y elimina solo su archivo temporal"); }
    }
}
