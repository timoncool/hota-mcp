using System.Text;
using System.Text.RegularExpressions;

namespace HotaMcp;

public sealed record ReferenceCard(string Kind,string Name,Dictionary<string,string> Fields,string Text);
public sealed record ReferenceAnswer(bool Available,string? Note,string Source,List<ReferenceCard> Cards);

/// <summary>
/// The game's own rule tables, turned into cards the agent can ask for by name.
///
/// Heroes III keeps its reference data in tab separated tables inside the LOD archives: secondary
/// skills with the text for each mastery, spells with cost, power and per-mastery effect, creature
/// stats, artefacts, buildings, map objects. HotA ships its own copies of those tables, so reading
/// them from the installation gives the rules of the exact build being played instead of a copy
/// that goes stale on the next update.
///
/// This is reference, not observation: it never says anything about the current party. It is only
/// reachable through the documentation tools, so it cannot leak into a game answer by accident.
/// </summary>
internal sealed class GameReference
{
    /// Table name, the kind of card it produces, and how many leading rows are group headers.
    private static readonly (string File,string Kind)[] Tables=
    [
        ("SSTRAITS.TXT","навык"),
        ("SPTRAITS.TXT","заклинание"),
        ("CRTRAITS.TXT","существо"),
        ("artraits.txt","артефакт"),
        ("HeroSpec.txt","специализация героя"),
        ("HOTRAITS.TXT","герой"),
        ("Building.txt","здание"),
        ("BldgNeut.txt","здание города"),
        ("BldgSpec.txt","особое здание"),
        ("Dwelling.txt","жилище"),
        ("ObjNames.txt","объект карты"),
        ("MineName.txt","шахта"),
        ("TERRNAME.txt","местность"),
        ("PriSkill.txt","основной навык"),
        ("SkillLev.txt","уровень навыка"),
        ("CrBanks.txt","банк существ"),
        ("TownType.txt","фракция"),
    ];

    private readonly object gate=new();
    private List<ReferenceCard>? cards;
    private string source="";

    public string Source{get{lock(gate){return source;}}}

    public List<ReferenceCard> Cards()
    {
        lock(gate)
        {
            if(cards is not null)return cards;
            cards=Load(out source);
            return cards;
        }
    }

    /// Looks up cards by name, optionally narrowed to one kind. Matching is by substring, because
    /// the agent asks with the word it saw on screen rather than the exact table spelling.
    private static List<string>? creatureNames;

    /// Creature names in table order, so a stack read from memory can be reported by name instead
    /// of by the row number the game happens to store. A number is not an identifier a player has.
    public static List<string> CreatureNames()
    {
        if(creatureNames is not null)return creatureNames;
        var result=new List<string>();
        string? data=FindDataDirectory();
        if(data is not null)
            foreach(var archive in new[]{"HotA_lng.lod","H3bitmap.lod"})
            {
                var lod=LodArchive.Open(Path.Combine(data,archive));
                string? text=lod?.ReadText("CRTRAITS.TXT");
                if(text is null)continue;
                foreach(var row in Rows(text).Skip(2))
                {
                    string name=Clean(row.ElementAtOrDefault(0)??"");
                    if(name.Length<2)continue;
                    // Faction captions fill only the first cell; creature rows carry numbers.
                    if(row.Length<5||!row.Skip(1).Take(7).Any(c=>int.TryParse(Clean(c),out _)))continue;
                    result.Add(name);
                }
                if(result.Count>0)break;
            }
        creatureNames=result;
        return result;
    }

    private static List<string>? buildingNames;

    /// Names of the buildings every town shares, in the order the game numbers them: the five mage
    /// guild levels, tavern, shipyard, fort, citadel, castle, the four halls, marketplace, resource
    /// silo and blacksmith. The row order of BLDGNEUT is that numbering, so nothing is guessed.
    /// Town-specific buildings above that row are numbered per faction and are not in this table;
    /// their names are on the town's own construction screen.
    public const string Decoration="украшение города";

    public static string Building(int id,int faction=-1)
    {
        if(buildingNames is null)
        {
            var result=new List<string>();
            string? data=FindDataDirectory();
            if(data is not null)
                foreach(var archive in new[]{"HotA_lng.lod","H3bitmap.lod"})
                {
                    var lod=LodArchive.Open(Path.Combine(data,archive));
                    string? text=lod?.ReadText("BLDGNEUT.TXT");
                    if(text is null)continue;
                    foreach(var row in Rows(text))result.Add(Clean(row.ElementAtOrDefault(0)??""));
                    if(result.Count>0)break;
                }
            buildingNames=result;
        }
        if(id>=0&&id<17&&id<buildingNames.Count&&buildingNames[id].Length>1)return buildingNames[id];
        // The towns HotA added keep their building names in HotA.dat, not in the SoD tables,
        // which carry only placeholders for them.
        if(faction>=0&&id is >=17 and <=27&&HotaTownRow("sp",faction,id-17) is {} hotaSpecial)return hotaSpecial;
        if(faction>=0&&id is >=30 and <=43&&HotaTownRow("dw",faction,id-30) is {} hotaDwelling)return hotaDwelling;
        // Buildings of one town type: the special ones are eleven rows per faction in BldgSpec,
        // row k for building 17+k; the dwellings fourteen rows per faction in Dwelling, the base
        // ones by level and the upgraded ones seven rows further on.
        if(faction>=0&&id is >=17 and <=27&&PlainRow("BldgSpec.txt",faction*11+id-17) is {Length:>1} special)return special;
        if(faction>=0&&id is >=30 and <=36&&PlainRow("Dwelling.txt",faction*14+id-30) is {Length:>1} dwelling)return dwelling;
        if(faction>=0&&id is >=37 and <=43&&PlainRow("Dwelling.txt",faction*14+7+id-37) is {Length:>1} upgraded)return upgraded;
        // 28-29 are the decorations the game raises by itself beside a real building; the hall
        // does not list them and a player never orders them.
        if(id is 28 or 29)return Decoration;
        if(id is >=30 and <=36)return $"жилище {id-29} уровня";
        if(id is >=37 and <=43)return $"улучшенное жилище {id-36} уровня";
        return $"постройка №{id}";
    }

    private static Dictionary<string,string[]>? hotaTowns;

    /// HotA.dat holds, per added town type, a block keyed «Castles\\cas<dw|sp>cost<type+1>.str»:
    /// a length-prefixed text of fourteen dwelling names (base, then upgraded) or eleven special
    /// building names, one per line.
    private static string? HotaTownRow(string kind,int faction,int row)
    {
        if(hotaTowns is null)
        {
            hotaTowns=new();
            string? data=FindDataDirectory();
            string file=data is null?"":Path.Combine(Path.GetDirectoryName(data)!,"HotA.dat");
            if(File.Exists(file))
            {
                byte[] bytes=File.ReadAllBytes(file);
                string latin=Encoding.Latin1.GetString(bytes);
                foreach(System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(latin,@"Castles\\cas(dw|sp)cost(\d+)\.str"))
                    for(int at=m.Index+m.Length;at<Math.Min(bytes.Length-4,m.Index+m.Length+200);at++)
                    {
                        int length=BitConverter.ToInt32(bytes,at);
                        if(length is <=20 or >=2000||at+4+length>bytes.Length)continue;
                        string text=Encoding.GetEncoding(1251).GetString(bytes,at+4,length);
                        if(!text.Contains('\n'))continue;
                        hotaTowns[$"{m.Groups[1].Value}{m.Groups[2].Value}"]=text.Split('\n').Select(l=>l.Trim()).ToArray();
                        break;
                    }
            }
        }
        return hotaTowns.TryGetValue($"{kind}{faction+1}",out var rows)&&row<rows.Length&&rows[row].Length>1?rows[row]:null;
    }

    private static readonly Dictionary<string,List<string[]>> plainTables=new();

    /// The first cell of one row of a table that has no heading.
    private static string? PlainRow(string file,int row)
    {
        if(!plainTables.TryGetValue(file,out var rows))
        {
            rows=[];
            string? data=FindDataDirectory();
            if(data is not null)
                foreach(var archive in new[]{"HotA_lng.lod","H3bitmap.lod"})
                {
                    string? text=LodArchive.Open(Path.Combine(data,archive))?.ReadText(file);
                    if(text is null)continue;
                    rows=Rows(text);break;
                }
            plainTables[file]=rows;
        }
        return row>=0&&row<rows.Count?Clean(rows[row].ElementAtOrDefault(0)??""):null;
    }

    /// The twelve town types in the order the starting-town grid itself lays them out, which is
    /// also the order the game numbers factions. Read off the grid cell by cell with the right
    /// button, the way a player reads a picture that carries no caption.
    public static readonly string[] Factions=[
        "Замок","Оплот","Башня","Инферно","Некрополис","Темница",
        "Цитадель","Крепость","Сопряжение","Причал","Фабрика","Кронверк"];

    /// The name of a faction by the number the game gives it.
    public static string Faction(int index)=>
        index>=0&&index<Factions.Length?Factions[index]:$"город №{index}";

    private static List<string>? heroNames;

    /// Names of the heroes in the order the game numbers them — the row order of HOTRAITS is the
    /// hero number, and the starting-hero grid of one town holds that town's sixteen heroes in
    /// that same order, so a portrait in the grid can be named instead of pressed blind.
    public static string Hero(int id)
    {
        if(heroNames is null)
        {
            var result=new List<string>();
            string? data=FindDataDirectory();
            if(data is not null)
                foreach(var archive in new[]{"HotA_lng.lod","H3bitmap.lod"})
                {
                    var lod=LodArchive.Open(Path.Combine(data,archive));
                    string? text=lod?.ReadText("HOTRAITS.TXT");
                    if(text is null)continue;
                    foreach(var row in Rows(text))result.Add(Clean(row.ElementAtOrDefault(0)??""));
                    if(result.Count>0)break;
                }
            heroNames=result;
        }
        return id>=0&&id<heroNames.Count&&heroNames[id].Length>1?heroNames[id]:$"герой №{id}";
    }

    /// Entries of a game text table, without its heading. These tables open with a caption row
    /// and a row of column titles, and only then the entries, in the order the game numbers them.
    /// The heading is cut by what it says, not by a count: counting rows by hand is what named four
    /// skills and two specialties wrongly before.
    private static List<string[]> Entries(string file)
    {
        string? data=FindDataDirectory();
        if(data is not null)
            foreach(var archive in new[]{"HotA_lng.lod","H3bitmap.lod"})
            {
                var lod=LodArchive.Open(Path.Combine(data,archive));
                string? text=lod?.ReadText(file);
                if(text is null)continue;
                var rows=Rows(text);
                int titles=rows.FindIndex(r=>Clean(r.ElementAtOrDefault(0)??"") is "Name" or "(short)");
                if(titles<0)throw new InvalidOperationException($"{file}: column titles not found");
                return rows.Skip(titles+1).ToList();
            }
        return [];
    }

    private static readonly Dictionary<string,List<string>> lineTables=new();

    /// A table the game keeps one entry per line, with no heading: the victory and loss
    /// conditions a scenario can have.
    private static string? Line(string file,int row)
    {
        if(!lineTables.TryGetValue(file,out var lines))
        {
            lines=[];
            string? data=FindDataDirectory();
            if(data is not null)
                foreach(var archive in new[]{"HotA_lng.lod","H3bitmap.lod"})
                {
                    string? text=LodArchive.Open(Path.Combine(data,archive))?.ReadText(file);
                    if(text is null)continue;
                    lines=text.Split('\n').Select(l=>l.Trim()).ToList();
                    break;
                }
            lineTables[file]=lines;
        }
        return row>=0&&row<lines.Count&&lines[row].Length>0?lines[row]:null;
    }

    /// The victory condition of a scenario by the type byte its header carries: 0xFF is the
    /// ordinary «defeat every enemy», which is the first line of the table; type N is line N+1.
    public static string Victory(int type)=>Line("VCDESC.TXT",type==0xFF?0:type+1)??$"особое условие победы №{type}";

    /// A mine by its subtype: the game names them one per line in that order.
    public static string Mine(int subtype)=>Line("MineName.txt",subtype)??$"шахта №{subtype}";

    private static List<string>? banks;

    /// A creature bank by its subtype. The table gives each bank several rows, one per level of
    /// guard, and writes the name only on the first; the names in order are the subtypes.
    public static string Bank(int subtype)
    {
        if(banks is null)
        {
            banks=[];
            string? data=FindDataDirectory();
            if(data is not null)
                foreach(var archive in new[]{"HotA_lng.lod","H3bitmap.lod"})
                {
                    string? text=LodArchive.Open(Path.Combine(data,archive))?.ReadText("CrBanks.txt");
                    if(text is null)continue;
                    var rows=Rows(text);
                    int titles=rows.FindIndex(r=>Clean(r.ElementAtOrDefault(0)??"")=="Adventure Object");
                    if(titles<0)break;
                    banks=rows.Skip(titles+1).Select(r=>Clean(r.ElementAtOrDefault(0)??"")).Where(n=>n.Length>0).ToList();
                    break;
                }
        }
        return subtype>=0&&subtype<banks.Count?banks[subtype]:HotaObject(16,subtype)??$"банк существ №{subtype}";
    }

    private static Dictionary<(int Type,int Subtype),string>? hotaObjects;

    /// The expansion's own objects — its banks, the resource warehouses and the rest — are named
    /// in HotA.dat, in the table the map editor shows as a hint: type, subtype (−1 for any), then
    /// the name on the first line of a quoted description.
    public static string? HotaObject(int type,int subtype)
    {
        if(hotaObjects is null)
        {
            hotaObjects=[];
            string? data=FindDataDirectory();
            string file=data is null?"":Path.Combine(Path.GetDirectoryName(data)!,"HotA.dat");
            if(File.Exists(file))
            {
                string text=Encoding.GetEncoding(1251).GetString(File.ReadAllBytes(file));
                foreach(Match m in Regex.Matches(text,"(?:^|\n)(\\d+)\r\n(-?\\d+)\r\n\"([^\r\"]+)"))
                    hotaObjects.TryAdd((int.Parse(m.Groups[1].Value),int.Parse(m.Groups[2].Value)),m.Groups[3].Value.Trim());
            }
        }
        return hotaObjects.TryGetValue((type,subtype),out var exact)?exact
            :hotaObjects.TryGetValue((type,-1),out var any)?any:null;
    }

    /// Every entry of HotA.dat's hint table as a card: type, subtype, then the quoted text whose
    /// first line is the name. Type 5 is an artefact (subtype = artefact number, the text carries
    /// «Класс: …» and the effect); everything else is a map object.
    private static List<ReferenceCard> HotaDatCards(string data)
    {
        var result=new List<ReferenceCard>();
        string file=Path.Combine(Path.GetDirectoryName(data)!,"HotA.dat");
        if(!File.Exists(file))return result;
        string text=Encoding.GetEncoding(1251).GetString(File.ReadAllBytes(file));
        var seen=new HashSet<(string,string)>();
        foreach(Match m in Regex.Matches(text,"(?:^|\n)(\\d+)\r\n(-?\\d+)\r\n\"([^\"]*)\""))
        {
            int type=int.Parse(m.Groups[1].Value),subtype=int.Parse(m.Groups[2].Value);
            var lines=m.Groups[3].Value.Replace("\r","").Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
            if(lines.Length==0||lines[0].Length<2)continue;
            string name=lines[0],body=string.Join("\n",lines.Skip(1));
            string kind=type==5?"артефакт":"объект карты";
            if(!seen.Add((kind,name+"\n"+body)))continue;
            var fields=new Dictionary<string,string>{["Тип объекта"]=type.ToString(),["Подтип"]=subtype.ToString()};
            if(type==5)
            {
                fields["Номер артефакта"]=subtype.ToString();
                if(lines.Skip(1).FirstOrDefault(l=>l.StartsWith("Класс:",StringComparison.Ordinal)) is string cls)
                    fields["Класс"]=cls["Класс:".Length..].Trim().TrimEnd('.');
            }
            if(body.Length>0)fields["Описание"]=body;
            result.Add(new(kind,name,fields,body.Length>0?$"{name}\n{body}":name));
        }
        return result;
    }

    /// The loss condition, numbered the same way.
    public static string Loss(int type)=>Line("LCDESC.TXT",type==0xFF?0:type+1)??$"особое условие поражения №{type}";

    private static List<string[]>? spells;

    /// A spell by the number its picture carries in the mage guild or the book. The spell table
    /// splits into sections with their own title rows; only rows that carry a level number are
    /// spells, and they run in the game's own spell order.
    public static string? Spell(int frame)
    {
        spells??=Entries("SPTRAITS.TXT").Where(r=>int.TryParse(Clean(r.ElementAtOrDefault(2)??""),out _)).ToList();
        if(frame<0||frame>=spells.Count)return null;
        return Clean(spells[frame].ElementAtOrDefault(0)??"") is {Length:>0} name?name:null;
    }

    private static List<string[]>? artifacts;

    /// An artefact by the number its picture carries: the frame of the artefact picture is the
    /// artefact's entry in the game's own artefact table.
    private static IReadOnlyDictionary<int,string>? liveArtifacts;

    /// The running game's artefact table names the expansion's artefacts too, which the text
    /// table of the base game does not carry.
    public static void UseLiveArtifactNames(IReadOnlyDictionary<int,string> names)=>liveArtifacts=names;

    private static IReadOnlyDictionary<int,string>? liveArtifactTexts;

    /// What an artefact does, as its own card says it — the description line of the game's
    /// artefact table.
    public static void UseLiveArtifactTexts(IReadOnlyDictionary<int,string> texts)=>liveArtifactTexts=texts;
    public static string? ArtifactText(int frame)=>liveArtifactTexts is not null&&liveArtifactTexts.TryGetValue(frame,out var text)?text:null;

    public static string? Artifact(int frame)
    {
        if(liveArtifacts is not null&&liveArtifacts.TryGetValue(frame,out var live))return live;
        artifacts??=Entries("artraits.txt");
        if(frame<0||frame>=artifacts.Count)return null;
        return Clean(artifacts[frame].ElementAtOrDefault(0)??"") is {Length:>0} name?name:null;
    }

    private static List<string[]>? specialties;

    /// What a hero is a specialist in, by the number of the specialty picture he shows. The
    /// picture number is the entry number of HeroSpec; the text is the one the game itself shows
    /// on the right button — a heading in braces and what it gives.
    public static string? Specialty(int frame)
    {
        specialties??=Entries("HeroSpec.txt");
        if(frame<0||frame>=specialties.Count)return null;
        string card=Clean(specialties[frame].ElementAtOrDefault(2)??"");
        if(card.Length==0)return Clean(specialties[frame].ElementAtOrDefault(0)??"") is {Length:>0} name?name:null;
        var parts=card.Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        string heading=parts[0].Trim('{','}');
        return parts.Length>1?$"{heading} — {parts[1]}":heading;
    }

    private static List<string[]>? skills;

    /// A secondary skill by the number the game gives it.
    public static string Skill(int index)
    {
        skills??=Entries("SSTRAITS.TXT");
        return index>=0&&index<skills.Count&&Clean(skills[index].ElementAtOrDefault(0)??"") is {Length:>0} name
            ?name:$"навык №{index}";
    }

    /// The skill a hero's icon stands for, and how well he knows it. The icon draws frame
    /// (навык+1)*3 + уровень; frame 0 is an empty slot.
    public static string? SkillFromFrame(int frame)
    {
        if(frame<=0)return null;
        string level=(frame%3) switch{0=>"базовый",1=>"продвинутый",_=>"экспертный"};
        return $"{Skill(frame/3-1)} ({level})";
    }

    private static List<string>? objectNames;

    /// Names of the map objects in the order the game numbers them — the row order of ObjNames is
    /// the object type, so a mine, a windmill or a shrine can be named instead of silently dropped
    /// for want of a hand-written table. A player sees every object on the map; so must the agent.
    public static string MapObject(int type,int subtype=-1)
    {
        if(objectNames is null)
        {
            var result=new List<string>();
            string? data=FindDataDirectory();
            if(data is not null)
                foreach(var archive in new[]{"HotA_lng.lod","H3bitmap.lod"})
                {
                    var lod=LodArchive.Open(Path.Combine(data,archive));
                    string? text=lod?.ReadText("ObjNames.txt");
                    if(text is null)continue;
                    foreach(var row in Rows(text))result.Add(Clean(row.ElementAtOrDefault(0)??""));
                    if(result.Count>0)break;
                }
            objectNames=result;
        }
        return type>=0&&type<objectNames.Count&&objectNames[type].Length>1
            ?objectNames[type]:HotaObject(type,subtype)??$"объект типа {type}";
    }

    private static IReadOnlyDictionary<int,string>? liveCreatures;

    /// The running game keeps one table of every creature it knows — the expansion's towns
    /// included, which the text files do not list. Once the game is attached, its own names are
    /// used, so a Kobold or a Mechanic is named the way its card names it.
    public static void UseLiveCreatureNames(IReadOnlyDictionary<int,string> names)=>liveCreatures=names;

    private static List<ReferenceCard> liveCards=[];

    /// Cards for the creatures the text files do not carry — the three expansion towns — built
    /// from the running game's own creature table: the same numbers its creature card shows.
    public static void UseLiveCreatureCards(List<ReferenceCard> cards)=>liveCards=cards;

    /// Gold price of one creature, found by the plural name a dwelling or the fort shows.
    public static int? CreatureGold(string plural)=>liveCards
        .Where(c=>c.Fields.TryGetValue("Plural",out var p)&&string.Equals(p,plural.Trim(),StringComparison.OrdinalIgnoreCase))
        .Select(c=>int.TryParse(c.Fields["Gold"],out int g)?g:(int?)null).FirstOrDefault();

    /// Names a stack by its type as the game stores it; an unknown type stays a number rather than
    /// becoming a guess.
    public static string Creature(int type)
    {
        if(liveCreatures is not null&&liveCreatures.TryGetValue(type,out var live))return live;
        var names=CreatureNames();
        if(type>=0&&type<names.Count)return names[type];
        return $"существо №{type} (его имени нет в таблицах игры)";
    }

    public ReferenceAnswer Find(string name,string? kind,int limit)
    {
        var all=Cards();
        if(all.Count==0)
            return new(false,"Game reference tables were not found next to the running game",source,[]);
        string needle=name.Trim();
        if(needle.Length<2)return new(true,"Ask for at least two characters",source,[]);
        var pool=all.Concat(liveCards.Where(l=>!all.Any(c=>c.Kind==l.Kind&&string.Equals(c.Name,l.Name,StringComparison.OrdinalIgnoreCase))));
        var matches=pool
            .Where(c=>kind is null||c.Kind.Contains(kind,StringComparison.OrdinalIgnoreCase))
            .Select(c=>(Card:c,Rank:Rank(c.Name,needle)))
            .Where(x=>x.Rank>0)
            // A wandering stack is named like its creature; the creature card carries the numbers
            // a fight is decided on, so it comes before the map object of the same name.
            .OrderByDescending(x=>x.Rank).ThenBy(x=>x.Card.Kind=="объект карты"?1:0).ThenBy(x=>x.Card.Name.Length)
            .Take(Math.Clamp(limit,1,20)).Select(x=>x.Card).ToList();
        if(matches.Count>0)return new(true,null,source,matches);
        // A name dictated by voice or typed from memory arrives with a slip or in another case form;
        // the closest names by edit distance stand in, and the answer says they are the closest.
        string wanted=Fold(needle);
        var near=pool
            .Where(c=>kind is null||c.Kind.Contains(kind,StringComparison.OrdinalIgnoreCase))
            .Select(c=>(Card:c,Distance:Distance(Fold(c.Name),wanted)))
            .Where(x=>x.Distance<=Math.Max(2,wanted.Length/4))
            .OrderBy(x=>x.Distance).ThenBy(x=>x.Card.Name.Length)
            .Take(Math.Clamp(limit,1,20)).Select(x=>x.Card).ToList();
        return near.Count>0
            ?new(true,$"No card named «{needle}»; the closest names are shown — check that this is the one you meant",source,near)
            :new(true,"No card with this name; try hota_docs for a description in prose",source,[]);
    }

    private static string Fold(string text)=>text.Trim().ToLowerInvariant().Replace('ё','е');

    private static int Distance(string a,string b)
    {
        var row=new int[b.Length+1];
        for(int j=0;j<=b.Length;j++)row[j]=j;
        for(int i=1;i<=a.Length;i++)
        {
            int diagonal=row[0];row[0]=i;
            for(int j=1;j<=b.Length;j++)
            {
                int above=row[j];
                row[j]=Math.Min(Math.Min(row[j]+1,row[j-1]+1),diagonal+(a[i-1]==b[j-1]?0:1));
                diagonal=above;
            }
        }
        return row[b.Length];
    }

    private static int Rank(string cardName,string needle)
    {
        if(string.Equals(cardName,needle,StringComparison.OrdinalIgnoreCase))return 3;
        if(cardName.StartsWith(needle,StringComparison.OrdinalIgnoreCase))return 2;
        if(cardName.Contains(needle,StringComparison.OrdinalIgnoreCase))return 1;
        return 0;
    }

    private static List<ReferenceCard> Load(out string source)
    {
        source="";
        string? data=FindDataDirectory();
        if(data is null)return [];
        // HotA ships its own copies; they win over the base game's tables for the same file.
        var archives=new[]{"HotA_lng.lod","HotA.lod","H3bitmap.lod","H3ab_bmp.lod"}
            .Select(name=>(Name:name,Archive:LodArchive.Open(Path.Combine(data,name))))
            .Where(x=>x.Archive is not null).ToArray();
        if(archives.Length==0)return [];
        var used=new List<string>();
        var result=new List<ReferenceCard>();
        var seen=new HashSet<string>();
        foreach(var (file,kind) in Tables)
        {
            foreach(var (archiveName,archive) in archives)
            {
                string? text=archive!.ReadText(file);
                if(text is null)continue;
                int before=result.Count;
                result.AddRange(Parse(text,kind,seen));
                // A table that is present but yields nothing is a parsing failure, not an absence:
                // saying so beats a reference that quietly answers "no such thing".
                used.Add(result.Count>before
                    ?$"{file} ({archiveName})"
                    :$"{file} ({archiveName}: прочитан, но ни одной карточки)");
                break;
            }
        }
        var missing=Tables.Select(t=>t.File)
            .Where(file=>!used.Any(entry=>entry.StartsWith(file,StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if(missing.Length>0)used.Add("не найдены в архивах: "+string.Join(", ",missing));
        // The expansion's hint table: every artefact with its class and effect — its own artefacts
        // included, which artraits.txt does not carry — and every map object with what it gives.
        var hint=HotaDatCards(data);
        int added=0;
        foreach(var card in hint)
        {
            bool known=result.Any(c=>c.Kind==card.Kind&&string.Equals(c.Name,card.Name,StringComparison.OrdinalIgnoreCase));
            // A base artefact already has its table card; an object's table card is only a name,
            // so the hint's description is added beside it.
            if(known&&card.Kind=="артефакт")continue;
            if(known&&card.Fields.ContainsKey("Описание"))
                result.RemoveAll(c=>c.Kind==card.Kind&&string.Equals(c.Name,card.Name,StringComparison.OrdinalIgnoreCase)
                    &&(string.IsNullOrWhiteSpace(c.Text)||c.Text.Trim()==c.Name));
            result.Add(card);added++;
        }
        if(added>0)used.Add($"HotA.dat (подсказки редактора: {added} карточек артефактов и объектов)");
        source=used.Count==0?"":string.Join(", ",used);
        return result;
    }

    private static string? FindDataDirectory()
    {
        foreach(var process in System.Diagnostics.Process.GetProcessesByName("h3hota HD"))
            using(process)
                try
                {
                    string? exe=process.MainModule?.FileName;
                    if(exe is null)continue;
                    string data=Path.Combine(Path.GetDirectoryName(exe)!,"Data");
                    if(Directory.Exists(data))return data;
                }
                catch(Exception e)when(e is InvalidOperationException or System.ComponentModel.Win32Exception){}
        string install=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HotaMcp","install.ini");
        if(File.Exists(install))
        {
            string launcher=File.ReadAllText(install).Split('=',2).ElementAtOrDefault(1)?.Trim()??"";
            if(launcher.Length>0)
            {
                string data=Path.Combine(Path.GetDirectoryName(launcher)!,"Data");
                if(Directory.Exists(data))return data;
            }
        }
        return null;
    }

    /// Splits the table into rows and cells. Descriptions are quoted and run over several lines —
    /// the mastery texts of a skill are one cell containing blank lines — so a newline only ends a
    /// row when it falls outside quotes.
    private static List<string[]> Rows(string text)
    {
        var rows=new List<string[]>();
        var row=new List<string>();
        var cell=new StringBuilder();
        bool quoted=false;
        for(int i=0;i<text.Length;i++)
        {
            char c=text[i];
            if(c=='"')
            {
                if(quoted&&i+1<text.Length&&text[i+1]=='"'){cell.Append('"');i++;}
                else quoted=!quoted;
                continue;
            }
            if(!quoted&&c=='\t'){row.Add(cell.ToString());cell.Clear();continue;}
            if(!quoted&&(c=='\n'||c=='\r'))
            {
                if(c=='\r'&&i+1<text.Length&&text[i+1]=='\n')i++;
                row.Add(cell.ToString());cell.Clear();
                rows.Add(row.ToArray());row=[];
                continue;
            }
            cell.Append(c);
        }
        if(cell.Length>0||row.Count>0){row.Add(cell.ToString());rows.Add(row.ToArray());}
        return rows;
    }

    private static IEnumerable<ReferenceCard> Parse(string text,string kind,HashSet<string> seen)
    {
        var rows=Rows(text);
        // Some of the game's tables are plain name lists with no columns at all — the map object
        // names, the mine and terrain names, the faction names. They carry no fields, but the names
        // themselves are what the agent sees on screen, so they are worth a card of their own.
        if(rows.All(row=>row.Count(cell=>Clean(cell).Length>0)<=1))
        {
            foreach(var row in rows)
            {
                string only=Clean(row.FirstOrDefault(cell=>Clean(cell).Length>0)??"");
                if(only.Length<2||double.TryParse(only,out _))continue;
                if(!seen.Add(kind+" "+only))continue;
                yield return new(kind,only,new Dictionary<string,string>(),only);
            }
            yield break;
        }
        int headerRow=HeaderRow(rows);
        if(headerRow<0)yield break;
        var headers=rows[headerRow];
        for(int i=headerRow+1;i<rows.Count;i++)
        {
            var row=rows[i];
            string name=Clean(row.ElementAtOrDefault(0)??"");
            if(name.Length<2)continue;
            // Section titles inside these tables fill only the first cell.
            if(row.Skip(1).All(cell=>Clean(cell).Length==0))continue;
            var fields=new Dictionary<string,string>();
            var body=new StringBuilder(name);
            for(int column=1;column<row.Length;column++)
            {
                string value=Clean(row[column]);
                if(value.Length==0)continue;
                string header=Clean(headers.ElementAtOrDefault(column)??"");
                string label=header.Length>0?header:"колонка "+column;
                fields[label]=value;
                body.Append('\n').Append(label).Append(": ").Append(value);
            }
            if(fields.Count==0)continue;
            if(!seen.Add(kind+" "+name))continue;
            yield return new(kind,name,fields,body.ToString());
        }
    }

    /// The tables open with a sparse row of group captions ("Cost", "Damage") and then the row of
    /// real column names. The real one is the fuller of the two, so the widest non-numeric row
    /// among the first few wins.
    private static int HeaderRow(List<string[]> rows)
    {
        int best=-1,width=0;
        for(int i=0;i<Math.Min(4,rows.Count);i++)
        {
            var filled=rows[i].Select(Clean).Where(cell=>cell.Length>0).ToArray();
            if(filled.Length<2||filled.Any(cell=>double.TryParse(cell,out _)))continue;
            if(filled.Length>width){width=filled.Length;best=i;}
        }
        return best>=0?best:rows.Count>0?0:-1;
    }

    private static string Clean(string cell)=>cell.Trim().Trim('"').Trim();
}
