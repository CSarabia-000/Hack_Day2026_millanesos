using Mindie.Controls;
using Mindie.Services;
using Mindie.Windows;
using Mindie.Views;
using System.Net;
using System.Net.Http;
using System.Windows.Automation;

namespace Mindie.Diagnostics;

public static partial class SelfTest
{
    private static async Task CheckScreenModelsAsync(MainWindow main,string output,Action<bool,string> check)
    {
        var root=Path.Combine(output,"screen-model-"+Guid.NewGuid().ToString("N")); var store=new LibraryStore(root); await store.LoadAsync();
        var reply=new QuickReplyHandler(); using var host=new AppHost(store,new OllamaService(reply)) { MainWindow=main }; main.Hide(); host.ShowPet();
        try {
            var captureCount=0; var opened=0; var fixture=MakeFixture(); var prepared=ChatImageService.Prepare(Images.Decode(fixture),"Pantalla de prueba.png");
            var bar=new QuickPromptWindow(host,host.PetWindow!,_=>Task.FromResult(""),()=>opened++,_=> { captureCount++; return Task.FromResult(prepared); }); bar.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            bar.Input.Text="¿Qué ves en pantalla?";
            check(captureCount==0,"Escribir una pregunta no captura antes de enviarla");
            var send=bar.SubmitAsync(true); await UntilAsync(()=>reply.Started.Task.IsCompleted);
            var sent=reply.Request.GetProperty("messages").EnumerateArray().Single(m=>m.TryGetProperty("images",out _));
            check(captureCount==1 && opened==0 && sent.GetProperty("images").GetArrayLength()==1 && sent.GetProperty("images")[0].GetString()==Convert.ToBase64String(prepared.Png),"Enviar la pregunta adjunta realmente la captura al modelo antes de abrir la conversación");
            reply.Complete(); await send;
            check(opened==1 && store.Data.Messages[0].Images.Single().Id==prepared.Image.Id,"La respuesta de pantalla y su imagen quedan en el historial normal del chat");
            reply.Reset(); var refreshCount=0;
            var retry=new QuickPromptWindow(host,host.PetWindow!,_=>Task.FromResult(""),()=>opened++,_=> { refreshCount++; return Task.FromResult(ChatImageService.Prepare(Images.Decode(fixture),"Captura nueva.png")); }); retry.Show(); retry.Input.Text="Qué es esto?";
            var failing=retry.SubmitAsync(true); await UntilAsync(()=>reply.Started.Task.IsCompleted); reply.Complete(true); await failing;
            reply.Reset(); var retrySend=retry.SubmitAsync(); await UntilAsync(()=>reply.Started.Task.IsCompleted);
            check(refreshCount==2 && retry.Attachments.Count==1 && reply.Request.GetProperty("messages").EnumerateArray().Single(m=>m.TryGetProperty("images",out _)).GetProperty("images").GetArrayLength()==1,"Reintentar toma una pantalla nueva y reemplaza la captura anterior sin duplicarla");
            reply.Complete(); await retrySend;
            reply.Reset(); var captureStarted=new TaskCompletionSource<bool>();
            var pending=new QuickPromptWindow(host,host.PetWindow!,_=>Task.FromResult(""),()=>opened++,async token=> { captureStarted.SetResult(true); await Task.Delay(5000,token); return prepared; }); pending.Show(); pending.Input.Text="Qué veo?";
            var canceled=pending.SubmitAsync(true); await captureStarted.Task; pending.Close(); await canceled;
            check(opened==2 && !reply.Started.Task.IsCompleted,"Cerrar mientras se prepara la captura cancela el envío y no abre un chat tardío");

            var hidden=false; var area=System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(host.PetWindow!).Handle).Bounds;
            var captured=await host.Capture.ScreenAsync(host.PetWindow!,CancellationToken.None,rectangle=> {
                hidden=main.Dispatcher.Invoke(()=>System.Windows.Application.Current.Windows.Cast<Window>().All(w=>!w.IsVisible));
                using var image=new System.Drawing.Bitmap(rectangle.Width,rectangle.Height); using(var graphics=System.Drawing.Graphics.FromImage(image)) graphics.Clear(System.Drawing.Color.Magenta);
                using var stream=new MemoryStream(); image.Save(stream,System.Drawing.Imaging.ImageFormat.Png); return stream.ToArray();
            });
            check(hidden && host.PetWindow!.IsVisible && !main.IsVisible && captured.Image.Width<=1600 && captured.Image.Height<=1600 && Math.Abs(captured.Image.Width/(double)captured.Image.Height-area.Width/(double)area.Height)<.01,"La captura completa usa el monitor del compañero, oculta las ventanas de Mindie y recupera su estado sin leer una pantalla personal en la prueba");
            using var cancelCapture=new CancellationTokenSource(); var restoring=host.Capture.ScreenAsync(host.PetWindow!,cancelCapture.Token,_=>fixture); cancelCapture.Cancel();
            try { await restoring; check(false,"Cancelar captura"); } catch(OperationCanceledException) { check(host.PetWindow!.IsVisible && !main.IsVisible,"Cancelar la captura restaura el compañero sin abrir la biblioteca"); }

            var modelHandler=new TextHttpHandler(request=> {
                if(request.Method==HttpMethod.Get) return new(HttpStatusCode.OK) { Content=new StringContent("{\"models\":[{\"name\":\"gemma4:12b\"},{\"name\":\"solo-texto:7b\"},{\"name\":\"vectors:latest\"},{\"name\":\"remoto:cloud\"}]}") };
                var body=JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()); var name=body.RootElement.GetProperty("model").GetString();
                return new(HttpStatusCode.OK) { Content=new StringContent(name=="gemma4:12b"?"{\"capabilities\":[\"completion\",\"vision\",\"audio\",\"tools\",\"thinking\"]}":name=="vectors:latest"?"{\"capabilities\":[\"embedding\"]}":"{\"capabilities\":[\"completion\"]}") };
            });
            using var models=new OllamaService(modelHandler); var catalog=await models.ListLocalModelsAsync(host.Settings);
            var gemma=catalog.Single(m=>m.Name=="gemma4:12b");
            check(catalog.Count==3 && gemma.CapabilityText.Contains("Imágenes") && gemma.CapabilityText.Contains("Audio") && gemma.CapabilityText.Contains("Razonamiento") && !catalog.Single(m=>m.Name=="vectors:latest").CanChat,"La lista usa tags/show, muestra las capacidades reales y excluye referencias cloud sin habilitar embeddings para conversación");
            var view=new SettingsView(host,(_,_)=>Task.FromResult(catalog)); main.PageHost.Content=view; main.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var picker=FindElements<LocalModelPicker>(view).Single(); await picker.Loading;
            check(!picker.Selector.IsEditable && picker.Selector.Items.Count==3 && !FindElements<TextBox>(view).Any(b=>AutomationProperties.GetName(b)=="Modelo de Ollama"),"Nombre del modelo es un selector de modelos instalados sin entrada de texto libre");
            picker.Selector.SelectedItem=catalog.Single(m=>m.Name=="solo-texto:7b"); await view.LastSave;
            var reload=new LibraryStore(root); await reload.LoadAsync(); check(reload.Data.Settings.Model=="solo-texto:7b","Elegir un modelo se guarda automáticamente en disco");
            var scroll=(ScrollViewer)view.Content; scroll.ScrollToVerticalOffset(1050); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); SaveVisual(main,Path.Combine(output,"settings-local-models.png"));
            picker.Selector.SelectedItem=catalog.Single(m=>m.Name=="gemma4:12b"); await view.LastSave;
            picker.Selector.IsDropDownOpen=true; await Task.Delay(150); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var popup=(System.Windows.Controls.Primitives.Popup)picker.Selector.Template.FindName("PART_Popup",picker.Selector);
            check(popup.IsOpen && popup.Child is FrameworkElement,"El selector abre una lista nativa con nombres y capacidades legibles");
            SaveElement((FrameworkElement)popup.Child,Path.Combine(output,"local-model-options.png")); picker.Selector.IsDropDownOpen=false;
            picker.Selector.SelectedItem=catalog.Single(m=>m.Name=="solo-texto:7b"); await view.LastSave;
            using var textHost=new AppHost(store,new OllamaService(new TextHttpHandler(_=>new(HttpStatusCode.OK) { Content=new StringContent("{\"models\":[{\"name\":\"solo-texto:7b\",\"capabilities\":[\"completion\"]}]}") })));
            try { await textHost.CaptureScreenForChatAsync(host.PetWindow!,CancellationToken.None); check(false,"Visión obligatoria"); } catch(InvalidOperationException ex) { check(ex.Message.Contains("no admite imágenes"),"Un modelo sin visión da una explicación antes de capturar la pantalla"); }
            await picker.RefreshAsync(); check(picker.Selector.SelectedItem is LocalModelInfo { Name:"solo-texto:7b" },"Actualizar modelos conserva el modelo elegido");
        }
        finally {
            foreach(var input in System.Windows.Application.Current.Windows.OfType<QuickPromptWindow>().ToArray()) input.Close();
            host.PetWindow?.Close(); main.Show(); main.Activate();
        }
    }
}
