using System.IO.Pipes;
using System.Text;

namespace HotaMcp;

internal static class LauncherControl
{
    public static async Task Run(int launcherPid,GameSession session,string endpoint,IHostApplicationLifetime lifetime)
    {
        while(!lifetime.ApplicationStopping.IsCancellationRequested)
        {
            try
            {
                await using var pipe=new NamedPipeServerStream($"hota-mcp-control-{launcherPid}",PipeDirection.InOut,1,
                    PipeTransmissionMode.Message,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(lifetime.ApplicationStopping);
                byte[] request=new byte[64];
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
                deadline.CancelAfter(TimeSpan.FromSeconds(1));
                int length=await pipe.ReadAsync(request,deadline.Token);
                string command=Encoding.UTF8.GetString(request,0,length).TrimEnd('\0','\r','\n');
                bool stop=command=="stop";
                string response=command is "status" or "stop"
                    ? (stop?"Остановка MCP-сервера...":$"MCP-сервер работает, PID {Environment.ProcessId}\r\n{endpoint}/mcp\r\n\r\n{await session.LauncherStatus(deadline.Token)}")
                    : "Неизвестная команда";
                await pipe.WriteAsync(Encoding.UTF8.GetBytes(response),deadline.Token);
                await pipe.FlushAsync(deadline.Token);
                if(stop){lifetime.StopApplication();return;}
            }
            catch(OperationCanceledException) when(lifetime.ApplicationStopping.IsCancellationRequested){return;}
            catch(IOException){await Task.Delay(100,lifetime.ApplicationStopping);}
            catch(OperationCanceledException){ }
        }
    }
}
