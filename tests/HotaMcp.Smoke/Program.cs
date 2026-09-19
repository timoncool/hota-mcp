using HotaMcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

if(args.Contains("--lifetime-parent")){await Task.Delay(Timeout.Infinite);return;}
var root=Path.GetFullPath(args[0]);
if(args.Contains("--lifecycle")){await LifecycleTest.Run(root);return;}
int stateIndex=Array.IndexOf(args,"--state-dir");
string stateDirectory=stateIndex>=0?Path.GetFullPath(args[stateIndex+1]):Path.Combine(root,"build/state");
var transport=new StdioClientTransport(new(){Name="hota-smoke",Command="dotnet",
    Arguments=[Path.Combine(root,"src/HotaMcp/bin/Debug/net8.0-windows/HotaMcp.dll"),"--stdio","--state-dir",stateDirectory]});
using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(args.Contains("--atlas")?120:35));
await using var client=await McpClient.CreateAsync(transport,cancellationToken:timeout.Token);
var tools=await client.ListToolsAsync(cancellationToken:timeout.Token);
Console.WriteLine("TOOLS "+string.Join(",",tools.Select(t=>t.Name)));
int atlasIndex=Array.IndexOf(args,"--atlas");
if(atlasIndex>=0)
{
    await AtlasFlow.Run(client,Path.GetFullPath(args[atlasIndex+1]),Path.Combine(root,"build","atlas",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]),timeout.Token);
    return;
}
var jsonOptions=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
async Task<T> Call<T>(string tool,Dictionary<string,object?> parameters)
{
    var result=await client.CallToolAsync(tool,parameters,cancellationToken:timeout.Token);
    string text=string.Join("\n",result.Content.OfType<TextContentBlock>().Select(x=>x.Text));
    if(result.IsError==true)throw new InvalidOperationException(text);
    return JsonSerializer.Deserialize<T>(text,jsonOptions)!;
}
var before=await Call<Observation>("observe",new());
Console.WriteLine($"OBSERVE {before.Screen} hero={before.Hero?.Id} movement={before.Hero?.Movement}");
if(args.Contains("--capture"))
{
    var result=await client.CallToolAsync("debug_capture",new Dictionary<string,object?>(),cancellationToken:timeout.Token);
    if(result.IsError==true||result.Content.Any(c=>c is not TextContentBlock))throw new Exception("Capture must return metadata, not an inline image");
    var capture=JsonSerializer.Deserialize<CaptureResult>(string.Join("\n",result.Content.OfType<TextContentBlock>().Select(c=>c.Text)),jsonOptions)!;
    byte[] png=await File.ReadAllBytesAsync(capture.Path,timeout.Token);
    if(!png.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))throw new Exception("PNG signature invalid");
    if(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16,4))!=before.Width||System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20,4))!=before.Height)throw new Exception("Capture dimensions mismatch");
    var after=await Call<Observation>("observe",new());
    if(after.Revision!=before.Revision)throw new Exception("Capture changed game observation");
    Console.WriteLine($"PASS explicit MCP capture: {capture.Width}x{capture.Height}, metadata only, observation unchanged; {capture.Path}");
    return;
}
if(args.Contains("--menus"))
{
    async Task<Observation> Menu(Observation state,string action,string expected)
    {
        var parameters=new Dictionary<string,object?>{{"operationId",Guid.NewGuid().ToString("N")},{"revision",state.Revision},{"action",action}};
        var result=await Call<OperationResult>("act",parameters);
        if(result.Status!="completed"||result.Observation?.Screen!=expected)throw new Exception($"Menu failed: {action} {result.Status}");
        var repeat=await Call<OperationResult>("act",parameters);
        if(JsonSerializer.Serialize(result)!=JsonSerializer.Serialize(repeat))throw new Exception("Menu retry changed result");
        var observed=await Call<Observation>("observe",new());
        if(observed.Screen!=expected||observed.Hero!=null||observed.Resources.Length!=0||observed.Towns.Count!=0)throw new Exception("Invalid frontend observation");
        return observed;
    }
    if(before.Screen=="game_type")before=await Menu(before,"menu:back","main_menu");
    for(int i=0;i<3;i++)
    {
        before=await Menu(before,i%2==0?"menu:load":"menu:new","game_type");
        before=await Menu(before,"menu:back","main_menu");
    }
    Console.WriteLine("PASS MCP menu load/new/back, three cycles, idempotent retries, no player state before game");
    return;
}
if(args.Contains("--targets"))
{
    var nearby=await Call<NearbyTargets>("nearby_targets",new());
    if(nearby.Targets.Count==0)throw new Exception("No visible targets returned");
    using var targetJson=JsonDocument.Parse(JsonSerializer.Serialize(nearby.Targets));
    foreach(var target in targetJson.RootElement.EnumerateArray())
        if(target.EnumerateObject().Any(p=>p.Name is not ("Id" or "Kind" or "Route")))throw new Exception("Target response contains extra knowledge");
    var fountain=nearby.Targets.Single(t=>t.Kind=="fountain_of_fortune");
    var inspected=await Call<TargetInspection>("inspect_target",new(){{"targetId",fountain.Id},{"revision",nearby.Revision}});
    if(inspected.Kind!="fountain_of_fortune")throw new Exception("Target identity mismatch");
    var after=await Call<Observation>("observe",new());
    if(after.Hero?.Movement!=before.Hero?.Movement||!after.Hero!.Position.SequenceEqual(before.Hero!.Position))throw new Exception("Inspect moved hero");
    if(after.Revision!=before.Revision)throw new Exception("Read-only target tools altered the observation");
    Console.WriteLine($"PASS {nearby.Targets.Count} targets read directly; observation unchanged; route: {JsonSerializer.Serialize(inspected.Route)}");
    before=after;
}
if(args.Contains("--towns"))
{
    var initial=before;
    async Task<Observation> Act(Observation state,string key,string expected)
    {
        if(!state.Actions.Any(a=>a.Key==key))throw new Exception($"Action missing: {key}");
        var result=await Call<OperationResult>("act",new(){{"operationId",Guid.NewGuid().ToString("N")},{"revision",state.Revision},{"action",key}});
        if(result.Status!="completed"||result.Observation?.Screen!=expected)throw new Exception($"Action failed: {key} {result.Status}");
        return result.Observation;
    }
    int townId=before.Towns.First().Id;
    before=await Act(before,$"town:open:{townId}","town");
    before=await Act(before,"town:construction","town_hall");
    before=await Act(before,"building:inspect:11","building_confirmation");
    if(initial.Towns.First().BuiltToday&&before.Actions.Any(a=>a.Key=="building:buy"))throw new Exception("Daily building limit is not reflected");
    before=await Act(before,"building:cancel","town_hall");
    before=await Act(before,"construction:close","town");
    before=await Act(before,"town:close","adventure");
    if(!before.Resources.SequenceEqual(initial.Resources)||before.Hero?.Movement!=initial.Hero?.Movement)throw new Exception("Town browsing changed resources/movement");
    Console.WriteLine("PASS native MCP town -> construction -> building information -> cancel -> town -> adventure; unavailable purchase omitted");
}
if(!args.Contains("--actions"))return;
var open=before.Elements.Single(x=>x.Id==10&&x.Asset=="iam009.def");
string operationId=Guid.NewGuid().ToString("N");
var call=new Dictionary<string,object?>{{"operationId",operationId},{"revision",before.Revision},{"element",open.Key}};
var opened=await Call<OperationResult>("click_ui",call);
if(opened.Status!="completed"||opened.Observation?.Screen!="system_options")throw new Exception("Options did not open");
var repeated=await Call<OperationResult>("click_ui",call);
if(repeated!=opened && JsonSerializer.Serialize(repeated)!=JsonSerializer.Serialize(opened))throw new Exception("Retry result changed");
var inside=await Call<Observation>("observe",new());
if(inside.Screen!="system_options")throw new Exception("Retry dispatched a second action");
var close=inside.Elements.Single(x=>x.Id==30722&&x.Asset=="soretrn.def");
var closed=await Call<OperationResult>("click_ui",new(){{"operationId",Guid.NewGuid().ToString("N")},{"revision",inside.Revision},{"element",close.Key}});
if(closed.Status!="completed"||closed.Observation?.Screen!="adventure")throw new Exception("Return failed");
if(closed.Observation.Hero?.Movement!=before.Hero?.Movement||!closed.Observation.Resources.SequenceEqual(before.Resources))throw new Exception("Unexpected game state change");
Console.WriteLine("PASS observe -> options -> duplicate request -> return; resources and movement unchanged");
