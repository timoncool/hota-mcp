namespace HotaMcp;

public record CombatStack(string Id,int Side,int Slot,int Type,string Name,int Count,int Hex,int Speed,int Attack,int Defence,
    int[] Hexes,bool Wide,bool Flying,bool Shooter)
{
    /// Every free hex touching the stack. A two-hex creature is struck from around either of its
    /// hexes, so the head hex alone misses half of the places a blow can come from.
    public IEnumerable<int> Around()=>Hexes.SelectMany(Deliveries.HexNeighbours).Distinct().Where(h=>!Hexes.Contains(h));

    // What the creature card shows on a right click.
    public int HealthEach{get;init;}
    public int TopHealth{get;init;}
    public int DamageMin{get;init;}
    public int DamageMax{get;init;}
    public int? Shots{get;init;}
    public int Retaliations{get;init;}
    public int Morale{get;init;}
    public int Luck{get;init;}
    public int StartCount{get;init;}
    public int AiValue{get;init;}
    public bool Waited{get;init;}
    public bool Acted{get;init;}
    public bool Defending{get;init;}
    public bool WarMachine{get;init;}
    public bool Summoned{get;init;}
    public string[] Abilities{get;init;}=[];
    public string[] Effects{get;init;}=[];
}

/// A part of a town wall during a siege, as the catapult sees it: standing while it has hit points.
public record WallPart(string Name,int HitPoints);

/// The battlefield itself: hexes a stack cannot enter or that hurt, and the wall in a siege. Mines
/// and quicksand laid by the enemy are invisible to the player, so only the own side's are listed.
public record CombatField(int[] Obstacles,int[] Quicksand,int[] LandMines,int[] FireWalls,int[] ForceFields,bool Siege,bool Moat,WallPart[] Walls);

/// Creatures lost since the fight began, one line per stack, priced by the game's own AI Value.
public record CombatLoss(string Id,int Side,string Name,int Start,int Lost,int ValueLost);

public record CombatView(int Round,int OwnSide,string? ActiveStack,bool OwnTurn,List<CombatStack> Stacks,int[] ReachableHexes,string[] AttackableTargets, int LogCount, CombatLogEntry[] Log)
{
    public CombatField? Field{get;init;}
    public CombatLoss[] Losses{get;init;}=[];
    /// Count changes since the observation of the previous own move, so what the enemy did while
    /// the agent waited is read at once instead of being pieced together from the log.
    public string[] SinceLastMove{get;init;}=[];
}

public record CombatLogEntry(int Index,string Text);

internal sealed class CombatReader(WindowsGame game,int player)
{
    /// Whether this side is one of the two in the battle the game holds — still true while the
    /// result window of that battle is open.
    public bool Participant()
    {
        uint manager=game.U32(0x699420);
        if(manager==0||game.U32(manager)!=0x63d3e8)return false;
        return game.I32(manager+0x54a8)==player||game.I32(manager+0x54ac)==player;
    }

    public CombatView Read()
    {
        uint manager=game.U32(0x699420);
        if(game.U32(manager)!=0x63d3e8)throw new InvalidOperationException("Combat manager layout unsupported");
        int[] owners=[game.I32(manager+0x54a8),game.I32(manager+0x54ac)];
        int own=Array.IndexOf(owners,player);
        if(own<0)throw new InvalidOperationException("Player is not a combat participant");
        int side=game.I32(manager+0x132b8),slot=game.I32(manager+0x132bc),activeSide=game.I32(manager+0x132c0);
        // Each battlefield hex records which stack stands on it (side and slot, 0xff when empty).
        // That is how the game itself knows the second hex of a two-hex creature, so the bridge
        // reads it rather than guessing which way the creature faces.
        byte[] field=game.Read(manager+0x1c4,187*0x70);
        var stacks=new List<CombatStack>();
        var losses=new List<CombatLoss>();
        for(int s=0;s<2;s++)for(int i=0;i<21;i++)
        {
            uint a=manager+0x54cc+(uint)(s*21+i)*0x548;
            int count=game.I32(a+0x4c);
            uint flags=game.U32(a+0x84);
            int start=game.I32(a+0x60);
            // Summoned and cloned stacks were never part of the army, so they are not its losses.
            if(start>0&&count>=0&&count<start&&(flags&0xc00000)==0&&game.I32(a+0xf4)==s&&game.I32(a+0xf8)==i
               &&game.I32(a+0x34) is >=0 and <=1023)
                losses.Add(new($"stack:{s}:{i}",s,game.Text(game.U32(a+0x8c))??GameReference.Creature(game.I32(a+0x34)),
                    start,start-count,(start-count)*game.I32(a+0xb4)));
            if(count<=0)continue;
            int type=game.I32(a+0x34),hex=game.I32(a+0x38);
            // During the tactics phase the stacks are not yet placed the way they are in the fight
            // itself, and a record that does not line up is not a broken adapter — it is a screen
            // where the fight has not started. Dropping the record keeps the rest readable instead
            // of blocking every observation until the battle is over.
            if(type<0||type>1023||hex<0||hex>186||game.I32(a+0xf4)!=s||game.I32(a+0xf8)!=i)continue;
            if((game.U32(a+8)&4)==0||(game.U32(a+8)&8)!=0)continue;
            string name=game.Text(game.U32(a+0x8c))??throw new InvalidOperationException("Combat creature name unavailable");
            int[] hexes=Enumerable.Range(0,187).Where(h=>field[h*0x70+0x18]==s&&field[h*0x70+0x19]==i).ToArray();
            if(!hexes.Contains(hex))throw new InvalidOperationException("Combat hex occupancy layout unsupported");
            int each=game.I32(a+0xc0);
            byte[] durations=game.Read(a+0x198,81*4);
            var effects=Enumerable.Range(0,81).Select(k=>(Spell:k,Rounds:BitConverter.ToInt32(durations,k*4))).Where(e=>e.Rounds>0)
                .Select(e=>$"{GameReference.Spell(e.Spell)??$"заклинание №{e.Spell}"} ({e.Rounds} р.)").ToArray();
            stacks.Add(new($"stack:{s}:{i}",s,i,type,name,count,hex,game.I32(a+0xc4),game.I32(a+0xc8),game.I32(a+0xcc),
                hexes,(flags&1)!=0,(flags&2)!=0,(flags&4)!=0)
            {
                HealthEach=each,TopHealth=each-game.I32(a+0x58),
                DamageMin=game.I32(a+0xd0),DamageMax=game.I32(a+0xd4),
                Shots=(flags&4)!=0?game.I32(a+0xd8):null,
                Retaliations=game.I32(a+0x454),Morale=game.I32(a+0x4e8),Luck=game.I32(a+0x4ec),
                StartCount=start,AiValue=game.I32(a+0xb4),
                Waited=(flags&0x2000000)!=0,Acted=(flags&0x4000000)!=0,Defending=(flags&0x8000000)!=0,
                WarMachine=(flags&0x40)!=0,Summoned=(flags&0xc00000)!=0,
                Abilities=Traits(flags),Effects=effects,
            });
        }
        string? active=stacks.Any(s=>s.Side==side&&s.Slot==slot)?$"stack:{side}:{slot}":null;
        bool ownTurn=activeSide==own&&active is not null;
        byte[] access=ownTurn?game.Read(manager+0x107,187):new byte[187];
        if(access.Any(b=>b>3))throw new InvalidOperationException("Combat accessibility layout unsupported");
        uint dlg=game.U32(game.U32(0x6992d0)+0x54);
        if(game.U32(dlg)!=0x63d528)throw new InvalidOperationException("Combat dialog changed");
        uint first=game.U32(dlg+0x58),end=game.U32(dlg+0x5c),capacity=game.U32(dlg+0x60);
        if(end<first||capacity<end||(end-first)%4!=0||(capacity-first)>4000000)throw new InvalidOperationException("Combat log layout unsupported");
        int countLog=(int)((end-first)/4);
        var log=new List<CombatLogEntry>();
        for(int i=Math.Max(0,countLog-24);i<countLog;i++)
        {
            uint str=game.U32(first+(uint)i*4);
            log.Add(new(i,game.Text(game.U32(str+4))??""));
        }
        if(game.U32(dlg+0x58)!=first||game.U32(dlg+0x5c)!=end)throw new InvalidOperationException("Combat log changed during read");
        return new(game.I32(manager+0x13d6c),own,active,ownTurn,stacks,
            Enumerable.Range(0,187).Where(h=>(access[h]&2)!=0&&!stacks.Any(s=>s.Hexes.Contains(h))).ToArray(),
            // Melee reach is what the accessibility plane marks, and during a siege the wall makes
            // every defender unreachable by that measure — which left a shooter with no targets at
            // all. A shot does not care about reach, so every enemy stack is offered while it is
            // our turn and the game decides what is legal, the same way it does for a spell.
            ownTurn?stacks.Where(s=>s.Side!=own).Select(s=>s.Id).ToArray():[],countLog,log.ToArray())
        {Field=ReadField(manager,field,own),Losses=losses.ToArray()};
    }

    private static string[] Traits(uint flags)
    {
        var traits=new List<string>();
        if((flags&0x10000)!=0)traits.Add("бьёт без ответного удара");
        if((flags&0x8000)!=0)traits.Add("бьёт дважды");
        if((flags&0x1000)!=0)traits.Add("стреляет без штрафа в ближнем бою");
        if((flags&0x8)!=0)traits.Add("дыхание: бьёт и клетку за целью");
        if((flags&0x80000)!=0)traits.Add("бьёт всех вокруг");
        if((flags&0x100000)!=0)traits.Add("стреляет огненным шаром по площади");
        if((flags&0x10)==0)traits.Add("неживой");
        if((flags&0x200000)!=0)traits.Add("не двигается");
        return traits.ToArray();
    }

    // Names of the wall parts by their place in the game's table of fortification pieces.
    private static readonly (int Index,string Name)[] WallPieces=
        [(5,"верхняя башня"),(6,"верхняя стена"),(8,"средняя верхняя стена"),(9,"ворота"),(10,"средняя нижняя стена"),
         (12,"нижняя стена"),(13,"нижняя башня"),(14,"главная башня")];

    private CombatField ReadField(uint manager,byte[] field,int own)
    {
        // Obstacles a spell or a creature laid carry the side that owns them; the enemy's mines
        // and quicksand stay hidden from the player, so the owner decides what is published.
        uint first=game.U32(manager+0x13d5c),end=game.U32(manager+0x13d60);
        if(end<first||(end-first)%0x18!=0||end-first>0x18*512)throw new InvalidOperationException("Combat obstacle list layout unsupported");
        int obstacleCount=(int)((end-first)/0x18);
        byte[] obstacles=obstacleCount>0?game.Read(first,obstacleCount*0x18):[];
        int Owner(int hex)
        {
            int index=BitConverter.ToInt32(field,hex*0x70+0x14);
            return index>=0&&index<obstacleCount?(sbyte)obstacles[index*0x18+9]:-1;
        }
        var blocked=new List<int>();var sand=new List<int>();var mines=new List<int>();var fire=new List<int>();var force=new List<int>();
        for(int h=0;h<187;h++)
        {
            byte bits=field[h*0x70+0x10];
            if((bits&0x20)!=0)force.Add(h);
            else if((bits&0x2)!=0)blocked.Add(h);
            if((bits&0x4)!=0&&Owner(h)==own)sand.Add(h);
            if((bits&0x8)!=0&&Owner(h)==own)mines.Add(h);
            if((bits&0x10)!=0)fire.Add(h);
        }
        bool siege=game.U32(manager+0x53c8)!=0&&game.I32(manager+0x53a4)>0;
        var walls=siege
            ?WallPieces.Select(p=>new WallPart(p.Name,game.I32(manager+0x13f60+(uint)p.Index*4))).ToArray()
            :[];
        return new(blocked.ToArray(),sand.ToArray(),mines.ToArray(),fire.ToArray(),force.ToArray(),siege,siege&&game.I32(manager+0x53a8)!=0,walls);
    }
    /// The middle of a battlefield hex. Each hex record keeps first the point where a creature
    /// stands — the bottom tip of the hex — and then the hex's own box: left, top, right, bottom.
    /// A click on the tip sits on the border with the hexes below it, so aiming uses the middle.
    public (int X,int Y) Center(int hex)
    {
        if(hex<0||hex>=187)throw new InvalidOperationException("Invalid combat hex");
        uint square=game.U32(0x699420)+0x1c4+(uint)hex*0x70;
        byte[] b=game.Read(square,12);
        int left=BitConverter.ToInt16(b,4),top=BitConverter.ToInt16(b,6),right=BitConverter.ToInt16(b,8),bottom=BitConverter.ToInt16(b,10);
        return ((left+right)/2,(top+bottom)/2);
    }

    public (int X,int Y) Point(int hex)
    {
        if(hex<0||hex>=187)throw new InvalidOperationException("Invalid combat hex");
        uint square=game.U32(0x699420)+0x1c4+(uint)hex*0x70;
        return (BitConverter.ToInt16(game.Read(square,2)),BitConverter.ToInt16(game.Read(square+2,2)));
    }
}
