namespace Mindie.Services;

public sealed record PreparedChatImage(ChatImage Image,byte[] Png);

public static class ChatImageService
{
    public const int MaximumCount=8,MaximumSide=1600,MaximumFileBytes=20_000_000,MaximumBatchBytes=32_000_000;
    internal static bool Valid(ChatImage? image)=>image is not null && Guid.TryParseExact(image.Id,"N",out _) && !string.IsNullOrWhiteSpace(image.Name) && image.Name.Length<=260 && image.Width is >0 and <=MaximumSide && image.Height is >0 and <=MaximumSide;
    internal static PreparedChatImage Prepare(BitmapSource source,string name)
    {
        BitmapSource image=source;
        var scale=Math.Min(1,MaximumSide/(double)Math.Max(source.PixelWidth,source.PixelHeight));
        if(scale<1) { var resized=new TransformedBitmap(source,new ScaleTransform(scale,scale)); resized.Freeze(); image=resized; }
        var png=Images.Encode(image); if(png.Length>MaximumBatchBytes) throw new ArgumentException("La captura es demasiado grande para enviarla.");
        return new(new(Guid.NewGuid().ToString("N"),name,image.PixelWidth,image.PixelHeight),png);
    }
    public static Task<IReadOnlyList<PreparedChatImage>> PrepareAsync(IReadOnlyList<string> paths,CancellationToken cancellation=default)=>Task.Run<IReadOnlyList<PreparedChatImage>>(()=> {
        if(paths.Count is 0 or >MaximumCount) throw new ArgumentException($"Puedes adjuntar de 1 a {MaximumCount} imágenes.");
        var result=new List<PreparedChatImage>(); var total=0;
        foreach(var path in paths) {
            cancellation.ThrowIfCancellationRequested();
            var file=new FileInfo(path); if(!file.Exists || file.Length>MaximumFileBytes) throw new ArgumentException("Elige imágenes de hasta 20 MB cada una.");
            try {
                using var stream=file.OpenRead(); var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None); var frame=decoder.Frames[0];
                var width=frame.PixelWidth; var height=frame.PixelHeight;
                if(width<1 || height<1 || (long)width*height>40_000_000) throw new ArgumentException("La imagen supera 40 millones de píxeles. Usa una copia más pequeña.");
                var orientation=1;
                try { if(frame.Metadata is BitmapMetadata metadata && metadata.ContainsQuery("/app1/ifd/{ushort=274}")) orientation=Convert.ToInt32(metadata.GetQuery("/app1/ifd/{ushort=274}")); } catch(Exception ex) when(ex is NotSupportedException or ArgumentException) { }
                stream.Position=0; var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad; image.StreamSource=stream;
                if(width>=height) image.DecodePixelWidth=Math.Min(MaximumSide,width); else image.DecodePixelHeight=Math.Min(MaximumSide,height);
                image.EndInit(); image.Freeze(); BitmapSource prepared=image;
                var transform=new TransformGroup();
                if(orientation is 2 or 5 or 7) transform.Children.Add(new ScaleTransform(-1,1));
                if(orientation==4) transform.Children.Add(new ScaleTransform(1,-1));
                var angle=orientation switch { 3=>180,5 or 8=>270,6 or 7=>90,_=>0 };
                if(angle!=0) transform.Children.Add(new RotateTransform(angle));
                if(transform.Children.Count>0) { transform.Freeze(); var rotated=new TransformedBitmap(image,transform); rotated.Freeze(); prepared=rotated; }
                cancellation.ThrowIfCancellationRequested(); var png=Images.Encode(prepared); total=checked(total+png.Length);
                if(total>MaximumBatchBytes) throw new ArgumentException("Las imágenes preparadas superan 32 MB. Adjunta menos imágenes.");
                result.Add(new(new(Guid.NewGuid().ToString("N"),file.Name,prepared.PixelWidth,prepared.PixelHeight),png));
            }
            catch(Exception ex) when(ex is FileFormatException or NotSupportedException or InvalidOperationException) { throw new ArgumentException($"No pude leer «{file.Name}». Usa una imagen PNG, JPG, BMP, GIF o TIFF válida.",ex); }
        }
        return result;
    },cancellation);
}
