using HotaMcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

var root=Path.GetFullPath(args[0]);
int stateIndex=Array.IndexOf(args,"--state-dir");
string stateDirectory=stateIndex>=0?Path.GetFullPath(args[stateIndex+1]):Path.Combine(root,"build/state");
var transport=new StdioClientTransport(new(){Name="hota-smoke",Command="dotnet",
    Arguments=[Path.Combine(root,"src/HotaMcp/bin/Debug/net8.0-windows/HotaMcp.dll"),"--stdio","--state-dir",stateDirectory]});
using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(35));
await using var client=await McpClient.CreateAsync(transport,cancellationToken:timeout.Token);
var tools=await client.ListToolsAsync(cancellationToken:timeout.Token);
Console.WriteLine("TOOLS "+string.Join(",",tools.Select(t=>t.Name)));
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
