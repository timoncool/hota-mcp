using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using HotaMcp;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
string? Value(string name){int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:null;}
bool stdio=args.Contains("--stdio"),diagnostic=args.Contains("--diagnostic");
string directory=Value("--state-dir")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HotaMcp");
string tokenFile=Path.Combine(directory,"connection.token");
string endpoint=Value("--endpoint")??"http://127.0.0.1:18773";
var endpointUri=new Uri(endpoint);
if(!endpointUri.IsLoopback||endpointUri.Scheme!="http")throw new InvalidOperationException("Only local HTTP endpoints supported");

if(stdio)
{
    string token=File.ReadAllText(tokenFile).Trim();
    var http=new HttpClient{BaseAddress=new Uri(endpoint.TrimEnd('/')+"/"),Timeout=TimeSpan.FromSeconds(15)};
    http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
    var host=Host.CreateApplicationBuilder();
    host.Logging.ClearProviders();host.Logging.AddConsole(o=>o.LogToStandardErrorThreshold=LogLevel.Trace);
    host.Services.AddSingleton<IGameEndpoint>(new RemoteEndpoint(http));
    host.Services.AddMcpServer().WithStdioServerTransport().WithTools<GameTools>();
    await host.Build().RunAsync();return;
}

int? pid=int.TryParse(Value("--game-pid"),out int configuredPid)?configuredPid:null;
int player=int.TryParse(Value("--player"),out int configuredPlayer)?configuredPlayer:0;
if(diagnostic)
{
    var game=new WindowsGame(pid??Process.GetProcessesByName("h3hota HD").Single().Id);
    using var bridge=new Bridge(game,player,Path.Combine(directory,"diagnostic"));
    if(args.Contains("--center-hero"))await game.KeyAsync(0x48,0x23);
    if(args.Contains("--map"))
    {
        if(int.TryParse(Value("--hover-x"),out int hx)&&int.TryParse(Value("--hover-y"),out int hy))
        {
            var observation=await bridge.Observe(CancellationToken.None);
            await game.MouseAsync(hx,hy,observation.Width,observation.Height,false,CancellationToken.None);
            await Task.Delay(150);
        }
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(bridge.MapDiagnostic()));return;
    }
    if(args.Contains("--read"))
    {
        // Developer-only read-only code/data inspection; addresses are never exposed to the player.
        uint address=Convert.ToUInt32(Value("--address")!.Replace("0x",""),16);
        int length=int.Parse(Value("--length")??"256");
        Console.WriteLine(Convert.ToHexString(game.Read(address,length)));
        return;
    }
    if(args.Contains("--ui"))
    {
        // Developer-only raw dialog dump: every control of the active dialog with id, state and text pointer.
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(bridge.RawUi()));
        return;
    }
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(bridge.Diagnostic()));
    try{Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(await bridge.Observe(CancellationToken.None)));}
    catch(Exception e){Console.Error.WriteLine(e.ToString());Environment.ExitCode=1;}
    return;
}
Directory.CreateDirectory(directory);
using var serviceLock=new FileStream(Path.Combine(directory,"service.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
int? hostLauncherPid=int.TryParse(Value("--launcher-pid"),out int parsedLauncherPid)?parsedLauncherPid:null;
using var session=new GameSession(pid,player,directory,hostLauncherPid);
string secret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
// Starts as an owner-local development service; per-player credentials are added with hotseat.
var builder=WebApplication.CreateBuilder();
builder.Configuration["AllowedHosts"]="127.0.0.1;localhost;[::1]";
builder.Logging.ClearProviders();builder.Logging.AddConsole(o=>o.LogToStandardErrorThreshold=LogLevel.Trace);
builder.Services.AddSingleton<IGameEndpoint>(session);
builder.Services.AddMcpServer().WithHttpTransport(o=>o.SessionMode=HttpServerSessionMode.StatefulForInitializeClients).WithTools<GameTools>().WithResources<GameResources>();
var app=builder.Build();
app.Use(async(context,next)=>{
    string supplied=context.Request.Headers.Authorization.ToString();
    if(!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied),Encoding.UTF8.GetBytes("Bearer "+secret)))
    {context.Response.StatusCode=401;return;}
    try{await next();}
    catch(InvalidOperationException e){context.Response.StatusCode=409;await context.Response.WriteAsJsonAsync(new{error=e.Message});}
});
app.MapMcp("/mcp");
app.MapPost("/bridge/status",(CancellationToken ct)=>session.Status(ct));
app.MapPost("/bridge/start",(CancellationToken ct)=>session.Start(ct));
app.MapPost("/bridge/graphics",(GraphicsRequest request,CancellationToken ct)=>session.Graphics(request.Renderer,ct));
app.MapPost("/bridge/observe",(CancellationToken ct)=>session.Observe(ct));
app.MapPost("/bridge/debug-capture",(CancellationToken ct)=>session.Capture(ct));
app.MapPost("/bridge/debug-snapshot",(CancellationToken ct)=>session.Snapshot(ct));
app.MapPost("/bridge/click",(OperationRequest request,CancellationToken ct)=>session.Click(request,ct));
app.MapPost("/bridge/text",(TextRequest request,CancellationToken ct)=>session.EnterText(request,ct));
app.MapPost("/bridge/move",(MoveRequest request,CancellationToken ct)=>session.Move(request,ct));
app.MapPost("/bridge/move-tile",(TileMoveRequest request,CancellationToken ct)=>session.MoveToTile(request,ct));
app.MapPost("/bridge/docs",(DocsRequest request,CancellationToken ct)=>session.Docs(request,ct));
app.MapPost("/bridge/docs-catalog",(CancellationToken ct)=>session.DocsCatalog(ct));
app.MapPost("/bridge/docs-read",(DocsReadRequest request,CancellationToken ct)=>session.DocsRead(request.Path,request.Heading,ct));
app.MapPost("/bridge/journal",(JournalRequest request,CancellationToken ct)=>session.Journal(request.Limit,ct));
app.MapPost("/bridge/plan",(PlanRequest request,CancellationToken ct)=>session.Plan(request.Value,ct));
app.MapPost("/bridge/nearby",(CancellationToken ct)=>session.Nearby(ct));
app.MapPost("/bridge/target",(TargetRequest request,CancellationToken ct)=>session.InspectTarget(request.TargetId,request.Revision,ct));
if(int.TryParse(Value("--launcher-pid"),out int launcherPid))
{
    var launcher=Process.GetProcessById(launcherPid);
    _=Task.Run(async()=>{await launcher.WaitForExitAsync();app.Lifetime.StopApplication();});
    app.Lifetime.ApplicationStarted.Register(()=>_ = LauncherControl.Run(launcherPid,session,endpoint,app.Lifetime));
}
app.Urls.Add(endpoint);
File.WriteAllText(tokenFile,secret);
await app.StartAsync();
await app.WaitForShutdownAsync();
record JournalRequest(int Limit);record PlanRequest(string? Value);
record TargetRequest(string TargetId,string Revision);
record GraphicsRequest(string? Renderer);
record DocsReadRequest(string Path,string? Heading);

