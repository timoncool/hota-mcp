using HotaMcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text;
using System.Text.Json;

internal static class AtlasFlow
{
    private sealed record Step(string Name,string? Action,string Screen,string? Panel=null,string? Field=null,int? Value=null);
    public static async Task Run(McpClient client,string recipe,string output,CancellationToken ct)
    {
        var options=new JsonSerializerOptions{PropertyNameCaseInsensitive=true,WriteIndented=true};
        var steps=JsonSerializer.Deserialize<Step[]>(await File.ReadAllTextAsync(recipe,ct),options)??throw new Exception("Empty atlas recipe");
        Directory.CreateDirectory(output);
        File.Copy(recipe,Path.Combine(output,"recipe.json"));
        var report=new StringBuilder("# UI atlas run\n\nAutomatic checks verify structured observations. Visual comparison must be recorded separately.\n\n");
        async Task<T> Call<T>(string tool,Dictionary<string,object?> args)
        {
            var result=await client.CallToolAsync(tool,args,cancellationToken:ct);
            string text=string.Join("\n",result.Content.OfType<TextContentBlock>().Select(c=>c.Text));
            if(result.IsError==true)throw new Exception($"{tool}: {text}");
            return JsonSerializer.Deserialize<T>(text,options)!;
        }
        async Task Save(string stem,object data)=>await File.WriteAllTextAsync(Path.Combine(output,stem+".json"),JsonSerializer.Serialize(data,options),ct);
        await Save("status",await Call<JsonElement>("game_status",new()));
        await Save("tools",(await client.ListToolsAsync(cancellationToken:ct)).Select(t=>new{t.Name,t.Description}));
        for(int index=0;index<steps.Length;index++)
        {
            var step=steps[index];string stem=$"{index:D2}";
            report.AppendLine($"## {stem}: {step.Name}\n");
            try
            {
                var before=await Call<Observation>("observe",new());
                await Save(stem+"-before",before);
                if(step.Action is not null)
                {
                    var parameters=new Dictionary<string,object?>{{"operationId",Guid.NewGuid().ToString("N")},{"revision",before.Revision},{"action",step.Action}};
                    await Save(stem+"-request",parameters);
                    var action=await Call<OperationResult>("act",parameters);
                    await Save(stem+"-result",action);
                    if(action.Status!="completed")throw new Exception($"Action {step.Action}: {action.Status}; do not repeat with a new ID");
                    var retry=await Call<OperationResult>("act",parameters);
                    if(JsonSerializer.Serialize(action)!=JsonSerializer.Serialize(retry))throw new Exception("Idempotent retry mismatch");
                }
                var snapshot=await Call<DebugSnapshot>("debug_snapshot",new());
                var observed=snapshot.Observation;
                await Save(stem+"-after",observed);
                var capture=snapshot.Capture;
                File.Copy(capture.Path,Path.Combine(output,stem+".png"));
                await Save(stem+"-capture",capture);
                if(observed.Screen!=step.Screen||step.Panel is not null&&observed.Setup?.Panel!=step.Panel)throw new Exception("Unexpected screen/panel");
                if(step.Field is not null&&observed.Setup?.Fields.SingleOrDefault(f=>f.Key==step.Field)?.Value!=step.Value)throw new Exception("Setting readback mismatch");
                report.AppendLine($"PASS structured check; screen={observed.Screen}; panel={observed.Setup?.Panel}; action={step.Action ?? "observe"}.\n");
                if(observed.Setup?.Map is {} map)report.AppendLine($"Selected map: {map.Name}, {map.Size}×{map.Size}.\n");
                if(observed.Setup is {} setup)foreach(var field in setup.Fields)report.AppendLine($"- {field.Key}: {field.Value}");
                report.AppendLine($"\n![Game frame]({stem}.png)\n\nVisual review: pending.\n");
                Console.WriteLine($"PASS atlas {stem} {step.Name}");
            }
            catch(Exception e)
            {
                await Save(stem+"-failure",new{step.Name,error=e.Message});
                report.AppendLine($"FAILED: {e.Message}\n");
                await File.WriteAllTextAsync(Path.Combine(output,"REPORT.md"),report.ToString(),CancellationToken.None);
                throw;
            }
            await File.WriteAllTextAsync(Path.Combine(output,"REPORT.md"),report.ToString(),ct);
        }
        Console.WriteLine("ATLAS "+output);
    }
}
