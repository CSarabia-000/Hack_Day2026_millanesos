namespace Mindie.Services;

public sealed partial class LibraryStore
{
    private readonly SemaphoreSlim _writeGate = new(1);
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    public string Root { get; }
    public LibraryData Data { get; private set; } = new();
    public string CapturePath(string id) { if (!Guid.TryParse(id, out _)) throw new ArgumentException("Recuerdo inválido."); return Path.Combine(Root, "captures", id + ".png"); }
    public LibraryStore(string? root = null) { Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mindie"); }
    public async Task LoadAsync()
    {
        Directory.CreateDirectory(Path.Combine(Root, "captures"));
        Directory.CreateDirectory(Path.Combine(Root, "documents"));
        var path = Path.Combine(Root, "library.json");
        if (!File.Exists(path)) return;
        try
        {
            Data = await Task.Run(async()=> {
                await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous);
                return await JsonSerializer.DeserializeAsync<LibraryData>(stream,_json)??new LibraryData();
            });
            var resizeLegacyWidget = Data.Version < 3;
            var introduceMindie = Data.Version < 5;
            if (resizeLegacyWidget) Data.Settings.PetSize *= 0.8;
            await LoadCustomPetsAsync();
            if(introduceMindie) { Data.Settings.Pet="mindie"; Data.Settings.PetName="Mindie"; }
            ValidateSettings(Data.Settings);
            Data.Memories = Data.Memories.Where(m => Guid.TryParse(m.Id, out _)).ToList();
            Data.GraphPositions ??=[];
            var memoryIds=Data.Memories.Select(m=>m.Id).ToHashSet(StringComparer.Ordinal);
            foreach(var pair in Data.GraphPositions.ToArray())
                if(pair.Value is null || !memoryIds.Contains(pair.Key) || !double.IsFinite(pair.Value.X) || !double.IsFinite(pair.Value.Y) || pair.Value.Z is double z && !double.IsFinite(z)) Data.GraphPositions.Remove(pair.Key);
            Data.Messages = Data.Messages.Where(m => m.Role is "user" or "assistant").TakeLast(100).ToList();
            foreach(var message in Data.Messages) message.Images=(message.Images??[]).Where(ChatImageService.Valid).Take(ChatImageService.MaximumCount).ToList();
            var migrateConversations=Data.Version<4; EnsureConversations();
            if (resizeLegacyWidget || migrateConversations || introduceMindie) { Data.Version = 5; await SaveAsync(); }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException) { File.Copy(path, path + ".backup-" + DateTime.Now.ToString("yyyyMMddHHmmss"), false); throw new InvalidDataException("No pude leer la biblioteca. Conservé una copia para recuperarla; tus archivos originales siguen disponibles.", ex); }
    }
    public static Uri ValidateEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "http" || !uri.IsLoopback || uri.HostNameType == UriHostNameType.Unknown || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) throw new ArgumentException("Usa una dirección local de Ollama, como http://127.0.0.1:11434.");
        return uri;
    }
    public static void ValidateSettings(Settings s)
    {
        if (s.Provider is not ("ollama" or "nvidia")) throw new ArgumentException("Selecciona Ollama local o NVIDIA API.");
        ValidateEndpoint(s.Endpoint);
        if (!Pets.IsKnown(s.Pet)) throw new ArgumentException("Selecciona una mascota válida.");
        s.PetName = s.PetName.Trim(); if (s.PetName.Length is 0 or > 24) throw new ArgumentException("El nombre debe tener entre 1 y 24 caracteres.");
        s.Model = s.Model.Trim(); if (s.Model.Length is 0 or > 150) throw new ArgumentException("Selecciona un modelo de Ollama.");
        if (!double.IsFinite(s.PetSize)) s.PetSize = Settings.DefaultPetSize;
        s.PetSize = Math.Clamp(s.PetSize, Settings.MinPetSize, Settings.MaxPetSize);
        if (s.PetLeft.HasValue && !double.IsFinite(s.PetLeft.Value)) s.PetLeft = null;
        if (s.PetTop.HasValue && !double.IsFinite(s.PetTop.Value)) s.PetTop = null;
    }
    public static void ValidateMemory(MemoryItem memory)
    {
        if (memory.SourceText.Length>WebTextService.MaximumTextCharacters || memory.OriginalSourceText.Length>WebTextService.MaximumTextCharacters) throw new ArgumentException("El texto de origen supera los 250 000 caracteres.");
        memory.SourceUrl=ValidateSourceUrl(memory.SourceUrl);
        memory.Title = memory.Title.Trim(); if (memory.Title.Length is 0 or > 100) throw new ArgumentException("Escribe un título de hasta 100 caracteres.");
        memory.Summary = memory.Summary.Trim(); if (memory.Summary.Length > 1500) throw new ArgumentException("El resumen es demasiado largo.");
        memory.Ideas = memory.Ideas.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()[..Math.Min(s.Trim().Length, 500)]).Take(8).ToList();
        if (!Pets.Categories.Contains(memory.Category)) memory.Category = "Ideas";
    }
    public static string ValidateSourceUrl(string value)
    {
        value=value.Trim(); if (value.Length==0) return "";
        if (!value.Contains("://")) value="https://"+value;
        if (!Uri.TryCreate(value,UriKind.Absolute,out var uri) || (uri.Scheme!="http" && uri.Scheme!="https") || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) || value.Any(char.IsWhiteSpace)) throw new ArgumentException("Escribe un enlace web válido, por ejemplo https://ejemplo.com/articulo.");
        return uri.AbsoluteUri;
    }
    public async Task SaveAsync()
    {
        await _writeGate.WaitAsync();
        try {
            var snapshot=new LibraryData { Version=Data.Version,Settings=Data.Settings.Copy(),Memories=Data.Memories.Select(m=>m.Copy()).ToList(),Messages=Data.Messages.Select(m=>new ChatMessage { Role=m.Role,Content=m.Content,CreatedAt=m.CreatedAt,Images=[..m.Images] }).ToList(),CustomPets=Data.CustomPets.Select(p=>new CustomPet { Id=p.Id,Name=p.Name,Description=p.Description }).ToList(),GraphPositions=new(Data.GraphPositions) };
            snapshot.Conversations=Data.Conversations.Select(CopyConversation).ToList(); snapshot.ActiveConversationId=Data.ActiveConversationId;
            await WriteJsonAsync(Path.Combine(Root,"library.json"),snapshot);
        }
        finally { _writeGate.Release(); }
    }
    private Task WriteJsonAsync<T>(string path,T snapshot)=>Task.Run(async()=> {
        try {
            await using(var stream=new FileStream(path+".tmp",FileMode.Create,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous)) await JsonSerializer.SerializeAsync(stream,snapshot,_json);
            File.Move(path+".tmp",path,true);
        }
        catch { try { File.Delete(path+".tmp"); } catch(IOException) { } catch(UnauthorizedAccessException) { } throw; }
    });
    public async Task AddAsync(MemoryItem item, byte[] png)
    {
        ValidateMemory(item); if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new ArgumentException("La captura no es un PNG válido.");
        await File.WriteAllBytesAsync(CapturePath(item.Id), png); Data.Memories.Insert(0, item);
        try { await SaveAsync(); } catch { Data.Memories.Remove(item); File.Delete(CapturePath(item.Id)); throw; }
    }
    public async Task DeleteAsync(MemoryItem item)
    {
        var index = Data.Memories.IndexOf(item); if (index < 0) return; Data.Memories.RemoveAt(index);
        var hadPosition=Data.GraphPositions.Remove(item.Id,out var position);
        try { await SaveAsync(); } catch { Data.Memories.Insert(index, item); if(hadPosition) Data.GraphPositions[item.Id]=position!; throw; }
        File.Delete(CapturePath(item.Id));
        if (item.IsPdf && Directory.Exists(DocumentDirectory(item.Id))) Directory.Delete(DocumentDirectory(item.Id),true);
    }
    public async Task ExportAsync(string path)
    {
        var memories=Data.Memories.Select(m=>m.Copy()).ToArray();
        await Task.Run(async()=> {
            var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                await using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous)) {
                    using var writer=new Utf8JsonWriter(stream,new JsonWriterOptions { Indented=true });
                    writer.WriteStartObject(); writer.WriteNumber("version",3); writer.WriteString("exportedAt",DateTimeOffset.Now); writer.WriteStartArray("memories");
                    foreach(var m in memories) {
                        var image=File.Exists(CapturePath(m.Id))?await File.ReadAllBytesAsync(CapturePath(m.Id)):Array.Empty<byte>();
                        var pdf=m.IsPdf?await File.ReadAllBytesAsync(DocumentPath(m.Id)):null;
                        var knowledge=m.IsPdf?await ReadKnowledgeAsync(m.Id):null;
                        JsonSerializer.Serialize(writer,new { m.Id,m.Title,m.Summary,m.Ideas,m.Category,m.Favorite,m.CreatedAt,m.Kind,m.SourceName,m.PageCount,m.UserContext,m.SourceUrl,m.SourceText,m.OriginalSourceText,m.WebTextCleanupVersion,image,pdf,knowledge },_json);
                        await writer.FlushAsync();
                    }
                    writer.WriteEndArray(); writer.WriteEndObject(); await writer.FlushAsync();
                }
                File.Move(temporary,path,true);
            }
            catch { try { File.Delete(temporary); } catch(IOException) { } catch(UnauthorizedAccessException) { } throw; }
        });
    }
}
