using Mindie.Controls;
using Mindie.Services;
using Mindie.Views;
using Mindie.Windows;
using System.Windows.Automation;
using System.Windows.Media.Media3D;

namespace Mindie.Diagnostics;

public static partial class SelfTest
{
    private static async Task CheckBrainAsync(AppHost host,MainWindow window,string output,Action<bool,string> check)
    {
        var nav=new[] { window.ChatNav,window.HomeNav,window.SavedNav,window.BrainNav };
        check(nav.Zip(nav.Skip(1),(a,b)=>a.TranslatePoint(new(),window).Y<b.TranslatePoint(new(),window).Y).All(v=>v),"El menú ordena Conversación, Compañeros, Mis guardados y Cerebro digital");
        var date=DateTimeOffset.Now.AddDays(-10);
        var records=new[] {
            new MemoryItem { Title="Inferencia variacional en modelos de lenguaje",Summary="Los modelos probabilísticos utilizan inferencia variacional y representaciones latentes.",Ideas=["Comparar inferencia y aprendizaje","Revisar representaciones de lenguaje"],Category="Estudio",CreatedAt=date },
            new MemoryItem { Title="Escritura y modelos de lenguaje",Summary="Una investigación compara el lenguaje y las representaciones de modelos probabilísticos.",Ideas=["Relacionar escritura y aprendizaje","Revisar los modelos de lenguaje"],Category="Inspiración",CreatedAt=date.AddMinutes(1) },
            new MemoryItem { Title="Ideas para un asistente local",Summary="Un asistente de lenguaje que ayuda a organizar escritura e ideas.",Ideas=["Conectar modelos y escritura"],Category="Ideas",CreatedAt=date.AddMinutes(2) },
            new MemoryItem { Title="Planificar la semana con pausas",Summary="Planificación de tareas, descanso y prioridades para organizar la semana.",Ideas=["Reservar pausas","Definir prioridades"],Category="Productividad",CreatedAt=date.AddMinutes(3) },
            new MemoryItem { Title="Descanso y hábitos personales",Summary="Pausas y descanso permiten sostener hábitos personales durante la semana.",Ideas=["Combinar hábitos y pausas"],Category="Personal",CreatedAt=date.AddMinutes(4) },
            new MemoryItem { Title="Estudiar redes neuronales",Summary="El aprendizaje usa modelos probabilísticos y representaciones.",Ideas=["Aprendizaje e inferencia variacional"],Category="Estudio",CreatedAt=date.AddMinutes(5) }
        };
        var graph=KnowledgeGraphBuilder.Build(records);
        check(graph.Nodes.Count(n=>n.IsMemory)==records.Length && graph.Nodes.Count(n=>!n.IsMemory && !n.IsRoot)==5 && graph.Nodes.Select(n=>n.Id).Distinct().Count()==graph.Nodes.Count,"El grafo representa cada recuerdo real una sola vez y lo agrupa en su zona de conocimiento");
        check(graph.Links.Any(e=>!e.IsTopic && new[] { e.From.Id,e.To.Id }.Contains(records[0].Id) && new[] { e.From.Id,e.To.Id }.Contains(records[1].Id)),"Los términos compartidos conectan recuerdos relacionados incluso entre categorías distintas");
        check(!graph.Links.Any(e=>!e.IsTopic && new[] { e.From.Id,e.To.Id }.Contains(records[0].Id) && new[] { e.From.Id,e.To.Id }.Contains(records[4].Id)),"Recuerdos sin términos relevantes en común no reciben una relación inventada");
        var next=KnowledgeGraphBuilder.Build(records.Append(new MemoryItem { Title="Más lenguaje e inferencia",Category="Estudio",CreatedAt=date.AddDays(1) }).ToArray());
        check(graph.Nodes.Where(n=>n.IsMemory).All(n=>next.Nodes.Single(p=>p.Id==n.Id).Position==n.Position),"Añadir conocimiento conserva la posición automática de los recuerdos existentes");
        var empty=KnowledgeGraphBuilder.Build([]);
        check(empty.Nodes.Count==1 && empty.Nodes[0].IsRoot && empty.Links.Count==0,"Una biblioteca vacía no presenta nodos ni relaciones de conocimiento ficticios");
        using(var cancelled=new CancellationTokenSource()) { cancelled.Cancel(); try { KnowledgeGraphBuilder.Build(records,cancellation:cancelled.Token); check(false,"Cancelar grafo"); } catch(OperationCanceledException) { check(true,"La construcción del grafo respeta la cancelación"); } }
        var bulk=Enumerable.Range(0,1000).Select(i=>new MemoryItem { Id=Guid.NewGuid().ToString(),Title=$"Aprendizaje {i}",Summary="Representaciones de lenguaje e inferencia variacional para estudiar modelos.",Category=Pets.Categories[i%5],CreatedAt=date.AddSeconds(i) }).ToArray();
        var large=KnowledgeGraphBuilder.Build(bulk);
        check(large.Nodes.Count(n=>n.IsMemory)==1000 && large.Links.Count(e=>!e.IsTopic)<=2000 && large.Nodes.Where(n=>n.IsMemory).All(n=>double.IsFinite(n.Position.X) && double.IsFinite(n.Position.Y)),"Mil recuerdos generan un grafo finito con enlaces acotados en lugar de conectar todos con todos");
        foreach(var item in records) await host.Store.AddAsync(item,MakeFixture());
        window.Navigate("brain"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var view=(BrainView)window.PageHost.Content; await view.RebuildAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(window.CurrentPage=="brain" && window.Breadcrumb.Text.Contains("Cerebro digital") && view.Scene.Graph.Nodes.Any(n=>n.Id==records[0].Id),"La nueva sección abre el grafo y usa los recuerdos de Mis guardados");
        SaveVisual(window,Path.Combine(output,"brain-overview.png"));
        var scene=view.Scene; var node=scene.Graph.Nodes.Single(n=>n.Id==records[0].Id); var original=node.Position;
        var nativeWorld=(Model3DGroup)((ModelVisual3D)scene.Viewport.Children[0]).Content;
        check(scene.Viewport.Camera is PerspectiveCamera && GraphGeometry3D.Sphere.IsFrozen && GraphGeometry3D.Cylinder.IsFrozen && GraphGeometry3D.Sphere.Bounds.SizeZ>1.9 && !nativeWorld.Children.OfType<GeometryModel3D>().Any(),"La escena conserva el volumen nativo de los recuerdos sin ninguna de las dos mallas cerebrales anteriores");
        check(scene.Graph.Nodes.Where(n=>n.IsMemory).Select(n=>n.Depth).Distinct().Count()>3,"Los recuerdos se distribuyen en profundidad además de los ejes horizontal y vertical");
        var corner=new byte[4]; BrainImage(scene).CopyPixels(new(1,1,1,1),corner,4,0);
        check(corner[3]==255 && corner.Take(3).All(v=>v==0) && scene.Background==Brushes.Black,"La esfera estelar se muestra sobre negro puro opaco");
        check(AstralShellBackdrop.Stars.Length==960 && AstralShellBackdrop.Filaments.Length<960*4 && AstralShellBackdrop.Stars.All(p=>Math.Abs(((Vector3D)p).Length-AstralShellBackdrop.Radius)<.001),"El único globo contiene estrellas distribuidas sobre una esfera con filamentos acotados");
        check(scene.Graph.Nodes.Count(n=>n.IsMemory)==host.Store.Data.Memories.Count && AstralShellBackdrop.Stars.Length>scene.Graph.Nodes.Count,"Las estrellas de ambientación no se convierten en recuerdos ni alteran los recuentos de la biblioteca");
        scene.ReduceMotion=true; check(!scene.AmbientRunning,"Reducir movimiento detiene la animación de la esfera");
        var angleBefore=scene.AmbientAngle; await Task.Delay(120);
        check(scene.AmbientAngle==angleBefore,"Una esfera pausada no mantiene cambios de posición en segundo plano");
        scene.ReduceMotion=false; scene.AnimateShell=false;
        check(!scene.AmbientRunning,"El control de pausa permite detener el ambiente sin bloquear el grafo");
        scene.AnimateShell=true; scene.Select(node.Id);
        check(!scene.AmbientRunning,"Seleccionar un recuerdo pausa el ambiente mientras se consulta su contenido");
        scene.Select(null); window.Activate(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var originalState=window.WindowState; window.WindowState=WindowState.Minimized; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(!scene.AmbientRunning,"Minimizar Mindie detiene el reloj de la esfera");
        angleBefore=scene.AmbientAngle; await Task.Delay(120); check(scene.AmbientAngle==angleBefore,"La esfera no acumula fotogramas cuando la ventana está minimizada");
        window.WindowState=originalState; window.Activate(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        if(SystemParameters.ClientAreaAnimation && window.IsActive) {
            await UntilAsync(()=>scene.AmbientRunning); angleBefore=scene.AmbientAngle;
            await Task.Delay(150); check(scene.AmbientAngle!=angleBefore,"El globo gira lentamente cuando está visible y activo sin un recuerdo seleccionado");
        }
        scene.AnimateShell=false;
        var positions=scene.Graph.Nodes.Select(n=>n.SpatialPosition).ToArray(); var projected=scene.ToView(node.SpatialPosition); var firstImage=BrainImage(scene);
        scene.Orbit(45,18); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check((scene.ToView(node.SpatialPosition)-projected).Length>10 && !SamePixels(firstImage,BrainImage(scene)) && positions.SequenceEqual(scene.Graph.Nodes.Select(n=>n.SpatialPosition)),"Girar la cámara cambia la proyección y el renderizado sin modificar los recuerdos");
        SaveVisual(window,Path.Combine(output,"brain-orbit.png")); scene.Fit();
        var yaw=scene.Yaw; scene.BeginPointer(new(8,8)); scene.MovePointer(new(68,35)); scene.EndPointer();
        check(scene.Yaw!=yaw,"Arrastrar el espacio libre gira la cámara tridimensional");
        scene.Fit(); projected=scene.ToView(node.SpatialPosition); yaw=scene.Yaw; scene.PanMode=true;
        scene.BeginPointer(new(8,8)); scene.MovePointer(new(50,40)); scene.EndPointer();
        check((scene.ToView(node.SpatialPosition)-projected).Length>15 && scene.Yaw==yaw,"El modo Mover desplaza la cámara sin girarla ni mover los nodos"); scene.PanMode=false; scene.Fit();
        var legacy=KnowledgeGraphBuilder.Build(records,new Dictionary<string,KnowledgeNodePosition> { [node.Id]=new(.61,.42,node.Category) }).Nodes.Single(n=>n.Id==node.Id);
        check((legacy.Position-new Point(610,302.4)).Length<.01 && double.IsFinite(legacy.Depth),"Las posiciones bidimensionales anteriores se recuperan con profundidad compatible");
        scene.Select(node.Id); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var before=BrainImage(scene);
        var spatialStart=node.SpatialPosition; var grab=scene.ToView(spatialStart);
        scene.BeginPointer(grab); scene.MovePointer(grab+new Vector(-22,12));
        scene.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(scene),Environment.TickCount,Key.Escape) { RoutedEvent=UIElement.KeyDownEvent });
        check(!scene.IsDragging && (node.SpatialPosition-spatialStart).Length<.001,"Escape cancela el arrastre y recupera los tres ejes sin confirmar la posición intermedia");
        scene.Select(node.Id);
        scene.BeginPointer(grab); scene.MovePointer(grab+new Vector(48,35));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(scene.IsDragging && node.Position!=original && scene.Graph.Connections(node).Any() && !SamePixels(before,BrainImage(scene)),"Arrastrar un nodo lo desplaza y redibuja sus conexiones iluminadas durante el movimiento");
        check((scene.ToView(node.SpatialPosition)-(grab+new Vector(48,35))).Length<1,"El nodo tridimensional permanece bajo el puntero durante el arrastre con perspectiva");
        SaveVisual(window,Path.Combine(output,"brain-dragging.png")); scene.EndPointer();
        await UntilAsync(()=>host.Store.Data.GraphPositions.ContainsKey(node.Id));
        await host.Store.SaveAsync(); var reload=new LibraryStore(host.Store.Root); await reload.LoadAsync();
        var stored=KnowledgeGraphBuilder.Build(records,reload.Data.GraphPositions).Nodes.Single(n=>n.Id==node.Id);
        check((stored.SpatialPosition-node.SpatialPosition).Length<.001 && reload.Data.GraphPositions[node.Id].Z is not null,"La posición tridimensional arrastrada se conserva al guardar y recargar la biblioteca");
        var beforeNear=node.SpatialPosition; var other=scene.Graph.Nodes.Single(n=>n.Id==records[4].Id); var linkCount=scene.Graph.Links.Count;
        scene.NudgeSpatialSelected(other.SpatialPosition+new Vector3D(.55,0,0)-node.SpatialPosition);
        grab=scene.ToView(node.SpatialPosition); scene.BeginPointer(grab); scene.MovePointer(grab+new Vector(8,0));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(scene.Nearby(node).Contains(other) && scene.Graph.Links.Count==linkCount,"Acercar un nodo a otro muestra proximidad sin inventar una relación permanente");
        scene.EndPointer(); scene.Select(node.Id); scene.NudgeSpatialSelected(beforeNear-node.SpatialPosition);
        var moved=node.Position;
        FindButtons(view).Single(b=>AutomationProperties.GetName(b)=="Mover nodo a la derecha").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(node.Position.X>moved.X,"Los botones permiten mover el nodo con clics sin depender del arrastre");
        var depthBefore=node.SpatialPosition;
        FindButtons(view).Single(b=>AutomationProperties.GetName(b)=="Acercar nodo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check((node.SpatialPosition-depthBefore).Length>.17 && Math.Abs(node.Depth-depthBefore.Z)>.1,"Los controles de profundidad mueven el nodo en el tercer eje");
        await host.Store.SaveGraphPositionAsync(node.Id,node.SpatialPosition,node.Category);
        reload=new LibraryStore(host.Store.Root); await reload.LoadAsync();
        stored=KnowledgeGraphBuilder.Build(records,reload.Data.GraphPositions).Nodes.Single(n=>n.Id==node.Id);
        check((stored.SpatialPosition-node.SpatialPosition).Length<.001,"El desplazamiento en profundidad persiste al recargar los metadatos");
        scene.Select(node.Id); scene.NudgeSelected(new(0,25)); var angle=scene.Zoom; scene.ChangeZoom(1.5); check(scene.Zoom>angle,"El zoom del grafo responde a sus controles");
        scene.Fit(); check(scene.Zoom==1,"Centrar restaura la escala del mapa");
        scene.Select(null);
        scene.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(scene),Environment.TickCount,Key.Right) { RoutedEvent=UIElement.KeyDownEvent });
        check(scene.Selected?.IsMemory==true,"Las flechas del teclado permiten recorrer los recuerdos del grafo");
        var search=FindElements<TextBox>(view).Single(t=>AutomationProperties.GetName(t)=="Buscar en el cerebro digital"); search.Text="escritura"; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(scene.Graph.Nodes.Count(n=>n.IsMemory && scene.Matches(n))>=2 && scene.Selected is null && FindButtons(view).Any(b=>AutomationProperties.GetName(b).StartsWith("Seleccionar recuerdo:")),"La búsqueda ilumina coincidencias y ofrece resultados seleccionables por teclado o clic");
        search.Clear(); scene.Select(records[0].Id); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        FindButtons(view).Single(b=>AutomationProperties.GetName(b)=="Abrir recuerdo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(()=>System.Windows.Application.Current.Windows.OfType<MemoryEditorWindow>().Any());
        var editor=System.Windows.Application.Current.Windows.OfType<MemoryEditorWindow>().Single(); check(editor.IsVisible,"Un nodo permite abrir y consultar su recuerdo real"); editor.Close();
        var added=new MemoryItem { Title="Un nuevo aprendizaje de lenguaje",Summary="Inferencia variacional y aprendizaje de modelos de lenguaje.",Category="Estudio",CreatedAt=date.AddDays(2) };
        await host.Store.AddAsync(added,MakeFixture()); host.Refresh();
        await UntilAsync(()=>scene.Graph.Nodes.Any(n=>n.Id==added.Id));
        check(scene.Graph.Links.Any(e=>e.From.Id==added.Id || e.To.Id==added.Id),"Guardar más contenido actualiza automáticamente el grafo y su zona");
        await host.Store.DeleteAsync(added); host.Refresh(); await UntilAsync(()=>scene.Graph.Nodes.All(n=>n.Id!=added.Id));
        check(scene.Graph.Links.All(e=>e.From.Id!=added.Id && e.To.Id!=added.Id),"Eliminar un recuerdo retira sus nodos y enlaces sin cerrar la vista");
        var width=window.Width; var height=window.Height; window.Width=940; window.Height=650; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        SaveVisual(window,Path.Combine(output,"brain-narrow.png"));
        check(ControlsFitHorizontally(view) && scene.ActualHeight>100 && FindButtons(view).Single(b=>AutomationProperties.GetName(b)=="Centrar").IsVisible,"El cerebro se adapta a una ventana estrecha conservando el mapa y los controles");
        FindButtons(view).Single(b=>AutomationProperties.GetName(b)=="Detalles").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(FindButtons(view).Single(b=>AutomationProperties.GetName(b)=="Abrir recuerdo").IsVisible && scene.ActualHeight>200,"En ventanas pequeñas el panel de detalles se despliega sin reducir la altura del mapa");
        FindButtons(view).Single(b=>AutomationProperties.GetName(b)=="Cerrar detalles").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.Width=width; window.Height=height;
        await host.Store.ResetGraphPositionsAsync(); await view.RebuildAsync();
        check(host.Store.Data.GraphPositions.Count==0 && view.Scene.Graph.Nodes.Single(n=>n.Id==records[0].Id).Position==original,"Reorganizar devuelve los nodos a su zona sin borrar conocimientos");
        var restored=view.Scene.Graph;
        view.Scene.SetGraph(large); view.Scene.Fit(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var world=(Model3DGroup)((ModelVisual3D)view.Scene.Viewport.Children[0]).Content;
        var spheres=world.Children.OfType<Model3DGroup>().SelectMany(g=>g.Children.OfType<GeometryModel3D>()).Where(m=>ReferenceEquals(m.Geometry,GraphGeometry3D.Sphere)).ToArray();
        check(spheres.Length==large.Nodes.Count(n=>!n.IsRoot) && spheres.All(m=>m.Material.IsFrozen),"Mil recuerdos se muestran con una única malla esférica compartida y materiales inmutables sin una bola central");
        var farNode=large.Nodes.First(n=>n.IsMemory); farNode.SpatialPosition=new(5.4,-3.8,3.7); view.Scene.SetGraph(large); view.Scene.Fit();
        var fitted=view.Scene.ToView(farNode.SpatialPosition);
        check(fitted.X>0 && fitted.X<view.Scene.ActualWidth && fitted.Y>0 && fitted.Y<view.Scene.ActualHeight,"Centrar incluye un nodo desplazado lejos del origen también en profundidad");
        view.Scene.SetGraph(KnowledgeGraphBuilder.Build(Enumerable.Range(1,12).Select(i=>new MemoryItem { Title=$"Detección y estudio de lenguaje · {i}",Category="Estudio",Summary="Lenguaje e investigación de modelos de inteligencia artificial.",CreatedAt=date.AddMinutes(i) }).ToArray()));
        view.Scene.Fit(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); SaveVisual(window,Path.Combine(output,"brain-single-zone.png"));
        check(view.Scene.Graph.Nodes.Count(n=>n.IsMemory)==12 && view.Scene.Graph.Nodes.Where(n=>n.IsMemory).Select(n=>n.Depth).Distinct().Count()==12,"Una sola categoría mantiene sus doce recuerdos distribuidos en el espacio tridimensional");
        view.Scene.SetGraph(restored); view.Scene.Fit();
        host.Store.Data.GraphPositions[records[0].Id]=new(.6,300.0/720,records[0].Category); await host.Store.SaveAsync();
        foreach(var item in records) await host.Store.DeleteAsync(item); host.Refresh(); await view.RebuildAsync();
        check(records.All(item=>view.Scene.Graph.Nodes.All(n=>n.Id!=item.Id)) && records.All(item=>!host.Store.Data.GraphPositions.ContainsKey(item.Id)),"La limpieza de recuerdos elimina también sus posiciones guardadas");
        window.Navigate("saved"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(!scene.AmbientRunning,"Salir de Cerebro digital descarga y detiene el reloj de ambientación");
    }
    private static RenderTargetBitmap BrainImage(FrameworkElement scene)
    {
        var drawing=new DrawingVisual(); using(var context=drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(scene),null,new Rect(0,0,scene.ActualWidth,scene.ActualHeight));
        var image=new RenderTargetBitmap((int)scene.ActualWidth,(int)scene.ActualHeight,96,96,PixelFormats.Pbgra32); image.Render(drawing); return image;
    }
}
