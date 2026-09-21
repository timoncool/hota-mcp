using System.Text;
using System.Text.RegularExpressions;

namespace HotaMcp;

public sealed record DocHit(string File,string Heading,int Score,string Snippet,string Text);
public sealed record DocsAnswer(string Query,int Sections,bool Available,string? Note,List<DocHit> Hits);
public sealed record DocEntry(string Path,int Sections,List<string> Headings);
public sealed record DocsCatalog(bool Available,string? Note,List<DocEntry> Documents);
public sealed record DocText(string Path,string? Heading,bool Found,string? Note,string Text);

/// <summary>
/// Everything the agent can ask "how does this work" about, in one searchable place: the project's
/// own playbooks and lessons, the game manual extract, and the rule tables read out of the
/// installed game itself.
///
/// Ranking is BM25 over sections, which is what makes a rare word decide the answer: "логистика"
/// appears in one skill card and nowhere else, so that card wins, while a common word like "герой"
/// no longer drags every long section to the top. Russian endings are folded to a stem so the word
/// the agent saw on screen matches the word in the text.
///
/// The reference is only reachable through these tools. It is never mixed into an observation.
/// </summary>
public sealed class DocsIndex
{
    // The corpus mixes two very different shapes: a few hundred prose sections and well over a
    // thousand one-line reference cards. Full length normalisation would let any card outrank a
    // long section that actually answers the question, so it is kept light.
    private const double K1=1.2,B=0.3;

    private sealed record Section(string File,string Heading,string Body,
        Dictionary<string,int> Terms,HashSet<string> HeadingTerms,int Length);

    private readonly object gate=new();
    private readonly GameReference reference=new();
    private DateTime loaded=DateTime.MinValue;
    private List<Section> sections=[];
    private Dictionary<string,int> documentFrequency=new();
    private double averageLength=1;

    public ReferenceAnswer Reference(string name,string? kind,int limit)=>reference.Find(name,kind,limit);

    // ------------------------------------------------------------------ loading

    private static string? FindRoot()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        for(int i=0;i<8&&dir is not null;i++,dir=dir.Parent)
            if(Directory.Exists(Path.Combine(dir.FullName,"docs","knowledge")))return dir.FullName;
        return null;
    }

    private void EnsureLoaded()
    {
        lock(gate)
        {
            if((DateTime.UtcNow-loaded).TotalSeconds<=10)return;
            var loadedSections=LoadProse();
            loadedSections.AddRange(LoadReference());
            var frequency=new Dictionary<string,int>();
            foreach(var section in loadedSections)
                foreach(var term in section.Terms.Keys)
                    frequency[term]=frequency.GetValueOrDefault(term)+1;
            sections=loadedSections;
            documentFrequency=frequency;
            averageLength=loadedSections.Count==0?1:Math.Max(1,loadedSections.Average(s=>s.Length));
            loaded=DateTime.UtcNow;
        }
    }

    private static Section Build(string file,string heading,string body)=>
        new(file,heading,body,Count(body+"\n"+heading),[..Tokens(heading)],Math.Max(1,Tokens(body).Count()));

    private List<Section> LoadProse()
    {
        var root=FindRoot();
        if(root is null)return [];
        var files=new List<string>();
        files.AddRange(Directory.GetFiles(Path.Combine(root,"docs"),"*.md",SearchOption.TopDirectoryOnly));
        var knowledge=Path.Combine(root,"docs","knowledge");
        if(Directory.Exists(knowledge))
        {
            files.AddRange(Directory.GetFiles(knowledge,"*.md",SearchOption.AllDirectories));
            files.AddRange(Directory.GetFiles(knowledge,"llms.txt",SearchOption.AllDirectories));
        }
        var skills=Path.Combine(root,"skills");
        if(Directory.Exists(skills))files.AddRange(Directory.GetFiles(skills,"SKILL.md",SearchOption.AllDirectories));
        var result=new List<Section>();
        // Sections are split on markdown headings; the heading path is kept as context.
        foreach(var file in files.Distinct().OrderBy(f=>f))
        {
            string[] lines;
            try{lines=File.ReadAllLines(file);}catch(IOException){continue;}
            var heading="";var body=new StringBuilder();var stack=new List<(int,string)>();
            string relative=Path.GetRelativePath(root,file).Replace('\\','/');
            void Flush()
            {
                if(body.Length==0)return;
                result.Add(Build(relative,heading,body.ToString()));
                body.Clear();
            }
            foreach(var line in lines)
            {
                var match=Regex.Match(line,@"^(#{1,6})\s+(.*)$");
                if(match.Success)
                {
                    Flush();
                    int level=match.Groups[1].Value.Length;var title=match.Groups[2].Value.Trim();
                    while(stack.Count>0&&stack[^1].Item1>=level)stack.RemoveAt(stack.Count-1);
                    stack.Add((level,title));
                    heading=string.Join(" / ",stack.Select(s=>s.Item2));
                }
                else if(line.StartsWith("|")||line.Trim().Length>0)body.AppendLine(line);
            }
            Flush();
        }
        return result;
    }

    private List<Section> LoadReference()=>
        reference.Cards().Select(card=>Build("game://reference/"+card.Kind,card.Kind+": "+card.Name,card.Text)).ToList();

    // ------------------------------------------------------------------ tokens

    private static readonly string[] Endings=
    [
        "ами","ями","иями","ого","его","ому","ему","ыми","ими","ей","ой","ах","ях","ам","ям",
        "ов","ев","ий","ый","ая","яя","ое","ее","ые","ие","ом","ем","ью","ия","ие","у","ю","а","я","ы","и","е","о","ь",
    ];

    /// Folds a Russian word to a stem by dropping one grammatical ending. Crude on purpose: the
    /// index and the query are folded the same way, so "логистике" and "логистика" meet.
    private static string Stem(string token)
    {
        if(token.Length<5)return token;
        foreach(var ending in Endings)
            if(token.Length-ending.Length>=4&&token.EndsWith(ending,StringComparison.Ordinal))
                return token[..^ending.Length];
        return token;
    }

    private static IEnumerable<string> Tokens(string text)=>
        Regex.Split(text.ToLowerInvariant(),@"[^\p{L}\p{N}]+")
            .Where(t=>t.Length>=3)
            .Select(Stem);

    private static Dictionary<string,int> Count(string text)
    {
        var counts=new Dictionary<string,int>();
        foreach(var token in Tokens(text))counts[token]=counts.GetValueOrDefault(token)+1;
        return counts;
    }

    // ------------------------------------------------------------------ queries

    public DocsCatalog Catalog()
    {
        EnsureLoaded();
        if(sections.Count==0)return new(false,"Documentation tree not found next to the bridge build",[]);
        var documents=sections.GroupBy(s=>s.File).OrderBy(g=>g.Key).Select(g=>
            new DocEntry(g.Key,g.Count(),g.Select(s=>s.Heading).Where(h=>h.Length>0).Distinct().Take(40).ToList())).ToList();
        return new(true,null,documents);
    }

    public DocText Read(string path,string? heading)
    {
        path=(path??string.Empty).Replace('\\','/').Trim().TrimStart('/');
        EnsureLoaded();
        var file=sections.Where(s=>string.Equals(s.File,path,StringComparison.OrdinalIgnoreCase)).ToList();
        if(file.Count==0)return new(path,heading,false,"No document with this path; ask the catalog for exact paths",string.Empty);
        var chosen=string.IsNullOrWhiteSpace(heading)
            ?file
            :file.Where(s=>s.Heading.Split(" / ").Any(part=>part.Contains(heading,StringComparison.OrdinalIgnoreCase))).ToList();
        if(chosen.Count==0)
        {
            var available=file.Select(s=>s.Heading).Where(h=>h.Length>0).Take(12).ToList();
            return new(path,heading,false,"Heading not found. Available headings: "+string.Join(" | ",available),string.Empty);
        }
        var text=string.Join("\n\n",chosen.Select(s=>(s.Heading.Length>0?"## "+s.Heading+"\n":"")+s.Body.Trim()));
        if(text.Length>12000)text=text[..12000]+"\n… (document truncated; ask for a more specific heading)";
        return new(path,heading,true,null,text);
    }

    public DocsAnswer Search(string query,int limit)
    {
        EnsureLoaded();
        if(sections.Count==0)return new(query,0,false,"Documentation tree not found next to the bridge build",[]);
        var terms=Tokens(query).Distinct().ToArray();
        if(terms.Length==0)return new(query,sections.Count,true,"Query needs at least one word of three or more letters",[]);
        int total=sections.Count;
        var scored=new List<(double Score,Section Section)>();
        foreach(var section in sections)
        {
            double score=0;
            int matched=0;
            foreach(var term in terms)
            {
                if(!section.Terms.TryGetValue(term,out int frequency))continue;
                matched++;
                int documents=documentFrequency.GetValueOrDefault(term,1);
                double idf=Math.Log(1+(total-documents+0.5)/(documents+0.5));
                double norm=frequency*(K1+1)/(frequency+K1*(1-B+B*section.Length/averageLength));
                score+=idf*norm;
                // A term in the heading names the subject of the section rather than mentioning it.
                if(section.HeadingTerms.Contains(term))score+=idf*1.5;
            }
            // A section that answers "guards of the dragon utopia" mentions all three words. One
            // that merely shares the word "dragon" is a different subject, however short it is.
            if(score>0)scored.Add((score*(0.25+0.75*matched/terms.Length),section));
        }
        var hits=scored.OrderByDescending(s=>s.Score).Take(Math.Clamp(limit,1,8)).Select(s=>
        {
            var section=s.Section;
            var paragraphs=section.Body.Split('\n',StringSplitOptions.RemoveEmptyEntries);
            var best=paragraphs
                .OrderByDescending(p=>Count(p).Where(t=>terms.Contains(t.Key)).Sum(t=>t.Value))
                .FirstOrDefault()?.Trim()??"";
            if(best.Length>600)best=best[..600]+"…";
            var text=section.Body.Trim();
            if(text.Length>3000)text=text[..3000]+"…";
            return new DocHit(section.File,section.Heading,(int)Math.Round(s.Score*10),best,text);
        }).ToList();
        return new(query,sections.Count,true,hits.Count==0?"Nothing in the documentation matches this query":null,hits);
    }
}
