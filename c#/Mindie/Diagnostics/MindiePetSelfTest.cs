using Mindie.Controls;
using Mindie.Services;
using Mindie.Views;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Mindie.Diagnostics;

public static partial class SelfTest
{
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern int GetCurrentProcessExplicitAppUserModelID(out IntPtr identity);
    private static async Task CheckMindieAsync(AppHost host,MainWindow window,string output,Action<bool,string> check)
    {
        check(Pets.All.Length==8 && Pets.All[0].Id=="mindie" && new Settings().Pet=="mindie" && new Settings().PetName=="Mindie","Mindie es el compañero principal y predeterminado, junto a los siete animales anteriores");
        using(var stream=System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Sprites/mindie.png")).Stream) {
            using var buffer=new MemoryStream(); await stream.CopyToAsync(buffer);
            var validated=await PetImportService.ValidateAsync(buffer.ToArray());
            check(validated.Frames.Length==24,"La hoja de Mindie supera la validación de 24 poses, transparencia, márgenes y variación por estado");
        }
        var visual=new DrawingVisual();
        using(var drawing=visual.RenderOpen()) {
            drawing.DrawRectangle(Ui.Brush("#151B22"),null,new(0,0,680,1080));
            foreach(var state in Enum.GetValues<PetState>()) {
                var y=(int)state*180;
                drawing.DrawText(new FormattedText(Pets.StateName(state),CultureInfo.GetCultureInfo("es-MX"),FlowDirection.LeftToRight,new Typeface("Segoe UI"),14,Brushes.White,1),new(12,y+5));
                for(var frame=0;frame<4;frame++) drawing.DrawImage(SpriteCatalog.Frame("mindie",state,frame),new(frame*170,y+27,150,150));
            }
        }
        var bitmap=new RenderTargetBitmap(680,1080,96,96,PixelFormats.Pbgra32); bitmap.Render(visual);
        await File.WriteAllBytesAsync(Path.Combine(output,"mindie-states.png"),Images.Encode(bitmap));
        check(FindButtons(window).Any(b=>AutomationProperties.GetName(b)=="Elegir Mindie") && FindElements<TextBlock>(window).Any(t=>t.Text=="Principal"),"Compañeros ofrece Mindie primero con distintivo Principal y selección estática");
        try { await host.Store.DeleteCustomPetAsync("mindie"); check(false,"Proteger compañero principal"); }
        catch(ArgumentException) { check(true,"Mindie es un recurso incluido y no puede eliminarse con la papelera de mascotas propias"); }
        var root=Path.Combine(output,"mindie-migration-"+Guid.NewGuid().ToString("N")); var old=new LibraryStore(root); await old.LoadAsync();
        old.Data.Version=4; old.Data.Settings.Pet="pollito"; old.Data.Settings.PetName="Pipo"; old.Data.Settings.PetLeft=123; old.Data.Settings.PetTop=90; old.Data.Settings.PetSize=152;
        old.Data.Settings.Provider="nvidia"; old.Data.Memories.Add(new() { Title="Conservar recuerdo",Summary="Información existente",Ideas=["Primera idea","Segunda idea"] });
        await old.SaveAsync(); var migrated=new LibraryStore(root); await migrated.LoadAsync();
        check(migrated.Data.Version==5 && migrated.Data.Settings.Pet=="mindie" && migrated.Data.Settings.PetName=="Mindie" && migrated.Data.Settings.PetLeft==123 && migrated.Data.Settings.PetSize==152 && migrated.Data.Settings.IsNvidia && migrated.Data.Memories.Single().Title=="Conservar recuerdo","Actualizar selecciona Mindie una vez y conserva posición, tamaño, proveedor y recuerdos");
        migrated.Data.Settings.Pet="pollito"; migrated.Data.Settings.PetName="Mi pollo"; await migrated.SaveAsync();
        var again=new LibraryStore(root); await again.LoadAsync();
        check(again.Data.Settings.Pet=="pollito" && again.Data.Settings.PetName=="Mi pollo","Elegir otro compañero después de actualizar se respeta en aperturas posteriores");
        var other=new Window(); Ui.WindowStyle(other);
        check(window.Icon is not null && other.Icon is not null,"Todas las ventanas heredan el icono de la burbujita, además de la ventana principal");
        var code=GetCurrentProcessExplicitAppUserModelID(out var identity);
        try { check(code==0 && Marshal.PtrToStringUni(identity)=="Mindie.Desktop","Windows recibe una identidad de aplicación explícita para agrupar Mindie en la barra de tareas"); }
        finally { if(identity!=IntPtr.Zero) Marshal.FreeCoTaskMem(identity); }
    }
}
