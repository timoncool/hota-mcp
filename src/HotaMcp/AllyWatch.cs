using System.Text;
using System.Text.Json;

namespace HotaMcp;

/// What an ally does on his turn, followed the way an allied player follows it: where his heroes
/// went, what they reached, picked up or beat, how their armies, levels, skills and artefacts
/// changed, what his towns built and hired. Read from the game while his turn runs — nothing is
/// pressed — and written to a log this side reads on its own turn.
internal sealed class AllyWatch(WindowsGame game,int player,string root)
{
    private sealed record HeroState(int Id,string Name,int Owner,int X,int Y,int Z,int Level,int Experience,
        int[] Primary,string[] Army,string[] Skills,string[] Artifacts);
    private sealed record TownState(int Id,string Name,int Owner,string[] Built,string[] Garrison);
    private sealed record State(int[] Date,bool Combat,List<HeroState> Heroes,List<TownState> Towns);

    private static readonly string[] Colours=["красный","синий","коричневый","зелёный","оранжевый","фиолетовый","бирюзовый","розовый"];
    private static readonly string[] Of=["красного","синего","коричневого","зелёного","оранжевого","фиолетового","бирюзового","розового"];
    private static readonly JsonSerializerOptions Readable=new(){Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    private static readonly string[] ResourceNames=["дерево","ртуть","руда","сера","кристаллы","самоцветы","золото"];
    private static readonly string[] PrimaryNames=["атака","защита","сила магии","знание"];
    private static readonly string[] SkillLevels=["","базовый","продвинутый","эксперт"];

    private string? pendingKey,committedKey;
    private State? committed;
    private int lastActive=-1;
    private int[]? turnResources;
    private int[] turnDate=[];
    /// Objects seen around the ally's heroes, by cell, so a hero arriving on a cell can be said to
    /// have reached what stood there, and an object gone from the map can be named.
    private readonly Dictionary<(int X,int Y,int Z),string> seen=new();

    public string LogFile=>Path.Combine(root,$"ally-log-player{player}.jsonl");

    public void Tick()
    {
        uint main=game.U32(0x699538);
        if(main==0)return;
        uint manager=game.U32(0x6992d0),dialog=game.U32(manager+0x54);
        if(dialog==0)return;
        uint screen=game.U32(dialog);
        if(GameReader.IsFrontend(screen))return;
        int active=game.I32(0x69ccf4);
        if(active is <0 or >7)return;
        var allies=Allies(main);
        if(active!=lastActive)
        {
            if(lastActive>=0)TurnEnded(main,lastActive,allies,screen);
            // A restart of the service in the middle of the ally's turn must not open it twice.
            if(allies.Contains(active)&&!(lastActive<0&&LastTurnMark()==$"— ход {Of[active]} начался"))
            {
                Append(active,Date(main),"turn",$"— ход {Of[active]} начался");
                turnResources=Resources(main,active);
                turnDate=Date(main);
            }
            if(allies.Contains(active)){turnResources??=Resources(main,active);if(turnDate.Length==0)turnDate=Date(main);}
            lastActive=active;pendingKey=committedKey=null;committed=null;seen.Clear();
        }
        if(!allies.Contains(active))return;
        var now=Read(main,allies,screen==0x63d528);
        string key=JsonSerializer.Serialize(now);
        // A hero on the move is read between two steps; only a state that held for two reads in a
        // row is taken, so the log carries where he stopped, not every cell he passed.
        if(key!=pendingKey){pendingKey=key;return;}
        if(committed is null){committed=now;committedKey=key;Look(now,allies,report:false,active);return;}
        if(key==committedKey)return;
        foreach(string line in Diff(committed,now))Append(active,now.Date,"event",line);
        Look(now,allies,report:true,active);
        committed=now;committedKey=key;
    }

    private void TurnEnded(uint main,int colour,List<int> allies,uint screen)
    {
        if(colour==player){Append(colour,Date(main),"own_end","— твой ход закончен");return;}
        if(!allies.Contains(colour))return;
        int[] date=turnDate.Length==3?turnDate:Date(main);
        // The last step of the turn may not have held for two reads before the turn passed; the
        // state at hand-over is final, so it is taken as it is.
        if(committed is not null)
        {
            var last=Read(main,allies,false);
            foreach(string line in Diff(committed,last))Append(colour,date,"event",line);
            Look(last,allies,report:true,colour);
        }
        if(turnResources is not null)
        {
            int[] after=Resources(main,colour);
            var change=Enumerable.Range(0,7).Where(i=>after[i]!=turnResources[i])
                .Select(i=>$"{ResourceNames[i]} {after[i]-turnResources[i]:+#;-#}").ToList();
            if(change.Count>0)Append(colour,date,"event",$"ресурсы за ход: {string.Join(", ",change)}");
        }
        Append(colour,date,"turn",$"— ход {Of[colour]} окончен");
        turnResources=null;turnDate=[];
    }

    private List<int> Allies(uint main)
    {
        byte[] header=game.Read(main+0x1f86c+0xc,0x14);
        var allies=new List<int>();
        if(header[0] is 0 or >8||header[1+player]>7)return allies;
        for(int colour=0;colour<8;colour++)
        {
            if(colour==player||header[1+colour]!=header[1+player])continue;
            byte[] record=game.Read(main+0x20ad0+(uint)colour*0x168,0x40);
            if(record[1]==0&&record[0x3e]==0)continue;
            allies.Add(colour);
        }
        return allies;
    }

    private int[] Date(uint main)
    {
        byte[] bytes=game.Read(main+0x1f63e,6);
        return Enumerable.Range(0,3).Select(i=>(int)BitConverter.ToUInt16(bytes,i*2)).ToArray();
    }

    private int[] Resources(uint main,int colour)
    {
        byte[] bytes=game.Read(main+0x20ad0+(uint)colour*0x168+0x9c,28);
        return Enumerable.Range(0,7).Select(i=>BitConverter.ToInt32(bytes,i*4)).ToArray();
    }

    private State Read(uint main,List<int> allies,bool combat)
    {
        var heroes=new List<HeroState>();
        byte[] code=game.Read(0x4317e1,19);
        if(!code.AsSpan(0,13).SequenceEqual(Convert.FromHexString("8BC2C1E00603C28D04C08D8441")))
            throw new InvalidOperationException("Hero adapter signature mismatch");
        uint start=checked(main+BitConverter.ToUInt32(code,13));
        for(int id=0;id<256;id++)
        {
            byte[] h;
            try{h=game.Read(checked(start+(uint)id*0x492),0x492);}catch(InvalidOperationException){break;}
            if(BitConverter.ToInt32(h,0x1a)!=id||!allies.Contains(h[0x22]))continue;
            var army=Enumerable.Range(0,7)
                .Select(s=>(Type:BitConverter.ToInt32(h,0x91+s*4),Count:BitConverter.ToInt32(h,0xad+s*4)))
                .Where(s=>s.Type>=0&&s.Count>0).Select(s=>$"{GameReference.Creature(s.Type)} x{s.Count}").ToArray();
            var skills=Enumerable.Range(0,28).Where(i=>h[0xc9+i] is >0 and <4)
                .Select(i=>$"{GameReference.Skill(i)} ({SkillLevels[h[0xc9+i]]})").OrderBy(s=>s).ToArray();
            var artifacts=Enumerable.Range(0,19).Select(s=>BitConverter.ToInt32(h,0x12d+s*8))
                .Concat(Enumerable.Range(0,Math.Min((int)h[0x3d1],64)).Select(s=>BitConverter.ToInt32(h,0x1d4+s*8)))
                .Where(a=>a>=0).Select(a=>GameReference.Artifact(a)??$"артефакт №{a}").OrderBy(a=>a).ToArray();
            heroes.Add(new(id,Encoding.GetEncoding(1251).GetString(h,0x23,13).Split('\0')[0],h[0x22],
                BitConverter.ToInt16(h,0),BitConverter.ToInt16(h,2),BitConverter.ToInt16(h,4),
                BitConverter.ToInt16(h,0x55),BitConverter.ToInt32(h,0x51),
                h.Skip(0x476).Take(4).Select(v=>(int)v).ToArray(),army,skills,artifacts));
        }
        var towns=new List<TownState>();
        foreach(int colour in allies)
            foreach(var town in new TownReader(game,colour).Read())
                towns.Add(new(town.Id,town.Name??$"город №{town.Id}",colour,town.Built.Where(b=>b!=GameReference.Decoration).ToArray(),town.Garrison.ToArray()));
        return new(Date(main),combat,heroes,towns);
    }

    private static IEnumerable<string> Diff(State before,State after)
    {
        if(!before.Combat&&after.Combat)yield return "бой начался";
        if(before.Combat&&!after.Combat)yield return "бой окончен";
        foreach(var hero in after.Heroes)
        {
            var old=before.Heroes.FirstOrDefault(h=>h.Id==hero.Id);
            if(old is null){yield return $"новый герой {hero.Name} на ({hero.X},{hero.Y},{hero.Z}), армия: {Join(hero.Army)}";continue;}
            if(old.X!=hero.X||old.Y!=hero.Y||old.Z!=hero.Z)
                yield return $"{hero.Name}: ({old.X},{old.Y},{old.Z}) → ({hero.X},{hero.Y},{hero.Z})";
            if(hero.Level!=old.Level)yield return $"{hero.Name}: уровень {old.Level} → {hero.Level}";
            if(hero.Experience!=old.Experience&&hero.Level==old.Level)
                yield return $"{hero.Name}: опыт {hero.Experience-old.Experience:+#;-#} (стало {hero.Experience})";
            var primary=Enumerable.Range(0,4).Where(i=>hero.Primary[i]!=old.Primary[i])
                .Select(i=>$"{PrimaryNames[i]} {old.Primary[i]}→{hero.Primary[i]}").ToList();
            if(primary.Count>0)yield return $"{hero.Name}: {string.Join(", ",primary)}";
            foreach(string skill in hero.Skills.Except(old.Skills))yield return $"{hero.Name}: навык {skill}";
            if(!hero.Army.SequenceEqual(old.Army))yield return $"{hero.Name}: армия {Join(old.Army)} → {Join(hero.Army)}";
            foreach(string artifact in Removed(hero.Artifacts,old.Artifacts))yield return $"{hero.Name}: получил артефакт «{artifact}»";
            foreach(string artifact in Removed(old.Artifacts,hero.Artifacts))yield return $"{hero.Name}: больше нет артефакта «{artifact}»";
        }
        foreach(var old in before.Heroes.Where(o=>after.Heroes.All(h=>h.Id!=o.Id)))
            yield return $"герой {old.Name} исчез с карты (побеждён или уволен), была армия: {Join(old.Army)}";
        foreach(var town in after.Towns)
        {
            var old=before.Towns.FirstOrDefault(t=>t.Id==town.Id);
            if(old is null){yield return $"город {town.Name} теперь у союзника ({Colours[town.Owner]})";continue;}
            foreach(string building in town.Built.Except(old.Built))yield return $"{town.Name}: построено «{building}»";
            if(!town.Garrison.SequenceEqual(old.Garrison))yield return $"{town.Name}: гарнизон {Join(old.Garrison)} → {Join(town.Garrison)}";
        }
        foreach(var old in before.Towns.Where(o=>after.Towns.All(t=>t.Id!=o.Id)))
            yield return $"город {old.Name} потерян";
    }

    /// Items of the first list not matched one-for-one in the second: two equal artefacts count twice.
    private static IEnumerable<string> Removed(string[] now,string[] then)
    {
        var rest=then.ToList();
        foreach(string item in now)if(!rest.Remove(item))yield return item;
    }

    private static string Join(string[] list)=>list.Length==0?"пусто":$"[{string.Join(", ",list)}]";

    /// The cells around each ally hero, as the ally sees them: what vanished since the last look,
    /// and what a hero now stands on.
    private void Look(State state,List<int> allies,bool report,int active)
    {
        uint setup=game.U32(0x699538)+0x1fb70;
        int size=game.I32(setup+0xd4);
        uint tiles=game.U32(setup+0xd0),vision=game.U32(0x698a48);
        if(size is <36 or >252||tiles==0||vision==0)return;
        var standing=state.Heroes.Select(h=>(h.X,h.Y,h.Z)).ToHashSet();
        var current=new Dictionary<(int X,int Y,int Z),string>();
        var looked=new HashSet<(int X,int Y,int Z)>();
        foreach(var hero in state.Heroes)
            for(int y=hero.Y-3;y<=hero.Y+3;y++)
                for(int x=hero.X-3;x<=hero.X+3;x++)
                {
                    if(x<0||y<0||x>=size||y>=size||hero.Z is <0 or >1)continue;
                    uint index=checked((uint)((hero.Z*size+y)*size+x));
                    if((game.Read(vision+index*2,1)[0]&(1<<hero.Owner))==0)continue;
                    var cell=(x,y,(int)hero.Z);
                    if(!looked.Add(cell))continue;
                    uint tile=checked(tiles+index*0x26);
                    byte access=game.Read(tile+0xd,1)[0];
                    int type=BitConverter.ToInt16(game.Read(tile+0x1e,2));
                    if((access&16)==0)continue;
                    if(type==34){if(seen.TryGetValue(cell,out var under))current[cell]=under;continue;}
                    string name=type==98&&new TownReader(game,player).Describe(BitConverter.ToUInt16(game.Read(tile,2))) is {Name:{Length:>0} town}
                        ?$"город {town}":MapReader.ObjectName(type,BitConverter.ToInt16(game.Read(tile+0x22,2)));
                    current[cell]=name;
                }
        if(report)
        {
            foreach(var (cell,name) in seen)
                if(looked.Contains(cell)&&!current.ContainsKey(cell))
                    Append(active,state.Date,"event",$"с карты исчез «{name}» на ({cell.X},{cell.Y},{cell.Z}) — забран или побеждён");
            foreach(var hero in state.Heroes)
                if(committed?.Heroes.FirstOrDefault(h=>h.Id==hero.Id) is {} old&&(old.X!=hero.X||old.Y!=hero.Y||old.Z!=hero.Z)
                   &&seen.TryGetValue((hero.X,hero.Y,hero.Z),out var at))
                    Append(active,state.Date,"event",$"{hero.Name} на объекте «{at}» ({hero.X},{hero.Y},{hero.Z})");
        }
        foreach(var cell in looked)seen.Remove(cell);
        foreach(var (cell,name) in current)seen[cell]=name;
    }

    private void Append(int colour,int[] date,string kind,string text)
    {
        string line=JsonSerializer.Serialize(new{time=DateTimeOffset.Now,day=date,colour=Colours[colour],kind,text},Readable);
        Directory.CreateDirectory(root);
        File.AppendAllText(LogFile,line+"\n");
    }

    private sealed record Entry(string Colour,int[] Day,string Kind,string Text);

    private string? LastTurnMark()=>Entries().LastOrDefault(e=>e.Kind is "turn" or "own_end")?.Text;

    private List<Entry> Entries()
    {
        if(!File.Exists(LogFile))return [];
        var list=new List<Entry>();
        foreach(string line in File.ReadLines(LogFile))
        {
            using var json=JsonDocument.Parse(line);
            var r=json.RootElement;
            list.Add(new(r.GetProperty("colour").GetString()??"",r.GetProperty("day").EnumerateArray().Select(v=>v.GetInt32()).ToArray(),
                r.GetProperty("kind").GetString()??"",r.GetProperty("text").GetString()??""));
        }
        return list;
    }

    private static string Line(Entry e)=>e.Day.Length==3
        ?$"м{e.Day[2]} н{e.Day[1]} д{e.Day[0]}, {e.Colour}: {e.Text}":$"{e.Colour}: {e.Text}";

    /// What the allies did since this side's last turn ended, for the first lines of its brief.
    public List<string> SinceLastTurn(int max)
    {
        var all=Entries();
        int from=all.FindLastIndex(e=>e.Kind=="own_end");
        var lines=all.Skip(from+1).Where(e=>e.Kind!="own_end").Select(Line).ToList();
        if(lines.Count==0)return [];
        var result=new List<string>{"Союзник за свой ход (полностью — ally_log):"};
        // The end of his turn — where his heroes stopped and what it cost him — matters most, so a
        // long log keeps its start and its end and drops the middle.
        if(lines.Count<=max)result.AddRange(lines.Select(l=>"   "+l));
        else
        {
            int tail=max/2,head=max-tail;
            result.AddRange(lines.Take(head).Select(l=>"   "+l));
            result.Add($"   … ещё {lines.Count-max} строк — ally_log");
            result.AddRange(lines.Skip(lines.Count-tail).Select(l=>"   "+l));
        }
        return result;
    }

    public object Recent(int limit)
    {
        var all=Entries().Where(e=>e.Kind!="own_end").ToList();
        if(limit<=0)limit=60;
        return new{total=all.Count,entries=all.Skip(Math.Max(0,all.Count-limit)).Select(Line).ToArray(),
            note="Что союзник делал в свои ходы, как это видит союзный игрок: ходы героев, объекты, бои, армии, "
                +"уровни, навыки, артефакты, постройки и гарнизоны его городов, итог ресурсов за ход. Записывается, пока он ходит."};
    }

    /// A new game starts with an empty log; the old one is kept beside it with the date.
    public void Archive(string stamp)
    {
        if(File.Exists(LogFile))File.Move(LogFile,Path.Combine(root,$"ally-log-player{player}-{stamp}.jsonl"));
        lastActive=-1;committed=null;pendingKey=committedKey=null;seen.Clear();turnResources=null;
    }
}
