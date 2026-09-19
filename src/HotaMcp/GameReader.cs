using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HotaMcp;

public record UiElement(string Key,int Id,string? Text,string? Asset,int X,int Y,int Width,int Height,bool Interactive);
public record HeroView(int Id,string Name,int[] Position,int Mana,int Movement,int MaxMovement,int[] Primary,int[] ArmyTypes,int[] ArmyCounts);
public record Observation(string Revision,int Player,int[] Date,int[] Resources,HeroView? Hero,string Screen,int Width,int Height,List<UiElement> Elements)
{
    public List<TownView> Towns {get;init;}=[];
    public List<AvailableAction> Actions {get;init;}=[];
}

internal sealed class GameReader(WindowsGame game,int player)
{
    private readonly string epoch=Guid.NewGuid().ToString("N");
    public Observation Observe()
    {
        // Two matching reads reduce transitional snapshots; safe-point synchronization remains future work.
        var first=ReadOnce();
        var second=ReadOnce();
        if(first.Revision!=second.Revision) throw new InvalidOperationException("State changing; observe again");
        return second;
    }
    public object DiagnosticPointers() {
        uint executive=game.U32(0x699550),current=game.U32(executive);
        var managers=new List<object>();var seen=new HashSet<uint>();
        while(current!=0&&seen.Add(current)&&managers.Count<32){
            managers.Add(new{address=current,vtable=game.U32(current),type=game.I32(current+12),status=game.I32(current+0x34),name=game.Text(current+0x14,28)});
            current=game.U32(current+4);
        }
        var inputCandidates=new List<object>();
        for(uint address=0x699000;address<0x699800;address+=4){
            uint candidate=game.U32(address);
            if(candidate<0x10000)continue;
            try{if(game.U32(candidate)==0x63fe10)inputCandidates.Add(new{address,candidate,name=game.Text(candidate+0x14,28),head=game.I32(candidate+0x838),tail=game.I32(candidate+0x83c)});}
            catch(InvalidOperationException){}
        }
        uint activeDialog=game.U32(game.U32(0x6992d0)+0x54);
        uint uiFirst=game.U32(activeDialog+0x34),uiLast=game.U32(activeDialog+0x38);
        var rawUi=new List<object>();
        if(uiLast>=uiFirst&&uiLast-uiFirst<=8192)
            for(uint slot=uiFirst;slot<uiLast;slot+=4){
                uint item=game.U32(slot);
                rawUi.Add(new{address=item,vtable=game.U32(item),id=BitConverter.ToUInt16(game.Read(item+0x10,2)),state=BitConverter.ToUInt16(game.Read(item+0x16,2)),text=game.U32(item) is 0x642dc0 or 0x642df8?game.Text(game.U32(item+0x34)):null,closeButton=game.U32(item)==0x63bb54?game.Read(item+0x44,1)[0]:-1,asset=game.U32(item)==0x63bb54?game.Text(game.U32(item+0x30)+4,16):null});
            }
        return new {
        activeDialog,dialogVtable=game.U32(activeDialog),rawUi,townScroll=game.I32(activeDialog+0x68),ownedTowns=game.U32(0x69ccfc)>=0x10000?game.Read(game.U32(0x69ccfc)+0x3e,4).Select(b=>(int)b).ToArray():[],
        current=game.I32(0x69ccf4), other=game.I32(0x6995a4), active=game.U32(0x69ccfc),
        main=game.U32(0x699538), mode=game.I32(0x698a40),managers,inputCandidates
    };}
    private Observation ReadOnce()
    {
        if(player is <0 or >7) throw new InvalidOperationException("Player configuration invalid");
        if(game.I32(0x69ccf4)!=player) throw new InvalidOperationException("Not this player's active context");
        uint main=game.U32(0x699538),p=main+0x20ad0+(uint)player*0x168;
        if(game.U32(0x69ccfc)!=p) throw new InvalidOperationException("Active player layout not validated");
        byte[] person=game.Read(p,0x168);
        if(person[0]!=player) throw new InvalidOperationException("Player identity mismatch");
        int[] resources=Enumerable.Range(0,7).Select(i=>BitConverter.ToInt32(person,0x9c+i*4)).ToArray();
        byte[] dateBytes=game.Read(main+0x1f63e,6);
        int[] date=Enumerable.Range(0,3).Select(i=>(int)BitConverter.ToUInt16(dateBytes,i*2)).ToArray();
        int heroId=BitConverter.ToInt32(person,4); HeroView? hero=null;
        if(heroId>=0)
        {
            if(heroId>1023) throw new InvalidOperationException("Hero index outside supported bounds");
            byte[] code=game.Read(0x4317e1,19);
            if(!code.AsSpan(0,13).SequenceEqual(Convert.FromHexString("8BC2C1E00603C28D04C08D8441")) || code[17]!=0x5d || code[18]!=0xc2)
                throw new InvalidOperationException("Hero adapter signature mismatch");
            uint address=checked(main+BitConverter.ToUInt32(code,13)+(uint)heroId*0x492);
            byte[] h=game.Read(address,0x492);
            if(BitConverter.ToInt32(h,0x1a)!=heroId || h[0x22]!=player) throw new InvalidOperationException("Hero ownership mismatch");
            string name=Encoding.GetEncoding(1251).GetString(h,0x23,13).Split('\0')[0];
            hero=new(heroId,name,Enumerable.Range(0,3).Select(i=>(int)BitConverter.ToInt16(h,i*2)).ToArray(),
                BitConverter.ToInt16(h,0x18),BitConverter.ToInt32(h,0x4d),BitConverter.ToInt32(h,0x49),
                h.Skip(0x476).Take(4).Select(v=>(int)v).ToArray(),
                Enumerable.Range(0,7).Select(i=>BitConverter.ToInt32(h,0x91+i*4)).ToArray(),
                Enumerable.Range(0,7).Select(i=>BitConverter.ToInt32(h,0xad+i*4)).ToArray());
        }
        uint manager=game.U32(0x6992d0), dlg=game.U32(manager+0x54);
        uint vtable=game.U32(dlg);
        string screen=vtable switch {0x63a5e4=>"adventure",0x642478=>"system_options",0x64373c=>"town",0x6437b0=>"town_hall",0x643954=>"building_confirmation",_=>"unsupported"};
        // Unvalidated dialog classes are not published to the player yet.
        if(screen=="unsupported") throw new InvalidOperationException("Current screen not supported by this adapter yet");
        uint surface=game.U32(manager+0x40);
        int width=game.I32(surface+0x24),height=game.I32(surface+0x28);
        if(width<640||width>8192||height<480||height>8192) throw new InvalidOperationException("Invalid surface geometry");
        byte[] d=game.Read(dlg,0x4c);
        int dx=BitConverter.ToInt32(d,0x18),dy=BitConverter.ToInt32(d,0x1c);
        uint start=BitConverter.ToUInt32(d,0x34),end=BitConverter.ToUInt32(d,0x38),cap=BitConverter.ToUInt32(d,0x3c);
        if(start>end||end>cap||(end-start)%4!=0||end-start>8192) throw new InvalidOperationException("Invalid UI list");
        var items=new List<UiElement>();
        for(uint pos=start;pos<end;pos+=4)
        {
            uint a=game.U32(pos);byte[] b=game.Read(a,0x30);
            if(BitConverter.ToUInt32(b,4)!=dlg) throw new InvalidOperationException("UI changed while reading");
            ushort state=BitConverter.ToUInt16(b,0x16);
            if((state&4)==0) continue;
            int iw=BitConverter.ToUInt16(b,0x1c),ih=BitConverter.ToUInt16(b,0x1e);
            if(iw==0||ih==0) continue;
            uint vt=BitConverter.ToUInt32(b);string? text=null,asset=null;
            if(vt is 0x642dc0 or 0x642df8) text=game.Text(game.U32(a+0x34));
            if(vt==0x63bb54) asset=game.Text(game.U32(a+0x30)+4,16);
            bool interactive=vt==0x63bb54&&(state&2)!=0&&(state&0x28)==0;
            if(string.IsNullOrEmpty(text)&&asset==null) continue;
            items.Add(new($"ui:{(pos-start)/4}",BitConverter.ToUInt16(b,0x10),text,asset,
                dx+BitConverter.ToInt16(b,0x18),dy+BitConverter.ToInt16(b,0x1a),iw,ih,interactive));
        }
        var towns=new TownReader(game,player).Read();
        var actions=new List<AvailableAction>();
        if(screen=="adventure")foreach(var town in towns)actions.Add(new($"town:open:{town.Id}",$"Открыть город: {town.Name}"));
        if(screen=="town")
        {
            actions.Add(new("town:construction","Открыть зал совета"));
            actions.Add(new("town:close","Вернуться на карту"));
        }
        if(screen=="town_hall")
        {
            foreach(var item in items.Where(i=>i.Id>=600&&i.Id<618))actions.Add(new($"building:inspect:{item.Id-600}",item.Text??"Описание здания"));
            actions.Add(new("construction:close","Вернуться в город"));
        }
        if(screen=="building_confirmation")
        {
            if(items.Any(i=>i.Id==30722&&i.Interactive))actions.Add(new("building:buy","Построить указанное здание за показанную цену"));
            if(items.Any(i=>i.Id==30721&&i.Interactive))actions.Add(new("building:cancel","Отменить покупку"));
        }
        var result=new Observation("",player,date,resources,hero,screen,width,height,items){Towns=towns,Actions=actions};
        string revision=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(epoch+JsonSerializer.Serialize(result))))[..24];
        return result with {Revision=revision};
    }
}
