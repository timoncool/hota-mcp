using System.Text.Json;

namespace HotaMcp;

public sealed record OperationRequest(string OperationId,string Revision,string Element);
public sealed record OperationResult(string Status,string Message,Observation? Observation);
public sealed record JournalEntry(long Sequence,DateTimeOffset Time,string Kind,object Data);
public sealed record TargetView(string Id,string Kind,RouteView Route,int X=0,int Y=0,int Z=0);
public sealed record NearbyTargets(string Revision,int HeroId,int Movement,List<TargetView> Targets,string Coverage);
public sealed record DocsRequest(string Query,int Limit);
public sealed record ReferenceRequest(string Name,string? Kind,int Limit);
public sealed record TargetInspection(string Id,string Kind,RouteView Route,string Revision);
public sealed record DebugSnapshot(Observation Observation,CaptureResult Capture,string ObservationPath);
public sealed record MoveRequest(string OperationId,string Revision,string TargetId);
public sealed record TileMoveRequest(string OperationId,string Revision,int X,int Y,int Z);
public sealed record MapClickRequest(int X,int Y);
public sealed record TextRequest(string Revision,string Element,string Text);
public sealed record InspectRequest(string Revision,string Element);
public sealed record ElementCard(string Element,string[] Card,Observation Observation);

internal sealed class Bridge(WindowsGame game,int player,string stateDirectory) : IDisposable
{
    private readonly GameReader reader=new(game,player);
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly Dictionary<string,(OperationRequest Request,OperationResult Result)> operations=new();
    private readonly List<JournalEntry> journal=[];
    private readonly Dictionary<string,MapObject> targets=new();
    private readonly Dictionary<MapObject,string> targetIds=new();
    private string plan="";

    public void Dispose(){game.Dispose();gate.Dispose();}

    // ---------------------------------------------------------------- observation

    public async Task<Observation> Observe(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try{return reader.Observe();}
        finally{gate.Release();}
    }

    public object Status()=>new
    {
        phase="development",player,gamePid=game.Process.Id,
        build=new{game.Build.Status,game.Build.Summary,game.Build.Validated,game.Build.Checks},
        capabilities=new[]{"observe_own_hero","observe_adventure_ui","open_system_options","return_to_game",
            "visible_targets","route_preview","move_to_target","move_to_tile","own_towns","town_construction",
            "town_recruitment","tavern_hero","hero_exchange","combat_actions","battle_result","spellbook",
            "save_game","save_list_and_load","inspect_element","hero_switch_on_map","map_terrain",
            "game_reference","hotkeys"},
        unavailable=new[]{"full_map_coverage","in_game_load_browser","full_scenario_setup",
            "hotseat","lan","installer","cost_measurement"}
    };

    public object Diagnostic()=>reader.DiagnosticPointers();
    public object RawUi()=>reader.DiagnosticDialog();
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
                list.Add(new(id,target.Kind,new RouteReader(game,player).Read(observation,target),target.X,target.Y,target.Z));
            }
            if(reader.Observe().Revision!=observation.Revision)
                throw new InvalidOperationException("State changed; request targets again");
            return new(observation.Revision,hero.Id,hero.Movement,list,
                "Recognized visible objects near selected hero; list is not exhaustive");
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
            return new(targetId,target.Kind,route,after.Revision);
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
            var element=before.Elements.SingleOrDefault(e=>e.Key==request.Element)
                ??throw new InvalidOperationException("Unknown control; observe again");
            await game.RightMouseDownAsync(element.X+element.Width/2,element.Y+element.Height/2,before.Width,before.Height,ct);
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
                screenChanged?"Screen transition confirmed by revision change":"Same screen, state change confirmed by revision",after);
            operations[request.OperationId]=(request,result);
            Record("operation_completed",new{request.OperationId,after.Revision,after.Screen,BeforeScreen=before.Screen});
            return result;
        }
        Record("operation_uncertain",new{request.OperationId});
        return pending;
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
        // A step counts only when the hero is somewhere else or paid movement for it; a dialog
        // opening on the way is the move having happened too.
        if(confirm.HasFlag(Confirm.HeroMoved)&&after.Screen=="adventure"
            &&!(after.Hero is not null&&before.Hero is not null
                &&(!after.Hero.Position.SequenceEqual(before.Hero.Position)||after.Hero.Movement<before.Hero.Movement)))return false;
        return true;
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
                Record("plan_updated",new{plan});
            }
            return new{plan,player};
        }
        finally{gate.Release();}
    }

    private void Record(string kind,object data)
    {
        Directory.CreateDirectory(stateDirectory);
        var entry=new JournalEntry(journal.Count+1,DateTimeOffset.UtcNow,kind,data);
        File.AppendAllText(Path.Combine(stateDirectory,"journal.jsonl"),JsonSerializer.Serialize(entry)+"\n");
        journal.Add(entry);
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
    Task<object> Journal(int limit,CancellationToken ct);
    Task<object> Plan(string? value,CancellationToken ct);
    Task<MapView> ReadMap(int x,int y,int z,int radius,CancellationToken ct);
    Task<TileInspection> InspectTile(int x,int y,int z,string revision,CancellationToken ct);
    Task<NearbyTargets> Nearby(CancellationToken ct);
    Task<DocsAnswer> Docs(DocsRequest request,CancellationToken ct);
    Task<DocsCatalog> DocsCatalog(CancellationToken ct);
    Task<DocText> DocsRead(string path,string? heading,CancellationToken ct);
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
    public Task<object> Journal(int limit,CancellationToken ct)=>bridge.GetJournal(limit,ct);
    public Task<object> Plan(string? value,CancellationToken ct)=>bridge.Plan(value,ct);
    public Task<MapView> ReadMap(int x,int y,int z,int radius,CancellationToken ct)=>bridge.ReadMap(x,y,z,radius,ct);
    public Task<TileInspection> InspectTile(int x,int y,int z,string revision,CancellationToken ct)=>bridge.InspectTile(x,y,z,revision,ct);
    public Task<NearbyTargets> Nearby(CancellationToken ct)=>bridge.Nearby(ct);
    public Task<DocsAnswer> Docs(DocsRequest request,CancellationToken ct)=>Task.FromResult(docs.Search(request.Query,request.Limit));
    public Task<DocsCatalog> DocsCatalog(CancellationToken ct)=>Task.FromResult(docs.Catalog());
    public Task<DocText> DocsRead(string path,string? heading,CancellationToken ct)=>Task.FromResult(docs.Read(path,heading));
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
    public Task<object> Journal(int limit,CancellationToken ct)=>Call<object>("bridge/journal",new{limit},ct);
    public Task<object> Plan(string? value,CancellationToken ct)=>Call<object>("bridge/plan",new{value},ct);
    public Task<MapView> ReadMap(int x,int y,int z,int radius,CancellationToken ct)=>Call<MapView>("bridge/map",new{x,y,z,radius},ct);
    public Task<TileInspection> InspectTile(int x,int y,int z,string revision,CancellationToken ct)=>Call<TileInspection>("bridge/inspect",new{x,y,z,revision},ct);
    public Task<NearbyTargets> Nearby(CancellationToken ct)=>Call<NearbyTargets>("bridge/nearby",new{},ct);
    public Task<DocsAnswer> Docs(DocsRequest request,CancellationToken ct)=>Call<DocsAnswer>("bridge/docs",request,ct);
    public Task<DocsCatalog> DocsCatalog(CancellationToken ct)=>Call<DocsCatalog>("bridge/docs-catalog",new{},ct);
    public Task<DocText> DocsRead(string path,string? heading,CancellationToken ct)=>Call<DocText>("bridge/docs-read",new{path,heading},ct);
    public Task<ReferenceAnswer> Reference(ReferenceRequest request,CancellationToken ct)=>Call<ReferenceAnswer>("bridge/reference",request,ct);
    public Task<TargetInspection> InspectTarget(string targetId,string revision,CancellationToken ct)=>Call<TargetInspection>("bridge/target",new{targetId,revision},ct);
}
