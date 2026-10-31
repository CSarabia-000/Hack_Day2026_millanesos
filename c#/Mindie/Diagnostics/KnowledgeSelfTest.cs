using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Mindie.Services;
using Mindie.Windows;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;

namespace Mindie.Diagnostics;

public static partial class SelfTest
{
    private static byte[] PdfFixture(byte[]? scan = null)
    {
        using var builder=new PdfDocumentBuilder();
        var font=builder.AddStandard14Font(Standard14Font.Helvetica);
        if (scan is not null) builder.AddPage(900,400).AddPng(scan,new PdfRectangle(0,0,900,400));
        else
        {
            var lines=new[] {
                new[] { "Manual de prueba Mindie", "Este documento trata del cultivo de plantas en un invernadero.", "Las plantas necesitan luz, agua y un calendario de cuidados." },
                new[] { "Calendario del cultivo", "Las semillas se plantan en marzo y se revisan cada semana.", "La cosecha esta prevista para septiembre con riego moderado." },
                new[] { "Datos finales del documento", "El codigo de acceso del invernadero es QUETZAL-731.", "El presupuesto total es de 4200 pesos. Responsable: Elena." }
            };
            foreach (var pageLines in lines)
            {
                var page=builder.AddPage(PageSize.A4); var y=750;
                foreach (var line in pageLines) { page.AddText(line,15,new PdfPoint(35,y),font); y-=35; }
            }
        }
        return builder.Build();
    }
    private static async Task CheckKnowledgeAsync(AppHost host,MainWindow window,string output,bool withOllama,Action<bool,string> check)
    {
        var png=MakeFixture();
        var longContext=string.Concat(Enumerable.Repeat("Quiero conservar una explicación extensa de esta selección. 🌱\n",2000))+"FINAL-CONTEXTO-8197";
        check(string.Concat(KnowledgeText.Split(longContext))==longContext,"El contexto extenso se divide sin perder caracteres, saltos ni emoji");
        check(KnowledgeText.Split(longContext).All(t=>t.Length<=6000),"Los fragmentos respetan el presupuesto del modelo");
        var handler=new KnowledgeHandler(); using var controlled=new OllamaService(handler);
        await controlled.AnalyzeAsync(host.Settings,png,CancellationToken.None,longContext);
        var reducedParts=handler.Requests.Where(r=>!r.TryGetProperty("format",out _)).Select(r=>r.GetProperty("messages")[1].GetProperty("content").GetString()).ToArray();
        check(string.Concat(reducedParts)==longContext,"Todo el contexto, incluido el final, llega al procesamiento por partes");
        check(handler.Requests.Last().GetProperty("messages")[1].GetProperty("content").GetString()!.Contains("FINAL-CONTEXTO-8197"),"El análisis de la imagen recibe también el contexto preparado");
        var contextWindow=new CaptureContextWindow(png) { Owner=window }; contextWindow.Show();
        contextWindow.ContextInput.Text="Esta captura contiene una guía que quiero adaptar a mi proyecto de estudio.";
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); SaveVisual(contextWindow,Path.Combine(output,"give-context.png"));
        check(ButtonsFit(contextWindow),"Los botones del paso Dar contexto caben dentro de la ventana");
        var analyzeButton=FindButtons(contextWindow).Single(b=>b.Content is string text && text=="Analizar selección");
        check(HasVisibleTooltipText(SaveElement(analyzeButton,Path.Combine(output,"context-analyze-button.png"))),"El botón Analizar selección muestra texto blanco legible sobre su fondo oscuro");
#if DEBUG
        if (Environment.GetCommandLineArgs().Contains("--preview")) await Task.Delay(20000);
#endif
        contextWindow.ContextInput.Text=longContext;
        check(contextWindow.ContextInput.MaxLength==0 && contextWindow.ContextText==longContext,"Dar contexto admite texto extenso sin límite artificial de caracteres"); contextWindow.Close();
        host.Settings.GiveContext=true;
        var contextual=new MemoryItem { Title="Recuerdo con contexto extenso",UserContext=longContext,SourceUrl="https://example.com/origen?tema=estudio#ideas" };
        await host.Store.AddAsync(contextual,png);
        var reloaded=new LibraryStore(host.Store.Root); await reloaded.LoadAsync();
        check(reloaded.Data.Settings.GiveContext && reloaded.Data.Memories.Single(m=>m.Id==contextual.Id).UserContext==longContext,"Configuración y contexto completo sobreviven a la recarga");
        check(reloaded.Data.Memories.Single(m=>m.Id==contextual.Id).SourceUrl==contextual.SourceUrl && contextual.Copy().SourceUrl==contextual.SourceUrl,"El enlace manual conserva ruta, consulta y fragmento al guardar, recargar y editar");
        check(LibraryStore.ValidateSourceUrl("example.com/articulo")=="https://example.com/articulo" && LibraryStore.ValidateSourceUrl("")=="","El enlace es opcional y admite direcciones web sin escribir el prefijo");
        foreach (var invalid in new[] { "file:///C:/Windows", "javascript:alert(1)", "https://usuario:clave@example.com/" })
        {
            try { LibraryStore.ValidateSourceUrl(invalid); check(false,"Enlace inválido aceptado"); }
            catch (ArgumentException) { }
        }
        check(true,"Solo se guardan enlaces web válidos para abrir por petición del usuario");
        host.Settings.GiveContext=false;
        var feeding=new PetWindow(host); feeding.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); feeding.FeedPdf(); var eatingFrame=feeding.Sprite.Source; await Task.Delay(360);
        check(feeding.Sprite.State==PetState.Eat && !ReferenceEquals(eatingFrame,feeding.Sprite.Source),"Dar un PDF activa y reproduce los fotogramas de comer/beber de la mascota"); feeding.Close();
        var bytes=PdfFixture(); var pdfPath=Path.Combine(output,"documento-prueba.PDF"); await File.WriteAllBytesAsync(pdfPath,bytes);
        check(PdfService.IsPdfDrop([pdfPath]) && !PdfService.IsPdfDrop([pdfPath,pdfPath]) && !PdfService.IsPdfDrop(["archivo.txt"]),"El arrastre acepta un PDF y rechaza múltiples archivos y otros formatos");
        var prepared=await new PdfService().ReadAsync(pdfPath,controlled,host.Settings,CancellationToken.None);
        check(prepared.Knowledge.Pages.Count==3 && prepared.Knowledge.Pages[2].Text.Contains("QUETZAL-731"),"Extracción real del texto y numeración de todas las páginas del PDF");
        check(prepared.Knowledge.Pages.All(p=>!p.NeedsVision),"El PDF con texto legible no necesita transcripción visual");
        check(Images.Decode(prepared.Cover).PixelWidth>500 && prepared.Original.SequenceEqual(bytes),"Windows renderiza la portada y se conserva el PDF original sin alterarlo");
        var scanPath=Path.Combine(output,"escaneado-prueba.pdf"); var scanBytes=PdfFixture(png); await File.WriteAllBytesAsync(scanPath,scanBytes);
        check(PdfService.Extract(scanBytes,"scan.pdf",CancellationToken.None).Pages.Single().NeedsVision,"Las páginas escaneadas se detectan para analizarlas con visión");
        var scanned=await new PdfService().ReadAsync(scanPath,controlled,host.Settings,CancellationToken.None);
        check(scanned.Knowledge.Pages[0].Text.Contains("Lectura visual"),"El flujo de lectura visual incorpora el contenido de la página al conocimiento");
        var invalidPath=Path.Combine(output,"invalido.pdf"); await File.WriteAllTextAsync(invalidPath,"No es un PDF");
        try { await new PdfService().ReadAsync(invalidPath,controlled,host.Settings,CancellationToken.None); check(false,"Se rechazó un PDF inválido"); }
        catch (InvalidDataException) { check(true,"Los archivos que no son PDFs se rechazan antes de la inferencia"); }
        using (var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();
            try { await new PdfService().ReadAsync(pdfPath,controlled,host.Settings,cancel.Token); check(false,"Se canceló la lectura"); }
            catch (OperationCanceledException) { check(true,"La lectura de PDF respeta la cancelación"); }
        }
        var large=new PdfKnowledge { Pages=Enumerable.Range(1,30).Select(n=>new PdfPageText { Number=n,Text=string.Concat(Enumerable.Repeat("Cultivo general y cuidados del invernadero. ",150)) }).ToList() };
        large.Pages[29].Text+=" El código especial QUETZAL731 corresponde al presupuesto de recuperación.";
        var evidence=KnowledgeText.Evidence(large,"¿Qué presupuesto corresponde al código QUETZAL731?");
        check(evidence.Contains("QUETZAL731") && evidence.Contains("[Página 30]"),"Las preguntas recuperan evidencia pertinente incluso al final de PDFs largos");
        var card=await controlled.SummarizePdfAsync(host.Settings,prepared.Knowledge,CancellationToken.None);
        await host.Store.AddPdfAsync(card,prepared);
        var knowledge=await host.Store.ReadKnowledgeAsync(card.Id); knowledge.Messages.Add(new() { Content="¿Cuál es el código?" }); knowledge.Messages.Add(new() { Role="assistant",Content="QUETZAL-731 [p. 3]" }); await host.Store.SaveKnowledgeAsync(card.Id,knowledge);
        reloaded=new LibraryStore(host.Store.Root); await reloaded.LoadAsync();
        check(reloaded.Data.Memories.Single(m=>m.Id==card.Id).IsPdf && (await reloaded.ReadKnowledgeAsync(card.Id)).Messages.Count==2 && File.ReadAllBytes(reloaded.DocumentPath(card.Id)).SequenceEqual(bytes),"La tarjeta PDF, el original, las páginas y su conversación sobreviven a la recarga");
        await controlled.AskPdfAsync(host.Settings,card,knowledge,[new ChatMessage { Content="¿Cuál es el código?" }],CancellationToken.None);
        var questionBody=handler.Requests.Last().GetProperty("messages")[1].GetProperty("content").GetString()!;
        check(questionBody.Contains("QUETZAL-731") && questionBody.Contains("Página 3"),"La consulta a Gemma recibe evidencia del PDF con números de página");
        window.Navigate("saved"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); SaveVisual(window,Path.Combine(output,"saved-with-pdf.png"));
        var viewer=new PdfKnowledgeWindow(host,card,knowledge) { Owner=window }; viewer.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); SaveVisual(viewer,Path.Combine(output,"pdf-chat.png"));
        check(ButtonsFit(viewer),"Los controles del chat PDF caben dentro de la ventana");
#if DEBUG
        if (Environment.GetCommandLineArgs().Contains("--preview")) await Task.Delay(30000);
#endif
        viewer.Close();
        var exportPath=Path.Combine(output,"knowledge-export.json"); await reloaded.ExportAsync(exportPath); using var exported=JsonDocument.Parse(await File.ReadAllTextAsync(exportPath));
        var exportedPdf=exported.RootElement.GetProperty("memories").EnumerateArray().Single(m=>m.GetProperty("id").GetString()==card.Id);
        check(Convert.FromBase64String(exportedPdf.GetProperty("pdf").GetString()!).SequenceEqual(bytes) && exportedPdf.GetProperty("knowledge").GetProperty("pages").GetArrayLength()==3,"La exportación incluye PDF original, contexto, páginas y conversación");
        check(exported.RootElement.GetProperty("memories").EnumerateArray().Single(m=>m.GetProperty("id").GetString()==contextual.Id).GetProperty("sourceUrl").GetString()==contextual.SourceUrl,"El enlace manual también se incluye en la exportación");
        await reloaded.DeleteAsync(reloaded.Data.Memories.Single(m=>m.Id==card.Id));
        check(!Directory.Exists(reloaded.DocumentDirectory(card.Id)) && !File.Exists(reloaded.CapturePath(card.Id)),"Eliminar una tarjeta PDF retira su original, conocimiento y miniatura");
        host.Store.Data.Memories.Remove(card); await host.Store.SaveAsync();
        if (withOllama)
        {
            var aiStore=new LibraryStore(Path.Combine(output,"real-pdf-"+Guid.NewGuid().ToString("N"))); await aiStore.LoadAsync(); using var aiHost=new AppHost(aiStore);
            var progress=new Progress<string>(text=>File.WriteAllText(Path.Combine(output,"knowledge-progress.txt"),text));
            var imported=await aiHost.ImportPdfAsync(pdfPath,progress);
            check(imported.Item.IsPdf && imported.Item.Title.Length>0,"Gemma real analiza un PDF y crea su tarjeta de conocimiento");
            var answer=await aiHost.AskPdfAsync(imported.Item,imported.Knowledge,[new ChatMessage { Content="¿Cuál es el código de acceso del invernadero? Cita su página." }]);
            check(answer.Content.Contains("731") && Regex.IsMatch(answer.Content,@"(?i)(p\.?|página|página:)\s*3"),"Gemma real responde sobre el PDF usando el dato y su página");
            var scan=await new PdfService().ReadAsync(scanPath,aiHost.Ollama,aiHost.Settings,CancellationToken.None,progress);
            check(scan.Knowledge.Pages[0].Text.Contains("semana",StringComparison.OrdinalIgnoreCase),"Gemma real lee una página escaneada del PDF");
            var contextualAnalysis=await aiHost.AnalyzeAsync(png,"Estos consejos son material para adaptar al proyecto de estudio Boreal.");
            check(contextualAnalysis.Title.Length>0,"Gemma real analiza una captura acompañada del contexto del usuario");
            await File.WriteAllTextAsync(Path.Combine(output,"knowledge-ollama.json"),JsonSerializer.Serialize(new { imported.Item,Answer=answer.Content,Scan=scan.Knowledge.Pages[0].Text,Contextual=contextualAnalysis }));
        }
    }
    private sealed class KnowledgeHandler : HttpMessageHandler
    {
        public List<JsonElement> Requests { get; }=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)); var root=body.RootElement.Clone(); Requests.Add(root);
            var content=root.TryGetProperty("format",out var format) && format.GetProperty("properties").TryGetProperty("remove",out _) ? "{\"remove\":[]}" : root.TryGetProperty("format",out _) ? JsonSerializer.Serialize(new { title="Guía de prueba del invernadero",summary="Documento sintético para verificar la biblioteca.",ideas=new[] { "Revisar el calendario","Conservar el código" },category="Estudio" })
                : root.GetProperty("messages")[1].GetProperty("content").GetString()!.Contains("FINAL-CONTEXTO-8197") ? "Notas finales: FINAL-CONTEXTO-8197" : "Notas de prueba: organiza tu semana y conserva los datos.";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(new { message=new { role="assistant",content } }),Encoding.UTF8,"application/json") };
        }
    }
    private static bool ButtonsFit(Window window)
    {
        var root=(FrameworkElement)window.Content;
        return FindButtons(root).All(button=> {
            var point=button.TranslatePoint(new(),root);
            return point.X>=-.5 && point.Y>=-.5 && point.X+button.ActualWidth<=root.ActualWidth+.5 && point.Y+button.ActualHeight<=root.ActualHeight+.5;
        });
    }
    private static IEnumerable<Button> FindButtons(DependencyObject? root)
    {
        if(root is null) yield break;
        if (root is Button button) yield return button;
        for (var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) foreach (var child in FindButtons(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
}
