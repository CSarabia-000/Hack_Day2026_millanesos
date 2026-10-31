namespace Mindie;

public sealed partial class AppHost
{
    private readonly HashSet<string> _deletingMemories=[];
    public async Task DeleteMemoryAsync(MemoryItem item)
    {
        if (IsBusy) throw new InvalidOperationException("Termina o cancela la operación actual antes de eliminar un recuerdo.");
        if (!_deletingMemories.Add(item.Id)) return;
        try
        {
            var current=Store.Data.Memories.FirstOrDefault(m=>m.Id==item.Id);
            if (current is null) return;
            await Store.DeleteAsync(current);
            Report("Recuerdo eliminado.");
        }
        finally { _deletingMemories.Remove(item.Id); Refresh(); }
    }
}
