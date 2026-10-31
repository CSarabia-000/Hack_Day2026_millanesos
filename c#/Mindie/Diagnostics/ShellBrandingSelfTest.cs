using Mindie.Services;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Mindie.Diagnostics;

public static partial class SelfTest
{
    private static Task CheckShellBrandingAsync(MainWindow window,string output,Action<bool,string> check)
    {
        var root=Path.Combine(output,"shell-branding-"+Guid.NewGuid().ToString("N"));
        var icon=ShellBranding.PrepareIcon(root); var bytes=File.ReadAllBytes(icon); var modified=File.GetLastWriteTimeUtc(icon);
        check(icon.Contains("mindie-orb-") && bytes.Length>1000 && Path.IsPathFullyQualified(icon),"El shell recibe un archivo de icono propio con nombre derivado del contenido");
        check(ShellBranding.PrepareIcon(root)==icon && File.GetLastWriteTimeUtc(icon)==modified,"Abrir Mindie reutiliza el icono sin reescribirlo ni invalidar la caché continuamente");
        var executable=Environment.ProcessPath!;
        var hwnd=new WindowInteropHelper(window).Handle;
        ShellBranding.ApplyWindow(hwnd,executable,icon);
        check(ShellBranding.ReadWindowProperty(hwnd,3)==icon+",0" && ShellBranding.ReadWindowProperty(hwnd,5)==ShellBranding.AppId,"La ventana entrega al shell su icono de relanzamiento y el AppUserModelID, además del icono WPF");
        check(ShellBranding.ReadWindowProperty(hwnd,2)=="\""+executable+"\"" && ShellBranding.ReadWindowProperty(hwnd,4)=="Mindie","El anclado conserva comando de relanzamiento con ruta entre comillas y nombre de aplicación");
        var type=Type.GetTypeFromProgID("WScript.Shell")!; object shell=Activator.CreateInstance(type)!;
        var path=Path.Combine(root,"Mindie.lnk");
        object link=((dynamic)shell).CreateShortcut(path);
        try { ((dynamic)link).TargetPath=executable; ((dynamic)link).Arguments="--minimized"; ((dynamic)link).WorkingDirectory=root; ((dynamic)link).IconLocation=",0"; ((dynamic)link).Save(); }
        finally { Marshal.FinalReleaseComObject(link); }
        check(ShellBranding.RepairShortcut(path,executable,icon),"Se repara un anclado antiguo sin icono explícito solo si apunta al ejecutable actual");
        link=((dynamic)shell).CreateShortcut(path);
        try { check(((dynamic)link).IconLocation==icon+",0" && ((dynamic)link).TargetPath==executable && ((dynamic)link).Arguments=="--minimized" && ((dynamic)link).WorkingDirectory==root,"La reparación cambia solo el icono y conserva destino, argumentos y directorio de trabajo"); }
        finally { Marshal.FinalReleaseComObject(link); }
        check(!ShellBranding.RepairShortcut(path,executable,icon),"Un anclado ya corregido no se reescribe en cada inicio");
        var before=File.ReadAllBytes(path);
        check(!ShellBranding.RepairShortcut(path,Path.Combine(root,"Otra-app.exe"),icon) && File.ReadAllBytes(path).SequenceEqual(before),"La reparación no modifica accesos a otros ejecutables ni siquiera si se llaman Mindie");
        Marshal.FinalReleaseComObject(shell);
        return Task.CompletedTask;
    }
}
