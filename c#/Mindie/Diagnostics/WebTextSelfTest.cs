using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Windows.Automation;
using Microsoft.Web.WebView2.Wpf;
using Mindie.Controls;
using Mindie.Services;
using Mindie.Windows;

namespace Mindie.Diagnostics;

public static partial class SelfTest
{
    private const string TextArticle="""
<!doctype html><html><head><meta charset="utf-8"><title>Artículo de prueba</title>
<link rel="stylesheet" href="/site.css"><script src="/bundle.js"></script></head><body>
<header>MENÚ QUE NO DEBE ANALIZARSE</header><nav>COMPRAR AHORA</nav>
<article><header><h1>Lectura con menos carga</h1></header>
<p>Un artículo íntegro conserva sus ideas y conclusiones. El café, la investigación y la información tienen acentos.</p>
<p>Esta página de prueba permite verificar una lectura ligera sin renderizado ni descargas visuales.</p>
<img src="/foto-grande.jpg"><video src="/pelicula.mp4"></video><iframe src="/anuncio"></iframe>
<ul><li>Leer con atención.</li><li>Conservar el texto original.</li></ul>
<table><tr><th>Concepto</th><th>Valor</th></tr><tr><td>Presupuesto</td><td>4200</td></tr></table>
<p hidden>OCULTO-UNO</p><p style="display: none">OCULTO-DOS</p>
<script>document.write("SCRIPT QUE NO DEBE EJECUTARSE");</script>
<p>La conclusión final incluye el código ENCINA-527, presente al final del artículo.</p>
</article><aside>PUBLICIDAD</aside><footer>SUSCRÍBETE</footer></body></html>
""";
    private static async Task CheckWebTextAsync(MainWindow owner,string output,Action<bool,string> check)
    {
        using var server=new TextPageServer(); using var reader=new WebTextService();
        var page=await reader.ReadAsync(server.Url,CancellationToken.None);
        check(page.Title=="Lectura con menos carga" && page.Text.Contains("ENCINA-527") && page.Text.Contains("4200") && page.Text.Contains("café"),"La lectura HTML conserva título, acentos, listas, tablas y la conclusión del artículo");
        check(!page.Text.Contains("COMPRAR AHORA") && !page.Text.Contains("PUBLICIDAD") && !page.Text.Contains("OCULTO") && !page.Text.Contains("SCRIPT QUE") && !page.Text.Contains("SUSCRÍBETE"),"El extractor descarta navegación, publicidad lateral, elementos ocultos y scripts");
        check(server.Requests.SequenceEqual(new[] { "/" }) && page.DownloadedBytes==Encoding.UTF8.GetByteCount(TextArticle),"Solo se descarga un HTML: no hay peticiones a fotos, CSS, scripts, video ni iframes");
        var redirected=await reader.ReadAsync(server.Url+"redirect",CancellationToken.None);
        check(redirected.Url==server.Url && redirected.Text==page.Text,"Las redirecciones conservan el enlace final y el texto extraído");
        var plain=await reader.ReadAsync(server.Url+"plain",CancellationToken.None);
        check(plain.Text.Contains("ENCINA-527"),"La ruta ligera también acepta una respuesta de texto plano");
        var latin=await WebTextService.ExtractAsync("<html><body><article><h1>Prueba</h1><p>"+new string('a',90)+" café y niño.</p></article></body></html>",server.Url);
        check(latin.Text.Contains("café y niño"),"Los caracteres Unicode del documento se conservan al extraer texto");
        using (var latinReader=new WebTextService(new TextHttpHandler(_=> {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var result=new HttpResponseMessage(HttpStatusCode.OK) { Content=new ByteArrayContent(Encoding.GetEncoding(1252).GetBytes(TextArticle)) };
            result.Content.Headers.ContentType=new("text/html") { CharSet="windows-1252" }; return result;
        })))
            check((await latinReader.ReadAsync(server.Url,CancellationToken.None)).Text.Contains("café"),"La descarga respeta la codificación Windows-1252 además de UTF-8");
        using(var cancelled=new CancellationTokenSource()) { cancelled.Cancel(); try { await reader.ReadAsync(server.Url,cancelled.Token); check(false,"Cancelación de lectura"); } catch(OperationCanceledException) { check(true,"La descarga de texto se puede cancelar sin iniciar el análisis"); } }
        foreach(var (type,content,reason) in new[] {
            ("application/pdf","%PDF-1.7","Los enlaces a PDF se rechazan antes de descargarlos como artículo"),
            ("text/html","<html><body><script src='/app.js'></script></body></html>","Una página vacía que requiere JavaScript ofrece pegar el texto en lugar de analizar contenido inexistente")
        })
        {
            using var controlledReader=new WebTextService(new TextHttpHandler(_=>new(HttpStatusCode.OK) { Content=new StringContent(content,Encoding.UTF8,type) }));
            try { await controlledReader.ReadAsync(server.Url,CancellationToken.None); check(false,reason); } catch(InvalidDataException) { check(true,reason); }
        }
        using(var largeReader=new WebTextService(new TextHttpHandler(_=> {
            var result=new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(TextArticle,Encoding.UTF8,"text/html") };
            result.Content.Headers.ContentLength=WebTextService.MaximumBytes+1L; return result;
        }))) {
            try { await largeReader.ReadAsync(server.Url,CancellationToken.None); check(false,"Límite HTML"); } catch(InvalidDataException) { check(true,"El límite de descarga evita cargar HTML excesivo en memoria"); }
        }
        try { WebTextService.FromPlainText(new string('a',WebTextService.MaximumTextCharacters+1),server.Url,""); check(false,"Límite de texto"); } catch(InvalidDataException) { check(true,"Un artículo demasiado extenso se rechaza explícitamente sin guardar solo el principio"); }
        var chooser=new CaptureModeWindow { Owner=owner }; var choiceFits=false;
        chooser.Loaded+=(_,_)=>chooser.Dispatcher.BeginInvoke(()=> {
            choiceFits=ButtonsFit(chooser); SaveVisual(chooser,Path.Combine(output,"capture-choices-text.png"));
            FindButtons(chooser).Single(b=>AutomationProperties.GetName(b)=="Página web: solo texto").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        },DispatcherPriority.ApplicationIdle);
        check(chooser.ShowDialog()==true && chooser.Mode==MemoryCaptureMode.WebText && choiceFits,"Guardar en mente ofrece Solo texto recomendado y conserva ambas opciones de captura con botones visibles");
        var ui=new WebTextCaptureWindow(new WebTextService(),true) { Owner=owner }; ui.Show(); ui.AddressInput.Text=server.Url; await ui.ReadAsync();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var analyze=FindButtons(ui).Single(b=>AutomationProperties.GetName(b)=="Analizar texto");
        check(ui.TextPreview.IsReadOnly && ui.TextPreview.Text.Contains("ENCINA-527") && !analyze.IsEnabled && !FindElements<WebView2>(ui).Any(),"El lector muestra texto revisable sin WebView2 y exige contexto cuando está activado");
        ui.ContextInput.Text="Quiero guardar el concepto del presupuesto para estudiar."; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(analyze.IsEnabled && ui.ContextInput.MaxLength==0 && ButtonsFit(ui),"El contexto no tiene límite fijo y las acciones del lector caben en pantalla");
        SaveVisual(ui,Path.Combine(output,"web-text-reader.png")); ui.AddressInput.Text=server.Url+"otra";
        check(!analyze.IsEnabled && ui.TextPreview.Text.Length==0,"Cambiar la URL invalida el contenido anterior para no mezclar páginas");
        ui.AddressInput.Text=server.Url; await ui.ReadAsync(); ui.Close();
        var model=new KnowledgeHandler(); using var ollama=new OllamaService(model);
        var card=await ollama.AnalyzeWebTextAsync(new Settings(),page,"Quiero recordar el presupuesto.",CancellationToken.None);
        check(model.Requests.Count==2 && model.Requests.All(r=>r.GetProperty("messages").EnumerateArray().All(m=>!m.TryGetProperty("images",out _))) && model.Requests.Last().GetProperty("messages")[1].GetProperty("content").GetString()!.Contains("ENCINA-527"),"Un artículo corto pasa por limpieza y resumen sin imágenes y conserva su última sección");
        var longText=string.Concat(Enumerable.Repeat("Contenido del artículo que debe analizarse completo. ",250))+" FINAL-TEXTO-8197";
        model.Requests.Clear(); await ollama.AnalyzeWebTextAsync(new Settings(),new(longText,page.Url,page.Title),"",CancellationToken.None);
        check(model.Requests.All(r=>r.GetProperty("messages").EnumerateArray().All(m=>!m.TryGetProperty("images",out _))) && model.Requests.Any(r=>r.GetProperty("messages")[1].GetProperty("content").GetString()!.Contains("FINAL-TEXTO-8197")),"El texto largo se procesa por partes sin omitir su final ni recurrir a visión");
        var store=new LibraryStore(Path.Combine(output,"text-library-"+Guid.NewGuid().ToString("N"))); await store.LoadAsync();
        var thumbnail=WebTextThumbnail.Create(page); await store.AddAsync(card,thumbnail);
        var reopened=new LibraryStore(store.Root); await reopened.LoadAsync(); var saved=reopened.Data.Memories.Single();
        check(saved.Kind=="web-text" && saved.SourceText==page.Text && saved.Copy().SourceText==page.Text && saved.SourceUrl==page.Url && Images.Decode(thumbnail).PixelWidth==640,"La tarjeta de Solo texto conserva el artículo y enlace al reabrir con una miniatura pequeña");
        var export=Path.Combine(output,"web-text-export.json"); await reopened.ExportAsync(export); using var json=JsonDocument.Parse(await File.ReadAllTextAsync(export));
        check(json.RootElement.GetProperty("memories")[0].GetProperty("sourceText").GetString()==page.Text,"La exportación incluye el texto completo guardado");
        var uiModel=new KnowledgeHandler(); using var host=new AppHost(store,new OllamaService(uiModel));
        var editor=new MemoryEditorWindow(host,thumbnail,saved) { Owner=owner }; editor.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(FindElements<TextBox>(editor).Single(t=>AutomationProperties.GetName(t)=="Texto guardado de la página").Text==page.Text,"Abrir una tarjeta de Solo texto muestra su artículo original, sin volver a descargar la web");
        SaveVisual(editor,Path.Combine(output,"web-text-saved.png"));
        FindButtons(editor).Single(b=>AutomationProperties.GetName(b)=="Analizar con Gemma").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(()=>!host.IsBusy && FindButtons(editor).Single(b=>AutomationProperties.GetName(b)=="Guardar cambios").IsEnabled);
        check(FindElements<TextBox>(editor).Single(t=>AutomationProperties.GetName(t)=="Título del recuerdo").Text.Length>0 && FindElements<ThinkingOrb>(editor).Single().IsActive==false && uiModel.Requests.Count>0 && uiModel.Requests.All(r=>r.GetProperty("messages").EnumerateArray().All(m=>!m.TryGetProperty("images",out _))),"Reanalizar el texto guardado genera campos sin enviar imágenes y termina el indicador de espera");
        editor.Close(); await reopened.DeleteAsync(saved); var deleted=new LibraryStore(store.Root); await deleted.LoadAsync();
        check(!deleted.Data.Memories.Any() && !File.Exists(store.CapturePath(saved.Id)),"Eliminar un recuerdo de texto retira su contenido y miniatura de la biblioteca");
        if (Environment.GetCommandLineArgs().Contains("--with-web-text-ollama"))
        {
            using var real=new OllamaService();
            var result=await real.AnalyzeWebTextAsync(new Settings(),page,"Quiero recordar especialmente el código final ENCINA-527 y el presupuesto de 4200.",CancellationToken.None);
            check(result.Title.Length>0 && result.Ideas.Count>=2 && result.SourceText==page.Text,"Gemma real genera una tarjeta usando solo texto y conserva el artículo completo");
            check((result.Title+" "+result.Summary+" "+string.Join(" ",result.Ideas)).Contains("ENCINA-527"),"Gemma real conserva el código de la última sección en la tarjeta de texto");
            await File.WriteAllTextAsync(Path.Combine(output,"web-text-ollama.json"),JsonSerializer.Serialize(result));
        }
    }
    private sealed class TextHttpHandler(Func<HttpRequestMessage,HttpResponseMessage> reply):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(reply(request)); }
    }
    private sealed class TextPageServer:IDisposable
    {
        private readonly TcpListener _listener=new(IPAddress.Loopback,0);
        private readonly CancellationTokenSource _stop=new();
        private readonly Task _worker;
        internal List<string> Requests { get; }=[];
        internal string Url { get; }
        internal TextPageServer()
        {
            _listener.Start(); Url=$"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _worker=Task.Run(async()=> {
                while(!_stop.IsCancellationRequested) {
                    try {
                        using var client=await _listener.AcceptTcpClientAsync(_stop.Token); using var stream=client.GetStream();
                        using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
                        var first=await reader.ReadLineAsync(_stop.Token); var route=first?.Split(' ').ElementAtOrDefault(1)??"/";
                        lock(Requests) Requests.Add(route);
                        for(var i=0;i<64;i++) if(string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token))) break;
                        var isRedirect=route=="/redirect"; var body=Encoding.UTF8.GetBytes(route=="/plain"?new string('a',100)+" ENCINA-527":TextArticle);
                        var header=Encoding.ASCII.GetBytes(isRedirect?"HTTP/1.1 302 Found\r\nLocation: /\r\nContent-Length: 0\r\nConnection: close\r\n\r\n":$"HTTP/1.1 200 OK\r\nContent-Type: {(route=="/plain"?"text/plain":"text/html")}; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(header,_stop.Token); if(!isRedirect) await stream.WriteAsync(body,_stop.Token);
                    }
                    catch(Exception ex) when(ex is OperationCanceledException or ObjectDisposedException or SocketException or IOException) { if(_stop.IsCancellationRequested) break; }
                }
            });
        }
        public void Dispose() { _stop.Cancel(); _listener.Stop(); }
    }
}
