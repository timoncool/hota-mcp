using System.ComponentModel;
using System.Diagnostics;

namespace HotaMcp;

// Owns attachment separately from the server: the launcher can start before the game.
internal sealed class GameSession(int? requestedPid,int player,string directory) : IGameEndpoint, IDisposable
{
    private readonly SemaphoreSlim gate=new(1,1);
    private WindowsGame? game;
    private Bridge? bridge;
    private string state="waiting_for_game",detail="Start HotA to connect";

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
        {game?.Dispose();game=null;bridge=null;state="attachment_error";detail=e.Message;}
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
            return state=="attached"?$"HotA: PID {game!.Process.Id}\r\nНаблюдение, системные опции, журнал и план.\r\nКарта, город и бой ещё в разработке.":
                state=="waiting_for_game"?"Ожидание запуска HotA. Сервер готов к подключению.":$"Подключение игры: {detail}";
        }
        finally{gate.Release();}
    }
    public Task<Observation> Observe(CancellationToken ct)=>WithGame(b=>b.Observe(ct),ct);
    public Task<OperationResult> Click(OperationRequest request,CancellationToken ct)=>WithGame(b=>b.Click(request,ct),ct);
    public Task<object> Journal(int limit,CancellationToken ct)=>WithGame(b=>b.GetJournal(limit,ct),ct);
    public Task<object> Plan(string? value,CancellationToken ct)=>WithGame(b=>b.Plan(value,ct),ct);
    public Task<MapView> ReadMap(int x,int y,int z,int radius,CancellationToken ct)=>WithGame(b=>b.ReadMap(x,y,z,radius,ct),ct);
    public Task<TileInspection> InspectTile(int x,int y,int z,string revision,CancellationToken ct)=>WithGame(b=>b.InspectTile(x,y,z,revision,ct),ct);
    public Task<NearbyTargets> Nearby(CancellationToken ct)=>WithGame(b=>b.Nearby(ct),ct);
    public Task<TargetInspection> InspectTarget(string targetId,string revision,CancellationToken ct)=>WithGame(b=>b.InspectTarget(targetId,revision,ct),ct);
    public void Dispose(){bridge?.Dispose();gate.Dispose();}
}
