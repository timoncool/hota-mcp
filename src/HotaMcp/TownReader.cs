namespace HotaMcp;

public sealed record TownView(int Id,string? Name,int Type,bool BuiltToday,int[] Buildings)
{
    /// The garrison as a player reads it: names beside counts, empty slots omitted.
    public List<string> Garrison=>GarrisonTypes.Zip(GarrisonCounts)
        .Where(s=>s.First>=0&&s.Second>0)
        .Select(s=>$"{GameReference.Creature(s.First)} x{s.Second}").ToList();
    public int GarrisonHero {get;init;}
    public int VisitingHero {get;init;}
    public int[] GarrisonTypes {get;init;}=[];
    public int[] GarrisonCounts {get;init;}=[];
    public int[][] Recruitable {get;init;}=[];
}
public sealed record AvailableAction(string Key,string Label);

internal sealed class TownReader(WindowsGame game,int player)
{
    public (int X,int Y) BuildingPoint(int building)
    {
        if(building<0||building>=44)throw new InvalidOperationException("Invalid building");
        uint manager=game.U32(0x69954c),dlg=game.U32(manager+0x118),town=game.U32(manager+0x38);
        if(game.U32(manager)!=0x643730||game.U32(dlg)!=0x64373c||game.Read(town+1,1)[0]!=player)throw new InvalidOperationException("Own town screen required");
        ulong built=BitConverter.ToUInt64(game.Read(town+0x150,8));
        if((built&(1UL<<building))==0)throw new InvalidOperationException("Building is not built");
        uint mask=game.U32(dlg+0x50);
        byte[] data=game.Read(mask,800*374*2);
        int best=0,bx=-1,by=-1;
        for(int y=1;y<373;y++)
        {
            int start=-1;
            for(int x=0;x<=800;x++)
            {
                bool match=x<800&&BitConverter.ToUInt16(data,(y*800+x)*2)==building+1;
                if(match){if(start<0)start=x;}
                else if(start>=0){int length=x-start;if(length>best){best=length;bx=start+length/2;by=y;}start=-1;}
            }
        }
        if(best<3||game.U32(dlg+0x50)!=mask)throw new InvalidOperationException("Building hit region unavailable");
        return (bx,by);
    }
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
                Enumerable.Range(0,44).Where(b=>(mask&(1UL<<b))!=0).ToArray())
            {
                GarrisonHero=BitConverter.ToInt32(town,0xc),VisitingHero=BitConverter.ToInt32(town,0x10),
                GarrisonTypes=Enumerable.Range(0,7).Select(s=>BitConverter.ToInt32(town,0xe0+s*4)).ToArray(),
                GarrisonCounts=Enumerable.Range(0,7).Select(s=>BitConverter.ToInt32(town,0xfc+s*4)).ToArray(),
                Recruitable=Enumerable.Range(0,2).Select(v=>Enumerable.Range(0,7).Select(s=>(int)BitConverter.ToInt16(town,0x16+(v*7+s)*2)).ToArray()).ToArray()
            });
        }
        return result;
    }
}
