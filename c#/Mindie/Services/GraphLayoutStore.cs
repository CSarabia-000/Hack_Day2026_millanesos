namespace Mindie.Services;

public sealed partial class LibraryStore
{
    private readonly SemaphoreSlim _graphGate=new(1);
    public async Task SaveGraphPositionAsync(string id,System.Windows.Media.Media3D.Point3D position,string category)
    {
        await _graphGate.WaitAsync();
        try
        {
            if(!Data.Memories.Any(m=>m.Id==id && m.Category==category) || !double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(position.Z)) return;
            var existed=Data.GraphPositions.TryGetValue(id,out var previous);
            Data.GraphPositions[id]=new(Math.Clamp(position.X/6+.5,-.5,1.5),Math.Clamp(.5-position.Y/4.5,-.5,1.5),category,Math.Clamp(position.Z,-3.8,3.8));
            try { await SaveAsync(); }
            catch { if(existed) Data.GraphPositions[id]=previous!; else Data.GraphPositions.Remove(id); throw; }
        }
        finally { _graphGate.Release(); }
    }
    public async Task ResetGraphPositionsAsync()
    {
        await _graphGate.WaitAsync();
        try {
            var old=Data.GraphPositions; Data.GraphPositions=[];
            try { await SaveAsync(); } catch { Data.GraphPositions=old; throw; }
        }
        finally { _graphGate.Release(); }
    }
}
