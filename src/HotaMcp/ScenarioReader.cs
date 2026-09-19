namespace HotaMcp;

public record ScenarioMap(string Name,string Description,int Size);
public record SetupChoice(string Action,string Label,bool Selected,bool Enabled);
public record SetupField(string Key,int Value,List<SetupChoice> Choices);
public record ScenarioSetup(string Panel,ScenarioMap? Map,List<SetupField> Fields);

internal sealed class ScenarioReader(WindowsGame game)
{
    internal record Control(int Id,string Field,int Value,string Label);
    internal static readonly Control[] Controls=[
        new(281,"size",36,"S — 36×36"),new(282,"size",72,"M — 72×72"),
        new(283,"size",108,"L — 108×108"),new(284,"size",144,"XL — 144×144"),
        new(3003,"size",180,"H — 180×180"),new(3004,"size",216,"XH — 216×216"),new(3005,"size",252,"G — 252×252"),
        ..Enumerable.Range(1,8).Select(n=>new Control(286+n,"players",n,n.ToString())),new(295,"players",-1,"Случайно"),
        ..Enumerable.Range(0,8).Select(n=>new Control(307+n,"computer_only",n,n.ToString())),new(315,"computer_only",-1,"Случайно"),
        new(326,"water",0,"Без воды"),new(327,"water",1,"Норма"),new(328,"water",2,"Острова"),new(329,"water",-1,"Случайно"),
        new(331,"monsters",0,"Слабые"),new(332,"monsters",1,"Норма"),new(333,"monsters",2,"Сильные"),new(334,"monsters",-1,"Случайно")
    ];
    internal static string Key(Control c)=>$"setup:{c.Field}:{c.Value}";
    public ScenarioSetup Read(uint dialog,List<UiElement> items)
    {
        byte[] flags=game.Read(dialog+0x37c,3);
        if(flags.Any(b=>b>1)||flags.Count(b=>b==1)>1)throw new InvalidOperationException("Scenario panel is changing");
        string panel=flags[2]==1?"random":flags[1]==1?"maps":"players";
        ScenarioMap? map=null;
        var fields=new List<SetupField>();
        if(panel=="random")
        {
            foreach(var (key,offset) in new[]{("size",0x18a0u),("players",0x18a8u),("computer_only",0x18b0u),("water",0x18b8u),("monsters",0x18bcu)})
            {
                int value=game.I32(dialog+offset);
                var controls=Controls.Where(c=>c.Field==key).ToArray();
                if(!controls.Any(c=>c.Value==value))throw new InvalidOperationException("Scenario setting layout unsupported");
                fields.Add(new(key,value,controls.Where(c=>items.Any(i=>i.Id==c.Id)).Select(c=>new SetupChoice(Key(c),c.Label,c.Value==value,items.Single(i=>i.Id==c.Id).Interactive)).ToList()));
            }
        }
        else if(panel=="maps")
        {
            uint first=game.U32(dialog+0x1054),last=game.U32(dialog+0x1058),cap=game.U32(dialog+0x105c);
            int index=game.I32(dialog+0x374);
            if(first>last||last>cap||(last-first)%0xca4!=0||index<0||(uint)index>=(last-first)/0xca4)
                throw new InvalidOperationException("Scenario list layout unsupported");
            uint selected=checked(first+(uint)index*0xca4);
            int size=game.I32(selected+0x18);
            if(size<36||size>252||size%36!=0)throw new InvalidOperationException("Scenario map size unsupported");
            map=new(game.Text(game.U32(selected+0x2d4))??"",game.Text(game.U32(selected+0x2e4),8192)??"",size);
        }
        return new(panel,map,fields);
    }
}
