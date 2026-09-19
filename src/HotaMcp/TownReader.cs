namespace HotaMcp;

public sealed record TownView(int Id,string? Name,int Type,bool BuiltToday,int[] Buildings);
public sealed record AvailableAction(string Key,string Label);

internal sealed class TownReader(WindowsGame game,int player)
{
    public List<TownView> Read()
    {
        uint main=game.U32(0x699538),owner=main+0x20ad0+(uint)player*0x168;
        if(game.I32(0x69ccf4)!=player||game.U32(0x69ccfc)!=owner)throw new InvalidOperationException("Wrong player context");
        var own=game.Read(owner+0x3e,50);int count=own[0];
        if(count>48)throw new InvalidOperationException("Unsupported town list");
        var code=game.Read(0x4081bd,6);
        if(code[0]!=0x8b||code[1]!=0x82)throw new InvalidOperationException("Town adapter signature mismatch");
        uint offset=BitConverter.ToUInt32(code,2);
        if(offset>0x1000000)throw new InvalidOperationException("Town vector layout invalid");
        uint start=game.U32(main+offset),end=game.U32(main+offset+4);
        if(end<start||(end-start)%0x168!=0||end-start>0x168*256)throw new InvalidOperationException("Town vector bounds invalid");
        var result=new List<TownView>();
        for(int i=0;i<count;i++)
        {
            int id=own[i+2];
            if(id>=48||start+(uint)(id+1)*0x168>end)throw new InvalidOperationException("Town index invalid");
            uint address=start+(uint)id*0x168;
            // Check ownership before reading the rest of the town.
            var identity=game.Read(address,2);
            if(identity[0]!=id||identity[1]!=player)throw new InvalidOperationException("Town ownership changed");
            var town=game.Read(address,0x168);ulong mask=BitConverter.ToUInt64(town,0x150);
            result.Add(new(id,game.Text(BitConverter.ToUInt32(town,0xc8)),town[4],town[2]!=0,
                Enumerable.Range(0,44).Where(b=>(mask&(1UL<<b))!=0).ToArray()));
        }
        return result;
    }
}
