namespace Mindie.Services;

internal static class ThumbnailCache
{
    private sealed record Entry(string Path,long Length,long Modified,BitmapSource Image);
    private static readonly LinkedList<Entry> Recent=[];
    private static readonly Dictionary<string,LinkedListNode<Entry>> Entries=new(StringComparer.OrdinalIgnoreCase);
    internal const int Capacity=48,MaximumSide=256;
    internal static BitmapSource? Load(string path)
    {
        try {
            var file=new FileInfo(path); if(!file.Exists) return null;
            if(Entries.TryGetValue(path,out var node)) {
                Recent.Remove(node); Entries.Remove(path);
                if(node.Value.Length==file.Length && node.Value.Modified==file.LastWriteTimeUtc.Ticks) { Recent.AddFirst(node); Entries[path]=node; return node.Value.Image; }
            }
            using var stream=file.OpenRead(); var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);
            var frame=decoder.Frames[0]; var width=frame.PixelWidth; var height=frame.PixelHeight; stream.Position=0;
            var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad; image.StreamSource=stream;
            if(width>=height) image.DecodePixelWidth=Math.Min(MaximumSide,width); else image.DecodePixelHeight=Math.Min(MaximumSide,height);
            image.EndInit(); image.Freeze();
            var entry=new Entry(path,file.Length,file.LastWriteTimeUtc.Ticks,image); Entries[path]=Recent.AddFirst(entry);
            if(Recent.Count>Capacity) { Entries.Remove(Recent.Last!.Value.Path); Recent.RemoveLast(); }
            return image;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException) { return null; }
    }
}
