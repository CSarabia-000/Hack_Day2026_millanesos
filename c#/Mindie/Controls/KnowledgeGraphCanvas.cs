using Mindie.Services;
using Mindie.Views;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media.Media3D;

namespace Mindie.Controls;

/// <summary>Native knowledge scene with a projected decorative star shell.</summary>
public sealed partial class KnowledgeGraphCanvas:Grid
{
    internal Viewport3D Viewport { get; }=new() { ClipToBounds=true,IsHitTestVisible=false };
    private readonly PerspectiveCamera _camera=new() { FieldOfView=42,NearPlaneDistance=.05,FarPlaneDistance=150 };
    private readonly Model3DGroup _scene=new(),_edges=new(),_points=new(),_proximity=new();
    private readonly Dictionary<KnowledgeNode,GeometryModel3D> _nodeModels=[];
    private readonly Dictionary<KnowledgeLink,GeometryModel3D> _edgeModels=[];
    private readonly Dictionary<KnowledgeNode,(Point3D Position,bool Active)> _nodePoses=[];
    private readonly Dictionary<KnowledgeLink,(Point3D From,Point3D To,double Radius)> _edgePoses=[];
    private readonly Dictionary<string,KnowledgeNode> _lookup=[];
    private readonly Labels _labels;
    private readonly AstralShellBackdrop _shell;
    private KnowledgeNode? _selected,_hovered,_pending;
    private string _query="";
    private double _yaw=-18,_pitch=12,_zoom=1,_fitDistance=10;
    private Point3D _target=new();
    private (Vector3D Forward,Vector3D Right,Vector3D Up) _basis;
    private double _projectionTan=Math.Tan(42*Math.PI/360);
    private static readonly Brush Ideas=Ui.Brush("#197D8D"),Study=Ui.Brush("#4269AD"),Inspiration=Ui.Brush("#8065AA"),Productivity=Ui.Brush("#9D7626"),Personal=Ui.Brush("#467C5C"),Fallback=Ui.Brush("#506B83");
    private static readonly Brush StageFallback=Ui.Brush("#C8BFFF");
    private static readonly IReadOnlyDictionary<string,Brush> StageColors=new Dictionary<string,Brush> {
        ["Ideas"]=Ui.Brush("#87D9EB"),["Estudio"]=Ui.Brush("#B4A8FF"),["Inspiración"]=Ui.Brush("#D9B8ED"),["Productividad"]=Ui.Brush("#E8CD95"),["Personal"]=Ui.Brush("#A2DAB8")
    };
    private static readonly Dictionary<(string,bool),Material> NodeMaterials=[];
    private static readonly Material EdgeMaterial=Flat("#716796"),GuideMaterial=Flat("#302A47");
    private static readonly Dictionary<string,Material> BrightMaterials=[];
    public KnowledgeGraph Graph { get; private set; }=new([],[]);
    public KnowledgeNode? Selected=>_selected;
    public bool PanMode { get; set; }
    internal bool IsDragging=>_dragging;
    internal double Zoom=>_zoom;
    internal double Yaw=>_yaw;
    internal double Pitch=>_pitch;
    internal bool IsInteracting=>_pointerDown || _hovered is not null || _selected is not null || _query.Length>0;
    internal bool AmbientRunning=>_shell.IsAnimating;
    internal double AmbientAngle=>_shell.Angle;
    public bool AnimateShell { get=>_shell.Enabled; set=>_shell.Enabled=value; }
    public bool ReduceMotion { get=>_shell.ReducedMotion; set=>_shell.ReducedMotion=value; }
    public event EventHandler<KnowledgeNode?>? SelectionChanged;
    public event EventHandler<KnowledgeNode>? PositionCommitted;
    public event EventHandler<KnowledgeNode>? OpenRequested;
    public KnowledgeGraphCanvas()
    {
        Background=Brushes.Black; Focusable=true; ClipToBounds=true; Cursor=Cursors.Hand;
        AutomationProperties.SetName(this,"Cerebro tridimensional. Arrastra el espacio para girar, Shift y arrastrar para desplazar, rueda para acercar. Flechas eligen recuerdos; Enter abre el seleccionado.");
        _scene.Children.Add(new AmbientLight(Color.FromRgb(155,155,155)));
        _scene.Children.Add(new DirectionalLight(Colors.White,new(-.4,-.8,-1)));
        _scene.Children.Add(_edges); _scene.Children.Add(_points); _scene.Children.Add(_proximity);
        _shell=new(this); Children.Add(_shell);
        Viewport.Camera=_camera; Viewport.Children.Add(new ModelVisual3D { Content=_scene }); Children.Add(Viewport);
        _labels=new(this) { IsHitTestVisible=false }; Children.Add(_labels);
        SizeChanged+=(_,_)=> { CalculateFit(); UpdateCamera(); };
        LostMouseCapture+=(_,_)=> { if(_pointerDown) EndPointer(); };
        IsVisibleChanged+=(_,_)=> { if(!IsVisible) CancelPointer(); };
        UpdateCamera();
    }
    protected override AutomationPeer OnCreateAutomationPeer()=>new FrameworkElementAutomationPeer(this);
    public static Brush ZoneBrush(string category)=>category switch { "Ideas"=>Ideas,"Estudio"=>Study,"Inspiración"=>Inspiration,"Productividad"=>Productivity,"Personal"=>Personal,_=>Fallback };
    internal static Brush StageBrush(string category)=>StageColors.TryGetValue(category,out var brush)?brush:StageFallback;
    private static Material Mat(string color,bool active)
    {
        var group=new MaterialGroup(); var brush=Ui.Brush(color);
        group.Children.Add(new DiffuseMaterial(brush));
        if(active) {
            var c=brush.Color; var glow=new SolidColorBrush(Color.FromRgb((byte)(c.R*.26),(byte)(c.G*.26),(byte)(c.B*.26))); glow.Freeze();
            group.Children.Add(new EmissiveMaterial(glow));
        }
        group.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(235,243,249)),active?65:45));
        group.Freeze(); return group;
    }
    private static Material Flat(string color) { var material=new DiffuseMaterial(Ui.Brush(color)); material.Freeze(); return material; }
    private static Material NodeMaterial(KnowledgeNode node,bool active)
    {
        var key=(node.IsRoot?"root":node.Category,active);
        if(!NodeMaterials.TryGetValue(key,out var material)) {
            var color=((SolidColorBrush)StageBrush(node.Category)).Color;
            NodeMaterials[key]=material=Mat(color.ToString(),active);
        }
        return material;
    }
    private static Material LinkMaterial(string category)
    {
        if(!BrightMaterials.TryGetValue(category,out var material)) BrightMaterials[category]=material=Mat(((SolidColorBrush)StageBrush(category)).Color.ToString(),true);
        return material;
    }
    private static double Radius(KnowledgeNode node)=>node.IsMemory ? .038 : .055;
    private static Transform3D NodeTransform(KnowledgeNode node,bool active)
    {
        var r=Radius(node)*(active?1.15:1); var p=node.SpatialPosition;
        return new Transform3DGroup { Children=new() { new ScaleTransform3D(r,r,r),new TranslateTransform3D(p.X,p.Y,p.Z) } };
    }
    public void SetGraph(KnowledgeGraph graph)
    {
        var id=_selected?.Id; CancelPointer(); Graph=graph; _lookup.Clear(); _nodeModels.Clear(); _edgeModels.Clear(); _nodePoses.Clear(); _edgePoses.Clear(); _edges.Children.Clear(); _points.Children.Clear(); _proximity.Children.Clear();
        var populated=graph.Nodes.Any(n=>n.IsMemory);
        foreach(var node in graph.Nodes) {
            _lookup[node.Id]=node;
            if(!populated || node.IsRoot) continue;
            var model=new GeometryModel3D(GraphGeometry3D.Sphere,NodeMaterial(node,false)) { Transform=NodeTransform(node,false) };
            _nodeModels[node]=model; _nodePoses[node]=(node.SpatialPosition,false); _points.Children.Add(model);
        }
        foreach(var link in graph.Links.Where(e=>!e.From.IsRoot && !e.To.IsRoot)) {
            var model=new GeometryModel3D(GraphGeometry3D.Cylinder,EdgeMaterial) { Transform=GraphGeometry3D.LinkTransform(link.From.SpatialPosition,link.To.SpatialPosition,link.IsTopic ? .006 : .0045) };
            _edgeModels[link]=model; _edgePoses[link]=(link.From.SpatialPosition,link.To.SpatialPosition,link.IsTopic?.006:.0045); _edges.Children.Add(model);
        }
        _hovered=null; _selected=id is null?null:_lookup.GetValueOrDefault(id); CalculateFit(); UpdateCamera(); RefreshHighlights(); SelectionChanged?.Invoke(this,_selected);
    }
    public void SetQuery(string query) { _query=query.Trim(); RefreshHighlights(); }
    public bool Matches(KnowledgeNode node)=>_query.Length==0 || node.Memory is not null && CultureInfo.GetCultureInfo("es-MX").CompareInfo.IndexOf(
        string.Join(" ",new[] { node.Label,node.Category,node.Memory.Summary }.Concat(node.Memory.Ideas)),_query,CompareOptions.IgnoreCase|CompareOptions.IgnoreNonSpace)>=0;
    public void Select(string? id)
    {
        _selected=id is null?null:_lookup.GetValueOrDefault(id); AutomationProperties.SetItemStatus(this,_selected?.Label??"Sin selección"); RefreshHighlights(); SelectionChanged?.Invoke(this,_selected);
    }
    public void Fit() { _yaw=-18; _pitch=12; _zoom=1; _target=new(); CalculateFit(); UpdateCamera(); }
    private void CalculateFit()
    {
        var aspect=Math.Max(.4,ActualWidth/Math.Max(1,ActualHeight)); var tan=Math.Tan(_camera.FieldOfView*Math.PI/360);
        var yaw=_yaw*Math.PI/180; var pitch=_pitch*Math.PI/180;
        var towardCamera=new Vector3D(Math.Sin(yaw)*Math.Cos(pitch),Math.Sin(pitch),Math.Cos(yaw)*Math.Cos(pitch));
        var right=Vector3D.CrossProduct(-towardCamera,new(0,1,0)); right.Normalize(); var up=Vector3D.CrossProduct(right,-towardCamera);
        var points=Graph.Nodes.Select(n=>n.SpatialPosition);
        var sphereFit=(AstralShellBackdrop.Radius+.05)/Math.Sin(Math.Atan(tan/Math.Max(1,aspect)))*1.07;
        _fitDistance=Math.Max(sphereFit,points.Select(p=> {
            var v=(Vector3D)p;
            return Vector3D.DotProduct(v,towardCamera)+Math.Max(Math.Abs(Vector3D.DotProduct(v,right))/tan,Math.Abs(Vector3D.DotProduct(v,up))*aspect/tan)*1.12+.6;
        }).DefaultIfEmpty(sphereFit).Max());
    }
    private void UpdateCamera()
    {
        var yaw=_yaw*Math.PI/180; var pitch=_pitch*Math.PI/180; var distance=_fitDistance/_zoom;
        var offset=new Vector3D(Math.Sin(yaw)*Math.Cos(pitch),Math.Sin(pitch),Math.Cos(yaw)*Math.Cos(pitch))*distance;
        _camera.Position=_target+offset; _camera.LookDirection=-offset; _camera.UpDirection=new(0,1,0); _labels?.InvalidateVisual(); _shell?.InvalidateVisual();
        var forward=_camera.LookDirection; forward.Normalize(); var right=Vector3D.CrossProduct(forward,new(0,1,0)); right.Normalize();
        var up=Vector3D.CrossProduct(right,forward); up.Normalize(); _basis=(forward,right,up); _projectionTan=Math.Tan(_camera.FieldOfView*Math.PI/360);
    }
    public void Orbit(double horizontal,double vertical) { _yaw=(_yaw+horizontal)%360; _pitch=Math.Clamp(_pitch+vertical,-85,85); UpdateCamera(); }
    public void ChangeZoom(double factor) { _zoom=Math.Clamp(_zoom*factor,.45,3.2); UpdateCamera(); }
    private (Vector3D Forward,Vector3D Right,Vector3D Up) Basis()=>_basis;
    internal Point ToView(Point3D world)
    {
        var basis=Basis(); var relative=world-_camera.Position; var depth=Vector3D.DotProduct(relative,basis.Forward);
        if(depth<=.01) return new(-10000,-10000);
        var x=Vector3D.DotProduct(relative,basis.Right)/(depth*_projectionTan);
        var y=Vector3D.DotProduct(relative,basis.Up)/(depth*_projectionTan)*ActualWidth/Math.Max(1,ActualHeight);
        return new((x+1)*ActualWidth/2,(1-y)*ActualHeight/2);
    }
    internal double ViewDepth(Point3D world)=>Vector3D.DotProduct(world-_camera.Position,Basis().Forward);
    private Vector3D Ray(Point point)
    {
        var basis=Basis(); var tan=Math.Tan(_camera.FieldOfView*Math.PI/360);
        var direction=basis.Forward+basis.Right*((point.X/Math.Max(1,ActualWidth)*2-1)*tan)+basis.Up*((1-point.Y/Math.Max(1,ActualHeight)*2)*tan*ActualHeight/Math.Max(1,ActualWidth));
        direction.Normalize(); return direction;
    }
    private Point3D OnPlane(Point point,Point3D plane,Vector3D normal)
    {
        var ray=Ray(point); var divisor=Vector3D.DotProduct(ray,normal);
        if(Math.Abs(divisor)<1e-8) return plane;
        var distance=Vector3D.DotProduct(plane-_camera.Position,normal)/divisor;
        return _camera.Position+ray*Math.Max(.01,distance);
    }
    private KnowledgeNode? Hit(Point point)
    {
        if(!Graph.Nodes.Any(n=>n.IsMemory)) return null;
        var ray=Ray(point); KnowledgeNode? best=null; var nearest=double.MaxValue;
        foreach(var node in Graph.Nodes) {
            if(node.IsRoot) continue;
            var relative=_camera.Position-node.SpatialPosition; var depth=(node.SpatialPosition-_camera.Position).Length;
            var radius=Math.Max(Radius(node),depth*Math.Tan(_camera.FieldOfView*Math.PI/360)*20/Math.Max(1,ActualWidth));
            var b=Vector3D.DotProduct(relative,ray); var discriminant=b*b-(relative.LengthSquared-radius*radius); if(discriminant<0) continue;
            var t=-b-Math.Sqrt(discriminant); if(t>0 && t<nearest) { nearest=t; best=node; }
        }
        return best;
    }
    internal IReadOnlyList<KnowledgeNode> Nearby(KnowledgeNode node)=>Graph.Nodes.Where(n=>n.IsMemory && n!=node && (n.SpatialPosition-node.SpatialPosition).Length<=.85).OrderBy(n=>(n.SpatialPosition-node.SpatialPosition).Length).Take(4).ToArray();
    private void RefreshHighlights()
    {
        var active=_pending??_hovered??_selected; KnowledgeLink[] incident=active is null?[]:Graph.Connections(active).ToArray();
        var neighbours=incident.Select(e=>e.From==active?e.To:e.From).ToHashSet();
        IReadOnlyList<KnowledgeNode> near=_dragging && active?.IsMemory==true?Nearby(active):[];
        foreach(var node in near) neighbours.Add(node);
        foreach(var (node,model) in _nodeModels) {
            var highlighted=node==active || neighbours.Contains(node); model.Material=node.IsMemory && !Matches(node)?GuideMaterial:NodeMaterial(node,highlighted);
            var pose=(node.SpatialPosition,highlighted); if(_nodePoses[node]!=pose) { model.Transform=NodeTransform(node,highlighted); _nodePoses[node]=pose; }
        }
        foreach(var (link,model) in _edgeModels) {
            var highlighted=active is not null && (link.From==active || link.To==active); model.Material=highlighted?LinkMaterial(active!.Category):EdgeMaterial;
            var pose=(link.From.SpatialPosition,link.To.SpatialPosition,highlighted ? .011 : link.IsTopic ? .006 : .0045);
            if(_edgePoses[link]!=pose) { model.Transform=GraphGeometry3D.LinkTransform(pose.Item1,pose.Item2,pose.Item3); _edgePoses[link]=pose; }
        }
        _proximity.Children.Clear();
        if(_dragging && active is not null) foreach(var node in near) {
            if(incident.Any(e=>e.From==node || e.To==node)) continue;
            var delta=node.SpatialPosition-active.SpatialPosition;
            for(var part=0;part<8;part+=2) {
                var model=new GeometryModel3D(GraphGeometry3D.Cylinder,LinkMaterial(active.Category)) { Transform=GraphGeometry3D.LinkTransform(active.SpatialPosition+delta*(part/8.0),active.SpatialPosition+delta*((part+1)/8.0),.009) };
                _proximity.Children.Add(model);
            }
        }
        _labels.InvalidateVisual(); _shell.RefreshMotion();
    }
}
