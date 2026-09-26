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

if(args.Contains("--usage"))
{
    // What the current game (or --game <id>) has cost, from its own ledger.
    Console.OutputEncoding=Encoding.UTF8;
    Console.WriteLine(UsageLedger.Report(directory,Value("--game")));
    return;
}

if(stdio)
{
    // A client may connect before anything is running. Bring the player's own launcher up and let
    // its MCP tab raise the service, instead of requiring a hand-started stack.
    Console.Error.WriteLine(await ServiceBootstrap.EnsureRunning(endpoint,directory,CancellationToken.None));
    string token=File.ReadAllText(tokenFile).Trim();
    var http=new HttpClient{BaseAddress=new Uri(endpoint.TrimEnd('/')+"/"),Timeout=TimeSpan.FromSeconds(15)};
    http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
    // A client playing one colour of a hotseat names it: HOTA_PLAYER=1 or blue.
    if(Environment.GetEnvironmentVariable("HOTA_PLAYER") is {Length:>0} colour)http.DefaultRequestHeaders.Add("X-Hota-Player",colour);
    var host=Host.CreateApplicationBuilder();
    host.Logging.ClearProviders();host.Logging.AddConsole(o=>o.LogToStandardErrorThreshold=LogLevel.Trace);
    host.Services.AddSingleton<IGameEndpoint>(new RemoteEndpoint(http));
    host.Services.AddMcpServer(o=>o.ServerInstructions=ServerInstructions.Text).WithStdioServerTransport().WithTools<GameTools>(ToolJson.Options)
        .WithRequestFilters(f=>f.AddCallToolFilter(ToolErrors.Filter));
    await host.Build().RunAsync();return;
}

int? pid=int.TryParse(Value("--game-pid"),out int configuredPid)?configuredPid:null;
int player=PlayerSetting.Read(directory,Value("--player"));
if(diagnostic)
{
    using var game=new WindowsGame(pid??Process.GetProcessesByName("h3hota HD").Single().Id);
    using var bridge=new Bridge(game,Math.Max(player,0),Path.Combine(directory,"diagnostic"));
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
// The «HotA MCP» shortcut: service first, then the player's launcher with the MCP tab, then the game.
bool launch=args.Contains("--launch");
if(launch)
{
    ServiceBootstrap.FreeConsole();
    if(await ServiceBootstrap.Answers(endpoint,tokenFile,CancellationToken.None))
    {
        await ServiceBootstrap.StartGame(endpoint,tokenFile,CancellationToken.None);
        return;
    }
}
using var serviceLock=new FileStream(Path.Combine(directory,"service.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
int? hostLauncherPid=launch?ServiceBootstrap.OpenLauncher(directory)
    :int.TryParse(Value("--launcher-pid"),out int parsedLauncherPid)?parsedLauncherPid:null;
using var session=new GameSession(pid,player,directory,hostLauncherPid);
// Remember where the launcher lives so a later cold start can raise this same service again.
if(hostLauncherPid is int knownLauncher)
    try{ServiceBootstrap.RememberLauncher(directory,Process.GetProcessById(knownLauncher).MainModule!.FileName);}
    catch(Exception e)when(e is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException){}
string secret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
// Starts as an owner-local development service; per-player credentials are added with hotseat.
var builder=WebApplication.CreateBuilder();
builder.Configuration["AllowedHosts"]="127.0.0.1;localhost;[::1]";
builder.Logging.ClearProviders();builder.Logging.AddConsole(o=>o.LogToStandardErrorThreshold=LogLevel.Trace);
builder.Services.AddSingleton<IGameEndpoint>(session);
builder.Services.AddMcpServer(o=>o.ServerInstructions=ServerInstructions.Text).WithHttpTransport(o=>o.SessionMode=HttpServerSessionMode.StatefulForInitializeClients).WithTools<GameTools>(ToolJson.Options).WithResources<GameResources>()
    .WithRequestFilters(f=>f.AddCallToolFilter(ToolErrors.Filter));
var app=builder.Build();
// Claude Code's own telemetry (OTLP/HTTP JSON logs) cannot carry the per-start secret; it only
// files costs, and the service listens on loopback alone.
app.MapPost("/v1/logs",async(HttpContext context)=>
{
    try
    {
        // The body is read here, not bound by the framework: an export in another encoding would
        // otherwise be turned away before any line of this service could say so.
        string type=context.Request.ContentType??"";
        if(!type.Contains("json",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Telemetry export arrived as «{type}»; the bridge reads OTLP JSON — set OTEL_EXPORTER_OTLP_LOGS_PROTOCOL=http/json");
        using var body=await System.Text.Json.JsonDocument.ParseAsync(context.Request.Body);
        session.Telemetry(body.RootElement);
        return Results.Json(new{});
    }
    catch(Exception e) when(e is InvalidOperationException or System.Text.Json.JsonException or IOException)
    {
        // A malformed export is the client's fault and resending it cannot help: logged, answered 400.
        File.AppendAllText(Path.Combine(directory,"errors.log"),$"{DateTimeOffset.Now:O} /v1/logs{Environment.NewLine}{e}{Environment.NewLine}{Environment.NewLine}");
        return Results.BadRequest(new{error=e.Message});
    }
});
app.Use(async(context,next)=>{
    if(context.Request.Path.Equals("/v1/logs")){await next();return;}
    string supplied=context.Request.Headers.Authorization.ToString();
    if(!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied),Encoding.UTF8.GetBytes("Bearer "+secret)))
    {context.Response.StatusCode=401;return;}
    try
    {
        session.Pin(context.Request.Headers["X-Hota-Player"].FirstOrDefault());
        await next();
    }
    catch(InvalidOperationException e)
    {
        // A refusal is an answer; anything else is a fault, and its stack is what finds it.
        if(e is not ActionRefused)
            File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HotaMcp","errors.log"),
                $"{DateTimeOffset.Now:O} {context.Request.Path}{Environment.NewLine}{e}{Environment.NewLine}{Environment.NewLine}");
        context.Response.StatusCode=409;await context.Response.WriteAsJsonAsync(new{error=e.Message,code=(e as ActionRefused)?.Code});
    }
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
app.MapPost("/bridge/inspect-element",(InspectRequest request,CancellationToken ct)=>session.InspectElement(request,ct));
app.MapPost("/bridge/inspect-cell",(CellRequest request,CancellationToken ct)=>session.InspectCell(request,ct));
app.MapPost("/bridge/probe-screen",(CancellationToken ct)=>session.ProbeScreen(ct));
app.MapPost("/bridge/mem",(MemRequest request,CancellationToken ct)=>session.Memory(request.Address,request.Length,ct));
app.MapPost("/bridge/tile-raw",(TileRequest request,CancellationToken ct)=>session.TileBytes(request.X,request.Y,request.Z,ct));
app.MapPost("/bridge/key",(KeyRequest request,CancellationToken ct)=>session.SendKey(request,ct));
app.MapPost("/bridge/press",(PressRequest request,CancellationToken ct)=>session.Press(request,ct));
app.MapPost("/bridge/press-right",(PressRequest request,CancellationToken ct)=>session.PressRight(request,ct));
app.MapPost("/bridge/move",(MoveRequest request,CancellationToken ct)=>session.Move(request,ct));
app.MapPost("/bridge/attack",(MoveRequest request,CancellationToken ct)=>session.Attack(request,ct));
app.MapPost("/bridge/map-click",(MapClickRequest request,CancellationToken ct)=>session.MapClick(request,ct));
app.MapPost("/bridge/move-tile",(TileMoveRequest request,CancellationToken ct)=>session.MoveToTile(request,ct));
app.MapPost("/bridge/docs",(DocsRequest request,CancellationToken ct)=>session.Docs(request,ct));
app.MapPost("/bridge/docs-catalog",(DocsCatalogRequest request,CancellationToken ct)=>session.DocsCatalog(request.Path,ct));
app.MapPost("/bridge/reference",(ReferenceRequest request,CancellationToken ct)=>session.Reference(request,ct));
app.MapPost("/bridge/docs-read",(DocsReadRequest request,CancellationToken ct)=>session.DocsRead(request.Path,request.Heading,request.Offset,request.MaxChars,ct));
app.MapPost("/bridge/journal",(JournalRequest request,CancellationToken ct)=>session.Journal(request.Limit,ct));
app.MapPost("/bridge/ally-log",(JournalRequest request,CancellationToken ct)=>session.AllyLog(request.Limit,ct));
app.Lifetime.ApplicationStarted.Register(()=>_ = session.WatchAllies(app.Lifetime.ApplicationStopping));
app.MapPost("/bridge/plan",(PlanRequest request,CancellationToken ct)=>session.Plan(request.Value,ct));
app.MapPost("/bridge/mark",(MarkRequest request,CancellationToken ct)=>session.Mark(request.X,request.Y,request.Z,request.Note,ct));
app.MapPost("/bridge/map",(MapRequest request,CancellationToken ct)=>session.ReadMap(request.X,request.Y,request.Z,request.Radius,ct));
app.MapPost("/bridge/minimap",(MiniMapRequest request,CancellationToken ct)=>session.ReadMiniMap(request.Z,ct));
app.MapPost("/bridge/minimap-capture",(CancellationToken ct)=>session.MinimapCapture(ct));
app.MapPost("/bridge/inspect",(TileRequest request,CancellationToken ct)=>session.InspectTile(request.X,request.Y,request.Z,request.Revision,ct));
app.MapPost("/bridge/nearby",(CancellationToken ct)=>session.Nearby(ct));
app.MapPost("/bridge/target",(TargetRequest request,CancellationToken ct)=>session.InspectTarget(request.TargetId,request.Revision,ct));
app.MapPost("/bridge/path",(TileRequest request,CancellationToken ct)=>session.InspectPath(request.X,request.Y,request.Z,request.Revision,ct));
if(hostLauncherPid is int launcherPid)
{
    var launcher=Process.GetProcessById(launcherPid);
    _=Task.Run(async()=>{await launcher.WaitForExitAsync();app.Lifetime.StopApplication();});
    app.Lifetime.ApplicationStarted.Register(()=>_ = LauncherControl.Run(launcherPid,session,endpoint,app.Lifetime));
}
app.Urls.Add(endpoint);
File.WriteAllText(tokenFile,secret);
await app.StartAsync();
if(launch&&hostLauncherPid is int openedLauncher)
{
    // The service answers on the launcher's control pipe already, so the tab shows it and does not
    // start a second one. Then the game, through the launcher's own Play button.
    string launchLog=Path.Combine(directory,"launch.log");
    try
    {
        await ServiceBootstrap.AttachTab(openedLauncher,app.Lifetime.ApplicationStopping);
        string? refusal=null;
        // The Play button is enabled a moment after the launcher window appears.
        for(int attempt=0;attempt<120;attempt++)
        {
            try{await session.Start(app.Lifetime.ApplicationStopping);refusal=null;break;}
            catch(InvalidOperationException e){refusal=e.Message;await Task.Delay(500,app.Lifetime.ApplicationStopping);}
        }
        File.WriteAllText(launchLog,refusal is null?"[OK] service, launcher tab and game started":"[ERROR] game not started: "+refusal);
    }
    catch(InvalidOperationException e){File.WriteAllText(launchLog,"[ERROR] "+e.Message);}
}
await app.WaitForShutdownAsync();
record JournalRequest(int Limit);record PlanRequest(string? Value);record MarkRequest(int X,int Y,int Z,string? Note);
record TargetRequest(string TargetId,string Revision);record MemRequest(uint Address,int Length);
record GraphicsRequest(string? Renderer);
record DocsReadRequest(string Path,string? Heading,int Offset,int MaxChars);
record DocsCatalogRequest(string? Path);
record MapRequest(int X,int Y,int Z,int Radius);
record MiniMapRequest(int Z);
record TileRequest(int X,int Y,int Z,string Revision);

/// Tool answers are read by a model: Cyrillic escaped as \uXXXX costs six characters a letter and
/// many times the tokens, so answers keep their text as it is, and fields with no value are left
/// out. Zeros and false stay: no movement left and not your turn are answers.
static class ToolJson
{
    public static readonly System.Text.Json.JsonSerializerOptions Options=new(ModelContextProtocol.McpJsonUtilities.DefaultOptions)
    {
        Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        // A control's animation frame is the bridge's own bookkeeping for revisions; to a player
        // it is a meaningless number on every control.
        TypeInfoResolver=System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.WithAddedModifier(
            ModelContextProtocol.McpJsonUtilities.DefaultOptions.TypeInfoResolver!,
            info=>{
                if(info.Type!=typeof(UiElement))return;
                var frame=info.Properties.FirstOrDefault(p=>p.Name=="frame");
                if(frame is not null)info.Properties.Remove(frame);
            }),
    };
}
