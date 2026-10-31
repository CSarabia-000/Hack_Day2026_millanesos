namespace Mindie.Services;

// Captures only metadata used by a view. Source documents and captured pixels stay out of UI comparisons.
internal sealed class MemorySnapshot(IReadOnlyList<MemoryItem> items)
{
    private readonly record struct Metadata(string Id,string Title,string Summary,string Category,DateTimeOffset CreatedAt,string[] Ideas,bool Favorite,string Kind,int PageCount,string SourceName,string UserContext,string SourceUrl);
    private readonly Metadata[] _items=items.Select(m=>new Metadata(m.Id,m.Title,m.Summary,m.Category,m.CreatedAt,m.Ideas.ToArray(),m.Favorite,m.Kind,m.PageCount,m.SourceName,m.UserContext,m.SourceUrl)).ToArray();
    internal bool Matches(IReadOnlyList<MemoryItem> items,bool graphOnly=false)
    {
        if(items.Count!=_items.Length) return false;
        for(var i=0;i<items.Count;i++) {
            var a=_items[i]; var b=items[i];
            if(a.Id!=b.Id || a.Title!=b.Title || a.Summary!=b.Summary || a.Category!=b.Category || a.CreatedAt!=b.CreatedAt || !a.Ideas.SequenceEqual(b.Ideas)) return false;
            if(!graphOnly && (a.Favorite!=b.Favorite || a.Kind!=b.Kind || a.PageCount!=b.PageCount || a.SourceName!=b.SourceName || a.UserContext!=b.UserContext || a.SourceUrl!=b.SourceUrl)) return false;
        }
        return true;
    }
}
