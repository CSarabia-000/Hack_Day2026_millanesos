using Mindie.Services;
using Mindie.Views;

namespace Mindie.Controls;

public sealed partial class KnowledgeGraphCanvas
{
    private sealed class Labels(KnowledgeGraphCanvas owner):FrameworkElement
    {
        private static readonly Brush Ink=Ui.Brush("#E2DDED"),Backdrop=Ui.Brush("#EB131019");
        private static readonly Brush Glow=CreateGlow();
        private static Brush CreateGlow() { var brush=new RadialGradientBrush(Color.FromArgb(110,160,134,255),Colors.Transparent); brush.Freeze(); return brush; }
        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing); if(ActualWidth<1 || ActualHeight<1 || !owner.Graph.Nodes.Any(n=>n.IsMemory)) return;
            var active=owner._pending??owner._hovered??owner._selected;
            var recent=owner.Graph.Nodes.Where(n=>n.IsMemory).OrderByDescending(n=>n.Memory!.CreatedAt).Take(2).ToHashSet();
            var nearby=active is null?new HashSet<KnowledgeNode>():owner.Graph.Connections(active).Select(e=>e.From==active?e.To:e.From).ToHashSet();
            var occupied=owner.Graph.Nodes.Where(n=>!n.IsRoot).Select(n=> {
                var p=owner.ToView(n.SpatialPosition); var radius=n.IsMemory?8:13;
                return new Rect(p-new Vector(radius,radius),new Size(radius*2,radius*2));
            }).ToList();
            foreach(var node in owner.Graph.Nodes.Where(n=>!n.IsRoot)) {
                var p=owner.ToView(node.SpatialPosition); var selected=node==active;
                drawing.DrawEllipse(Glow,null,p,selected?16:9,selected?16:9);
                drawing.DrawEllipse(StageBrush(node.Category),null,p,selected?2.8:1.8,selected?2.8:1.8);
            }
            foreach(var node in owner.Graph.Nodes.Where(n=>!n.IsRoot).OrderBy(n=>!n.IsMemory?0:n==active?1:2))
            {
                var p=owner.ToView(node.SpatialPosition); if(p.X<-100 || p.X>ActualWidth+100 || p.Y<-100 || p.Y>ActualHeight+100) continue;
                if(node.IsMemory && node!=active && !nearby.Contains(node) && !(owner._query.Length>0 && owner.Matches(node)) && !(active is null && recent.Contains(node))) continue;
                var caption=node.IsMemory?node.Label:node.Label+" · "+owner.Graph.Nodes.Count(n=>n.IsMemory && n.Category==node.Category);
                var text=Text(caption,node.IsMemory?11:12,node.IsMemory?Ink:StageBrush(node.Category),!node.IsMemory,node.IsMemory?175:180);
                Point position;
                if(!node.IsMemory) position=p+new Vector(-90,17);
                else {
                    var candidates=new[] { p+new Vector(12,-text.Height/2),p+new Vector(-text.Width-12,-text.Height/2),p+new Vector(-text.Width/2,13),p+new Vector(-text.Width/2,-text.Height-15) };
                    var chosen=candidates.Select(at=>new Rect(at,new Size(Math.Max(1,text.Width),Math.Max(1,text.Height)))).FirstOrDefault(r=>r.Left>=6 && r.Right<=ActualWidth-6 && r.Top>=6 && r.Bottom<=ActualHeight-6 && !occupied.Any(o=>o.IntersectsWith(r)));
                    if(chosen.Width==0) {
                        if(node!=active) continue;
                        position=new(Math.Clamp(p.X+12,8,Math.Max(8,ActualWidth-text.Width-8)),Math.Clamp(p.Y,8,Math.Max(8,ActualHeight-text.Height-8)));
                    }
                    else position=chosen.TopLeft;
                }
                var rect=new Rect(position+new Vector(node.IsMemory?0:(180-text.Width)/2,0),new Size(Math.Max(1,text.Width),text.Height));
                occupied.Add(rect);
                if(node==active) { var backdrop=rect; backdrop.Inflate(5,3); drawing.DrawRoundedRectangle(Backdrop,null,backdrop,5,5); }
                drawing.DrawText(text,position);
            }
        }
        private FormattedText Text(string value,double size,Brush color,bool center,double width)=>new(value,CultureInfo.GetCultureInfo("es-MX"),FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,color,VisualTreeHelper.GetDpi(this).PixelsPerDip) {
            MaxTextWidth=width,MaxTextHeight=size*1.65,Trimming=TextTrimming.CharacterEllipsis,TextAlignment=center?TextAlignment.Center:TextAlignment.Left
        };
    }
}
