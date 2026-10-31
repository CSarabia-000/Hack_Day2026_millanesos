using Mindie.Services;
using Mindie.Windows;

namespace Mindie;

public sealed partial class AppHost
{
    public void OpenSource(string value)
    {
        var url=LibraryStore.ValidateSourceUrl(value);
        if (url.Length>0) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute=true });
    }
    public void ChoosePdf()
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Title="Añadir PDF a Mis guardados",Filter="Documento PDF|*.pdf",Multiselect=false,CheckFileExists=true };
        if (picker.ShowDialog(MainWindow)==true) ImportPdf(picker.FileName);
    }
    public void ImportPdf(string path)
    {
        if (IsBusy) { Report("Termina o cancela la operación actual antes de añadir un PDF."); return; }
        Open("saved"); var window = new PdfImportWindow(this,path) { Owner=MainWindow }; window.Show();
    }
    public async Task<(MemoryItem Item,PdfKnowledge Knowledge)> ImportPdfAsync(string path, IProgress<string> progress)
    {
        if (IsBusy) throw new InvalidOperationException("Ya hay una operación en curso.");
        var settings=Settings.Copy(); IsBusy=true; _request=new();
        try
        {
            var status = await Ollama.StatusAsync(settings,_request.Token);
            if (!status.Available) throw new InvalidOperationException("Configura el proveedor y el modelo en Configuración → Inteligencia artificial.");
            if (!status.Capabilities.Contains("vision")) throw new InvalidOperationException("El modelo necesita visión para leer páginas escaneadas y figuras del PDF.");
            var prepared = await new PdfService().ReadAsync(path,Ollama,settings,_request.Token,progress);
            var item = await Ollama.SummarizePdfAsync(settings,prepared.Knowledge,_request.Token,progress);
            _request.Token.ThrowIfCancellationRequested(); progress.Report("Guardando el PDF y su tarjeta en tu equipo…");
            await Store.AddPdfAsync(item,prepared); Refresh(); return (item,prepared.Knowledge);
        }
        finally { _request.Dispose(); _request=null; IsBusy=false; }
    }
    public async Task OpenPdfAsync(MemoryItem item)
    {
        var existing = System.Windows.Application.Current.Windows.OfType<PdfKnowledgeWindow>().FirstOrDefault(w=>w.Item.Id==item.Id);
        if (existing is not null) { existing.Show(); existing.Activate(); return; }
        var knowledge = await Store.ReadKnowledgeAsync(item.Id);
        var window = new PdfKnowledgeWindow(this,item,knowledge) { Owner=MainWindow }; window.Show(); window.Activate();
    }
    public async Task<ChatMessage> AskPdfAsync(MemoryItem item, PdfKnowledge knowledge, IEnumerable<ChatMessage> history)
    {
        if (IsBusy) throw new InvalidOperationException("Termina o cancela la operación actual antes de preguntar.");
        if (!Store.Data.Memories.Any(m=>m.Id==item.Id)) throw new InvalidOperationException("Este PDF ya no está en Mis guardados.");
        _busyMemoryId=item.Id; IsBusy=true; _request=new();
        try
        {
            var conversation = history.ToList();
            var response = await Ollama.AskPdfAsync(Settings,item,knowledge,conversation,_request.Token);
            var old = knowledge.Messages; knowledge.Messages=conversation.Append(response).TakeLast(100).ToList();
            try { await Store.SaveKnowledgeAsync(item.Id,knowledge); } catch { knowledge.Messages=old; throw; }
            if (Settings.Speak) Speech.Speak(response.Content);
            return response;
        }
        finally { _request.Dispose(); _request=null; _busyMemoryId=null; IsBusy=false; }
    }
    public async Task<MemoryItem> SummarizeSavedPdfAsync(MemoryItem item, IProgress<string> progress)
    {
        if (IsBusy) throw new InvalidOperationException("Ya hay una operación en curso.");
        IsBusy=true; _request=new();
        try { return await Ollama.SummarizePdfAsync(Settings,await Store.ReadKnowledgeAsync(item.Id),_request.Token,progress); }
        finally { _request.Dispose(); _request=null; IsBusy=false; }
    }
}
