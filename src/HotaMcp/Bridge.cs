using System.Text.Json;

namespace HotaMcp;

public sealed record OperationRequest(string OperationId,string Revision,string Element);
public sealed record OperationResult(string Status,string Message,Observation? Observation);
public sealed record JournalEntry(long Sequence,DateTimeOffset Time,string Kind,object Data);
public sealed record TargetView(string Id,string Kind,RouteView Route,int X=0,int Y=0,int Z=0)
{
    /// What stands there in the words the game uses: «Троглодит (бродячий отряд)», «Рудник»,
    /// «Тайник Бесов», «ресурс: сера» — the same name the map reader gives the cell.
    public string? Name {get;init;}
}
public sealed record NearbyTargets(string Revision,int HeroId,int Movement,List<TargetView> Targets,string Coverage)
{
    /// Targets the game lays no path to, grouped by the stack or gate that shuts them in: one
    /// fight opens the whole group.
    public List<string> LockedBehind {get;init;}=[];
}
public sealed record DocsRequest(string Query,int Limit,string? Detail);
public sealed record ReferenceRequest(string Name,string? Kind,int Limit);
public sealed record TargetInspection(string Id,string Kind,RouteView Route,string Revision);
/// A frame and what the bridge knows at that instant. On a screen the bridge has not mapped the
/// observation is null, and the raw controls of the top window plus the reason stand in its place —
/// that is exactly the material a new screen is mapped from.
public sealed record DebugSnapshot(Observation? Observation,CaptureResult Capture,string ObservationPath)
{
    public object? Probe {get;init;}
    public string? Unmapped {get;init;}
}
public sealed record MoveRequest(string OperationId,string Revision,string TargetId);
public sealed record TileMoveRequest(string OperationId,string Revision,int X,int Y,int Z);
public sealed record MapClickRequest(int X,int Y);
public sealed record TextRequest(string Revision,string Element,string Text);
public sealed record InspectRequest(string Revision,string Element);
public sealed record CellRequest(string Revision,int X,int Y,int Z);
public sealed record KeyRequest(int Key,int Scan,bool Control);
public sealed record PressRequest(int X,int Y);
public sealed record CellCard(int X,int Y,int Z,string[] Card,Observation Observation);
public sealed record ElementCard(string Element,string? Hint,string[] Card,Observation Observation);

internal sealed class Bridge(WindowsGame game,int player,string stateDirectory) : IDisposable
{
    private readonly GameReader reader=new(game,player);
    private readonly AllyWatch allies=new(game,player,Directory.GetParent(stateDirectory)?.Parent?.FullName??stateDirectory);
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
    /// What the two tavern portraits said when the right button was held on them, read the
    /// moment the tavern opened. Kept only while the tavern is the screen on display.
    private List<(string Side,string Card)> tavernCards=[];

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
    /// Notes the agent pins to map cells — a stack too strong for now, a passage, where the enemy
    /// was last seen. Its own memory of the map, kept beside the plan and shown where the cell is.
    private Dictionary<string,string>? markersCache;
    private Dictionary<string,string> markers=>markersCache??=File.Exists(MarkersFile)
        ?JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(MarkersFile))??new()
        :new();
    private string MarkersFile=>Path.Combine(
        Directory.GetParent(stateDirectory)?.Parent?.FullName??stateDirectory,$"markers-player{player}.json");
    private static string CellKey(int x,int y,int z)=>$"{x},{y},{z}";

    public async Task<object> Mark(int x,int y,int z,string? note,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if(note is {Length:>300})throw new ActionRefused(ActionRefused.BadText,"A marker note is at most 300 characters");
            if(string.IsNullOrWhiteSpace(note))markers.Remove(CellKey(x,y,z));
            else markers[CellKey(x,y,z)]=note.Trim();
            File.WriteAllText(MarkersFile,JsonSerializer.Serialize(markers));
            Record("marker",new{x,y,z,note});
            return new{markers=markers.Select(m=>new{cell=m.Key,note=m.Value}).ToArray()};
        }
        finally{gate.Release();}
    }

    /// A new game starts with a clean memory: the plan and the map notes of the last game are moved
    /// beside it with the date, so they can be read as a record but no longer steer the new one.
    private void ArchiveMemory(string? scenario)
    {
        UsageLedger.StartGame(Directory.GetParent(stateDirectory)?.Parent?.FullName??stateDirectory,scenario);
        string stamp=DateTime.Now.ToString("yyyyMMdd-HHmmss");
        foreach(string file in new[]{PlanFile,MarkersFile})
            if(File.Exists(file))File.Move(file,Path.Combine(Path.GetDirectoryName(file)!,
                Path.GetFileNameWithoutExtension(file)+$"-{stamp}"+Path.GetExtension(file)));
        allies.Archive(stamp);
        planCache=null;markersCache=null;planDay=[];
        Record("memory_archived",new{stamp});
    }

    /// A refusal is a wrong belief about the state caught before it cost anything; counted over a
    /// game it says how well the agent keeps track of the board.
    public void RecordRefusal(ActionRefused refusal)=>Record("refused",new{code=refusal.Code,refusal.Message});

    private string PlanFile=>Path.Combine(
        Directory.GetParent(stateDirectory)?.Parent?.FullName??stateDirectory,$"plan-player{player}.txt");

    public void Dispose(){game.Dispose();gate.Dispose();}

    public void LoadTables()=>reader.LoadTables();

    /// One look at the ally's side, taken between two agent calls, never during one.
    public void AllyTick()
    {
        if(!gate.Wait(0))return;
        // Names of artefacts and creatures come from the game's tables; read before the first look,
        // or one artefact is logged under a number and then under its name as if it changed hands.
        try{reader.LoadTables();allies.Tick();}
        finally{gate.Release();}
    }

    public async Task<object> AllyLog(int limit,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try{return allies.Recent(limit);}
        finally{gate.Release();}
    }

    // ---------------------------------------------------------------- observation

    public async Task<Observation> Observe(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var state=reader.Observe();
            // Whoever opened the tavern — the agent or a person at the keyboard — the first look
            // at it reads both candidates the way a player does, by holding the right button.
            if(state.Screen=="tavern"&&tavernCards.Count==0)await ReadTavern(state);
            reader.CommitDecision(state.Combat);
            return WithMemory(state);
        }
        finally{gate.Release();}
    }

    /// A turn is long and a controller's memory is not guaranteed to survive it. The plan and the
    /// last few results are therefore part of every observation, not something to be asked for:
    /// whoever reads the state also reads what the goal was and what already happened, so nothing
    /// is re-decided from scratch or done twice.
    /// The game day of the latest reading; the usage ledger files every call under it.
    public int[] LastDate {get;private set;}=[];

    private Observation WithMemory(Observation state)
    {
        if(state.Date.Length==3)LastDate=state.Date;
        var lines=new List<string>(state.Brief);
        if(state.Screen=="adventure"&&state.Side is {Underground:true})
            try{lines.Insert(Math.Min(1,lines.Count),new MapReader(game,player).View().Z==1
                ?"На экране подземелье (кнопка-переключатель на панели — view:level)."
                :"На экране поверхность (подземелье — view:level).");}
            catch(InvalidOperationException){}
        // On this side's turn the brief opens with what the ally did while it waited.
        if(state.Side is {Yours:true,Allies.Length:>0}&&allies.SinceLastTurn(12) is {Count:>0} ally)
            lines.InsertRange(Math.Min(1,lines.Count),ally);
        if(state.Screen!="tavern")tavernCards=[];
        foreach(var (side,card) in tavernCards)
            lines.Add($"Таверна, кандидат {side} (карточка правой кнопки): {card}. "
                +$"Выбрать его — tavern:pick:{side}, нанять выбранного — tavern:hire.");
        string progress=string.Join("|",
            [string.Join(",",state.Date),string.Join(",",state.Resources),
             string.Join(";",state.Heroes.Select(h=>$"{h.Id}:{string.Join(",",h.Position)}:{h.Movement}:{string.Join(",",h.ArmyCounts)}")),
             string.Join(";",state.Towns.Select(t=>$"{t.Id}:{t.Buildings.Length}:{t.BuiltToday}")),
             state.Screen,state.Combat?.LogCount.ToString()??""]);
        // Waiting is not stalling: in a menu, or while another player moves, nothing is this side's to do.
        if(state.Side is not {Yours:true})idleReads=0;
        else if(progress==lastProgress)idleReads++;
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
        if(!string.IsNullOrWhiteSpace(plan)&&state.Date is [1,1,1]&&planDay.Length==3&&!planDay.SequenceEqual(state.Date))
            lines.Add("Похоже, началась новая партия (день 1, неделя 1, месяц 1), а план записан в другой: "
                +"прочитай его как запись прошлой игры и перепиши под эту — цель, стороны и города здесь другие.");
        if(!string.IsNullOrWhiteSpace(plan)&&state.Date.Length>0&&!planDay.SequenceEqual(state.Date))
            lines.Add($"План записан {(planDay.Length>2?$"в день {planDay[0]} недели {planDay[1]}":"раньше")}, "
                +"а сейчас другой день — перечитай его, выполни, и в конце хода перепиши: цель оставь дословно, "
                +"прошедший день сожми в одну строку СДЕЛАНО.");
        if(markers.Count>0&&state.Screen=="adventure")
            lines.Add("Твои метки на карте (tool mark): "+string.Join("; ",markers.Take(20).Select(m=>$"({m.Key}) {m.Value}"))
                +(markers.Count>20?$"; ещё {markers.Count-20}":"")+".");
        var recent=journal.Where(e=>e.Kind is "operation_completed" or "move_completed" or "battle_result"
                or "plan_updated" or "cell_inspected" or "refused")
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
    /// Developer mapping only: raw bytes at an address, so a table can be located by comparing
    /// what the game shows with what lies in memory instead of guessing an offset.
    public object Memory(uint address,int length)=>new
    {
        address,length,
        bytes=Convert.ToHexString(game.Read(address,Math.Clamp(length,1,4096)))
    };

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
            RequireOwnTurn(initial);
            if(initial.Hero is null)throw new InvalidOperationException("Select a hero first");
            // The hover changes what the game reports under the cursor, so the consistency window
            // starts after it: otherwise this call always invalidates its own observation.
            var observation=initial;
            var hero=observation.Hero??throw new InvalidOperationException("Hero selection lost while refreshing routes");
            var region=new MapReader(game,player).Read(observation,hero.Position[0],hero.Position[1],hero.Position[2],12);
            // Read only: the route table is the one the game holds now. When it is stale the routes
            // say so, and the player points at a cell himself (inspect_path) to have it rebuilt.
            var explainer=new RouteExplainer(game,player);
            var locked=new List<(string By,string What)>();
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
                string? townName=null;
                if(target.Type==98)
                {
                    kind=observation.Towns.Any(t=>t.Id==target.Id)?"свой город":"ЧУЖОЙ ГОРОД";
                    // The flag over a town is seen by everyone: whose it is, or nobody's.
                    if(kind!="свой город"&&new TownReader(game,player).Describe(target.Id) is var (tn,owner))
                    {
                        townName=tn;
                        kind=owner==255?"нейтральный город (без хозяина — можно взять, в нём гарнизон)"
                            :observation.Side?.Allies.Contains(owner)==true?$"город союзника ({ColourName(owner)})"
                            :$"ЧУЖОЙ ГОРОД ({ColourName(owner)})";
                    }
                }
                if(target.Type==34)
                    kind=observation.Heroes.Any(h=>h.Id==target.Id)?"свой герой"
                        :observation.ForeignHeroes.FirstOrDefault(h=>h.Id==target.Id) is {} other&&observation.Side?.Allies.Contains(other.Owner)==true
                            ?$"герой союзника ({ColourName(other.Owner)})":"ЧУЖОЙ ГЕРОЙ";
                string? name=target.Type switch
                {
                    98 => observation.Towns.FirstOrDefault(t=>t.Id==target.Id)?.Name??townName??target.Name,
                    34 => observation.Heroes.FirstOrDefault(h=>h.Id==target.Id)?.Name??target.Name,
                    _ => target.Name,
                };
                if(markers.TryGetValue(CellKey(target.X,target.Y,target.Z),out var mark))name=$"{name??kind} — твоя метка: {mark}";
                list.Add(new(id,kind,Explain(explainer,observation,new RouteReader(game,player).Read(observation,target),target.X,target.Y,target.Z),target.X,target.Y,target.Z){Name=name});
                if(explainer.LastBlocker is string lockedBy)locked.Add((lockedBy,name??kind));
            }
            // The flag over a mine is seen on the map wherever the land is explored, so every mine
            // says whose it is — an ally's mine is not taken for a free one.
            for(int i=0;i<list.Count;i++)
            {
                var target=targets[list[i].Id];
                if(target.Type==53)
                {
                    int owner=new MapReader(game,player).MineOwner(target.Id,target.X,target.Y,target.Z);
                    string flag=owner==player?"твоя":owner>7?"ничья — захватить"
                        :observation.Side?.Allies.Contains(owner)==true?$"флаг {ColourName(owner)} (союзник — не трогать)"
                        :$"флаг {ColourName(owner)} (противник — захватить)";
                    list[i]=list[i] with{Kind=$"{list[i].Kind} — {flag}"};
                    continue;
                }
                // Stacks, piles, heroes and towns carry no visited mark; everything else says it
                // in the status line when pointed at, and that is what a player reads.
                if(target.Type is 34 or 54 or 79 or 98 or 5)continue;
                if(await VisitedMark(target.X,target.Y,target.Z) is string mark)
                    list[i]=list[i] with{Kind=$"{list[i].Kind} ({mark})"};
            }
            var settled=reader.Observe();
            if(settled.Hero is null||settled.Screen!="adventure")
                throw new InvalidOperationException("State changed; request targets again");
            return new(settled.Revision,hero.Id,hero.Movement,list,
                "Посещён ли объект выбранным сейчас героем — спроси inspect_cell по его клетке: игра сама пишет "
                +"в карточке «(Посещено)», и статус этот свой у каждого героя. "
                +"Recognized visible objects near selected hero; list is not exhaustive. Routes are the game's own, "
                +"rebuilt before reading when stale; a target the game lays no path to names what shuts the way.")
            {LockedBehind=locked.GroupBy(l=>l.By).Select(g=>$"за охраной {g.Key}: {string.Join(", ",g.Select(l=>l.What))}").ToList()};
        }
        finally{gate.Release();}
    }

    /// «Посещено» / «Не посещено» from the status line while the pointer rests on an object the
    /// camera already shows, as the game writes it for the selected hero.
    private async Task<string?> VisitedMark(int x,int y,int z)
    {
        var now=reader.Observe();
        var map=new MapReader(game,player);
        if(!map.IsOnScreen(now,x,y,z))return null;
        var point=map.ScreenPoint(now,x,y,z);
        for(int attempt=0;attempt<5;attempt++)
        {
            await game.MouseAsync(point.X,point.Y,now.Width,now.Height,false,CancellationToken.None);
            await Task.Delay(120,CancellationToken.None);
            try{map.VerifyMouse(x,y,z);}catch(InvalidOperationException){continue;}
            string? line=reader.Observe().Elements.FirstOrDefault(e=>e.Id==200)?.Text;
            if(line is null)return null;
            if(line.Contains("Не посещено",StringComparison.OrdinalIgnoreCase))return "не посещено";
            if(line.Contains("Посещено",StringComparison.OrdinalIgnoreCase))return "посещено";
            return null;
        }
        return null;
    }

    /// The colour as it stands in the game's own «Принадлежит <цвет> игроку».
    private static string Colours(int owner)=>owner switch
    {
        0=>"красному",1=>"синему",2=>"коричневому",3=>"зелёному",4=>"оранжевому",5=>"фиолетовому",6=>"бирюзовому",7=>"розовому",_=>"игроку"
    };

    private static string ColourName(int owner)=>owner switch
    {
        0=>"красный",1=>"синий",2=>"коричневый",3=>"зелёный",4=>"оранжевый",5=>"фиолетовый",6=>"бирюзовый",7=>"розовый",_=>$"игрок {owner}"
    };

    public async Task<TargetInspection> InspectTarget(string targetId,string revision,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if(!targets.TryGetValue(targetId,out var target))
                throw new InvalidOperationException("Unknown target; request nearby_targets first");
            var stale=reader.Observe();
            if(stale.Revision!=revision)throw new InvalidOperationException("State changed; request nearby_targets again");
            RequireOwnTurn(stale);
            var map=new MapReader(game,player);
            map.ValidateTarget(stale,target);
            var before=await PlanRouteTo(stale,target.X,target.Y,target.Z);
            var route=Explain(new RouteExplainer(game,player),before,new RouteReader(game,player).Read(before,target),target.X,target.Y,target.Z);
            var after=reader.Observe();
            if(after.Revision!=before.Revision)throw new InvalidOperationException("State changed while reading target");
            // Walking into somebody else's town or onto his hero is a battle, but the route is
            // computed as if the cell were empty and nothing on the map shows a stack. Saying it
            // here, where the decision to go is made, is the difference between a siege and a
            // scout walking to his death.
            string kind=target.Kind;
            bool allied=target.Type==98&&new TownReader(game,player).Describe(target.Id) is var (_,townOwner)&&after.Side?.Allies.Contains(townOwner)==true
                ||target.Type==34&&after.ForeignHeroes.FirstOrDefault(h=>h.Id==target.Id) is {} ally&&after.Side?.Allies.Contains(ally.Owner)==true;
            if(allied)kind+=" — союзник: не бой и не штурм";
            else if(target.Type==98&&!after.Towns.Any(t=>t.Id==target.Id))
                kind+=" — вход в чужой город это штурм его гарнизона, состав которого не виден; "
                    +"сначала посмотри, кто там, и приходи силой";
            if(!allied&&target.Type==34&&!after.Heroes.Any(h=>h.Id==target.Id))
                kind+=" — шаг на чужого героя это бой с его армией; "
                    +"его состав показывает карточка героя по правому щелчку";
            return new(targetId,kind,route,after.Revision);
        }
        finally{gate.Release();}
    }

    /// The game's own route from the selected hero to any explored cell, planned the way pointing
    /// at the cell plans it. This is how a player sees whether a place can be walked to at all and
    /// how many days it takes; nothing moves.
    public async Task<RouteView> InspectPath(int x,int y,int z,string revision,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var before=reader.Observe();
            if(before.Revision!=revision)throw new ActionRefused(ActionRefused.StaleRevision,"Observation is stale; observe again");
            RequireOwnTurn(before);
            if(before.Screen!="adventure"||before.Hero is null)throw new InvalidOperationException("Own hero on the adventure map required");
            var planned=await PlanRouteTo(before,x,y,z);
            var route=Explain(new RouteExplainer(game,player),planned,new RouteReader(game,player).Read(planned,new MapObject(x,y,z,-1,"cell")),x,y,z);
            Record("path_inspected",new{x,y,z,route.State,route.MovementCost,route.Steps});
            return route;
        }
        finally{gate.Release();}
    }

    public async Task<TileInspection> InspectTile(int x,int y,int z,string revision,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var before=reader.Observe();
            if(before.Revision!=revision)throw new ActionRefused(ActionRefused.StaleRevision,"Observation is stale; observe again");
            RequireOwnTurn(before);
            var map=new MapReader(game,player);
            // The camera follows the selected hero, so a cell the player knows about is often off
            // screen. A player brings it into view by pressing the minimap; the bridge does the
            // same instead of refusing to look. The press moves the camera only.
            before=await EnsureVisible(before,x,y,z);
            var point=map.ScreenPoint(before,x,y,z);
            for(int attempt=0;;attempt++)
            {
                await game.MouseAsync(point.X,point.Y,before.Width,before.Height,false,ct);
                await Task.Delay(200,ct);
                try{map.VerifyMouse(x,y,z);break;}
                catch(InvalidOperationException)when(attempt<9){}
            }
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
            if(before.Revision!=request.Revision)throw new ActionRefused(ActionRefused.StaleRevision,"Observation is stale; observe again");
            RequireOwnTurn(before);
            if(before.Screen!="adventure")throw new InvalidOperationException("Adventure map required");
            var map=new MapReader(game,player);
            before=await EnsureVisible(before,request.X,request.Y,request.Z);
            var point=map.ScreenPoint(before,request.X,request.Y,request.Z);
            // Right after the camera moves the game can still hit-test the old view; the card is read
            // only once the game itself names this cell as the one under the cursor.
            for(int attempt=0;;attempt++)
            {
                await game.MouseAsync(point.X,point.Y,before.Width,before.Height,false,ct);
                await Task.Delay(200,ct);
                try{map.VerifyMouse(request.X,request.Y,request.Z);break;}
                catch(InvalidOperationException)when(attempt<9){}
            }
            await game.RightMouseDownAsync(point.X,point.Y,before.Width,before.Height,ct);
            GameReader.CardView card;
            try
            {
                await Task.Delay(350,CancellationToken.None);
                card=reader.ReadCard();
                if(reader.TownCard() is string town)card=card with {Texts=[town]};
                if(reader.ForeignHeroCard() is string foreign)card=card with {Texts=[foreign]};
                Record("cell_inspected",new{request.X,request.Y,request.Z,card.Texts});
            }
            finally{await game.RightMouseUpAsync();}
            await Task.Delay(150,CancellationToken.None);
            var after=reader.Observe();
            // The card of another player's hero can stay up after the button is released; it is
            // closed the way its own screen closes, so the map is back for the next look.
            if(after.Screen=="enemy_hero_card")
            {
                await game.KeyAsync(0x0d,0x1c);
                await Task.Delay(150,CancellationToken.None);
                after=reader.Observe();
            }
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
            if(before.Revision!=request.Revision)throw new ActionRefused(ActionRefused.StaleRevision,"Observation is stale; observe again");
            RequireOwnTurn(before);
            // Any cell on screen can be looked at, not only the ones the observation lists: an
            // element key works, and so does "id:<number>" for a control the observation leaves
            // out to stay compact.
            int centreX,centreY;
            var element=before.Elements.SingleOrDefault(e=>e.Key==request.Element);
            if(element is not null)(centreX,centreY)=(element.X+element.Width/2,element.Y+element.Height/2);
            // In a fight a stack is looked at where it stands: the pointer resting on it fills the
            // status line with what the game says about hitting it, the right button opens its card.
            else if(before.Combat?.Stacks.FirstOrDefault(s=>s.Id==request.Element) is {} stack)
                (centreX,centreY)=new CombatReader(game,player).Center(stack.Hex);
            else if(request.Element.StartsWith("id:")&&int.TryParse(request.Element[3..],out int wanted))
            {
                var box=reader.FindControlById(wanted)
                    ??throw new InvalidOperationException($"No control with id {wanted} on this screen");
                (centreX,centreY)=(box.X+box.Width/2,box.Y+box.Height/2);
            }
            else throw new InvalidOperationException("Unknown control; observe again, or address it as id:<number>");
            // A player first rests the pointer on a control: the hint line of the window then
            // says what it does and, for a purchase, what it costs («Улучшить (Золото: 7500…)»).
            // The hint is whatever text line changed under the resting pointer.
            // The pointer may already rest on this control, so the hint line is first cleared by
            // moving it to the corner of the screen.
            await game.MouseAsync(0,0,before.Width,before.Height,false,ct);
            await Task.Delay(150,CancellationToken.None);
            var resting=reader.Observe();
            await game.MouseAsync(centreX,centreY,before.Width,before.Height,false,ct);
            await Task.Delay(200,CancellationToken.None);
            var hovered=reader.Observe();
            var was=resting.Elements.GroupBy(e=>e.Key).ToDictionary(g=>g.Key,g=>g.First().Text);
            var changed=hovered.Elements
                .Where(e=>!string.IsNullOrWhiteSpace(e.Text)&&(!was.TryGetValue(e.Key,out var old)||old!=e.Text))
                .Select(e=>e.Text!.Trim()).Distinct().ToList();
            // Over a stack in a fight several lines answer at once — the status line with what a
            // blow would do and the panel of the stack's effects — and all of them are the hint.
            string? hint=before.Screen=="combat"&&changed.Count>0?string.Join(" | ",changed.Take(4)):changed.FirstOrDefault();
            await game.RightMouseDownAsync(centreX,centreY,before.Width,before.Height,ct);
            GameReader.CardView card;
            try
            {
                await Task.Delay(350,CancellationToken.None);
                card=reader.ReadCard();
                // The expansion's backpack window draws its artefact card outside the game's list of
                // windows; the card is the artefact's name and the description from the game's own
                // artefact table, so that is what is returned.
                if(before.Screen=="backpack")
                {
                    var picture=element??before.Elements.FirstOrDefault(e=>request.Element==$"id:{e.Id}");
                    if(picture is not null&&picture.Id is >=2000 and <2064&&GameReference.ArtifactText(picture.Frame) is string text)
                        card=card with{Texts=[text]};
                }
                Record("element_inspected",new{request.Element,card.Vtable,card.Texts});
            }
            finally{await game.RightMouseUpAsync();}
            await Task.Delay(150,CancellationToken.None);
            var after=reader.Observe();
            if(after.Screen!=before.Screen)
                throw new InvalidOperationException("The control reacted instead of showing a card; observe again");
            return new(request.Element,hint,card.Texts,after);
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
                if(previous.Request!=request)throw new ActionRefused(ActionRefused.OperationReused,"Operation ID reused with different arguments");
                return previous.Result;
            }
            var before=reader.Observe();
            if(before.Revision!=request.Revision)throw new ActionRefused(ActionRefused.StaleRevision,"Observation is stale; observe again before acting");
            RequireOwnTurn(before);
            // A key ending in «:<n>» is a template: the agent fills in the number, and the
            // command itself checks the number against what the screen allows.
            var action=before.Actions.SingleOrDefault(a=>a.Key==request.Element)
                ??before.Actions.Where(a=>a.Key.EndsWith(":<n>")&&request.Element.StartsWith(a.Key[..^3],StringComparison.Ordinal)
                        &&int.TryParse(request.Element[(a.Key.Length-3)..],out int n)&&n>=0)
                    .Select(a=>new AvailableAction(request.Element,a.Label)).FirstOrDefault()
                // «:<name>» takes a short name of letters (latin or cyrillic), digits and dashes.
                ??before.Actions.Where(a=>a.Key.EndsWith(":<name>")&&request.Element.StartsWith(a.Key[..^6],StringComparison.Ordinal)
                        &&request.Element.Length>a.Key.Length-6&&request.Element.Length-(a.Key.Length-6)<=32
                        &&request.Element[(a.Key.Length-6)..].All(c=>char.IsAsciiLetterOrDigit(c)||c=='-'||c is >='а' and <='я'||c is >='А' and <='Я'||c is 'ё' or 'Ё'))
                    .Select(a=>new AvailableAction(request.Element,a.Label)).FirstOrDefault();
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

    /// A player who opens the tavern holds the right button on each of the two portraits to see
    /// who they are: class and level, the four primary skills, specialty, skills and the army
    /// each would bring. The bridge does exactly that the moment the tavern opens and keeps what
    /// the two cards said, so the agent sees both candidates at once instead of only the one the
    /// game happens to have selected. Only the text of the cards is read — what is on the screen.
    private async Task ReadTavern(Observation state)
    {
        tavernCards=[];
        foreach(var (id,side) in new[]{(5,"слева"),(6,"справа")})
        {
            var box=reader.FindControlById(id);
            if(box is null)continue;
            await game.RightMouseDownAsync(box.X+box.Width/2,box.Y+box.Height/2,state.Width,state.Height,CancellationToken.None);
            try
            {
                await Task.Delay(350,CancellationToken.None);
                tavernCards.Add((side,reader.HeroCard()??string.Join(" / ",reader.ReadCard().Texts)));
            }
            finally{await game.RightMouseUpAsync();}
            await Task.Delay(150,CancellationToken.None);
        }
        Record("tavern_candidates",tavernCards.Select(c=>new{c.Side,c.Card}));
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
            // In a fight the game fills the shaded-hex table for the next stack a moment after it
            // becomes active; read too early, the table still belongs to the stack that just moved,
            // and the next blow is planned against reach it does not have. The answer waits until
            // two reads a beat apart agree.
            // Enemy stacks moving in between pause for their animations, and two reads inside one
            // such pause agree while the fight is still running; so the log has to stand still
            // too, and for three reads in a row.
            if(after.Combat is not null)
                for(int settle=0,agree=0;settle<20&&agree<2;settle++)
                {
                    await Task.Delay(150,CancellationToken.None);
                    Observation again;
                    try{again=reader.Observe();}catch(InvalidOperationException){continue;}
                    if(again.Combat is null){after=again;break;}
                    bool same=again.Combat.ActiveStack==after.Combat!.ActiveStack&&again.Combat.OwnTurn==after.Combat.OwnTurn
                        &&again.Combat.LogCount==after.Combat.LogCount
                        &&again.Combat.ReachableHexes.SequenceEqual(after.Combat.ReachableHexes);
                    agree=same?agree+1:0;
                    after=again;
                }
            reader.CommitDecision(after.Combat);
            // An attack and a step look alike to the game's input: both are a click on the field.
            // Which one happened is read from the fight's own log — a blow writes «<отряд>
            // наносит(ят) урон»; a step writes nothing. The agent is told plainly when a blow it
            // asked for turned into a move.
            string blow="";
            if(request.Element.StartsWith("combat:attack:",StringComparison.Ordinal)&&before.Combat is not null&&after.Combat is not null)
            {
                // The log names a lone creature in the singular and a stack in the plural, so the
                // proof is the defender itself: fewer of them, or none left.
                string victimId=request.Element["combat:attack:".Length..];
                int cut=victimId.IndexOf(":from:",StringComparison.Ordinal);
                if(cut>=0)victimId=victimId[..cut];
                int was=before.Combat.Stacks.FirstOrDefault(s=>s.Id==victimId)?.Count??0;
                int now=after.Combat.Stacks.FirstOrDefault(s=>s.Id==victimId)?.Count??0;
                // A blow that wounds without killing leaves the count as it was; the log still
                // names the attacker, in the plural for a stack and the singular for one creature.
                var attacker=before.Combat.Stacks.FirstOrDefault(s=>s.Id==before.Combat.ActiveStack);
                string[] names=attacker is null?[]:[attacker.Name,GameReference.Creature(attacker.Type)];
                bool logged=after.Combat.Log.Any(e=>e.Index>=before.Combat.LogCount&&e.Text.Contains("нанос",StringComparison.Ordinal)
                    &&names.Any(n=>n.Length>0&&e.Text.StartsWith(n,StringComparison.Ordinal)));
                bool struck=now<was||logged;
                blow=struck?" Удар состоялся."
                    :" УДАРА НЕ БЫЛО: отряд переместился, но не атаковал — цель вне досягаемости или выбрана клетка хода. Посмотри журнал боя и досягаемые клетки.";
            }
            // Closing a window on the map is often followed by the next one — a level-up after a
            // chest, a message after a fight. It opens a moment later, so the answer waits for it
            // rather than telling the agent the map is free.
            string next="";
            if(after.Screen=="adventure"&&before.Screen!="adventure")
                for(int beat=0;beat<12;beat++)
                {
                    await Task.Delay(100,CancellationToken.None);
                    Observation settled;
                    try{settled=reader.Observe();}catch(InvalidOperationException){continue;}
                    if(settled.Screen!="adventure"){after=settled;next=$"; the game then opened {settled.Screen} — read it";break;}
                }
            // After a press the camera may still glide to the hero and pictures still settle; the
            // revision handed back is the one the next action will be checked against, so it is
            // taken only once two reads in a row agree.
            for(int beat=0;beat<10;beat++)
            {
                await Task.Delay(100,CancellationToken.None);
                Observation again;
                try{again=reader.Observe();}catch(InvalidOperationException){continue;}
                if(again.Revision==after.Revision&&again.Screen==after.Screen)break;
                after=again;
            }
            var result=new OperationResult("completed",
                (screenChanged?"Screen transition confirmed by revision change":"Same screen, state change confirmed by revision")+blow+next
                +(command.Confirm.HasFlag(Confirm.GarrisonChanged)?ArmyChange(before,after):""),after);
            operations[request.OperationId]=(request,result);
            Record("operation_completed",new{request.OperationId,after.Revision,after.Screen,BeforeScreen=before.Screen});
            if(request.Element=="scenario:start"&&after.Screen!="scenario_selection")ArchiveMemory(before.Setup?.Map?.Name);
            if(after.Screen=="tavern"&&before.Screen!="tavern")await ReadTavern(after);
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
        // High morale hands the same stack a second move in the same round; the only trace of the
        // first action then is the log having grown while that stack is still the one to act.
        if(confirm.HasFlag(Confirm.CombatTurn)
            &&!(after.Combat?.OwnTurn==true
                &&(after.Combat.ActiveStack!=before.Combat?.ActiveStack||after.Combat.Round!=before.Combat?.Round
                    ||after.Combat.LogCount>before.Combat!.LogCount)))return false;
        if(confirm.HasFlag(Confirm.PartyLoaded)&&!(after.Hero is not null&&after.Date.Length>0))return false;
        // Handing a stack over must actually change the armies of this side. Counting only the
        // town's own garrison was not enough: when a garrison hero stands in the town the upper
        // row is HIS army and the town garrison stays empty, so a real transfer looked like
        // nothing happening. Pressing a slot only selects it, and selection alone already changes
        // the observed revision, so the revision is not evidence.
        if(confirm.HasFlag(Confirm.GarrisonChanged)&&ArmySignature(before)==ArmySignature(after))return false;
        if(confirm.HasFlag(Confirm.ScreenLeft)&&after.Screen==before.Screen)return false;
        if(confirm.HasFlag(Confirm.GoldSpent)&&!(after.Resources.Length>6&&before.Resources.Length>6&&after.Resources[6]<before.Resources[6]))return false;
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
            // Some surfaces decide what a click means from where the cursor already is, not from
            // the coordinates carried by the click itself: the grids of starting towns and heroes
            // ignore a click that arrives without the cursor standing on the cell. So the cursor
            // is walked there first, exactly as a hand would.
            await game.MouseAsync(request.X,request.Y,width,height,false,CancellationToken.None);
            await Task.Delay(200,CancellationToken.None);
            await game.MouseAsync(request.X,request.Y,width,height,true,CancellationToken.None);
            await Task.Delay(300,CancellationToken.None);
            Record("developer_press",new{request.X,request.Y,From=probe.Vtable});
            return new{pressed=true,request.X,request.Y};
        }
        finally{gate.Release();}
    }

    /// Developer mapping only: holds the right button over one point, which is how a player asks
    /// this game what a picture means — the grids of starting towns and heroes carry no captions
    /// at all, and their names live only in the card the right button opens. Screens the adapter
    /// cannot name yet are exactly where this is needed, so it asks for no screen.
    public async Task<object> PressRight(PressRequest request,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            uint manager=game.U32(0x6992d0),surface=game.U32(manager+0x40);
            int width=game.I32(surface+0x24),height=game.I32(surface+0x28);
            if(request.X<0||request.Y<0||request.X>=width||request.Y>=height)
                throw new InvalidOperationException("Point is outside the game surface");
            await game.RightMouseDownAsync(request.X,request.Y,width,height,ct);
            string[] texts;
            object controls;
            try
            {
                await Task.Delay(350,CancellationToken.None);
                texts=reader.ReadCard().Texts;
                // What the card is built of, for mapping: every control with its class, frame and
                // picture, taken while the card is still held open.
                controls=reader.ProbeScreen();
            }
            finally{await game.RightMouseUpAsync();}
            await Task.Delay(150,CancellationToken.None);
            Record("developer_press_right",new{request.X,request.Y,texts});
            return new{request.X,request.Y,texts,controls};
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
                throw new ActionRefused(ActionRefused.BadText,"Text must be 1-64 characters without path separators");
            var before=reader.Observe();
            if(before.Revision!=request.Revision)throw new ActionRefused(ActionRefused.StaleRevision,"Observation is stale; observe again before acting");
            RequireOwnTurn(before);
            var field=before.Elements.SingleOrDefault(e=>e.Key==request.Element)
                ??throw new ActionRefused(ActionRefused.UnknownControl,"Unknown edit control; observe again");
            // Focus the ordinary edit control with a window mouse event, then type characters.
            await game.MouseAsync(field.X+field.Width/2,field.Y+field.Height/2,before.Width,before.Height,true,CancellationToken.None);
            await Task.Delay(200,CancellationToken.None);
            // What is already in the field is erased the way a person does it: to the end, then one
            // backspace per character.
            int existing=(field.Text??"").Length;
            if(existing>0)
            {
                await game.KeyAsync(0x23,0x4f);
                for(int i=0;i<existing;i++)await game.KeyAsync(0x08,0x0e);
                await Task.Delay(150,CancellationToken.None);
            }
            // The hotseat name fields read key presses; the other edit fields read characters.
            if(reader.ControlClass(field.Id)==0x640220)await game.TypeKeysAsync(request.Text);
            else await game.TextAsync(request.Text);
            await Task.Delay(200,CancellationToken.None);
            var after=reader.Observe();
            if(after.Screen!=before.Screen)throw new InvalidOperationException("Screen changed while entering text");
            string? now=after.Elements.FirstOrDefault(e=>e.Id==field.Id&&e.X==field.X&&e.Y==field.Y)?.Text;
            Record("text_entered",new{request.Element,Length=request.Text.Length,Screen=before.Screen,Now=now});
            return now==request.Text
                ?new("completed",$"The field now reads «{now}»",after)
                :new("uncertain",$"The field reads «{now}», not «{request.Text}»; observe and correct before confirming",after);
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
                if(prior.Request!=identity)throw new ActionRefused(ActionRefused.OperationReused,"Operation ID reused with different arguments");
                return prior.Result;
            }
            if(!targets.TryGetValue(request.TargetId,out var target))throw new InvalidOperationException("Request nearby_targets first");
            var before=RequireOwnHeroOnMap(request.Revision);
            var map=new MapReader(game,player);
            map.ValidateTarget(before,target);
            if(!attack&&string.Equals(target.Kind,"creatures",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Danger: this cell holds a creature stack, and moving onto it starts a battle. Approach a neighbouring cell with move_to_tile, or use attack_target to fight deliberately");
            // A fight is chosen, not stumbled into: when the game's own route does not reach the
            // stack today, the hero would spend the whole day walking a detour and arrive
            // tomorrow with nothing left. That is refused with the reason, so the detour is a
            // decision taken with move_to_tile rather than a side effect of an attack order.
            var route=attack?new RouteReader(game,player).Read(before,target):null;
            if(route is not null&&route.State!="reachable_today")
                throw new InvalidOperationException($"Отряд сегодня не достать: маршрут {route.State}"
                    +(route.MovementCost is int cost?$", нужно {cost} хода":"")
                    +(route.Detail is {} why?$" ({why})":"")
                    +". Подойди ближе move_to_tile и атакуй, когда nearby_targets покажет reachable_today. Герой не двигался.");
            int[] destination=[target.X,target.Y,target.Z];
            return await RunMove(request.OperationId,identity,before,destination,attack?30:10,"move",
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
                if(prior.Request!=identity)throw new ActionRefused(ActionRefused.OperationReused,"Operation ID reused with different arguments");
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
            await ClickToPlan(before,destination[0],destination[1],destination[2]);
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
            // The game refused to lay a path, so nothing was sent to the hero. That is a plain
            // refusal with its reason, not an unknown outcome.
            Record(journal+"_preparation_unconfirmed",new{operationId,planned});
            operations.Remove(operationId);
            throw new InvalidOperationException(NoRouteReason(before,destination));
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
            // Arriving at a town, a bank or a find opens its window a moment after the hero stops;
            // answering on the stop alone told the agent «reached» while a dialog was on its way.
            for(int beat=0;beat<12&&after.Screen=="adventure";beat++)
            {
                await Task.Delay(100,CancellationToken.None);
                Observation settled;
                try{settled=reader.Observe();}
                catch(InvalidOperationException){continue;}
                if(settled.Screen!="adventure"){after=settled;message+=$"; the game then opened {settled.Screen} — read it";}
                else if(settled.Hero is not null)after=settled;
            }
            // A monolith, a portal or gates move the hero on a moment after he steps on them.
            if(after.Hero is {} moved&&!moved.Position.SequenceEqual(destination)&&message.StartsWith("Hero reached",StringComparison.Ordinal))
                message=$"Hero stepped onto ({string.Join(",",destination)}) and the object there carried him to ({string.Join(",",moved.Position)})"
                    +(moved.Position[2]!=destination[2]?(moved.Position[2]==0?" — now on the surface":" — now underground"):"");
            // A route through a teleporting object ends on the other side of it, short of a
            // destination that was on this side: the level is what tells.
            else if(after.Hero is {} carried&&carried.Position[2]!=before.Hero!.Position[2])
                message=$"The hero went through a teleporting object on the way and is now at ({string.Join(",",carried.Position)})"
                    +(carried.Position[2]==0?" — on the surface":" — underground")+$"; the commanded cell ({string.Join(",",destination)}) was not reached";
            var result=new OperationResult("completed",message,after);
            operations[operationId]=(identity,result);
            Record(journal+"_completed",new{operationId,result});
            return result;
        }
        Record(journal+"_uncertain",identity);
        return pending;
    }

    /// Why the game would not lay a path, in the words a player would use: a guard whose zone
    /// covers the approach, a cell that cannot be stood on, or ground nobody has seen yet.
    private string NoRouteReason(Observation before,int[] cell)
    {
        string where=$"({cell[0]},{cell[1]})";
        try
        {
            var look=new MapReader(game,player).Read(before,cell[0],cell[1],cell[2],1);
            var guards=look.Objects.Where(o=>string.Equals(o.Kind,"creatures",StringComparison.OrdinalIgnoreCase)
                &&Math.Abs(o.X-cell[0])<=1&&Math.Abs(o.Y-cell[1])<=1).ToList();
            if(guards.Count>0)
                return $"Игра не проложила путь к {where}: рядом стоит охрана — "
                    +string.Join(", ",guards.Select(g=>$"{(g.Name.Length>0?g.Name:"отряд")} на ({g.X},{g.Y})"))
                    +". Её зона закрывает подход: сначала разбей её (attack_target) или выбери другую клетку. Герой не двигался.";
            int row=cell[1]-look.Y,col=cell[0]-look.X;
            char mark=row>=0&&row<look.Blocked.Length&&col>=0&&col<look.Blocked[row].Length?look.Blocked[row][col]:'?';
            if(mark=='?')return $"Игра не проложила путь к {where}: клетка в тумане, туда ещё никто не смотрел. Герой не двигался.";
            if(mark=='#')return $"Игра не проложила путь к {where}: на клетку нельзя встать (скалы, деревья, вода или часть объекта). Вход в объект — его собственная клетка из nearby_targets. Герой не двигался.";
        }
        catch(InvalidOperationException e)when(e.Message.Contains("hidden",StringComparison.OrdinalIgnoreCase))
        {
            return $"Игра не проложила путь к {where}: клетка скрыта туманом. Герой не двигался.";
        }
        var explainer=new RouteExplainer(game,player);
        string? why=explainer.WhyNoPath(before,cell[0],cell[1],cell[2]);
        if(explainer.OpenWay)
            return $"Игра не проложила путь к {where}: по разведанной суше дорога туда есть, но она длиннее, чем игра прокладывает от этой клетки. "
                +"Веди героя к промежуточной клетке по пути (read_map) — дальше путь проложится. Герой не двигался.";
        if(why is not null)return $"Игра не проложила путь к {where}: {why}. Герой не двигался.";
        return $"Игра не проложила путь к {where}: пути нет — клетка отрезана препятствиями. Посмотри read_map вокруг неё. Герой не двигался.";
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
        // The minimap shows one level at a time; the sidebar's elevation toggle switches it, as a
        // player does before looking at the other level.
        if(map.View().Z!=z)
        {
            var toggle=reader.FindControlById(4)??throw new InvalidOperationException("The elevation toggle is not on the sidebar");
            await game.MouseAsync(toggle.X+toggle.Width/2,toggle.Y+toggle.Height/2,observation.Width,observation.Height,false,CancellationToken.None);
            await Task.Delay(150,CancellationToken.None);
            await game.MouseAsync(toggle.X+toggle.Width/2,toggle.Y+toggle.Height/2,observation.Width,observation.Height,true,CancellationToken.None);
            await Task.Delay(300,CancellationToken.None);
            if(map.View().Z!=z)throw new InvalidOperationException($"The elevation toggle did not bring level {z} into view");
            observation=reader.Observe();
            Record("level_switched",new{z});
            if(map.IsOnScreen(observation,x,y,z))return observation;
        }
        // Ctrl+arrow cannot be used here: the game reads Ctrl from the real keyboard, so a posted
        // Ctrl+arrow arrives as a bare arrow — a step of the selected hero.
        var point=map.MinimapPoint(observation,x,y,z);
        await game.MouseAsync(point.X,point.Y,observation.Width,observation.Height,true,CancellationToken.None);
        await Task.Delay(300,CancellationToken.None);
        var moved=reader.Observe();
        if(!map.IsOnScreen(moved,x,y,z))
        {
            var view=map.View();
            throw new InvalidOperationException($"The camera did not reach ({x},{y},{z}): the view shows from ({view.X},{view.Y}); "
                +$"minimap press at {point}, real pointer at {game.RealPointer()}");
        }
        Record("view_centered",new{x,y,z});
        return moved;
    }

    /// After the hero's movement changes, the game's route cache still describes the hero as it
    /// was. Moving the cursor does not rebuild it: the rebuild belongs to the game's own map
    /// selection handler, the one a player triggers by pointing at a destination. Asking it for
    /// this destination is therefore what makes the route to it exist; it plans, it does not move.
    /// The hero's own cell is never asked for — selecting it opens the hero screen instead.
    /// A route is laid the way a player lays it: the camera is brought over the cell by a press on
    /// the minimap, and one press on the cell makes the game plan the way and draw it. A second
    /// press on the cell already planned would send the hero, so when the planned cell is the one
    /// asked for, a free neighbour of the hero is pressed first and the cell after it.
    private async Task ClickToPlan(Observation observation,int x,int y,int z)
    {
        if(observation.Screen!="adventure"||observation.Hero is null)throw new InvalidOperationException("Own hero on the adventure map required to plan a route");
        // Pointing at another own hero shows the meeting cursor: the press lays the way to him,
        // and arriving opens the exchange. Should the game take the press as choosing him
        // instead, the selection changes, and that is caught after the press below.
        bool meeting=observation.Heroes.Any(h=>h.Id!=observation.Hero.Id&&h.Position.SequenceEqual(new[]{x,y,z}));
        int selected=observation.Hero.Id;
        var map=new MapReader(game,player);
        if(observation.Hero.PlannedDestination.SequenceEqual(new[]{x,y,z}))
        {
            int[] at=observation.Hero.Position;
            var around=map.Read(observation,at[0],at[1],at[2],1);
            (int X,int Y)? free=null;
            for(int row=0;row<around.Height&&free is null;row++)
                for(int col=0;col<around.Width&&free is null;col++)
                {
                    int cx=around.X+col,cy=around.Y+row;
                    if((cx==at[0]&&cy==at[1])||(cx==x&&cy==y))continue;
                    if(around.Blocked[row][col]=='.'&&!around.Objects.Any(o=>o.X==cx&&o.Y==cy))free=(cx,cy);
                }
            if(free is null)throw new InvalidOperationException("No free cell beside the hero to re-plan the route without sending him");
            await PressCell(observation,free.Value.X,free.Value.Y,at[2]);
            observation=reader.Observe();
        }
        await PressCell(observation,x,y,z);
        if(meeting&&reader.Observe().Hero?.Id is int now&&now!=selected)
            throw new ActionRefused(ActionRefused.UnknownControl,"Щелчок по своему герою выбрал его, а не проложил путь к встрече: выбранный герой сменился. Ничего не пройдено.");
    }

    private async Task PressCell(Observation observation,int x,int y,int z)
    {
        var shown=await EnsureVisible(observation,x,y,z);
        var map=new MapReader(game,player);
        var point=map.ScreenPoint(shown,x,y,z);
        // A press lands on whatever cell the game has under the pointer, and a press on the cell a
        // route already leads to sends the hero. After a camera jump the game may still resolve
        // the pointer against the old view, so the press is sent only once the game itself names
        // the intended cell as the one under the pointer.
        bool reset=false;
        for(int attempt=0;;attempt++)
        {
            await game.MouseAsync(point.X,point.Y,shown.Width,shown.Height,false,CancellationToken.None);
            await Task.Delay(120,CancellationToken.None);
            bool confirmed;
            try{map.VerifyMouse(x,y,z);confirmed=true;}
            catch(InvalidOperationException){confirmed=false;}
            if(confirmed)break;
            if(attempt<9)continue;
            // A press on a real control of the sidebar brings the pointer back to life; the hero
            // picked for it is picked back, so the selection ends where it began.
            if(!reset&&await ResetPointer(shown))
            {
                reset=true;attempt=-1;
                // Picking the hero centres the camera on him, so the cell is brought back into view.
                shown=await EnsureVisible(reader.Observe(),x,y,z);
                point=map.ScreenPoint(shown,x,y,z);
                continue;
            }
            throw new ActionRefused(ActionRefused.UnknownControl,$"Игра не подтвердила клетку ({x},{y}) под курсором даже после сброса курсора — щелчок не отправлен, герой не двигался.");
        }
        await game.MouseAsync(point.X,point.Y,shown.Width,shown.Height,true,CancellationToken.None);
        await Task.Delay(150,CancellationToken.None);
        Record("cell_pressed",new{x,y,z});
    }

    /// After a minimap jump the game can stop resolving the pointer over the map until a real
    /// control is pressed. Picking another hero in the sidebar and then the selected one again is
    /// such a press and leaves the selection as it was. With a single hero there is no such pair.
    private async Task<bool> ResetPointer(Observation observation)
    {
        if(observation.Hero is null)return false;
        var list=GameReader.SidebarHeroes(game,player);
        int own=Array.IndexOf(list,observation.Hero.Id),other=Array.FindIndex(list,h=>h>=0&&h!=observation.Hero.Id);
        if(own<0||other<0)return false;
        UiElement? Portrait(int slot)=>observation.Elements.FirstOrDefault(e=>e.Id==15+slot&&e.Interactive);
        if(Portrait(own) is not {} mine||Portrait(other) is not {} theirs)return false;
        foreach(var press in new[]{theirs,mine})
        {
            await game.MouseAsync(press.X+press.Width/2,press.Y+press.Height/2,observation.Width,observation.Height,false,CancellationToken.None);
            await Task.Delay(150,CancellationToken.None);
            await game.MouseAsync(press.X+press.Width/2,press.Y+press.Height/2,observation.Width,observation.Height,true,CancellationToken.None);
            await Task.Delay(300,CancellationToken.None);
        }
        Record("pointer_reset",new{hero=observation.Hero.Id});
        return reader.Observe().Hero?.Id==observation.Hero.Id;
    }

    private async Task<Observation> PlanRouteTo(Observation observation,int x,int y,int z)
    {
        var map=new MapReader(game,player);
        if(observation.Screen!="adventure"||observation.Hero is null)return observation;
        int[] here=observation.Hero.Position;
        if(here[0]==x&&here[1]==y&&here[2]==z)return observation;
        if(!map.RoutesAreStale(observation)&&observation.Hero.PlannedDestination.SequenceEqual(new[]{x,y,z}))
            return observation;
        await ClickToPlan(observation,x,y,z);
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

    /// The game says only that it found no path; what stands in the way is what a player looks
    /// for next, so it is named here.
    private static RouteView Explain(RouteExplainer explainer,Observation observation,RouteView route,int x,int y,int z)
    {
        explainer.LastBlocker=null;
        if(route.State!="not_available"||route.Detail?.StartsWith("The game found no path",StringComparison.Ordinal)!=true)return route;
        if(explainer.WhyNoPath(observation,x,y,z) is string why)return route with{Detail=$"{why} ({route.Detail})"};
        // The game's table reaches only so far; a way open over explored land is not "no path".
        return explainer.OpenWay?route with{State="needs_more_days",Detail="путь по разведанной суше есть, но дальше, чем игра считает отсюда; точную цену даст inspect_target"}:route;
    }

    private async Task HoverAndSettle(Observation observation,int x,int y)
    {
        await game.MouseAsync(x,y,observation.Width,observation.Height,false,CancellationToken.None);
        await Task.Delay(150,CancellationToken.None);
    }

    /// In a hotseat game the screen belongs to whoever's turn it is. Pointing or pressing on an
    /// ally's turn would move his hero or spend his gold, so every gesture waits for this side's
    /// turn. Menus before a game have no turn; a fight this side is part of is answered whoever's
    /// turn brought it.
    private static void RequireOwnTurn(Observation before)
    {
        if(before.Side is null||before.Side.Yours||before.Combat is not null)return;
        // A window addressed to this side on another's turn — its own flag on a hand-over, the result
        // of a fight it took part in — is the reader's call: it offers actions only for those.
        if(before.Screen is "message" or "battle_result"&&before.Actions.Count>0)return;
        throw new ActionRefused(ActionRefused.NotYourTurn,
            $"Сейчас ходит {before.Side.ActiveColour}, а ты играешь за {before.Side.Colour}: ничего не нажато. "
            +"Жди своего хода — observe покажет, когда он начнётся.");
    }

    private Observation RequireOwnHeroOnMap(string revision)
    {
        var before=reader.Observe();
        RequireOwnTurn(before);
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
            Observation? before=null;string? unmapped=null;
            try{before=reader.Observe();}
            catch(InvalidOperationException e){unmapped=e.Message;}
            var capture=DebugCapture.Save(game,player,Path.Combine(stateDirectory,"captures"));
            string path=Path.ChangeExtension(capture.Path,"json");
            var options=new JsonSerializerOptions{WriteIndented=true};
            if(before is not null)
            {
                var after=reader.Observe();
                if(before.Revision!=after.Revision)
                    throw new InvalidOperationException("State changed during diagnostic capture; snapshot not confirmed");
                await File.WriteAllTextAsync(path,JsonSerializer.Serialize(before,options),ct);
                return new(before,capture,path);
            }
            // Not mapped yet: keep whatever the top window is made of next to the frame.
            object probe;
            try{probe=reader.ProbeScreen();}
            catch(InvalidOperationException e){probe=new{error=e.Message};}
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{unmapped,probe},options),ct);
            Record("snapshot_unmapped",new{unmapped,capture.Path});
            return new(null,capture,path){Probe=probe,Unmapped=unmapped};
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
    Task<object> PressRight(PressRequest request,CancellationToken ct);
    Task<object> Journal(int limit,CancellationToken ct);
    Task<object> AllyLog(int limit,CancellationToken ct);
    Task<object> Plan(string? value,CancellationToken ct);
    Task<object> Mark(int x,int y,int z,string? note,CancellationToken ct);
    Task<MapView> ReadMap(int x,int y,int z,int radius,CancellationToken ct);
    Task<TileInspection> InspectTile(int x,int y,int z,string revision,CancellationToken ct);
    Task<NearbyTargets> Nearby(CancellationToken ct);
    Task<DocsAnswer> Docs(DocsRequest request,CancellationToken ct);
    Task<DocsCatalog> DocsCatalog(string? path,CancellationToken ct);
    Task<DocText> DocsRead(string path,string? heading,int offset,int maxChars,CancellationToken ct);
    Task<ReferenceAnswer> Reference(ReferenceRequest request,CancellationToken ct);
    Task<TargetInspection> InspectTarget(string targetId,string revision,CancellationToken ct);
    Task<RouteView> InspectPath(int x,int y,int z,string revision,CancellationToken ct);
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
    public Task<object> PressRight(PressRequest request,CancellationToken ct)=>bridge.PressRight(request,ct);
    public Task<object> Journal(int limit,CancellationToken ct)=>bridge.GetJournal(limit,ct);
    public Task<object> AllyLog(int limit,CancellationToken ct)=>bridge.AllyLog(limit,ct);
    public Task<object> Plan(string? value,CancellationToken ct)=>bridge.Plan(value,ct);
    public Task<object> Mark(int x,int y,int z,string? note,CancellationToken ct)=>bridge.Mark(x,y,z,note,ct);
    public Task<MapView> ReadMap(int x,int y,int z,int radius,CancellationToken ct)=>bridge.ReadMap(x,y,z,radius,ct);
    public Task<TileInspection> InspectTile(int x,int y,int z,string revision,CancellationToken ct)=>bridge.InspectTile(x,y,z,revision,ct);
    public Task<NearbyTargets> Nearby(CancellationToken ct)=>bridge.Nearby(ct);
    public Task<DocsAnswer> Docs(DocsRequest request,CancellationToken ct)=>Task.FromResult(docs.Search(request.Query,request.Limit,request.Detail));
    public Task<DocsCatalog> DocsCatalog(string? path,CancellationToken ct)=>Task.FromResult(docs.Catalog(path));
    public Task<DocText> DocsRead(string path,string? heading,int offset,int maxChars,CancellationToken ct)=>Task.FromResult(docs.Read(path,heading,offset,maxChars));
    public Task<ReferenceAnswer> Reference(ReferenceRequest request,CancellationToken ct)=>Task.FromResult(docs.Reference(request.Name,request.Kind,request.Limit));
    public Task<TargetInspection> InspectTarget(string targetId,string revision,CancellationToken ct)=>bridge.InspectTarget(targetId,revision,ct);
    public Task<RouteView> InspectPath(int x,int y,int z,string revision,CancellationToken ct)=>bridge.InspectPath(x,y,z,revision,ct);
}

internal sealed class RemoteEndpoint(HttpClient client) : IGameEndpoint
{
    private async Task<T> Call<T>(string route,object body,CancellationToken ct)
    {
        using var response=await client.PostAsJsonAsync(route,body,ct);
        if(!response.IsSuccessStatusCode)
        {
            string reply=await response.Content.ReadAsStringAsync(ct);
            string message=reply;string? code=null;
            try
            {
                using var json=JsonDocument.Parse(reply);
                if(json.RootElement.ValueKind==JsonValueKind.Object&&json.RootElement.TryGetProperty("error",out var error))
                {
                    message=error.GetString()??reply;
                    if(json.RootElement.TryGetProperty("code",out var c)&&c.ValueKind==JsonValueKind.String)code=c.GetString();
                }
            }
            catch(JsonException){}
            throw code is null?new InvalidOperationException(message):new ActionRefused(code,message);
        }
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
    public Task<object> PressRight(PressRequest request,CancellationToken ct)=>Call<object>("bridge/press-right",request,ct);
    public Task<object> Journal(int limit,CancellationToken ct)=>Call<object>("bridge/journal",new{limit},ct);
    public Task<object> AllyLog(int limit,CancellationToken ct)=>Call<object>("bridge/ally-log",new{limit},ct);
    public Task<object> Plan(string? value,CancellationToken ct)=>Call<object>("bridge/plan",new{value},ct);
    public Task<object> Mark(int x,int y,int z,string? note,CancellationToken ct)=>Call<object>("bridge/mark",new{x,y,z,note},ct);
    public Task<MapView> ReadMap(int x,int y,int z,int radius,CancellationToken ct)=>Call<MapView>("bridge/map",new{x,y,z,radius},ct);
    public Task<TileInspection> InspectTile(int x,int y,int z,string revision,CancellationToken ct)=>Call<TileInspection>("bridge/inspect",new{x,y,z,revision},ct);
    public Task<NearbyTargets> Nearby(CancellationToken ct)=>Call<NearbyTargets>("bridge/nearby",new{},ct);
    public Task<DocsAnswer> Docs(DocsRequest request,CancellationToken ct)=>Call<DocsAnswer>("bridge/docs",request,ct);
    public Task<DocsCatalog> DocsCatalog(string? path,CancellationToken ct)=>Call<DocsCatalog>("bridge/docs-catalog",new{path},ct);
    public Task<DocText> DocsRead(string path,string? heading,int offset,int maxChars,CancellationToken ct)=>Call<DocText>("bridge/docs-read",new{path,heading,offset,maxChars},ct);
    public Task<ReferenceAnswer> Reference(ReferenceRequest request,CancellationToken ct)=>Call<ReferenceAnswer>("bridge/reference",request,ct);
    public Task<TargetInspection> InspectTarget(string targetId,string revision,CancellationToken ct)=>Call<TargetInspection>("bridge/target",new{targetId,revision},ct);
    public Task<RouteView> InspectPath(int x,int y,int z,string revision,CancellationToken ct)=>Call<RouteView>("bridge/path",new{x,y,z,revision},ct);
}
