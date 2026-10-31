using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Windows.Documents;
using Mindie.Views;
using WpfBlock = System.Windows.Documents.Block;
using WpfInline = System.Windows.Documents.Inline;
using WpfList = System.Windows.Documents.List;
using MarkdownBlock = Markdig.Syntax.Block;

namespace Mindie.Controls;

/// <summary>Renders model Markdown as selectable native WPF text, without HTML or remote images.</summary>
public static class MarkdownMessage
{
    private static readonly MarkdownPipeline Pipeline=new MarkdownPipelineBuilder().DisableHtml().Build();
    public static RichTextBox Create(string text,Action<string>? openLink=null)
    {
        var control=new RichTextBox {
            Document=Document(text,openLink), IsReadOnly=true,IsReadOnlyCaretVisible=false,IsDocumentEnabled=true,
            BorderThickness=new(0),Padding=new(0),Margin=new(0),Background=Brushes.Transparent,Foreground=Ui.Ink,
            VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
            FontSize=14,FontFamily=new("Segoe UI Variable Text, Segoe UI"),MinHeight=0
        };
        control.PreviewMouseWheel+=(_,e)=> {
            if (VisualTreeHelper.GetParent(control) is not UIElement parent) return;
            e.Handled=true;
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice,e.Timestamp,e.Delta) { RoutedEvent=UIElement.MouseWheelEvent });
        };
        return control;
    }
    public static FlowDocument Document(string text,Action<string>? openLink=null)
    {
        var document=new FlowDocument { FontFamily=new("Segoe UI Variable Text, Segoe UI"),FontSize=14,Foreground=Ui.Ink,PagePadding=new(0),LineHeight=21,TextAlignment=TextAlignment.Left };
        foreach (var block in Markdown.Parse(text,Pipeline)) document.Blocks.Add(RenderBlock(block,openLink));
        if (document.Blocks.LastBlock is { } last) last.Margin=new(last.Margin.Left,last.Margin.Top,last.Margin.Right,0);
        return document;
    }
    private static WpfBlock RenderBlock(MarkdownBlock block,Action<string>? openLink)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var title=Paragraph(heading.Inline,openLink); title.FontWeight=FontWeights.SemiBold; title.FontSize=heading.Level<=2?19:16; title.Margin=new(0,5,0,8); return title;
            case ParagraphBlock paragraph: return Paragraph(paragraph.Inline,openLink);
            case ListBlock list:
                var result=new WpfList { MarkerStyle=list.IsOrdered?TextMarkerStyle.Decimal:TextMarkerStyle.Disc,Margin=new(0,2,0,8),Padding=new(22,0,0,0),MarkerOffset=6 };
                if (list.IsOrdered && int.TryParse(list.OrderedStart,out var start)) result.StartIndex=Math.Clamp(start,1,1000000);
                foreach (var child in list.OfType<ListItemBlock>())
                {
                    var item=new ListItem { Margin=new(0,0,0,4) };
                    foreach (var content in child) item.Blocks.Add(RenderBlock(content,openLink));
                    if (item.Blocks.LastBlock is { } last) last.Margin=new(last.Margin.Left,last.Margin.Top,last.Margin.Right,0);
                    result.ListItems.Add(item);
                }
                return result;
            case QuoteBlock quote:
                var section=new Section { Margin=new(0,3,0,8),Padding=new(12,0,0,0),BorderBrush=Ui.Line,BorderThickness=new(2,0,0,0) };
                foreach (var child in quote) section.Blocks.Add(RenderBlock(child,openLink));
                return section;
            case CodeBlock code:
                return new Paragraph(new Run(code.Lines.ToString().TrimEnd('\r','\n'))) { FontFamily=new("Consolas"),FontSize=13,Background=Ui.Ground,Padding=new(10),Margin=new(0,4,0,10) };
            case ThematicBreakBlock:
                return new Paragraph { BorderBrush=Ui.Line,BorderThickness=new(0,0,0,1),Margin=new(0,5,0,10),FontSize=1 };
            default:
                var fallback=new Section();
                if (block is ContainerBlock container) foreach (var child in container) fallback.Blocks.Add(RenderBlock(child,openLink));
                else if (block is LeafBlock leaf) return Paragraph(leaf.Inline,openLink);
                return fallback;
        }
    }
    private static Paragraph Paragraph(ContainerInline? content,Action<string>? openLink)
    {
        var paragraph=new Paragraph { Margin=new(0,0,0,9) };
        AddInlines(paragraph.Inlines,content,openLink); return paragraph;
    }
    private static void AddInlines(InlineCollection target,ContainerInline? content,Action<string>? openLink)
    {
        if (content is null) return;
        foreach (var inline in content)
        {
            switch (inline)
            {
                case LiteralInline literal: target.Add(new Run(literal.Content.ToString())); break;
                case CodeInline code: target.Add(new Run(code.Content) { FontFamily=new("Consolas"),FontSize=13,Background=Ui.Brush("#E8ECEF") }); break;
                case LineBreakInline: target.Add(new LineBreak()); break;
                case EmphasisInline emphasis:
                    var span=new Span();
                    if (emphasis.DelimiterCount>=2) span.FontWeight=FontWeights.Bold;
                    if (emphasis.DelimiterCount==1 || emphasis.DelimiterCount>=3) span.FontStyle=FontStyles.Italic;
                    AddInlines(span.Inlines,emphasis,openLink); target.Add(span); break;
                case LinkInline link:
                    var label=new Span(); AddInlines(label.Inlines,link,openLink);
                    if (link.IsImage) { target.Add(new Run("[Imagen: ")); target.Add(label); target.Add(new Run("]")); }
                    else target.Add(Link(label,link.Url,openLink));
                    break;
                case AutolinkInline auto:
                    target.Add(Link(new Run(auto.Url),auto.Url,openLink)); break;
                case HtmlEntityInline entity: target.Add(new Run(entity.Transcoded.ToString())); break;
                case HtmlInline html: target.Add(new Run(html.Tag)); break;
                case ContainerInline nested: AddInlines(target,nested,openLink); break;
                default: target.Add(new Run(inline.ToString())); break;
            }
        }
    }
    private static WpfInline Link(WpfInline label,string? value,Action<string>? openLink)
    {
        if (!Uri.TryCreate(value,UriKind.Absolute,out var uri) || (uri.Scheme!="http" && uri.Scheme!="https") || !string.IsNullOrEmpty(uri.UserInfo)) return label;
        var hyperlink=new Hyperlink(label) { NavigateUri=uri,Foreground=Ui.Accent,ToolTip=uri.AbsoluteUri };
        hyperlink.RequestNavigate+=(_,e)=> { openLink?.Invoke(e.Uri.AbsoluteUri); e.Handled=true; };
        return hyperlink;
    }
}
