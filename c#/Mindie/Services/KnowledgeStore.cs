namespace Mindie.Services;

public sealed partial class LibraryStore
{
    public string DocumentDirectory(string id)
    {
        if (!Guid.TryParse(id,out _)) throw new ArgumentException("Documento inválido.");
        return Path.Combine(Root,"documents",id);
    }
    public string DocumentPath(string id) => Path.Combine(DocumentDirectory(id),"original.pdf");
    public async Task<PdfKnowledge> ReadKnowledgeAsync(string id)
    {
        var json = await File.ReadAllTextAsync(Path.Combine(DocumentDirectory(id),"knowledge.json"));
        return JsonSerializer.Deserialize<PdfKnowledge>(json,_json) ?? throw new InvalidDataException("No pude leer el contenido de este PDF.");
    }
    public async Task SaveKnowledgeAsync(string id, PdfKnowledge knowledge)
    {
        await _writeGate.WaitAsync();
        try
        {
            var snapshot=new PdfKnowledge { FileName=knowledge.FileName,Pages=knowledge.Pages.Select(p=>new PdfPageText { Number=p.Number,Text=p.Text,NeedsVision=p.NeedsVision }).ToList(),Messages=knowledge.Messages.Select(m=>new ChatMessage { Role=m.Role,Content=m.Content,CreatedAt=m.CreatedAt,Images=[..m.Images] }).ToList() };
            await WriteJsonAsync(Path.Combine(DocumentDirectory(id),"knowledge.json"),snapshot);
        }
        finally { _writeGate.Release(); }
    }
    public async Task AddPdfAsync(MemoryItem item, PreparedPdf prepared)
    {
        if (!item.IsPdf) throw new ArgumentException("La tarjeta debe ser de tipo PDF.");
        var directory = DocumentDirectory(item.Id);
        if (Directory.Exists(directory)) throw new InvalidOperationException("El PDF ya existe en la biblioteca.");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllBytesAsync(DocumentPath(item.Id),prepared.Original);
            await SaveKnowledgeAsync(item.Id,prepared.Knowledge);
            await AddAsync(item,prepared.Cover);
        }
        catch
        {
            File.Delete(CapturePath(item.Id)); Directory.Delete(directory,true); throw;
        }
    }
}
