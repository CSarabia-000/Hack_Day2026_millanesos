using Mindie.Services;
using Mindie.Views;

namespace Mindie;

public partial class App : System.Windows.Application
{
    public static AppHost? Host { get; private set; }
    private Mutex? _instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        NativeMethods.SetCurrentProcessExplicitAppUserModelID(ShellBranding.AppId);
        base.OnStartup(e);
        DispatcherUnhandledException += (_,args) => { if (Host is not null) Host.Report(args.Exception.Message); else MessageBox.Show(args.Exception.Message,"Mindie"); args.Handled = true; };
        try
        {
#if MINDIE_DIAGNOSTICS
            if(e.Args.Contains("--prepare-previews")) { Shutdown(Diagnostics.SelfTest.PrepareSpritePreviews()); return; }
            if(e.Args.Contains("--configure-nvidia")) { var index=Array.IndexOf(e.Args,"--configure-nvidia"); Shutdown(await Diagnostics.SelfTest.ConfigureNvidiaAsync(index+1<e.Args.Length?e.Args[index+1]:"",e.Args.Contains("--store-key-only"))); return; }
            if(e.Args.Contains("--performance-probe")) { var exit=await Diagnostics.SelfTest.RunPerformanceProbeAsync(e.Args.Contains("--baseline")); Shutdown(exit); return; }
            if (e.Args.Contains("--self-test")) { var exit = await Diagnostics.SelfTest.RunAsync(e.Args.Contains("--with-ollama")); Shutdown(exit); return; }
#else
            if(e.Args.Any(arg=>arg is "--self-test" or "--performance-probe" or "--prepare-previews" or "--configure-nvidia")) { Shutdown(2); return; }
#endif
            _instance = new Mutex(true,@"Local\Mindie.Native.Desktop",out var first);
            if (!first) { MessageBox.Show("Mindie ya está abierto. Puedes encontrarlo en la bandeja del sistema.","Mindie"); Shutdown(); return; }
            NativeMethods.RefreshApplicationIcon();
            ShellBranding.Initialize();
            var store = new LibraryStore(); await store.LoadAsync(); Host = new(store); Host.CreateTray();
            var window = new MainWindow(Host); Host.MainWindow = window; MainWindow = window;
            if (!e.Args.Contains("--minimized")) window.Show(); else new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            if (Host.Settings.Onboarded || e.Args.Contains("--minimized")) Host.ShowPet();
            await Host.CheckConnectionAsync();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message,"No se pudo abrir Mindie",MessageBoxButton.OK,MessageBoxImage.Error); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { Host?.Dispose(); _instance?.Dispose(); base.OnExit(e); }
}
