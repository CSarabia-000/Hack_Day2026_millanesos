using System.Windows.Automation;
using Mindie.Services;
using Mindie.Views;

namespace Mindie.Diagnostics;

public static partial class SelfTest
{
    private static async Task CheckRefinedUiAsync(AppHost host,MainWindow window,string output,Action<bool,string> check)
    {
        window.Navigate("chat"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var chat=FindElements<ChatView>(window).Single();
        FindButtons(chat).Single(b=>AutomationProperties.GetName(b)=="Ordenar una idea").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var prompt=FindElements<TextBox>(chat).Single(t=>AutomationProperties.GetName(t)=="Tu mensaje");
        check(prompt.Text=="Ayúdame a ordenar esta idea: " && host.Messages.Count==0,"Las sugerencias preparan el mensaje para revisarlo y no envían una consulta automáticamente");
        var specific=host.Settings.Specific;
        FindButtons(chat).Single(b=>AutomationProperties.GetName(b)=="Detallada").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(()=>host.Settings.Specific && FindButtons(chat).Single(b=>AutomationProperties.GetName(b)=="Detallada").Foreground==Brushes.White);
        check(host.Settings.Specific && FindButtons(chat).Single(b=>AutomationProperties.GetName(b)=="Detallada").Foreground==Brushes.White,"El selector de detalle del chat actualiza la preferencia y muestra la opción elegida");
        FindButtons(chat).Single(b=>AutomationProperties.GetName(b)=="Breve").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(()=>!host.Settings.Specific && FindButtons(chat).Single(b=>AutomationProperties.GetName(b)=="Breve").Foreground==Brushes.White); prompt.Clear();
        check(ControlsFitHorizontally(chat) && FindSprites(chat).All(s=>!s.Animate),"El chat mantiene visibles sus acciones y todas sus mascotas son estáticas");
        SaveVisual(window,Path.Combine(output,"conversation-empty.png"));
        host.Messages.Add(new() { Role="user",Content="¿Cómo puedo convertir una idea en un proyecto pequeño?" });
        host.Messages.Add(new() { Role="assistant",Content="Empieza con una **versión pequeña** que puedas terminar esta semana.\n\n1. Escribe qué problema resuelve.\n2. Elige una sola función útil.\n3. Pruébala con una persona.\n\n¿Qué parte de tu idea te gustaría construir primero?" });
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(FindButtons(chat).Any(b=>AutomationProperties.GetName(b)=="Copiar respuesta") && FindElements<RichTextBox>(chat).Single().IsReadOnly,"Las respuestas con formato tienen una acción accesible para copiarlas y conservan la selección de texto");
        SaveVisual(window,Path.Combine(output,"conversation-messages.png")); host.Messages.Clear();
        host.Settings.Specific=specific;

        var examples=new[] {
            new MemoryItem { Title="Un método para estudiar con más claridad",Summary="Una rutina breve para comprender mejor lo que lees y conservar las ideas importantes.",Ideas=["Reformula cada concepto con tus propias palabras.","Deja una pregunta para tu próxima sesión."],Category="Estudio",Kind="capture",CreatedAt=DateTimeOffset.Now },
            new MemoryItem { Title="Cómo nacen las ideas que vale la pena guardar",Summary="Una lectura sobre creatividad, observación y conexiones entre disciplinas.",Ideas=["Relaciona la lectura con una experiencia propia.","Anota una aplicación concreta antes de seguir."],Category="Inspiración",Kind="web",SourceUrl="https://example.com/ideas",CreatedAt=DateTimeOffset.Now.AddDays(-1),Favorite=true },
            new MemoryItem { Title="Auto-Encoding Variational Bayes",Summary="Un documento sobre inferencia variacional y el truco de reparametrización en modelos probabilísticos.",Ideas=["La red de reconocimiento aproxima el posterior.","El gradiente estocástico permite entrenar el modelo."],Category="Estudio",Kind="pdf",SourceName="variational-bayes.pdf",PageCount=12,CreatedAt=DateTimeOffset.Now.AddDays(-2),Favorite=true }
        };
        foreach (var item in examples) await host.Store.AddAsync(item,MakeFixture());
        window.Navigate("saved"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var saved=FindElements<SavedView>(window).Single();
        Button Id(string id)=>FindButtons(saved).Single(b=>AutomationProperties.GetAutomationId(b)==id);
        string[] Cards()=>FindElements<Border>(saved).Select(b=>AutomationProperties.GetAutomationId(b)).Where(id=>id.StartsWith("MemoryCard-")).ToArray();
        check(FindButtons(saved).Any(b=>AutomationProperties.GetAutomationId(b)=="MemoryTag-Estudio") && FindButtons(saved).Any(b=>AutomationProperties.GetAutomationId(b)=="MemoryTag-Inspiración"),"La biblioteca muestra las etiquetas existentes como filtros visibles");
        var expectedStudy=host.Store.Data.Memories.Count(m=>m.Category=="Estudio");
        Id("MemoryTag-Estudio").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(Cards().Length==expectedStudy && Cards().Contains("MemoryCard-"+examples[0].Id) && !Cards().Contains("MemoryCard-"+examples[1].Id),"Seleccionar una etiqueta filtra los recuerdos y conserva el recuento real");
        FindButtons(saved).Single(b=>AutomationProperties.GetName(b)=="Favoritos").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(Cards().Contains("MemoryCard-"+examples[2].Id) && !Cards().Contains("MemoryCard-"+examples[0].Id) && !Cards().Contains("MemoryCard-"+examples[1].Id),"Los filtros de etiqueta y favoritos se combinan");
        host.Refresh(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(Cards().Contains("MemoryCard-"+examples[2].Id) && !Cards().Contains("MemoryCard-"+examples[0].Id),"Una actualización del estado de la app conserva los filtros seleccionados");
        FindButtons(saved).Single(b=>AutomationProperties.GetName(b)=="Limpiar filtros").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var search=FindElements<TextBox>(saved).Single(t=>AutomationProperties.GetName(t)=="Buscar recuerdos");
        search.Text="inspiración"; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(Cards().Contains("MemoryCard-"+examples[1].Id) && !Cards().Contains("MemoryCard-"+examples[0].Id),"La búsqueda incluye el nombre de la etiqueta además del título, resumen e ideas");
        search.Clear(); FindButtons(saved).Single(b=>AutomationProperties.GetName(b)=="Favoritos").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(Cards().Contains("MemoryCard-"+examples[1].Id) && !Cards().Contains("MemoryCard-"+examples[0].Id),"El filtro de favoritos muestra solo los recuerdos marcados");
        FindButtons(saved).Single(b=>AutomationProperties.GetName(b)=="Limpiar filtros").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); SaveVisual(window,Path.Combine(output,"saved-redesign.png"));
        var originalWidth=window.Width; window.Width=940; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        SaveVisual(window,Path.Combine(output,"saved-narrow.png"));
        check(ControlsFitHorizontally(saved),"La biblioteca conserva buscador, etiquetas y acciones dentro de la ventana estrecha");
        window.Navigate("home"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        FindButtons(window).Single(b=>AutomationProperties.GetName(b)=="Crear mascota").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var creator=FindElements<PetCreatorView>(window).Single();
        check(ControlsFitHorizontally(creator) && creator.PromptInput.IsReadOnly,"El creador adapta sus pasos a una columna en ventanas estrechas y mantiene el prompt de solo lectura");
        SaveVisual(window,Path.Combine(output,"creator-narrow.png"));
        window.Width=originalWidth; window.Navigate("saved"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        foreach (var item in examples) await host.Store.DeleteAsync(item);
        host.Refresh();
    }
    private static bool ControlsFitHorizontally(FrameworkElement root)
    {
        var overflow=FindButtons(root).Where(b=>b.IsVisible).Select(button=> {
            var point=button.TranslatePoint(new(),root);
            return new { name=AutomationProperties.GetName(button),x=point.X,width=button.ActualWidth,available=root.ActualWidth };
        }).Where(b=>b.x<-.5 || b.x+b.width>b.available+.5).ToArray();
        if (overflow.Length>0) File.WriteAllText(Path.Combine(Environment.CurrentDirectory,"test-results","native","ui-overflow.json"),JsonSerializer.Serialize(overflow));
        return overflow.Length==0;
    }
}
