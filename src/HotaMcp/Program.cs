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

int pid=int.TryParse(Value("--game-pid"),out int configuredPid)?configuredPid:
    Process.GetProcessesByName("h3hota HD").Single().Id;
int player=int.TryParse(Value("--player"),out int configuredPlayer)?configuredPlayer:0;
using var game=new WindowsGame(pid);
using var bridge=new Bridge(game,player,Path.Combine(directory,"sessions",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));
if(diagnostic)
{
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(bridge.Diagnostic()));
    try{Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(await bridge.Observe(CancellationToken.None)));}
    catch(Exception e){Console.Error.WriteLine(e.Message);Environment.ExitCode=1;}
    return;
}
Directory.CreateDirectory(directory);
using var serviceLock=new FileStream(Path.Combine(directory,"service.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
string secret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
// Starts as an owner-local development service; per-player credentials are added with hotseat.
var builder=WebApplication.CreateBuilder();
builder.Configuration["AllowedHosts"]="127.0.0.1;localhost;[::1]";
builder.Logging.ClearProviders();builder.Logging.AddConsole(o=>o.LogToStandardErrorThreshold=LogLevel.Trace);
builder.Services.AddSingleton<IGameEndpoint>(new LocalEndpoint(bridge));
builder.Services.AddMcpServer().WithHttpTransport(o=>o.SessionMode=HttpServerSessionMode.StatefulForInitializeClients).WithTools<GameTools>();
var app=builder.Build();
app.Use(async(context,next)=>{
    string supplied=context.Request.Headers.Authorization.ToString();
    if(!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied),Encoding.UTF8.GetBytes("Bearer "+secret)))
    {context.Response.StatusCode=401;return;}
    try{await next();}
    catch(InvalidOperationException e){context.Response.StatusCode=409;await context.Response.WriteAsJsonAsync(new{error=e.Message});}
});
app.MapMcp("/mcp");
app.MapPost("/bridge/status",()=>bridge.Status());
app.MapPost("/bridge/observe",(CancellationToken ct)=>bridge.Observe(ct));
app.MapPost("/bridge/click",(OperationRequest request,CancellationToken ct)=>bridge.Click(request,ct));
app.MapPost("/bridge/journal",(JournalRequest request,CancellationToken ct)=>bridge.GetJournal(request.Limit,ct));
app.MapPost("/bridge/plan",(PlanRequest request,CancellationToken ct)=>bridge.Plan(request.Value,ct));
if(int.TryParse(Value("--launcher-pid"),out int launcherPid))
{
    var launcher=Process.GetProcessById(launcherPid);
    _=Task.Run(async()=>{await launcher.WaitForExitAsync();app.Lifetime.StopApplication();});
    app.Lifetime.ApplicationStarted.Register(()=>_ = LauncherControl.Run(launcherPid,pid,endpoint,app.Lifetime));
}
app.Urls.Add(endpoint);
await app.StartAsync();
File.WriteAllText(tokenFile,secret);
await app.WaitForShutdownAsync();
record JournalRequest(int Limit);
record PlanRequest(string? Value);
