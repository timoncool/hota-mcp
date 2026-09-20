using System.Text;
using System.Text.RegularExpressions;

namespace HotaMcp;

public sealed record DocHit(string File,string Heading,int Score,string Snippet,string Text);
public sealed record DocsAnswer(string Query,int Sections,bool Available,string? Note,List<DocHit> Hits);
public sealed record DocEntry(string Path,int Sections,List<string> Headings);
public sealed record DocsCatalog(bool Available,string? Note,List<DocEntry> Documents);
public sealed record DocText(string Path,string? Heading,bool Found,string? Note,string Text);

/// <summary>
/// Searchable index of the project's own documentation: lessons, playbooks, capability map,
/// the game manual extract and the hotkey reference. The bridge exposes it as an MCP tool so the
/// agent can ask "what do I do here" or "how does this work" at any step and read the answer.
/// Read-only: only files under the repository docs tree are opened.
/// </summary>
public sealed class DocsIndex
{
    private static readonly string[] StopWords=["и","в","во","на","не","что","как","для","это","или","the","and","with","from","that","this","с","по","за","от","до","у","же","бы","а","но","то","из","к","о","об"];
    private readonly object gate=new();
    private DateTime loaded=DateTime.MinValue;
    private List<(string File,string Heading,string Body)> sections=[];

    private static string? FindRoot()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        for(int i=0;i<8&&dir is not null;i++,dir=dir.Parent)
            if(Directory.Exists(Path.Combine(dir.FullName,"docs","knowledge")))return dir.FullName;
        return null;
    }

    private List<(string,string,string)> Load()
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
        var result=new List<(string,string,string)>();
        // Sections are split on markdown headings; the heading path is kept as context.
        foreach(var file in files.Distinct().OrderBy(f=>f))
        {
            string[] lines;
            try{lines=File.ReadAllLines(file);}catch(IOException){continue;}
            var heading="";var body=new StringBuilder();var stack=new List<(int,string)>();
            void Flush()
            {
                if(body.Length==0)return;
                result.Add((Path.GetRelativePath(root,file).Replace('\\','/'),heading,body.ToString()));
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

    private static IEnumerable<string> Tokens(string text)=>
        Regex.Split(text.ToLowerInvariant(),@"[^\p{L}\p{N}]+")
            .Where(t=>t.Length>=3&&!StopWords.Contains(t));

    public DocsCatalog Catalog()
    {
        lock(gate){if((DateTime.UtcNow-loaded).TotalSeconds>10){sections=Load();loaded=DateTime.UtcNow;}}
        if(sections.Count==0)return new(false,"Documentation tree not found next to the bridge build",[]);
        var documents=sections.GroupBy(s=>s.File).OrderBy(g=>g.Key).Select(g=>
            new DocEntry(g.Key,g.Count(),g.Select(s=>s.Heading).Where(h=>h.Length>0).Distinct().Take(40).ToList())).ToList();
        return new(true,null,documents);
    }

    public DocText Read(string path,string? heading)
    {
        path=(path??string.Empty).Replace('\\','/').Trim().TrimStart('/');
        lock(gate){if((DateTime.UtcNow-loaded).TotalSeconds>10){sections=Load();loaded=DateTime.UtcNow;}}
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
        lock(gate)
        {
            if((DateTime.UtcNow-loaded).TotalSeconds>10){sections=Load();loaded=DateTime.UtcNow;}
        }
        if(sections.Count==0)return new(query,0,false,"Documentation tree not found next to the bridge build",[]);
        var tokens=Tokens(query).Distinct().ToArray();
        if(tokens.Length==0)return new(query,sections.Count,true,"Query needs at least one word of three or more letters",[]);
        var scored=new List<(int Score,int Index)>();
        // Russian words inflect, so every token is also matched by its stems: an exact hit scores
        // in full, a stem hit at a reduced weight, which keeps "герою" finding "герой/героя/героев".
        var variants=new List<(string Token,double Weight)>();
        foreach(var token in tokens)
        {
            variants.Add((token,1.0));
            if(token.Length>=8)variants.Add((token[..^2],0.6));
            else if(token.Length>=5)variants.Add((token[..^1],0.6));
        }
        for(int i=0;i<sections.Count;i++)
        {
            var (_,heading,body)=sections[i];
            string head=heading.ToLowerInvariant(),text=body.ToLowerInvariant();
            double score=0;
            foreach(var (token,weight) in variants)
            {
                if(head.Contains(token))score+=6*weight;
                int count=Regex.Matches(text,Regex.Escape(token)).Count;
                score+=Math.Min(count,12)*weight;
            }
            // Long sections must not outrank a precise short one just by containing more words.
            score/=1.0+Math.Log10(1.0+text.Length/400.0);
            if(score>0)scored.Add(((int)Math.Round(score*10),i));
        }
        var hits=scored.OrderByDescending(s=>s.Score).Take(Math.Clamp(limit,1,8)).Select(s=>
        {
            var (file,heading,body)=sections[s.Index];
            var paragraphs=body.Split('\n',StringSplitOptions.RemoveEmptyEntries);
            var best=paragraphs.OrderByDescending(p=>tokens.Sum(t=>Regex.Matches(p.ToLowerInvariant(),Regex.Escape(t)).Count)).FirstOrDefault()?.Trim()??"";
            if(best.Length>600)best=best[..600]+"…";
            var text=body.Trim();
            if(text.Length>3000)text=text[..3000]+"…";
            return new DocHit(file,heading,s.Score,best,text);
        }).ToList();
        return new(query,sections.Count,true,hits.Count==0?"Nothing in the documentation matches this query":null,hits);
    }
}
