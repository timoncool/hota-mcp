using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace HotaMcp;

public sealed record DocHit(string File,string Heading,double Score,string Snippet,string? Text);
public sealed record DocsAnswer(string Query,int Sections,bool Available,string? Note,List<DocHit> Hits,string? Next);
public sealed record DocEntry(string Path,int Sections,List<string>? Headings);
public sealed record DocsCatalog(bool Available,string? Note,int Documents,List<DocEntry> Entries,string? Next);
public sealed record DocText(string Path,string? Heading,bool Found,string? Note,string Text,
    int Offset,int Length,int? NextOffset);

/// <summary>
/// Everything the agent can ask "how does this work" about, in one searchable place: the project's
/// own playbooks and lessons, the game manual extract, and the rule tables read out of the
/// installed game itself.
///
/// Retrieval is SQLite FTS5 held in memory: its bm25() does the ranking, its snippet() cuts the
/// passage that answers. Russian morphology is handled by indexing a stemmed copy of every field
/// beside the original, so "логистике" finds "логистика" while snippets still show real text.
/// A trigram index over titles catches typos when nothing matches at all.
///
/// The reference is only reachable through these tools. It is never mixed into an observation.
/// </summary>
public sealed class DocsIndex:IDisposable
{
    // Heading terms name the subject of a section; body terms merely mention it. Stemmed columns
    // score below their exact counterparts so an exact word still wins over a morphological match.
    private static readonly string Weights=
        Environment.GetEnvironmentVariable("HOTA_DOCS_WEIGHTS")??"10.0,1.0,5.0,0.5";

    private readonly object gate=new();
    private readonly GameReference reference=new();
    private const string ReferencePrefix="game://reference/";
    private SqliteConnection? db;
    private DateTime loaded=DateTime.MinValue;
    private int sectionCount;
    private int proseCount;

    public ReferenceAnswer Reference(string name,string? kind,int limit)=>reference.Find(name,kind,limit);

    public void Dispose(){db?.Dispose();db=null;}

    // ------------------------------------------------------------------ loading

    private static string? FindRoot()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        for(int i=0;i<8&&dir is not null;i++,dir=dir.Parent)
            if(Directory.Exists(Path.Combine(dir.FullName,"docs","knowledge")))return dir.FullName;
        return null;
    }

    private sealed record Section(string File,string Heading,string Body);

    private void EnsureLoaded()
    {
        lock(gate)
        {
            if(db is not null&&(DateTime.UtcNow-loaded).TotalSeconds<=10)return;
            // Prose and rule cards are one index but never one ranked list. A question like "как
            // оценить силу отряда" is answered by a playbook, not by whichever spell card happens
            // to share a word; cards are what hota_reference is for, and they only enter a docs
            // answer when the prose has nothing.
            var sections=LoadProse();
            proseCount=sections.Count;
            sections.AddRange(reference.Cards().Select(card=>
                new Section(ReferencePrefix+card.Kind,card.Kind+": "+card.Name,card.Text)));
            Build(sections);
            sectionCount=sections.Count;
            loaded=DateTime.UtcNow;
        }
    }

    private void Build(List<Section> sections)
    {
        db?.Dispose();
        var connection=new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Execute(connection,"""
            CREATE TABLE sections(id INTEGER PRIMARY KEY, file TEXT, heading TEXT, body TEXT);
            CREATE INDEX sections_file ON sections(file);
            CREATE VIRTUAL TABLE fts USING fts5(
                heading, body, heading_stem, body_stem,
                tokenize='unicode61 remove_diacritics 2');
            CREATE VIRTUAL TABLE titles USING fts5(heading, tokenize='trigram');
            """);
        using var transaction=connection.BeginTransaction();
        using var insert=connection.CreateCommand();
        insert.CommandText="INSERT INTO sections(id,file,heading,body) VALUES(@i,@f,@h,@b)";
        using var index=connection.CreateCommand();
        index.CommandText="INSERT INTO fts(rowid,heading,body,heading_stem,body_stem) VALUES(@i,@h,@b,@hs,@bs)";
        using var title=connection.CreateCommand();
        title.CommandText="INSERT INTO titles(rowid,heading) VALUES(@i,@h)";
        for(int i=0;i<sections.Count;i++)
        {
            var section=sections[i];
            insert.Parameters.Clear();
            insert.Parameters.AddWithValue("@i",i+1);
            insert.Parameters.AddWithValue("@f",section.File);
            insert.Parameters.AddWithValue("@h",section.Heading);
            insert.Parameters.AddWithValue("@b",section.Body);
            insert.ExecuteNonQuery();
            index.Parameters.Clear();
            index.Parameters.AddWithValue("@i",i+1);
            index.Parameters.AddWithValue("@h",section.Heading);
            index.Parameters.AddWithValue("@b",section.Body);
            index.Parameters.AddWithValue("@hs",Stemmed(section.Heading));
            index.Parameters.AddWithValue("@bs",Stemmed(section.Body));
            index.ExecuteNonQuery();
            title.Parameters.Clear();
            title.Parameters.AddWithValue("@i",i+1);
            title.Parameters.AddWithValue("@h",section.File+" "+section.Heading);
            title.ExecuteNonQuery();
        }
        transaction.Commit();
        db=connection;
    }

    private static void Execute(SqliteConnection connection,string sql)
    {
        using var command=connection.CreateCommand();
        command.CommandText=sql;
        command.ExecuteNonQuery();
    }

    private static List<Section> LoadProse()
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
        foreach(var file in files.Distinct().OrderBy(f=>f))
        {
            string[] lines;
            try{lines=File.ReadAllLines(file);}catch(IOException){continue;}
            var heading="";var body=new StringBuilder();var stack=new List<(int,string)>();
            string relative=Path.GetRelativePath(root,file).Replace('\\','/');
            void Flush()
            {
                if(body.Length==0)return;
                result.Add(new Section(relative,heading,body.ToString()));
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

    // ------------------------------------------------------------------ tokens

    private static readonly string[] Endings=
    [
        "ами","ями","иями","ого","его","ому","ему","ыми","ими","ей","ой","ах","ях","ам","ям",
        "ов","ев","ий","ый","ая","яя","ое","ее","ые","ие","ом","ем","ью","ия","ие","у","ю","а","я","ы","и","е","о","ь",
    ];

    /// Folds a Russian word to a stem by dropping one grammatical ending. Crude on purpose: the
    /// stemmed column and the stemmed query are folded the same way, so "логистике" and
    /// "логистика" meet, while the untouched columns keep exact matching available.
    private static string Stem(string token)
    {
        if(token.Length<5)return token;
        foreach(var ending in Endings)
            if(token.Length-ending.Length>=4&&token.EndsWith(ending,StringComparison.Ordinal))
                return token[..^ending.Length];
        return token;
    }

    private static IEnumerable<string> Tokens(string text)=>
        Regex.Split(text.ToLowerInvariant(),@"[^\p{L}\p{N}]+").Where(t=>t.Length>=3);

    private static string Stemmed(string text)=>string.Join(' ',Tokens(text).Select(Stem));

    // Words that carry no subject. "сколько войск охраняет крипт" is a question about крипт, and
    // letting "сколько" and "охраняет" weigh as much as the noun drags in every section that
    // happens to count something.
    private static readonly HashSet<string> Stopwords=
    [
        "как","что","чем","где","когда","зачем","почему","какой","какая","какие","какое","кто",
        "для","при","над","под","про","без","его","это","этот","эта","они","она","оно","мне","them",
        "нужно","надо","можно","нельзя","есть","быть","был","была","было","будет","дает","даёт",
        "сколько","который","которая","которые","très","the","and","for","with","what","how","does",
    ];

    /// FTS5 reads its own query language, so anything the user typed has to arrive as quoted
    /// tokens. Each term is matched exactly or by stem, in any column.
    private static string[] QueryTerms(string query)
    {
        var terms=Tokens(query).Distinct().ToArray();
        var meaningful=terms.Where(t=>!Stopwords.Contains(t)).ToArray();
        return meaningful.Length>0?meaningful:terms;
    }

    private static string Forms(string term)
    {
        var stem=Stem(term);
        var forms=new List<string>{$"\"{term}\"*"};
        if(stem!=term)forms.Add($"\"{stem}\"*");
        return "("+string.Join(" OR ",forms)+")";
    }

    /// Sections covering every term of the question answer it; sections sharing one word merely
    /// mention it. So the conjunction is asked first and the disjunction only fills what is left.
    private static string? MatchExpression(string query,bool all)
    {
        var terms=QueryTerms(query);
        if(terms.Length==0)return null;
        return string.Join(all?" AND ":" OR ",terms.Select(Forms));
    }

    // ------------------------------------------------------------------ queries

    /// <summary>
    /// Layer one of the reference: what exists, at the cheapest possible detail. Without a path it
    /// lists documents only; with a path it opens that one document's headings. Listing every
    /// heading of every document at once costs more than the answer usually does, so it is not done.
    /// </summary>
    public DocsCatalog Catalog(string? path)
    {
        EnsureLoaded();
        lock(gate)
        {
            if(db is null||sectionCount==0)return new(false,"Documentation tree not found next to the bridge build",0,[],null);
            int documents=Scalar(db,"SELECT COUNT(DISTINCT file) FROM sections");
            if(!string.IsNullOrWhiteSpace(path))
            {
                var wanted=path.Replace('\\','/').Trim().TrimStart('/');
                var headings=new List<string>();
                using var command=db.CreateCommand();
                command.CommandText="SELECT DISTINCT heading FROM sections WHERE file=@f COLLATE NOCASE AND heading<>'' ORDER BY id";
                command.Parameters.AddWithValue("@f",wanted);
                using(var reader=command.ExecuteReader())while(reader.Read())headings.Add(reader.GetString(0));
                if(headings.Count==0)
                    return new(true,"No document with this path; call without a path to list documents",documents,[],null);
                int sections=Scalar(db,"SELECT COUNT(*) FROM sections WHERE file=@f COLLATE NOCASE",("@f",wanted));
                return new(true,null,documents,[new DocEntry(wanted,sections,headings)],
                    "hota_docs_read with this path and one of these headings returns the section text");
            }
            var entries=new List<DocEntry>();
            using var list=db.CreateCommand();
            list.CommandText="SELECT file,COUNT(*) FROM sections GROUP BY file ORDER BY file";
            using(var reader=list.ExecuteReader())while(reader.Read())entries.Add(new(reader.GetString(0),reader.GetInt32(1),null));
            return new(true,null,entries.Count,entries,
                "hota_docs_catalog with a path lists that document's headings; hota_docs answers a question directly");
        }
    }

    private static int Scalar(SqliteConnection connection,string sql,params (string,object)[] parameters)
    {
        using var command=connection.CreateCommand();
        command.CommandText=sql;
        foreach(var (name,value) in parameters)command.Parameters.AddWithValue(name,value);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>
    /// Layer three: the actual text, in windows. A section that does not fit is not silently cut —
    /// the answer says how long it is and where the next window starts.
    /// </summary>
    public DocText Read(string path,string? heading,int offset,int maxChars)
    {
        path=(path??string.Empty).Replace('\\','/').Trim().TrimStart('/');
        EnsureLoaded();
        lock(gate)
        {
            if(db is null)return new(path,heading,false,"Documentation tree not found next to the bridge build",string.Empty,0,0,null);
            var all=new List<(string Heading,string Body)>();
            using var command=db.CreateCommand();
            command.CommandText="SELECT heading,body FROM sections WHERE file=@f COLLATE NOCASE ORDER BY id";
            command.Parameters.AddWithValue("@f",path);
            using(var reader=command.ExecuteReader())while(reader.Read())all.Add((reader.GetString(0),reader.GetString(1)));
            if(all.Count==0)return new(path,heading,false,"No document with this path; ask the catalog for exact paths",string.Empty,0,0,null);
            var chosen=string.IsNullOrWhiteSpace(heading)
                ?all
                :all.Where(s=>s.Heading.Split(" / ").Any(part=>part.Contains(heading,StringComparison.OrdinalIgnoreCase))).ToList();
            if(chosen.Count==0)
            {
                var available=all.Select(s=>s.Heading).Where(h=>h.Length>0).Take(12);
                return new(path,heading,false,"Heading not found. Available headings: "+string.Join(" | ",available),string.Empty,0,0,null);
            }
            var whole=string.Join("\n\n",chosen.Select(s=>(s.Heading.Length>0?"## "+s.Heading+"\n":"")+s.Body.Trim()));
            int window=Math.Clamp(maxChars<=0?6000:maxChars,500,20000);
            int start=Math.Clamp(offset,0,whole.Length);
            var text=whole[start..Math.Min(whole.Length,start+window)];
            int? next=start+text.Length<whole.Length?start+text.Length:null;
            var note=next is null?null
                :$"Shown {text.Length} of {whole.Length} characters. Pass offset={next} for the rest, or name a heading to read less.";
            return new(path,heading,true,note,text,start,whole.Length,next);
        }
    }

    /// <summary>
    /// Layer two: the answer itself. Detail decides how much text comes back per hit —
    /// "titles" for orientation, "snippet" for the passage that answers, "full" for the whole
    /// section. The default is the passage, because a search that returns whole sections costs
    /// several times what the answer is worth and the agent rarely reads past the passage.
    /// </summary>
    public DocsAnswer Search(string query,int limit,string? detail)
    {
        EnsureLoaded();
        bool titlesOnly=string.Equals(detail,"titles",StringComparison.OrdinalIgnoreCase);
        bool full=string.Equals(detail,"full",StringComparison.OrdinalIgnoreCase);
        lock(gate)
        {
            if(db is null||sectionCount==0)return new(query,0,false,"Documentation tree not found next to the bridge build",[],null);
            var conjunction=MatchExpression(query,all:true);
            var disjunction=MatchExpression(query,all:false);
            if(conjunction is null||disjunction is null)
                return new(query,proseCount,true,"Query needs at least one word of three or more letters",[],null);
            int take=Math.Clamp(limit,1,8);
            var hits=Query(db,conjunction,take,titlesOnly,full,cards:false);
            if(hits.Count<take)
            {
                var seen=hits.Select(h=>h.File+" "+h.Heading).ToHashSet();
                foreach(var hit in Query(db,disjunction,take,titlesOnly,full,cards:false))
                    if(seen.Add(hit.File+" "+hit.Heading)&&hits.Count<take)hits.Add(hit);
            }
            string? note=null;
            if(hits.Count==0)
            {
                // No playbook covers it. A rule card may still name the thing that was asked about.
                hits=Query(db,disjunction,take,titlesOnly,full,cards:true);
                if(hits.Count>0)note="No playbook covers this; these are rule cards from the game's own tables";
            }
            if(hits.Count==0)
            {
                // Nothing matched a whole word. The query was probably misheard or mistyped, so the
                // trigram index over titles gets a chance before the agent is told there is nothing.
                var fuzzy=Fuzzy(db,query,take,titlesOnly,full);
                if(fuzzy.Count>0)
                {
                    hits=fuzzy;
                    note="No exact word matched; these are the closest titles by spelling";
                }
                else note="Nothing in the documentation matches this query";
            }
            string? next=hits.Count==0
                ?"Try other words, or hota_docs_catalog to see what subjects exist"
                :full?null
                :"hota_docs_read with a hit's file and heading returns that whole section";
            return new(query,proseCount,true,note,hits,next);
        }
    }

    private static List<DocHit> Query(SqliteConnection connection,string expression,int take,bool titlesOnly,bool full,bool cards)
    {
        using var command=connection.CreateCommand();
        command.CommandText=$"""
            SELECT s.file, s.heading, bm25(fts,{Weights}) AS rank,
                   snippet(fts,1,'','','…',24) AS passage, s.body
            FROM fts JOIN sections s ON s.id=fts.rowid
            WHERE fts MATCH @m AND (s.file LIKE @p)=@c
            ORDER BY rank
            LIMIT @n
            """;
        command.Parameters.AddWithValue("@m",expression);
        command.Parameters.AddWithValue("@p",ReferencePrefix+"%");
        command.Parameters.AddWithValue("@c",cards?1:0);
        command.Parameters.AddWithValue("@n",take);
        return Collect(command,titlesOnly,full);
    }

    /// Typos and mishearings never match a whole word, so the query is broken into the same
    /// three-letter pieces the trigram index is built from: "уронн" still shares "уро" and "рон"
    /// with "урон", and bm25 ranks by how many pieces landed.
    private static List<DocHit> Fuzzy(SqliteConnection connection,string query,int take,bool titlesOnly,bool full)
    {
        var grams=new List<string>();
        foreach(var token in QueryTerms(query))
            for(int i=0;i+3<=token.Length;i++)
            {
                var gram=token.Substring(i,3);
                if(!gram.Contains('"')&&!grams.Contains(gram))grams.Add(gram);
            }
        if(grams.Count==0)return [];
        var probe=string.Join(" OR ",grams.Select(g=>$"\"{g}\""));
        using var command=connection.CreateCommand();
        command.CommandText="""
            SELECT s.file, s.heading, bm25(titles) AS rank, substr(s.body,1,400), s.body
            FROM titles JOIN sections s ON s.id=titles.rowid
            WHERE titles MATCH @m
            ORDER BY rank
            LIMIT @n
            """;
        command.Parameters.AddWithValue("@m",probe);
        command.Parameters.AddWithValue("@n",take);
        try{return Collect(command,titlesOnly,full);}
        catch(SqliteException){return [];}
    }

    private static List<DocHit> Collect(SqliteCommand command,bool titlesOnly,bool full)
    {
        var hits=new List<DocHit>();
        using var reader=command.ExecuteReader();
        while(reader.Read())
        {
            var passage=titlesOnly?"":reader.GetString(3).Trim();
            if(passage.Length>600)passage=passage[..600]+"…";
            string? text=null;
            if(full)
            {
                text=reader.GetString(4).Trim();
                if(text.Length>4000)text=text[..4000]+"… (hota_docs_read with offset returns the rest)";
            }
            // bm25 returns a negative score where more negative is better; flipped so a bigger
            // number reads as a better hit.
            hits.Add(new(reader.GetString(0),reader.GetString(1),Math.Round(-reader.GetDouble(2),3),passage,text));
        }
        return hits;
    }
}
