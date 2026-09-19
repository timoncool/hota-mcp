namespace HotaMcp;

public record CombatStack(string Id,int Side,int Slot,int Type,string Name,int Count,int Hex,int Speed,int Attack,int Defence);
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
        var stacks=new List<CombatStack>();
        for(int s=0;s<2;s++)for(int i=0;i<21;i++)
        {
            uint a=manager+0x54cc+(uint)(s*21+i)*0x548;
            int count=game.I32(a+0x4c);
            if(count<=0)continue;
            int type=game.I32(a+0x34),hex=game.I32(a+0x38);
            if(type<0||type>1023||hex<0||hex>186||game.I32(a+0xf4)!=s||game.I32(a+0xf8)!=i)throw new InvalidOperationException("Combat stack layout unsupported");
            if((game.U32(a+8)&4)==0||(game.U32(a+8)&8)!=0)continue;
            string name=game.Text(game.U32(a+0x8c))??throw new InvalidOperationException("Combat creature name unavailable");
            stacks.Add(new($"stack:{s}:{i}",s,i,type,name,count,hex,game.I32(a+0xc4),game.I32(a+0xc8),game.I32(a+0xcc)));
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
            Enumerable.Range(0,187).Where(h=>(access[h]&2)!=0&&!stacks.Any(s=>s.Hex==h)).ToArray(),
            stacks.Where(s=>s.Side!=own&&(access[s.Hex]&1)!=0).Select(s=>s.Id).ToArray(),countLog,log.ToArray());
    }
    public (int X,int Y) Point(int hex)
    {
        if(hex<0||hex>=187)throw new InvalidOperationException("Invalid combat hex");
        uint square=game.U32(0x699420)+0x1c4+(uint)hex*0x70;
        return (BitConverter.ToInt16(game.Read(square,2)),BitConverter.ToInt16(game.Read(square+2,2)));
    }
}
