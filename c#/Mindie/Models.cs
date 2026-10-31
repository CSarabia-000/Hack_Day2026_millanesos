namespace Mindie;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Notify(name); return true; }
}
public enum PetState { Idle, Move, Think, Drowsy, Sleep, Eat }
public sealed record PetInfo(string Id, string Name, string Nickname, string Description, string Detail);
public static class Pets
{
    public static readonly PetInfo[] All = [
        new("mindie", "Mindie", "Mindie", "Tu burbujita de ideas, siempre cerca.", "Principal · Curioso · Cercano"),
        new("pollito", "Pollito", "Pipo", "Curiosidad en pequeño formato.", "Curioso · Cercano · Optimista"),
        new("cochinito", "Cochinito", "Mochi", "Pausa, piensa y sigue adelante.", "Tranquilo · Atento · Paciente"),
        new("ardilla", "Ardilla", "Nuez", "Cada buena idea tiene su lugar.", "Ingeniosa · Ágil · Organizada"),
        new("perrito", "Perrito", "Toby", "Contigo, de principio a fin.", "Leal · Activo · Compañero"),
        new("gatito", "Gatito", "Miso", "Observa el mundo a tu ritmo.", "Sereno · Curioso · Independiente"),
        new("leon", "León", "Leo", "Un impulso para dar el siguiente paso.", "Valiente · Sereno · Constante"),
        new("cocodrilo", "Cocodrilo", "Coco", "Que ninguna idea se escape.", "Divertido · Paciente · Creativo")
    ];
    private static readonly Dictionary<string,PetInfo> Custom=[];
    public static PetInfo Find(string id) => All.FirstOrDefault(p => p.Id == id) ?? Custom.GetValueOrDefault(id) ?? All[0];
    public static bool IsKnown(string id) => All.Any(p=>p.Id==id) || Custom.ContainsKey(id);
    internal static void Register(CustomPet pet) => Custom[pet.Id]=pet.Info;
    internal static void Unregister(string id) => Custom.Remove(id);
    public static readonly string[] Categories = ["Ideas", "Estudio", "Productividad", "Inspiración", "Personal"];
    public static string StateName(PetState state) => state switch { PetState.Idle => "Parpadeando", PetState.Move => "En movimiento", PetState.Think => "Pensando", PetState.Drowsy => "Con sueño", PetState.Sleep => "Durmiendo", _ => "Comiendo / bebiendo" };
}
public sealed class Settings
{
    public const double DefaultPetSize = 144;
    public const double MinPetSize = 88;
    public const double MaxPetSize = 224;
    public string Pet { get; set; } = "mindie";
    public string PetName { get; set; } = "Mindie";
    public string Endpoint { get; set; } = "http://127.0.0.1:11434";
    public string Model { get; set; } = "gemma4:12b";
    public string Provider { get; set; } = "ollama";
    public const string NvidiaModel = "google/gemma-4-31b-it";
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsNvidia => Provider == "nvidia";
    [System.Text.Json.Serialization.JsonIgnore]
    public string ActiveModel => IsNvidia ? NvidiaModel : Model;
    [System.Text.Json.Serialization.JsonIgnore]
    public string ProviderName => IsNvidia ? "NVIDIA API" : "Ollama local";
    public bool Specific { get; set; }
    public bool GiveContext { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public bool Speak { get; set; }
    public bool StartWithWindows { get; set; }
    public bool ReduceMotion { get; set; }
    public bool Onboarded { get; set; }
    public double? PetLeft { get; set; }
    public double? PetTop { get; set; }
    public double PetSize { get; set; } = DefaultPetSize;
    public Settings Copy() => (Settings)MemberwiseClone();
}
public sealed class MemoryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> Ideas { get; set; } = [];
    public string Category { get; set; } = "Ideas";
    public bool Favorite { get; set; }
    public string Kind { get; set; } = "capture";
    public string SourceName { get; set; } = "";
    public int PageCount { get; set; }
    public string UserContext { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string SourceText { get; set; } = "";
    public string OriginalSourceText { get; set; } = "";
    public int WebTextCleanupVersion { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsPdf => Kind == "pdf";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    [System.Text.Json.Serialization.JsonIgnore]
    public string DateLabel => CreatedAt.ToLocalTime().ToString("d MMM yyyy", CultureInfo.GetCultureInfo("es-MX"));
    public MemoryItem Copy() { var copy=(MemoryItem)MemberwiseClone(); copy.Ideas=[..Ideas]; return copy; }
}
public sealed class PdfPageText
{
    public int Number { get; set; }
    public string Text { get; set; } = "";
    public bool NeedsVision { get; set; }
}
public sealed class PdfKnowledge
{
    public string FileName { get; set; } = "";
    public List<PdfPageText> Pages { get; set; } = [];
    public List<ChatMessage> Messages { get; set; } = [];
}
public sealed class ChatMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public List<ChatImage> Images { get; set; } = [];
}
public sealed record ChatImage(string Id,string Name,int Width,int Height,bool IsScreenCapture=false);
public sealed class ChatConversation
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDeleted { get; set; }
    public string Id { get; set; }=Guid.NewGuid().ToString("N");
    public string Title { get; set; }="Nueva conversación";
    public string Scope { get; set; }="general";
    public string? MemoryId { get; set; }
    public List<ChatImage> ScreenImages { get; set; }=[];
    public DateTimeOffset CreatedAt { get; set; }=DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; }=DateTimeOffset.Now;
    public ObservableCollection<ChatMessage> Messages { get; set; }=[];
}
public sealed class LibraryData
{
    public int Version { get; set; } = 5;
    public Settings Settings { get; set; } = new();
    public List<MemoryItem> Memories { get; set; } = [];
    public List<ChatMessage> Messages { get; set; } = [];
    public List<ChatConversation> Conversations { get; set; }=[];
    public string? ActiveConversationId { get; set; }
    public List<CustomPet> CustomPets { get; set; } = [];
    public Dictionary<string,KnowledgeNodePosition> GraphPositions { get; set; } = [];
}
public sealed record KnowledgeNodePosition(double X,double Y,string Category,double? Z=null);
public sealed class CustomPet
{
    public string Id { get; set; }="custom-"+Guid.NewGuid().ToString("N");
    public string Name { get; set; }="";
    public string Description { get; set; }="Creado por ti.";
    [System.Text.Json.Serialization.JsonIgnore]
    public PetInfo Info=>new(Id,Name,Name,Description,"Personalizado · Seis estados");
}
public sealed record OllamaStatus(bool Available, string[] Models, string[] Capabilities);
