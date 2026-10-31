namespace Mindie.Services;

public sealed partial class LibraryStore
{
    public string ChatImagePath(string id)
    {
        if(!Guid.TryParseExact(id,"N",out _)) throw new ArgumentException("Imagen de conversación inválida.");
        return Path.Combine(Root,"chat-images",id+".png");
    }
    internal async Task SaveChatImagesAsync(IReadOnlyList<PreparedChatImage> images,CancellationToken cancellation)
    {
        if(images.Count==0) return;
        if(images.Count>ChatImageService.MaximumCount || images.Sum(i=>(long)i.Png.Length)>ChatImageService.MaximumBatchBytes) throw new ArgumentException("Hay demasiadas imágenes o son demasiado grandes.");
        foreach(var image in images) if(!ChatImageService.Valid(image.Image) || image.Png.Length<8 || !image.Png.AsSpan(0,8).SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 })) throw new ArgumentException("La imagen preparada no es válida.");
        Directory.CreateDirectory(Path.Combine(Root,"chat-images")); var written=new List<ChatImage>();
        try {
            foreach(var image in images) {
                var path=ChatImagePath(image.Image.Id);
                await using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous);
                written.Add(image.Image); await stream.WriteAsync(image.Png,cancellation);
            }
        }
        catch { DeleteChatImages(written); throw; }
    }
    internal Task<byte[]> ReadChatImageAsync(ChatImage image,CancellationToken cancellation)=>File.ReadAllBytesAsync(ChatImagePath(image.Id),cancellation);
    internal void DeleteChatImages(IEnumerable<ChatImage> images)
    {
        foreach(var image in images.DistinctBy(i=>i.Id)) {
            try { File.Delete(ChatImagePath(image.Id)); } catch(IOException) { } catch(UnauthorizedAccessException) { }
        }
    }
}
