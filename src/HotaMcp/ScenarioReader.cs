namespace HotaMcp;

public record ScenarioMap(string Name,string Description,int Size,int Players,int Humans,string Victory,string Loss);
public record SetupChoice(string Action,string Label,bool Selected,bool Enabled);
public record SetupField(string Key,int Value,List<SetupChoice> Choices);
public record ScenarioSetup(string Panel,ScenarioMap? Map,List<SetupField> Fields)
{
    /// The flags the right panel draws under «Союзники» and «Враги» for the chosen map, seen from
    /// the first human colour: who plays together and who against.
    public List<string> Allies {get;init;}=[];
    public List<string> Enemies {get;init;}=[];
}

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
    private static string ColourName(int c)=>c switch{0=>"красный",1=>"синий",2=>"коричневый",3=>"зелёный",4=>"оранжевый",5=>"фиолетовый",6=>"бирюзовый",7=>"розовый",_=>"?"};
    public ScenarioSetup Read(uint dialog,List<UiElement> items)
    {
        // The panel on display is told by what the player can press on it: the colour flags of the
        // players panel, the size buttons of the generator, the size filters of the map list. The
        // panel bytes of the dialog were read for this once and turned out wrong in a hotseat game.
        bool Shown(int from,int to)=>items.Any(i=>i.Id>=from&&i.Id<=to&&i.Interactive);
        string panel=Shown(263,270)?"players":Shown(281,284)?"random":Shown(137,141)?"maps"
            :throw new InvalidOperationException("Scenario panel is changing");
        ScenarioMap? map=null;
        var fields=new List<SetupField>();
        var available=new List<SetupChoice>();
        if(panel=="random")
        {
            foreach(var (key,offset) in new[]{("size",0x18a0u),("players",0x18a8u),("computer_only",0x18b0u),("water",0x18b8u),("monsters",0x18bcu)})
            {
                int value=game.I32(dialog+offset);
                var controls=Controls.Where(c=>c.Field==key).ToArray();
                if(!controls.Any(c=>c.Value==value))throw new InvalidOperationException("Scenario setting layout unsupported");
                fields.Add(new(key,value,controls.Where(c=>items.Any(i=>i.Id==c.Id)).Select(c=>new SetupChoice(Key(c),c.Label,c.Value==value,items.Single(i=>i.Id==c.Id).Interactive)).ToList()));
            }
            // Switches of the generator: the underground button stays pressed while the map gets a
            // second level, and each road type is a tick drawn as frame 1.
            if(items.FirstOrDefault(i=>i.Id==285) is {} under)
                fields.Add(new("underground",under.Selected?1:0,[new("rmg:underground",under.Selected?"подземный уровень: есть (нажать — убрать)":"подземный уровень: нет (нажать — добавить)",under.Selected,under.Interactive)]));
            foreach(var (id,road,name) in new[]{(7007,"dirt","грунтовые"),(7008,"gravel","гравийные"),(7009,"cobble","мощёные")})
                if(items.FirstOrDefault(i=>i.Id==id) is {} tick)
                    fields.Add(new($"road_{road}",tick.Frame,[new($"rmg:road:{road}",$"{name} дороги: {(tick.Frame==1?"есть":"нет")} (нажать — переключить)",tick.Frame==1,tick.Interactive)]));
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
            map=new(game.Text(game.U32(selected+0x2d4))??"",game.Text(game.U32(selected+0x2e4),8192)??"",size,
                game.Read(selected+6,1)[0],game.Read(selected+8,1)[0],
                GameReference.Victory(game.Read(selected+0x30,1)[0]),GameReference.Loss(game.Read(selected+0x7c,1)[0]));
            // Every scenario the filter currently admits, in the order the list shows them. The
            // player scrolls this list with his eyes; without it the agent knows only the one row
            // that happens to be selected and cannot choose a map at all.
            int count=(int)((last-first)/0xca4);
            for(int row=0;row<count&&row<1000;row++)
            {
                uint entry=checked(first+(uint)row*0xca4);
                string title=game.Text(game.U32(entry+0x2d4))??"";
                if(title.Length==0)continue;
                int side=game.I32(entry+0x18);
                // The list shows, beside every name, how many sides play and how many of them may
                // be people, and pictures of the victory and loss conditions; the header of each
                // entry carries the same: players at +6, human-playable at +8, the victory type at
                // +0x30 and the loss type at +0x7c. Checked against the list for three maps.
                int players=game.Read(entry+6,1)[0],humans=game.Read(entry+8,1)[0];
                // The entry starts with the map header: teams at +0xC and one team number per
                // colour at +0xD, the underground level at +0x1C.
                byte[] header=game.Read(entry+0xc,0x14);
                string teams=header[0]==1?"; команды: "+string.Join(" / ",Enumerable.Range(0,8).Where(c=>header[1+c]<8)
                    .GroupBy(c=>header[1+c]).OrderBy(g=>g.Key).Select(g=>string.Join("+",g.Select(ColourName)))):"";
                string under=header[0x10]==1?", с подземельем":"";
                available.Add(new($"scenario:map:{title}",
                    $"{title} — {side}×{side}{under}, игроков {players} (людьми {humans}){teams}, "
                    +$"победа: {GameReference.Victory(game.Read(entry+0x30,1)[0])}, "
                    +$"поражение: {GameReference.Loss(game.Read(entry+0x7c,1)[0])}",row==index,true));
            }
        }
        if(available.Count>0)fields.Add(new("map",0,available));
        // Flags 112-119 stand under «Союзники», 120-127 under «Враги»; each flag's frame is its colour.
        List<string> Flags(int from)=>items.Where(i=>i.Id>=from&&i.Id<from+8&&i.Frame is >=0 and <8)
            .OrderBy(i=>i.Id).Select(i=>ColourName(i.Frame)).ToList();
        return new(panel,map,fields){Allies=Flags(112),Enemies=Flags(120)};
    }
}
