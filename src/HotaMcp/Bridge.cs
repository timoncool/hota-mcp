using System.Text.Json;

namespace HotaMcp;

public sealed record OperationRequest(string OperationId,string Revision,string Element);
public sealed record OperationResult(string Status,string Message,Observation? Observation);
public sealed record JournalEntry(long Sequence,DateTimeOffset Time,string Kind,object Data);
public sealed record TargetView(string Id,string Kind,RouteView Route,int X=0,int Y=0,int Z=0);
public sealed record NearbyTargets(string Revision,int HeroId,int Movement,List<TargetView> Targets,string Coverage);
public sealed record DocsRequest(string Query,int Limit,string? Detail);
public sealed record ReferenceRequest(string Name,string? Kind,int Limit);
public sealed record TargetInspection(string Id,string Kind,RouteView Route,string Revision);
public sealed record DebugSnapshot(Observation Observation,CaptureResult Capture,string ObservationPath);
public sealed record MoveRequest(string OperationId,string Revision,string TargetId);
public sealed record TileMoveRequest(string OperationId,string Revision,int X,int Y,int Z);
public sealed record MapClickRequest(int X,int Y);
public sealed record TextRequest(string Revision,string Element,string Text);
public sealed record InspectRequest(string Revision,string Element);
public sealed record CellRequest(string Revision,int X,int Y,int Z);
public sealed record KeyRequest(int Key,int Scan,bool Control);
public sealed record PressRequest(int X,int Y);
public sealed record CellCard(int X,int Y,int Z,string[] Card,Observation Observation);
public sealed record ElementCard(string Element,string[] Card,Observation Observation);

internal sealed class Bridge(WindowsGame game,int player,string stateDirectory) : IDisposable
{
    private readonly GameReader reader=new(game,player);
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly Dictionary<string,(OperationRequest Request,OperationResult Result)> operations=new();
    private readonly List<JournalEntry> journal=[];
    /// The game day the plan was written on. A plan from an earlier day is not wrong, but it has
    /// not been reconciled with what happened since, and the difference is worth saying out loud.
    private int[] planDay=[];

    /// Half of what goes wrong in a long game is not a wrong decision but no decision: the same
    /// state read again and again while nothing moves. The bridge therefore keeps the last state
    /// that mattered — date, positions, movement, army, resources — and counts the observations
    /// that changed none of it.
    private string lastProgress="";
    private int idleReads;

    /// An environment an agent can act in needs three things: what it sees, what it can do, and
    /// whether it is getting anywhere. The first two are the observation and the actions; this is
    /// the third. Without it a turn feels the same whether the army doubled or the day was wasted.
    private (int[] Day,int Gold,int Army,int Towns) yesterday=([],0,0,0);
    /// Names of the towns this side held at the last observation. A town changes hands on the
    /// opponent's turn, between two of your own reads, and nothing on your screen says so.
    private List<string> heldTowns=[];

    private readonly Dictionary<string,MapObject> targets=new();
    private readonly Dictionary<MapObject,string> targetIds=new();
    private string? planCache;
    private string plan
    {
        get
        {
            if(planCache is null)
                try{planCache=File.Exists(PlanFile)?File.ReadAllText(PlanFile):"";}
                catch(IOException){planCache="";}
            return planCache;
        }
        set
        {
            planCache=value;
            try{File.WriteAllText(PlanFile,value);}catch(IOException){}
        }
    }
    /// The plan is the controller's memory across turns, and a turn outlives the process: the
    /// service is restarted on every install. Keeping it only in memory silently threw the goal
    /// away mid-game, so it lives in the session directory and is read back on start.
    private string PlanFile=>Path.Combine(
        Directory.GetParent(stateDirectory)?.Parent?.FullName??stateDirectory,$"plan-player{player}.txt");

    public void Dispose(){game.Dispose();gate.Dispose();}

    // ---------------------------------------------------------------- observation

    public async Task<Observation> Observe(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try{return WithMemory(reader.Observe());}
        finally{gate.Release();}
    }

    /// A turn is long and a controller's memory is not guaranteed to survive it. The plan and the
    /// last few results are therefore part of every observation, not something to be asked for:
    /// whoever reads the state also reads what the goal was and what already happened, so nothing
    /// is re-decided from scratch or done twice.
    private Observation WithMemory(Observation state)
    {
        var lines=new List<string>(state.Brief);
        string progress=string.Join("|",
            [string.Join(",",state.Date),string.Join(",",state.Resources),
             string.Join(";",state.Heroes.Select(h=>$"{h.Id}:{string.Join(",",h.Position)}:{h.Movement}:{string.Join(",",h.ArmyCounts)}")),
             string.Join(";",state.Towns.Select(t=>$"{t.Id}:{t.Buildings.Length}:{t.BuiltToday}"))]);
        if(progress==lastProgress)idleReads++;
        else {lastProgress=progress;idleReads=0;}
        var townsNow=state.Towns.Select(t=>t.Name??"безымянный").ToList();
        if(heldTowns.Count>0)
        {
            foreach(var lost in heldTowns.Except(townsNow))
                lines.Insert(0,$"ГОРОД ПОТЕРЯН: {lost} больше не твой. Его забрали на чужом ходу. "
                    +"Отбить город обычно важнее всего остального: он кормит и армию, и доход.");
            foreach(var gained in townsNow.Except(heldTowns))
                lines.Insert(0,$"Новый город: {gained} теперь твой.");
        }
        heldTowns=townsNow;
        int army=state.Heroes.Sum(h=>h.ArmyCounts.Sum())+state.Towns.Sum(t=>t.GarrisonCounts.Sum());
        int gold=state.Resources.Length>6?state.Resources[6]:0;
        if(state.Date.Length>0&&!yesterday.Day.SequenceEqual(state.Date))
        {
            if(yesterday.Day.Length>0)
                lines.Add($"За прошлый день: золото {Delta(gold-yesterday.Gold)}, войско {Delta(army-yesterday.Army)} существ, "
                    +$"городов {Delta(state.Towns.Count-yesterday.Towns)}. "
                    +"Если день не дал ничего из этого, он потрачен зря — сверься с планом.");
            yesterday=(state.Date,gold,army,state.Towns.Count);
        }
        if(idleReads>=6)
            lines.Add($"Топтание: {idleReads} наблюдений подряд без единого изменения — ни шага, ни ресурса, ни постройки. "
                +"Так теряется ход. Либо выполни активную задачу из плана, либо признай её невыполнимой сейчас, "
                +"запиши это в план и возьми следующую.");
        if(!string.IsNullOrWhiteSpace(plan))
        {
            lines.Add("Записанный план (tool plan, перепиши его в конце хода):");
            foreach(var line in plan.Split('\n').Select(l=>l.TrimEnd()).Where(l=>l.Length>0).Take(12))
                lines.Add("   "+line);
        }
        else lines.Add("План пуст. Запиши через plan цель партии и задачи — иначе следующий ход начнётся вслепую. "
            +"Форма: ЦЕЛЬ (одна строка, не меняется) / ЗАДАЧИ СЕЙЧАС (по герою, с координатами) / "
            +"СДЕЛАНО (одна строка на день) / ЗАПРЕТЫ / НЕ ВЗЯТО ПОБЛИЗОСТИ.");
        if(state.Date.Length>2&&state.Date[0]==1)
            lines.Add("Первый день недели — время разбора: сверь по плану, что из задач прошлой недели сделано, "
                +"что нет и почему, сожми прошлую неделю в одну строку СДЕЛАНО и поставь задачи на новую. "
                +"Сегодня же приходит прирост существ и обновляются недельные объекты.");
        if(!string.IsNullOrWhiteSpace(plan)&&state.Date.Length>0&&!planDay.SequenceEqual(state.Date))
            lines.Add($"План записан {(planDay.Length>2?$"в день {planDay[0]} недели {planDay[1]}":"раньше")}, "
                +"а сейчас другой день — перечитай его, выполни, и в конце хода перепиши: цель оставь дословно, "
                +"прошедший день сожми в одну строку СДЕЛАНО.");
        var recent=journal.Where(e=>e.Kind is "operation_completed" or "move_completed" or "battle_result"
                or "plan_updated" or "cell_inspected")
            .TakeLast(4)
            .Select(e=>$"   {e.Kind}: {Summarise(e)}").ToList();
        if(recent.Count>0)
        {
            lines.Add("Последнее, что делал этот контроллер (полностью — read_journal):");
            lines.AddRange(recent);
        }
        return state with {Brief=lines};
    }

    private static readonly System.Text.Json.JsonSerializerOptions Readable=new()
    {
        Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string Delta(int value)=>value>0?"+"+value:value.ToString();

    private static string Summarise(JournalEntry entry)
    {
        string text=System.Text.Json.JsonSerializer.Serialize(entry.Data,Readable);
        return text.Length>160?text[..160]+"…":text;
    }

    public object Status()=>new
    {
        phase="development",player,gamePid=game.Process.Id,
        build=new{game.Build.Status,game.Build.Summary,game.Build.Validated,game.Build.Checks},
        capabilities=new[]{"observe_own_hero","observe_adventure_ui","open_system_options","return_to_game",
            "visible_targets","route_preview","move_to_target","move_to_tile","own_towns","town_construction",
            "town_recruitment","tavern_hero","hero_exchange","combat_actions","battle_result","spellbook",
            "save_game","save_list_and_load","inspect_element","hero_switch_on_map","map_terrain",
            "game_reference","hotkeys","installer","launcher_tab"},
        unavailable=new[]{"full_map_coverage","in_game_load_browser","full_scenario_setup",
            "hotseat","lan","cost_measurement"},
        howToPlay=new[]{
            "Партия ведётся состояниями: осмотреться, экономика, накопление, штурм, сведение тиров, расширение, оборона. Активно одно, переход по условию.",
            "У каждого героя роль: главный берёт охраняемое и всю армию, сборщик — свободное и разведку, подвозчик возит прирост на фронт.",
            "День решается деревом по приоритету: угроза, постройка в городе, первый день недели, цель главного, ближайшее свободное для сборщика, остаток ходов, переписать план.",
            "Полностью — hota_docs(\"машина состояний партии\"); циклы игры — hota_docs(\"игровые циклы\"); решения по ситуациям — hota_docs(\"реестр ситуаций\").",
            "План возвращается в каждом observe и переписывается в конце хода: состояние, роли, активная задача со сроком, очередь, сделано одной строкой."}
    };

    public object Diagnostic()=>reader.DiagnosticPointers();
    public object RawUi()=>reader.DiagnosticDialog();
    public object ProbeScreen()=>reader.ProbeScreen();
    public object TileBytes(int x,int y,int z)=>new MapReader(game,player).TileBytes(reader.Observe(),x,y,z);
    public object MapDiagnostic()=>new MapReader(game,player).Diagnostic(reader.Observe());

    public async Task<MapView> ReadMap(int x,int y,int z,int radius,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var before=reader.Observe();
            var map=new MapReader(game,player);
            var first=map.Read(before,x,y,z,radius);
            var second=map.Read(before,x,y,z,radius);
            if(JsonSerializer.Serialize(first)!=JsonSerializer.Serialize(second)||reader.Observe().Revision!=before.Revision)
                throw new InvalidOperationException("Map changed while reading; observe again");
            return second;
        }
        finally{gate.Release();}
    }

    public async Task<NearbyTargets> Nearby(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var initial=reader.Observe();
            if(initial.Hero is null)throw new InvalidOperationException("Select a hero first");
            // The hover changes what the game reports under the cursor, so the consistency window
            // starts after it: otherwise this call always invalidates its own observation.
            var observation=initial;
            var hero=observation.Hero??throw new InvalidOperationException("Hero selection lost while refreshing routes");
            var region=new MapReader(game,player).Read(observation,hero.Position[0],hero.Position[1],hero.Position[2],12);
            var list=new List<TargetView>();
            foreach(var target in region.Objects)
            {
                if(!targetIds.TryGetValue(target,out var id))
                {
                    id="target_"+Guid.NewGuid().ToString("N")[..12];
                    targetIds.Add(target,id);
                    targets.Add(id,target);
                }
                // A player reads the flag before anything else: whose town, whose hero. The tile
                // carries the object's own index, so a town id or a hero id compared against what
                // this player owns says it exactly, without guessing at an owner byte.
                string kind=target.Kind;
                if(target.Type==98)
                    kind=observation.Towns.Any(t=>t.Id==target.Id)?"свой город":"ЧУЖОЙ ГОРОД";
                if(target.Type==34)
                    kind=observation.Heroes.Any(h=>h.Id==target.Id)?"свой герой":"ЧУЖОЙ ГЕРОЙ";
                list.Add(new(id,kind,new RouteReader(game,player).Read(observation,target),target.X,target.Y,target.Z));
            }
            var settled=reader.Observe();
            if(settled.Hero is null||settled.Screen!="adventure")
                throw new InvalidOperationException("State changed; request targets again");
            return new(settled.Revision,hero.Id,hero.Movement,list,
                "Посещён ли объект выбранным сейчас героем — спроси inspect_cell по его клетке: игра сама пишет "
                +"в карточке «(Посещено)», и статус этот свой у каждого героя. "
                +"Recognized visible objects near selected hero; list is not exhaustive. Route data is "
                +"whatever the game had cached and any action invalidates it — inspect_target computes "
                +"the route for one destination you choose.");
        }
        finally{gate.Release();}
    }

    public async Task<TargetInspection> InspectTarget(string targetId,string revision,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if(!targets.TryGetValue(targetId,out var target))
                throw new InvalidOperationException("Unknown target; request nearby_targets first");
            var stale=reader.Observe();
            if(stale.Revision!=revision)throw new InvalidOperationException("State changed; request nearby_targets again");
            var map=new MapReader(game,player);
            map.ValidateTarget(stale,target);
            var before=await PlanRouteTo(stale,target.X,target.Y,target.Z);
            var route=new RouteReader(game,player).Read(before,target);
            var after=reader.Observe();
            if(after.Revision!=before.Revision)throw new InvalidOperationException("State changed while reading target");
            // Walking into somebody else's town or onto his hero is a battle, but the route is
            // computed as if the cell were empty and nothing on the map shows a stack. Saying it
            // here, where the decision to go is made, is the difference between a siege and a
            // scout walking to his death.
            string kind=target.Kind;
            if(target.Type==98&&!after.Towns.Any(t=>t.Id==target.Id))
                kind+=" — вход в чужой город это штурм его гарнизона, состав которого не виден; "
                    +"сначала посмотри, кто там, и приходи силой";
            if(target.Type==34&&!after.Heroes.Any(h=>h.Id==target.Id))
                kind+=" — шаг на чужого героя это бой с его армией; "
                    +"его состав показывает карточка героя по правому щелчку";
            return new(targetId,kind,route,after.Revision);
        }
        finally{gate.Release();}
    }

    public async Task<TileInspection> InspectTile(int x,int y,int z,string revision,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var before=reader.Observe();
            if(before.Revision!=revision)throw new InvalidOperationException("Observation is stale; observe again");
            var map=new MapReader(game,player);
            // The camera follows the selected hero, so a cell the player knows about is often off
            // screen. A player brings it into view by pressing the minimap; the bridge does the
            // same instead of refusing to look. The press moves the camera only.
            if(!map.IsOnScreen(before,x,y,z))
            {
                var onMinimap=map.MinimapPoint(before,x,y,z);
                await game.MouseAsync(onMinimap.X,onMinimap.Y,before.Width,before.Height,true,ct);
                await Task.Delay(250,ct);
                before=reader.Observe();
                if(!map.IsOnScreen(before,x,y,z))
                    throw new InvalidOperationException(
                        "Клетка не попала в окно даже после доводки камеры по миникарте");
            }
            var point=map.ScreenPoint(before,x,y,z);
            await game.MouseAsync(point.X,point.Y,before.Width,before.Height,false,ct);
            await Task.Delay(150,ct);
            map.VerifyMouse(x,y,z);
            var after=reader.Observe();
            if(after.Screen!="adventure")throw new InvalidOperationException("Screen changed during inspection");
            return new(x,y,z,after.Elements.SingleOrDefault(e=>e.Id==200)?.Text,after);
        }
        finally{gate.Release();}
    }

    /// The player's own way to look at a map cell before walking into it: hold the right button
    /// over it and the game names what stands there and roughly how many. Nothing is entered and
    /// no fight starts. The cell must be on screen, exactly as it must be for a player.
    public async Task<CellCard> InspectCell(CellRequest request,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var before=reader.Observe();
            if(before.Revision!=request.Revision)throw new InvalidOperationException("Observation is stale; observe again");
            if(before.Screen!="adventure")throw new InvalidOperationException("Adventure map required");
            var map=new MapReader(game,player);
            var point=map.ScreenPoint(before,request.X,request.Y,request.Z);
            await game.RightMouseDownAsync(point.X,point.Y,before.Width,before.Height,ct);
            GameReader.CardView card;
            try
            {
                await Task.Delay(350,CancellationToken.None);
                card=reader.ReadCard();
                Record("cell_inspected",new{request.X,request.Y,request.Z,card.Texts});
            }
            finally{await game.RightMouseUpAsync();}
            await Task.Delay(150,CancellationToken.None);
            var after=reader.Observe();
            if(after.Screen!="adventure")throw new InvalidOperationException("The map reacted instead of showing a card; observe again");
            return new(request.X,request.Y,request.Z,card.Texts,after);
        }
        finally{gate.Release();}
    }

    /// Holds the right mouse button on a published control so the game shows its ordinary info
    /// card, reads the card, then releases. This is the player's own way to read creature stats,
    /// skill texts and artefact descriptions without acting on the control.
    public async Task<ElementCard> InspectElement(InspectRequest request,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var before=reader.Observe();
            if(before.Revision!=request.Revision)throw new InvalidOperationException("Observation is stale; observe again");
            // Any cell on screen can be looked at, not only the ones the observation lists: an
            // element key works, and so does "id:<number>" for a control the observation leaves
            // out to stay compact.
            int centreX,centreY;
            var element=before.Elements.SingleOrDefault(e=>e.Key==request.Element);
            if(element is not null)(centreX,centreY)=(element.X+element.Width/2,element.Y+element.Height/2);
            else if(request.Element.StartsWith("id:")&&int.TryParse(request.Element[3..],out int wanted))
            {
                var box=reader.FindControlById(wanted)
                    ??throw new InvalidOperationException($"No control with id {wanted} on this screen");
                (centreX,centreY)=(box.X+box.Width/2,box.Y+box.Height/2);
            }
            else throw new InvalidOperationException("Unknown control; observe again, or address it as id:<number>");
            await game.RightMouseDownAsync(centreX,centreY,before.Width,before.Height,ct);
            GameReader.CardView card;
            try
            {
                await Task.Delay(350,CancellationToken.None);
                card=reader.ReadCard();
                Record("element_inspected",new{request.Element,card.Vtable,card.Texts});
            }
            finally{await game.RightMouseUpAsync();}
            await Task.Delay(150,CancellationToken.None);
            var after=reader.Observe();
            if(after.Screen!=before.Screen)
                throw new InvalidOperationException("The control reacted instead of showing a card; observe again");
            return new(request.Element,card.Texts,after);
        }
        finally{gate.Release();}
    }

    // ---------------------------------------------------------------- actions

    public async Task<OperationResult> Click(OperationRequest request,CancellationToken ct)
    {
        RequireOperationId(request.OperationId);
        await gate.WaitAsync(ct);
        try
        {
            if(operations.TryGetValue(request.OperationId,out var previous))
            {
                if(previous.Request!=request)throw new InvalidOperationException("Operation ID reused with different arguments");
                return previous.Result;
            }
            var before=reader.Observe();
            if(before.Revision!=request.Revision)throw new InvalidOperationException("Observation is stale; observe again before acting");
            var action=before.Actions.SingleOrDefault(a=>a.Key==request.Element);
            var command=action is not null
                ?GameCommands.ForAction(action,before)
                :GameCommands.ForElement(before,request.Element);
            var pending=new OperationResult("uncertain","Dispatch started; do not repeat using a new ID",null);
            operations.Add(request.OperationId,(request,pending));
            Record("operation_started",request);
            await command.Deliver(new CommandContext(game,reader,player,before,request.Element),ct);
            return await AwaitResult(request,before,command,pending);
        }
        finally{gate.Release();}
    }

    /// Waits for the game itself to show the action happened. A screen that merely looks right is
    /// not evidence: the observed revision must change, and every extra Confirm flag must hold.
    private async Task<OperationResult> AwaitResult(OperationRequest request,Observation before,GameCommand command,OperationResult pending)
    {
        var deadline=DateTime.UtcNow.AddSeconds(command.TimeoutSeconds);
        while(DateTime.UtcNow<deadline)
        {
            await Task.Delay(70,CancellationToken.None);
            Observation? after=null;
            try{after=reader.Observe();}
            catch(InvalidOperationException){}
            if(after is null)continue;
            if(command.BattleMayEnd&&after.Screen=="battle_result")
            {
                var finished=new OperationResult("completed","Battle result screen confirmed; read and accept the result",after);
                operations[request.OperationId]=(request,finished);
                Record("battle_finished",new{request.OperationId,after});
                return finished;
            }
            if(!Confirmed(command.Confirm,request.Element,before,after))continue;
            bool screenChanged=after.Screen!=before.Screen&&after.Revision!=before.Revision;
            bool sameScreenReset=after.Screen==before.Screen&&after.Revision!=before.Revision;
            // Ending a turn legitimately lands on the game's own question instead of the map.
            bool landed=command.Accepts(after.Screen)||command.Confirm.HasFlag(Confirm.TurnAdvanced)&&after.Screen=="message";
            if(!(screenChanged&&landed||sameScreenReset))continue;
            if(after.Combat is not null&&before.Combat is not null)
                Record("combat_action_evidence",new{request.OperationId,Action=request.Element,
                    BeforeLogCount=before.Combat.LogCount,AfterLogCount=after.Combat.LogCount,
                    Entries=after.Combat.Log.Where(e=>e.Index>=before.Combat.LogCount).ToArray()});
            var result=new OperationResult("completed",
                (screenChanged?"Screen transition confirmed by revision change":"Same screen, state change confirmed by revision")
                +(command.Confirm.HasFlag(Confirm.GarrisonChanged)?ArmyChange(before,after):""),after);
            operations[request.OperationId]=(request,result);
            Record("operation_completed",new{request.OperationId,after.Revision,after.Screen,BeforeScreen=before.Screen});
            return result;
        }
        Record("operation_uncertain",new{request.OperationId});
        return pending;
    }

    /// Everything this side holds in troops, in one comparable string: every hero's army and every
    /// town's garrison. Two observations with the same signature hold the same troops in the same
    /// places, whatever the screen did.
    private static string ArmySignature(Observation state)=>string.Join("|",
        state.Heroes.OrderBy(h=>h.Id).Select(h=>$"h{h.Id}:"+string.Join(",",h.ArmyTypes.Zip(h.ArmyCounts).Select(s=>$"{s.First}x{s.Second}")))
        .Concat(state.Towns.OrderBy(t=>t.Id).Select(t=>$"t{t.Id}:"+string.Join(",",t.GarrisonTypes.Zip(t.GarrisonCounts).Select(s=>$"{s.First}x{s.Second}")))));

    /// What actually happened to the troops, in the words a player would use. The agent asked for
    /// a merge; if the game instead swapped two stacks or split one, it is told so plainly rather
    /// than being left to infer it from a later observation.
    private static string ArmyChange(Observation before,Observation after)
    {
        var lines=new List<string>();
        foreach(var now in after.Heroes)
        {
            var was=before.Heroes.FirstOrDefault(h=>h.Id==now.Id);
            if(was is null)continue;
            string oldArmy=string.Join(", ",was.Army),newArmy=string.Join(", ",now.Army);
            if(oldArmy!=newArmy)lines.Add($"{now.Name}: было [{oldArmy}] стало [{newArmy}]");
        }
        foreach(var now in after.Towns)
        {
            var was=before.Towns.FirstOrDefault(t=>t.Id==now.Id);
            if(was is null)continue;
            string oldArmy=string.Join(", ",was.Garrison),newArmy=string.Join(", ",now.Garrison);
            if(oldArmy!=newArmy)lines.Add($"гарнизон {now.Name}: было [{oldArmy}] стало [{newArmy}]");
        }
        return lines.Count==0?"":" Войска: "+string.Join("; ",lines)+".";
    }

    private static bool Confirmed(Confirm confirm,string element,Observation before,Observation after)
    {
        if(confirm.HasFlag(Confirm.SetupChoice)
            &&after.Setup?.Fields.SelectMany(f=>f.Choices).Any(c=>c.Action==element&&c.Selected)!=true)return false;
        if(confirm.HasFlag(Confirm.TurnAdvanced)
            &&after.Screen!="message"&&after.Date.SequenceEqual(before.Date))return false;
        if(confirm.HasFlag(Confirm.CombatLog)
            &&!(after.Combat is not null&&after.Combat.LogCount>before.Combat!.LogCount))return false;
        if(confirm.HasFlag(Confirm.ManaSpent)&&!(after.Hero?.Mana<before.Hero?.Mana))return false;
        if(confirm.HasFlag(Confirm.CombatTurn)
            &&!(after.Combat?.OwnTurn==true
                &&(after.Combat.ActiveStack!=before.Combat?.ActiveStack||after.Combat.Round!=before.Combat?.Round)))return false;
        if(confirm.HasFlag(Confirm.PartyLoaded)&&!(after.Hero is not null&&after.Date.Length>0))return false;
        // Handing a stack over must actually change the armies of this side. Counting only the
        // town's own garrison was not enough: when a garrison hero stands in the town the upper
        // row is HIS army and the town garrison stays empty, so a real transfer looked like
        // nothing happening. Pressing a slot only selects it, and selection alone already changes
        // the observed revision, so the revision is not evidence.
        if(confirm.HasFlag(Confirm.GarrisonChanged)&&ArmySignature(before)==ArmySignature(after))return false;
        // A step counts only when the hero is somewhere else or paid movement for it; a dialog
        // opening on the way is the move having happened too.
        if(confirm.HasFlag(Confirm.HeroMoved)&&after.Screen=="adventure"
            &&!(after.Hero is not null&&before.Hero is not null
                &&(!after.Hero.Position.SequenceEqual(before.Hero.Position)||after.Hero.Movement<before.Hero.Movement)))return false;
        return true;
    }

    /// Developer mapping only: presses one point of the game surface, so a screen the adapter has
    /// no actions for yet can still be left. Not part of the agent surface.
    public async Task<object> Press(PressRequest request,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var probe=reader.ProbeScreen();
            uint manager=game.U32(0x6992d0),surface=game.U32(manager+0x40);
            int width=game.I32(surface+0x24),height=game.I32(surface+0x28);
            if(request.X<0||request.Y<0||request.X>=width||request.Y>=height)
                throw new InvalidOperationException("Point is outside the game surface");
            await game.MouseAsync(request.X,request.Y,width,height,true,CancellationToken.None);
            await Task.Delay(300,CancellationToken.None);
            Record("developer_press",new{request.X,request.Y,From=probe.Vtable});
            return new{pressed=true,request.X,request.Y};
        }
        finally{gate.Release();}
    }

    /// Developer mapping only: sends one ordinary key to the game so an unmapped screen can be
    /// opened and identified. Not part of the agent surface — every playable action has its own
    /// semantic key, and this one carries no precondition or postcondition checks.
    public async Task<object> SendKey(KeyRequest request,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if(request.Key is <0 or >255||request.Scan is <0 or >255)
                throw new InvalidOperationException("Key and scan code must be single bytes");
            if(request.Control)await game.KeyWithControlAsync((ushort)request.Key,(ushort)request.Scan);
            else await game.KeyAsync((ushort)request.Key,(ushort)request.Scan);
            Record("developer_key",new{request.Key,request.Scan,request.Control});
            return new{sent=true,request.Key,request.Scan,request.Control};
        }
        finally{gate.Release();}
    }

    public async Task<OperationResult> EnterText(TextRequest request,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if(string.IsNullOrEmpty(request.Text)||request.Text.Length>64
                ||request.Text.Any(c=>c<32||c=='\\'||c=='/'||c==':'||c=='*'||c=='?'||c=='"'||c=='<'||c=='>'||c=='|'))
                throw new InvalidOperationException("Text must be 1-64 characters without path separators");
            var before=reader.Observe();
            if(before.Revision!=request.Revision)throw new InvalidOperationException("Observation is stale; observe again before acting");
            var field=before.Elements.SingleOrDefault(e=>e.Key==request.Element)
                ??throw new InvalidOperationException("Unknown edit control; observe again");
            // Focus the ordinary edit control with a window mouse event, then type characters.
            await game.MouseAsync(field.X+field.Width/2,field.Y+field.Height/2,before.Width,before.Height,true,CancellationToken.None);
            await Task.Delay(200,CancellationToken.None);
            await game.TextAsync(request.Text);
            await Task.Delay(200,CancellationToken.None);
            var after=reader.Observe();
            if(after.Screen!=before.Screen)throw new InvalidOperationException("Screen changed while entering text");
            Record("text_entered",new{request.Element,Length=request.Text.Length,Screen=before.Screen});
            return new("completed","Text entered into the addressed edit control; read it back before confirming",after);
        }
        finally{gate.Release();}
    }

    // ---------------------------------------------------------------- movement

    public Task<OperationResult> Move(MoveRequest request,CancellationToken ct)=>MoveCore(request,false,ct);
    public Task<OperationResult> Attack(MoveRequest request,CancellationToken ct)=>MoveCore(request,true,ct);

    public async Task<OperationResult> MapClick(MapClickRequest request,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var before=reader.Observe();
            if(before.Screen!="adventure")throw new InvalidOperationException("Adventure map required");
            if(request.X<0||request.Y<0||request.X>=before.Width||request.Y>=before.Height)
                throw new InvalidOperationException("Point is outside the game surface");
            await game.MouseAsync(request.X,request.Y,before.Width,before.Height,true,CancellationToken.None);
            await Task.Delay(400,CancellationToken.None);
            Record("map_click",request);
            return new("completed","Map point clicked",null);
        }
        finally{gate.Release();}
    }

    private async Task<OperationResult> MoveCore(MoveRequest request,bool attack,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            RequireOperationId(request.OperationId);
            var identity=new OperationRequest(request.OperationId,request.Revision,(attack?"attack:":"move:")+request.TargetId);
            if(operations.TryGetValue(request.OperationId,out var prior))
            {
                if(prior.Request!=identity)throw new InvalidOperationException("Operation ID reused with different arguments");
                return prior.Result;
            }
            if(!targets.TryGetValue(request.TargetId,out var target))throw new InvalidOperationException("Request nearby_targets first");
            var before=RequireOwnHeroOnMap(request.Revision);
            var map=new MapReader(game,player);
            map.ValidateTarget(before,target);
            if(!attack&&string.Equals(target.Kind,"creatures",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Danger: this cell holds a creature stack, and moving onto it starts a battle. Approach a neighbouring cell with move_to_tile, or use attack_target to fight deliberately");
            int[] destination=[target.X,target.Y,target.Z];
            return await RunMove(request.OperationId,identity,before,destination,10,"move",
                planned=>map.ValidateTarget(planned,target),
                after=>
                {
                    bool collected=after.Screen=="adventure"&&target.Kind is "resource" or "campfire"
                        &&after.Hero?.Id==before.Hero!.Id&&after.Hero.Movement<before.Hero.Movement
                        &&!map.IsTargetPresent(after,target)
                        &&after.Resources.Where((value,index)=>value>before.Resources[index]).Any();
                    if(collected)return "Target collected; object disappeared and resources increased";
                    if(after.Hero?.Position.SequenceEqual(destination)==true)return "Hero reached target cell";
                    if(after.Screen!="adventure")return "Movement opened an interaction; read the dialog";
                    return null;
                },
                after=>after.Hero?.Id==before.Hero!.Id&&after.Hero.Movement<before.Hero.Movement
                    &&!after.Hero.Position.SequenceEqual(before.Hero.Position)
                    ?"Hero stopped short of the target":null);
        }
        finally{gate.Release();}
    }

    public async Task<OperationResult> MoveToTile(TileMoveRequest request,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            RequireOperationId(request.OperationId);
            var identity=new OperationRequest(request.OperationId,request.Revision,$"move-tile:{request.X},{request.Y},{request.Z}");
            if(operations.TryGetValue(request.OperationId,out var prior))
            {
                if(prior.Request!=identity)throw new InvalidOperationException("Operation ID reused with different arguments");
                return prior.Result;
            }
            var before=RequireOwnHeroOnMap(request.Revision);
            if(request.X<0||request.Y<0||request.X>255||request.Y>255||request.Z<0||request.Z>1)
                throw new InvalidOperationException("Cell outside supported map bounds");
            int[] destination=[request.X,request.Y,request.Z];
            RefuseCreatureCell(before,destination);
            return await RunMove(request.OperationId,identity,before,destination,12,"move_tile",null,
                after=>
                {
                    if(after.Hero?.Position.SequenceEqual(destination)==true)return "Hero reached the commanded cell";
                    if(after.Screen!="adventure")return "Movement opened an interaction; read the dialog";
                    return null;
                },
                after=>after.Hero?.Id==before.Hero!.Id&&after.Hero.Movement<before.Hero.Movement
                    &&!after.Hero.Position.SequenceEqual(before.Hero.Position)
                    ?"Hero stopped short of the commanded cell":null);
        }
        finally{gate.Release();}
    }

    /// Plans the route with the game's own map-selection handler, sends the hero along it with the
    /// ordinary M command, then waits until the game itself shows the move happened.
    private async Task<OperationResult> RunMove(string operationId,OperationRequest identity,Observation before,
        int[] destination,int timeoutSeconds,string journal,Action<Observation>? verifyPlanned,
        Func<Observation,string?> finished,Func<Observation,string?> stopped)
    {
        var pending=new OperationResult("uncertain","Movement preparation started; inspect state before any retry with a new ID",null);
        operations.Add(operationId,(identity,pending));
        Record(journal+"_started",identity);
        if(!before.Hero!.PlannedDestination.SequenceEqual(destination))
            game.NativeAction(31,player,destination[0]|(destination[1]<<8)|(destination[2]<<16));
        Observation? planned=null;
        for(int attempt=0;attempt<20;attempt++)
        {
            await Task.Delay(50,CancellationToken.None);
            try{planned=reader.Observe();}
            catch(InvalidOperationException){continue;}
            if(planned.Hero?.Id==before.Hero.Id&&planned.Hero.PlannedDestination.SequenceEqual(destination))break;
        }
        if(planned?.Hero?.Id!=before.Hero.Id||!planned.Hero.PlannedDestination.SequenceEqual(destination))
        {
            Record(journal+"_preparation_unconfirmed",new{operationId,planned});
            return pending;
        }
        verifyPlanned?.Invoke(planned);
        if(planned.Hero.Movement!=before.Hero.Movement||!planned.Hero.Position.SequenceEqual(before.Hero.Position))
            throw new InvalidOperationException("Hero changed during route preparation");
        // M is the game's ordinary move-along-selected-path command.
        await game.KeyAsync(0x4d,0x32);
        var deadline=DateTime.UtcNow.AddSeconds(timeoutSeconds);
        Observation? resting=null;
        int still=0;
        while(DateTime.UtcNow<deadline)
        {
            await Task.Delay(100,CancellationToken.None);
            Observation after;
            try{after=reader.Observe();}
            catch(InvalidOperationException){continue;}
            string? message=finished(after);
            // Pressing M walks the whole planned path. Reporting on the first step would end the
            // command while the hero is still walking, so a hero that moved but is not at the
            // destination is only reported once it has stood still for a while: the game stops a
            // hero short when the movement of the day runs out or something blocks the way.
            if(message is null)
            {
                if(resting is not null&&after.Hero?.Position.SequenceEqual(resting.Hero!.Position)==true
                    &&after.Hero.Movement==resting.Hero.Movement)still++;
                else{resting=after;still=0;}
                if(still<4)continue;
                message=stopped(after);
                if(message is null)continue;
            }
            var result=new OperationResult("completed",message,after);
            operations[operationId]=(identity,result);
            Record(journal+"_completed",new{operationId,result});
            return result;
        }
        Record(journal+"_uncertain",identity);
        return pending;
    }

    /// Stepping onto a creature stack starts a battle; that must be a deliberate attack, never a
    /// side effect of an exploration move.
    private void RefuseCreatureCell(Observation before,int[] cell)
    {
        try
        {
            var look=new MapReader(game,player).Read(before,cell[0],cell[1],cell[2],1);
            bool creature=look.Objects.Any(o=>o.X==cell[0]&&o.Y==cell[1]&&o.Z==cell[2]
                &&string.Equals(o.Kind,"creatures",StringComparison.OrdinalIgnoreCase));
            if(creature)
                throw new InvalidOperationException("Danger: this cell holds a creature stack, and stepping there starts a battle. Approach a neighbouring cell instead, or use attack_target when the fight is intended");
        }
        catch(InvalidOperationException e)when(!e.Message.StartsWith("Danger:")){}
    }

    /// Brings a cell into view the way a player does: a press on the sidebar minimap. The game
    /// plans routes and accepts map targets only inside the visible part of the map, so anything
    /// further away is unreachable until the camera is moved.
    private async Task<Observation> EnsureVisible(Observation observation,int x,int y,int z)
    {
        var map=new MapReader(game,player);
        if(map.IsOnScreen(observation,x,y,z))return observation;
        var point=map.MinimapPoint(observation,x,y,z);
        await game.MouseAsync(point.X,point.Y,observation.Width,observation.Height,true,CancellationToken.None);
        await Task.Delay(300,CancellationToken.None);
        var moved=reader.Observe();
        if(!map.IsOnScreen(moved,x,y,z))
            throw new InvalidOperationException($"The camera did not reach ({x},{y},{z}); the cell stays outside the visible map");
        Record("view_centered",new{x,y,z});
        return moved;
    }

    /// After the hero's movement changes, the game's route cache still describes the hero as it
    /// was. Moving the cursor does not rebuild it: the rebuild belongs to the game's own map
    /// selection handler, the one a player triggers by pointing at a destination. Asking it for
    /// this destination is therefore what makes the route to it exist; it plans, it does not move.
    /// The hero's own cell is never asked for — selecting it opens the hero screen instead.
    private async Task<Observation> PlanRouteTo(Observation observation,int x,int y,int z)
    {
        var map=new MapReader(game,player);
        if(observation.Screen!="adventure"||observation.Hero is null)return observation;
        int[] here=observation.Hero.Position;
        if(here[0]==x&&here[1]==y&&here[2]==z)return observation;
        if(!map.RoutesAreStale(observation)&&observation.Hero.PlannedDestination.SequenceEqual(new[]{x,y,z}))
            return observation;
        game.NativeAction(31,player,x|(y<<8)|(z<<16));
        for(int attempt=0;attempt<20;attempt++)
        {
            await Task.Delay(50,CancellationToken.None);
            Observation current;
            try{current=reader.Observe();}
            catch(InvalidOperationException){continue;}
            if(current.Screen!="adventure")return current;
            if(!map.RoutesAreStale(current)&&current.Hero?.PlannedDestination.SequenceEqual(new[]{x,y,z})==true)
                return current;
        }
        return reader.Observe();
    }

    private async Task HoverAndSettle(Observation observation,int x,int y)
    {
        await game.MouseAsync(x,y,observation.Width,observation.Height,false,CancellationToken.None);
        await Task.Delay(150,CancellationToken.None);
    }

    private Observation RequireOwnHeroOnMap(string revision)
    {
        var before=reader.Observe();
        if(before.Revision!=revision||before.Screen!="adventure"||before.Hero is null)
            throw new InvalidOperationException("Fresh own-hero adventure observation required");
        return before;
    }

    private static void RequireOperationId(string operationId)
    {
        if(string.IsNullOrWhiteSpace(operationId)||operationId.Length>100)
            throw new InvalidOperationException("Provide a unique operation ID of at most 100 characters");
    }

    // ---------------------------------------------------------------- diagnostics, journal, plan

    public async Task<DebugSnapshot> Snapshot(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var before=reader.Observe();
            var capture=DebugCapture.Save(game,player,Path.Combine(stateDirectory,"captures"));
            var after=reader.Observe();
            if(before.Revision!=after.Revision)
                throw new InvalidOperationException("State changed during diagnostic capture; snapshot not confirmed");
            string path=Path.ChangeExtension(capture.Path,"json");
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(before,new JsonSerializerOptions{WriteIndented=true}),ct);
            return new(before,capture,path);
        }
        finally{gate.Release();}
    }

    public async Task<CaptureResult> Capture(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try{return DebugCapture.Save(game,player,Path.Combine(stateDirectory,"captures"));}
        finally{gate.Release();}
    }

    public async Task<object> GetJournal(int limit,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try{return journal.TakeLast(Math.Clamp(limit,1,100)).ToArray();}
        finally{gate.Release();}
    }

    public async Task<object> Plan(string? value,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if(value!=null)
            {
                if(value.Length>12000)throw new InvalidOperationException("Plan too large");
                plan=value;
                try{planDay=reader.Observe().Date;}catch(InvalidOperationException){planDay=[];}
                Record("plan_updated",new{plan});
            }
            return new{plan,player};
        }
        finally{gate.Release();}
    }

    /// The journal answers "what happened a turn ago", so it carries the game's own date and one
    /// compact line per event. Embedding a whole observation in every entry made it unreadable and
    /// expensive: the state belongs in observe, the history belongs here.
    private void Record(string kind,object data)
    {
        Directory.CreateDirectory(stateDirectory);
        var node=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(data));
        Strip(node);
        int[] day=[];
        try{day=reader.Observe().Date;}catch{}
        var entry=new JournalEntry(journal.Count+1,DateTimeOffset.UtcNow,kind,new{day,detail=node});
        File.AppendAllText(Path.Combine(stateDirectory,"journal.jsonl"),JsonSerializer.Serialize(entry)+"\n");
        journal.Add(entry);
    }

    /// Drops the observation snapshots that actions carry back, keeping the few fields that say
    /// what the action actually achieved.
    private static void Strip(System.Text.Json.Nodes.JsonNode? node)
    {
        if(node is System.Text.Json.Nodes.JsonObject map)
        {
            foreach(var key in map.Select(pair=>pair.Key).ToArray())
            {
                if(string.Equals(key,"observation",StringComparison.OrdinalIgnoreCase))
                {
                    var observation=map[key];
                    var hero=observation?["hero"];
                    map[key]=new System.Text.Json.Nodes.JsonObject
                    {
                        ["screen"]=observation?["screen"]?.DeepClone(),
                        ["hero"]=hero?["name"]?.DeepClone(),
                        ["position"]=hero?["position"]?.DeepClone(),
                        ["movement"]=hero?["movement"]?.DeepClone(),
                    };
                    continue;
                }
                if(string.Equals(key,"elements",StringComparison.OrdinalIgnoreCase)
                   ||string.Equals(key,"actions",StringComparison.OrdinalIgnoreCase)){map.Remove(key);continue;}
                Strip(map[key]);
            }
        }
        else if(node is System.Text.Json.Nodes.JsonArray list)
            foreach(var item in list)Strip(item);
    }
}

public interface IGameEndpoint
{
    Task<OperationResult> Move(MoveRequest request,CancellationToken ct);
    Task<OperationResult> Attack(MoveRequest request,CancellationToken ct);
    Task<OperationResult> MapClick(MapClickRequest request,CancellationToken ct);
    Task<OperationResult> MoveToTile(TileMoveRequest request,CancellationToken ct);
    Task<DebugSnapshot> Snapshot(CancellationToken ct);
    Task<object> Start(CancellationToken ct);
    Task<object> Graphics(string? renderer,CancellationToken ct);
    Task<CaptureResult> Capture(CancellationToken ct);
    Task<object> Status(CancellationToken ct);
    Task<Observation> Observe(CancellationToken ct);
    Task<OperationResult> Click(OperationRequest request,CancellationToken ct);
    Task<OperationResult> EnterText(TextRequest request,CancellationToken ct);
    Task<ElementCard> InspectElement(InspectRequest request,CancellationToken ct);
    Task<CellCard> InspectCell(CellRequest request,CancellationToken ct);
    Task<object> ProbeScreen(CancellationToken ct);
    Task<object> SendKey(KeyRequest request,CancellationToken ct);
    Task<object> Press(PressRequest request,CancellationToken ct);
    Task<object> Journal(int limit,CancellationToken ct);
    Task<object> Plan(string? value,CancellationToken ct);
    Task<MapView> ReadMap(int x,int y,int z,int radius,CancellationToken ct);
    Task<TileInspection> InspectTile(int x,int y,int z,string revision,CancellationToken ct);
    Task<NearbyTargets> Nearby(CancellationToken ct);
    Task<DocsAnswer> Docs(DocsRequest request,CancellationToken ct);
    Task<DocsCatalog> DocsCatalog(string? path,CancellationToken ct);
    Task<DocText> DocsRead(string path,string? heading,int offset,int maxChars,CancellationToken ct);
    Task<ReferenceAnswer> Reference(ReferenceRequest request,CancellationToken ct);
    Task<TargetInspection> InspectTarget(string targetId,string revision,CancellationToken ct);
}

internal sealed class LocalEndpoint(Bridge bridge) : IGameEndpoint
{
    private readonly DocsIndex docs=new();
    public Task<OperationResult> Move(MoveRequest request,CancellationToken ct)=>bridge.Move(request,ct);
    public Task<OperationResult> Attack(MoveRequest request,CancellationToken ct)=>bridge.Attack(request,ct);
    public Task<OperationResult> MapClick(MapClickRequest request,CancellationToken ct)=>bridge.MapClick(request,ct);
    public Task<OperationResult> MoveToTile(TileMoveRequest request,CancellationToken ct)=>bridge.MoveToTile(request,ct);
    public Task<DebugSnapshot> Snapshot(CancellationToken ct)=>bridge.Snapshot(ct);
    public Task<object> Start(CancellationToken ct)=>throw new InvalidOperationException("Game is already attached");
    public Task<object> Graphics(string? renderer,CancellationToken ct)=>throw new InvalidOperationException("Launcher host required");
    public Task<CaptureResult> Capture(CancellationToken ct)=>bridge.Capture(ct);
    public Task<object> Status(CancellationToken ct)=>Task.FromResult(bridge.Status());
    public Task<Observation> Observe(CancellationToken ct)=>bridge.Observe(ct);
    public Task<OperationResult> Click(OperationRequest request,CancellationToken ct)=>bridge.Click(request,ct);
    public Task<OperationResult> EnterText(TextRequest request,CancellationToken ct)=>bridge.EnterText(request,ct);
    public Task<ElementCard> InspectElement(InspectRequest request,CancellationToken ct)=>bridge.InspectElement(request,ct);
    public Task<CellCard> InspectCell(CellRequest request,CancellationToken ct)=>bridge.InspectCell(request,ct);
    public Task<object> ProbeScreen(CancellationToken ct)=>Task.FromResult(bridge.ProbeScreen());
    public Task<object> SendKey(KeyRequest request,CancellationToken ct)=>bridge.SendKey(request,ct);
    public Task<object> Press(PressRequest request,CancellationToken ct)=>bridge.Press(request,ct);
    public Task<object> Journal(int limit,CancellationToken ct)=>bridge.GetJournal(limit,ct);
    public Task<object> Plan(string? value,CancellationToken ct)=>bridge.Plan(value,ct);
    public Task<MapView> ReadMap(int x,int y,int z,int radius,CancellationToken ct)=>bridge.ReadMap(x,y,z,radius,ct);
    public Task<TileInspection> InspectTile(int x,int y,int z,string revision,CancellationToken ct)=>bridge.InspectTile(x,y,z,revision,ct);
    public Task<NearbyTargets> Nearby(CancellationToken ct)=>bridge.Nearby(ct);
    public Task<DocsAnswer> Docs(DocsRequest request,CancellationToken ct)=>Task.FromResult(docs.Search(request.Query,request.Limit,request.Detail));
    public Task<DocsCatalog> DocsCatalog(string? path,CancellationToken ct)=>Task.FromResult(docs.Catalog(path));
    public Task<DocText> DocsRead(string path,string? heading,int offset,int maxChars,CancellationToken ct)=>Task.FromResult(docs.Read(path,heading,offset,maxChars));
    public Task<ReferenceAnswer> Reference(ReferenceRequest request,CancellationToken ct)=>Task.FromResult(docs.Reference(request.Name,request.Kind,request.Limit));
    public Task<TargetInspection> InspectTarget(string targetId,string revision,CancellationToken ct)=>bridge.InspectTarget(targetId,revision,ct);
}

internal sealed class RemoteEndpoint(HttpClient client) : IGameEndpoint
{
    private async Task<T> Call<T>(string route,object body,CancellationToken ct)
    {
        using var response=await client.PostAsJsonAsync(route,body,ct);
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException(await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken:ct))!;
    }
    public Task<OperationResult> Move(MoveRequest request,CancellationToken ct)=>Call<OperationResult>("bridge/move",request,ct);
    public Task<OperationResult> Attack(MoveRequest request,CancellationToken ct)=>Call<OperationResult>("bridge/attack",request,ct);
    public Task<OperationResult> MapClick(MapClickRequest request,CancellationToken ct)=>Call<OperationResult>("bridge/map-click",request,ct);
    public Task<OperationResult> MoveToTile(TileMoveRequest request,CancellationToken ct)=>Call<OperationResult>("bridge/move-tile",request,ct);
    public Task<DebugSnapshot> Snapshot(CancellationToken ct)=>Call<DebugSnapshot>("bridge/debug-snapshot",new{},ct);
    public Task<object> Start(CancellationToken ct)=>Call<object>("bridge/start",new{},ct);
    public Task<object> Graphics(string? renderer,CancellationToken ct)=>Call<object>("bridge/graphics",new{renderer},ct);
    public Task<CaptureResult> Capture(CancellationToken ct)=>Call<CaptureResult>("bridge/debug-capture",new{},ct);
    public Task<object> Status(CancellationToken ct)=>Call<object>("bridge/status",new{},ct);
    public Task<Observation> Observe(CancellationToken ct)=>Call<Observation>("bridge/observe",new{},ct);
    public Task<OperationResult> Click(OperationRequest request,CancellationToken ct)=>Call<OperationResult>("bridge/click",request,ct);
    public Task<OperationResult> EnterText(TextRequest request,CancellationToken ct)=>Call<OperationResult>("bridge/text",request,ct);
    public Task<ElementCard> InspectElement(InspectRequest request,CancellationToken ct)=>Call<ElementCard>("bridge/inspect-element",request,ct);
    public Task<CellCard> InspectCell(CellRequest request,CancellationToken ct)=>Call<CellCard>("bridge/inspect-cell",request,ct);
    public Task<object> ProbeScreen(CancellationToken ct)=>Call<object>("bridge/probe-screen",new{},ct);
    public Task<object> SendKey(KeyRequest request,CancellationToken ct)=>Call<object>("bridge/key",request,ct);
    public Task<object> Press(PressRequest request,CancellationToken ct)=>Call<object>("bridge/press",request,ct);
    public Task<object> Journal(int limit,CancellationToken ct)=>Call<object>("bridge/journal",new{limit},ct);
    public Task<object> Plan(string? value,CancellationToken ct)=>Call<object>("bridge/plan",new{value},ct);
    public Task<MapView> ReadMap(int x,int y,int z,int radius,CancellationToken ct)=>Call<MapView>("bridge/map",new{x,y,z,radius},ct);
    public Task<TileInspection> InspectTile(int x,int y,int z,string revision,CancellationToken ct)=>Call<TileInspection>("bridge/inspect",new{x,y,z,revision},ct);
    public Task<NearbyTargets> Nearby(CancellationToken ct)=>Call<NearbyTargets>("bridge/nearby",new{},ct);
    public Task<DocsAnswer> Docs(DocsRequest request,CancellationToken ct)=>Call<DocsAnswer>("bridge/docs",request,ct);
    public Task<DocsCatalog> DocsCatalog(string? path,CancellationToken ct)=>Call<DocsCatalog>("bridge/docs-catalog",new{path},ct);
    public Task<DocText> DocsRead(string path,string? heading,int offset,int maxChars,CancellationToken ct)=>Call<DocText>("bridge/docs-read",new{path,heading,offset,maxChars},ct);
    public Task<ReferenceAnswer> Reference(ReferenceRequest request,CancellationToken ct)=>Call<ReferenceAnswer>("bridge/reference",request,ct);
    public Task<TargetInspection> InspectTarget(string targetId,string revision,CancellationToken ct)=>Call<TargetInspection>("bridge/target",new{targetId,revision},ct);
}
