using System.Text.RegularExpressions;
using System.Windows.Media.Media3D;

namespace Mindie.Services;

public sealed class KnowledgeNode(string id,string label,string category,Point position,MemoryItem? memory=null)
{
    public string Id { get; }=id;
    public string Label { get; }=label;
    public string Category { get; }=category;
    public Point Position { get; set; }=position;
    public double Depth { get; set; }
    public Point3D SpatialPosition {
        get=>new((Position.X/KnowledgeGraph.Width-.5)*6,(.5-Position.Y/KnowledgeGraph.Height)*4.5,Depth);
        set { Position=new((value.X/6+.5)*KnowledgeGraph.Width,(.5-value.Y/4.5)*KnowledgeGraph.Height); Depth=value.Z; }
    }
    public MemoryItem? Memory { get; }=memory;
    public bool IsMemory=>Memory is not null;
    public bool IsRoot=>Id=="brain";
}
public sealed record KnowledgeLink(KnowledgeNode From,KnowledgeNode To,double Weight,string Reason,bool IsTopic);
public sealed record KnowledgeGraph(IReadOnlyList<KnowledgeNode> Nodes,IReadOnlyList<KnowledgeLink> Links)
{
    public const double Width=1000,Height=720;
    public IEnumerable<KnowledgeLink> Connections(KnowledgeNode node)=>Links.Where(e=>e.From==node || e.To==node);
}
public static class KnowledgeGraphBuilder
{
    private static readonly Regex Words=new(@"[\p{L}\p{N}]+",RegexOptions.Compiled);
    private static readonly HashSet<string> Stop=new(("que del las los una uno unos unas para por con como cual cuales donde este esta esto sobre puede pueden hacer desde hasta entre tras ante sus son ser sido sin mas muy pero tambien segun cada todo todos todas tiene tienen hay era eso esa ese estos estas solo esta estar mismo misma otro otra otras otros nuestro nuestra tu tus su mis mi un la el de y o a al en es se lo le les the and for with from this that have into about your using use how what when its are not una recuerda recuerdo articulo texto documento pagina estudio ideas clave resumen guia presenta explica informacion contenido forma parte mediante traves uso dias dia").Split(' '));
    public static KnowledgeGraph Build(IReadOnlyList<MemoryItem> memories,IReadOnlyDictionary<string,KnowledgeNodePosition>? positions=null,CancellationToken cancellation=default)
    {
        var root=new KnowledgeNode("brain","Tu conocimiento","",new(500,365));
        var nodes=new List<KnowledgeNode> { root }; var links=new List<KnowledgeLink>();
        var records=memories.OrderBy(m=>m.CreatedAt).ThenBy(m=>m.Id,StringComparer.Ordinal).ToArray();
        foreach(var group in records.GroupBy(m=>m.Category).OrderBy(g=>Array.IndexOf(Pets.Categories,g.Key)))
        {
            cancellation.ThrowIfCancellationRequested();
            var hub=new KnowledgeNode("zone:"+group.Key,group.Key,group.Key,new(500,360));
            var direction=group.Key switch { "Ideas"=>new Vector3D(-.72,.45,.53),"Estudio"=>new(.65,.5,.56),"Inspiración"=>new(-.6,-.5,.62),"Productividad"=>new(.7,-.5,.5),_=>new(.05,-.3,-.95) }; direction.Normalize();
            hub.SpatialPosition=(Point3D)(direction*1.94);
            nodes.Add(hub); links.Add(new(root,hub,1,"Zona de conocimiento",true));
            var index=0;
            foreach(var memory in group)
            {
                var angle=(index*137.507764+StableAngle(group.Key))*Math.PI/180;
                var spread=Math.Min(1.2,.18+.055*Math.Sqrt(index)); index++;
                var right=Vector3D.CrossProduct(direction,new(0,1,0)); right.Normalize(); var up=Vector3D.CrossProduct(right,direction);
                var spatial=direction*Math.Cos(spread)+(right*Math.Cos(angle)+up*Math.Sin(angle))*Math.Sin(spread); spatial.Normalize(); spatial*=2.25;
                var point=new Point((spatial.X/6+.5)*KnowledgeGraph.Width,(.5-spatial.Y/4.5)*KnowledgeGraph.Height);
                if (positions is not null && positions.TryGetValue(memory.Id,out var saved) && saved.Category==memory.Category && double.IsFinite(saved.X) && double.IsFinite(saved.Y))
                    point=new(saved.X*KnowledgeGraph.Width,saved.Y*KnowledgeGraph.Height);
                point=new(Math.Clamp(point.X,-500,1500),Math.Clamp(point.Y,-360,1080));
                var depth=spatial.Z;
                if(positions is not null && positions.TryGetValue(memory.Id,out var pinned) && pinned.Category==memory.Category && pinned.Z is double z && double.IsFinite(z)) depth=z;
                var node=new KnowledgeNode(memory.Id,memory.Title,memory.Category,point,memory) { Depth=Math.Clamp(depth,-3.8,3.8) }; nodes.Add(node);
                links.Add(new(hub,node,1,"Pertenece a "+memory.Category,true));
            }
        }
        var documents=nodes.Where(n=>n.IsMemory).ToArray();
        var tokens=documents.Select(n=>Terms(n.Memory!)).ToArray();
        var inverted=new Dictionary<string,List<int>>(StringComparer.Ordinal);
        for(var i=0;i<tokens.Length;i++) foreach(var word in tokens[i].Keys) {
            if(!inverted.TryGetValue(word,out var list)) inverted[word]=list=[];
            list.Add(i);
        }
        var norms=new double[documents.Length]; var shared=new Dictionary<(int,int),double>(); var reasons=new Dictionary<(int,int),List<string>>();
        foreach(var (word,indices) in inverted.OrderBy(e=>e.Key,StringComparer.Ordinal))
        {
            cancellation.ThrowIfCancellationRequested();
            var idf=1+Math.Log((documents.Length+1.0)/(indices.Count+1));
            foreach(var i in indices) norms[i]+=Math.Pow(tokens[i][word]*idf,2);
            // Ubiquitous terms are weak evidence; cap each posting list to bound pair expansion.
            if(indices.Count<2 || indices.Count>48 || indices.Count>Math.Max(8,documents.Length*.35)) continue;
            for(var a=0;a<indices.Count;a++) for(var b=a+1;b<indices.Count;b++) {
                var pair=(indices[a],indices[b]);
                shared[pair]=shared.GetValueOrDefault(pair)+tokens[pair.Item1][word]*tokens[pair.Item2][word]*idf*idf;
                if(!reasons.TryGetValue(pair,out var terms)) reasons[pair]=terms=[];
                if(terms.Count<4) terms.Add(word);
            }
        }
        var degrees=new int[documents.Length];
        foreach(var entry in shared.Select(e=>(Pair:e.Key,Score:e.Value/Math.Max(.001,Math.Sqrt(norms[e.Key.Item1]*norms[e.Key.Item2]))))
            .Where(e=>e.Score>=.12).OrderByDescending(e=>e.Score).ThenBy(e=>e.Pair.Item1).ThenBy(e=>e.Pair.Item2))
        {
            cancellation.ThrowIfCancellationRequested(); var (a,b)=entry.Pair;
            if(degrees[a]>=4 || degrees[b]>=4) continue;
            degrees[a]++; degrees[b]++;
            links.Add(new(documents[a],documents[b],entry.Score,"Términos en común: "+string.Join(", ",reasons[entry.Pair]),false));
        }
        return new(nodes,links);
    }
    private static double StableAngle(string id)
    {
        uint hash=2166136261; foreach(var letter in id) hash=unchecked((hash^letter)*16777619);
        return hash%360;
    }
    private static Dictionary<string,double> Terms(MemoryItem memory)
    {
        var terms=new Dictionary<string,double>(StringComparer.Ordinal);
        void Add(string text,double weight) {
            var normalized=Regex.Replace(text.ToLowerInvariant().Normalize(NormalizationForm.FormD),@"\p{M}","");
            foreach(Match match in Words.Matches(normalized)) {
                var term=match.Value; if(term.Length<3 || Stop.Contains(term) || term.All(char.IsDigit)) continue;
                terms[term]=Math.Min(8,terms.GetValueOrDefault(term)+weight);
            }
        }
        Add(memory.Title,3); Add(memory.Summary,1); foreach(var idea in memory.Ideas) Add(idea,1);
        return terms.OrderByDescending(e=>e.Value).ThenBy(e=>e.Key,StringComparer.Ordinal).Take(80).ToDictionary(e=>e.Key,e=>e.Value,StringComparer.Ordinal);
    }
}
