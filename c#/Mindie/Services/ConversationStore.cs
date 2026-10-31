namespace Mindie.Services;

public sealed partial class LibraryStore
{
    public async Task DeleteConversationAsync(ChatConversation conversation)
    {
        var index=Data.Conversations.IndexOf(conversation); if(index<0) return;
        var previousActive=Data.ActiveConversationId; var previousMessages=Data.Messages;
        Data.Conversations.RemoveAt(index);
        if(Data.ActiveConversationId==conversation.Id) {
            var active=Data.Conversations.OrderByDescending(c=>c.UpdatedAt).FirstOrDefault(); Data.ActiveConversationId=active?.Id; Data.Messages=active?.Messages.ToList()??[];
        }
        try { await SaveAsync(); }
        catch { Data.Conversations.Insert(index,conversation); Data.ActiveConversationId=previousActive; Data.Messages=previousMessages; throw; }
        var retained=Data.Conversations.SelectMany(c=>c.Messages.SelectMany(m=>m.Images).Concat(c.ScreenImages)).Select(i=>i.Id).ToHashSet(StringComparer.Ordinal);
        DeleteChatImages(conversation.Messages.SelectMany(m=>m.Images).Concat(conversation.ScreenImages).Where(i=>!retained.Contains(i.Id)));
        conversation.IsDeleted=true;
    }
    internal static ChatMessage CopyMessage(ChatMessage m)=>new() { Role=m.Role,Content=m.Content,CreatedAt=m.CreatedAt,Images=[..m.Images] };
    internal static ChatConversation CopyConversation(ChatConversation c)=>new() { Id=c.Id,Title=c.Title,Scope=c.Scope,MemoryId=c.MemoryId,ScreenImages=[..c.ScreenImages],CreatedAt=c.CreatedAt,UpdatedAt=c.UpdatedAt,Messages=new(c.Messages.Select(CopyMessage)) };
    internal void EnsureConversations()
    {
        Data.Conversations=(Data.Conversations??[]).Where(c=>c is not null && Guid.TryParse(c.Id,out _)).DistinctBy(c=>c.Id).ToList();
        foreach(var conversation in Data.Conversations) {
            conversation.Messages=new((conversation.Messages??[]).Where(m=>m is not null && m.Role is "user" or "assistant").TakeLast(100));
            foreach(var message in conversation.Messages) message.Images=(message.Images??[]).Where(ChatImageService.Valid).Take(ChatImageService.MaximumCount).ToList();
            if(conversation.Scope is not ("general" or "screen" or "memory")) conversation.Scope="general";
            conversation.ScreenImages=(conversation.ScreenImages??[]).Where(ChatImageService.Valid).Where(i=>i.IsScreenCapture).Take(ChatImageService.MaximumCount).ToList();
            if(conversation.Scope=="screen" && conversation.ScreenImages.Count==0) conversation.ScreenImages=conversation.Messages.LastOrDefault(m=>m.Images.Any(i=>i.IsScreenCapture))?.Images.Where(i=>i.IsScreenCapture).ToList()??[];
            if(conversation.Scope=="memory" && !Guid.TryParse(conversation.MemoryId,out _)) conversation.MemoryId=null;
        }
        if(Data.Conversations.Count==0 && Data.Messages.Count>0) {
            var legacy=new ChatConversation { Title="Conversación anterior",Messages=new(Data.Messages),CreatedAt=Data.Messages[0].CreatedAt,UpdatedAt=Data.Messages[^1].CreatedAt };
            Data.Conversations.Add(legacy); Data.ActiveConversationId=legacy.Id;
        }
        var active=Data.Conversations.FirstOrDefault(c=>c.Id==Data.ActiveConversationId);
        if(active is null && Data.Conversations.Count>0) { active=Data.Conversations.OrderByDescending(c=>c.UpdatedAt).First(); Data.ActiveConversationId=active.Id; }
        if(active is not null) Data.Messages=active.Messages.ToList();
    }
}
