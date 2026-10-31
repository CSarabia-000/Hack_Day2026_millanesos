using System.Windows.Interop;
using Mindie.Views;

namespace Mindie;

public partial class MainWindow : Window
{
    private readonly AppHost _host;
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(7) };
    private HwndSource? _source;
    public string CurrentPage { get; private set; } = "home";
    public MainWindow(AppHost host)
    {
        _host = host; InitializeComponent(); DataContext = host;
        Services.ShellBranding.Attach(this);
        Ui.NavigationContent(ChatNav,"Conversación","M 4,4 H 20 V 16 H 10 L 4,21 Z");
        Ui.NavigationContent(HistoryNav,"Historial","M 4,7 A 9,9 0 1 1 3,14 M 4,2 V 8 H 10 M 12,7 V 12 L 16,14");
        Ui.NavigationContent(HomeNav,"Compañeros","M 7,14 C 4,17 5,21 8,20 C 11,18 13,18 16,20 C 19,21 20,17 17,14 C 14,10 10,10 7,14 Z M 5,9 A 2,3 0 1 1 4.9,9 M 10,5 A 2,3 0 1 1 9.9,5 M 16,5 A 2,3 0 1 1 15.9,5 M 21,9 A 2,3 0 1 1 20.9,9");
        Ui.NavigationContent(SavedNav,"Mis guardados","M 6,3 H 18 V 21 L 12,17 L 6,21 Z");
        Ui.NavigationContent(BrainNav,"Cerebro digital","M 12,9 A 3,3 0 1 1 11.9,9 M 4,3 A 2,2 0 1 1 3.9,3 M 21,3 A 2,2 0 1 1 20.9,3 M 4,18 A 2,2 0 1 1 3.9,18 M 21,18 A 2,2 0 1 1 20.9,18 M 6,6 L 10,10 M 19,6 L 14,10 M 6,19 L 10,14 M 19,19 L 14,14");
        Ui.NavigationContent(CaptureNav,"Guardar en mente","M 8,3 H 3 V 8 M 16,3 H 21 V 8 M 21,16 V 21 H 16 M 8,21 H 3 V 16 M 8,12 H 16 M 12,8 V 16");
        Ui.NavigationContent(ShowPetNav,"Mostrar compañero","M 2,12 C 7,3 17,3 22,12 C 17,21 7,21 2,12 Z M 12,9 A 3,3 0 1 1 11.9,9");
        Ui.NavigationContent(SettingsNav,"Configuración","M 12,8 A 4,4 0 1 1 11.9,8 M 9,3 L 10,1 H 14 L 15,3 L 18,5 L 21,5 L 23,9 L 21,12 L 23,15 L 21,19 L 18,19 L 15,21 L 14,23 H 10 L 9,21 L 6,19 L 3,19 L 1,15 L 3,12 L 1,9 L 3,5 L 6,5 Z");
        _noticeTimer.Tick += (_, _) => { NoticeBar.Visibility = Visibility.Collapsed; _noticeTimer.Stop(); };
        host.Notice += OnNotice;
        SourceInitialized += (_, _) => { var handle = new WindowInteropHelper(this).Handle; _source = HwndSource.FromHwnd(handle); _source.AddHook(WndProc); if (!Environment.GetCommandLineArgs().Contains("--self-test")) { NativeMethods.RegisterHotKey(handle, 1, 0x4006, 0x4D); NativeMethods.RegisterHotKey(handle, 2, 0x4006, 0x20); } NativeMethods.FitToWorkArea(this,true); };
        Loaded += (_,_) => NativeMethods.FitToWorkArea(this);
        IsVisibleChanged += (_,_) => { if (IsVisible) Dispatcher.BeginInvoke(() => NativeMethods.FitToWorkArea(this),DispatcherPriority.Loaded); };
        Closing += (_, e) => { (PageHost.Content as SettingsView)?.FlushPending(); if (!host.Exiting) { e.Cancel = true; Hide(); } };
        Closed += (_, _) => { var handle = new WindowInteropHelper(this).Handle; NativeMethods.UnregisterHotKey(handle, 1); NativeMethods.UnregisterHotKey(handle, 2); _source?.RemoveHook(WndProc); host.Notice -= OnNotice; _noticeTimer.Stop(); };
        InputBindings.Add(new KeyBinding(new ActionCommand(() => { Navigate("saved"); (PageHost.Content as SavedView)?.FocusSearch(); }), new KeyGesture(Key.K, ModifierKeys.Control)));
        Navigate("home");
    }
    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x312) { if (wParam.ToInt32() == 1) _ = Ui.Safe(_host.CaptureAsync); else if (wParam.ToInt32() == 2) _host.ShowPet(); handled = true; } return IntPtr.Zero;
    }
    private void OnNotice(object? sender, string message) { NoticeText.Text = message; NoticeBar.Visibility = Visibility.Visible; _noticeTimer.Stop(); _noticeTimer.Start(); }
    public void Navigate(string page)
    {
        (PageHost.Content as SettingsView)?.FlushPending();
        CurrentPage = page;
        foreach (var button in new[] { HomeNav, SavedNav, ChatNav, HistoryNav, BrainNav, SettingsNav }) { button.Background = Brushes.Transparent; button.Foreground = Ui.Muted; button.FontWeight = FontWeights.Normal; }
        var active = page switch { "saved" => SavedNav, "chat" => ChatNav, "history"=>HistoryNav, "brain" => BrainNav, "settings" => SettingsNav, _ => HomeNav }; active.Background = Ui.Brush("#E2E7E0"); active.Foreground = Ui.Ink; active.FontWeight = FontWeights.SemiBold;
        Breadcrumb.Text = "Tu espacio   /   " + (page switch { "saved" => "Mis guardados", "chat" => "Conversación", "history"=>"Historial", "brain" => "Cerebro digital", "settings" => "Configuración", _ => "Compañeros" });
        PageHost.Content = page switch { "saved" => new SavedView(_host), "chat" => new ChatView(_host), "history"=>new HistoryView(_host), "brain" => new BrainView(_host), "settings" => new SettingsView(_host), _ => new HomeView(_host) };
    }
    private void HomeClick(object sender, RoutedEventArgs e) => Navigate("home");
    private void SavedClick(object sender, RoutedEventArgs e) => Navigate("saved");
    private void ChatClick(object sender, RoutedEventArgs e) => Navigate("chat");
    private void HistoryClick(object sender,RoutedEventArgs e)=>Navigate("history");
    private void BrainClick(object sender, RoutedEventArgs e) => Navigate("brain");
    private void SettingsClick(object sender, RoutedEventArgs e) => Navigate("settings");
    private async void CaptureClick(object sender, RoutedEventArgs e) => await Ui.Safe(_host.CaptureAsync);
    private void ShowPetClick(object sender, RoutedEventArgs e) => _host.ShowPet();
}
internal sealed class ActionCommand(Action action) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action();
}
