using System.Net;
using System.Net.Http;
using System.Windows.Automation;
using Mindie.Controls;
using Mindie.Services;
using Mindie.Windows;

namespace Mindie.Diagnostics;

public static partial class SelfTest
{
    private static async Task CheckAnalysisWaitAsync(MainWindow owner,string output,Action<bool,string> check)
    {
        var store=new LibraryStore(Path.Combine(output,"analysis-wait-"+Guid.NewGuid().ToString("N"))); await store.LoadAsync();
        var handler=new WaitingAnalysisHandler(); using var host=new AppHost(store,new OllamaService(handler));
        var editor=new MemoryEditorWindow(host,MakeFixture()) { Owner=owner }; editor.Show();
        await UntilAsync(()=>host.IsBusy && FindElements<ThinkingOrb>(editor).Any(o=>o.IsActive && o.IsVisible));
        var orb=FindElements<ThinkingOrb>(editor).Single();
        Button Save()=>FindButtons(editor).Single(b=>AutomationProperties.GetName(b)=="Guardar recuerdo");
        TextBox Title()=>FindElements<TextBox>(editor).Single(t=>AutomationProperties.GetName(t)=="Título del recuerdo");
        check(orb.IsActive && !Save().IsEnabled && Title().Text.Length==0 && FindButtons(editor).Single(b=>AutomationProperties.GetName(b)=="Cancelar análisis").IsVisible,"El editor muestra la espera durante la generación y permite cancelar antes de recibir los campos");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        SaveVisual(editor,Path.Combine(output,"analysis-wait.png")); var first=SaveOrb(orb,Path.Combine(output,"thinking-orb-first.png"));
        await Task.Delay(370); var next=SaveOrb(orb,Path.Combine(output,"thinking-orb-next.png"));
        var animated=SystemParameters.ClientAreaAnimation;
        check(animated?orb.IsAnimating && orb.MotionAngle>0 && !SamePixels(first,next):!orb.IsAnimating,"El orbe nativo cambia realmente de imagen mientras Gemma trabaja y respeta la animación de Windows");
        host.Settings.ReduceMotion=true; host.Refresh(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var still=SaveOrb(orb,Path.Combine(output,"thinking-orb-reduced.png")); await Task.Delay(160);
        check(orb.IsActive && !orb.IsAnimating && SamePixels(still,SaveOrb(orb,Path.Combine(output,"thinking-orb-reduced-next.png"))),"Reducir el movimiento conserva un indicador estático y el mensaje de análisis");
        host.Settings.ReduceMotion=false; host.Refresh(); editor.Hide(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(!orb.IsAnimating && host.IsBusy,"Ocultar la ventana detiene los relojes visuales sin interrumpir el análisis solicitado");
        editor.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(orb.IsActive && (!animated || orb.IsAnimating),"Volver a mostrar el editor reanuda el indicador de una operación todavía activa");
        editor.WindowState=WindowState.Minimized; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(!orb.IsAnimating && host.IsBusy,"Minimizar el editor detiene la animación mientras continúa la solicitud");
        editor.WindowState=WindowState.Normal; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        handler.Complete(); await UntilAsync(()=>!host.IsBusy && !orb.IsActive && Save().IsEnabled && Title().Text=="Un recuerdo de prueba");
        check(!orb.IsVisible && Title().IsVisible && Title().Text=="Un recuerdo de prueba","Al completar el análisis desaparece el orbe y aparecen los campos generados para revisión");
        SaveVisual(editor,Path.Combine(output,"analysis-ready.png"));
        void Reanalyze()=>FindButtons(editor).Single(b=>AutomationProperties.GetName(b)=="Analizar con Gemma").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        handler.Reset(); Reanalyze(); await UntilAsync(()=>host.IsBusy && orb.IsActive);
        FindButtons(editor).Single(b=>AutomationProperties.GetName(b)=="Cancelar análisis").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(()=>!host.IsBusy && !orb.IsActive && Save().IsEnabled);
        check(Title().Text=="Un recuerdo de prueba" && Title().IsVisible && !orb.IsAnimating,"Cancelar la generación recupera el formulario y conserva los datos anteriores sin dejar la animación encendida");
        handler.Reset(); Reanalyze(); await UntilAsync(()=>host.IsBusy && orb.IsActive); handler.Complete(true);
        await UntilAsync(()=>!host.IsBusy && !orb.IsActive && Save().IsEnabled);
        check(FindElements<TextBlock>(editor).Any(t=>t.IsVisible && t.Text.Contains("Fallo controlado")) && Title().IsVisible,"Un error de Gemma detiene el indicador y permite completar los campos a mano o volver a intentar");
        handler.Reset(); Reanalyze(); await UntilAsync(()=>host.IsBusy && orb.IsActive); editor.Close();
        await UntilAsync(()=>!host.IsBusy);
        check(!orb.IsAnimating && !orb.IsActive,"Cerrar el editor cancela su solicitud y libera la animación");
        var pdfPath=Path.Combine(output,"analysis-wait.pdf"); await File.WriteAllBytesAsync(pdfPath,PdfFixture());
        handler.Reset(); var pdfWindow=new PdfImportWindow(host,pdfPath) { Owner=owner }; pdfWindow.Show();
        await UntilAsync(()=>host.IsBusy && FindElements<ThinkingOrb>(pdfWindow).Any(o=>o.IsVisible && o.IsActive));
        var pdfOrb=FindElements<ThinkingOrb>(pdfWindow).Single();
        check(ButtonsFit(pdfWindow) && FindButtons(pdfWindow).Single(b=>AutomationProperties.GetName(b)=="Cancelar").IsVisible,"La importación PDF usa el mismo indicador y conserva la cancelación visible");
        SaveVisual(pdfWindow,Path.Combine(output,"pdf-analysis-wait.png")); pdfWindow.Close(); await UntilAsync(()=>!host.IsBusy);
        check(!pdfOrb.IsActive && !pdfOrb.IsAnimating && store.Data.Memories.Count==0,"Cancelar la importación PDF detiene la animación sin guardar una tarjeta incompleta");
    }
    private static RenderTargetBitmap SaveOrb(FrameworkElement orb,string path)
    {
        var drawing=new DrawingVisual();
        using(var context=drawing.RenderOpen()) {
            context.DrawRectangle(Brushes.White,null,new Rect(0,0,orb.ActualWidth,orb.ActualHeight));
            context.DrawRectangle(new VisualBrush(orb),null,new Rect(0,0,orb.ActualWidth,orb.ActualHeight));
        }
        var image=new RenderTargetBitmap((int)orb.ActualWidth,(int)orb.ActualHeight,96,96,PixelFormats.Pbgra32); image.Render(drawing);
        File.WriteAllBytes(path,Images.Encode(image)); return image;
    }
    private static bool SamePixels(BitmapSource left,BitmapSource right)
    {
        if (left.PixelWidth!=right.PixelWidth || left.PixelHeight!=right.PixelHeight) return false;
        var stride=left.PixelWidth*4; var a=new byte[stride*left.PixelHeight]; var b=new byte[a.Length];
        left.CopyPixels(a,stride,0); right.CopyPixels(b,stride,0); return a.SequenceEqual(b);
    }
    private sealed class WaitingAnalysisHandler:HttpMessageHandler
    {
        private TaskCompletionSource<bool> _response=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Reset()=>_response=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Complete(bool error=false)=>_response.TrySetResult(error);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/tags",StringComparison.Ordinal)) return Json(new { models=new[] { new { name="gemma4:12b",capabilities=new[] { "vision" } } } });
            var error=await _response.Task.WaitAsync(cancellationToken);
            if (error) return Json(new { error="Fallo controlado de la IA." },HttpStatusCode.InternalServerError);
            var content=JsonSerializer.Serialize(new { title="Un recuerdo de prueba",summary="Un resumen generado para revisar.",ideas=new[] { "Primera idea","Segunda idea" },category="Estudio" });
            return Json(new { message=new { role="assistant",content } });
        }
        private static HttpResponseMessage Json(object body,HttpStatusCode status=HttpStatusCode.OK)=>new(status) { Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json") };
    }
}
