using Mindie.Controls;
using Mindie.Services;
using Mindie.Views;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace Mindie.Diagnostics;

public static partial class SelfTest
{
    private sealed class FixtureKeys:INvidiaKeyStore
    {
        public string? Value="synthetic-key-not-a-real-credential";
        public string? Read()=>Value;
        public void Save(string value)=>Value=value;
        public void Delete()=>Value=null;
    }
    private sealed class NvidiaFixture:HttpMessageHandler
    {
        public JsonElement Payload;
        public string? Authorization;
        public Uri? Uri;
        public int Calls;
        public bool Block;
        public HttpStatusCode Code=HttpStatusCode.OK;
        public string Reply="Respuesta de prueba";
        public string Finish="stop";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            Calls++; Uri=request.RequestUri; Authorization=request.Headers.Authorization?.ToString();
            Payload=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation)).RootElement.Clone();
            if(Block) await Task.Delay(Timeout.Infinite,cancellation);
            return new(Code) { Content=new StringContent(JsonSerializer.Serialize(new { choices=new[] { new { message=new { content=Reply },finish_reason=Finish } } }),Encoding.UTF8,"application/json") };
        }
    }
    private static async Task CheckNvidiaAsync(MainWindow main,string output,Action<bool,string> check)
    {
        var keys=new FixtureKeys(); var handler=new NvidiaFixture();
        using var service=new OllamaService(handler,keys);
        var settings=new Settings { Provider="nvidia" };
        var status=await service.StatusAsync(settings);
        check(status.Available && status.Models.SequenceEqual(new[] { Settings.NvidiaModel }) && status.Capabilities.Contains("vision") && !status.Capabilities.Contains("audio") && handler.Calls==0,"NVIDIA conoce el modelo remoto sin consultar Ollama ni atribuirle audio");
        var image=ChatImageService.Prepare(Images.Decode(MakeFixture()),"Prueba.png");
        var result=await service.ChatAsync(settings,[new ChatMessage { Content="Describe la imagen",Images=[image.Image] }],CancellationToken.None,(_,_)=>Task.FromResult(image.Png));
        check(handler.Uri==new Uri("https://integrate.api.nvidia.com/v1/chat/completions") && handler.Authorization=="Bearer "+keys.Value && result.Content==handler.Reply,"NVIDIA envía la autenticación al endpoint fijo y lee la respuesta final");
        var payload=handler.Payload;
        check(payload.GetProperty("model").GetString()==Settings.NvidiaModel && !payload.TryGetProperty("options",out _) && !payload.TryGetProperty("keep_alive",out _) && !payload.TryGetProperty("think",out _) && !payload.TryGetProperty("format",out _) && !payload.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean(),"El adaptador NVIDIA traduce los parámetros sin enviar campos exclusivos de Ollama");
        var parts=payload.GetProperty("messages").EnumerateArray().Last().GetProperty("content");
        check(parts.ValueKind==JsonValueKind.Array && parts[1].GetProperty("image_url").GetProperty("url").GetString()=="data:image/png;base64,"+Convert.ToBase64String(image.Png),"Las imágenes se envían como partes multimodales sin rutas locales ni archivos públicos");
        handler.Reply="{\"supported\":false,\"answer\":\"No debes mostrar esta respuesta fuera del tema\"}";
        var memory=new MemoryItem { Title="Plantas",Summary="Regar una vez a la semana",Ideas=["Luz indirecta","Maceta con drenaje"] };
        var grounded=await service.AskMemoryAsync(settings,memory,[new ChatMessage { Content="¿Quién ganó un torneo?" }],null,CancellationToken.None);
        check(grounded.Content.Contains("no contiene información suficiente") && !grounded.Content.Contains("No debes mostrar"),"NVIDIA conserva la negativa fija para preguntas ajenas al recuerdo");
        check(handler.Payload.GetProperty("messages")[0].GetProperty("content").GetString()!.Contains("supported") && !handler.Payload.TryGetProperty("response_format",out _),"El esquema se solicita explícitamente sin suponer soporte remoto de response_format");
        handler.Reply="{\"title\":\"Estudio organizado\",\"summary\":\"Organiza el tiempo para estudiar.\",\"ideas\":[\"Organiza tu semana\",\"Descansa cada hora\"],\"category\":\"Estudio\"}";
        var card=await service.AnalyzeAsync(settings,image.Png,CancellationToken.None);
        check(card.Title=="Estudio organizado" && card.Ideas.Count==2,"La generación remota de campos pasa por la validación existente de tarjetas");
        handler.Reply="{\"remove\":[1]}";
        var page=WebTextService.FromPlainText("Este artículo explica cómo organizar una semana de estudio, con descansos y tiempo para revisar los conceptos aprendidos.\n\nPUBLICIDAD\n\nLa conclusión conserva los ejemplos y los datos del texto original para poder consultarlos después.","https://example.com","Estudio");
        var cleaned=await service.CleanWebTextAsync(settings,page,CancellationToken.None);
        check(!cleaned.Text.Contains("PUBLICIDAD") && cleaned.Text.Contains("La conclusión") && cleaned.OriginalText.Contains("PUBLICIDAD"),"La limpieza web mediante NVIDIA conserva los bloques originales y retira solo IDs válidos");
        keys.Value=null; var before=handler.Calls;
        try { await service.TestNvidiaAsync(); check(false,"Rechazar clave ausente"); } catch(InvalidOperationException ex) { check(ex.Message.Contains("Configura tu clave") && handler.Calls==before,"Sin clave NVIDIA no hay solicitud ni fallback silencioso"); }
        keys.Value="synthetic-key-not-a-real-credential"; handler.Code=HttpStatusCode.Unauthorized;
        try { await service.TestNvidiaAsync(); check(false,"Rechazar autenticación inválida"); } catch(InvalidOperationException ex) { check(ex.Message.Contains("rechazó") && !ex.ToString().Contains(keys.Value),"Un 401 explica cómo recuperarse sin exponer la credencial"); }
        handler.Code=HttpStatusCode.OK; handler.Finish="length";
        try { await service.TestNvidiaAsync(); check(false,"Rechazar salida truncada"); } catch(InvalidOperationException ex) { check(ex.Message.Contains("incompleto"),"No se guarda una respuesta remota truncada"); }
        handler.Finish="stop"; handler.Block=true; using var cancel=new CancellationTokenSource();
        var pending=service.TestNvidiaAsync(cancel.Token); cancel.Cancel();
        try { await pending; check(false,"Cancelar HTTP NVIDIA"); } catch(OperationCanceledException) { check(true,"Cancelar NVIDIA interrumpe la solicitud HTTP"); }
        handler.Block=false;
        var root=Path.Combine(output,"nvidia-"+Guid.NewGuid().ToString("N")); var store=new LibraryStore(root); await store.LoadAsync();
        using var host=new AppHost(store,new OllamaService(handler,keys));
        store.Data.Settings.Provider="nvidia"; handler.Code=HttpStatusCode.Unauthorized;
        try { await host.SendAsync("Describe esta imagen",[image]); check(false,"Revertir conversación NVIDIA fallida"); }
        catch(InvalidOperationException) { check(host.Messages.Count==0 && store.Data.Conversations.Count==0 && !File.Exists(store.ChatImagePath(image.Image.Id)),"Un fallo NVIDIA revierte el turno, la sesión pendiente y sus adjuntos"); }
        handler.Code=HttpStatusCode.OK; handler.Block=true;
        var calls=handler.Calls; var sending=host.SendAsync("Petición cancelable",[image]); await UntilAsync(()=>handler.Calls>calls);
        var localSettings=host.Settings.Copy(); localSettings.Provider="ollama"; await host.SaveSettingsAsync(localSettings); host.Cancel();
        try { await sending; check(false,"Cancelar al cambiar proveedor"); }
        catch(OperationCanceledException) { check(handler.Uri!.Host=="integrate.api.nvidia.com" && host.Messages.Count==0 && !File.Exists(store.ChatImagePath(image.Image.Id)),"Cambiar proveedor no redirige una generación en curso; cancelar no deja turno ni imagen"); }
        handler.Block=false;
        main.Hide(); var aiWindow=new MainWindow(host); host.MainWindow=aiWindow; aiWindow.Show(); aiWindow.Navigate("settings");
        var localDiscoveries=0;
        var view=new SettingsView(host,(_,_)=> { localDiscoveries++; return Task.FromResult<IReadOnlyList<LocalModelInfo>>([new(host.Settings.Model,["completion","vision"])]); });
        aiWindow.PageHost.Content=view; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var local=FindElements<LocalModelPicker>(view).Single(); await local.Loading;
        var providers=FindElements<AiProviderSettings>(view).Single();
        providers.ProviderPicker.SelectedIndex=1; await UntilAsync(()=>host.Settings.IsNvidia); await view.LastSave; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(!FindElements<LocalModelPicker>(view).Any() && FindElements<TextBlock>(view).Any(t=>t.Text.Contains("se realiza en NVIDIA")),"Configuración muestra campos y aviso de NVIDIA sin mantener activo el catálogo local");
        var password=FindElements<PasswordBox>(view).Single(); password.Password="synthetic-replacement-credential"; await Task.Delay(650);
        check(keys.Value=="synthetic-replacement-credential" && password.Password.Length==0,"La clave se guarda automáticamente mediante el almacén seguro y se limpia el input");
        var json=await File.ReadAllTextAsync(Path.Combine(root,"library.json"));
        check(json.Contains("nvidia") && !json.Contains(keys.Value) && host.ProcessingLabel.Contains("remota"),"Solo se persiste el proveedor y la interfaz identifica el procesamiento remoto");
        var scroll=(ScrollViewer)view.Content; scroll.ScrollToVerticalOffset(880); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); SaveVisual(aiWindow,Path.Combine(output,"settings-nvidia.png"));
        scroll.ScrollToVerticalOffset(1100); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); SaveVisual(aiWindow,Path.Combine(output,"settings-nvidia-key.png"));
        var restored=new LibraryStore(root); await restored.LoadAsync();
        check(restored.Data.Settings.IsNvidia && restored.Data.Settings.Model=="gemma4:12b","Reabrir conserva el proveedor y la preferencia local por separado");
        providers.ProviderPicker.SelectedIndex=0; await UntilAsync(()=>!host.Settings.IsNvidia); await view.LastSave; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(FindElements<LocalModelPicker>(view).Any() && localDiscoveries>=2 && host.ProcessingLabel.Contains("localmente"),"Volver a Ollama restaura el selector y las capacidades de sus modelos");
        aiWindow.Navigate("home"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/LogoMark.png")) is not null && aiWindow.Icon is not null,"El logo elegido y el icono Windows están incluidos como recursos");
        SaveVisual(aiWindow,Path.Combine(output,"official-logo.png"));
        host.CloseWindows(); main.Show();
    }
    public static async Task<int> ConfigureNvidiaAsync(string path,bool storeOnly=false)
    {
        var output=Path.Combine(Environment.CurrentDirectory,"nvidia-connection.json");
        var progress=Path.Combine(Environment.CurrentDirectory,"nvidia-progress.txt");
        try {
            await File.WriteAllTextAsync(progress,"Leyendo credencial proporcionada, sin copiarla al proyecto.");
            var input=await File.ReadAllTextAsync(path);
            var match=Regex.Match(input,@"nvapi-[A-Za-z0-9_-]+");
            if(!match.Success) throw new InvalidOperationException("El archivo no contiene una clave NVIDIA reconocible.");
            new NvidiaCredentials().Save(match.Value);
            if(storeOnly) {
                await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new { passed=true,credentialStoredInWindows=true,connectionChecked=false }));
                return 0;
            }
            var keys=new FixtureKeys { Value=match.Value };
            using var service=new OllamaService(new HttpClientHandler { AllowAutoRedirect=false },keys);
            await File.WriteAllTextAsync(progress,"Probando conversación NVIDIA con contenido sintético.");
            await service.TestNvidiaAsync();
            await File.WriteAllTextAsync(progress,"Texto verificado; clave guardada en Windows. Probando una imagen sintética.");
            // Only synthetic material is used for the live visual and grounded checks.
            var card=await service.AnalyzeAsync(new Settings { Provider="nvidia" },MakeFixture(),CancellationToken.None);
            await File.WriteAllTextAsync(progress,"Visión y tarjeta verificadas. Comprobando rechazo de una pregunta ajena a la fuente.");
            var refusal=await service.AskMemoryAsync(new Settings { Provider="nvidia" },new MemoryItem { Title="Estudio",Summary="Organizar la semana y descansar cada hora.",Ideas=["Organizar la semana","Descansar"] },[new ChatMessage { Content="¿Quién ganó el campeonato mundial de fútbol de 2018?" }],null,CancellationToken.None);
            if(!refusal.Content.Contains("no contiene información suficiente")) throw new InvalidOperationException("La respuesta no respetó la restricción de fuente en la prueba real.");
            await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new { passed=true,model=Settings.NvidiaModel,connection=true,vision=true,structuredCard=card.Title.Length>0,sourceRefusal=true,credentialStoredInWindows=true },new JsonSerializerOptions { WriteIndented=true }));
            return 0;
        }
        catch(Exception ex) { await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new { passed=false,error=ex.Message })); return 1; }
    }
}
