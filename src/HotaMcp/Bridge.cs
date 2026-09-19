using System.Text.Json;

namespace HotaMcp;

public sealed record OperationRequest(string OperationId,string Revision,string Element);
public sealed record OperationResult(string Status,string Message,Observation? Observation);
public sealed record JournalEntry(long Sequence,DateTimeOffset Time,string Kind,object Data);

internal sealed class Bridge(WindowsGame game,int player,string stateDirectory) : IDisposable
{
    private readonly GameReader reader=new(game,player);
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly Dictionary<string,(OperationRequest Request,OperationResult Result)> operations=new();
    private readonly List<JournalEntry> journal=[];
    private string plan="";
    public object Status() => new { phase="development", player, gamePid=game.Process.Id,
        capabilities=new[]{"observe_own_hero","observe_adventure_ui","open_system_options","return_to_game","journal","plan"},
        unavailable=new[]{"map","movement","town","battle","hotseat","lan","installer"} };
    public object Diagnostic()=>reader.DiagnosticPointers();
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
            var item=before.Elements.SingleOrDefault(e=>e.Key==request.Element);
            if(item is null || !item.Interactive) throw new InvalidOperationException("Element is not an available action");
            string expected=(before.Screen,item.Id,item.Asset) switch {
                ("adventure",10,"iam009.def")=>"system_options",
                ("system_options",30722,"soretrn.def")=>"adventure",
                _=>throw new InvalidOperationException("This UI action has not been validated yet")
            };
            var pending=new OperationResult("uncertain","Dispatch started; do not repeat using a new ID",null);
            operations.Add(request.OperationId,(request,pending));
            Record("operation_started",request);
            await game.MouseAsync(item.X+item.Width/2,item.Y+item.Height/2,before.Width,before.Height,true,ct);
            var deadline=DateTime.UtcNow.AddSeconds(3);
            while(DateTime.UtcNow<deadline)
            {
                await Task.Delay(70,CancellationToken.None);
                Observation? after=null;
                try {after=reader.Observe();} catch(InvalidOperationException) { }
                if(after?.Screen==expected)
                {
                    var result=new OperationResult("completed","Expected screen confirmed",after);
                    operations[request.OperationId]=(request,result);Record("operation_completed",new{request.OperationId,after.Revision,after.Screen});
                    return result;
                }
            }
            Record("operation_uncertain",new{request.OperationId});return pending;
        }
        finally {gate.Release();}
    }
    public async Task<object> GetJournal(int limit,CancellationToken ct)
    {
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
    Task<object> Status(CancellationToken ct);
    Task<Observation> Observe(CancellationToken ct);
    Task<OperationResult> Click(OperationRequest request,CancellationToken ct);
    Task<object> Journal(int limit,CancellationToken ct);
    Task<object> Plan(string? value,CancellationToken ct);
}

internal sealed class LocalEndpoint(Bridge bridge) : IGameEndpoint
{
    public Task<object> Status(CancellationToken ct)=>Task.FromResult(bridge.Status());
    public Task<Observation> Observe(CancellationToken ct)=>bridge.Observe(ct);
    public Task<OperationResult> Click(OperationRequest request,CancellationToken ct)=>bridge.Click(request,ct);
    public Task<object> Journal(int limit,CancellationToken ct)=>bridge.GetJournal(limit,ct);
    public Task<object> Plan(string? value,CancellationToken ct)=>bridge.Plan(value,ct);
}

internal sealed class RemoteEndpoint(HttpClient client) : IGameEndpoint
{
    private async Task<T> Call<T>(string route,object body,CancellationToken ct)
    {
        using var response=await client.PostAsJsonAsync(route,body,ct);
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException(await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken:ct))!;
    }
    public Task<object> Status(CancellationToken ct)=>Call<object>("bridge/status",new{},ct);
    public Task<Observation> Observe(CancellationToken ct)=>Call<Observation>("bridge/observe",new{},ct);
    public Task<OperationResult> Click(OperationRequest request,CancellationToken ct)=>Call<OperationResult>("bridge/click",request,ct);
    public Task<object> Journal(int limit,CancellationToken ct)=>Call<object>("bridge/journal",new{limit},ct);
    public Task<object> Plan(string? value,CancellationToken ct)=>Call<object>("bridge/plan",new{value},ct);
}
