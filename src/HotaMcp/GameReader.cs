using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HotaMcp;

public record UiElement(string Key,int Id,string? Text,string? Asset,int X,int Y,int Width,int Height,bool Interactive);
public record HeroView(int Id,string Name,int[] Position,int Mana,int Movement,int MaxMovement,int[] Primary,int[] ArmyTypes,int[] ArmyCounts);
public record Observation(string Revision,int Player,int[] Date,int[] Resources,HeroView? Hero,string Screen,int Width,int Height,List<UiElement> Elements);

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
    public object DiagnosticPointers() => new {
        current=game.I32(0x69ccf4), other=game.I32(0x6995a4), active=game.U32(0x69ccfc),
        main=game.U32(0x699538), mode=game.I32(0x698a40)
    };
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
        string screen=vtable switch {0x63a5e4=>"adventure",0x642478=>"system_options",_=>"unsupported"};
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
            bool interactive=vt==0x63bb54&&(state&2)!=0;
            if(string.IsNullOrEmpty(text)&&asset==null) continue;
            items.Add(new($"ui:{(pos-start)/4}",BitConverter.ToUInt16(b,0x10),text,asset,
                dx+BitConverter.ToInt16(b,0x18),dy+BitConverter.ToInt16(b,0x1a),iw,ih,interactive));
        }
        var result=new Observation("",player,date,resources,hero,screen,width,height,items);
        string revision=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(epoch+JsonSerializer.Serialize(result))))[..24];
        return result with {Revision=revision};
    }
}
