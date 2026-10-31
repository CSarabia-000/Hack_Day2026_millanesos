namespace Mindie.Services;

public static partial class CaptureImages
{
    public static byte[] JoinVertical(IReadOnlyList<byte[]> parts)
    {
        if (parts.Count==0) throw new ArgumentException("No hay imágenes de la página.");
        if (parts.Count==1) return parts[0];
        var sizes=parts.Select(p=> {
            using var stream=new MemoryStream(p,false);
            var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None); var frame=decoder.Frames[0];
            return (Width:frame.PixelWidth,Height:frame.PixelHeight);
        }).ToArray(); var width=sizes[0].Width; var height=sizes.Sum(p=>p.Height);
        FullPageCaptureService.ValidateSize(width,height);
        if (sizes.Any(p=>p.Width!=width)) throw new InvalidDataException("La página cambió de ancho durante la captura.");
        var image=new WriteableBitmap(width,height,96,96,PixelFormats.Bgra32,null); var y=0;
        foreach (var bytes in parts)
        {
            var part=Images.Decode(bytes);
            var source=new FormatConvertedBitmap(part,PixelFormats.Bgra32,null,0); var stride=width*4; var pixels=new byte[stride*source.PixelHeight]; source.CopyPixels(pixels,stride,0);
            image.WritePixels(new(0,y,width,source.PixelHeight),pixels,stride,0); y+=source.PixelHeight;
        }
        image.Freeze(); return Images.Encode(image);
    }
    public static List<byte[]> SplitVertical(byte[] png,CancellationToken cancellation=default)
    {
        var source=Images.Decode(png); var parts=new List<byte[]>();
        for (var y=0;y<source.PixelHeight;y+=FullPageCaptureService.PartHeight)
        {
            cancellation.ThrowIfCancellationRequested(); var part=new CroppedBitmap(source,new(0,y,source.PixelWidth,Math.Min(FullPageCaptureService.PartHeight,source.PixelHeight-y))); part.Freeze(); parts.Add(Images.Encode(part));
        }
        return parts;
    }
}
