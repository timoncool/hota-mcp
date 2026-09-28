using HotaMcp;
using System.Text.Json;

if(args is ["--job-probe",var output])
{
    if(!JobProbe.IsProcessInJob(System.Diagnostics.Process.GetCurrentProcess().Handle,IntPtr.Zero,out bool inJob))
        throw new Exception("Cannot inspect process job");
    var limits=new byte[144];
    if(inJob&&!JobProbe.QueryInformationJobObject(IntPtr.Zero,9,limits,(uint)limits.Length,IntPtr.Zero))
        throw new Exception("Cannot inspect job limits");
    uint flags=BitConverter.ToUInt32(limits,16);
    File.WriteAllText(output,$"inJob={inJob};flags={flags:X};pid={Environment.ProcessId}");
    return;
}

static void Check(bool expected,byte[] header,int player,int colour,string scenario)
{
    bool actual=TeamRelations.AreAllies(header,player,colour);
    if(actual!=expected)throw new Exception($"{scenario}: expected {expected}, got {actual}");
}

byte[] teams=[2,0,1,1,0,255,255,255,255];
Check(true,teams,1,2,"blue and brown on the same team");
Check(false,teams,1,0,"blue and red on opposing teams");
Check(false,teams,1,3,"blue and green on opposing teams");
Check(false,teams,1,1,"a player is not their own ally");
Check(false,teams,1,4,"an absent colour is not an ally");
Check(false,teams,4,1,"an absent player has no allies");
Check(false,[0,0,0,0,0,255,255,255,255],1,2,"without teams, other colours are opponents");
Check(false,[9,0,0,0,0,255,255,255,255],1,2,"invalid team count is rejected");
Check(false,[2,0],1,0,"truncated header is rejected");
Check(false,teams,-1,0,"invalid player is rejected");
Check(false,teams,1,8,"invalid colour is rejected");
Console.WriteLine("PASS team privacy: allies are logged, enemies and absent colours are excluded");

var waiting=OpponentTurnObservation.Waiting(1);
using var json=JsonDocument.Parse(JsonSerializer.Serialize(waiting));
var root=json.RootElement;
if(root.GetProperty("Screen").GetString()!="waiting"||root.GetProperty("Player").GetInt32()!=1
   ||root.GetProperty("Date").GetArrayLength()!=0||root.GetProperty("Resources").GetArrayLength()!=0
   ||root.GetProperty("Hero").ValueKind!=JsonValueKind.Null||root.GetProperty("Towns").GetArrayLength()!=0
   ||root.GetProperty("Heroes").GetArrayLength()!=0||root.GetProperty("ForeignHeroes").GetArrayLength()!=0
   ||root.GetProperty("ForeignArmy").GetArrayLength()!=0||root.GetProperty("ForeignHero").ValueKind!=JsonValueKind.Null
   ||root.GetProperty("Actions").GetArrayLength()!=0||root.GetProperty("Elements").GetArrayLength()!=0
   ||root.GetProperty("Side").ValueKind!=JsonValueKind.Null||root.GetProperty("Width").GetInt32()!=0
   ||root.GetProperty("Height").GetInt32()!=0||root.GetProperty("Revision").GetString()!="")
    throw new Exception("Opponent-turn waiting observation exposed game state");
Console.WriteLine("PASS opponent-turn waiting observation contains no other-side state");

var handedOff=OpponentTurnObservation.AfterHandoff(new OperationResult("completed","enemy hero moved",
    new Observation("secret",1,[2,1,1],[100],null,"enemy_hero_card",800,600,[])
    {ForeignHero="opponent",ForeignArmy=["army"]}),1);
if(handedOff.Status!="completed"||handedOff.Message.Contains("enemy",StringComparison.OrdinalIgnoreCase)
   ||handedOff.Observation?.Screen!="waiting"||handedOff.Observation.ForeignHero is not null
   ||handedOff.Observation.ForeignArmy.Count!=0)
    throw new Exception("Turn hand-off returned another player's screen");
Console.WriteLine("PASS turn hand-off preserves operation status without enemy details");

if(!CombatTurnRules.OwnActor(1,0,1,1)||CombatTurnRules.OwnActor(1,0,1,0)
   ||CombatTurnRules.OwnActor(1,0,2,1)||CombatTurnRules.OwnActor(1,0,1,-1))
    throw new Exception("Defender combat actor ownership is incorrect");
Console.WriteLine("PASS defender is allowed only when its own combat side acts");

if(VisitStatus.FromHint("Сад Откровения (Посещено)")!="visited"
   ||VisitStatus.FromHint("Сад Откровения (Не посещено)")!="not_visited")
    throw new Exception("Visit status did not follow the selected hero's exact game hint");
if(VisitStatus.FromHint("Сад Откровения: даруется лишь единожды")!="unknown"
   ||VisitStatus.FromHint(null)!="unknown")
    throw new Exception("Unknown visit state was guessed from an object rule or missing hint");
Console.WriteLine("PASS visit state is explicit only for exact game markers");

string token="first";
var sent=new List<string>();
using(var client=new HttpClient(new StubHandler(request=>
{
    sent.Add(request.Headers.Authorization?.Parameter??"");
    return new(System.Net.HttpStatusCode.OK){Content=new StringContent("{}")};
})){BaseAddress=new Uri("http://localhost/")})
{
    var remote=new RemoteEndpoint(client,()=>token,_=>Task.CompletedTask);
    await remote.Status(default);
    token="second";
    await remote.Status(default);
    if(!sent.SequenceEqual(new[]{"first","second"}))throw new Exception("Stdio retained an old token");
}
Console.WriteLine("PASS stdio reloads the service token on every request");

int requests=0,restores=0;
using(var client=new HttpClient(new StubHandler(_=>
{
    requests++;throw new HttpRequestException("offline");
})){BaseAddress=new Uri("http://localhost/")})
{
    var remote=new RemoteEndpoint(client,()=>token,_=>{restores++;return Task.CompletedTask;});
    try{await remote.Status(default);throw new Exception("Offline status should fail");}
    catch(InvalidOperationException e)when(e.Message.Contains("Start it explicitly")){}
    if(requests!=1||restores!=0)throw new Exception("Read-only request started HotA automatically");
}
Console.WriteLine("PASS read-only request never starts HotA automatically");

requests=0;restores=0;
using(var client=new HttpClient(new StubHandler(_=>{requests++;throw new HttpRequestException("offline");}))
    {BaseAddress=new Uri("http://localhost/")})
{
    var remote=new RemoteEndpoint(client,()=>token,_=>{restores++;return Task.CompletedTask;});
    try{await remote.Graphics(null,default);throw new Exception("Action should report connection failure");}
    catch(InvalidOperationException e)when(e.Message.Contains("outcome is unknown")){}
    if(requests!=1||restores!=0)throw new Exception("Mutation was automatically replayed");
}
Console.WriteLine("PASS disconnected action is never replayed");

requests=0;restores=0;
using(var client=new HttpClient(new StubHandler(_=>
    {requests++;return new(System.Net.HttpStatusCode.OK){Content=new StringContent("{}")};}))
    {BaseAddress=new Uri("http://localhost/")})
{
    var remote=new RemoteEndpoint(client,()=>token,_=>{restores++;return Task.CompletedTask;});
    await remote.Start(default);
    if(requests!=1||restores!=1)throw new Exception("Explicit start_game did not start the service");
}
Console.WriteLine("PASS only explicit start_game starts the service");

string probe=Path.Combine(Path.GetTempPath(),"hota-job-"+Guid.NewGuid().ToString("N")+".txt");
try
{
    ServiceBootstrap.StartDetached(Environment.ProcessPath!,$"--job-probe \"{probe}\"",AppContext.BaseDirectory);
    for(int i=0;i<100&&!File.Exists(probe);i++)await Task.Delay(50);
    if(!File.Exists(probe))throw new Exception("Detached process did not report its job");
    string report=File.ReadAllText(probe);
    Console.WriteLine(report);
    uint childFlags=Convert.ToUInt32(report.Split("flags=")[1].Split(';')[0],16);
    if((childFlags&0x2000)!=0)throw new Exception("Detached process inherited a kill-on-close job");
}
finally{if(File.Exists(probe))File.Delete(probe);}
Console.WriteLine("PASS desktop launch does not inherit the harness kill-on-close job");

sealed class StubHandler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        =>Task.FromResult(respond(request));
}

static class JobProbe
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll",SetLastError=true)]
    public static extern bool QueryInformationJobObject(IntPtr job,int infoClass,[System.Runtime.InteropServices.Out]byte[] info,uint length,IntPtr returned);
    [System.Runtime.InteropServices.DllImport("kernel32.dll",SetLastError=true)]
    [return:System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static extern bool IsProcessInJob(IntPtr process,IntPtr job,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]out bool inJob);
}
