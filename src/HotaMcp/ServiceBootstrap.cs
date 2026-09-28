using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;

namespace HotaMcp;

/// <summary>
/// Brings the whole chain up from a cold machine: service, then the player's own HD Launcher with
/// the MCP tab in it, then the game through the launcher's Play button.
///
/// The «HotA MCP» shortcut runs HotaMcp.exe --launch. An MCP client starts the same chain only
/// when its start_game tool is called explicitly. Merely connecting over stdio does nothing.
/// </summary>
internal static class ServiceBootstrap
{
    // Launch through the desktop shell so service lifetime is independent of the MCP host's job.
    internal static void StartDetached(string executable,string arguments,string directory)
    {
        Exception? failure=null;
        var thread=new Thread(()=>
        {
            object? windows=null,desktop=null,view=null,shell=null;
            try
            {
                windows=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"),true)!);
                object location=0,unused=null!;
                int window;
                desktop=((dynamic)windows!).FindWindowSW(ref location,ref unused,8,out window,1);
                if(desktop is null)throw new InvalidOperationException("The Windows desktop shell is not available");
                view=((dynamic)desktop).Document;
                shell=((dynamic)view!).Application;
                ((dynamic)shell!).ShellExecute(executable,arguments,directory,"open",0);
            }
            catch(Exception e){failure=e;}
            finally
            {
                foreach(var value in new[]{shell,view,desktop,windows})
                    if(value is not null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if(failure is not null)throw new InvalidOperationException("Could not launch HotA through the Windows desktop shell",failure);
    }

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

    /// The launcher this service is hosted by: the one already open, or the recorded one started now.
    public static int OpenLauncher(string stateDirectory)
    {
        using(var running=Process.GetProcessesByName("HD_Launcher").FirstOrDefault())
            if(running is not null)return running.Id;
        string launcher=FindLauncher(stateDirectory)
            ??throw new InvalidOperationException("HD Launcher not found. Install the add-on with tools/install.ps1.");
        using var started=Process.Start(new ProcessStartInfo(launcher){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(launcher)!})
            ??throw new InvalidOperationException("Could not start the HD Launcher");
        return started.Id;
    }

    /// Puts the MCP tab into the launcher once its window is up. The helper stays alive while the
    /// launcher lives — the tab's window callbacks live in its module — and exits with it.
    public static async Task AttachTab(int launcherPid,CancellationToken ct)
    {
        string helper=Path.Combine(AppContext.BaseDirectory,"launcher-attach.exe");
        string module=Path.Combine(AppContext.BaseDirectory,"hota_launcher_tab.dll");
        if(!File.Exists(helper)||!File.Exists(module))
            throw new InvalidOperationException("The MCP tab files are missing next to HotaMcp.exe; reinstall the add-on");
        using var launcher=Process.GetProcessById(launcherPid);
        for(int attempt=0;attempt<240&&launcher.MainWindowHandle==0;attempt++)
        {
            await Task.Delay(250,ct);
            launcher.Refresh();
        }
        if(launcher.MainWindowHandle==0)throw new InvalidOperationException("The HD Launcher window did not appear");
        var start=new ProcessStartInfo(helper){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=AppContext.BaseDirectory};
        start.ArgumentList.Add(launcherPid.ToString());
        start.ArgumentList.Add(module);
        Process.Start(start)?.Dispose();
    }

    /// Returns once the service answers, starting the whole chain if nothing is running yet.
    public static async Task<string> EnsureRunning(string endpoint,string stateDirectory,CancellationToken ct)
    {
        string tokenFile=Path.Combine(stateDirectory,"connection.token");
        if(await Answers(endpoint,tokenFile,ct))return "already_running";
        StartDetached(Path.Combine(AppContext.BaseDirectory,"HotaMcp.exe"),"--launch",AppContext.BaseDirectory);
        for(int attempt=0;attempt<120;attempt++)
        {
            await Task.Delay(500,ct);
            if(await Answers(endpoint,tokenFile,ct))return "service_started";
        }
        throw new InvalidOperationException("HotaMcp --launch did not bring the service up within a minute");
    }

    public static async Task<bool> Answers(string endpoint,string tokenFile,CancellationToken ct)
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

    /// Asks a running service to press the launcher's Play button.
    public static async Task StartGame(string endpoint,string tokenFile,CancellationToken ct)
    {
        using var http=new HttpClient{BaseAddress=new Uri(endpoint.TrimEnd('/')+"/"),Timeout=TimeSpan.FromSeconds(10)};
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",File.ReadAllText(tokenFile).Trim());
        using var _=await http.PostAsync("bridge/start",new StringContent("{}",System.Text.Encoding.UTF8,"application/json"),ct);
    }

    /// The shortcut starts a console program; the service needs no window of its own.
    [DllImport("kernel32.dll")] public static extern bool FreeConsole();
}
