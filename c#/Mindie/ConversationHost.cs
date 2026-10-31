using Mindie.Services;
using Mindie.Views;
using Mindie.Windows;

namespace Mindie;

public sealed partial class AppHost
{
    private string? _busyConversationId,_busyMemoryId;
    internal bool IsChatBusy(ChatConversation? conversation,MemoryItem? document)=>IsBusy && (conversation is not null?_busyConversationId==conversation.Id:document is not null && _busyMemoryId==document.Id);
    public ChatConversation CurrentConversation { get; private set; }=new();
    public ObservableCollection<ChatMessage> Messages=>CurrentConversation.Messages;
    public ChatConversation NewConversation()
    {
        CurrentConversation=new(); Notify(nameof(Messages)); Refresh(); return CurrentConversation;
    }
    public void OpenConversation(ChatConversation conversation)
    {
        if(conversation.IsDeleted) { Report("Esta conversación se eliminó del Historial."); return; }
        CurrentConversation=conversation; Store.Data.ActiveConversationId=conversation.Id;
        Store.Data.Messages=conversation.Messages.ToList(); Notify(nameof(Messages)); Open("chat"); Refresh();
    }
    public async Task DeleteConversationAsync(ChatConversation conversation)
    {
        if(IsBusy) throw new InvalidOperationException("Espera a que termine la respuesta o detenla antes de eliminar una conversación.");
        await Store.DeleteConversationAsync(conversation);
        if(CurrentConversation==conversation) NewConversation();
        Refresh();
    }
    public async Task OpenMemoryChatAsync(MemoryItem memory)
    {
        if(memory.IsPdf) { await OpenPdfAsync(memory); return; }
        var conversation=Store.Data.Conversations.FirstOrDefault(c=>c.Scope=="memory" && c.MemoryId==memory.Id)??new ChatConversation { Scope="memory",MemoryId=memory.Id,Title=memory.Title };
        var existing=System.Windows.Application.Current.Windows.OfType<MemoryChatWindow>().FirstOrDefault(w=>w.Conversation.MemoryId==memory.Id && !w.Conversation.IsDeleted);
        if(existing is not null) { existing.Show(); existing.Activate(); return; }
        var window=new MemoryChatWindow(this,conversation); window.Show(); window.Activate();
    }
    internal MemoryItem? ConversationMemory(ChatConversation conversation)=>Store.Data.Memories.FirstOrDefault(m=>m.Id==conversation.MemoryId);
}
