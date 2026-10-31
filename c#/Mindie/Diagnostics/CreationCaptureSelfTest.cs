using System.Net;
using System.Net.Sockets;
using System.Windows.Automation;
using Mindie.Controls;
using Mindie.Services;
using Mindie.Views;
using Mindie.Windows;

namespace Mindie.Diagnostics;

public static partial class SelfTest
{
    private static async Task CheckCreationCaptureAsync(AppHost host,MainWindow window,string output,bool withOllama,Action<bool,string> check)
    {
        var choice=new CaptureModeWindow { Owner=window }; var fits=false;
        choice.Loaded+=(_,_)=>choice.Dispatcher.BeginInvoke(()=> { fits=ButtonsFit(choice); SaveVisual(choice,Path.Combine(output,"capture-modes.png")); FindButtons(choice).Single(b=>AutomationProperties.GetName(b)=="Página web completa").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); },DispatcherPriority.ApplicationIdle);
        check(choice.ShowDialog()==true && choice.Mode==MemoryCaptureMode.FullPage && fits,"Guardar en mente ofrece área o página completa con controles visibles y selección funcional");
        window.Navigate("home"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); FindButtons(window).Single(b=>AutomationProperties.GetName(b)=="Crear mascota").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var creator=FindElements<PetCreatorView>(window).Single();
        check(creator.PromptInput.IsReadOnly && creator.PromptInput.Text==PetImportService.Prompt(PetCreatorView.DefaultIdea) && creator.PromptInput.Text.Contains("24 poses") && creator.PromptInput.Text.Contains("transparencia alfa real"),"El creador ofrece el prompt técnico completo y fijo, listo para copiar sin editarlo en la app");
        check(!FindElements<TextBox>(creator).Any(t=>AutomationProperties.GetName(t)=="Idea de tu mascota") && FindElements<TextBlock>(creator).Any(t=>t.Text.Contains("inspírate en la imagen") && t.Text.Contains("pequeño dragón de arcilla verde")),"El creador explica cómo cambiar el personaje o usar una imagen en ChatGPT, sin campo de idea editable");
        check(FindButtons(creator).Any(b=>AutomationProperties.GetName(b)=="Abrir ChatGPT") && FindButtons(creator).Any(b=>AutomationProperties.GetName(b)=="Elegir hoja PNG"),"La sección de mascotas incluye acceso a ChatGPT y un apartado para importar su PNG");
        host.Refresh(); check(FindElements<PetCreatorView>(window).Single()==creator,"La creación de una mascota conserva lo escrito cuando se actualiza el estado de la app"); SaveVisual(window,Path.Combine(output,"create-pet.png"));
        using var spriteStream=System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Sprites/pollito.png")).Stream; using var buffer=new MemoryStream(); await spriteStream.CopyToAsync(buffer); var spriteBytes=buffer.ToArray();
        var sheet=await PetImportService.ValidateAsync(spriteBytes);
        var importPath=Path.Combine(output,"pet-import.png"); await File.WriteAllBytesAsync(importPath,spriteBytes); creator.StateInput.SelectedIndex=4; await creator.ImportSheetAsync(importPath);
        check(creator.StateInput.SelectedIndex==0 && FindButtons(creator).Single(b=>AutomationProperties.GetName(b)=="Crear y usar mascota").IsEnabled,"La sección de importación valida el PNG, reinicia la vista previa y habilita crear la mascota");
        var invalidImport=Path.Combine(output,"pet-invalid.png"); await File.WriteAllBytesAsync(invalidImport,MakeFixture()); await creator.ImportSheetAsync(invalidImport);
        check(!FindButtons(creator).Single(b=>AutomationProperties.GetName(b)=="Crear y usar mascota").IsEnabled && FindElements<Image>(creator).All(i=>i.Source is null),"Una nueva imagen inválida borra la previsualización anterior y bloquea la creación");
        check(sheet.Frames.GetLength(0)==6 && sheet.Frames.GetLength(1)==4 && sheet.Frames.Cast<BitmapSource>().All(f=>f.IsFrozen && f.PixelWidth==256),"La hoja importada prepara 24 fotogramas transparentes y alineados para los seis estados");
        try { await PetImportService.ValidateAsync(MakeFixture()); check(false,"Se rechazó una imagen que no tiene la cuadrícula requerida"); } catch(ArgumentException) { check(true,"Las imágenes que no tienen 4 columnas y 6 filas se rechazan con una explicación"); }
        var opaque=BitmapSource.Create(256,384,96,96,PixelFormats.Bgra32,null,Enumerable.Repeat((byte)255,256*384*4).ToArray(),256*4);
        try { await PetImportService.ValidateAsync(Images.Encode(opaque)); check(false,"Se rechazó un fondo opaco"); } catch(ArgumentException) { check(true,"Las hojas sin transparencia real no se convierten en mascotas con fondo rectangular"); }
        var repeated=new WriteableBitmap(1024,1536,96,96,PixelFormats.Bgra32,null); var repeatedPixels=new byte[256*256*4]; sheet.Frames[0,0].CopyPixels(repeatedPixels,256*4,0);
        for(var row=0;row<6;row++) for(var column=0;column<4;column++) repeated.WritePixels(new(column*256,row*256,256,256),repeatedPixels,256*4,0);
        try { await PetImportService.ValidateAsync(Images.Encode(repeated)); check(false,"Se rechazaron poses idénticas"); } catch(ArgumentException) { check(true,"Una imagen estática repetida no se acepta como animación de seis estados"); }
        using(var cancelled=new CancellationTokenSource()) { cancelled.Cancel(); try { await PetImportService.ValidateAsync(spriteBytes,cancelled.Token); check(false,"Se canceló la validación"); } catch(OperationCanceledException) { check(true,"La validación de una mascota respeta la cancelación"); } }
        var oldSettings=host.Settings.Copy(); var pet=await host.Store.AddCustomPetAsync("Nova","Compañero de prueba",sheet); host.Settings.Pet=pet.Id; host.Settings.PetName=pet.Name; await host.Store.SaveAsync();
        var reloaded=new LibraryStore(host.Store.Root); await reloaded.LoadAsync(); LibraryStore.ValidateSettings(reloaded.Data.Settings);
        check(reloaded.Data.CustomPets.Any(p=>p.Id==pet.Id) && reloaded.Data.Settings.Pet==pet.Id && File.ReadAllBytes(reloaded.PetPath(pet.Id)).SequenceEqual(spriteBytes),"La mascota propia y su selección se conservan al reabrir, con copia intacta del PNG");
        window.Navigate("home"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); var sprites=FindSprites(window).ToArray();
        check(sprites.Length==Pets.All.Length+1 && sprites.All(s=>!s.Animate) && FindButtons(window).Any(b=>AutomationProperties.GetName(b)=="Elegir Nova"),"La mascota creada aparece junto a los ocho compañeros incluidos y sus previsualizaciones siguen estáticas"); SaveVisual(window,Path.Combine(output,"custom-pet-gallery.png"));
        var widget=new PetWindow(host); widget.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); widget.PreviewState(PetState.Move); var first=widget.Sprite.Source; await Task.Delay(250);
        check(widget.Sprite.PetId==pet.Id && !ReferenceEquals(first,widget.Sprite.Source),"El widget reproduce realmente los fotogramas de la mascota importada"); widget.Close();
        File.Delete(host.Store.PetPath(pet.Id)); var unavailable=new LibraryStore(host.Store.Root); await unavailable.LoadAsync();
        check(unavailable.Data.Settings.Pet=="mindie" && unavailable.PetWarnings.Count>0 && unavailable.Data.CustomPets.Count==1,"Una hoja propia ausente no impide abrir la biblioteca y se informa del problema");
        await File.WriteAllBytesAsync(host.Store.PetPath(pet.Id),spriteBytes); await new LibraryStore(host.Store.Root).LoadAsync(); host.Store.Data.Settings=oldSettings; await host.Store.SaveAsync();

        using var server=new SyntheticPageServer(); var browser=new FullPageCaptureWindow(Path.Combine(output,"browser-test")) { Owner=window }; browser.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await browser.Initialization;
        check(browser.Browser.CoreWebView2 is not null,"La captura completa utiliza el runtime Edge instalado y abre una vista nativa funcional");
        var core=browser.Browser.CoreWebView2!; var loaded=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        core.NavigationCompleted+=(_,e)=> { if(e.IsSuccess && core.Source.StartsWith(server.Url,StringComparison.Ordinal)) loaded.TrySetResult(); };
        core.Navigate(server.Url); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var expectedWidth=int.Parse(await core.ExecuteScriptAsync("document.documentElement.scrollWidth"),CultureInfo.InvariantCulture);
        var capture=await new FullPageCaptureService(core).CaptureAsync(CancellationToken.None); var full=Images.Decode(capture.Png);
        File.WriteAllBytes(Path.Combine(output,"full-page.png"),capture.Png); await File.WriteAllTextAsync(Path.Combine(output,"web-capture-metadata.json"),JsonSerializer.Serialize(new { full.PixelWidth,full.PixelHeight,capture.Url,capture.Title,ExpectedUrl=server.Url }));
        check(full.PixelWidth==expectedWidth && full.PixelHeight==4600 && capture.Url==server.Url && capture.Title=="Página sintética Mindie","La captura real conserva URL, título y toda la altura, más allá de la vista inicial");
        await File.WriteAllTextAsync(Path.Combine(output,"web-pixels.json"),JsonSerializer.Serialize(new { Top=Pixel(full,10,10).ToString(),Middle=Pixel(full,10,2500).ToString(),Bottom=Pixel(full,10,full.PixelHeight-10).ToString() }));
        // Allow small color-profile conversion differences while distinguishing the three bands.
        bool Near(Color actual,Color expected)=>Math.Abs(actual.R-expected.R)<=8 && Math.Abs(actual.G-expected.G)<=8 && Math.Abs(actual.B-expected.B)<=8;
        check(Near(Pixel(full,10,10),Color.FromRgb(242,245,234)) && Near(Pixel(full,10,2500),Color.FromRgb(227,237,245)) && Near(Pixel(full,10,full.PixelHeight-10),Color.FromRgb(32,58,88)),"Las secciones superior, central e inferior de la captura corresponden a partes distintas de la página");
        var pieces=CaptureImages.SplitVertical(capture.Png); check(pieces.Count==3 && pieces.Sum(p=>Images.Decode(p).PixelHeight)==full.PixelHeight,"El análisis divide la página completa sin omitir su última sección"); File.WriteAllBytes(Path.Combine(output,"web-last-section.png"),pieces[^1]);
        File.WriteAllBytes(Path.Combine(output,"full-page.png"),capture.Png); SaveVisual(browser,Path.Combine(output,"full-page-browser.png"));
        var handler=new KnowledgeHandler(); using(var controlled=new OllamaService(handler))
        {
            var item=await controlled.AnalyzeWebAsync(host.Settings,capture.Png,"Recordar también el pie",CancellationToken.None);
            var requests=handler.Requests.Where(r=>!r.TryGetProperty("format",out _)).ToArray();
            check(item.Kind=="web" && requests.Length==3 && requests.Last().GetProperty("messages")[1].GetProperty("images")[0].GetString()==Convert.ToBase64String(pieces[2]),"Gemma recibe las tres imágenes, incluida la sección final, antes de crear la tarjeta web");
        }
        using(var cancelled=new CancellationTokenSource()) { cancelled.Cancel(); try { await new FullPageCaptureService(core).CaptureAsync(cancelled.Token); check(false,"Se canceló la captura web"); } catch(OperationCanceledException) { check(true,"La captura de página completa se puede cancelar"); } }
        var ended=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); browser.BrowserEnvironment!.BrowserProcessExited+=(_,_)=>ended.TrySetResult(); browser.Close(); await Task.WhenAny(ended.Task,Task.Delay(10000));
        var webItem=new MemoryItem { Kind="web",Title="Página web completa de prueba",SourceUrl=capture.Url,SourceName=capture.Title,UserContext="Mi contexto" }; await host.Store.AddAsync(webItem,capture.Png);
        reloaded=new LibraryStore(host.Store.Root); await reloaded.LoadAsync();
        check(reloaded.Data.Memories.Any(m=>m.Id==webItem.Id && m.Kind=="web" && m.SourceUrl==capture.Url) && Images.Load(reloaded.CapturePath(webItem.Id))!.PixelHeight==full.PixelHeight,"La tarjeta web guarda la captura íntegra y su enlace para consultarla después");
        if(withOllama)
        {
            var actual=await host.Ollama.AnalyzeWebAsync(host.Settings,capture.Png,"Me interesa el inicio y también la conclusión, especialmente el código de verificación del pie de página.",CancellationToken.None,new Progress<string>(text=>File.WriteAllText(Path.Combine(output,"web-progress.txt"),text)));
            check(string.Join(" ",new[] { actual.Title,actual.Summary }.Concat(actual.Ideas)).Contains("CIPRES",StringComparison.OrdinalIgnoreCase),"Gemma real analiza la página completa e incorpora el dato que solo aparece al final");
            await File.WriteAllTextAsync(Path.Combine(output,"web-ollama.json"),JsonSerializer.Serialize(actual));
        }
        window.Navigate("saved");
    }
    private static Color Pixel(BitmapSource source,int x,int y)
    {
        var image=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0); var pixel=new byte[4]; image.CopyPixels(new(x,y,1,1),pixel,4,0); return Color.FromArgb(pixel[3],pixel[2],pixel[1],pixel[0]);
    }
    private sealed class SyntheticPageServer:IDisposable
    {
        private readonly TcpListener _listener=new(IPAddress.Loopback,0);
        private readonly CancellationTokenSource _stop=new();
        public string Url { get; }
        public SyntheticPageServer()
        {
            _listener.Start(); Url=$"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _=Task.Run(async()=> {
                while(!_stop.IsCancellationRequested)
                {
                    try
                    {
                        using var client=await _listener.AcceptTcpClientAsync(_stop.Token); using var stream=client.GetStream();
                        using(var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true)) for(var line=0;line<64;line++) if(string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token))) break;
                        const string html="""
<!doctype html><html><head><title>Página sintética Mindie</title><style>*{box-sizing:border-box}body{margin:0;font:26px Arial}section{height:2000px;padding:40px;background:#f2f5ea}section+section{background:#e3edf5}footer{height:600px;padding:40px;background:#203a58;color:white}</style></head><body><section><h1>Cómo cuidar un invernadero</h1><p>Al principio: organiza la siembra y protege las plantas del frío.</p></section><section><h2>Cuidados durante el crecimiento</h2><p>Revisa el riego cada semana y mantén luz suficiente.</p></section><footer id="final"><h2>Conclusión y verificación</h2></footer><script>let added=false;addEventListener('scroll',()=>{if(scrollY>600&&!added){added=true;document.getElementById('final').insertAdjacentHTML('beforeend','<p>El código final de verificación es CIPRES-928.</p>')}});</script></body></html>
""";
                        var body=Encoding.UTF8.GetBytes(html); var header=Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"); await stream.WriteAsync(header,_stop.Token); await stream.WriteAsync(body,_stop.Token);
                    }
                    catch(Exception ex) when(ex is OperationCanceledException or ObjectDisposedException or SocketException or IOException) { if(_stop.IsCancellationRequested) break; }
                }
            });
        }
        public void Dispose() { _stop.Cancel(); _listener.Stop(); _stop.Dispose(); }
    }
}
