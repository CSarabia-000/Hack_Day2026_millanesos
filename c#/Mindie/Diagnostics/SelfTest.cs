using Mindie.Controls;
using Mindie.Services;
using Mindie.Windows;

namespace Mindie.Diagnostics;

public static partial class SelfTest
{
    public static async Task<int> RunAsync(bool withOllama)
    {
        var output = Path.Combine(Environment.CurrentDirectory,"test-results","native"); Directory.CreateDirectory(output);
        var checks = new List<string>(); string? error = null;
        void Check(bool condition,string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); File.WriteAllText(Path.Combine(output,"progress.txt"),string.Join(Environment.NewLine,checks)); }
        try
        {
            Check(LibraryStore.ValidateEndpoint("http://127.0.0.1:11434").IsLoopback,"Loopback de Ollama permitido");
            foreach (var endpoint in new[] { "https://example.com", "http://127.0.0.1.evil.test", "http://user:secret@localhost", "http://192.168.1.1:11434", "http://localhost/path" }) { try { LibraryStore.ValidateEndpoint(endpoint); throw new InvalidOperationException("Destino externo aceptado"); } catch (ArgumentException) { } }
            Check(true,"Destinos externos e inyección de URL rechazados");
            var crop = Images.CropRect(new(100,50,200,100),1280,720,1920,1080); Check(crop == new Int32Rect(150,75,300,150),"Selección correcta al 150% de DPI");
            var item = new MemoryItem { Title = "  Un recuerdo  ", Ideas = ["  Organizar  ","","Descansar"], Category = "Inválida" }; LibraryStore.ValidateMemory(item);
            Check(item.Title == "Un recuerdo" && item.Ideas.Count == 2 && item.Category == "Ideas","Metadatos validados y normalizados");
            Check(Math.Abs(new Settings().PetSize-180*.8)<.001,"Widget nuevo un 20% más pequeño: 144 píxeles lógicos");
            var legacyRoot = Path.Combine(output,"legacy-size-"+Guid.NewGuid().ToString("N"));
            var legacy = new LibraryStore(legacyRoot); await legacy.LoadAsync(); legacy.Data.Version = 2; legacy.Data.Settings.PetSize = 200; legacy.Data.Settings.PetLeft = 120; legacy.Data.Messages.Add(new() { Content = "Mensaje que debe conservarse" }); await legacy.SaveAsync();
            var migrated = new LibraryStore(legacyRoot); await migrated.LoadAsync();
            Check(migrated.Data.Version == 5 && migrated.Data.Settings.PetSize == 160 && migrated.Data.Settings.PetLeft == 120 && migrated.Data.Messages.Count == 1,"Tamaño existente reducido al 80% conservando posición e historial");
            var loadedAgain = new LibraryStore(legacyRoot); await loadedAgain.LoadAsync(); Check(loadedAgain.Data.Settings.PetSize == 160,"La reducción no se repite al abrir de nuevo");
            foreach (var pet in Pets.All)
            {
                Check(SpriteCatalog.HasAnimation(pet.Id),$"{pet.Name}: atlas de animación incluido");
                foreach (var state in Enum.GetValues<PetState>())
                {
                    var frames = Enumerable.Range(0,4).Select(i => SpriteCatalog.Frame(pet.Id,state,i)).ToArray();
                    Check(frames.All(f => f.PixelWidth == frames[0].PixelWidth && f.PixelHeight == frames[0].PixelHeight),$"{pet.Name}: cuatro frames alineados para {state}");
                    var first = new FormatConvertedBitmap(frames[0],PixelFormats.Bgra32,null,0); var pixel = new byte[4]; first.CopyPixels(new(0,0,1,1),pixel,4,0);
                    Check(pixel[3] < 12,$"{pet.Name}: fondo transparente en {state}");
                }
            }
            var png = MakeFixture(); await File.WriteAllBytesAsync(Path.Combine(output,"vision-fixture.png"),png);
            var store = new LibraryStore(Path.Combine(output,"library-" + Guid.NewGuid().ToString("N"))); await store.LoadAsync(); await store.AddAsync(item,png);
            var reloaded = new LibraryStore(store.Root); await reloaded.LoadAsync(); Check(reloaded.Data.Memories.Single().Title == item.Title && File.Exists(reloaded.CapturePath(item.Id)),"Persistencia de PNG y biblioteca tras recargar");
            var host = new AppHost(store);
            var window = new MainWindow(host); host.MainWindow = window; window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var mainHandle = new System.Windows.Interop.WindowInteropHelper(window).Handle; NativeMethods.GetWindowRect(mainHandle,out var mainBounds); var workArea = System.Windows.Forms.Screen.FromHandle(mainHandle).WorkingArea;
            Check(mainBounds.Top >= workArea.Top && mainBounds.Left >= workArea.Left && mainBounds.Right <= workArea.Right && mainBounds.Bottom <= workArea.Bottom,"Ventana inicial y controles de título dentro del área útil del monitor");
            var selectorSprites = FindSprites((DependencyObject)window.Content).ToArray(); Check(selectorSprites.Length == Pets.All.Length && selectorSprites.All(s => !s.Animate),"Las ocho tarjetas de selección son estáticas");
            await CheckMindieAsync(host,window,output,Check);
            await CheckShellBrandingAsync(window,output,Check);
            SaveVisual(window,Path.Combine(output,"home.png")); window.Navigate("saved"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); SaveVisual(window,Path.Combine(output,"saved.png")); window.Navigate("settings"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); SaveVisual(window,Path.Combine(output,"settings.png"));
            var petWindow = new PetWindow(host); petWindow.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(petWindow.WindowStyle == WindowStyle.None && petWindow.AllowsTransparency && petWindow.Background == Brushes.Transparent && !petWindow.ShowInTaskbar,"Widget sin marco, sin fondo y sin barra de tareas");
            var petHandle = new System.Windows.Interop.WindowInteropHelper(petWindow).Handle; NativeMethods.GetWindowRect(petHandle,out var petBounds); var petArea = System.Windows.Forms.Screen.FromHandle(petHandle).WorkingArea;
            Check(petBounds.Left >= petArea.Left && petBounds.Top >= petArea.Top && petBounds.Right <= petArea.Right && petBounds.Bottom <= petArea.Bottom,"El widget reducido cabe en el monitor con la escala DPI actual");
            Check(!petWindow.Sprite.HitOpaque(new(0,0)) && petWindow.Sprite.HitOpaque(new(petWindow.Sprite.ActualWidth*.5,petWindow.Sprite.ActualHeight*.6)),"Solo el cuerpo del animal recibe los clics");
            petWindow.BeginDrag(new(petBounds.Left+70,petBounds.Top+70));
            petWindow.UpdateDrag(new(petBounds.Left+25,petBounds.Top+35));
            var dragFrame=petWindow.Sprite.Source; NativeMethods.GetWindowRect(petHandle,out var duringDrag);
            Check(duringDrag.Left==petBounds.Left-45 && duringDrag.Top==petBounds.Top-35 && petWindow.Sprite.State==PetState.Move,"El arrastre desplaza el HWND en píxeles físicos y activa movimiento antes de soltar");
            await Task.Delay(300);
            Check(!ReferenceEquals(dragFrame,petWindow.Sprite.Source) && petWindow.Sprite.State==PetState.Move && !petWindow.AreActionsOpen,"El reloj de animación sigue cambiando fotogramas durante el arrastre y mantiene el menú oculto");
            petWindow.UpdateDrag(new(petBounds.Left-10,petBounds.Top+10)); await petWindow.FinishDragAsync();
            NativeMethods.GetWindowRect(petHandle,out var released);
            Check(host.Settings.PetLeft==released.Left && host.Settings.PetTop==released.Top && petWindow.Sprite.State==PetState.Idle,"Al soltar se guarda la posición final y la mascota vuelve a reposo sin reproducir movimiento tardío");
            petWindow.BeginDrag(new(released.Left+70,released.Top+70)); petWindow.UpdateDrag(new(released.Left-10,released.Top-10)); await petWindow.FinishDragAsync(true);
            NativeMethods.GetWindowRect(petHandle,out var cancelledDrag);
            Check(cancelledDrag.Left==released.Left && cancelledDrag.Top==released.Top && host.Settings.PetLeft==released.Left,"Cancelar el arrastre restaura la posición inicial sin guardar el desplazamiento");
            petWindow.BeginDrag(new(100,100)); petWindow.UpdateDrag(new(100.5,100.5));
            Check(petWindow.Sprite.State==PetState.Idle,"Un clic sin superar el umbral de arrastre no activa la animación de movimiento"); await petWindow.FinishDragAsync(true);
            petWindow.PreviewState(PetState.Move); var frameBefore = petWindow.Sprite.Source; await Task.Delay(250); Check(!ReferenceEquals(frameBefore,petWindow.Sprite.Source),"Los sprites cambian realmente de fotograma en WPF"); petWindow.Touch();
            SaveVisual(petWindow,Path.Combine(output,"pet.png"));
            petWindow.OpenActions(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(petWindow.AreActionsOpen,"La cápsula se abre desde el widget");
            var placements = PetWindow.MenuPlacements(new(128,52),new(144,144),new());
            Check(placements[0].Point == new Point(8,144) && placements[1].Point == new Point(8,-52),"Menú de tres acciones centrado debajo y alternativa superior junto al borde del monitor");
            var petRegion = new Rect(100,100,144,144); var lowerMenu = new Rect(90,254,164,52); var upperMenu = new Rect(90,38,164,52);
            Check(PetWindow.ContainsActionPointer(new(96,249),petRegion,lowerMenu,18) && PetWindow.ContainsActionPointer(new(258,270),petRegion,lowerMenu,18),"Cruzar el hueco en diagonal hacia cualquier icono conserva el menú");
            Check(PetWindow.ContainsActionPointer(new(96,95),petRegion,upperMenu,18),"El espacio protegido también funciona con la cápsula encima");
            Check(PetWindow.ContainsActionPointer(new(192,498),new(200,200,288,288),new(180,508,328,104),36),"Protección del recorrido compatible con escala DPI al 200%");
            Check(!PetWindow.ContainsActionPointer(new(40,249),petRegion,lowerMenu,18),"Alejarse del conjunto permite cerrar el menú");
            petWindow.ActionMenu.Reveal(true); SaveElement(petWindow.ActionMenu,Path.Combine(output,"widget-menu.png"));
            var tooltipButtons = new[] { petWindow.ActionMenu.WriteButton, petWindow.ActionMenu.CaptureButton, petWindow.ActionMenu.LibraryButton };
            for (var index = 0; index < tooltipButtons.Length; index++)
            {
                var button = tooltipButtons[index];
                var tooltip = new ToolTip { Content = button.ToolTip, PlacementTarget = button };
                try
                {
                    tooltip.IsOpen = true; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); tooltip.UpdateLayout();
                    var rendered = SaveElement(tooltip,Path.Combine(output,$"widget-tooltip-{index+1}.png"));
                    Check(HasVisibleTooltipText(rendered),$"Etiqueta «{button.ToolTip}»: letras claras visibles sobre el fondo oscuro");
                }
                finally { tooltip.IsOpen = false; }
            }
            petWindow.CloseActions(); Check(!petWindow.AreActionsOpen,"La cápsula desaparece inmediatamente antes de capturar");
            var writes = 0; var captures = 0; var libraries = 0;
            var capsule = new WidgetActionMenu(()=>writes++,()=>captures++,()=>libraries++);
            var menuButtons = new[] { capsule.WriteButton, capsule.CaptureButton, capsule.LibraryButton };
            foreach (var button in menuButtons) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(writes == 1 && captures == 1 && libraries == 1 && (string)capsule.CaptureButton.ToolTip == "Guardar en mente" && (string)capsule.LibraryButton.ToolTip == "Mis guardados","Los tres iconos funcionan con sus etiquetas accesibles");
            Check(menuButtons.All(button => button.Content is FrameworkElement { Width: 16, Height: 16 }),"Los tres iconos tienen un tamaño compacto de 16 píxeles");
            var dismissed = false; capsule.Reveal(true); capsule.Dismiss(false,()=>dismissed=true); capsule.Reveal(false); await Task.Delay(190);
            Check(!dismissed && !capsule.IsClosing,"Volver al menú interrumpe su cierre sin parpadeos");
            petWindow.Close();
            await CheckKnowledgeAsync(host,window,output,withOllama,Check);
            await CheckMemoryUiAsync(host,window,output,Check);
            await CheckRefinedUiAsync(host,window,output,Check);
            await CheckAnalysisWaitAsync(window,output,Check);
            await CheckWebTextAsync(window,output,Check);
            await CheckWebTextCleanupAsync(window,output,Check);
            await CheckBrainAsync(host,window,output,Check);
            await CheckPreferencesAsync(host,window,output,Check);
            await CheckOptimizationsAsync(host,window,output,Check);
            await CheckQuickPromptAsync(window,output,Check);
            await CheckScreenModelsAsync(window,output,Check);
            await CheckConversationsAsync(window,output,Check);
            await CheckNvidiaAsync(window,output,Check);
            await CheckCapturePresentationAsync(host,window,output,Check);
            await CheckCreationCaptureAsync(host,window,output,withOllama || Environment.GetCommandLineArgs().Contains("--with-web-ollama"),Check);
            if (withOllama)
            {
                var status = await host.Ollama.StatusAsync(host.Settings); Check(status.Available && status.Capabilities.Contains("vision"),"Gemma 4 12b local conectado con visión");
                var reply = await host.Ollama.ChatAsync(host.Settings,[new ChatMessage { Content = "Responde únicamente: Hola, soy Mindie." }],CancellationToken.None); Check(reply.Content.Length > 0,"Conversación real con Gemma");
                var analyzed = await host.Ollama.AnalyzeAsync(host.Settings,png,CancellationToken.None); Check(analyzed.Title.Length > 0 && analyzed.Ideas.Count >= 2,"Análisis visual real con título e ideas");
                await File.WriteAllTextAsync(Path.Combine(output,"ollama-result.json"),JsonSerializer.Serialize(new { reply.Content, analyzed.Title, analyzed.Summary, analyzed.Ideas },new JsonSerializerOptions { WriteIndented = true }));
                host.Messages.Add(new() { Content = "¿Cómo organizo mi día?" }); host.Messages.Add(reply); window.Navigate("chat"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); SaveVisual(window,Path.Combine(output,"chat.png"));
            }
            window.Hide(); host.Dispose();
        }
        catch (Exception ex) { error = ex.ToString(); }
        await File.WriteAllTextAsync(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new { passed = error is null, checks, error },new JsonSerializerOptions { WriteIndented = true }));
        return error is null ? 0 : 1;
    }
    private static byte[] MakeFixture()
    {
        var drawing = new DrawingVisual(); using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(239,243,247)),null,new(0,0,900,400)); var y = 45;
            foreach (var text in new[] { "Tres ideas para estudiar mejor", "1. Organiza tu semana.", "2. Elimina distracciones.", "3. Descansa cada hora.", "Contenido sintético de prueba." }) { context.DrawText(new FormattedText(text,CultureInfo.GetCultureInfo("es-MX"),FlowDirection.LeftToRight,new Typeface("Segoe UI"), y == 45 ? 32 : 24,Brushes.Black,1),new(40,y)); y += 65; }
        }
        var bitmap = new RenderTargetBitmap(900,400,96,96,PixelFormats.Pbgra32); bitmap.Render(drawing); return Images.Encode(bitmap);
    }
    private static void SaveVisual(Window window,string path)
    {
        window.UpdateLayout(); var content=(FrameworkElement)window.Content;
        var bounds=new Rect(0,0,content.ActualWidth+content.Margin.Left+content.Margin.Right,content.ActualHeight+content.Margin.Top+content.Margin.Bottom); var drawing=new DrawingVisual();
        using (var context=drawing.RenderOpen())
        {
            context.DrawRectangle(window.Background??Brushes.White,null,bounds);
            context.DrawRectangle(new VisualBrush(content) { ViewboxUnits=BrushMappingMode.Absolute,Viewbox=bounds,Stretch=Stretch.Fill },null,bounds);
        }
        var bitmap=new RenderTargetBitmap(Math.Max(1,(int)bounds.Width),Math.Max(1,(int)bounds.Height),96,96,PixelFormats.Pbgra32); bitmap.Render(drawing); File.WriteAllBytes(path,Images.Encode(bitmap));
    }
    private static RenderTargetBitmap SaveElement(FrameworkElement visual,string path)
    {
        var bitmap = new RenderTargetBitmap(Math.Max(1,(int)visual.ActualWidth),Math.Max(1,(int)visual.ActualHeight),96,96,PixelFormats.Pbgra32); bitmap.Render(visual); File.WriteAllBytes(path,Images.Encode(bitmap));
        return bitmap;
    }
    private static bool HasVisibleTooltipText(BitmapSource bitmap)
    {
        // Inspect the rendered interior: a white Foreground property alone did not prevent dark text.
        var image = new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0);
        var stride = image.PixelWidth*4; var pixels = new byte[stride*image.PixelHeight]; image.CopyPixels(pixels,stride,0);
        var brightPixels = 0;
        for (var y = 5; y < image.PixelHeight-5; y++)
            for (var x = 5; x < image.PixelWidth-5; x++)
            {
                var offset = y*stride+x*4;
                if (pixels[offset] >= 200 && pixels[offset+1] >= 200 && pixels[offset+2] >= 200 && pixels[offset+3] >= 200) brightPixels++;
            }
        return brightPixels > 12;
    }
    private static IEnumerable<SpritePlayer> FindSprites(DependencyObject root)
    {
        if (root is SpritePlayer sprite) yield return sprite;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in FindSprites(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
}
