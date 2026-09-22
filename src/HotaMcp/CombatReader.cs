namespace HotaMcp;

public record CombatStack(string Id,int Side,int Slot,int Type,string Name,int Count,int Hex,int Speed,int Attack,int Defence,
    int[] Hexes,bool Wide,bool Flying,bool Shooter)
{
    /// Every free hex touching the stack. A two-hex creature is struck from around either of its
    /// hexes, so the head hex alone misses half of the places a blow can come from.
    public IEnumerable<int> Around()=>Hexes.SelectMany(Deliveries.HexNeighbours).Distinct().Where(h=>!Hexes.Contains(h));
}
public record CombatView(int Round,int OwnSide,string? ActiveStack,bool OwnTurn,List<CombatStack> Stacks,int[] ReachableHexes,string[] AttackableTargets, int LogCount, CombatLogEntry[] Log);

public record CombatLogEntry(int Index,string Text);

internal sealed class CombatReader(WindowsGame game,int player)
{
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
        for(int s=0;s<2;s++)for(int i=0;i<21;i++)
        {
            uint a=manager+0x54cc+(uint)(s*21+i)*0x548;
            int count=game.I32(a+0x4c);
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
            uint flags=game.U32(a+0x84);
            stacks.Add(new($"stack:{s}:{i}",s,i,type,name,count,hex,game.I32(a+0xc4),game.I32(a+0xc8),game.I32(a+0xcc),
                hexes,(flags&1)!=0,(flags&2)!=0,(flags&4)!=0));
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
            ownTurn?stacks.Where(s=>s.Side!=own).Select(s=>s.Id).ToArray():[],countLog,log.ToArray());
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
