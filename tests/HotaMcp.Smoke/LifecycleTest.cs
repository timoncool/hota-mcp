using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

internal static class LifecycleTest
{
    private static Process Start(params string[] arguments)
    {
        var info=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
        foreach(string argument in arguments)info.ArgumentList.Add(argument);
        var process=Process.Start(info)!;
        process.OutputDataReceived+=(_,_)=>{};
        process.ErrorDataReceived+=(_,e)=>{if(e.Data is not null && e.Data.Contains("fail:"))Console.Error.WriteLine(e.Data);};
        process.BeginOutputReadLine();process.BeginErrorReadLine();return process;
    }
    public static async Task Run(string root)
    {
        string assembly=Path.Combine(root,"src/HotaMcp/bin/Debug/net8.0-windows/HotaMcp.dll");
        string state=Path.Combine(root,"build","lifecycle",Guid.NewGuid().ToString("N"));
        using var parent=Start(Assembly.GetExecutingAssembly().Location,"--lifetime-parent");
        using var server=Start(assembly,"--launcher-pid",parent.Id.ToString(),"--game-pid",int.MaxValue.ToString(),
            "--state-dir",state,"--endpoint","http://127.0.0.1:18774");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try
        {
            while(!File.Exists(Path.Combine(state,"connection.token")))
            {
                if(server.HasExited)throw new Exception($"Server exited: {server.ExitCode}");
                await Task.Delay(100,timeout.Token);
            }
            var transport=new StdioClientTransport(new(){Command="dotnet",Arguments=[assembly,"--stdio","--state-dir",state,"--endpoint","http://127.0.0.1:18774"]});
            await using var client=await McpClient.CreateAsync(transport,cancellationToken:timeout.Token);
            var status=await client.CallToolAsync("game_status",new Dictionary<string,object?>(),cancellationToken:timeout.Token);
            string text=string.Join("",status.Content.OfType<TextContentBlock>().Select(x=>x.Text));
            using var json=JsonDocument.Parse(text);
            if(json.RootElement.GetProperty("state").GetString()!="waiting_for_game")throw new Exception(text);
            var observation=await client.CallToolAsync("observe",new Dictionary<string,object?>(),cancellationToken:timeout.Token);
            if(observation.IsError!=true)throw new Exception("Observation without a game was not rejected");
            parent.Kill();await parent.WaitForExitAsync(timeout.Token);
            await server.WaitForExitAsync(timeout.Token);
            if(server.ExitCode!=0)throw new Exception($"Shutdown failed: {server.ExitCode}");
            Console.WriteLine("PASS MCP available without game; observe denied; parent exit shuts down server cleanly");
        }
        finally
        {
            if(!server.HasExited)server.Kill();
            if(!parent.HasExited)parent.Kill();
        }
    }
}
