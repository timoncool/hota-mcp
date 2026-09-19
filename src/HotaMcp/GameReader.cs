using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HotaMcp;

public record UiElement(string Key,int Id,string? Text,string? Asset,int X,int Y,int Width,int Height,bool Interactive);
public record HeroView(int Id,string Name,int[] Position,int Mana,int Movement,int MaxMovement,int[] Primary,int[] ArmyTypes,int[] ArmyCounts)
{
    public int[] PlannedDestination {get;init;}=[];
}
public record Observation(string Revision,int Player,int[] Date,int[] Resources,HeroView? Hero,string Screen,int Width,int Height,List<UiElement> Elements)
{
    public List<TownView> Towns {get;init;}=[];
    public List<AvailableAction> Actions {get;init;}=[];
    public ScenarioSetup? Setup {get;init;}
    public CombatView? Combat {get;init;}
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
        if(!frontend)
        {
        if(game.I32(0x69ccf4)!=player) throw new InvalidOperationException("Not this player's active context");
        uint main=game.U32(0x699538),p=main+0x20ad0+(uint)player*0x168;
        if(game.U32(0x69ccfc)!=p) throw new InvalidOperationException("Active player layout not validated");
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
        string screen=vtable switch {0x640c5c=>"recruitment",0x643990=>"tavern",0x63d46c=>"battle_result",0x641ddc=>"spellbook",0x63d528=>"combat",0x63db40=>"message",0x63ff60=>"main_menu",0x63e6d8=>"game_type",0x641cbc=>"scenario_selection",0x63a5e4=>"adventure",0x642478=>"system_options",0x64373c=>"town",0x6437b0=>"town_hall",0x643954=>"building_confirmation",_=>"unsupported"};
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
        var controls=new List<uint>();
        if(screen is "message" or "combat" or "spellbook" or "battle_result" or "tavern" or "recruitment")
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
            uint a=controls[controlIndex];byte[] b=game.Read(a,0x30);
            if(BitConverter.ToUInt32(b,4)!=dlg) throw new InvalidOperationException("UI changed while reading");
            ushort state=BitConverter.ToUInt16(b,0x16);
            if((state&4)==0) continue;
            int iw=BitConverter.ToUInt16(b,0x1c),ih=BitConverter.ToUInt16(b,0x1e);
            if(iw==0||ih==0) continue;
            uint vt=BitConverter.ToUInt32(b);string? text=null,asset=null;
            if(vt is 0x642dc0 or 0x642df8 or 0x642d50) text=game.Text(game.U32(a+0x34));
            if(vt is 0x63bb54 or 0x63bb88||(screen=="spellbook"||screen=="adventure")&&vt==0x63ec48) asset=game.Text(game.U32(a+0x30)+4,16);
            if(vt==0x63bb88)text=game.Text(game.U32(a+0x5c));
            bool interactive=(vt is 0x63bb54 or 0x63bb88||(screen=="spellbook"||screen=="adventure")&&vt==0x63ec48)&&(state&2)!=0&&(state&0x28)==0;
            if(string.IsNullOrEmpty(text)&&asset==null) continue;
            items.Add(new($"ui:{controlIndex}",BitConverter.ToUInt16(b,0x10),text,asset,
                dx+BitConverter.ToInt16(b,0x18),dy+BitConverter.ToInt16(b,0x1a),iw,ih,interactive));
        }
        if(screen=="scenario_selection"&&items.Any(i=>i.Id==186&&i.Asset=="scnrsav.def"))screen="save_game";
        var towns=frontend?new List<TownView>():new TownReader(game,player).Read();
        var actions=new List<AvailableAction>();
        if(screen=="message"&&items.Count(i=>i.Interactive)==1&&items.Any(i=>i.Id==30722&&i.Asset=="iokay.def"&&i.Interactive))actions.Add(new("message:accept","Подтвердить прочитанное сообщение"));
        if(screen=="message"&&items.Count(i=>i.Interactive)==2&&items.Any(i=>i.Id==30725&&i.Asset=="iokay.def"&&i.Interactive)&&items.Any(i=>i.Id==30726&&i.Asset=="icancel.def"&&i.Interactive))actions.Add(new("message:confirm","Согласиться с вопросом текущего диалога"));
        if(actions.Any(a=>a.Key=="message:confirm"))actions.Add(new("message:decline","Отказаться от действия в текущем диалоге"));
        if(screen=="main_menu")
        {
            if(items.Any(i=>i.Id==101&&i.Interactive))actions.Add(new("menu:new","Новая игра"));
            if(items.Any(i=>i.Id==102&&i.Interactive))actions.Add(new("menu:load","Загрузить игру"));
        }
        if(screen=="game_type"&&items.Any(i=>i.Id==104&&i.Interactive))actions.Add(new("menu:back","Главное меню"));
        if(screen=="game_type"&&items.Any(i=>i.Id==100&&i.Interactive))actions.Add(new("menu:single","Одиночная игра"));
        if(screen=="scenario_selection"&&items.Any(i=>i.Id==188&&i.Interactive))actions.Add(new("scenario:back","Выйти из выбора сценария"));
        if(screen=="scenario_selection"&&items.Any(i=>i.Id==186&&i.Interactive))actions.Add(new("scenario:start","Начать партию с текущими настройками"));
        if(screen=="scenario_selection")
        {
            foreach(var (id,key) in new[]{(128,"scenario:maps"),(129,"scenario:players"),(130,"scenario:random")})
                if(items.Any(i=>i.Id==id&&i.Interactive))actions.Add(new(key,items.Single(i=>i.Id==id).Text!));
        }
        if(screen=="adventure")foreach(var town in towns)actions.Add(new($"town:open:{town.Id}",$"Открыть город: {town.Name}"));
        if(screen=="adventure"&&items.Any(i=>i.Id==12&&i.Asset=="iam001.def"&&i.Interactive))actions.Add(new("turn:end","Закончить ход; игра может запросить подтверждение"));
        if(screen=="town")
        {
            int townId=game.Read(game.U32(game.U32(0x69954c)+0x38),1)[0];
            var currentTown=towns.Single(t=>t.Id==townId);
            if(currentTown.Buildings.Contains(5))actions.Add(new("town:tavern","Открыть таверну"));
            for(int level=0;level<7;level++)if(currentTown.Buildings.Contains(30+level))actions.Add(new($"town:recruit:{level}",$"Открыть найм существ уровня {level+1}"));
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
        if(screen=="save_game")actions.Add(new("save:confirm","Сохранить игру; может открыться запрос имени"));
        if(screen=="recruitment")
        {
            if(items.Any(i=>i.Id==532&&i.Interactive))actions.Add(new("recruit:max","Выбрать максимум доступных для найма существ"));
            if(items.Any(i=>i.Id==30722&&i.Interactive))actions.Add(new("recruit:buy","Нанять выбранное количество за указанную цену"));
            actions.Add(new("recruit:cancel","Отменить найм"));
        }
        if(screen=="system_options"&&items.Any(i=>i.Id==106&&i.Interactive))actions.Add(new("game:save","Открыть сохранение игры"));
        if(screen=="tavern")
        {
            if(items.Any(i=>i.Id==12&&i.Interactive))actions.Add(new("tavern:hire","Нанять выбранного героя за указанную цену"));
            actions.Add(new("tavern:close","Выйти из таверны"));
        }
        if(screen=="battle_result"&&items.Any(i=>i.Id==30722&&i.Interactive))actions.Add(new("battle:accept","Принять результат боя"));
        if(screen=="spellbook")
        {
            actions.Add(new("spellbook:close","Закрыть книгу"));
            foreach(var label in items.Where(i=>i.Id>=749&&i.Id<=772&&!string.IsNullOrEmpty(i.Text)))
                if(items.Any(i=>i.Id==label.Id-280&&i.Interactive))actions.Add(new($"spellbook:select:{label.Id-280}",label.Text!));
        }
        var setup=screen=="scenario_selection"?new ScenarioReader(game).Read(dlg,items):null;
        if(setup is not null)foreach(var choice in setup.Fields.SelectMany(f=>f.Choices).Where(c=>c.Enabled&&!c.Selected))actions.Add(new(choice.Action,choice.Label));
        var combat=screen=="combat"?new CombatReader(game,player).Read():null;
        if(combat?.OwnTurn==true)
        {
            if(items.Any(i=>i.Id==2005&&i.Text=="Выберите цель заклинания"))
            {
                foreach(var stack in combat.Stacks)actions.Add(new("spell:target:"+stack.Id,"Выбрать цель заклинания: "+stack.Name+"; допустимость проверяет игра"));
                actions.Add(new("spell:cancel","Отменить выбор цели"));
            }
            else
            {
            if(items.Any(i=>i.Id==2008&&i.Interactive))actions.Add(new("combat:spellbook","Открыть книгу заклинаний"));
            if(items.Any(i=>i.Id==2009&&i.Interactive))actions.Add(new("combat:wait","Ждать"));
            if(items.Any(i=>i.Id==2010&&i.Interactive))actions.Add(new("combat:defend","Защищаться"));
            foreach(int hex in combat.ReachableHexes)actions.Add(new($"combat:move:{hex}",$"Переместиться на клетку {hex}"));
            foreach(string id in combat.AttackableTargets)actions.Add(new("combat:attack:"+id,"Атаковать: "+combat.Stacks.Single(s=>s.Id==id).Name));
            }
        }
        var result=new Observation("",player,date,resources,hero,screen,width,height,items){Towns=towns,Actions=actions,Setup=setup,Combat=combat};
        string revision=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(epoch+JsonSerializer.Serialize(result))))[..24];
        return result with {Revision=revision};
    }
}
