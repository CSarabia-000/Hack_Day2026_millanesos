using Mindie.Views;
using System.Windows.Interop;

namespace Mindie.Windows;

public sealed class QuickChatWindow : Window
{
    private readonly ChatView _chat;
    public ChatConversation Conversation { get; }
    public QuickChatWindow(AppHost host,PetWindow origin,ChatConversation conversation)
    {
        Services.ShellBranding.Attach(this);
        Title = $"Mindie · {host.Settings.PetName}"; Width = 470; Height = 570; MinWidth = 400; MinHeight = 400; Topmost = host.Settings.AlwaysOnTop; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Conversation=conversation;
        _chat = new ChatView(host,true,conversation:conversation) { Margin = new(18) }; Content = _chat;
        Loaded += (_,_) => {
            NativeMethods.GetCursorPos(out var anchor);
            if(NativeMethods.GetWindowRect(new WindowInteropHelper(origin).Handle,out var bounds)) { anchor.X=(bounds.Left+bounds.Right)/2; anchor.Y=(bounds.Top+bounds.Bottom)/2; }
            NativeMethods.FitToWorkArea(this,true,new(anchor.X,anchor.Y)); _chat.FocusInput();
        };
    }
    public void FocusInput()=>_chat.FocusInput();
}
