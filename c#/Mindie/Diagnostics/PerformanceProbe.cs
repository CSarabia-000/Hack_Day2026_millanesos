using System.Diagnostics;
using System.Reflection;
using System.Windows.Automation;

namespace Mindie.Diagnostics;

public static partial class SelfTest
{
    public static int PrepareSpritePreviews()
    {
        var folder=Path.Combine(Environment.CurrentDirectory,"Mindie","Assets","Previews"); Directory.CreateDirectory(folder);
        foreach(var pet in Pets.All) File.WriteAllBytes(Path.Combine(folder,pet.Id+".png"),Services.Images.Encode(Controls.SpriteCatalog.Frame(pet.Id,PetState.Idle,0)));
        return 0;
    }
    public static async Task<int> RunPerformanceProbeAsync(bool baseline)
    {
        var output=Path.Combine(Environment.CurrentDirectory,"test-results"); Directory.CreateDirectory(output);
        var store=new Services.LibraryStore(Path.Combine(output,"performance-"+Guid.NewGuid().ToString("N"))); await store.LoadAsync();
        using var host=new AppHost(store);
        var clock=Stopwatch.StartNew(); var window=new MainWindow(host); host.MainWindow=window; window.Show();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); clock.Stop();
        var homeMs=clock.Elapsed.TotalMilliseconds;
        var cache=(System.Collections.IDictionary)typeof(Controls.SpriteCatalog).GetField("Cache",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        var frames=cache.Values.Cast<Array>().Sum(a=>a.Length);
        var fixture=MakeFixture();
        for(var i=0;i<400;i++) {
            var item=new MemoryItem { Title=$"Recuerdo sintético {i}",Summary="Ideas sobre aprendizaje, lectura y creatividad.",Ideas=["Una conexión útil","Una aplicación concreta"],Category=Pets.Categories[i%5],CreatedAt=DateTimeOffset.UtcNow.AddMinutes(-i) };
            store.Data.Memories.Add(item);
            if(i<80) await File.WriteAllBytesAsync(store.CapturePath(item.Id),fixture);
        }
        clock.Restart(); var allocated=GC.GetTotalAllocatedBytes(true); window.Navigate("saved");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); clock.Stop();
        var libraryMs=clock.Elapsed.TotalMilliseconds; var libraryAllocated=GC.GetTotalAllocatedBytes(true)-allocated;
        var realized=FindElements<Border>(window).Count(b=>AutomationProperties.GetAutomationId(b).StartsWith("MemoryCard-",StringComparison.Ordinal));
        var imageBytes=FindElements<Image>(window.PageHost).Select(i=>i.Source).OfType<BitmapSource>().Sum(s=>(long)s.PixelWidth*s.PixelHeight*4);
        clock.Restart(); allocated=GC.GetTotalAllocatedBytes(true);
        for(var i=0;i<3;i++) { host.Refresh(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
        clock.Stop();
        var report=new { synthetic=true,memories=400,images=80,homeMs,preparedAnimationFrames=frames,libraryMs,libraryAllocatedBytes=libraryAllocated,realizedCards=realized,visibleImageDecodedBytes=imageBytes,unchangedRefreshMs=clock.Elapsed.TotalMilliseconds,unchangedRefreshAllocatedBytes=GC.GetTotalAllocatedBytes(true)-allocated,panels=FindElements<VirtualizingStackPanel>(window.PageHost).Select(p=>new { p.ActualHeight,p.ViewportHeight,p.ExtentHeight,contentScroll=p.ScrollOwner?.CanContentScroll,virtualized=VirtualizingPanel.GetIsVirtualizing(p),children=p.Children.Count }).ToArray() };
        await File.WriteAllTextAsync(Path.Combine(output,baseline?"performance-baseline.json":"performance-after.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented=true }));
        window.Close(); return 0;
    }
}
