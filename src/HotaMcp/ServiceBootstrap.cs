using System.Diagnostics;
using System.Net.Http.Headers;

namespace HotaMcp;

/// <summary>
/// Brings the whole chain up from a cold machine, so a client only has to ask for the game.
///
/// The service is meant to be raised by the player's own HD Launcher through the MCP tab. When a
/// client connects and nothing is running yet, this starts that same launcher: the installed tab
/// then starts the service exactly as it would for a player who opened the launcher by hand. The
/// launcher is never bypassed and the game is not started here.
/// </summary>
internal static class ServiceBootstrap
{
    private static string InstallFile(string stateDirectory)=>Path.Combine(stateDirectory,"install.ini");

    /// Records where the launcher lives, so a cold start does not have to guess.
    public static void RememberLauncher(string stateDirectory,string launcherPath)
    {
        Directory.CreateDirectory(stateDirectory);
        File.WriteAllText(InstallFile(stateDirectory),"Launcher="+launcherPath);
    }

    public static string? FindLauncher(string stateDirectory)
    {
        var running=Process.GetProcessesByName("HD_Launcher").FirstOrDefault();
        try
        {
            string? path=running?.MainModule?.FileName;
            if(path is not null&&File.Exists(path))return path;
        }
        catch(Exception e)when(e is InvalidOperationException or System.ComponentModel.Win32Exception){}
        finally{running?.Dispose();}

        string file=InstallFile(stateDirectory);
        if(File.Exists(file))
        {
            string recorded=File.ReadAllText(file).Split('=',2).ElementAtOrDefault(1)?.Trim()??"";
            if(File.Exists(recorded))return recorded;
        }
        return null;
    }

    /// Returns once the service answers, starting the launcher if that is what is missing.
    public static async Task<string> EnsureRunning(string endpoint,string stateDirectory,CancellationToken ct)
    {
        string tokenFile=Path.Combine(stateDirectory,"connection.token");
        if(await Answers(endpoint,tokenFile,ct))return "already_running";

        string launcher=FindLauncher(stateDirectory)
            ??throw new InvalidOperationException(
                "HD Launcher not found. Install the add-on with tools/install.ps1, or open the launcher once so its location is known.");
        EnsureWatcher();
        if(Process.GetProcessesByName("HD_Launcher").Length==0)
        {
            var start=new ProcessStartInfo(launcher){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(launcher)!};
            using var started=Process.Start(start)
                ??throw new InvalidOperationException("Could not start the HD Launcher");
        }
        // The tab raises the service itself; waiting on the endpoint is what proves it happened.
        for(int attempt=0;attempt<120;attempt++)
        {
            await Task.Delay(500,ct);
            if(await Answers(endpoint,tokenFile,ct))return "launcher_started_service";
        }
        throw new InvalidOperationException(
            "The launcher is open but the MCP tab did not start the service. Check that autostart is enabled on the MCP tab.");
    }

    /// The watcher is what puts the MCP tab into the launcher. Normally it is started at logon by
    /// the autostart entry the installer creates; starting it here covers the first run after an
    /// install and any session where it was stopped by hand.
    private static void EnsureWatcher()
    {
        if(Process.GetProcessesByName("launcher-attach").Length>0)return;
        string watcher=Path.Combine(AppContext.BaseDirectory,"launcher-attach.exe");
        string module=Path.Combine(AppContext.BaseDirectory,"hota_launcher_tab.dll");
        if(!File.Exists(watcher)||!File.Exists(module))return;
        var start=new ProcessStartInfo(watcher){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=AppContext.BaseDirectory};
        start.ArgumentList.Add("--watch");
        start.ArgumentList.Add(module);
        try{Process.Start(start)?.Dispose();}
        catch(System.ComponentModel.Win32Exception){}
    }

    private static async Task<bool> Answers(string endpoint,string tokenFile,CancellationToken ct)
    {
        if(!File.Exists(tokenFile))return false;
        try
        {
            using var http=new HttpClient{BaseAddress=new Uri(endpoint.TrimEnd('/')+"/"),Timeout=TimeSpan.FromSeconds(3)};
            http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",File.ReadAllText(tokenFile).Trim());
            using var response=await http.PostAsync("bridge/status",new StringContent("{}",System.Text.Encoding.UTF8,"application/json"),ct);
            return response.IsSuccessStatusCode;
        }
        catch(Exception e)when(e is HttpRequestException or TaskCanceledException or IOException or UriFormatException)
        {
            return false;
        }
    }
}
