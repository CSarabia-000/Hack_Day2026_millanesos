using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using SkiaSharp;

namespace Mindie.Services;

public sealed record PreparedPdf(byte[] Original, byte[] Cover, PdfKnowledge Knowledge);

public sealed class PdfService
{
    public static bool IsPdfDrop(string[] files) => files.Length == 1 && string.Equals(Path.GetExtension(files[0]),".pdf",StringComparison.OrdinalIgnoreCase) && File.Exists(files[0]);
    public async Task<PreparedPdf> ReadAsync(string path, OllamaService ollama, Settings settings, CancellationToken cancellation, IProgress<string>? progress = null)
    {
        if (!IsPdfDrop([path])) throw new ArgumentException("Arrastra un único archivo PDF a la mascota.");
        var file = new FileInfo(path);
        if (file.Length > 100_000_000) throw new ArgumentException("El PDF supera 100 MB. Usa un documento de menor tamaño.");
        var bytes = await File.ReadAllBytesAsync(path,cancellation);
        if (!Encoding.ASCII.GetString(bytes.AsSpan(0,Math.Min(1024,bytes.Length))).Contains("%PDF-")) throw new InvalidDataException("El archivo no contiene un PDF válido.");
        progress?.Report("Leyendo las páginas del PDF…");
        var knowledge = await Task.Run(()=>Extract(bytes,Path.GetFileName(path),cancellation),cancellation);
        var cover = await RenderAsync(bytes,0,cancellation);
        foreach (var page in knowledge.Pages.Where(p=>p.NeedsVision))
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report($"Leyendo imágenes y texto escaneado · página {page.Number} de {knowledge.Pages.Count}…");
            var image = page.Number==1 ? cover : await RenderAsync(bytes,page.Number-1,cancellation);
            var visual = await ollama.ReadPdfPageAsync(settings,image,page.Number,cancellation);
            page.Text += "\n[Lectura visual de Gemma; comprueba detalles en el original]\n"+visual;
        }
        return new(bytes,cover,knowledge);
    }
    internal static PdfKnowledge Extract(byte[] bytes, string fileName, CancellationToken cancellation)
    {
        try
        {
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
            var result = new PdfKnowledge { FileName=fileName };
            foreach (var page in pdf.GetPages())
            {
                cancellation.ThrowIfCancellationRequested();
                var text = ContentOrderTextExtractor.GetText(page);
                result.Pages.Add(new() { Number=page.Number,Text=text,NeedsVision=text.Count(char.IsLetterOrDigit)<40 || page.GetImages().Any() });
            }
            if (result.Pages.Count==0) throw new InvalidDataException("El PDF no tiene páginas.");
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not InvalidDataException) { throw new InvalidDataException("No pude leer este PDF. Puede estar dañado o protegido por contraseña.",ex); }
    }
    internal static Task<byte[]> RenderAsync(byte[] pdf, int index, CancellationToken cancellation) => Task.Run(()=>
    {
        cancellation.ThrowIfCancellationRequested();
        using var input = new MemoryStream(pdf,false);
        using var bitmap = PDFtoImage.Conversion.ToImage(input,page:index,options:new PDFtoImage.RenderOptions(Width:1600,Height:1600,WithAspectRatio:true));
        cancellation.ThrowIfCancellationRequested();
        using var data = bitmap.Encode(SKEncodedImageFormat.Png,100); return data.ToArray();
    },cancellation);
}
