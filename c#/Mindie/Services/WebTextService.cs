using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Mindie.Services;

public sealed record WebPageText(string Text,string Url,string Title,long DownloadedBytes=0,int CleanupVersion=0,string OriginalText="");

/// <summary>Downloads one HTML document and parses it without a browser, scripts or asset loaders.</summary>
public sealed class WebTextService:IDisposable
{
    public const int MaximumBytes=5_000_000;
    public const int MaximumTextCharacters=250_000;
    private readonly HttpClient _client;
    public WebTextService():this(new HttpClientHandler { AutomaticDecompression=DecompressionMethods.All,AllowAutoRedirect=false,UseCookies=false }) { }
    internal WebTextService(HttpMessageHandler handler)
    {
        _client=new(handler) { Timeout=Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("Mindie/0.8 (Windows; text reader)");
        _client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,text/plain;q=0.9");
    }
    public async Task<WebPageText> ReadAsync(string address,CancellationToken cancellation)
    {
        var url=LibraryStore.ValidateSourceUrl(address); if (url.Length==0) throw new ArgumentException("Pega el enlace de la página que quieres leer.");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            for (var redirect=0;redirect<=5;redirect++)
            {
                using var response=await _client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    var location=response.Headers.Location??throw new InvalidDataException("La página no indica adónde redirige.");
                    url=LibraryStore.ValidateSourceUrl(new Uri(new Uri(url),location).AbsoluteUri); continue;
                }
                if (!response.IsSuccessStatusCode) throw new InvalidDataException($"La página respondió con código {(int)response.StatusCode}. Puedes pegar el texto copiado de tu navegador.");
                var type=response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
                if (type is not null && type!="text/html" && type!="application/xhtml+xml" && type!="text/plain") throw new InvalidDataException("Ese enlace no devuelve una página de texto. Para un PDF usa Añadir PDF; también puedes pegar texto del navegador.");
                if (response.Content.Headers.ContentLength>MaximumBytes) throw new InvalidDataException("La página supera los 5 MB de HTML. Pega solo el artículo o usa una selección de pantalla.");
                await using var stream=await response.Content.ReadAsStreamAsync(deadline.Token); using var buffer=new MemoryStream();
                var chunk=new byte[16_384]; int count;
                while ((count=await stream.ReadAsync(chunk,deadline.Token))>0)
                {
                    if (buffer.Length+count>MaximumBytes) throw new InvalidDataException("La página supera los 5 MB de HTML. Pega solo el artículo o usa una selección de pantalla.");
                    buffer.Write(chunk,0,count);
                }
                var bytes=buffer.ToArray(); var charset=response.Content.Headers.ContentType?.CharSet;
                return await Task.Run(async()=> {
                    var encoding=GetEncoding(bytes,charset);
                    using var reader=new StreamReader(new MemoryStream(bytes),encoding,true);
                    var source=await reader.ReadToEndAsync(deadline.Token);
                    if (type=="text/plain") return FromPlainText(source,url,new Uri(url).Host,bytes.Length);
                    return await ExtractAsync(source,url,bytes.Length,deadline.Token);
                },deadline.Token);
            }
            throw new InvalidDataException("El enlace redirige demasiadas veces. Pega la dirección final o el texto del artículo.");
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new TimeoutException("La página tardó demasiado en responder. Vuelve a intentar o pega el texto del navegador."); }
        catch (HttpRequestException ex) { throw new InvalidDataException("No pude descargar el texto de esa dirección. Revisa el enlace o pega el contenido del navegador.",ex); }
    }
    private static Encoding GetEncoding(byte[] bytes,string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset)) charset=Regex.Match(Encoding.ASCII.GetString(bytes,0,Math.Min(bytes.Length,4096)),@"charset\s*=\s*[""']?\s*([a-zA-Z0-9_-]+)",RegexOptions.IgnoreCase).Groups[1].Value;
        if (string.IsNullOrWhiteSpace(charset)) return Encoding.UTF8;
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(charset.Trim('"','\'')); } catch (ArgumentException) { return Encoding.UTF8; }
    }
    internal static async Task<WebPageText> ExtractAsync(string html,string url,long bytes=0,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var document=await new HtmlParser().ParseDocumentAsync(html,cancellation);
        var title=document.QuerySelector("meta[property='og:title']")?.GetAttribute("content")??document.Title;
        foreach (var unwanted in document.QuerySelectorAll("script,style,noscript,template,nav,aside,form,button,svg,canvas,iframe,video,audio,[hidden],[aria-hidden='true'],[role='navigation'],[role='banner'],[role='contentinfo'],body > header,body > footer").ToArray()) unwanted.Remove();
        foreach (var element in document.QuerySelectorAll("[style]").ToArray())
            if (Regex.IsMatch(element.GetAttribute("style")??"",@"(?:display\s*:\s*none|visibility\s*:\s*hidden)",RegexOptions.IgnoreCase)) element.Remove();
        var articles=document.QuerySelectorAll("[itemprop='articleBody'],article").OrderByDescending(e=>e.TextContent.Length).ToArray();
        var root=articles.FirstOrDefault(e=>e.TextContent.Trim().Length>=120)??document.QuerySelector("main,[role='main']")??document.Body;
        if (root is null) throw new InvalidDataException("No encontré texto legible. Pega el contenido del navegador.");
        var heading=root.QuerySelector("h1")?.TextContent;
        if (!string.IsNullOrWhiteSpace(heading)) title=heading;
        var builder=new StringBuilder();
        var pending=new Stack<(INode Node,bool Exit)>(); pending.Push((root,false)); var visited=0;
        while (pending.Count>0)
        {
            if (++visited%256==0) cancellation.ThrowIfCancellationRequested();
            var (node,exit)=pending.Pop();
            if (builder.Length>MaximumTextCharacters*4) throw new InvalidDataException("El texto es demasiado largo. Pega solo la parte que quieres guardar.");
            if (node is IText text) { builder.Append(text.Data); continue; }
            if (node is not IElement element) continue;
            var tag=element.LocalName;
            var block=tag is "p" or "div" or "section" or "article" or "main" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "li" or "ul" or "ol" or "blockquote" or "pre" or "table" or "tr";
            if (exit) { if (block) builder.Append('\n'); else if (tag is "td" or "th") builder.Append(" | "); continue; }
            if (block || tag=="br") builder.Append('\n'); if (tag=="li") builder.Append("• ");
            pending.Push((element,true));
            for (var i=element.ChildNodes.Length-1;i>=0;i--) pending.Push((element.ChildNodes[i],false));
        }
        var normalized=string.Join("\n\n",builder.ToString().Split('\n').Select(CleanLine).Where(line=>line.Length>0));
        return FromPlainText(normalized,url,CleanLine(title??""),bytes);
    }
    private static string CleanLine(string value)=>Regex.Replace(value,@"\s+"," ").Trim();
    public static WebPageText FromPlainText(string text,string url,string title,long bytes=0)
    {
        text=text.Trim();
        if (text.Length<80) throw new InvalidDataException("Encontré muy poco texto. La página puede necesitar JavaScript o iniciar sesión. Pega el artículo copiado de tu navegador o usa una captura.");
        if (text.Length>MaximumTextCharacters) throw new InvalidDataException("El artículo supera los 250 000 caracteres. Pega solo la parte que quieres conservar.");
        url=LibraryStore.ValidateSourceUrl(url);
        title=string.IsNullOrWhiteSpace(title)?(url.Length>0?new Uri(url).Host:"Texto de una página web"):CleanLine(title);
        return new(text,url,title[..Math.Min(title.Length,500)],bytes);
    }
    public void Dispose()=>_client.Dispose();
}
