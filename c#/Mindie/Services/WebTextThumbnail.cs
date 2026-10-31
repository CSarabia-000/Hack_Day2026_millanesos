namespace Mindie.Services;

internal static class WebTextThumbnail
{
    internal static byte[] Create(WebPageText page)
    {
        var visual=new DrawingVisual();
        using (var drawing=visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(247,248,250)),null,new(0,0,640,400));
            void Text(string value,double size,double y,Brush brush,double height) {
                var text=new FormattedText(value,CultureInfo.GetCultureInfo("es-MX"),FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush,1) { MaxTextWidth=552,MaxTextHeight=height,Trimming=TextTrimming.CharacterEllipsis };
                drawing.DrawText(text,new(44,y));
            }
            Text("Página web · Solo texto",16,35,Views.Ui.Accent,30);
            Text(page.Title,27,86,Views.Ui.Ink,90);
            drawing.DrawLine(new Pen(Views.Ui.Line,1),new(44,192),new(596,192));
            Text(page.Text[..Math.Min(page.Text.Length,400)].Replace("\n"," "),17,218,Views.Ui.Muted,100);
            Text(page.Url.Length>0?new Uri(page.Url).Host:"Texto pegado por ti",14,348,Views.Ui.Muted,24);
        }
        var image=new RenderTargetBitmap(640,400,96,96,PixelFormats.Pbgra32); image.Render(visual); return Images.Encode(image);
    }
}
