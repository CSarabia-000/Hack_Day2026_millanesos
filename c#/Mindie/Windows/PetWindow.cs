using Mindie.Controls;
using Mindie.Views;
using Mindie.Services;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace Mindie.Windows;

public sealed class PetWindow : Window
{
    private readonly AppHost _host;
    private readonly SpritePlayer _sprite = new();
    private readonly Popup _actions = new() { AllowsTransparency = true, StaysOpen = true, Placement = PlacementMode.Custom };
    private readonly WidgetActionMenu _menu;
    private OutsideClickDismissal? _outsideClick;
    internal OutsideClickDismissal? OutsideClick=>_outsideClick;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private long _lastInteraction = Environment.TickCount64;
    private long _overrideUntil;
    private PetState _override;
    private long _outsideSince;
    private long _eatingUntil;
    private bool _dragPending, _dragging, _dragWasOpen;
    private Point _dragPointer;
    private NativeMethods.Rect _dragBounds;
    public SpritePlayer Sprite => _sprite;
    public PetWindow(AppHost host)
    {
        _host = host; Title = "Mindie · Compañero";
        _sprite.PetId=host.Settings.Pet; _sprite.ReduceMotion=host.Settings.ReduceMotion; _sprite.Animate=true;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = host.Settings.AlwaysOnTop;
        WindowStartupLocation = WindowStartupLocation.Manual; Content = _sprite; _actions.PlacementTarget = _sprite;
        AllowDrop=true; _sprite.AllowDrop=true;
        DragOver+=(_,e)=> { var files=e.Data.GetData(DataFormats.FileDrop) as string[] ?? []; var valid=!host.IsBusy && PdfService.IsPdfDrop(files); e.Effects=valid?DragDropEffects.Copy:DragDropEffects.None; e.Handled=true; if (valid) { CloseActions(); FeedPdf(1500); } };
        Drop+=(_,e)=> { e.Handled=true; var files=e.Data.GetData(DataFormats.FileDrop) as string[] ?? []; if (PdfService.IsPdfDrop(files) && !host.IsBusy) { FeedPdf(); host.ImportPdf(files[0]); } else host.Report("Suelta un único archivo PDF cuando termine la operación actual."); };
        System.Windows.Automation.AutomationProperties.SetName(_sprite,"Compañero Mindie. Arrastra para mover, clic para acciones o clic derecho para sus estados.");
        _menu = new WidgetActionMenu(
            () => { CloseActions(); host.QuickChat(); },
            () => { CloseActions(); _ = Ui.Safe(()=>host.CaptureFromPetAsync(this)); },
            () => { CloseActions(); host.Open("saved"); });
        _actions.Child = _menu;
        _actions.CustomPopupPlacementCallback = MenuPlacements;
        _actions.Opened += (_,_) => {
            _outsideSince = 0;
            _menu.Reveal(ReduceMenuMotion, reset: true);
            if (HwndSource.FromVisual(_menu) is HwndSource source) NativeMethods.SetWindowDisplayAffinity(source.Handle, 0x11);
            _outsideClick=new(Dispatcher,point=>OutsideClickDismissal.HitsWindow(point,new WindowInteropHelper(this).Handle) || (HwndSource.FromVisual(_menu) is HwndSource popup && OutsideClickDismissal.HitsWindow(point,popup.Handle)),CloseActions);
        };
        _actions.Closed+=(_,_)=> { _outsideClick?.Dispose(); _outsideClick=null; };
        _menu.MouseEnter += (_,_) => OpenActions();
        _menu.PreviewKeyDown += (_,e) => { if (e.Key == Key.Escape) { CloseActions(); e.Handled = true; } };
        var context = new ContextMenu();
        AddMenu(context,"Escribir",() => host.QuickChat()); AddMenu(context,"Guardar en mente",() => _ = Ui.Safe(()=>host.CaptureFromPetAsync(this))); AddMenu(context,"Mis guardados",() => host.Open("saved")); context.Items.Add(new Separator());
        AddMenu(context,"Añadir PDF…",host.ChoosePdf);
        foreach (var state in Enum.GetValues<PetState>()) { var captured = state; AddMenu(context,Pets.StateName(state),() => PreviewState(captured,true)); }
        context.Items.Add(new Separator()); AddMenu(context,"Despertar / reposo",Touch); AddMenu(context,"Abrir Mindie",() => host.Open()); AddMenu(context,"Ocultar compañero",host.HidePet); AddMenu(context,"Salir de Mindie",host.Exit); _sprite.ContextMenu = context;
        context.Opened += (_,_) => _actions.IsOpen = false;
        SourceInitialized += (_,_) => {
            var hwnd = new WindowInteropHelper(this).Handle; NativeMethods.ToolWindow(this); NativeMethods.ExcludeFromCapture(this); HwndSource.FromHwnd(hwnd).AddHook(HitTest);
            PlaceFromSettings();
        };
        _sprite.MouseEnter += (_,_) => { if (!_dragPending && _sprite.HitOpaque(Mouse.GetPosition(_sprite))) { Touch(); OpenActions(); } };
        _sprite.MouseMove += async (_,e) => {
            if (_dragPending) {
                e.Handled=true;
                if (Mouse.LeftButton==MouseButtonState.Released) await Ui.Safe(()=>FinishDragAsync());
                else if (NativeMethods.GetCursorPos(out var cursor)) UpdateDrag(new(cursor.X,cursor.Y));
            }
            else if (Mouse.LeftButton == MouseButtonState.Released && _sprite.HitOpaque(Mouse.GetPosition(_sprite))) OpenActions();
        };
        _sprite.MouseLeftButtonDown += (_, e) => {
            e.Handled = true;
            if (e.ClickCount > 1) { host.Open(); return; }
            if (NativeMethods.GetCursorPos(out var cursor)) { BeginDrag(new(cursor.X,cursor.Y)); if (!_sprite.CaptureMouse()) _dragPending=false; }
        };
        _sprite.MouseLeftButtonUp += async (_,e)=> { if (_dragPending) { e.Handled=true; await Ui.Safe(()=>FinishDragAsync()); } };
        _sprite.LostMouseCapture += async (_,_)=> { if (_dragPending) await Ui.Safe(()=>FinishDragAsync()); };
        PreviewKeyDown+=async(_,e)=> { if (_dragPending && e.Key==Key.Escape) { e.Handled=true; await Ui.Safe(()=>FinishDragAsync(true)); } };
        _timer.Tick += (_,_) => Tick(); IsVisibleChanged += (_,_) => { if (IsVisible) Tick(); else { _timer.Stop(); CloseActions(); } };
        host.Changed += OnHostChanged; Closed += (_,_) => { _timer.Stop(); CloseActions(); host.Changed -= OnHostChanged; }; ApplySettings();
    }
    private static void AddMenu(ContextMenu context,string label,Action action) { var item = new MenuItem { Header = label }; item.Click += (_,_) => action(); context.Items.Add(item); }
    private void OnHostChanged(object? sender,EventArgs e) => Tick();
    private bool ReduceMenuMotion => _host.Settings.ReduceMotion || !SystemParameters.ClientAreaAnimation;
    internal WidgetActionMenu ActionMenu => _menu;
    internal bool AreActionsOpen => _actions.IsOpen;
    internal static CustomPopupPlacement[] MenuPlacements(Size popup, Size target, Point offset) => [
        new(new Point((target.Width-popup.Width)/2, target.Height), PopupPrimaryAxis.Horizontal),
        new(new Point((target.Width-popup.Width)/2, -popup.Height), PopupPrimaryAxis.Horizontal)
    ];
    internal void OpenActions()
    {
        if (_dragPending || _sprite.ContextMenu?.IsOpen == true || _host.IsQuickPromptOpen) return;
        _timer.Interval=TimeSpan.FromMilliseconds(250); if(IsVisible) _timer.Start();
        _outsideSince = 0;
        if (!_actions.IsOpen) _actions.IsOpen = true;
        else if (_menu.IsClosing) _menu.Reveal(ReduceMenuMotion);
    }
    public void CloseActions() { _outsideSince = 0; _actions.IsOpen = false; if (_sprite.ContextMenu is { } menu) menu.IsOpen = false; }
    public void Touch() { _lastInteraction = Environment.TickCount64; _overrideUntil = 0; _eatingUntil=0; Tick(); }
    internal void FeedPdf(int milliseconds = 6000) { _lastInteraction=Environment.TickCount64; _eatingUntil=_lastInteraction+milliseconds; Tick(); }
    public void PreviewState(PetState state,bool persistent = false) { _override = state; _overrideUntil = persistent ? long.MaxValue : Environment.TickCount64 + 8000; Tick(); }
    private void Tick()
    {
        if (_actions.IsOpen)
        {
            if (IsPointerInActionArea() || _menu.IsMouseOver || _menu.IsKeyboardFocusWithin) { _outsideSince = 0; if (_menu.IsClosing) _menu.Reveal(ReduceMenuMotion); }
            else if (Mouse.LeftButton != MouseButtonState.Pressed)
            {
                if (_outsideSince == 0) _outsideSince = Environment.TickCount64;
                else if (Environment.TickCount64-_outsideSince >= 600) _menu.Dismiss(ReduceMenuMotion, () => _actions.IsOpen = false);
            }
        }
        var idle = Environment.TickCount64 - _lastInteraction;
        _sprite.State = _dragging ? PetState.Move : Environment.TickCount64<_eatingUntil ? PetState.Eat : _host.IsBusy ? PetState.Think : Environment.TickCount64 < _overrideUntil ? _override : idle >= 180_000 ? PetState.Sleep : idle >= 90_000 ? PetState.Drowsy : PetState.Idle;
        _timer.Stop();
        if(!IsVisible) return;
        var now=Environment.TickCount64;
        var next=new[] { _eatingUntil,_overrideUntil,_lastInteraction+90_000,_lastInteraction+180_000 }.Where(t=>t>now && t!=long.MaxValue).DefaultIfEmpty(now+60_000).Min();
        _timer.Interval=TimeSpan.FromMilliseconds(_actions.IsOpen?250:Math.Clamp(next-now,1,60_000)); _timer.Start();
    }
    // Keep drag handling in WPF's regular input loop so the shared sprite clock keeps rendering.
    // Screen coordinates avoid feedback as the transparent HWND moves beneath the pointer.
    internal void BeginDrag(Point pointer)
    {
        _dragWasOpen=_actions.IsOpen && !_menu.IsClosing; CloseActions(); Touch();
        NativeMethods.GetWindowRect(new WindowInteropHelper(this).Handle,out _dragBounds);
        _dragPointer=pointer; _dragPending=true; _dragging=false;
    }
    internal void UpdateDrag(Point pointer)
    {
        if (!_dragPending) return;
        var dx=pointer.X-_dragPointer.X; var dy=pointer.Y-_dragPointer.Y;
        var dpi=NativeMethods.GetDpiForWindow(new WindowInteropHelper(this).Handle); var scale=dpi>0?dpi/96.0:1;
        if (!_dragging && Math.Abs(dx)<SystemParameters.MinimumHorizontalDragDistance*scale && Math.Abs(dy)<SystemParameters.MinimumVerticalDragDistance*scale) return;
        _dragging=true; _sprite.State=PetState.Move;
        NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle,IntPtr.Zero,_dragBounds.Left+(int)Math.Round(dx),_dragBounds.Top+(int)Math.Round(dy),0,0,0x0015);
    }
    internal async Task FinishDragAsync(bool cancel=false)
    {
        if (!_dragPending) return;
        var moved=_dragging; _dragPending=false; _dragging=false;
        if (_sprite.IsMouseCaptured) _sprite.ReleaseMouseCapture();
        var hwnd=new WindowInteropHelper(this).Handle;
        if (cancel) NativeMethods.SetWindowPos(hwnd,IntPtr.Zero,_dragBounds.Left,_dragBounds.Top,0,0,0x0015);
        Touch(); Tick();
        if (moved && !cancel) {
            NativeMethods.GetWindowRect(hwnd,out var bounds);
            var area=Forms.Screen.FromHandle(hwnd).WorkingArea;
            var left=Math.Clamp(bounds.Left,area.Left,Math.Max(area.Left,area.Right-(bounds.Right-bounds.Left)));
            var top=Math.Clamp(bounds.Top,area.Top,Math.Max(area.Top,area.Bottom-(bounds.Bottom-bounds.Top)));
            var settings=_host.Settings.Copy(); settings.PetLeft=left; settings.PetTop=top;
            await _host.SaveSettingsAsync(settings);
        }
        else if (!cancel && !_dragWasOpen) OpenActions();
    }
    private bool IsPointerInActionArea()
    {
        // WPF MouseOver becomes false over transparent pixels and between the two HWNDs.
        // Observe a padded screen-space envelope instead, without creating a hit-test overlay.
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!NativeMethods.GetCursorPos(out var cursor) || !NativeMethods.GetWindowRect(hwnd,out var pet)) return _sprite.IsMouseOver;
        if (HwndSource.FromVisual(_menu) is not HwndSource popup || !NativeMethods.GetWindowRect(popup.Handle,out var menu)) return _sprite.IsMouseOver;
        var dpi = NativeMethods.GetDpiForWindow(hwnd); var padding = 18 * (dpi > 0 ? dpi / 96.0 : 1);
        return ContainsActionPointer(new(cursor.X,cursor.Y),
            new(pet.Left,pet.Top,pet.Right-pet.Left,pet.Bottom-pet.Top),
            new(menu.Left,menu.Top,menu.Right-menu.Left,menu.Bottom-menu.Top),padding);
    }
    internal static bool ContainsActionPointer(Point pointer, Rect pet, Rect menu, double padding)
    {
        var safeArea = Rect.Union(pet,menu); safeArea.Inflate(padding,padding);
        return safeArea.Contains(pointer);
    }
    private IntPtr HitTest(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if (message == 0x84)
        {
            if (_dragPending) { handled=true; return new IntPtr(1); }
            var coordinates=lParam.ToInt64(); var p = _sprite.PointFromScreen(new((short)coordinates,(short)(coordinates>>16)));
            if (!_sprite.HitOpaque(p)) { handled = true; return new IntPtr(-1); }
        }
        return IntPtr.Zero;
    }
    private void PlaceFromSettings()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var dpi = NativeMethods.GetDpiForWindow(hwnd); var scale = dpi > 0 ? dpi / 96.0 : 1;
        var size = (int)Math.Ceiling(_host.Settings.PetSize*scale);
        var area = Forms.Screen.PrimaryScreen!.WorkingArea;
        var left = (int)(_host.Settings.PetLeft ?? area.Right-size-24); var top = (int)(_host.Settings.PetTop ?? area.Bottom-size-24);
        var bounds = new System.Drawing.Rectangle(left,top,size,size);
        if (Forms.Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds))) area = Forms.Screen.FromRectangle(bounds).WorkingArea;
        left = Math.Clamp(left,area.Left,Math.Max(area.Left,area.Right-size));
        top = Math.Clamp(top,area.Top,Math.Max(area.Top,area.Bottom-size));
        NativeMethods.SetWindowPos(hwnd,IntPtr.Zero,left,top,0,0,0x0015);
    }
    public void ApplySettings()
    {
        _sprite.PetId = _host.Settings.Pet; _sprite.ReduceMotion = _host.Settings.ReduceMotion; Width = Height = _host.Settings.PetSize; Topmost = _host.Settings.AlwaysOnTop;
        if (new WindowInteropHelper(this).Handle != IntPtr.Zero) PlaceFromSettings();
    }
}
