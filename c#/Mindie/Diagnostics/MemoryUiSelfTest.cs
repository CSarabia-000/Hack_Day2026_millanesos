using System.Windows.Automation;
using System.Windows.Documents;
using Mindie.Controls;
using Mindie.Services;
using Mindie.Views;
using Mindie.Windows;

namespace Mindie.Diagnostics;

public static partial class SelfTest
{
    private static async Task CheckMemoryUiAsync(AppHost host,MainWindow window,string output,Action<bool,string> check)
    {
        const string answer="El documento presenta el marco de **Auto-Encoding Variational Bayes (AEVB)** y el método **Stochastic Gradient Variational Bayes (SGVB)** [p. 1]. Estos permiten realizar inferencia eficiente en modelos probabilísticos, utilizando el *truco de reparametrización* [p. 1, p. 4].";
        var document=MarkdownMessage.Document(answer);
        var plain=new TextRange(document.ContentStart,document.ContentEnd).Text;
        var runs=DocumentRuns(document.Blocks).ToArray();
        check(!plain.Contains("**") && plain.Contains("AEVB") && plain.Contains("[p. 1, p. 4]"),"Las respuestas muestran el texto sin delimitadores Markdown y conservan las citas de página");
        check(runs.Any(r=>r.Text.Contains("AEVB") && r.FontWeight==FontWeights.Bold) && runs.Any(r=>r.Text.Contains("SGVB") && r.FontWeight==FontWeights.Bold),"Los dos términos de la respuesta de ejemplo se muestran realmente en negrita");
        check(runs.Any(r=>r.Text.Contains("truco de reparametrización") && r.FontStyle==FontStyles.Italic),"Los asteriscos simples se convierten en cursiva nativa");
        var nested=MarkdownMessage.Document("**negrita con *cursiva*** y _otro énfasis_.");
        check(DocumentRuns(nested.Blocks).Any(r=>r.Text.Contains("cursiva") && r.FontWeight==FontWeights.Bold && r.FontStyle==FontStyles.Italic),"La negrita y cursiva anidadas conservan ambos estilos");
        var literal=MarkdownMessage.Document(@"\*literal\* y `a*b` y x_1 y 2 * 3 = 6.");
        var literalText=new TextRange(literal.ContentStart,literal.ContentEnd).Text;
        check(literalText.Contains("*literal*") && literalText.Contains("a*b") && literalText.Contains("x_1") && literalText.Contains("2 * 3"),"Los asteriscos literales, el código y las variables no se eliminan ni se formatean por error");
        var list=MarkdownMessage.Document("## Ideas\n\n- **Una**\n- Otra\n\n3. Primero\n4. Segundo");
        check(list.Blocks.OfType<System.Windows.Documents.List>().Count()==2 && list.Blocks.OfType<System.Windows.Documents.List>().Last().StartIndex==3,"Las respuestas respetan listas y su numeración original");
        var code=MarkdownMessage.Document("```csharp\nvar texto = \"**literal**\";\n```");
        check(new TextRange(code.ContentStart,code.ContentEnd).Text.Contains("**literal**") && DocumentRuns(code.Blocks).All(r=>r.FontFamily.Source=="Consolas"),"Los bloques de código conservan sus símbolos y usan tipografía monoespaciada");
        var links=MarkdownMessage.Document("<b>texto</b> [web](https://example.com/) [otro](javascript:alert(1)) ![imagen](https://example.com/image.png)");
        check(DocumentInlines(links.Blocks).OfType<Hyperlink>().Count()==1 && new TextRange(links.ContentStart,links.ContentEnd).Text.Contains("<b>texto</b>"),"Markdown se presenta como texto nativo sin ejecutar HTML ni cargar imágenes remotas");
        host.Messages.Add(new() { Role="assistant",Content=answer }); window.Navigate("chat"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var rendered=FindElements<RichTextBox>(window).Single();
        check(rendered.IsReadOnly && rendered.IsDocumentEnabled && DocumentRuns(rendered.Document.Blocks).Any(r=>r.FontWeight==FontWeights.Bold),"El chat aplica el formato al historial y permite seleccionar el texto de las respuestas");
        SaveVisual(window,Path.Combine(output,"chat-markdown.png")); host.Messages.Clear();
        host.Messages.Add(new() { Role="assistant",Content=string.Join("\n\n",Enumerable.Repeat(answer,25)) });
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var chat=FindElements<ChatView>(window).Single(); var outerScroll=FindElements<ScrollViewer>(chat).Single(s=>s.Content is StackPanel);
        outerScroll.ScrollToTop(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        FindElements<RichTextBox>(chat).Single().RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120) { RoutedEvent=UIElement.PreviewMouseWheelEvent });
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(outerScroll.VerticalOffset>0,"La rueda sobre una respuesta con formato sigue desplazando la conversación"); host.Messages.Clear();

        var survivors=host.Store.Data.Memories.ToArray(); var png=MakeFixture();
        var capture=new MemoryItem { Title="Eliminar prueba captura",Summary="Se eliminará sin cerrar ninguna ventana." }; await host.Store.AddAsync(capture,png);
        var pdf=new MemoryItem { Kind="pdf",Title="Eliminar prueba PDF",SourceName="documento.pdf",PageCount=3,Summary="Documento sintético para comprobar el borrado." };
        var bytes=PdfFixture(); var knowledge=PdfService.Extract(bytes,pdf.SourceName,CancellationToken.None);
        knowledge.Messages.Add(new() { Role="assistant",Content=answer }); await host.Store.AddPdfAsync(pdf,new(bytes,png,knowledge));
        var confirmed=false; var view=new SavedView(host,_=>confirmed); window.Navigate("saved"); window.PageHost.Content=view; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var search=FindElements<TextBox>(view).Single(t=>AutomationProperties.GetName(t)=="Buscar recuerdos"); search.Text="Eliminar prueba"; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var deleteButtons=FindButtons(view).Where(b=>AutomationProperties.GetAutomationId(b).StartsWith("DeleteMemory-")).ToArray();
        check(deleteButtons.Length==2 && deleteButtons.All(b=>b.ToolTip is string tip && tip=="Eliminar recuerdo" && b.Content is System.Windows.Shapes.Path),"Cada tarjeta de captura y PDF ofrece una papelera compacta con nombre accesible");
        SaveVisual(window,Path.Combine(output,"saved-delete-icons.png"));
        var editor=new MemoryEditorWindow(host,png,capture) { Owner=window }; editor.Show();
        var viewer=new PdfKnowledgeWindow(host,pdf,knowledge) { Owner=window }; viewer.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Button DeleteButton(string id)=>FindButtons(view).Single(b=>AutomationProperties.GetAutomationId(b)=="DeleteMemory-"+id);
        DeleteButton(capture.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(host.Store.Data.Memories.Any(m=>m.Id==capture.Id) && File.Exists(host.Store.CapturePath(capture.Id)),"Cancelar la confirmación conserva el recuerdo y su captura");
        confirmed=true; DeleteButton(capture.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(()=>!FindButtons(view).Any(b=>AutomationProperties.GetAutomationId(b)=="DeleteMemory-"+capture.Id));
        check(window.IsVisible && window.CurrentPage=="saved" && editor.IsVisible && viewer.IsVisible && search.Text=="Eliminar prueba","Eliminar una tarjeta conserva abiertas la biblioteca, el editor y el lector, manteniendo la búsqueda");
        check(!File.Exists(host.Store.CapturePath(capture.Id)) && FindButtons(editor).Single(b=>AutomationProperties.GetName(b)=="Guardar cambios").IsEnabled==false,"El recuerdo borrado desaparece del disco y su editor abierto no puede volver a guardarlo");
        DeleteButton(pdf.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(()=>!FindButtons(view).Any(b=>AutomationProperties.GetAutomationId(b)=="DeleteMemory-"+pdf.Id));
        check(viewer.IsVisible && !Directory.Exists(host.Store.DocumentDirectory(pdf.Id)) && !File.Exists(host.Store.CapturePath(pdf.Id)),"Eliminar un PDF retira sus archivos y conversación guardada sin cerrar el lector");
        check(FindButtons(viewer).Single(b=>AutomationProperties.GetName(b)=="Abrir PDF original").IsEnabled==false && FindButtons(viewer).Single(b=>AutomationProperties.GetName(b)=="Enviar").IsEnabled==false,"El lector avisa del PDF eliminado y desactiva acciones que necesitan el archivo");
        var reloaded=new LibraryStore(host.Store.Root); await reloaded.LoadAsync();
        check(!reloaded.Data.Memories.Any(m=>m.Id==capture.Id || m.Id==pdf.Id) && survivors.All(s=>reloaded.Data.Memories.Any(m=>m.Id==s.Id) && File.Exists(reloaded.CapturePath(s.Id))),"Tras recargar solo faltan los recuerdos elegidos y se conservan los demás");
        editor.Close(); viewer.Close(); window.Navigate("saved");
    }
    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline=Environment.TickCount64+5000;
        while (!condition()) { if (Environment.TickCount64>deadline) throw new TimeoutException("La tarjeta no se actualizó después de eliminar."); await Task.Delay(20); }
    }
    private static IEnumerable<T> FindElements<T>(DependencyObject? root) where T:DependencyObject
    {
        if(root is null) yield break;
        if (root is T item) yield return item;
        for (var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) foreach (var child in FindElements<T>(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
    private static IEnumerable<Inline> DocumentInlines(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph) foreach (var inline in NestedInlines(paragraph.Inlines)) yield return inline;
            else if (block is Section section) foreach (var inline in DocumentInlines(section.Blocks)) yield return inline;
            else if (block is System.Windows.Documents.List list) foreach (var item in list.ListItems) foreach (var inline in DocumentInlines(item.Blocks)) yield return inline;
        }
    }
    private static IEnumerable<Inline> NestedInlines(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            yield return inline;
            if (inline is Span span) foreach (var child in NestedInlines(span.Inlines)) yield return child;
        }
    }
    private static IEnumerable<Run> DocumentRuns(BlockCollection blocks)=>DocumentInlines(blocks).OfType<Run>();
}
