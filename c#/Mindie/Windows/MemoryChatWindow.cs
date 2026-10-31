using Mindie.Views;

namespace Mindie.Windows;

public sealed class MemoryChatWindow:Window
{
    public ChatConversation Conversation { get; }
    public MemoryChatWindow(AppHost host,ChatConversation conversation)
    {
        Services.ShellBranding.Attach(this);
        Conversation=conversation; Title="Mindie · "+conversation.Title; Width=740; Height=700; MinWidth=470; MinHeight=430;
        var chat=new ChatView(host,true,conversation:conversation) { Margin=new(24) }; Content=chat;
        Loaded+=(_,_)=> { NativeMethods.FitToWorkArea(this,true); chat.FocusInput(); };
    }
}
