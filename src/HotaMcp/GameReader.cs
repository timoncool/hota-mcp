using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HotaMcp;

public record UiElement(string Key,int Id,string? Text,string? Asset,int X,int Y,int Width,int Height,bool Interactive)
{
    /// What this control is, by the picture it draws. Most buttons in the game carry no caption,
    /// and a control id names nothing; the picture is their identity.
    public string? Means=>UiAssets.Name(Asset);

    /// Picture frame the control is drawing. The game uses it to colour a row: in the town hall
    /// 0 marks a building that already stands, 2 one that can be built now, 3 one that cannot.
    public int Frame {get;init;}

    /// The control is drawn as the chosen one of its row — a pressed difficulty piece, a size
    /// filter in force, an open panel tab. The game marks it with one bit of the control state.
    public bool Selected {get;init;}
}
public record HeroView(int Id,string Name,int[] Position,int Mana,int Movement,int MaxMovement,int[] Primary,int[] ArmyTypes,int[] ArmyCounts)
{
    /// The army as a player reads it: creature names beside their counts, empty slots omitted.
    /// A type number is what the game stores, not something a player can act on.
    public List<string> Army=>ArmyTypes.Zip(ArmyCounts)
        .Where(s=>s.First>=0&&s.Second>0)
        .Select(s=>$"{GameReference.Creature(s.First)} x{s.Second}").ToList();
    public int[] PlannedDestination {get;init;}=[];
}
public record ForeignHero(int Id,string Name,int Owner,int[] Position);
public record HeroSkill(string Name,string Mastery,string Element);
public record HeroSlot(string Slot,string Element);
public record HeroSheet(string Name,string Title,List<HeroSkill> Skills,List<HeroSlot> Equipped,string Specialty)
{
    /// Where to point inspect_element to read what the game explains on this screen. The help
    /// calls these out as clickable icons: morale, luck, experience to the next level, mana,
    /// the four primary skills and the seven army slots.
    public Dictionary<string,string> Inspect {get;init;}=new();
}

public record Observation(string Revision,int Player,int[] Date,int[] Resources,HeroView? Hero,string Screen,int Width,int Height,List<UiElement> Elements)
{
    /// Filled only on the hero screen: what the player reads there and cannot read anywhere else.
    public HeroSheet? Sheet {get;init;}
    public List<TownView> Towns {get;init;}=[];
    public List<AvailableAction> Actions {get;init;}=[];
    public ScenarioSetup? Setup {get;init;}
    public CombatView? Combat {get;init;}
    public SaveList? Saves {get;init;}
    /// Filled on the building purchase card: what the game shows there, including a price the
    /// player can read while the button is grey.
    public BuildOffer? Build {get;init;}
    /// Every hero this player owns, not only the selected one. A player sees them all on the
    /// sidebar at a glance; without this an agent has to cycle the selection to learn what it has.
    public List<HeroView> Heroes {get;init;}=[];
    /// The screen in words, the way a player takes it in at a glance: who is here, what is theirs,
    /// what is still pending today and which action key answers each of those. Read it first — it
    /// is written so the obvious follow-up questions do not have to be asked as separate calls.
    public List<string> Brief {get;init;}=[];

    /// Which army cell of the town screen is picked up right now, or null when none is. A picked
    /// stack makes the next press on another cell a transfer, so this says whether a gesture
    /// starts from a clean screen.
    public string? SelectedStack {get;init;}
    /// Which of your towns the town screen is showing. With more than one town the first of the
    /// list is not the one on screen, and every town action must follow the screen.
    public int OpenTown {get;init;}=-1;
    /// Whose card is on screen and what a player can tell about his army: the creature of each
    /// stack and the size band the game prints instead of a number.
    public string? ForeignHero {get;init;}
    public List<string> ForeignArmy {get;init;}=[];
    /// Heroes of other players standing on tiles this player can see. Not knowing that somebody is
    /// walking at your town is how a town is lost without a single warning.
    public List<ForeignHero> ForeignHeroes {get;init;}=[];
    /// Who this session plays and whether the game is currently waiting for that side. In a shared
    /// game — hotseat, or a human on another colour — acting for the wrong side is the one mistake
    /// that cannot be undone, so the answer is stated rather than assumed.
    public SideView? Side {get;init;}
}

/// The side this bridge is bound to, the side the game is currently asking for, and whether they
/// are the same. `Yours` is the only safe condition for an action.
public sealed record SideView(int Player,string Colour,int ActivePlayer,string ActiveColour,bool Yours)
{
    /// Colours on this side's team. In a game with teams an ally's heroes and towns are not a
    /// threat and not a target, and a player reads that off the flag at once.
    public int[] Allies {get;init;}=[];
    /// Everyone still in the game: colour, human or computer, team, and whose side each is on.
    public List<string> Participants {get;init;}=[];
    /// Whether the map has an underground level — the player sees it as the level switch by the
    /// minimap.
    public bool Underground {get;init;}
}

/// The building purchase card as a player reads it: what is offered, what it gives, whether the
/// game will take the order right now and, when it will not, why. A missing button is an answer,
/// not an absence — it is the difference between "cannot afford" and "already built today".
public sealed record BuildOffer(string? Title,string? Effect,string? Conditions,int[] Cost,bool CanBuy,string? Blocked)
{
    /// The price as the player reads it off the icons: the resource named, not a bare number.
    public List<string> Price {get;init;}=[];
}

internal sealed class GameReader(WindowsGame game,int player)
{
    /// Reads the purchase card. The price is on screen whether or not the game will accept the
    /// order, so it is published either way; the reason the order is refused is worked out from
    /// the card's own condition line, the town's daily limit and the player's resources.
    private static BuildOffer? ReadBuildOffer(List<UiElement> items,TownView? town,int[] resources)
    {
        string? Text(int id)=>items.FirstOrDefault(i=>i.Id==id)?.Text?.Trim();
        var amounts=items.Where(i=>i.Id==65535&&int.TryParse(i.Text?.Trim(),out _)).ToList();
        var numbers=amounts.Select(i=>int.Parse(i.Text!.Trim())).ToArray();
        // Each amount sits under its own resource icon, a 32x32 picture whose frame is the
        // resource index. Pairing them by column turns four bare numbers into a price.
        var icons=items.Where(i=>i.Id==65535&&i.Text is null&&i.Width==32&&i.Height==32).ToList();
        var short_=new List<string>();
        var price=amounts.Select(amount=>
        {
            var icon=icons.OrderBy(i=>Math.Abs(i.X+i.Width/2-(amount.X+amount.Width/2))).FirstOrDefault();
            string name=icon is null?"ресурс":Resource(icon.Frame);
            int need=int.Parse(amount.Text!.Trim());
            if(icon is not null&&icon.Frame is >=0 and <7&&icon.Frame<resources.Length&&resources[icon.Frame]<need)
                short_.Add($"{name}: нужно {need}, есть {resources[icon.Frame]}");
            return $"{name} {amount.Text!.Trim()}";
        }).ToList();
        bool canBuy=items.Any(i=>i.Id==30722&&i.Interactive);
        string? conditions=Text(5);
        string? blocked=null;
        if(!canBuy)
        {
            bool unmet=conditions is not null&&conditions.Contains("Требуется",StringComparison.OrdinalIgnoreCase);
            if(unmet)blocked="не выполнены условия постройки";
            else if(town?.BuiltToday==true)blocked="в этом городе сегодня уже строили";
            else if(short_.Count>0)blocked="не хватает ресурсов — "+string.Join("; ",short_);
            else blocked="игра не принимает заказ; причина по карточке не определена";
        }
        return new(Text(3),Text(4),conditions,numbers,canBuy,blocked){Price=price};
    }

    /// The seven resources in the order the game numbers them, which is also the frame index of
    /// the icon it draws beside a price.
    private static string Resource(int index)=>index switch
    {
        0=>"дерево",1=>"ртуть",2=>"руда",3=>"сера",4=>"кристаллы",5=>"самоцветы",6=>"золото",
        _=>"ресурс "+index
    };

    /// The eight player colours in the order the game numbers them.
    private static string Colour(int index)=>index switch
    {
        0=>"красный",1=>"синий",2=>"коричневый",3=>"зелёный",
        4=>"оранжевый",5=>"фиолетовый",6=>"бирюзовый",7=>"розовый",
        _=>"неизвестный ("+index+")"
    };

    /// Dialog classes this adapter can name. Everything else is an unmapped screen.
    private static readonly Dictionary<uint,string> ScreenNames=new()
    {
        [0x63a5e4]="adventure",[0x63db40]="message",[0x642478]="system_options",
        [0x63ff60]="main_menu",[0x63e6d8]="game_type",[0x641cbc]="scenario_selection",
        [0x64373c]="town",[0x6437b0]="town_hall",[0x643954]="building_confirmation",
        [0x640c5c]="recruitment",[0x643990]="tavern",[0x643c24]="creature_card",
        // Right-clicking an enemy hero on the map: his name and the size band of each stack.
        [0x6406b8]="enemy_hero_card",
        // The real split dialog: a slider between two halves of one stack, with its own confirm.
        [0x63b8f8]="split_army",
        [0x63eae8]="hero_screen",[0x642438]="exchange",[0x63fe74]="level_up",
        [0x63d528]="combat",[0x63d46c]="battle_result",[0x641ddc]="spellbook",
        [0x640330]="kingdom_overview",[0x63a610]="adventure_options",[0x643c64]="world_view",
        [0x640610]="puzzle_map",[0x641720]="scenario_info",[0x643774]="thieves_guild",
        [0x643a08]="marketplace",[0x6437ec]="mage_guild",[0x6439cc]="town_fort",
        // After a won scenario that makes the high score table: «введите ваше имя», field 501, OK 503.
        [0x63ebbc]="high_score_name",
        // The high score table: tabs for scenarios and campaigns, reset, exit.
        [0x63eb98]="high_scores",
    };

    private readonly string epoch=Guid.NewGuid().ToString("N");

    // Counts at the previous own decision of the current fight, keyed by round and moving stack.
    // Only a settled reading becomes that point: inside one action the bridge reads the fight many
    // times, and a frame of a retaliation animation briefly looks like an own move. A fight is told
    // apart from the next one by its line-up at the start, so a spellbook or a creature card
    // opened mid-fight leaves the memory as it was.
    private (string Fight,string Key,Dictionary<string,(string Name,int Count)> Counts)? decision;
    private string[] decisionChanges=[];

    private static string FightOf(CombatView combat)=>string.Join(",",combat.Stacks.Where(s=>!s.Summoned).Select(s=>$"{s.Id}:{s.StartCount}")
        .Concat(combat.Losses.Select(l=>$"{l.Id}:{l.Start}")).Distinct().OrderBy(x=>x,StringComparer.Ordinal));

    private static string[] Changes(CombatView combat,Dictionary<string,(string Name,int Count)> before)
    {
        var now=combat.Stacks.ToDictionary(s=>s.Id,s=>(s.Name,s.Count));
        return before.Keys.Union(now.Keys).Select(id=>
        {
            var was=before.TryGetValue(id,out var b)?b:(Name:now[id].Name,Count:0);
            int count=now.TryGetValue(id,out var n)?n.Count:0;
            string whose=id.StartsWith($"stack:{combat.OwnSide}:",StringComparison.Ordinal)?"твои":"враг";
            return count==was.Count?null:$"{was.Name} ({whose}) {was.Count} → {count}"+(count==0?" (отряд уничтожен)":"");
        }).OfType<string>().ToArray();
    }

    private CombatView? Remember(CombatView? combat)
    {
        if(combat is null)return null;
        if(decision is not null&&decision.Value.Fight!=FightOf(combat)){decision=null;decisionChanges=[];}
        if(decision is null)return combat;
        if(!combat.OwnTurn||decision.Value.Key==$"{combat.Round}:{combat.ActiveStack}")return combat with{SinceLastMove=decisionChanges};
        return combat with{SinceLastMove=Changes(combat,decision.Value.Counts)};
    }

    /// Marks a settled own move as the point the next «since your last move» is counted from.
    public void CommitDecision(CombatView? combat)
    {
        if(combat is null||!combat.OwnTurn)return;
        string fight=FightOf(combat),key=$"{combat.Round}:{combat.ActiveStack}";
        if(decision is {} d&&d.Fight==fight&&d.Key==key)return;
        decisionChanges=decision is {} last&&last.Fight==fight?Changes(combat,last.Counts):[];
        decision=(fight,key,combat.Stacks.ToDictionary(s=>s.Id,s=>(s.Name,s.Count)));
    }
    private bool creaturesRead;

    /// Every creature the running game knows, by type: the table the game consults to print a
    /// stack's name. Record 0x74 bytes, the singular name behind +0x14. It is read once, since the
    /// game fills it at start and never changes it.
    private void ReadCreatureNames()
    {
        if(creaturesRead)return;
        uint table=game.U32(0x6747B0);
        if(table<0x10000)return;
        var names=new Dictionary<int,string>();
        for(int type=0;type<256;type++)
        {
            string? name;
            try{name=game.Text(game.U32(table+(uint)type*0x74+0x14));}catch(InvalidOperationException){break;}
            // Unused slots sit between the base game and the expansion; they are skipped, and
            // only a name that reads as words is taken.
            if(string.IsNullOrWhiteSpace(name)||name.Length>40||!name.Any(char.IsLetter))continue;
            names[type]=name.Trim();
        }
        if(names.Count<100)return;
        GameReference.UseLiveCreatureNames(names);
        // The same record carries the creature's numbers — the ones its card shows a player:
        // cost at +0x20 (seven resources), AI value +0x40, growth +0x44, hit points +0x4c, speed
        // +0x50, attack +0x54, defence +0x58, damage +0x5c/+0x60, shots +0x64. Checked against the
        // text tables for base-game creatures, where both exist and agree.
        var cards=new List<ReferenceCard>();
        foreach(var (type,name) in names)
        {
            byte[] r;
            try{r=game.Read(table+(uint)type*0x74,0x74);}catch(InvalidOperationException){continue;}
            int I(int o)=>BitConverter.ToInt32(r,o);
            string plural=game.Text(BitConverter.ToUInt32(r,0x18))?.Trim()??name;
            var fields=new Dictionary<string,string>
            {
                ["Plural"]=plural,["Gold"]=I(0x38).ToString(),["AI Value"]=I(0x40).ToString(),["Growth"]=I(0x44).ToString(),
                ["Hit Points"]=I(0x4c).ToString(),["Speed"]=I(0x50).ToString(),["Attack"]=I(0x54).ToString(),
                ["Defense"]=I(0x58).ToString(),["Damage"]=$"{I(0x5c)}-{I(0x60)}",["Shots"]=I(0x64).ToString(),
            };
            cards.Add(new("существо",name,fields,
                $"{name} (мн. {plural}) — из таблицы существ запущенной игры: здоровье {I(0x4c)}, скорость {I(0x50)}, "
                +$"атака {I(0x54)}, защита {I(0x58)}, урон {I(0x5c)}-{I(0x60)}, выстрелов {I(0x64)}, "
                +$"цена {I(0x38)} золота, прирост {I(0x44)}, AI Value {I(0x40)}."));
        }
        GameReference.UseLiveCreatureCards(cards);
        creaturesRead=true;
    }

    private bool artifactsRead;

    /// Artefact names from the game's own artefact table: a record of 0x20 bytes per artefact
    /// with the name pointer first, the order being the artefact numbers the pictures carry.
    private void ReadArtifactNames()
    {
        if(artifactsRead)return;
        uint table=game.U32(0x660B68);
        if(table<0x10000)return;
        var names=new Dictionary<int,string>();
        var texts=new Dictionary<int,string>();
        for(int id=0;id<512;id++)
        {
            string? name;
            try{name=game.Text(game.U32(table+(uint)id*0x20));}catch(InvalidOperationException){continue;}
            if(string.IsNullOrWhiteSpace(name)||name.Length>60||!name.Any(char.IsLetter))continue;
            names[id]=name.Trim();
            // The record is name, price, slot, class, then the description the artefact card prints.
            try{if(game.Text(game.U32(table+(uint)id*0x20+0x10),2048) is {Length:>0} text)texts[id]=text.Trim();}
            catch(InvalidOperationException){}
        }
        if(names.Count<100)return;
        GameReference.UseLiveArtifactNames(names);
        GameReference.UseLiveArtifactTexts(texts);
        artifactsRead=true;
    }

    public Observation Observe()
    {
        ReadCreatureNames();
        ReadArtifactNames();
        // Two matching reads reduce transitional snapshots; safe-point synchronization remains future work.
        var first=ReadOnce();
        var second=ReadOnce();
        if(second.Screen!=animatedScreen)
        {
            // A screen just opened: watch it for a few beats so every picture that animates on
            // its own is known before the first revision is handed out, not discovered one
            // observation later as a spurious change of state.
            animatedKeys=[];animatedScreen=second.Screen;
            var look=second;
            // Four short beats missed slower animations on the map, and the first revision after
            // leaving a town then went stale on the very next read.
            for(int beat=0;beat<8;beat++)
            {
                Thread.Sleep(80);
                var next=ReadOnce();
                if(next.Screen!=look.Screen||next.Elements.Count!=look.Elements.Count)break;
                for(int i=0;i<next.Elements.Count;i++)
                    if(next.Elements[i]!=look.Elements[i]&&(next.Elements[i] with {Frame=0})==(look.Elements[i] with {Frame=0}))
                        animatedKeys.Add(next.Elements[i].Key);
                look=next;
            }
        }
        if(first.Revision==second.Revision)return Revise(second);
        // Some screens animate: the creatures in the fort and in the recruitment window step
        // through their frames on their own. Two reads that differ only in the frame a picture
        // happens to show are the same state, not a changing one; those pictures are kept out of
        // the revision, and nothing else is forgiven.
        if(first.Elements.Count!=second.Elements.Count)throw new InvalidOperationException("State changing; observe again");
        var animated=new HashSet<string>();
        for(int i=0;i<first.Elements.Count;i++)
        {
            var a=first.Elements[i];var b=second.Elements[i];
            if(a==b)continue;
            if(a with {Frame=0}!=b with {Frame=0})throw new InvalidOperationException("State changing; observe again");
            animated.Add(a.Key);
        }
        // Pictures seen animating on this screen stay known as such for as long as the screen is
        // up: two reads that happen to catch the same frame must not bring them back into the
        // revision, or the next observation looks like a different state.
        animatedKeys.UnionWith(animated);
        var settled=Revise(second);
        return settled;
    }

    private HashSet<string> animatedKeys=[];
    private string animatedScreen="";

    /// The revision of an observation, with the frames of pictures that animate on their own left
    /// out: they change between two reads of the very same state.
    private Observation Revise(Observation result)
    {
        bool same=result.Screen==animatedScreen;
        var steady=result with {Revision="",Elements=result.Elements.Select(e=>same&&animatedKeys.Contains(e.Key)?e with {Frame=0}:e).ToList()};
        string revision=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(epoch+JsonSerializer.Serialize(steady))))[..24];
        return result with {Revision=revision};
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
        var dialogs=new List<object>();uint dialogCursor=game.U32(game.U32(0x6992d0)+0x50);
        var dialogSeen=new HashSet<uint>();
        while(dialogCursor!=0&&dialogSeen.Add(dialogCursor)&&dialogs.Count<32)
        {
            uint first=game.U32(dialogCursor+0x34),last=game.U32(dialogCursor+0x38);
            var controls=new List<object>();
            if(last>=first&&last-first<=8192)
                for(uint slot=first;slot<last;slot+=4)
                {
                    uint item=game.U32(slot),vt=game.U32(item);
                    controls.Add(new{address=item,vtable=vt,id=BitConverter.ToUInt16(game.Read(item+0x10,2)),text=vt is 0x642dc0 or 0x642df8?game.Text(game.U32(item+0x34)):null,asset=vt==0x63bb54?game.Text(game.U32(item+0x30)+4,16):null});
                }
            dialogs.Add(new{address=dialogCursor,vtable=game.U32(dialogCursor),z=game.I32(dialogCursor+4),previous=game.U32(dialogCursor+8),next=game.U32(dialogCursor+12),deactivated=game.I32(dialogCursor+0x48),controls});
            dialogCursor=game.U32(dialogCursor+12);
        }
        if(activeDialog==0)return new{activeDialog,managers,inputCandidates,windowManager=Convert.ToHexString(game.Read(game.U32(0x6992d0),0x80)),menu=game.U32(0x6996b0)};
        uint uiFirst=game.U32(activeDialog+0x34),uiLast=game.U32(activeDialog+0x38);
        var rawUi=new List<object>();
        if(uiLast>=uiFirst&&uiLast-uiFirst<=8192)
            for(uint slot=uiFirst;slot<uiLast;slot+=4){
                uint item=game.U32(slot);
                rawUi.Add(new{address=item,vtable=game.U32(item),id=BitConverter.ToUInt16(game.Read(item+0x10,2)),state=BitConverter.ToUInt16(game.Read(item+0x16,2)),text=game.U32(item) is 0x642dc0 or 0x642df8?game.Text(game.U32(item+0x34)):null,closeButton=game.U32(item)==0x63bb54?game.Read(item+0x44,1)[0]:-1,asset=game.U32(item)==0x63bb54?game.Text(game.U32(item+0x30)+4,16):null});
            }
        var linkedUi=new List<object>();var linkedSeen=new HashSet<uint>();
        for(uint item=game.U32(activeDialog+0x2c);item!=0&&linkedSeen.Add(item)&&linkedUi.Count<2048;item=game.U32(item+8))
        {
            uint vt=game.U32(item);
            linkedUi.Add(new{address=item,vtable=vt,id=BitConverter.ToUInt16(game.Read(item+0x10,2)),parent=game.U32(item+4),state=BitConverter.ToUInt16(game.Read(item+0x16,2)),close=vt==0x63bb54?game.Read(item+0x44,1)[0]:-1,keys=vt==0x63bb54?ReadButtonKeys(item):[],text=vt is 0x642dc0 or 0x642df8?game.Text(game.U32(item+0x34)):null,asset=vt==0x63bb54?game.Text(game.U32(item+0x30)+4,16):null});
        }
        object? scenarioProbe=null;
        object? selectedMapProbe=null;
        if(game.U32(activeDialog)==0x641cbc)
        {
            try
            {
                uint map=checked(game.U32(activeDialog+0x1054)+(uint)game.I32(activeDialog+0x374)*0xca4);
                selectedMapProbe=new{name=game.Text(game.U32(map+0x2d4)),description=game.Text(game.U32(map+0x2e4)),dimension=game.I32(map+0x18),settings=Enumerable.Range(0,10).Select(i=>game.I32(activeDialog+0x1898+(uint)i*4)).ToArray()};
            }
            catch(Exception e) when(e is InvalidOperationException or OverflowException){selectedMapProbe=new{error=e.Message};}
            try{scenarioProbe=new{flags=game.Read(activeDialog+0x37c,3),vectors=Convert.ToHexString(game.Read(activeDialog+0x1050,20)),dimension=game.I32(activeDialog+0x3a4),name=game.Text(game.U32(activeDialog+0x660)),description=game.Text(game.U32(activeDialog+0x670)),top=game.I32(activeDialog+0x370),selected=game.I32(activeDialog+0x374),buttons=Enumerable.Range(0,(int)(uiLast-uiFirst)/4).Select(i=>game.U32(uiFirst+(uint)i*4)).Where(a=>game.U32(a) is 0x63bb54 or 0x63bb88).Select(a=>new{id=BitConverter.ToUInt16(game.Read(a+0x10,2)),frame=game.I32(a+0x34)}).ToArray()};}
            catch(InvalidOperationException e){scenarioProbe=new{error=e.Message};}
        }
        return new {
        scenarioProbe,selectedMapProbe,linkedUi,
        activeDialog,dialogVtable=game.U32(activeDialog),rawUi,dialogs,townScroll=game.I32(activeDialog+0x68),ownedTowns=game.U32(0x69ccfc)>=0x10000?game.Read(game.U32(0x69ccfc)+0x3e,4).Select(b=>(int)b).ToArray():[],
        current=game.I32(0x69ccf4), other=game.I32(0x6995a4), active=game.U32(0x69ccfc),
        main=game.U32(0x699538), mode=game.I32(0x698a40),managers,inputCandidates
    };}
    public record ControlBox(int Id,int X,int Y,int Width,int Height);
    /// Which army cell of the town screen currently carries the selection frame.
    ///
    /// The frame is a control of its own, one per cell: 115 plus the slot over the upper row and
    /// 140 plus the slot over the lower one, hidden (state 2) until the player picks that stack
    /// and visible (state 6) while it is picked. Reading it is what keeps a transfer honest — a
    /// selection left over from an earlier gesture turns the next press into a move nobody asked
    /// for, and that is exactly how two stacks once swapped instead of merging.
    public (bool Garrison,int Slot)? SelectedArmyCell()
    {
        uint manager=game.U32(0x6992d0),dlg=game.U32(manager+0x54);
        if(dlg==0)return null;
        var seen=new HashSet<uint>();
        for(uint item=game.U32(dlg+0x2c);item!=0&&seen.Add(item)&&seen.Count<2048;item=game.U32(item+8))
        {
            int id=BitConverter.ToUInt16(game.Read(item+0x10,2));
            bool upper=id is >=115 and <=121,lower=id is >=140 and <=146;
            if(!upper&&!lower)continue;
            if((BitConverter.ToUInt16(game.Read(item+0x16,2))&4)==0)continue;
            return (upper,id-(upper?115:140));
        }
        return null;
    }


    /// Finds any control of the active dialog by its own id, whether or not the observation
    /// publishes it. Observations stay compact; inspection still reaches every cell on screen.
    public ControlBox? FindControlById(int id,int occurrence=0)
    {
        uint manager=game.U32(0x6992d0),dlg=game.U32(manager+0x54);
        if(dlg==0)return null;
        int dx=game.I32(dlg+0x18),dy=game.I32(dlg+0x1c);
        var seen=new HashSet<uint>();
        int found=0;
        for(uint item=game.U32(dlg+0x2c);item!=0&&seen.Add(item)&&seen.Count<2048;item=game.U32(item+8))
        {
            if(BitConverter.ToUInt16(game.Read(item+0x10,2))!=id)continue;
            if(found++<occurrence)continue;
            int w=BitConverter.ToUInt16(game.Read(item+0x1c,2)),h=BitConverter.ToUInt16(game.Read(item+0x1e,2));
            if(w<1||h<1)continue;
            return new(id,dx+BitConverter.ToInt16(game.Read(item+0x18,2),0),
                dy+BitConverter.ToInt16(game.Read(item+0x1a,2),0),w,h);
        }
        return null;
    }

    public record ScreenProbe(uint Vtable,string? Name,int Controls,object[] Items);
    /// Names whatever dialog is on top and lists its controls, even when the screen reader has no
    /// name for it yet. This is how an unmapped screen gets mapped: it is the developer's view,
    /// never part of a player observation.
    public ScreenProbe ProbeScreen()
    {
        uint manager=game.U32(0x6992d0),dlg=game.U32(manager+0x54);
        if(dlg==0)throw new InvalidOperationException("No dialog on screen");
        uint vtable=game.U32(dlg);
        int dx=game.I32(dlg+0x18),dy=game.I32(dlg+0x1c);
        var items=new List<object>();
        var seen=new HashSet<uint>();
        for(uint item=game.U32(dlg+0x2c);item!=0&&seen.Add(item)&&items.Count<512;item=game.U32(item+8))
        {
            uint vt=game.U32(item);
            ushort state=BitConverter.ToUInt16(game.Read(item+0x16,2));
            items.Add(new{
                id=(int)BitConverter.ToUInt16(game.Read(item+0x10,2)),
                vtable=vt,state,
                x=dx+BitConverter.ToInt16(game.Read(item+0x18,2),0),
                y=dy+BitConverter.ToInt16(game.Read(item+0x1a,2),0),
                w=(int)BitConverter.ToUInt16(game.Read(item+0x1c,2)),
                h=(int)BitConverter.ToUInt16(game.Read(item+0x1e,2)),
                asset=vt is 0x63bb54 or 0x63bb88?game.Text(game.U32(item+0x30)+4,16):null,
                frame=game.I32(item+0x34),
                text=vt is 0x642dc0 or 0x642df8 or 0x642d50 or 0x641c70 or 0x63ebf4?game.Text(game.U32(item+0x34))
                    :vt==0x63bb88?game.Text(game.U32(item+0x5c)):null});
        }
        return new(vtable,NameOf(vtable),items.Count,items.ToArray());
    }

    /// Rectangles of the smaller controls now drawn on top of the screen. In a fight the
    /// expansion shows a stats panel of the stack under the pointer, and near the right edge of the
    /// field it lies right over that stack and takes the click meant for it.
    public List<(int X,int Y,int W,int H)> Overlays()
    {
        uint manager=game.U32(0x6992d0),dlg=game.U32(manager+0x54);
        var found=new List<(int,int,int,int)>();
        if(dlg==0)return found;
        int dx=game.I32(dlg+0x18),dy=game.I32(dlg+0x1c);
        var seen=new HashSet<uint>();
        for(uint item=game.U32(dlg+0x2c);item!=0&&seen.Add(item)&&seen.Count<512;item=game.U32(item+8))
        {
            ushort state=BitConverter.ToUInt16(game.Read(item+0x16,2));
            if((state&4)==0)continue;
            int w=BitConverter.ToUInt16(game.Read(item+0x1c,2)),h=BitConverter.ToUInt16(game.Read(item+0x1e,2));
            int x=dx+BitConverter.ToInt16(game.Read(item+0x18,2),0),y=dy+BitConverter.ToInt16(game.Read(item+0x1a,2),0);
            // The field itself and the command bar under it are not in the way.
            if(w>=400||h>=300||y>=556)continue;
            found.Add((x,y,w,h));
        }
        return found;
    }

    public static string? NameOf(uint vtable)=>ScreenNames.TryGetValue(vtable,out var name)?name:null;

    public record CardView(uint Vtable,string[] Texts);
    /// Reads the plain text of whatever dialog is on top right now, without classifying it as a
    /// supported screen. Used for the game's own info cards, which appear only while the right
    /// mouse button is held and have no actions of their own.
    public CardView ReadCard()
    {
        uint manager=game.U32(0x6992d0),dlg=game.U32(manager+0x54);
        if(dlg==0)throw new InvalidOperationException("No dialog on screen");
        int dx=game.I32(dlg+0x18),dy=game.I32(dlg+0x1c);
        var found=new List<(int X,int Y,string Text)>();
        (int,int,string)? Read(uint item)
        {
            uint vt=game.U32(item);
            string? text=vt is 0x642dc0 or 0x642df8 or 0x642d50?game.Text(game.U32(item+0x34))
                :vt==0x63bb88?game.Text(game.U32(item+0x5c)):null;
            if(string.IsNullOrWhiteSpace(text))return null;
            return (dx+BitConverter.ToInt16(game.Read(item+0x18,2),0),dy+BitConverter.ToInt16(game.Read(item+0x1a,2),0),text.Trim());
        }
        var seen=new HashSet<uint>();
        for(uint item=game.U32(dlg+0x2c);item!=0&&seen.Add(item)&&seen.Count<2048;item=game.U32(item+8))
            if(Read(item) is {} one)found.Add(one);
        if(found.Count==0)
            for(uint slot=game.U32(dlg+0x34),end=game.U32(dlg+0x38);slot<end&&end-slot<=8192;slot+=4)
                if(Read(game.U32(slot)) is {} one)found.Add(one);
        // A card is read the way the eye reads it: top to bottom, left to right, one line per
        // row of the card. Equal values must all survive — attack 1 and defence 1 are two facts,
        // and collapsing repeats had silently dropped every stat that happened to match another.
        var lines=found.OrderBy(f=>f.Y).ThenBy(f=>f.X)
            .GroupBy(f=>f.Y/6)
            .Select(row=>string.Join("  ",row.OrderBy(f=>f.X).Select(f=>f.Text)))
            .ToArray();
        return new(game.U32(dlg),lines);
    }

    /// The hero sheet the game opens in view-only mode when the right button is held on a hero
    /// portrait — in the tavern, in the kingdom overview. It is read as one line in the words of
    /// the sheet itself: name, level and class, the four primary skills, specialty, experience
    /// and mana, secondary skills and the army. The army is a row of portraits without captions;
    /// each portrait draws the creature number plus two, and the count stands under it.
    /// The card the game shows while the right button is held on a town that is not yours:
    /// its name and the creatures of its garrison, drawn as small portraits (creature number plus
    /// two). How many stand in each stack the card does not say, so neither does this.
    public string? TownCard()
    {
        uint manager=game.U32(0x6992d0),dlg=game.U32(manager+0x54);
        if(dlg==0||game.U32(dlg)!=0x640704)return null;
        string? name=null;var garrison=new List<(int X,int Y,string Creature)>();
        var seen=new HashSet<uint>();
        int dx=game.I32(dlg+0x18),dy=game.I32(dlg+0x1c);
        for(uint item=game.U32(dlg+0x2c);item!=0&&seen.Add(item)&&seen.Count<512;item=game.U32(item+8))
        {
            uint vt=game.U32(item);
            int id=BitConverter.ToUInt16(game.Read(item+0x10,2));
            if(id==2002&&vt is 0x642dc0 or 0x642df8)name=game.Text(game.U32(item+0x34))?.Trim();
            if(id is >=2009 and <=2015&&vt==0x63ec48&&(BitConverter.ToUInt16(game.Read(item+0x16,2))&4)!=0)
            {
                int frame=game.I32(item+0x34);
                if(frame>=2)garrison.Add((BitConverter.ToInt16(game.Read(item+0x18,2),0),BitConverter.ToInt16(game.Read(item+0x1a,2),0),GameReference.Creature(frame-2)));
            }
        }
        string army=garrison.Count==0?"пусто":string.Join(", ",garrison.OrderBy(g=>g.Y).ThenBy(g=>g.X).Select(g=>g.Creature));
        return $"Город {name}; гарнизон: {army} (сколько в каждом отряде, карточка не показывает)";
    }

    /// The card of somebody else's hero held open by the right button on the map: his name and,
    /// per stack, the creature picture (creature number plus two) with the size band under it.
    public string? ForeignHeroCard()
    {
        uint manager=game.U32(0x6992d0),dlg=game.U32(manager+0x54);
        if(dlg==0||game.U32(dlg)!=0x6406b8)return null;
        var controls=new Dictionary<int,(uint Vt,int Frame,string? Text,bool Shown)>();
        var seen=new HashSet<uint>();
        for(uint item=game.U32(dlg+0x2c);item!=0&&seen.Add(item)&&seen.Count<512;item=game.U32(item+8))
        {
            uint vt=game.U32(item);
            int id=BitConverter.ToUInt16(game.Read(item+0x10,2));
            bool shown=(BitConverter.ToUInt16(game.Read(item+0x16,2))&4)!=0;
            string? text=vt is 0x642dc0 or 0x642df8 or 0x642d50?game.Text(game.U32(item+0x34))?.Trim():null;
            controls[id]=(vt,game.I32(item+0x34),text,shown);
        }
        string name=controls.TryGetValue(2002,out var n)?n.Text??"":"";
        var army=new List<string>();
        for(int slot=0;slot<7;slot++)
        {
            if(!controls.TryGetValue(2011+slot*2,out var picture)||!picture.Shown||picture.Frame<2)continue;
            string size=controls.TryGetValue(2012+slot*2,out var band)?band.Text??"?":"?";
            army.Add($"{GameReference.Creature(picture.Frame-2)} {size}");
        }
        return $"Герой {name}; войско: {(army.Count>0?string.Join(", ",army):"не прочитано")} (размер вилкой — точнее игрок не видит)";
    }

    public string? HeroCard()
    {
        uint manager=game.U32(0x6992d0),dlg=game.U32(manager+0x54);
        if(dlg==0||game.U32(dlg)!=0x63eae8)return null;
        var controls=new Dictionary<int,(uint Vt,int Frame,string? Text)>();
        var seen=new HashSet<uint>();
        for(uint item=game.U32(dlg+0x2c);item!=0&&seen.Add(item)&&seen.Count<2048;item=game.U32(item+8))
        {
            uint vt=game.U32(item);
            int id=BitConverter.ToUInt16(game.Read(item+0x10,2));
            string? text=vt is 0x642dc0 or 0x642df8 or 0x642d50?game.Text(game.U32(item+0x34))?.Trim():null;
            if(!controls.ContainsKey(id)||text is {Length:>0})controls[id]=(vt,game.I32(item+0x34),text);
        }
        string T(int id)=>controls.TryGetValue(id,out var c)?c.Text??"":"";
        var skills=Enumerable.Range(0,8).Select(i=>$"{T(95+i)} {T(87+i)}".Trim()).Where(x=>x.Length>0);
        var army=Enumerable.Range(0,7)
            .Where(i=>controls.TryGetValue(54+i,out var p)&&p.Vt==0x63ec48&&p.Frame>=2&&T(61+i).Length>0)
            .Select(i=>$"{GameReference.Creature(controls[54+i].Frame-2)} {T(61+i)}");
        return $"{T(1)} — {T(140)}; атака {T(46)}, защита {T(47)}, сила магии {T(48)}, знания {T(49)}; "
            +$"специализация {T(139)}; опыт {T(112)}, мана {T(113)}; навыки: {string.Join(", ",skills)}; "
            +$"армия: {string.Join(", ",army)}";
    }

    /// Who stands in each place of the hero list on the right of the adventure map. The player
    /// record keeps the heroes out on the map in list order at +0x08, eight places, empty ones -1;
    /// a hero leading a town garrison is not in it, exactly as he is not in the panel.
    public static int[] SidebarHeroes(WindowsGame game,int player)
    {
        uint record=game.U32(0x699538)+0x20ad0+(uint)player*0x168;
        return Enumerable.Range(0,8).Select(i=>game.I32(record+8+(uint)i*4)).ToArray();
    }

    /// Which town stands in each place of the town list on the right of the adventure map: the
    /// player record keeps the count at +0x3E and the towns in list order from +0x40.
    public static int[] SidebarTowns(WindowsGame game,int player)
    {
        uint record=game.U32(0x699538)+0x20ad0+(uint)player*0x168;
        int count=Math.Clamp((int)game.Read(record+0x3e,1)[0],0,48);
        return game.Read(record+0x40,count).Select(b=>(int)b).ToArray();
    }

    public (int X,int Y)? FindControl(int x,int y,int w,int h)
    {
        // Locate one dialog control by its exact reported rectangle and return its centre.
        // Used where the game's own slot widgets must be addressed: the town garrison row.
        uint manager=game.U32(0x6992d0),dlg=game.U32(manager+0x54);
        if(dlg==0)return null;
        int dx=game.I32(dlg+0x18),dy=game.I32(dlg+0x1c);
        (int X,int Y)? found=null;
        var seen=new HashSet<uint>();
        for(uint item=game.U32(dlg+0x2c);item!=0&&seen.Add(item);item=game.U32(item+8))
        {
            int ix=dx+BitConverter.ToInt16(game.Read(item+0x18,2),0),iy=dy+BitConverter.ToInt16(game.Read(item+0x1a,2),0);
            int iw=BitConverter.ToUInt16(game.Read(item+0x1c,2)),ih=BitConverter.ToUInt16(game.Read(item+0x1e,2));
            if(ix!=x||iy!=y||iw!=w||ih!=h)continue;
            found??=(ix+iw/2,iy+ih/2);
        }
        return found;
    }
    public object DiagnosticDialog()    {
        // Developer-only structural dump of the active dialog: every control, its raw fields
        // and the dialog header words used by the list widgets. No game-state writes.
        uint manager=game.U32(0x6992d0),dlg=game.U32(manager+0x54);
        if(dlg==0)return new{error="no active dialog"};
        var controls=new List<object>();
        var seen=new HashSet<uint>();
        int dx=game.I32(dlg+0x18),dy=game.I32(dlg+0x1c);
        for(uint item=game.U32(dlg+0x2c);item!=0&&seen.Add(item)&&controls.Count<4096;item=game.U32(item+8))
        {
            uint vt=game.U32(item);
            var record=new Dictionary<string,object?>{{"address",item},{"vtable",vt},
                {"id",(int)BitConverter.ToUInt16(game.Read(item+0x10,2))},
                {"state",(int)BitConverter.ToUInt16(game.Read(item+0x16,2))},
                {"parent",game.U32(item+4)},
                {"x",dx+BitConverter.ToInt16(game.Read(item+0x18,2),0)},
                {"y",dy+BitConverter.ToInt16(game.Read(item+0x1a,2),0)},
                {"w",(int)BitConverter.ToUInt16(game.Read(item+0x1c,2))},
                {"h",(int)BitConverter.ToUInt16(game.Read(item+0x1e,2))}};
            if(vt is 0x642dc0 or 0x642df8 or 0x642d50)record["text"]=game.Text(game.U32(item+0x34));
            if(vt is 0x63bb54 or 0x63bb88)
            {
                record["asset"]=game.Text(game.U32(item+0x30)+4,16);
                var keys=ReadButtonKeys(item);
                record["keys"]=string.Join(",",keys.Select(k=>k.ToString("X")));
                try{record["caption"]=game.Text(game.U32(item+0x5c));}catch(InvalidOperationException){}
            }
            controls.Add(record);
        }
        return new{activeDialog=dlg,vtable=game.U32(dlg),controls,
            header=Convert.ToHexString(game.Read(dlg,0x60)),
            words=Enumerable.Range(0,0x40).Select(i=>new{i,value=game.I32(dlg+(uint)(i*4))}).Where(x=>x.value!=0).ToArray()};
    }
    private int[] ReadButtonKeys(uint item)
    {
        uint first=game.U32(item+0x4c),last=game.U32(item+0x50);
        if(last<first||last-first>128||(last-first)%4!=0)return [];
        return Enumerable.Range(0,(int)(last-first)/4).Select(i=>game.I32(first+(uint)i*4)).ToArray();
    }
    private Observation ReadOnce()
    {
        if(player is <0 or >7) throw new InvalidOperationException("Player configuration invalid");
        uint manager=game.U32(0x6992d0),dlg=game.U32(manager+0x54);
        if(dlg==0)throw new InvalidOperationException("UI transition in progress");
        uint vtable=game.U32(dlg);
        bool frontend=vtable is 0x63ff60 or 0x63e6d8 or 0x641cbc;
        int[] resources=[],date=[];HeroView? hero=null;
        // Another player's turn in a shared game: the screen is his, so none of its controls are
        // this side's to read or press, but this side's own state is still its own.
        bool waiting=!frontend&&game.I32(0x69ccf4)!=player;
        if(!frontend)
        {
        uint main=game.U32(0x699538),p=main+0x20ad0+(uint)player*0x168;
        if(!waiting&&game.U32(0x69ccfc)!=p) throw new InvalidOperationException("Active player layout not validated");
        byte[] person=game.Read(p,0x168);
        if(person[0]!=player) throw new InvalidOperationException("Player identity mismatch");
        resources=Enumerable.Range(0,7).Select(i=>BitConverter.ToInt32(person,0x9c+i*4)).ToArray();
        byte[] dateBytes=game.Read(main+0x1f63e,6);
        date=Enumerable.Range(0,3).Select(i=>(int)BitConverter.ToUInt16(dateBytes,i*2)).ToArray();
        int heroId=BitConverter.ToInt32(person,4);
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
                Enumerable.Range(0,7).Select(i=>BitConverter.ToInt32(h,0xad+i*4)).ToArray())
                {PlannedDestination=[BitConverter.ToInt32(h,0x35),BitConverter.ToInt32(h,0x39),BitConverter.ToInt16(h,0x3d)]};
        }
        }
        var roster=new List<HeroView>();
        var foreignHeroes=new List<ForeignHero>();
        if(!frontend)
        {
            uint main2=game.U32(0x699538);
            byte[] code2=game.Read(0x4317e1,19);
            if(code2.AsSpan(0,13).SequenceEqual(Convert.FromHexString("8BC2C1E00603C28D04C08D8441")))
            {
                uint baseAddress=checked(main2+BitConverter.ToUInt32(code2,13));
                // Heroes live in one array; ownership is a byte inside each record, so the roster
                // is read by walking it rather than by cycling the selection in the interface.
                for(int id=0;id<256;id++)
                {
                    uint at=checked(baseAddress+(uint)id*0x492);
                    byte[] head;
                    // The array ends where the game says it does, not where a guessed count does:
                    // the first unreadable record is the end, and one bad record never costs the
                    // whole observation.
                    try{head=game.Read(at,0x24);}catch{break;}
                    if(BitConverter.ToInt32(head,0x1a)!=id)continue;
                    if(head[0x22]!=player)
                    {
                        // Somebody else's hero counts only where the player can actually see him.
                        // Reading a position through the fog would be looking at what the player
                        // cannot, so the tile's visibility decides.
                        int fx=BitConverter.ToInt16(head,0),fy=BitConverter.ToInt16(head,2),fz=BitConverter.ToInt16(head,4);
                        if(head[0x22]>7)continue;
                        try
                        {
                            if(game.U32(0x699538)==0)continue;
                            // Same layout the map reader uses: the size and the visibility plane
                            // live in the scenario setup, and a hero is reported only where this
                            // player's own bit is set in that plane.
                            uint mapSetup=game.U32(0x699538)+0x1fb70;
                            int size=game.I32(mapSetup+0xd4);
                            uint vision=game.U32(0x698a48);
                            if(size<36||size>252||vision==0)continue;
                            if(fx<0||fy<0||fx>=size||fy>=size||fz<0)continue;
                            uint index=checked((uint)((fz*size+fy)*size+fx));
                            if((game.Read(vision+index*2,1)[0]&(1<<player))==0)continue;
                            string otherName=Encoding.GetEncoding(1251).GetString(game.Read(at,0x492),0x23,13).Split('\0')[0];
                            foreignHeroes.Add(new(id,otherName,head[0x22],[fx,fy,fz]));
                        }
                        catch(Exception){}
                        continue;
                    }
                    byte[] h2;
                    try{h2=game.Read(at,0x492);}catch{continue;}
                    string name2=Encoding.GetEncoding(1251).GetString(h2,0x23,13).Split(' ')[0];
                    roster.Add(new(id,name2,Enumerable.Range(0,3).Select(i=>(int)BitConverter.ToInt16(h2,i*2)).ToArray(),
                        BitConverter.ToInt16(h2,0x18),BitConverter.ToInt32(h2,0x4d),BitConverter.ToInt32(h2,0x49),
                        h2.Skip(0x476).Take(4).Select(v=>(int)v).ToArray(),
                        Enumerable.Range(0,7).Select(i=>BitConverter.ToInt32(h2,0x91+i*4)).ToArray(),
                        Enumerable.Range(0,7).Select(i=>BitConverter.ToInt32(h2,0xad+i*4)).ToArray()));
                }
            }
        }
        // Every small popup the expansion opens over a screen — the grids of starting towns and
        // heroes, the drop-down lists, the team agreements — is a dialog class from HotA.dll, which
        // sits far above the base game image and at a different address each run. They are told
        // apart from one another by what stands in them, not by the class pointer.
        string screen=NameOf(vtable)??(vtable>0x700000?"popup_choice":"unsupported");
        // Unvalidated dialog classes are not published to the player yet.
        if(screen=="unsupported") throw new InvalidOperationException($"Current screen not supported by this adapter yet (dialog class 0x{vtable:X})");
        uint surface=game.U32(manager+0x40);
        int width=game.I32(surface+0x24),height=game.I32(surface+0x28);
        if(width<640||width>8192||height<480||height>8192) throw new InvalidOperationException("Invalid surface geometry");
        byte[] d=game.Read(dlg,0x4c);
        int dx=BitConverter.ToInt32(d,0x18),dy=BitConverter.ToInt32(d,0x1c);
        uint start=BitConverter.ToUInt32(d,0x34),end=BitConverter.ToUInt32(d,0x38),cap=BitConverter.ToUInt32(d,0x3c);
        if(start>end||end>cap||(end-start)%4!=0||end-start>8192) throw new InvalidOperationException("Invalid UI list");
        var items=new List<UiElement>();
        var controls=new List<uint>();
        if(screen is "message" or "combat" or "spellbook" or "battle_result" or "tavern" or "recruitment" or "creature_card" or "split_army" or "hero_screen" or "exchange" or "level_up" or "kingdom_overview" or "adventure_options" or "world_view" or "puzzle_map" or "scenario_info" or "thieves_guild" or "marketplace" or "mage_guild" or "town_fort" or "popup_choice")
        {
            var seen=new HashSet<uint>();
            for(uint item=game.U32(dlg+0x2c);item!=0;item=game.U32(item+8))
            {
                if(!seen.Add(item)||seen.Count>2048)throw new InvalidOperationException("Invalid dialog control list");
                controls.Add(item);
            }
        }
        else for(uint pos=start;pos<end;pos+=4)controls.Add(game.U32(pos));
        for(int controlIndex=0;controlIndex<controls.Count;controlIndex++)
        {
            uint a=controls[controlIndex];byte[] b=game.Read(a,0x38);
            if(BitConverter.ToUInt32(b,4)!=dlg) throw new InvalidOperationException("UI changed while reading");
            ushort state=BitConverter.ToUInt16(b,0x16);
            if((state&4)==0) continue;
            int iw=BitConverter.ToUInt16(b,0x1c),ih=BitConverter.ToUInt16(b,0x1e);
            if(iw==0||ih==0) continue;
            uint vt=BitConverter.ToUInt32(b);string? text=null,asset=null;
            // 0x641c70 is the edit field (the file name in the save browser): its line lives where a
            // text label keeps its own.
            if(vt is 0x642dc0 or 0x642df8 or 0x642d50 or 0x641c70 or 0x63ebf4) text=game.Text(game.U32(a+0x34));
            if(vt is 0x63bb54 or 0x63bb88||(screen=="spellbook"||screen=="adventure")&&vt==0x63ec48) asset=game.Text(game.U32(a+0x30)+4,16);
            // A reward in a message is a picture with its amount under it; which file the picture
            // comes from says what kind of reward it is — resource, artifact, creature, skill.
            if(screen=="message"&&vt is 0x63ec48 or 0x63ba94&&asset is null)
                try{asset=game.Text(game.U32(a+0x30)+4,16);}catch(InvalidOperationException){}
            if(vt==0x63bb88)text=game.Text(game.U32(a+0x5c));
            bool interactive=(vt is 0x63bb54 or 0x63bb88||(screen is "spellbook" or "adventure" or "popup_choice")&&vt==0x63ec48)&&(state&2)!=0&&(state&0x28)==0;
            if(vt==0x641c70)interactive=(state&2)!=0;
            // The grids of starting towns and heroes are pictures with no caption and no button
            // graphic, and the game hit-tests them itself; they are the whole content of the popup,
            // so dropping them would leave the screen empty.
            if(screen=="popup_choice"&&vt is 0x63ec48 or 0x63ba94)interactive=(state&2)!=0;
            // The hero list on the right of the map is a column of portraits drawn as plain
            // pictures; pressing one selects that hero, pressing the selected one opens his sheet.
            if(screen=="adventure"&&vt==0x63ba94&&BitConverter.ToUInt16(b,0x10) is >=15 and <=19)interactive=(state&6)==6;
            // Some controls carry no text and no button graphic yet still say something: the town
            // hall colours a bare picture next to each row to mark built, buildable or blocked.
            bool bareControlMatters=screen is "town_hall" or "town_fort" or "adventure" or "hero_screen" or "building_confirmation" or "popup_choice" or "tavern" or "battle_result" or "marketplace" or "mage_guild" or "enemy_hero_card"||(screen is "message" or "exchange" or "level_up")&&(state&2)!=0;
            if(string.IsNullOrEmpty(text)&&asset==null&&!bareControlMatters&&vt!=0x641c70) continue;
            items.Add(new UiElement($"ui:{controlIndex}",BitConverter.ToUInt16(b,0x10),text,asset,
                dx+BitConverter.ToInt16(b,0x18),dy+BitConverter.ToInt16(b,0x1a),iw,ih,interactive)
                {Frame=BitConverter.ToInt32(b,0x34),Selected=(state&16)!=0});
        }
        // The expansion's backpack is its own window: an eight by eight grid of cells numbered
        // from 1000, with each carried artefact drawn over it as a picture numbered from 2000.
        if(screen=="popup_choice"&&items.Count(i=>i.Id is >=1000 and <1064&&i.Width==47)==64)screen="backpack";
        if(screen=="scenario_selection"&&items.Any(i=>i.Id==186&&i.Asset=="scnrsav.def"))screen="save_game";
        SaveList? saves=null;
        if((screen=="scenario_selection"||screen=="save_game")&&items.Any(i=>i.Id==186&&i.Asset=="scnrlod.def"))screen="load_game";
        if(screen is "save_game" or "load_game")
        {
            // Own save files are visible to the player here; the adapter only reports them.
            try {saves=new SaveListReader(game).Read(dlg,screen=="save_game"?"save":"load");}
            catch(InvalidOperationException e){saves=new(screen=="save_game"?"save":"load",[],-1,"Save list unavailable: "+e.Message);}
        }
        var towns=frontend?new List<TownView>():new TownReader(game,player).Read();
        var setup=screen=="scenario_selection"?new ScenarioReader(game).Read(dlg,items):null;
        CombatView? fight=null;
        if(screen=="combat")
        {
            // A computer attacking this side on its own turn opens a fight this side must answer.
            try{fight=new CombatReader(game,player).Read();waiting=false;}
            catch(InvalidOperationException)when(waiting){}
        }
        var combat=Remember(fight);
        if(waiting)items=[];
        // The side list of towns — on the map and in the town screen — draws each town's icon with
        // an odd frame once the town has built today: the cross the player sees over it.
        int iconBase=screen=="adventure"?32:screen=="town"?155:-1;
        if(iconBase>0&&!frontend&&!waiting)
        {
            var order=SidebarTowns(game,player);
            towns=towns.Select(t=>
            {
                int place=Array.IndexOf(order,t.Id);
                var icon=place<0?null:items.FirstOrDefault(i=>i.Id==iconBase+place);
                return icon is null?t:t with{IconCross=icon.Frame%2==1};
            }).ToList();
        }
        int openTown=-1;
        if(!waiting&&screen is "town" or "town_hall" or "town_fort" or "building_confirmation" or "recruitment" or "marketplace")
            try{openTown=game.Read(game.U32(game.U32(0x69954c)+0x38),1)[0];}
            catch(InvalidOperationException){openTown=-1;}
        List<string> foreignArmy=[];
        string? foreignName=null;
        if(screen=="enemy_hero_card")
        {
            // The card shows what a player sees of somebody else's army: a picture per stack and a
            // size band instead of a number. The picture's frame is the creature, two ahead of the
            // type the game stores, the same offset the town rows use.
            foreignName=items.FirstOrDefault(i=>i.Id==2002)?.Text?.Trim();
            for(int slot=0;slot<7;slot++)
            {
                var size=items.FirstOrDefault(i=>i.Id==2012+slot*2);
                var picture=items.FirstOrDefault(i=>i.Id==2011+slot*2);
                if(size?.Text is null||picture is null)continue;
                foreignArmy.Add($"{GameReference.Creature(picture.Frame-2)} {size.Text.Trim()}");
            }
        }
        string? selected=null;
        if(screen=="town")
        {
            var cell=SelectedArmyCell();
            if(cell is {} picked)selected=$"{(picked.Garrison?"верхний":"нижний")} ряд, слот {picked.Slot}";
        }
        var actions=waiting?[]:ScreenActions.Build(game,player,screen,items,towns,hero,roster,saves,setup,combat,selected);
        HeroSheet? sheet=null;
        if(screen=="hero_screen")
        {
            // Skill names sit at 87..94, their mastery at 95..102, and the icon that carries the
            // full description at 79..86. Equipment slots 2..20 hold an artefact when the slot
            // draws a picture at all.
            var skills=new List<HeroSkill>();
            for(int i=0;i<8;i++)
            {
                var name=items.FirstOrDefault(e=>e.Id==87+i);
                if(name is null||string.IsNullOrWhiteSpace(name.Text))continue;
                skills.Add(new(name.Text.Trim(),items.FirstOrDefault(e=>e.Id==95+i)?.Text?.Trim()??"",$"id:{79+i}"));
            }
            var equipped=new List<HeroSlot>();
            // Worn cells are numbered two above the game's slot order; the five below the doll are
            // the visible part of the backpack.
            foreach(var slot in items.Where(e=>e.Id is >=2 and <=20&&e.Width==44&&e.Frame!=ExchangeArtifacts.Highlight))
                equipped.Add(new($"{ExchangeArtifacts.Slots[slot.Id-2]}: {GameReference.Artifact(slot.Frame)??$"артефакт с картинкой {slot.Frame}"}",$"id:{slot.Id}"));
            foreach(var slot in items.Where(e=>e.Id is >=40 and <=44&&e.Width==44&&e.Frame!=ExchangeArtifacts.Highlight))
                equipped.Add(new($"рюкзак: {GameReference.Artifact(slot.Frame)??$"артефакт с картинкой {slot.Frame}"}",$"id:{slot.Id}"));
            var inspect=new Dictionary<string,string>
            {
                ["боевой дух"]="id:116",["удача"]="id:117",["опыт и следующий уровень"]="id:119",
                ["мана"]="id:120",["специализация"]="id:118",
                ["атака"]="id:50",["защита"]="id:51",["сила магии"]="id:52",["знания"]="id:53",
            };
            for(int slot=0;slot<7;slot++)
                if(items.Any(e=>e.Id==54+slot&&e.Frame>0))inspect[$"отряд {slot+1}"]=$"id:{54+slot}";
            sheet=new(items.FirstOrDefault(e=>e.Id==1)?.Text?.Trim()??"",
                items.FirstOrDefault(e=>e.Id==140)?.Text?.Trim()??"",skills,equipped,
                GameReference.Specialty(items.FirstOrDefault(e=>e.Id==118)?.Frame??-1)??"id:118"){Inspect=inspect};
        }
        var build=screen=="building_confirmation"?ReadBuildOffer(items,towns.FirstOrDefault(t=>t.Id==openTown),resources):null;
        SideView? side=null;
        if(!frontend)
        {
            int active=game.I32(0x69ccf4);
            side=new(player,Colour(player),active,Colour(active),active==player);
            // Teams and who is human come from the map's own header and the record the game keeps
            // for every colour.
            uint main=game.U32(0x699538),info=main+0x1f86c;
            byte[] header=game.Read(info+0xc,0x14);
            bool teams=header[0]!=0;
            if(header[0]>1||header.Skip(1).Take(8).Any(t=>t>7))throw new InvalidOperationException("Map team layout unsupported");
            var allies=new List<int>();var participants=new List<string>();
            for(int colour=0;colour<8;colour++)
            {
                byte[] record=game.Read(main+0x20ad0+(uint)colour*0x168,0xe2);
                if(record[1]==0&&record[0x3e]==0)continue;
                bool ally=colour!=player&&teams&&header[1+colour]==header[1+player];
                if(ally)allies.Add(colour);
                string who=record[0xe1]!=0?"человек":"компьютер";
                string relation=colour==player?"это ты":ally?"союзник":"противник";
                participants.Add($"{Colour(colour)} — {who}"+(teams?$", команда {header[1+colour]+1}":"")+$", {relation}");
            }
            side=side with{Allies=allies.ToArray(),Participants=participants,Underground=header[0x10]!=0};
        }
        var result=new Observation("",player,date,resources,hero,screen,width,height,items){Towns=towns,Actions=actions,Setup=setup,Combat=combat,Saves=saves,Sheet=sheet,Build=build,Heroes=roster,Side=side,SelectedStack=selected,OpenTown=openTown,ForeignHero=foreignName,ForeignArmy=foreignArmy,ForeignHeroes=foreignHeroes,Brief=ScreenBriefing.Build(waiting?"waiting":screen,date,resources,towns,roster,hero,side,selected,build,openTown,foreignName,foreignArmy,foreignHeroes,items,combat,screen=="adventure"?SidebarHeroes(game,player):null)};
        return Revise(result);
    }
}
