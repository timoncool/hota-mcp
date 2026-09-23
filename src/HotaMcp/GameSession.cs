using System.ComponentModel;
using System.Diagnostics;

namespace HotaMcp;

// Owns attachment separately from the server: the launcher can start before the game.
internal sealed class GameSession(int? requestedPid,int player,string directory,int? launcherPid=null) : IGameEndpoint, IDisposable
{
    private readonly SemaphoreSlim gate=new(1,1);
    private WindowsGame? game;
    private Bridge? bridge;
    private Process? adapter;
    private DateTime launchRequested;
    private string state="waiting_for_game",detail="Start HotA to connect";
    public async Task<object> Graphics(string? renderer,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try{return LauncherActions.Graphics(launcherPid??throw new InvalidOperationException("HD Launcher host required"),renderer);}
        finally{gate.Release();}
    }
    public async Task<object> Start(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            Refresh();
            if(game is not null)return new{state="already_running",gamePid=game.Process.Id};
            if(state!="waiting_for_game")throw new InvalidOperationException(detail);
            if(launcherPid is null)throw new InvalidOperationException("Server must be hosted by HD Launcher to start the game");
            if(DateTime.UtcNow-launchRequested<TimeSpan.FromSeconds(30))return new{state="launch_pending"};
            LauncherActions.Play(launcherPid.Value);launchRequested=DateTime.UtcNow;
            return new{state="launch_requested"};
        }
        finally{gate.Release();}
    }
    private async Task EnsureAdapter(CancellationToken ct)
    {
        if(game!.NativeReady)return;
        if(adapter is null||adapter.HasExited)
        {
            adapter?.Dispose();
            string root=Path.Combine(AppContext.BaseDirectory,"native");
            string exe=Path.Combine(root,"game-attach.exe"),dll=Path.Combine(root,"hota_game_bridge.dll");
            if(!File.Exists(exe)||!File.Exists(dll))throw new InvalidOperationException("Packaged native adapter missing");
            var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root};
            start.ArgumentList.Add(game.Process.Id.ToString());start.ArgumentList.Add(dll);
            adapter=Process.Start(start)??throw new InvalidOperationException("Cannot start native adapter");
        }
        for(int i=0;i<60;i++)
        {
            if(game.NativeReady)return;
            if(adapter.HasExited)throw new InvalidOperationException($"Native adapter failed: {adapter.ExitCode}");
            await Task.Delay(50,ct);
        }
        throw new InvalidOperationException("Native adapter is not ready yet");
    }

    private void Refresh()
    {
        if(game is not null && !game.Process.HasExited)return;
        bridge?.Dispose();bridge=null;game=null;
        Process[] candidates=[];
        try
        {
            if(requestedPid is int pid)
            {
                try{candidates=[Process.GetProcessById(pid)];}
                catch(ArgumentException){state="waiting_for_game";detail="Configured game process is not running";return;}
            }
            else candidates=Process.GetProcessesByName("h3hota HD");
            if(candidates.Length!=1)
            {
                state=candidates.Length==0?"waiting_for_game":"ambiguous_game";
                detail=candidates.Length==0?"Start HotA to connect":"Multiple HotA processes; select a process explicitly";
                return;
            }
            game=new WindowsGame(candidates[0].Id);
            bridge=new Bridge(game,player,Path.Combine(directory,"sessions",Guid.NewGuid().ToString("N")));
            state="attached";detail="Game attached; observations remain subject to player and screen checks";
        }
        catch(Exception e) when(e is InvalidOperationException or Win32Exception or IOException or ArgumentException)
        {
            game?.Dispose();game=null;bridge=null;state="attachment_error";detail=e.Message;
            // No dialog at all means an intro video is playing; a click skips it, as a player does.
            if(candidates.Length==1&&e.Message.Contains("no active dialog",StringComparison.Ordinal)
               &&WindowsGame.SkipIntro(candidates[0]))
                detail="Идёт заставка: отправлен щелчок в окно игры для пропуска, спроси статус ещё раз";
        }
        finally{foreach(var candidate in candidates)candidate.Dispose();}
    }

    private async Task<T> WithGame<T>(Func<Bridge,Task<T>> action,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try{Refresh();return await action(bridge??throw new InvalidOperationException(detail));}
        finally{gate.Release();}
    }
    public async Task<object> Status(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try{Refresh();return new{state,detail,gamePid=game?.Process.Id,player,game=bridge?.Status()};}
        finally{gate.Release();}
    }
    public async Task<string> LauncherStatus(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            Refresh();
            return state=="attached"?$"HotA: PID {game!.Process.Id}\r\nКарта, города, герои, обмен, таверна, бой и меню партии.\r\nСправочник игры, журнал и план агента.":
                state=="waiting_for_game"?"Ожидание запуска HotA. Сервер готов к подключению.":$"Подключение игры: {detail}";
        }
        finally{gate.Release();}
    }
    public Task<Observation> Observe(CancellationToken ct)=>WithGame(b=>b.Observe(ct),ct);
    public Task<OperationResult> Move(MoveRequest request,CancellationToken ct)=>WithGame(async b=>{await EnsureAdapter(ct);return await b.Move(request,ct);},ct);
    public Task<OperationResult> Attack(MoveRequest request,CancellationToken ct)=>WithGame(async b=>{await EnsureAdapter(ct);return await b.Attack(request,ct);},ct);
    public Task<OperationResult> MapClick(MapClickRequest request,CancellationToken ct)=>WithGame(async b=>{await EnsureAdapter(ct);return await b.MapClick(request,ct);},ct);
    public Task<OperationResult> MoveToTile(TileMoveRequest request,CancellationToken ct)=>WithGame(async b=>{await EnsureAdapter(ct);return await b.MoveToTile(request,ct);},ct);
    public Task<DebugSnapshot> Snapshot(CancellationToken ct)=>WithGame(b=>b.Snapshot(ct),ct);
    public Task<CaptureResult> Capture(CancellationToken ct)=>WithGame(b=>b.Capture(ct),ct);
    public Task<OperationResult> Click(OperationRequest request,CancellationToken ct)=>WithGame(async b=>{await EnsureAdapter(ct);return await b.Click(request,ct);},ct);
    public Task<OperationResult> EnterText(TextRequest request,CancellationToken ct)=>WithGame(b=>b.EnterText(request,ct),ct);
    public Task<ElementCard> InspectElement(InspectRequest request,CancellationToken ct)=>WithGame(b=>b.InspectElement(request,ct),ct);
    public Task<CellCard> InspectCell(CellRequest request,CancellationToken ct)=>WithGame(b=>b.InspectCell(request,ct),ct);
    public Task<object> ProbeScreen(CancellationToken ct)=>WithGame(b=>Task.FromResult(b.ProbeScreen()),ct);
    public Task<object> Memory(uint address,int length,CancellationToken ct)=>WithGame(b=>Task.FromResult(b.Memory(address,length)),ct);
    public Task<object> TileBytes(int x,int y,int z,CancellationToken ct)=>WithGame(b=>Task.FromResult(b.TileBytes(x,y,z)),ct);
    public Task<object> SendKey(KeyRequest request,CancellationToken ct)=>WithGame(b=>b.SendKey(request,ct),ct);
    public Task<object> Press(PressRequest request,CancellationToken ct)=>WithGame(b=>b.Press(request,ct),ct);
    public Task<object> PressRight(PressRequest request,CancellationToken ct)=>WithGame(b=>b.PressRight(request,ct),ct);
    public Task<object> Journal(int limit,CancellationToken ct)=>WithGame(b=>b.GetJournal(limit,ct),ct);
    public Task<object> Plan(string? value,CancellationToken ct)=>WithGame(b=>b.Plan(value,ct),ct);
    public Task<MapView> ReadMap(int x,int y,int z,int radius,CancellationToken ct)=>WithGame(b=>b.ReadMap(x,y,z,radius,ct),ct);
    public Task<TileInspection> InspectTile(int x,int y,int z,string revision,CancellationToken ct)=>WithGame(b=>b.InspectTile(x,y,z,revision,ct),ct);
    public Task<NearbyTargets> Nearby(CancellationToken ct)=>WithGame(b=>b.Nearby(ct),ct);
    private readonly DocsIndex docsIndex=new();
    public Task<DocsAnswer> Docs(DocsRequest request,CancellationToken ct)=>Task.FromResult(docsIndex.Search(request.Query,request.Limit,request.Detail));
    public Task<DocsCatalog> DocsCatalog(string? path,CancellationToken ct)=>Task.FromResult(docsIndex.Catalog(path));
    public Task<DocText> DocsRead(string path,string? heading,int offset,int maxChars,CancellationToken ct)=>Task.FromResult(docsIndex.Read(path,heading,offset,maxChars));
    public Task<ReferenceAnswer> Reference(ReferenceRequest request,CancellationToken ct)=>Task.FromResult(docsIndex.Reference(request.Name,request.Kind,request.Limit));
    public Task<TargetInspection> InspectTarget(string targetId,string revision,CancellationToken ct)=>WithGame(b=>b.InspectTarget(targetId,revision,ct),ct);
    public Task<RouteView> InspectPath(int x,int y,int z,string revision,CancellationToken ct)=>WithGame(async b=>{await EnsureAdapter(ct);return await b.InspectPath(x,y,z,revision,ct);},ct);
    public void Dispose(){bridge?.Dispose();adapter?.Dispose();gate.Dispose();}
}
