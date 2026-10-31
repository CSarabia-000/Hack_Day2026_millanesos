namespace Mindie.Services;

public sealed record LocalModelInfo(string Name,string[] Capabilities,bool CapabilitiesKnown=true,bool Installed=true)
{
    public bool CanChat=>Installed && (!CapabilitiesKnown || Capabilities.Contains("completion"));
    public string CapabilityText=>!Installed?"No está instalado en este servidor":!CapabilitiesKnown?"Capacidades no disponibles":Capabilities.Length==0?"Sin capacidades reportadas":string.Join(" · ",Capabilities.Select(c=>c switch {
        "completion"=>"Texto","vision"=>"Imágenes","audio"=>"Audio","thinking"=>"Razonamiento","tools"=>"Herramientas","embedding"=>"Embeddings","insert"=>"Inserción",_=>c
    }));
}

public sealed partial class OllamaService
{
    public async Task<IReadOnlyList<LocalModelInfo>> ListLocalModelsAsync(Settings settings,CancellationToken cancellation=default)
    {
        var response=await RequestAsync(settings,"tags",null,cancellation);
        var result=new List<LocalModelInfo>();
        foreach(var model in response.GetProperty("models").EnumerateArray()) {
            cancellation.ThrowIfCancellationRequested();
            var name=model.GetProperty("name").GetString();
            if(string.IsNullOrWhiteSpace(name) || name.EndsWith(":cloud",StringComparison.OrdinalIgnoreCase) || (model.TryGetProperty("remote_host",out var remote) && remote.ValueKind==JsonValueKind.String && !string.IsNullOrEmpty(remote.GetString()))) continue;
            try {
                var info=model.TryGetProperty("capabilities",out _)?model:await RequestAsync(settings,"show",new { model=name },cancellation);
                result.Add(new(name,ReadCapabilities(info),info.TryGetProperty("capabilities",out _)));
            }
            catch(Exception ex) when(ex is InvalidOperationException or TimeoutException) { result.Add(new(name,[],false)); }
        }
        return result.DistinctBy(m=>m.Name).OrderBy(m=>m.Name,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static string[] ReadCapabilities(JsonElement model)=>model.TryGetProperty("capabilities",out var caps) && caps.ValueKind==JsonValueKind.Array?caps.EnumerateArray().Where(c=>c.ValueKind==JsonValueKind.String).Select(c=>c.GetString()!).Distinct().ToArray():[];
}
