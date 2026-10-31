using Mindie.Views;

namespace Mindie.Windows;

public enum MemoryCaptureMode { Area,FullPage,WebText }
public sealed class CaptureModeWindow : Window
{
    public MemoryCaptureMode Mode { get; private set; }
    public CaptureModeWindow()
    {
        Ui.WindowStyle(this); Title="Mindie · Guardar en mente"; Width=680; Height=560; MinWidth=480; MinHeight=500; WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var choices=Ui.Column();
        Button Choice(string title,string description,MemoryCaptureMode mode)
        {
            var button=Ui.Button(title,()=> { Mode=mode; DialogResult=true; }); button.HorizontalContentAlignment=HorizontalAlignment.Stretch; button.Padding=new(16,12,16,12); button.Margin=new(0,0,0,10);
            button.Content=Ui.Column(Ui.Text(title,17,weight:FontWeights.SemiBold),Ui.Space(4),Ui.Text(description,12,Ui.Muted)); return button;
        }
        var text=Choice("Página web: solo texto","Recomendado para artículos. Lee el contenido sin cargar imágenes, videos ni scripts.",MemoryCaptureMode.WebText); text.Background=Ui.Soft; text.BorderBrush=Ui.Accent; text.IsDefault=true; choices.Children.Add(text);
        choices.Children.Add(Choice("Área de pantalla","Selecciona con el mouse la región exacta que quieres guardar.",MemoryCaptureMode.Area));
        choices.Children.Add(Choice("Página web completa","Captura visual con imágenes y gráficos, incluido lo que queda debajo. Utiliza más recursos.",MemoryCaptureMode.FullPage));
        var cancel=Ui.Button("Cancelar",()=>DialogResult=false); cancel.HorizontalAlignment=HorizontalAlignment.Right; cancel.Margin=new(0,10,0,0);
        Content=Ui.Column(Ui.Heading("¿Qué quieres guardar?","Elige el método que mejor encaje con el contenido."),choices,cancel); ((FrameworkElement)Content).Margin=new(26);
        Loaded+=(_,_)=>NativeMethods.FitToWorkArea(this,true); PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Escape) { DialogResult=false; e.Handled=true; } };
    }
}
