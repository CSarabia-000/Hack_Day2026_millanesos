using Mindie.Services;
using Mindie.Views;

namespace Mindie.Windows;

public sealed class PdfKnowledgeWindow : Window
{
    public MemoryItem Item { get; }
    public PdfKnowledgeWindow(AppHost host,MemoryItem item,PdfKnowledge knowledge)
    {
        Ui.WindowStyle(this);
        Item=item; Title=$"Mindie · {item.Title}"; Width=1120; Height=800; MinWidth=800; MinHeight=550; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var grid = new Grid { Margin=new(24) }; grid.ColumnDefinitions.Add(new() { Width=new(330) }); grid.ColumnDefinitions.Add(new() { Width=new(24) }); grid.ColumnDefinitions.Add(new());
        var cover = new Image { Source=Images.Load(host.Store.CapturePath(item.Id)),Height=190,Stretch=Stretch.Uniform };
        var edit=Ui.Button("Editar tarjeta",()=> { new MemoryEditorWindow(host,File.ReadAllBytes(host.Store.CapturePath(item.Id)),item) { Owner=this }.Show(); });
        var original=Ui.Button("Abrir PDF original",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(host.Store.DocumentPath(item.Id)) { UseShellExecute=true })); original.Margin=new(0,10,0,0);
        var side=Ui.Column(Ui.Text("CONOCIMIENTO · PDF",11,Ui.Accent,FontWeights.SemiBold),Ui.Space(10),Ui.Text(item.Title,23,weight:FontWeights.SemiBold),Ui.Space(12),cover,Ui.Space(14),Ui.Text($"{item.SourceName} · {item.PageCount} páginas",12,Ui.Muted),Ui.Space(14),Ui.Text(item.Summary,14),Ui.Space(12),Ui.Text(string.Join("\n",item.Ideas.Select(t=>"• "+t)),13,Ui.Muted),Ui.Space(20),edit,original);
        grid.Children.Add(Ui.Scroll(side));
        var chat = new ChatView(host,true,item,knowledge); Grid.SetColumn(chat,2); grid.Children.Add(chat); Content=grid;
        Loaded+=(_,_)=> { NativeMethods.FitToWorkArea(this,true); chat.FocusInput(); };
        void UpdateDeletedState(object? sender,EventArgs args)
        {
            var exists=host.Store.Data.Memories.Any(m=>m.Id==item.Id);
            edit.IsEnabled=exists; original.IsEnabled=exists;
        }
        host.Changed+=UpdateDeletedState; Closed+=(_,_)=>host.Changed-=UpdateDeletedState;
    }
}
