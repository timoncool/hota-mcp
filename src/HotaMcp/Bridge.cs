using System.Text.Json;

namespace HotaMcp;

public sealed record OperationRequest(string OperationId,string Revision,string Element);
public sealed record OperationResult(string Status,string Message,Observation? Observation);
public sealed record JournalEntry(long Sequence,DateTimeOffset Time,string Kind,object Data);
public sealed record TargetView(string Id,string Kind,RouteView Route);
public sealed record NearbyTargets(string Revision,int HeroId,int Movement,List<TargetView> Targets,string Coverage);
public sealed record TargetInspection(string Id,string Kind,RouteView Route,string Revision);
public sealed record DebugSnapshot(Observation Observation,CaptureResult Capture,string ObservationPath);
public sealed record MoveRequest(string OperationId,string Revision,string TargetId);
public sealed record TextRequest(string Revision,string Element,string Text);

internal sealed class Bridge(WindowsGame game,int player,string stateDirectory) : IDisposable
{
    private readonly GameReader reader=new(game,player);
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly Dictionary<string,(OperationRequest Request,OperationResult Result)> operations=new();
    private readonly List<JournalEntry> journal=[];
    private string plan="";
    private readonly Dictionary<string,MapObject> targets=new();
    private readonly Dictionary<MapObject,string> targetIds=new();
    public async Task<OperationResult> Move(MoveRequest request,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if(string.IsNullOrWhiteSpace(request.OperationId)||request.OperationId.Length>100)throw new InvalidOperationException("Invalid operation ID");
            var identity=new OperationRequest(request.OperationId,request.Revision,"move:"+request.TargetId);
            if(operations.TryGetValue(request.OperationId,out var prior))
            {
                if(prior.Request!=identity)throw new InvalidOperationException("Operation ID reused with different arguments");
                return prior.Result;
            }
            if(!targets.TryGetValue(request.TargetId,out var target))throw new InvalidOperationException("Request nearby_targets first");
            var before=reader.Observe();
            if(before.Revision!=request.Revision||before.Screen!="adventure"||before.Hero is null)throw new InvalidOperationException("Fresh own-hero adventure observation required");
            new MapReader(game,player).ValidateTarget(before,target);
            var pending=new OperationResult("uncertain","Movement preparation started; inspect state before any retry with a new ID",null);
            operations.Add(request.OperationId,(identity,pending));Record("move_started",request);
            if(!before.Hero.PlannedDestination.SequenceEqual(new[]{target.X,target.Y,target.Z}))game.NativeAction(31,player,target.X|(target.Y<<8)|(target.Z<<16));
            Observation? planned=null;
            for(int i=0;i<20;i++)
            {
                await Task.Delay(50,CancellationToken.None);
                try{planned=reader.Observe();}catch(InvalidOperationException){continue;}
                if(planned.Hero?.Id==before.Hero.Id&&planned.Hero.PlannedDestination.SequenceEqual(new[]{target.X,target.Y,target.Z}))break;
            }
            if(planned?.Hero?.Id!=before.Hero.Id||!planned.Hero.PlannedDestination.SequenceEqual(new[]{target.X,target.Y,target.Z}))
            {
                Record("move_preparation_unconfirmed",new{request.OperationId,planned});return pending;
            }
            new MapReader(game,player).ValidateTarget(planned,target);
            if(planned.Hero.Movement!=before.Hero.Movement||!planned.Hero.Position.SequenceEqual(before.Hero.Position))throw new InvalidOperationException("Hero changed during route preparation");
            // M is the game's ordinary move-along-selected-path command.
            await game.KeyAsync(0x4d,0x32);
            DateTime deadline=DateTime.UtcNow.AddSeconds(10);
            while(DateTime.UtcNow<deadline)
            {
                await Task.Delay(100,CancellationToken.None);
                Observation after;
                try{after=reader.Observe();}catch(InvalidOperationException){continue;}
                bool arrived=after.Hero?.Position.SequenceEqual(new[]{target.X,target.Y,target.Z})==true;
                bool collected=after.Screen=="adventure"&&target.Kind is "resource" or "campfire"&&
                    after.Hero?.Id==before.Hero.Id&&after.Hero.Movement<before.Hero.Movement&&
                    !new MapReader(game,player).IsTargetPresent(after,target)&&after.Resources.Where((v,i)=>v>before.Resources[i]).Any();
                if(after.Screen!="adventure"||arrived||collected)
                {
                    var result=new OperationResult("completed",collected?"Target collected; object disappeared and resources increased":arrived?"Hero reached target cell":"Movement opened an interaction; read the dialog",after);
                    operations[request.OperationId]=(identity,result);Record("move_completed",new{request.OperationId,result});return result;
                }
            }
            Record("move_uncertain",request);return pending;
        }
        finally{gate.Release();}
    }
    public async Task<DebugSnapshot> Snapshot(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var before=reader.Observe();
            var capture=DebugCapture.Save(game,player,Path.Combine(stateDirectory,"captures"));
            var after=reader.Observe();
            if(before.Revision!=after.Revision)throw new InvalidOperationException("State changed during diagnostic capture; snapshot not confirmed");
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
    public async Task<NearbyTargets> Nearby(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var observation=reader.Observe();var hero=observation.Hero??throw new InvalidOperationException("Select a hero first");
            var region=new MapReader(game,player).Read(observation,hero.Position[0],hero.Position[1],hero.Position[2],12);
            var list=new List<TargetView>();
            foreach(var target in region.Objects)
            {
                if(!targetIds.TryGetValue(target,out var id)){id="target_"+Guid.NewGuid().ToString("N")[..12];targetIds.Add(target,id);targets.Add(id,target);}
                list.Add(new(id,target.Kind,new RouteReader(game,player).Read(observation,target)));
            }
            if(reader.Observe().Revision!=observation.Revision)throw new InvalidOperationException("State changed; request targets again");
            return new(observation.Revision,hero.Id,hero.Movement,list,"Recognized visible objects near selected hero; list is not exhaustive");
        }
        finally{gate.Release();}
    }
    public async Task<TargetInspection> InspectTarget(string targetId,string revision,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if(!targets.TryGetValue(targetId,out var target))throw new InvalidOperationException("Unknown target; request nearby_targets first");
            var before=reader.Observe();
            if(before.Revision!=revision)throw new InvalidOperationException("State changed; request nearby_targets again");
            var map=new MapReader(game,player);map.ValidateTarget(before,target);
            var route=new RouteReader(game,player).Read(before,target);
            var after=reader.Observe();
            if(after.Revision!=before.Revision)throw new InvalidOperationException("State changed while reading target");
            return new(targetId,target.Kind,route,after.Revision);
        }
        finally{gate.Release();}
    }
    public object Status() => new { phase="development", player, gamePid=game.Process.Id,
        capabilities=new[]{"observe_own_hero","observe_adventure_ui","open_system_options","return_to_game","visible_targets","route_preview","own_towns","town_construction","journal","plan"},
        unavailable=new[]{"full_map_coverage","movement","town_recruitment","battle","hotseat","lan","installer"} };
    public object Diagnostic()=>reader.DiagnosticPointers();
    public object RawUi()=>reader.DiagnosticDialog();
    public object MapDiagnostic()=>new MapReader(game,player).Diagnostic(reader.Observe());
    public async Task<MapView> ReadMap(int x,int y,int z,int radius,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var before=reader.Observe();var map=new MapReader(game,player);
            var first=map.Read(before,x,y,z,radius);var second=map.Read(before,x,y,z,radius);
            if(JsonSerializer.Serialize(first)!=JsonSerializer.Serialize(second)||reader.Observe().Revision!=before.Revision)
                throw new InvalidOperationException("Map changed while reading; observe again");
            return second;
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
            var map=new MapReader(game,player);var point=map.ScreenPoint(before,x,y,z);
            await game.MouseAsync(point.X,point.Y,before.Width,before.Height,false,ct);
            await Task.Delay(150,ct);map.VerifyMouse(x,y,z);
            var after=reader.Observe();
            if(after.Screen!="adventure")throw new InvalidOperationException("Screen changed during inspection");
            return new(x,y,z,after.Elements.SingleOrDefault(e=>e.Id==200)?.Text,after);
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
    public async Task<Observation> Observe(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try {return reader.Observe();} finally {gate.Release();}
    }
    public async Task<OperationResult> Click(OperationRequest request,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(request.OperationId)||request.OperationId.Length>100)
            throw new InvalidOperationException("Provide a unique operation ID of at most 100 characters");
        await gate.WaitAsync(ct);
        try
        {
            if(operations.TryGetValue(request.OperationId,out var previous))
            {
                if(previous.Request!=request) throw new InvalidOperationException("Operation ID reused with different arguments");
                return previous.Result;
            }
            var before=reader.Observe();
            if(before.Revision!=request.Revision) throw new InvalidOperationException("Observation is stale; observe again before acting");
            int nativeOperation,argument=0;string expected;
            var action=before.Actions.SingleOrDefault(a=>a.Key==request.Element);
            if(action is not null)
            {
                (nativeOperation,expected)=action.Key switch {
                    "menu:new" or "menu:load"=>(20,"game_type"),"menu:back"=>(21,"main_menu"),
                    "menu:single"=>(21,"scenario_selection"),"scenario:back"=>(22,"main_menu"),
                    "scenario:maps" or "scenario:players" or "scenario:random"=>(23,"scenario_selection"),
                    "scenario:start"=>(25,"adventure"),
                    "message:accept"=>(26,"adventure"),
                    "message:confirm"=>(29,"adventure"),"message:decline"=>(30,"adventure"),
                    "turn:end"=>(28,"adventure"),
                    "combat:spellbook"=>(35,"spellbook"),"spellbook:close"=>(36,"combat"),
                    _ when action.Key.StartsWith("spellbook:select:")=>(37,"combat"),
                    "spell:cancel"=>(36,"combat"),
                    "battle:accept"=>(39,"adventure"),
                    "game:load"=>(45,"message"),
                    "game:main_menu"=>(25,"main_menu"),
                    "save:confirm"=>(44,"message"),"game:save"=>(43,"save_game"),"recruit:max"=>(42,"recruitment"),"recruit:buy"=>(41,"town"),"recruit:cancel"=>(36,"town"),
                    "tavern:hire"=>(41,"town"),"tavern:close"=>(36,"town"),
                    _ when action.Key.StartsWith("spell:target:")=>(38,"combat"),
                    "combat:wait"=>(32,"combat"),"combat:defend"=>(33,"combat"),
                    _ when action.Key.StartsWith("combat:move:")||action.Key.StartsWith("combat:attack:")=>(34,"combat"),
                    _ when action.Key.StartsWith("setup:")=>(24,"scenario_selection"),
                    "town:construction"=>(4,"town_hall"),"town:close"=>(27,"adventure"),
                    "town:tavern"=>(40,"tavern"),
                    _ when action.Key.StartsWith("town:recruit:")=>(40,"recruitment"),
                    "construction:close"=>(10,"town"),"building:cancel"=>(6,"town_hall"),
                    "building:buy"=>(7,"town"),
                    _ when action.Key.StartsWith("town:open:")=>(3,"town"),
                    _ when action.Key.StartsWith("building:inspect:")=>(5,"building_confirmation"),
                    _=>throw new InvalidOperationException("Action not implemented")
                };
                if(nativeOperation is 3 or 5)argument=int.Parse(action.Key.Split(':')[2]);
                if(nativeOperation==20)argument=action.Key=="menu:new"?101:102;
                if(nativeOperation==21)argument=action.Key=="menu:single"?100:104;
                if(nativeOperation==23)argument=action.Key switch {"scenario:maps"=>128,"scenario:players"=>129,_=>130};
                if(nativeOperation==24)argument=ScenarioReader.Controls.Single(c=>ScenarioReader.Key(c)==action.Key).Id;
                if(nativeOperation==34)argument=action.Key.StartsWith("combat:move:")?int.Parse(action.Key.Split(':')[2]):before.Combat!.Stacks.Single(s=>s.Id==action.Key[14..]).Hex;
            }
            else
            {
                var item=before.Elements.SingleOrDefault(e=>e.Key==request.Element);
                if(item is null||!item.Interactive)throw new InvalidOperationException("Action unavailable");
                (nativeOperation,expected)=(before.Screen,item.Id,item.Asset) switch {
                    ("adventure",10,"iam009.def")=>(1,"system_options"),
                    ("system_options",30722,"soretrn.def")=>(2,"adventure"),
                    ("system_options",102,"soload.def")=>(45,"message"),
                    ("system_options",106,"sosave.def")=>(43,"save_game"),
                    ("message",30722,"iokay.def")=>(26,"adventure"),
                    ("message",30725,"iokay.def")=>(29,"adventure"),
                    ("message",30726,"icancel.def")=>(30,"adventure"),
                    _=>throw new InvalidOperationException("Use an available semantic action")
                };
            }
            var pending=new OperationResult("uncertain","Dispatch started; do not repeat using a new ID",null);
            operations.Add(request.OperationId,(request,pending));
            Record("operation_started",request);
            if(nativeOperation==3)
            {
                if(before.Towns.Count!=1||before.Towns[0].Id!=argument)throw new InvalidOperationException("Town sidebar selection currently verified for one owned town only");
                var portrait=before.Elements.Single(e=>e.Id==32&&e.Asset=="itpa.def");
                for(int i=0;i<2;i++)
                {
                    await game.MouseAsync(portrait.X+portrait.Width/2,portrait.Y+portrait.Height/2,before.Width,before.Height,true,CancellationToken.None);
                    await Task.Delay(150,CancellationToken.None);
                    if(reader.Observe().Screen=="town")break;
                }
            }
            else if(nativeOperation==40)
            {
                int building=request.Element=="town:tavern"?5:30+int.Parse(request.Element.Split(':')[2]);
                var point=new TownReader(game,player).BuildingPoint(building);
                await game.MouseAsync(point.X,point.Y,before.Width,before.Height,true,CancellationToken.None);
            }
            else if(nativeOperation is 50 or 51 or 52)
            {
                // Ordinary window mouse event into the modal loop: the dialog reads its own
                // pressed control and produces the real callback result.
                var button=before.Elements.Single(e=>e.Key==request.Element);
                await game.MouseAsync(button.X+button.Width/2,button.Y+button.Height/2,before.Width,before.Height,true,CancellationToken.None);
            }
            else if(nativeOperation==0)
            {
                // Structural file-browser button (load from disk, exit to menu): addressed by
                // its own reported control bounds, no guessing of screen coordinates.
                var button=before.Elements.Single(e=>e.Key==request.Element);
                await game.MouseAsync(button.X+button.Width/2,button.Y+button.Height/2,before.Width,before.Height,true,CancellationToken.None);
            }
            else if(nativeOperation==27)await game.KeyAsync(0x1b,0x01);
            else if(nativeOperation==28)await game.KeyAsync(0x45,0x12);
            else if(nativeOperation==32)await game.KeyAsync(0x57,0x11);
            else if(nativeOperation==33)await game.KeyAsync(0x44,0x20);
            else if(nativeOperation is 26 or 29)await game.KeyAsync(0x0d,0x1c);
            else if(nativeOperation==30)await game.KeyAsync(0x1b,0x01);
            else if(nativeOperation==45)await game.KeyAsync(0x4c,0x26);
            else if(nativeOperation==44)
            {
                var button=before.Elements.Single(e=>e.Id==186&&e.Asset=="scnrsav.def"&&e.Interactive);
                await game.MouseAsync(button.X+button.Width/2,button.Y+button.Height/2,before.Width,before.Height,true,CancellationToken.None);
            }
            else if(nativeOperation==43)await game.KeyAsync(0x53,0x1f);
            else if(nativeOperation==42)await game.KeyAsync(0x4d,0x32);
            else if(nativeOperation is 39 or 41)await game.KeyAsync(0x0d,0x1c);
            else if(nativeOperation==36)await game.KeyAsync(0x1b,0x01);
            else if(nativeOperation==37)
            {
                var item=before.Elements.Single(e=>e.Id==int.Parse(request.Element.Split(':')[2]));
                await game.MouseAsync(item.X+item.Width/2,item.Y+item.Height/2,before.Width,before.Height,true,CancellationToken.None);
            }
            else if(nativeOperation==35)await game.KeyAsync(0x43,0x2e);
            else if(nativeOperation==34||nativeOperation==38)
            {
                if(nativeOperation==38)argument=before.Combat!.Stacks.Single(s=>s.Id==request.Element[13..]).Hex;
                var point=new CombatReader(game,player).Point(argument);
                await game.MouseAsync(point.X,point.Y,before.Width,before.Height,true,CancellationToken.None);
            }
            else game.NativeAction(nativeOperation,player,argument);
            var deadline=DateTime.UtcNow.AddSeconds(nativeOperation is 25 or 32 or 33 or 34?10:3);
            while(DateTime.UtcNow<deadline)
            {
                await Task.Delay(70,CancellationToken.None);
                Observation? after=null;
                try {after=reader.Observe();} catch(InvalidOperationException) { }
                if(nativeOperation is 32 or 33 or 34 or 38 && after?.Screen=="battle_result")
                {
                    var finished=new OperationResult("completed","Battle result screen confirmed; read and accept the result",after);
                    operations[request.OperationId]=(request,finished);Record("battle_finished",new{request.OperationId,after});
                    return finished;
                }
                bool settingConfirmed=nativeOperation!=24||after?.Setup?.Fields.SelectMany(f=>f.Choices).Any(c=>c.Action==request.Element&&c.Selected)==true;
                bool turnConfirmed=nativeOperation!=28||after is not null&&(after.Screen=="message"||!after.Date.SequenceEqual(before.Date));
                bool logRequired=nativeOperation is 32 or 33 or 38||request.Element.StartsWith("combat:attack:");
                bool logConfirmed=!logRequired||after?.Combat is not null&&after.Combat.LogCount>before.Combat!.LogCount;
                if(nativeOperation==38)logConfirmed=logConfirmed&&after?.Hero?.Mana<before.Hero?.Mana;
                bool combatConfirmed=nativeOperation is not (32 or 33 or 34)||after?.Combat?.OwnTurn==true&&(after.Combat.ActiveStack!=before.Combat?.ActiveStack||after.Combat.Round!=before.Combat?.Round);
                // A screen change must be visible in the revision; screen equality alone is not evidence.
                bool screenChanged=after is not null&&after.Screen!=before.Screen&&after.Revision!=before.Revision;
                bool sameScreenReset=after is not null&&after.Screen==before.Screen&&after.Revision!=before.Revision;
                if(after is not null&&((screenChanged&&(after.Screen==expected||nativeOperation==28&&after.Screen=="message"))||sameScreenReset)&&settingConfirmed&&turnConfirmed&&combatConfirmed&&logConfirmed)
                {
                    if(after.Combat is not null&&before.Combat is not null)
                        Record("combat_action_evidence",new{request.OperationId,Action=request.Element,BeforeLogCount=before.Combat.LogCount,AfterLogCount=after.Combat.LogCount,Entries=after.Combat.Log.Where(e=>e.Index>=before.Combat.LogCount).ToArray()});
                    var result=new OperationResult("completed",screenChanged?"Screen transition confirmed by revision change":"Same screen, state change confirmed by revision",after);
                    operations[request.OperationId]=(request,result);Record("operation_completed",new{request.OperationId,after.Revision,after.Screen,BeforeScreen=before.Screen});
                    return result;
                }
            }
            Record("operation_uncertain",new{request.OperationId});return pending;
        }
        finally {gate.Release();}
    }
    public async Task<OperationResult> EnterText(TextRequest request,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if(string.IsNullOrEmpty(request.Text)||request.Text.Length>64||request.Text.Any(c=>c<32||c=='\\'||c=='/'||c==':'||c=='*'||c=='?'||c=='"'||c=='<'||c=='>'||c=='|'))
                throw new InvalidOperationException("Text must be 1-64 characters without path separators");
            var before=reader.Observe();
            if(before.Revision!=request.Revision)throw new InvalidOperationException("Observation is stale; observe again before acting");
            var field=before.Elements.SingleOrDefault(e=>e.Key==request.Element);
            if(field is null)throw new InvalidOperationException("Unknown edit control; observe again");
            // Focus the ordinary edit control with a window mouse event, then type characters.
            await game.MouseAsync(field.X+field.Width/2,field.Y+field.Height/2,before.Width,before.Height,true,CancellationToken.None);
            await Task.Delay(200,CancellationToken.None);
            await game.TextAsync(request.Text);
            await Task.Delay(200,CancellationToken.None);
            var after=reader.Observe();
            if(after.Screen!=before.Screen)throw new InvalidOperationException("Screen changed while entering text");
            Record("text_entered",new{request.Element,Length=request.Text.Length,Screen=before.Screen});
            return new OperationResult("completed","Text entered into the addressed edit control; read it back before confirming",after);
        }
        finally{gate.Release();}
    }
    public async Task<object> GetJournal(int limit,CancellationToken ct)    {
        await gate.WaitAsync(ct);try{return journal.TakeLast(Math.Clamp(limit,1,100)).ToArray();}finally{gate.Release();}
    }
    public async Task<object> Plan(string? value,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if(value!=null){if(value.Length>12000)throw new InvalidOperationException("Plan too large");plan=value;Record("plan_updated",new{plan});}
            return new{plan,player};
        }
        finally{gate.Release();}
    }
    public void Dispose(){game.Dispose();gate.Dispose();}
}

public interface IGameEndpoint
{
    Task<OperationResult> Move(MoveRequest request,CancellationToken ct);
    Task<DebugSnapshot> Snapshot(CancellationToken ct);
    Task<object> Start(CancellationToken ct);
    Task<object> Graphics(string? renderer,CancellationToken ct);
    Task<CaptureResult> Capture(CancellationToken ct);
    Task<object> Status(CancellationToken ct);
    Task<Observation> Observe(CancellationToken ct);
    Task<OperationResult> Click(OperationRequest request,CancellationToken ct);
    Task<OperationResult> EnterText(TextRequest request,CancellationToken ct);
    Task<object> Journal(int limit,CancellationToken ct);
    Task<object> Plan(string? value,CancellationToken ct);
    Task<MapView> ReadMap(int x,int y,int z,int radius,CancellationToken ct);
    Task<TileInspection> InspectTile(int x,int y,int z,string revision,CancellationToken ct);
    Task<NearbyTargets> Nearby(CancellationToken ct);
    Task<TargetInspection> InspectTarget(string targetId,string revision,CancellationToken ct);
}

internal sealed class LocalEndpoint(Bridge bridge) : IGameEndpoint
{
    public Task<OperationResult> Move(MoveRequest request,CancellationToken ct)=>bridge.Move(request,ct);
    public Task<DebugSnapshot> Snapshot(CancellationToken ct)=>bridge.Snapshot(ct);
    public Task<object> Start(CancellationToken ct)=>throw new InvalidOperationException("Game is already attached");
    public Task<object> Graphics(string? renderer,CancellationToken ct)=>throw new InvalidOperationException("Launcher host required");
    public Task<CaptureResult> Capture(CancellationToken ct)=>bridge.Capture(ct);
    public Task<object> Status(CancellationToken ct)=>Task.FromResult(bridge.Status());
    public Task<Observation> Observe(CancellationToken ct)=>bridge.Observe(ct);
    public Task<OperationResult> Click(OperationRequest request,CancellationToken ct)=>bridge.Click(request,ct);
    public Task<OperationResult> EnterText(TextRequest request,CancellationToken ct)=>bridge.EnterText(request,ct);
    public Task<object> Journal(int limit,CancellationToken ct)=>bridge.GetJournal(limit,ct);
    public Task<object> Plan(string? value,CancellationToken ct)=>bridge.Plan(value,ct);
    public Task<MapView> ReadMap(int x,int y,int z,int radius,CancellationToken ct)=>bridge.ReadMap(x,y,z,radius,ct);
    public Task<TileInspection> InspectTile(int x,int y,int z,string revision,CancellationToken ct)=>bridge.InspectTile(x,y,z,revision,ct);
    public Task<NearbyTargets> Nearby(CancellationToken ct)=>bridge.Nearby(ct);
    public Task<TargetInspection> InspectTarget(string targetId,string revision,CancellationToken ct)=>bridge.InspectTarget(targetId,revision,ct);
}

internal sealed class RemoteEndpoint(HttpClient client) : IGameEndpoint
{
    public Task<OperationResult> Move(MoveRequest request,CancellationToken ct)=>Call<OperationResult>("bridge/move",request,ct);
    public Task<DebugSnapshot> Snapshot(CancellationToken ct)=>Call<DebugSnapshot>("bridge/debug-snapshot",new{},ct);
    public Task<object> Start(CancellationToken ct)=>Call<object>("bridge/start",new{},ct);
    public Task<object> Graphics(string? renderer,CancellationToken ct)=>Call<object>("bridge/graphics",new{renderer},ct);
    public Task<CaptureResult> Capture(CancellationToken ct)=>Call<CaptureResult>("bridge/debug-capture",new{},ct);
    private async Task<T> Call<T>(string route,object body,CancellationToken ct)
    {
        using var response=await client.PostAsJsonAsync(route,body,ct);
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException(await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken:ct))!;
    }
    public Task<object> Status(CancellationToken ct)=>Call<object>("bridge/status",new{},ct);
    public Task<Observation> Observe(CancellationToken ct)=>Call<Observation>("bridge/observe",new{},ct);
    public Task<OperationResult> Click(OperationRequest request,CancellationToken ct)=>Call<OperationResult>("bridge/click",request,ct);
    public Task<OperationResult> EnterText(TextRequest request,CancellationToken ct)=>Call<OperationResult>("bridge/text",request,ct);
    public Task<object> Journal(int limit,CancellationToken ct)=>Call<object>("bridge/journal",new{limit},ct);
    public Task<object> Plan(string? value,CancellationToken ct)=>Call<object>("bridge/plan",new{value},ct);
    public Task<MapView> ReadMap(int x,int y,int z,int radius,CancellationToken ct)=>Call<MapView>("bridge/map",new{x,y,z,radius},ct);
    public Task<TileInspection> InspectTile(int x,int y,int z,string revision,CancellationToken ct)=>Call<TileInspection>("bridge/inspect",new{x,y,z,revision},ct);
    public Task<NearbyTargets> Nearby(CancellationToken ct)=>Call<NearbyTargets>("bridge/nearby",new{},ct);
    public Task<TargetInspection> InspectTarget(string targetId,string revision,CancellationToken ct)=>Call<TargetInspection>("bridge/target",new{targetId,revision},ct);
}

