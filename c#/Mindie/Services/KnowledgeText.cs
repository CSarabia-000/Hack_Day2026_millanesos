using System.Text.RegularExpressions;

namespace Mindie.Services;

public static class KnowledgeText
{
    private sealed record Chunk(int Page,string Text,HashSet<string> Terms);
    private sealed class EvidenceIndex
    {
        internal (int Number,string Text)[] Pages=[];
        internal List<Chunk> Chunks=[];
        internal Dictionary<string,int> Frequency=[];
    }
    private static readonly ConditionalWeakTable<PdfKnowledge,EvidenceIndex> Indices=new();
    public static IEnumerable<string> Split(string text, int size = 6000, int overlap = 0)
    {
        if (size < 100 || overlap < 0 || overlap >= size/2) throw new ArgumentException("Tamaño de fragmento inválido.");
        for (var start = 0; start < text.Length;)
        {
            var end = Math.Min(start+size,text.Length);
            if (end < text.Length)
            {
                var boundary = text.LastIndexOfAny(['\n',' '],end-1,end-start);
                if (boundary > start+size/2) end = boundary+1;
                if (char.IsHighSurrogate(text[end-1]) && char.IsLowSurrogate(text[end])) end--;
            }
            yield return text[start..end];
            if (end == text.Length) break;
            start = end-overlap;
            if (start > 0 && char.IsLowSurrogate(text[start]) && char.IsHighSurrogate(text[start-1])) start--;
        }
    }

    private static HashSet<string> Terms(string text) => Regex.Matches(Regex.Replace(text.ToLowerInvariant().Normalize(NormalizationForm.FormD),@"\p{M}",""),@"[\p{L}\p{N}]+")
        .Select(m=>m.Value).Where(t=>t.Length>2 && !StopWords.Contains(t)).ToHashSet();
    private static readonly HashSet<string> StopWords = new("que del las los una uno unos unas para por con como cual cuales donde este esta esto sobre puedes puede hacer dime explicame documento pagina segun the and what how this from".Split(' '));

    public static string Evidence(PdfKnowledge document, string question,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        var cached=Indices.GetValue(document,_=>new()); List<Chunk> chunks; Dictionary<string,int> frequency;
        lock(cached) {
            if(cached.Pages.Length!=document.Pages.Count || cached.Pages.Where((p,i)=>p.Number!=document.Pages[i].Number || p.Text!=document.Pages[i].Text).Any()) {
                var rebuilt=new List<Chunk>();
                foreach(var page in document.Pages) foreach(var text in Split(page.Text,2100,250)) { cancellation.ThrowIfCancellationRequested(); rebuilt.Add(new(page.Number,text,Terms(text))); }
                cached.Frequency=rebuilt.SelectMany(c=>c.Terms).GroupBy(t=>t).ToDictionary(g=>g.Key,g=>g.Count()); cached.Chunks=rebuilt; cached.Pages=document.Pages.Select(p=>(p.Number,p.Text)).ToArray();
            }
            chunks=cached.Chunks; frequency=cached.Frequency;
        }
        if (chunks.Count == 0) throw new InvalidDataException("No hay contenido legible en este PDF.");
        cancellation.ThrowIfCancellationRequested();
        var terms = Terms(question);
        var ranked = chunks.Select((c,i)=> new { c.Page,c.Text, Index=i, Score=terms.Sum(t=>c.Terms.Contains(t) ? Math.Log(1+(double)chunks.Count/(1+frequency[t])) : 0) })
            .OrderByDescending(c=>c.Score).ThenBy(c=>c.Index).ToList();
        var selected = ranked.Where(c=>c.Score>0).Take(5).ToList();
        // Include distributed passages for broad questions; do not just send the start of a long PDF.
        foreach (var index in Enumerable.Range(0,5).Select(i=>(int)Math.Round(i*(chunks.Count-1)/4.0)))
        {
            if (selected.Count >= 6) break;
            var candidate = ranked.First(c=>c.Index==index);
            if (selected.All(c=>c.Index!=index)) selected.Add(candidate);
        }
        return string.Join("\n\n",selected.OrderBy(c=>c.Index).Select(c=>$"[Página {c.Page}]\n{c.Text}"));
    }
}
