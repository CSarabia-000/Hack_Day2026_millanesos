using Mindie.Services;
using Mindie.Views;
using Mindie.Windows;
using System.Net;
using System.Net.Http;
using System.Windows.Automation;

namespace Mindie.Diagnostics;

public static partial class SelfTest
{
    private static async Task CheckConversationsAsync(MainWindow previous,string output,Action<bool,string> check)
    {
        var root=Path.Combine(output,"conversations-"+Guid.NewGuid().ToString("N")); var store=new LibraryStore(root); await store.LoadAsync();
        store.Data.Version=3; store.Data.Messages=[new() { Content="LEGADO-204" }]; await store.SaveAsync();
        store=new(root); await store.LoadAsync();
        check(store.Data.Version==5 && store.Data.Conversations.Count==1 && store.Data.Conversations[0].Messages[0].Content=="LEGADO-204","La migración conserva el historial anterior como una conversación independiente");
        var handler=new ConversationReplyHandler(); using var host=new AppHost(store,new OllamaService(handler)); previous.Hide();
        var main=new MainWindow(host); host.MainWindow=main; main.Show();
        try {
            host.NewConversation(); await host.SendAsync("PRIVADO-AZUL: primera conversación"); var first=host.CurrentConversation;
            host.QuickChat(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); var second=host.CurrentConversation;
            check(second.Id!=first.Id && second.Messages.Count==0 && store.Data.Conversations.Contains(first),"Escribir desde la mascota inicia un chat nuevo sin perder los anteriores");
            var bar=System.Windows.Application.Current.Windows.OfType<QuickPromptWindow>().Single();
            FindButtons(bar).Single(b=>AutomationProperties.GetName(b)=="Adjuntar imágenes").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(bar.AttachmentOptions.IsOpen && bar.AttachmentOptions.Items.OfType<MenuItem>().Select(i=>(string)i.Header).SequenceEqual(new[] { "Subir imágenes","Qué veo en pantalla?" }),"El botón más muestra Subir imágenes y Qué veo en pantalla");
            SaveElement(bar.AttachmentOptions,Path.Combine(output,"attachment-options.png")); bar.AttachmentOptions.IsOpen=false;
            bar.Input.Text="Qué veo en pantalla?"; await bar.SubmitAsync();
            check(second.Scope=="general" && !handler.Last.GetProperty("messages").EnumerateArray().Any(m=>m.TryGetProperty("images",out _)),"Escribir una pregunta sobre la pantalla ya no toma capturas automáticamente");
            var quick=System.Windows.Application.Current.Windows.OfType<QuickChatWindow>().Single();
            host.OpenConversation(first);
            var field=FindElements<TextBox>(quick).Single(t=>AutomationProperties.GetName(t)=="Tu mensaje"); field.Text="Seguimiento de SEGUNDA-510";
            FindButtons(quick).Single(b=>AutomationProperties.GetName(b)=="Enviar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await UntilAsync(()=>second.Messages.Count==4 && !host.IsBusy);
            check(quick.Conversation==second && !handler.Last.GetRawText().Contains("PRIVADO-AZUL") && handler.Last.GetRawText().Contains("SEGUNDA-510"),"Las preguntas del mismo chat continúan en su sesión aunque se abra otra desde Historial");
            main.Navigate("history"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(AutomationProperties.GetName(main.HistoryNav)=="Historial" && main.PageHost.Content is HistoryView && FindButtons(main.PageHost).Any(b=>AutomationProperties.GetName(b)=="Continuar"),"Historial aparece debajo de Conversación y permite continuar los chats guardados");
            SaveVisual(main,Path.Combine(output,"conversation-history.png"));
            quick.Close();

            var memory=new MemoryItem { Title="Guía de plantas",Summary="La planta A usa riego semanal.",Ideas=["Regar cada siete días"],SourceText="El protocolo CODE-728 establece riego semanal para la planta A.",Kind="web-text" };
            var other=new MemoryItem { Title="Otra memoria",Summary="SECRETO-OTRO-991",Ideas=["Dato ajeno"] };
            await store.AddAsync(memory,MakeFixture()); await store.AddAsync(other,MakeFixture());
            main.Navigate("saved"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(FindButtons(main.PageHost).Count(b=>AutomationProperties.GetName(b)=="Preguntar al recuerdo")>=2,"Cada tarjeta que no es PDF ofrece Preguntar al recuerdo directamente");
            await host.OpenMemoryChatAsync(memory); var memoryWindow=System.Windows.Application.Current.Windows.OfType<MemoryChatWindow>().Single(); var discussion=memoryWindow.Conversation;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); SaveVisual(memoryWindow,Path.Combine(output,"memory-conversation.png"));
            await host.SendAsync("¿Qué indica el protocolo?",conversation:discussion);
            check(handler.Last.GetRawText().Contains("CODE-728") && !handler.Last.GetRawText().Contains("SECRETO-OTRO-991") && !handler.Last.GetRawText().Contains("PRIVADO-AZUL"),"El modelo recibe solo el recuerdo elegido y su conversación, sin otros recuerdos ni chats");
            handler.Supported=false; await host.SendAsync("Dime el resultado de un partido ajeno",conversation:discussion);
            check(discussion.Messages.Last().Content.StartsWith("Este recuerdo no contiene") && !discussion.Messages.Last().Content.Contains("respuesta inventada"),"Una respuesta marcada sin evidencia se reemplaza por una explicación limitada al recuerdo");
            handler.Supported=true;
            var prepared=ChatImageService.Prepare(Images.Decode(MakeFixture()),"Pantalla.png"); prepared=prepared with { Image=prepared.Image with { IsScreenCapture=true } };
            host.NewConversation(); var screen=host.CurrentConversation;
            handler.Gate=new(TaskCreationOptions.RunContinuationsAsynchronously); var generating=host.SendAsync("Describe la captura",[prepared]);
            await UntilAsync(()=>handler.Last.TryGetProperty("format",out _) && host.IsBusy && screen.Messages.Count==1);
            check(host.IsChatBusy(screen,null) && !host.IsChatBusy(first,null) && !host.IsChatBusy(discussion,null),"Pensando y Detener pertenecen solo al chat que está generando, sin cancelar otro desde una ventana distinta");
            handler.Gate.SetResult(); await generating; handler.Gate=null;
            check(screen.Scope=="screen" && handler.Last.GetProperty("messages").EnumerateArray().Count(m=>m.TryGetProperty("images",out _))==1 && !handler.Last.GetRawText().Contains("CODE-728"),"La consulta de pantalla se restringe a su imagen y no mezcla el contexto de una memoria");
            handler.Supported=false; await host.SendAsync("Cuéntame una biografía que no sale en la imagen",conversation:screen);
            check(screen.Messages.Last().Content.StartsWith("No puedo determinarlo") && !screen.Messages.Last().Content.Contains("respuesta inventada"),"Las preguntas no sustentadas por la imagen reciben una respuesta de falta de información");
            screen.Messages.RemoveAt(0); screen.Messages.RemoveAt(0); await host.SendAsync("¿Sigue disponible la fuente?",conversation:screen);
            check(handler.Last.GetProperty("messages").EnumerateArray().Any(m=>m.TryGetProperty("images",out _)) && File.Exists(store.ChatImagePath(prepared.Image.Id)),"La captura de referencia permanece disponible aunque su primer turno salga del historial visible");
            var reloaded=new LibraryStore(root); await reloaded.LoadAsync();
            check(reloaded.Data.Conversations.Count==5 && reloaded.Data.Conversations.Single(c=>c.Id==second.Id).Messages.Count==4 && reloaded.Data.Conversations.Single(c=>c.Id==screen.Id).ScreenImages[0].IsScreenCapture && reloaded.Data.Conversations.Single(c=>c.Id==discussion.Id).MemoryId==memory.Id,"Reabrir conserva sesiones, seguimientos, referencias de memoria y alcance de pantalla");
            main.Navigate("history"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); SaveVisual(main,Path.Combine(output,"conversation-history-complete.png"));
            await store.DeleteAsync(memory); host.Refresh(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(!FindButtons(memoryWindow).Single(b=>AutomationProperties.GetName(b)=="Enviar").IsEnabled && memoryWindow.IsVisible,"Eliminar la memoria desactiva nuevas preguntas y conserva abierta la conversación");
            await CheckHistoryDeletionAsync(host,main,handler,first,second,screen,other,output,check);
            memoryWindow.Close();
        }
        finally { host.CloseWindows(); previous.Show(); previous.Activate(); }
    }
    private static async Task CheckHistoryDeletionAsync(AppHost host,MainWindow main,ConversationReplyHandler handler,ChatConversation first,ChatConversation second,ChatConversation screen,MemoryItem memory,string output,Action<bool,string> check)
    {
        var navs=new[] { main.ChatNav,main.HistoryNav,main.HomeNav,main.SavedNav,main.BrainNav,main.CaptureNav,main.ShowPetNav,main.SettingsNav };
        check(navs.All(b=>b.Content is StackPanel panel && panel.Children.OfType<System.Windows.Shapes.Path>().Count()==1 && AutomationProperties.GetName(b).Length>0),"Las ocho acciones del menú lateral tienen iconos vectoriales y conservan sus nombres accesibles");
        main.Navigate("history"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var icon=((StackPanel)main.HistoryNav.Content).Children.OfType<System.Windows.Shapes.Path>().Single();
        check(icon.Stroke==main.HistoryNav.Foreground,"El icono de la sección seleccionada usa el mismo color activo que su texto");
        var shared=screen.ScreenImages[0]; first.Messages[0].Images.Add(shared);
        var extra=ChatImageService.Prepare(Images.Decode(MakeFixture()),"Adjunto solo de esta conversación.png");
        await host.Store.SaveChatImagesAsync([extra],CancellationToken.None); screen.Messages.Last().Images.Add(extra.Image); await host.Store.SaveAsync();
        host.ShowQuickConversation(host.PetWindow!,screen); var quick=System.Windows.Application.Current.Windows.OfType<QuickChatWindow>().Single();
        var confirmed=false; var attempts=0;
        var view=new HistoryView(host,_=> { attempts++; return confirmed; }); main.PageHost.Content=view; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var search=FindElements<TextBox>(view).Single(t=>AutomationProperties.GetName(t)=="Buscar conversaciones"); search.Text=screen.Title;
        Button Trash(ChatConversation conversation)=>FindButtons(view).Single(b=>AutomationProperties.GetAutomationId(b)=="DeleteConversation-"+conversation.Id);
        var count=host.Store.Data.Conversations.Count;
        Trash(screen).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(attempts==1 && host.Store.Data.Conversations.Count==count && File.Exists(host.Store.ChatImagePath(extra.Image.Id)),"Cancelar la papelera del Historial conserva la conversación y sus archivos");
        handler.Gate=new(TaskCreationOptions.RunContinuationsAsynchronously); var pending=host.SendAsync("Mensaje en preparación",conversation:second);
        await UntilAsync(()=>host.IsBusy && second.Messages.Count==5);
        check(!Trash(screen).IsEnabled,"El borrado espera mientras la IA genera para evitar guardar o eliminar mensajes a medio preparar");
        handler.Gate.SetResult(); await pending; handler.Gate=null;
        SaveVisual(main,Path.Combine(output,"sidebar-and-history-delete.png"));
        confirmed=true; Trash(screen).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await UntilAsync(()=>screen.IsDeleted);
        check(main.IsVisible && main.CurrentPage=="history" && search.Text==screen.Title && !host.Store.Data.Conversations.Contains(screen) && host.CurrentConversation!=screen,"Confirmar elimina únicamente esa conversación y mantiene Historial abierto con su búsqueda");
        check(!File.Exists(host.Store.ChatImagePath(extra.Image.Id)) && File.Exists(host.Store.ChatImagePath(shared.Id)) && File.Exists(host.Store.CapturePath(memory.Id)),"El borrado limpia adjuntos exclusivos y conserva imágenes compartidas y recuerdos de Mis guardados");
        check(quick.IsVisible && !FindButtons(quick).Single(b=>AutomationProperties.GetName(b)=="Enviar").IsEnabled,"La conversación eliminada permanece visible y no permite enviar mensajes que la restauren");
        try { await host.SendAsync("No resucitar",conversation:screen); check(false,"Chat eliminado"); } catch(InvalidOperationException) { check(!host.Store.Data.Conversations.Contains(screen),"Un chat abierto eliminado no puede reaparecer en Historial al enviar"); }
        var reload=new LibraryStore(host.Store.Root); await reload.LoadAsync();
        check(reload.Data.Conversations.Count==count-1 && reload.Data.Conversations.All(c=>c.Id!=screen.Id) && reload.Data.Conversations.Any(c=>c.Id==first.Id),"La eliminación persiste al reabrir sin reimportar mensajes de compatibilidad");
        search.Text=first.Title;
        var blocked=Path.Combine(host.Store.Root,"library.json.tmp"); Directory.CreateDirectory(blocked);
        try {
            Trash(first).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilAsync(()=>FindElements<TextBlock>(view).Any(t=>t.Text.StartsWith("No se pudo eliminar la conversación")));
            check(host.Store.Data.Conversations.Contains(first) && !first.IsDeleted && File.Exists(host.Store.ChatImagePath(shared.Id)),"Un fallo de guardado conserva conversación y archivos y muestra un error sin cerrar Historial");
        }
        finally { Directory.Delete(blocked); }
        Trash(first).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await UntilAsync(()=>first.IsDeleted);
        check(!File.Exists(host.Store.ChatImagePath(shared.Id)) && host.Store.Data.Conversations.Contains(second),"La imagen compartida se elimina solo al borrar su última conversación y los demás chats se conservan");
        quick.Close();
    }
    private sealed class ConversationReplyHandler:HttpMessageHandler
    {
        internal JsonElement Last { get; private set; }
        internal bool Supported=true;
        internal TaskCompletionSource? Gate;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            if(request.Method==HttpMethod.Get) return new(HttpStatusCode.OK) { Content=new StringContent("{\"models\":[{\"name\":\"gemma4:12b\",\"capabilities\":[\"completion\",\"vision\"]}]}") };
            using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)); Last=json.RootElement.Clone();
            if(Gate is not null) await Gate.Task.WaitAsync(cancellationToken);
            var text=Last.TryGetProperty("format",out _)?JsonSerializer.Serialize(new { supported=Supported,answer=Supported?"El dato está en la fuente aportada.":"respuesta inventada que no se debe mostrar" }):"Respuesta breve de prueba.";
            return new(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(new { message=new { content=text } })) };
        }
    }
}
