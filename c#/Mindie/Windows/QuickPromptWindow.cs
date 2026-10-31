using Microsoft.Win32;
using Mindie.Controls;
using Mindie.Services;
using Mindie.Views;
using System.Windows.Automation;
using System.Windows.Interop;
using ShapePath=System.Windows.Shapes.Path;

namespace Mindie.Windows;

public sealed class QuickPromptWindow:Window
{
    private readonly AppHost _host;
    private readonly PetWindow _origin;
    private readonly Func<CancellationToken,Task<string>> _listen;
    private readonly Action _openConversation;
    private readonly Func<CancellationToken,Task<PreparedChatImage>> _captureScreen;
    private string? _automaticImageId;
    private bool _retryCapture;
    private readonly ChatConversation _conversation;
    private readonly ContextMenu _attachmentOptions=new();
    internal ContextMenu AttachmentOptions=>_attachmentOptions;
    private readonly List<PreparedChatImage> _images=[];
    private readonly WrapPanel _previews=new() { Margin=new(10,8,10,0),Visibility=Visibility.Collapsed };
    private readonly TextBlock _status=Ui.Text("",11,Ui.Brush("#D1D8DE"));
    private readonly TextBlock _hint=Ui.Text("Escribe un mensaje…",13,Ui.Brush("#C3C9CE"));
    private readonly Button _add,_send,_microphone;
    private readonly CancellationTokenSource _lifetime=new();
    private CancellationTokenSource? _listening,_sending;
    private bool _preparing,_closed,_placing,_choosingImages;
    private OutsideClickDismissal? _outsideClick;
    internal OutsideClickDismissal? OutsideClick=>_outsideClick;
    internal TextBox Input { get; }=Ui.Input("","Mensaje rápido para el compañero");
    internal IReadOnlyList<PreparedChatImage> Attachments=>_images;
    internal bool Sending=>_sending is not null;
    public QuickPromptWindow(AppHost host,PetWindow origin):this(host,origin,host.Speech.ListenAsync,null) { }
    internal QuickPromptWindow(AppHost host,PetWindow origin,Func<CancellationToken,Task<string>> listen,Action? openConversation,Func<CancellationToken,Task<PreparedChatImage>>? captureScreen=null)
    {
        _host=host; _origin=origin; _listen=listen; _conversation=host.CurrentConversation; _openConversation=openConversation??(()=>host.ShowQuickConversation(origin,_conversation));
        _captureScreen=captureScreen??(cancellation=>host.CaptureScreenForChatAsync(origin,cancellation));
        Title="Mindie · Entrada rápida"; Width=390; SizeToContent=SizeToContent.Height; MaxHeight=320;
        WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent; ResizeMode=ResizeMode.NoResize; ShowInTaskbar=false; Topmost=true; WindowStartupLocation=WindowStartupLocation.Manual;
        Input.FontSize=13; Input.Foreground=Brushes.White; Input.CaretBrush=Brushes.White; Input.Background=Brushes.Transparent; Input.BorderThickness=new(0); Input.Padding=new(2,0,2,0); Input.MinHeight=34; Input.Height=34; Input.MaxLength=8000;
        var field=new Grid { Margin=new(4,0,5,0) }; field.Children.Add(Input); _hint.VerticalAlignment=VerticalAlignment.Center; _hint.IsHitTestVisible=false; _hint.Margin=new(4,0,4,0); field.Children.Add(_hint);
        _add=RoundButton("Adjuntar imágenes","M 8,2 V 14 M 2,8 H 14",16,()=> { _attachmentOptions.PlacementTarget=_add; _attachmentOptions.IsOpen=true; return Task.CompletedTask; });
        var upload=new MenuItem { Header="Subir imágenes" }; upload.Click+=async(_,_)=> { _attachmentOptions.IsOpen=false; _retryCapture=false; await ChooseImagesAsync(); };
        var screen=new MenuItem { Header="Qué veo en pantalla?" }; screen.Click+=async(_,_)=> { _attachmentOptions.IsOpen=false; await SubmitAsync(true); };
        _attachmentOptions.Items.Add(upload); _attachmentOptions.Items.Add(screen);
        _microphone=RoundButton("Dictar mensaje","M 8,2 C 5,2 5,4 5,5 V 9 C 5,12 11,12 11,9 V 5 C 11,4 11,2 8,2 M 2,8 V 9 C 2,17 14,17 14,9 V 8 M 8,15 V 19 M 4,19 H 12",14,async()=>await BeginDictationAsync());
        _send=RoundButton("Enviar mensaje","M 3,9 L 8,4 L 13,9 M 8,4 V 15",16,async()=>await SubmitAsync()); _send.Background=Ui.Brush("#5F8663");
        var row=new Grid { Margin=new(6,4,6,4),MinHeight=40 }; row.ColumnDefinitions.Add(new() { Width=GridLength.Auto }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width=GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        row.Children.Add(_add); Grid.SetColumn(field,1); row.Children.Add(field); Grid.SetColumn(_microphone,2); row.Children.Add(_microphone); Grid.SetColumn(_send,3); row.Children.Add(_send);
        _status.Margin=new(14,0,14,10); _status.Visibility=Visibility.Collapsed; AutomationProperties.SetLiveSetting(_status,AutomationLiveSetting.Polite);
        var body=Ui.Column(_previews,row,_status);
        Content=new Border { Margin=new(5),Background=new LinearGradientBrush(Color.FromArgb(248,65,69,72),Color.FromArgb(248,48,52,55),90),BorderBrush=Ui.Brush("#737B80"),BorderThickness=new(1),CornerRadius=new(25),Child=body };
        AutomationProperties.SetName(this,"Entrada rápida de Mindie. Adjunta imágenes, escribe o dicta y pulsa Enter. Escape cierra.");
        Input.TextChanged+=(_,_)=> { _hint.Visibility=Input.Text.Length==0?Visibility.Visible:Visibility.Collapsed; UpdateControls(); };
        Input.PreviewKeyDown+=async(_,e)=> { if(e.Key==Key.Enter) { e.Handled=true; await SubmitAsync(); } };
        PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Escape) { e.Handled=true; Close(); } };
        SourceInitialized+=(_,_)=> { NativeMethods.ToolWindow(this); NativeMethods.ExcludeFromCapture(this); Place(true); };
        Loaded+=(_,_)=> { Place(); FocusInput(); if(_outsideClick is not { IsActive:true }) _outsideClick=new(Dispatcher,ContainsClick,Close); };
        SizeChanged+=(_,_)=>Place(); origin.LocationChanged+=OriginMoved; origin.SizeChanged+=OriginMoved;
        Closed+=(_,_)=> { _closed=true; _attachmentOptions.IsOpen=false; _outsideClick?.Dispose(); _lifetime.Cancel(); _listening?.Cancel(); _sending?.Cancel(); origin.LocationChanged-=OriginMoved; origin.SizeChanged-=OriginMoved; _lifetime.Dispose(); };
        UpdateControls();
    }
    public void FocusInput() { Input.Focus(); Input.CaretIndex=Input.Text.Length; }
    private bool ContainsClick(NativeMethods.Point point)=>_choosingImages || OutsideClickDismissal.HitsWindow(point,new WindowInteropHelper(this).Handle) || OutsideClickDismissal.HitsWindow(point,new WindowInteropHelper(_origin).Handle);
    private void OriginMoved(object? sender,EventArgs e)=>Place();
    internal static Rect Position(Rect pet,Rect work,Size size,double gap,double margin)
    {
        var left=Math.Clamp(pet.X+(pet.Width-size.Width)/2,work.Left+margin,Math.Max(work.Left+margin,work.Right-margin-size.Width));
        var below=pet.Bottom+gap; var top=below+size.Height<=work.Bottom-margin?below:pet.Top-gap-size.Height;
        top=Math.Clamp(top,work.Top+margin,Math.Max(work.Top+margin,work.Bottom-margin-size.Height)); return new(left,top,size.Width,size.Height);
    }
    private void Place(bool moveMonitor=false)
    {
        if(_placing || _closed) return; var hwnd=new WindowInteropHelper(this).Handle; var pet=new WindowInteropHelper(_origin).Handle;
        if(hwnd==IntPtr.Zero || pet==IntPtr.Zero || !NativeMethods.GetWindowRect(pet,out var bounds)) return;
        _placing=true;
        try {
            var work=System.Windows.Forms.Screen.FromHandle(pet).WorkingArea;
            if(moveMonitor) NativeMethods.SetWindowPos(hwnd,IntPtr.Zero,work.Left+12,work.Top+12,0,0,0x0015);
            var dpi=NativeMethods.GetDpiForWindow(hwnd); var scale=dpi>0?dpi/96.0:1;
            Width=Math.Min(390,(work.Width-24*scale)/scale);
            if(!NativeMethods.GetWindowRect(hwnd,out var current)) return;
            var rect=Position(new(bounds.Left,bounds.Top,bounds.Right-bounds.Left,bounds.Bottom-bounds.Top),new(work.X,work.Y,work.Width,work.Height),new(current.Right-current.Left,current.Bottom-current.Top),8*scale,12*scale);
            if(current.Left!=(int)rect.X || current.Top!=(int)rect.Y) NativeMethods.SetWindowPos(hwnd,IntPtr.Zero,(int)rect.X,(int)rect.Y,0,0,0x0015);
        }
        finally { _placing=false; }
    }
    private async Task ChooseImagesAsync()
    {
        var picker=new OpenFileDialog { Title="Adjuntar imágenes",Filter="Imágenes|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff",Multiselect=true };
        bool selected;
        _choosingImages=true;
        try { selected=picker.ShowDialog(this)==true; }
        finally { _choosingImages=false; }
        if(selected) await AddImagesAsync(picker.FileNames);
    }
    internal async Task AddImagesAsync(IReadOnlyList<string> paths)
    {
        if(_preparing || _sending is not null || _listening is not null) return;
        if(paths.Count==0) return;
        if(_images.Count+paths.Count>ChatImageService.MaximumCount) { Status($"Puedes adjuntar hasta {ChatImageService.MaximumCount} imágenes.",true); return; }
        _preparing=true; UpdateControls(); Status("Preparando imágenes…");
        try {
            var added=await ChatImageService.PrepareAsync(paths,_lifetime.Token); if(_closed) return;
            if(_images.Sum(i=>(long)i.Png.Length)+added.Sum(i=>(long)i.Png.Length)>ChatImageService.MaximumBatchBytes) throw new ArgumentException("Los adjuntos superan 32 MB. Quita alguna imagen.");
            _images.AddRange(added); DrawAttachments(); Status($"{_images.Count} {(_images.Count==1?"imagen adjunta":"imágenes adjuntas")}");
        }
        catch(OperationCanceledException) { }
        catch(Exception ex) { if(!_closed) Status(ex.Message,true); }
        finally { _preparing=false; if(!_closed) { UpdateControls(); FocusInput(); } }
    }
    internal void RemoveImage(string id)
    {
        if(_sending is not null || _preparing) return;
        if(id==_automaticImageId) { _retryCapture=false; _automaticImageId=null; }
        _images.RemoveAll(i=>i.Image.Id==id); DrawAttachments(); Status(_images.Count==0?"":$"{_images.Count} imágenes adjuntas"); UpdateControls(); FocusInput();
    }
    private void DrawAttachments()
    {
        _previews.Children.Clear(); _previews.Visibility=_images.Count==0?Visibility.Collapsed:Visibility.Visible;
        foreach(var image in _images) {
            var preview=new BitmapImage(); using(var stream=new MemoryStream(image.Png,false)) { preview.BeginInit(); preview.CacheOption=BitmapCacheOption.OnLoad; preview.DecodePixelWidth=80; preview.StreamSource=stream; preview.EndInit(); preview.Freeze(); }
            var grid=new Grid { Width=75,Height=61 }; grid.Children.Add(new Image { Source=preview,Stretch=Stretch.Uniform,Margin=new(4) });
            var remove=RoundButton("Quitar imagen: "+image.Image.Name,"M 3,3 L 13,13 M 13,3 L 3,13",10,()=> { RemoveImage(image.Image.Id); return Task.CompletedTask; });
            remove.Width=20; remove.Height=20; remove.MinHeight=20; remove.Background=Ui.Brush("#DB292F32"); remove.HorizontalAlignment=HorizontalAlignment.Right; remove.VerticalAlignment=VerticalAlignment.Top; grid.Children.Add(remove);
            _previews.Children.Add(new Border { Child=grid,Background=Ui.Brush("#303638"),BorderBrush=Ui.Brush("#626B70"),BorderThickness=new(1),CornerRadius=new(9),Margin=new(3),ToolTip=image.Image.Name });
        }
    }
    internal async Task SubmitAsync(bool captureScreen=false)
    {
        if(_sending is not null) { _sending.Cancel(); return; }
        if(_preparing || _listening is not null) return;
        if(Input.Text.Trim().Length==0 && _images.Count==0 && !captureScreen) return;
        if(_host.IsBusy) { Status("Espera a que termine la operación actual.",true); return; }
        _sending=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); UpdateControls(); Status("Preparando tu respuesta…");
        try {
            if(captureScreen) _retryCapture=true;
            if(_retryCapture) {
                var manual=_images.Where(i=>i.Image.Id!=_automaticImageId).ToArray();
                if(manual.Length>=ChatImageService.MaximumCount) throw new ArgumentException("Quita una imagen para poder adjuntar la captura automática de pantalla.");
                Status("Capturando la pantalla completa…");
                var screenshot=await _captureScreen(_sending.Token); _sending.Token.ThrowIfCancellationRequested();
                if(manual.Sum(i=>(long)i.Png.Length)+screenshot.Png.Length>ChatImageService.MaximumBatchBytes) throw new ArgumentException("Quita alguna imagen para adjuntar la captura sin superar 32 MB.");
                screenshot=screenshot with { Image=screenshot.Image with { IsScreenCapture=true } };
                _images.RemoveAll(i=>i.Image.Id==_automaticImageId); _images.Add(screenshot); _automaticImageId=screenshot.Image.Id; DrawAttachments(); UpdateControls();
                Status("Analizando tu pantalla…");
            }
            var question=Input.Text.Trim().Length==0 && _retryCapture?"¿Qué veo en pantalla? Describe solo lo visible en esta captura.":Input.Text;
            await _host.SendAsync(question,_images.ToArray(),_sending.Token,_conversation);
            if(_closed) return; _images.Clear(); _openConversation(); Close();
        }
        catch(OperationCanceledException) { if(!_closed) Status("Respuesta cancelada. Tu mensaje y adjuntos siguen aquí."); }
        catch(Exception ex) { if(!_closed) Status(ex.Message,true); }
        finally { _sending.Dispose(); _sending=null; if(!_closed) { UpdateControls(); FocusInput(); } }
    }
    public async Task BeginDictationAsync()
    {
        if(_listening is not null) { _listening.Cancel(); return; }
        if(_sending is not null || _preparing) return;
        if(_host.IsBusy) { Status("Espera a que termine la operación actual.",true); return; }
        _listening=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); UpdateControls(); Status("Te escucho… Puedes detener el dictado con el micrófono.");
        try { var text=await _listen(_listening.Token); if(!_closed) { Input.Text=(Input.Text+" "+text).Trim(); Status("Revisa el mensaje y pulsa Enter para enviarlo."); } }
        catch(OperationCanceledException) { if(!_closed) Status("Dictado detenido. Puedes escribir o volver a dictar."); }
        catch(Exception ex) { if(!_closed) Status(ex.Message,true); }
        finally { _listening.Dispose(); _listening=null; if(!_closed) { UpdateControls(); FocusInput(); } }
    }
    private void Status(string text,bool error=false) { _status.Text=text; _status.Foreground=Ui.Brush(error?"#FFB8AE":"#D1D8DE"); _status.Visibility=text.Length==0?Visibility.Collapsed:Visibility.Visible; }
    private void UpdateControls()
    {
        var busy=_sending is not null || _preparing; Input.IsReadOnly=busy || _listening is not null; _add.IsEnabled=!busy && _listening is null; _microphone.IsEnabled=!busy;
        _send.IsEnabled=_sending is not null || !busy && _listening is null && (Input.Text.Trim().Length>0 || _images.Count>0);
        _send.Content=Symbol(_sending is null?"M 3,9 L 8,4 L 13,9 M 8,4 V 15":"M 4,4 H 12 V 12 H 4 Z",16);
        var label=_sending is null?"Enviar mensaje":"Cancelar respuesta"; _send.ToolTip=label; AutomationProperties.SetName(_send,label);
        _microphone.Background=_listening is null?Brushes.Transparent:Ui.Brush("#84634E");
        AutomationProperties.SetName(_microphone,_listening is null?"Dictar mensaje":"Detener dictado"); _microphone.ToolTip=_listening is null?"Dictar mensaje":"Detener dictado";
        foreach(var button in _previews.Children.OfType<Border>().Select(b=>b.Child).OfType<Grid>().SelectMany(g=>g.Children.OfType<Button>())) button.IsEnabled=!busy;
    }
    private static UIElement Symbol(string geometry,double size)=>new ShapePath { Width=size,Height=size,Stretch=Stretch.Uniform,Stroke=Brushes.White,StrokeThickness=1.7,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Data=Geometry.Parse(geometry) };
    private static Button RoundButton(string label,string geometry,double size,Func<Task> action)
    {
        var button=new Button { Content=Symbol(geometry,size),Width=30,Height=30,MinHeight=30,Padding=new(0),Margin=new(1),Background=Brushes.Transparent,Foreground=Brushes.White,BorderBrush=Brushes.Transparent,BorderThickness=new(1),ToolTip=label };
        AutomationProperties.SetName(button,label);
        var surface=new FrameworkElementFactory(typeof(Border),"Surface"); surface.SetValue(Border.CornerRadiusProperty,new CornerRadius(16)); surface.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(BackgroundProperty)); surface.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(BorderBrushProperty)); surface.SetValue(Border.BorderThicknessProperty,new Thickness(1));
        var content=new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(HorizontalAlignmentProperty,HorizontalAlignment.Center); content.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center); surface.AppendChild(content);
        var template=new ControlTemplate(typeof(Button)) { VisualTree=surface };
        var focus=new Trigger { Property=IsKeyboardFocusedProperty,Value=true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty,Ui.Brush("#C7E6C9"),"Surface")); template.Triggers.Add(focus);
        var disabled=new Trigger { Property=IsEnabledProperty,Value=false }; disabled.Setters.Add(new Setter(OpacityProperty,.45,"Surface")); template.Triggers.Add(disabled);
        var hover=new Trigger { Property=IsMouseOverProperty,Value=true }; hover.Setters.Add(new Setter(OpacityProperty,.78,"Surface")); template.Triggers.Add(hover); button.Template=template;
        button.Click+=async(_,_)=>await action(); return button;
    }
}
