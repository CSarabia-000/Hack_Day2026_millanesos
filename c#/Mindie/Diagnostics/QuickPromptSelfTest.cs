using Mindie.Services;
using Mindie.Controls;
using Mindie.Windows;
using System.Net;
using System.Net.Http;
using System.Windows.Automation;

namespace Mindie.Diagnostics;

public static partial class SelfTest
{
    private static async Task CheckQuickPromptAsync(MainWindow main,string output,Action<bool,string> check)
    {
        foreach(var scale in new[] { 1.0,1.5,2.0 }) foreach(var point in new[] { new Point(0,0),new Point(1700,0),new Point(0,900),new Point(1700,900) }) {
            var work=new Rect(0,0,1920*scale,1080*scale); var pet=new Rect(point.X*scale,point.Y*scale,144*scale,144*scale);
            var input=QuickPromptWindow.Position(pet,work,new(390*scale,160*scale),8*scale,12*scale);
            check(work.Contains(input),$"La barra rápida permanece dentro del monitor al {scale*100:0}% y cerca del borde {point}");
        }
        var root=Path.Combine(output,"quick-prompt-"+Guid.NewGuid().ToString("N")); var store=new LibraryStore(root); await store.LoadAsync();
        var handler=new QuickReplyHandler(); using var host=new AppHost(store,new OllamaService(handler)) { MainWindow=main };
        main.Hide(); var first=Path.Combine(output,"quick-image-a.png"); var second=Path.Combine(output,"quick-image-b.png"); var fixture=MakeFixture();
        await File.WriteAllBytesAsync(first,fixture); await File.WriteAllBytesAsync(second,fixture);
        try {
            host.QuickChat(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var input=System.Windows.Application.Current.Windows.OfType<QuickPromptWindow>().Single();
            check(input.IsVisible && input.WindowStyle==WindowStyle.None && input.AllowsTransparency && !input.ShowInTaskbar && input.Width<=390 && !main.IsVisible && !System.Windows.Application.Current.Windows.OfType<QuickChatWindow>().Any(),"Escribir muestra solo una barra sin marco junto a la mascota y conserva oculta la biblioteca y el chat");
            var menu=host.PetWindow!.ActionMenu;
            check(FindButtons(menu).Count()==3 && !FindButtons(menu).Any(b=>AutomationProperties.GetName(b)=="Hablar") && !host.PetWindow.Sprite.ContextMenu!.Items.OfType<MenuItem>().Any(i=>(string?)i.Header=="Hablar") && FindButtons(input).Any(b=>AutomationProperties.GetName(b)=="Dictar mensaje"),"La mascota ofrece tres acciones sin Hablar y el micrófono permanece dentro de Escribir");
            var hwnd=new System.Windows.Interop.WindowInteropHelper(input).Handle; NativeMethods.GetWindowRect(hwnd,out var rect); var area=System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
            check(rect.Left>=area.Left && rect.Top>=area.Top && rect.Right<=area.Right && rect.Bottom<=area.Bottom,"La barra nativa queda dentro del área útil del monitor de la mascota");
            var clickWatcher=input.OutsideClick!;
            var inside=input.Input.PointToScreen(new(8,input.Input.ActualHeight/2)); clickWatcher.ObserveClick(new() { X=(int)inside.X,Y=(int)inside.Y });
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(clickWatcher.IsActive && input.IsVisible,"Hacer clic dentro de la barra conserva la entrada y el detector nativo está activo");
            var petInside=host.PetWindow.Sprite.PointToScreen(new(host.PetWindow.Sprite.ActualWidth*.5,host.PetWindow.Sprite.ActualHeight*.6));
            clickWatcher.ObserveClick(new() { X=(int)petInside.X,Y=(int)petInside.Y }); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(input.IsVisible,"Hacer clic sobre el cuerpo de la mascota conserva la barra abierta");
            var outside=new NativeMethods.Point { X=area.Left+8,Y=area.Top+8 };
            clickWatcher.ObserveClick(outside); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(!input.IsVisible && !clickWatcher.IsActive && !host.IsQuickPromptOpen,"Un clic fuera cierra la barra, libera el observador y no muestra un chat");
            host.PetWindow.OpenActions(); menu.Reveal(true); await Task.Delay(60); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var capsuleWatcher=host.PetWindow.OutsideClick!;
            var menuInside=menu.WriteButton.PointToScreen(new(12,12)); capsuleWatcher.ObserveClick(new() { X=(int)menuInside.X,Y=(int)menuInside.Y }); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(host.PetWindow.AreActionsOpen,"Un clic dentro de la cápsula mantiene operativos sus iconos");
            capsuleWatcher.ObserveClick(outside); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(!host.PetWindow.AreActionsOpen && !capsuleWatcher.IsActive,"Un clic fuera cierra inmediatamente la cápsula y libera su observador");
            host.QuickChat(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); input=System.Windows.Application.Current.Windows.OfType<QuickPromptWindow>().Single();
            host.QuickChat(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(ReferenceEquals(input,System.Windows.Application.Current.Windows.OfType<QuickPromptWindow>().Single()),"Volver a Escribir reutiliza la barra y conserva su borrador");
            SaveVisual(input,Path.Combine(output,"quick-prompt-empty.png"));
            await input.AddImagesAsync([first,second]); input.Input.Text="Compara estas dos imágenes.";
            check(input.Attachments.Count==2 && host.Messages.Count==0 && !handler.Started.Task.IsCompleted && FindButtons(input).Any(b=>AutomationProperties.GetName(b)=="Adjuntar imágenes"),"El botón más admite varios adjuntos con vista previa sin enviar ni abrir el chat");
            var removed=input.Attachments[0].Image.Id; input.RemoveImage(removed);
            check(input.Attachments.Count==1 && input.Attachments.All(i=>i.Image.Id!=removed),"Quitar un adjunto solo modifica el borrador");
            await input.AddImagesAsync([first]); var prepared=input.Attachments.ToArray();
            SaveVisual(input,Path.Combine(output,"quick-prompt-attachments.png"));
            input.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(input.Input)!,Environment.TickCount,Key.Enter) { RoutedEvent=UIElement.PreviewKeyDownEvent });
            await UntilAsync(()=>handler.Started.Task.IsCompleted);
            check(input.IsVisible && input.Sending && host.IsBusy && !main.IsVisible && !System.Windows.Application.Current.Windows.OfType<QuickChatWindow>().Any(),"Enter deja la barra esperando y no abre el chat antes de tener una respuesta");
            var sent=handler.Request.GetProperty("messages").EnumerateArray().Last();
            check(sent.GetProperty("images").GetArrayLength()==2 && prepared.Select(p=>Convert.ToBase64String(p.Png)).SequenceEqual(sent.GetProperty("images").EnumerateArray().Select(v=>v.GetString())) && handler.Request.GetProperty("options").GetProperty("num_ctx").GetInt32()==8192,"Gemma recibe realmente las dos imágenes preparadas en el mensaje y un contexto visual acotado");
            SaveVisual(input,Path.Combine(output,"quick-prompt-wait.png")); host.HidePet(); handler.Complete();
            await UntilAsync(()=>!input.IsVisible && System.Windows.Application.Current.Windows.OfType<QuickChatWindow>().Any(w=>w.IsVisible));
            var conversation=System.Windows.Application.Current.Windows.OfType<QuickChatWindow>().Single(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(!main.IsVisible && host.Messages.Count==2 && FindButtons(conversation).Count(b=>AutomationProperties.GetName(b).StartsWith("Ver imagen:",StringComparison.Ordinal))==2 && FindElements<RichTextBox>(conversation).Any(),"Al terminar se abre el chat existente con respuesta Markdown y las imágenes del mensaje, sin activar la biblioteca aunque se oculte la mascota durante la espera");
            SaveVisual(conversation,Path.Combine(output,"quick-prompt-conversation.png"));
            var reloaded=new LibraryStore(root); await reloaded.LoadAsync();
            check(reloaded.Data.Messages[0].Images.SequenceEqual(prepared.Select(i=>i.Image)) && prepared.All(i=>File.ReadAllBytes(store.ChatImagePath(i.Image.Id)).SequenceEqual(i.Png)) && !(await File.ReadAllTextAsync(Path.Combine(root,"library.json"))).Contains(Convert.ToBase64String(prepared[0].Png)),"El historial persiste metadatos y archivos de imagen separados sin incrustar base64 en library.json");
            check((await File.ReadAllBytesAsync(first)).SequenceEqual(fixture) && (await File.ReadAllBytesAsync(second)).SequenceEqual(fixture),"Adjuntar imágenes conserva intactos los archivos originales elegidos");
            conversation.Close(); handler.Reset(); host.QuickChat(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); input=System.Windows.Application.Current.Windows.OfType<QuickPromptWindow>().Single();
            await input.AddImagesAsync([first]); input.Input.Text="Conserva este borrador";
            var failing=input.SubmitAsync(); await UntilAsync(()=>handler.Started.Task.IsCompleted); handler.Complete(true); await failing;
            check(input.IsVisible && input.Input.Text=="Conserva este borrador" && input.Attachments.Count==1 && host.Messages.Count==0 && Directory.GetFiles(Path.Combine(root,"chat-images")).Length==2 && !System.Windows.Application.Current.Windows.OfType<QuickChatWindow>().Any(),"Un error de IA conserva texto y adjuntos, revierte el mensaje incompleto y no abre el chat");
            SaveVisual(input,Path.Combine(output,"quick-prompt-error.png"));
            handler.Reset(); var canceled=input.SubmitAsync(); await UntilAsync(()=>handler.Started.Task.IsCompleted);
            FindButtons(input).Single(b=>AutomationProperties.GetName(b)=="Cancelar respuesta").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await canceled;
            check(input.IsVisible && input.Attachments.Count==1 && input.Input.Text=="Conserva este borrador" && !host.IsBusy && Directory.GetFiles(Path.Combine(root,"chat-images")).Length==2,"Cancelar desde la barra mantiene el borrador y limpia únicamente sus imágenes sin confirmar");
            var before=input.Attachments.Count; await input.AddImagesAsync(Enumerable.Repeat(first,9).ToArray());
            var invalid=Path.Combine(output,"quick-image-invalid.png"); await File.WriteAllTextAsync(invalid,"Este archivo no es una imagen."); await input.AddImagesAsync([invalid]);
            check(input.Attachments.Count==before && input.IsVisible,"Demasiados adjuntos y archivos inválidos muestran un error sin perder los adjuntos válidos");
            handler.Reset(); var closing=input.SubmitAsync(); await UntilAsync(()=>handler.Started.Task.IsCompleted); input.OutsideClick!.ObserveClick(outside); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await closing;
            check(!input.IsVisible && !host.IsBusy && host.Messages.Count==0 && !System.Windows.Application.Current.Windows.OfType<QuickChatWindow>().Any(),"Hacer clic fuera durante la espera cierra la barra, cancela su solicitud y evita un chat tardío");

            handler.Reset(); var opened=0;
            var voice=new QuickPromptWindow(host,host.PetWindow!,_=>Task.FromResult("Este mensaje fue dictado."),()=>opened++); voice.Show();
            FindButtons(voice).Single(b=>AutomationProperties.GetName(b)=="Dictar mensaje").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilAsync(()=>voice.Input.Text=="Este mensaje fue dictado.");
            check(voice.IsVisible && !handler.Started.Task.IsCompleted && opened==0 && !voice.Input.IsReadOnly,"El micrófono dentro de Escribir dicta primero en la barra y deja revisar el texto sin enviarlo automáticamente");
            var voiceSending=voice.SubmitAsync(); await UntilAsync(()=>handler.Started.Task.IsCompleted); handler.Complete(); await voiceSending;
            check(opened==1 && !voice.IsVisible,"El texto dictado abre la conversación únicamente después de enviar y recibir la respuesta");
            var unavailable=new QuickPromptWindow(host,host.PetWindow!,_=>Task.FromException<string>(new InvalidOperationException("Micrófono no disponible para la prueba.")),()=>opened++); unavailable.Show();
            FindButtons(unavailable).Single(b=>AutomationProperties.GetName(b)=="Dictar mensaje").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilAsync(()=>FindElements<TextBlock>(unavailable).Any(t=>t.Text.Contains("Micrófono no disponible")));
            check(!unavailable.Input.IsReadOnly && opened==1,"Si el dictado no está disponible, el usuario puede escribir en la misma barra sin abrir otro chat"); unavailable.Close();

            var wide=new byte[2400*600*4]; var source=BitmapSource.Create(2400,600,96,96,PixelFormats.Bgra32,null,wide,2400*4); var large=Path.Combine(output,"quick-image-wide.png"); await File.WriteAllBytesAsync(large,Images.Encode(source));
            var scaled=(await ChatImageService.PrepareAsync([large])).Single();
            check(scaled.Image.Width==1600 && scaled.Image.Height==400,"Las imágenes grandes se reducen conservando su proporción antes de enviarlas a Gemma");
            var jpeg=Path.Combine(output,"quick-image-oriented.jpg"); var metadata=new BitmapMetadata("jpg"); metadata.SetQuery("/app1/ifd/{ushort=274}",(ushort)6);
            var encoder=new JpegBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source,null,metadata,null)); await using(var stream=File.Create(jpeg)) encoder.Save(stream);
            var rotated=(await ChatImageService.PrepareAsync([jpeg])).Single();
            check(rotated.Image.Width==400 && rotated.Image.Height==1600,"Los adjuntos JPEG respetan la orientación EXIF de las fotos verticales");
            try { store.ChatImagePath("../../fuera"); check(false,"Validar imagen de chat"); } catch(ArgumentException) { check(true,"Las rutas de adjuntos solo aceptan identificadores y no pueden salir de la carpeta privada"); }
        }
        finally {
            foreach(var input in System.Windows.Application.Current.Windows.OfType<QuickPromptWindow>().ToArray()) input.Close();
            foreach(var chat in System.Windows.Application.Current.Windows.OfType<QuickChatWindow>().ToArray()) chat.Close();
            host.PetWindow?.Close(); main.Show(); main.Activate();
        }
    }
    private sealed class QuickReplyHandler:HttpMessageHandler
    {
        private TaskCompletionSource<bool> _reply=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Started { get; private set; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal JsonElement Request { get; private set; }
        internal void Reset() { _reply=new(TaskCreationOptions.RunContinuationsAsynchronously); Started=new(TaskCreationOptions.RunContinuationsAsynchronously); }
        internal void Complete(bool error=false)=>_reply.TrySetResult(error);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)); Request=json.RootElement.Clone(); Started.TrySetResult(true);
            var error=await _reply.Task.WaitAsync(cancellationToken);
            var answer=Request.TryGetProperty("format",out _)?JsonSerializer.Serialize(new { supported=true,answer="La captura muestra el contenido de prueba." }):"Respuesta **lista** para las imágenes.";
            return new(error?HttpStatusCode.InternalServerError:HttpStatusCode.OK) { Content=new StringContent(error?"{\"error\":\"Error de prueba. Puedes reintentar.\"}":JsonSerializer.Serialize(new { message=new { content=answer } })) };
        }
    }
}
