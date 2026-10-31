using Mindie.Services;
using Mindie.Windows;

namespace Mindie;

public sealed partial class AppHost
{
    public Task CaptureAsync()=>CaptureCoreAsync(null);
    public Task CaptureFromPetAsync(PetWindow origin)=>CaptureCoreAsync(origin);
    private async Task CaptureCoreAsync(PetWindow? origin)
    {
        if(IsBusy || _capturing) { Report("Termina o cancela la operación actual antes de iniciar otra captura."); return; }
        _capturing=true;
        try
        {
            var presentation=new CapturePresentation(origin,MainWindow);
            PetWindow?.CloseActions(); origin?.CloseActions();
            var choice=presentation.Dialog(new CaptureModeWindow());
            if(choice.ShowDialog()!=true) return;
            if(choice.Mode==MemoryCaptureMode.WebText)
            {
                var reader=presentation.Dialog(new WebTextCaptureWindow(Settings.GiveContext));
                if(reader.ShowDialog()!=true || reader.Result is not { } textPage) return;
                if(origin is null) Open("saved");
                presentation.Editor(new MemoryEditorWindow(this,WebTextThumbnail.Create(textPage),context:reader.ContextText,sourceUrl:textPage.Url,webText:textPage),MainWindow).Show();
                return;
            }
            byte[]? png; var sourceUrl=""; var pageTitle=""; var fullPage=choice.Mode==MemoryCaptureMode.FullPage;
            if(fullPage)
            {
                var browser=presentation.Dialog(new FullPageCaptureWindow(Store.Root));
                if(browser.ShowDialog()!=true || browser.Result is null) return;
                png=browser.Result.Png; sourceUrl=browser.Result.Url; pageTitle=browser.Result.Title;
            }
            else png=await Capture.SelectAsync();
            if(png is null) return;
            if(origin is null) Open("saved");
            var context="";
            if(Settings.GiveContext)
            {
                var dialog=presentation.Dialog(new CaptureContextWindow(png,fullPage)); dialog.SourceInput.Text=sourceUrl;
                if(dialog.ShowDialog()!=true) return;
                context=dialog.ContextText; sourceUrl=dialog.SourceUrl;
            }
            presentation.Editor(new MemoryEditorWindow(this,png,context:context,sourceUrl:sourceUrl,fullPage:fullPage,pageTitle:pageTitle),MainWindow).Show();
        }
        finally { _capturing=false; }
    }
}
