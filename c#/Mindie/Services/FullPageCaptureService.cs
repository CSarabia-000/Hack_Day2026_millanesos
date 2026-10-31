using Microsoft.Web.WebView2.Core;

namespace Mindie.Services;

public sealed record WebPageCapture(byte[] Png,string Url,string Title);
public sealed class FullPageCaptureService
{
    public const long MaximumPixels=64_000_000;
    public const int PartHeight=1800;
    private readonly CoreWebView2 _browser;
    public FullPageCaptureService(CoreWebView2 browser) { _browser=browser; }
    private async Task<JsonElement> EvaluateAsync(string expression,CancellationToken cancellation)
    {
        var result=await _browser.CallDevToolsProtocolMethodAsync("Runtime.evaluate",JsonSerializer.Serialize(new { expression,awaitPromise=true,returnByValue=true })).WaitAsync(cancellation);
        using var json=JsonDocument.Parse(result);
        if (json.RootElement.TryGetProperty("exceptionDetails",out _)) throw new InvalidOperationException("La página cambió durante la captura. Vuelve a intentarlo.");
        return json.RootElement.GetProperty("result").GetProperty("value").Clone();
    }
    internal static void ValidateSize(int width,int height)
    {
        if (width<=0 || height<=0 || width>4096 || (long)width*height>MaximumPixels) throw new InvalidOperationException("La página es demasiado grande para capturarla completa. Usa una página más corta o la captura de un área.");
    }
    public async Task<WebPageCapture> CaptureAsync(CancellationToken cancellation,IProgress<string>? progress=null)
    {
        cancellation.ThrowIfCancellationRequested();
        var url=LibraryStore.ValidateSourceUrl(_browser.Source); if (url.Length==0) throw new InvalidOperationException("Abre una página web antes de capturar.");
        var title=_browser.DocumentTitle;
        var original=await EvaluateAsync("({x:window.scrollX,y:window.scrollY,width:window.innerWidth,height:window.innerHeight})",cancellation);
        try
        {
            await _browser.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride",JsonSerializer.Serialize(new { width=original.GetProperty("width").GetInt32(),height=original.GetProperty("height").GetInt32(),deviceScaleFactor=1,mobile=false })).WaitAsync(cancellation);
            progress?.Report("Preparando las imágenes y el contenido de la página…");
            await EvaluateAsync("Promise.race([document.fonts?.ready??Promise.resolve(),new Promise(r=>setTimeout(r,1500))]).then(()=>true)",cancellation);
            await EvaluateAsync("(()=>{document.querySelectorAll('img[loading=lazy]').forEach(i=>i.loading='eager');return true})()",cancellation);
            // Visit each viewport so lazy content can load. Infinite pages fail explicitly at the size limit.
            var step=Math.Max(100,(int)(original.GetProperty("height").GetDouble()*.8));
            for (var y=0;;y+=step)
            {
                var dimensions=await EvaluateAsync("({width:Math.max(document.documentElement.scrollWidth,document.body?.scrollWidth??0),height:Math.max(document.documentElement.scrollHeight,document.body?.scrollHeight??0)})",cancellation);
                var width=(int)Math.Ceiling(dimensions.GetProperty("width").GetDouble()); var height=(int)Math.Ceiling(dimensions.GetProperty("height").GetDouble()); ValidateSize(width,height);
                if (y>=height) break;
                progress?.Report($"Preparando contenido · {Math.Min(y+(int)original.GetProperty("height").GetDouble(),height)} de {height} px…");
                await EvaluateAsync($"(()=>{{window.scrollTo({{top:{y},left:0,behavior:'instant'}});return true}})()",cancellation);
                await Task.Delay(100,cancellation);
            }
            await Task.Delay(350,cancellation);
            using var metrics=JsonDocument.Parse(await _browser.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics","{}").WaitAsync(cancellation));
            var size=metrics.RootElement.GetProperty("cssContentSize"); var totalWidth=(int)Math.Ceiling(size.GetProperty("width").GetDouble()); var totalHeight=(int)Math.Ceiling(size.GetProperty("height").GetDouble()); ValidateSize(totalWidth,totalHeight);
            var parts=new List<byte[]>();
            for (var y=0;y<totalHeight;y+=PartHeight)
            {
                cancellation.ThrowIfCancellationRequested(); progress?.Report($"Capturando la página completa · parte {parts.Count+1} de {(totalHeight+PartHeight-1)/PartHeight}…");
                var result=await _browser.CallDevToolsProtocolMethodAsync("Page.captureScreenshot",JsonSerializer.Serialize(new { format="png",fromSurface=true,captureBeyondViewport=true,clip=new { x=0,y,width=totalWidth,height=Math.Min(PartHeight,totalHeight-y),scale=1 } })).WaitAsync(cancellation);
                using var json=JsonDocument.Parse(result); parts.Add(Convert.FromBase64String(json.RootElement.GetProperty("data").GetString()!));
            }
            cancellation.ThrowIfCancellationRequested();
            if (new Uri(_browser.Source).GetLeftPart(UriPartial.Query)!=new Uri(url).GetLeftPart(UriPartial.Query)) throw new InvalidOperationException("La página cambió de dirección durante la captura. Vuelve a capturar la página actual.");
            return new(CaptureImages.JoinVertical(parts),url,title);
        }
        finally
        {
            try { await _browser.CallDevToolsProtocolMethodAsync("Emulation.clearDeviceMetricsOverride","{}"); await EvaluateAsync($"(()=>{{window.scrollTo({original.GetProperty("x").GetDouble().ToString(CultureInfo.InvariantCulture)},{original.GetProperty("y").GetDouble().ToString(CultureInfo.InvariantCulture)});return true}})()",CancellationToken.None); } catch { }
        }
    }
}
