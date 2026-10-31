using System.Net;
using System.Net.Http;
using System.Windows.Automation;
using Mindie.Controls;
using Mindie.Services;
using Mindie.Windows;

namespace Mindie.Diagnostics;

public static partial class SelfTest
{
    private static async Task CheckWebTextCleanupAsync(MainWindow owner,string output,Action<bool,string> check)
    {
        var paragraphs=new[] {
            "La escritura y la diversidad lingüística",
            "El estudio analizó 880.000 textos. En el 87% de los casos la similitud semántica superó el 95%. Los autores explican que se conserva el sentido, pero se reducen ciertas diferencias de estilo.",
            "PUBLICIDAD",
            "Huellas que permanecen",
            "La publicidad es objeto de otro ejemplo dentro del estudio, no una orden. Un hallazgo contrario muestra asociaciones que sí se conservan, junto con sus excepciones y límites.",
            "La conclusión requiere más investigaciones: el código final del artículo es ROBLE-628.",
            "Noticias",
            "Cómo conectar un pendrive a tu móvil Android",
            "Jeff Bezos anuncia un nuevo fondo."
        };
        var expected=string.Join("\n\n",paragraphs.Where((_,i)=>i is not 2 and not 6 and not 7 and not 8));
        var page=new WebPageText(string.Join("\n\n",paragraphs),"https://example.com/articulo",paragraphs[0]);
        var model=new CleanupHandler(); using var ollama=new OllamaService(model);
        var result=await ollama.AnalyzeWebTextAsync(new Settings(),page,"Quiero conservar los hallazgos y sus límites.",CancellationToken.None);
        check(result.SourceText==expected && result.OriginalSourceText==page.Text && result.WebTextCleanupVersion==OllamaService.WebTextCleanupVersion,"Gemma selecciona fragmentos ajenos y el filtro conserva literalmente los párrafos restantes y su original");
        check(result.SourceText.Contains("880.000") && result.SourceText.Contains("87%") && result.SourceText.Contains("95%") && result.SourceText.Contains("hallazgo contrario") && result.SourceText.Contains("ROBLE-628"),"La limpieza conserva cifras, subtítulos, excepciones, opiniones opuestas y la conclusión");
        var summary=model.Requests.Last().GetProperty("messages")[1].GetProperty("content").GetString()!;
        check(!summary.Contains("PUBLICIDAD") && !summary.Contains("pendrive") && !summary.Contains("Jeff Bezos") && summary.Contains("ROBLE-628") && model.Requests.All(r=>r.GetProperty("messages").EnumerateArray().All(m=>!m.TryGetProperty("images",out _))),"El resumen recibe el texto limpio, no los anuncios ni recomendaciones, y continúa sin imágenes");
        model.Requests.Clear();
        await ollama.AnalyzeWebTextAsync(new Settings(),new(result.SourceText,page.Url,page.Title,CleanupVersion:result.WebTextCleanupVersion,OriginalText:result.OriginalSourceText),"",CancellationToken.None);
        check(model.Requests.Count==1,"Reanalizar un texto ya limpio evita repetir la clasificación con IA");
        var many=new WebPageText(string.Join("\n\n",Enumerable.Range(0,35).Select(i=>$"Párrafo {i}. "+new string((char)('a'+i%20),800)))+"\n\nPUBLICIDAD\n\nConclusión final ROBLE-628.",page.Url,page.Title);
        model.Requests.Clear(); var cleaned=await ollama.CleanWebTextAsync(new Settings(),many,CancellationToken.None);
        check(model.Requests.Count>1 && cleaned.Text.Contains("Párrafo 34.") && cleaned.Text.EndsWith("Conclusión final ROBLE-628.",StringComparison.Ordinal) && !cleaned.Text.Contains("\n\nPUBLICIDAD"),"La limpieza por lotes llega al final de artículos largos y conserva completos los párrafos que solo se muestran como extractos a la IA");
        foreach (var invalid in new[] { "{\"remove\":[99999]}", "{\"remove\":\"todos\"}", "{\"otro\":[]}" })
        {
            using var bad=new OllamaService(new CleanupHandler(_=>invalid));
            try { await bad.CleanWebTextAsync(new Settings(),page,CancellationToken.None); check(false,"Limpieza inválida aceptada"); }
            catch(InvalidOperationException) { check(page.Text==string.Join("\n\n",paragraphs),"Una respuesta de limpieza inválida no altera el original ni acepta IDs ajenos"); }
        }
        using(var empty=new OllamaService(new CleanupHandler(blocks=>JsonSerializer.Serialize(new { remove=blocks.EnumerateArray().Select(b=>b.GetProperty("id").GetInt32()).ToArray() })))) {
            try { await empty.CleanWebTextAsync(new Settings(),page,CancellationToken.None); check(false,"Limpieza vacía aceptada"); }
            catch(InvalidOperationException) { check(true,"Si la IA descarta todo el contenido se conserva el original y el análisis informa del fallo"); }
        }
        using(var cancelled=new CancellationTokenSource()) {
            cancelled.Cancel(); model.Requests.Clear();
            try { await ollama.CleanWebTextAsync(new Settings(),page,cancelled.Token); check(false,"Cancelación de limpieza"); }
            catch(OperationCanceledException) { check(model.Requests.Count==0,"Cancelar antes de limpiar no envía solicitudes ni modifica el artículo"); }
        }
        var store=new LibraryStore(Path.Combine(output,"cleanup-library-"+Guid.NewGuid().ToString("N"))); await store.LoadAsync(); var thumbnail=WebTextThumbnail.Create(page);
        using var host=new AppHost(store,new OllamaService(new CleanupHandler()));
        var editor=new MemoryEditorWindow(host,thumbnail,webText:page) { Owner=owner }; editor.Show();
        await UntilAsync(()=>!host.IsBusy && FindButtons(editor).Single(b=>AutomationProperties.GetName(b)=="Guardar recuerdo").IsEnabled && FindElements<ThinkingOrb>(editor).Single().IsActive==false && FindButtons(editor).Any(b=>AutomationProperties.GetName(b)=="Ver texto original"));
        var source=FindElements<TextBox>(editor).Single(t=>AutomationProperties.GetName(t)=="Texto guardado de la página");
        check(source.Text==expected,"El editor muestra el texto depurado cuando termina el análisis");
        SaveVisual(editor,Path.Combine(output,"web-text-cleaned.png"));
        FindButtons(editor).Single(b=>AutomationProperties.GetName(b)=="Ver texto original").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(source.Text==page.Text,"Ver texto original permite comprobar todos los fragmentos retirados");
        FindButtons(editor).Single(b=>AutomationProperties.GetName(b)=="Ver texto limpio").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        FindButtons(editor).Single(b=>AutomationProperties.GetName(b)=="Guardar recuerdo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(()=>store.Data.Memories.Count==1 && !editor.IsVisible);
        var reload=new LibraryStore(store.Root); await reload.LoadAsync(); var saved=reload.Data.Memories.Single();
        check(saved.SourceText==expected && saved.OriginalSourceText==page.Text && saved.Copy().OriginalSourceText==page.Text && saved.WebTextCleanupVersion==OllamaService.WebTextCleanupVersion,"Guardar y recargar conserva el texto limpio, el original y la marca que evita volver a limpiarlo");
        var export=Path.Combine(output,"cleanup-export.json"); await reload.ExportAsync(export); using var data=JsonDocument.Parse(await File.ReadAllTextAsync(export));
        check(data.RootElement.GetProperty("memories")[0].GetProperty("originalSourceText").GetString()==page.Text,"La exportación incluye el original junto al contenido depurado");
        var reopened=new MemoryEditorWindow(host,thumbnail,saved) { Owner=owner }; reopened.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(FindElements<TextBox>(reopened).Single(t=>AutomationProperties.GetName(t)=="Texto guardado de la página").Text==expected && FindButtons(reopened).Any(b=>AutomationProperties.GetName(b)=="Ver texto original"),"Una tarjeta reabierta sigue mostrando la versión limpia y permite consultar el original"); reopened.Close();
        var args=Environment.GetCommandLineArgs(); var fixtureIndex=Array.IndexOf(args,"--clean-web-text-file");
        if (fixtureIndex>=0 && fixtureIndex+1<args.Length)
        {
            var input=await File.ReadAllTextAsync(args[fixtureIndex+1]); using var real=new OllamaService();
            var filtered=await real.CleanWebTextAsync(new Settings(),new(input,"","Estudio sobre escritura asistida por IA y diversidad lingüística"),CancellationToken.None);
            check(!filtered.Text.Contains("PUBLICIDAD") && !filtered.Text.Contains("Últimas Noticias") && !filtered.Text.Contains("Compartir nota") && !filtered.Text.Contains("pendrive") && !filtered.Text.Contains("Jeff Bezos") && !filtered.Text.Contains("gimnasio"),"Gemma real retira anuncios, menús y recomendaciones ajenas del texto aportado por el usuario");
            check(new[] { "880.000","87%","95%","21%","50%","seis puntos porcentuales","neuroticismo","servicios personalizados" }.All(filtered.Text.Contains),"La limpieza real del adjunto conserva las cifras, los hallazgos contrarios y la conclusión del estudio");
            // Only aggregate verification data is persisted; the user's article is not copied into the project.
            await File.WriteAllTextAsync(Path.Combine(output,"cleanup-real-result.json"),JsonSerializer.Serialize(new { OriginalCharacters=input.Length,CleanedCharacters=filtered.Text.Length,Verified=true }));
        }
    }
    private sealed class CleanupHandler(Func<JsonElement,string>? overrideReply=null):HttpMessageHandler
    {
        internal List<JsonElement> Requests { get; }=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)); var body=json.RootElement.Clone(); Requests.Add(body); string content;
            if (body.GetProperty("format").GetProperty("properties").TryGetProperty("remove",out _))
            {
                var text=body.GetProperty("messages")[1].GetProperty("content").GetString()!; var start=text.LastIndexOf("\n[",StringComparison.Ordinal)+1;
                using var blocks=JsonDocument.Parse(text[start..]);
                content=overrideReply is not null?overrideReply(blocks.RootElement):JsonSerializer.Serialize(new { remove=blocks.RootElement.EnumerateArray().Where(b=>b.GetProperty("text").GetString() is var value && (value=="PUBLICIDAD" || value=="Noticias" || value!.StartsWith("Cómo conectar",StringComparison.Ordinal) || value.StartsWith("Jeff Bezos",StringComparison.Ordinal))).Select(b=>b.GetProperty("id").GetInt32()).ToArray() });
            }
            else content=JsonSerializer.Serialize(new { title="Diversidad de la escritura",summary="El artículo estudia cambios de estilo con IA.",ideas=new[] { "Conservar las cifras del estudio","Revisar los límites de la conclusión" },category="Estudio" });
            return new(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(new { message=new { role="assistant",content } }),Encoding.UTF8,"application/json") };
        }
    }
}
