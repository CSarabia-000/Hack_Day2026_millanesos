using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Mindie.Services;

public sealed partial class OllamaService
{
    private static readonly Uri NvidiaEndpoint=new("https://integrate.api.nvidia.com/v1/chat/completions");
    internal static object NvidiaPayload(object payload)
    {
        var source=JsonSerializer.SerializeToElement(payload);
        var messages=new List<object>();
        var hasFormat=source.TryGetProperty("format",out var schema);
        if(hasFormat) messages.Add(new { role="system",content="Devuelve únicamente un objeto JSON válido que cumpla exactamente este esquema. Sin Markdown, explicaciones ni bloques de código. Respeta las restricciones de fuente de los otros mensajes. Esquema JSON: "+schema.GetRawText() });
        long imageBytes=0; var imageCount=0;
        foreach(var message in source.GetProperty("messages").EnumerateArray()) {
            var role=message.GetProperty("role").GetString()!;
            var text=message.GetProperty("content").GetString()??"";
            if(message.TryGetProperty("images",out var images) && images.GetArrayLength()>0) {
                if(role!="user") throw new InvalidOperationException("Las imágenes deben adjuntarse a un mensaje del usuario.");
                var parts=new List<object> { new { type="text",text } };
                foreach(var image in images.EnumerateArray()) {
                    var encoded=image.GetString()??""; imageBytes+=encoded.Length; imageCount++;
                    if(imageCount>ChatImageService.MaximumCount || imageBytes>44_800_000) throw new InvalidOperationException("Las imágenes de este contexto superan el límite. Inicia una nueva conversación o usa menos imágenes.");
                    parts.Add(new { type="image_url",image_url=new { url="data:image/png;base64,"+encoded } });
                }
                messages.Add(new { role,content=parts });
            }
            else messages.Add(new { role,content=text });
        }
        var options=source.TryGetProperty("options",out var configured)?configured:default;
        var tokens=options.ValueKind==JsonValueKind.Object && options.TryGetProperty("num_predict",out var count)?count.GetInt32():1024;
        var temperature=options.ValueKind==JsonValueKind.Object && options.TryGetProperty("temperature",out var heat)?heat.GetDouble():.2;
        return new { model=Settings.NvidiaModel,messages,stream=false,max_tokens=Math.Clamp(hasFormat?Math.Max(tokens,1024):tokens,128,4096),temperature,chat_template_kwargs=new { enable_thinking=false } };
    }
    private async Task<JsonElement> RequestNvidiaAsync(object payload,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var key=Keys.Read();
        if(string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Configura tu clave NVIDIA en Configuración → Inteligencia artificial, o elige Ollama local.");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromMinutes(3));
        using var request=new HttpRequestMessage(HttpMethod.Post,NvidiaEndpoint) { Content=JsonContent.Create(NvidiaPayload(payload)) };
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try {
            using var response=await _client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            if(!response.IsSuccessStatusCode) throw new InvalidOperationException(response.StatusCode switch {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden=>"NVIDIA rechazó la clave o el acceso al modelo. Revisa tu clave y permisos en Configuración.",
                HttpStatusCode.TooManyRequests=>"NVIDIA ha alcanzado el límite de solicitudes o cuota. Espera y vuelve a intentarlo.",
                HttpStatusCode.RequestEntityTooLarge=>"El contenido supera el tamaño admitido por NVIDIA. Usa menos imágenes o una selección más pequeña.",
                HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest=>"NVIDIA no pudo aceptar esta solicitud. Revisa el contenido e intenta de nuevo.",
                _=>$"NVIDIA respondió con error {(int)response.StatusCode}. Intenta de nuevo más tarde."
            });
            if(response.StatusCode==HttpStatusCode.Accepted) throw new InvalidOperationException("NVIDIA dejó la solicitud pendiente. Intenta de nuevo más tarde; no se guardó una respuesta incompleta.");
            await using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);
            // Generation is bounded to 4096 tokens; reject unexpectedly large responses before parsing.
            using var buffer=new MemoryStream(); var chunk=new byte[8192];
            while(true) { var read=await stream.ReadAsync(chunk,deadline.Token); if(read==0) break; if(buffer.Length+read>2_000_000) throw new InvalidOperationException("La respuesta de NVIDIA superó el límite de tamaño."); buffer.Write(chunk,0,read); }
            using var json=JsonDocument.Parse(buffer.ToArray());
            var choice=json.RootElement.GetProperty("choices")[0];
            if(choice.TryGetProperty("finish_reason",out var finish) && finish.GetString()=="length") throw new InvalidOperationException("NVIDIA alcanzó el límite de respuesta. Reintenta con una petición más concreta; no se guardó texto incompleto.");
            var content=choice.GetProperty("message").GetProperty("content").GetString()?.Trim();
            if(string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("NVIDIA no devolvió texto. Intenta de nuevo.");
            return JsonSerializer.SerializeToElement(new { message=new { content } });
        }
        catch(HttpRequestException) { throw new InvalidOperationException("No puedo conectar con NVIDIA. Comprueba tu conexión a Internet e intenta de nuevo."); }
        catch(OperationCanceledException) when(!cancellation.IsCancellationRequested) { throw new TimeoutException("NVIDIA tardó demasiado. Reintenta cuando el servicio esté disponible."); }
        catch(Exception ex) when(ex is JsonException or KeyNotFoundException or IndexOutOfRangeException) { throw new InvalidOperationException("NVIDIA devolvió una respuesta que no pude interpretar."); }
    }
    public async Task TestNvidiaAsync(CancellationToken cancellation=default)
    {
        _=await RequestNvidiaAsync(new { messages=new[] { new { role="user",content="Responde únicamente: OK" } },options=new { num_predict=128,temperature=0 } },cancellation);
    }
}
