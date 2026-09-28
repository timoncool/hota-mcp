using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HotaMcp;

/// <summary>
/// Offers a newer release when the «HotA MCP» shortcut starts the chain, before the launcher and the
/// game are up, the way the studios' updaters do: the latest GitHub release is compared with this
/// build, the player is asked, the installer is downloaded, checked against the SHA-256 GitHub keeps
/// for the file, and started in its update mode, which waits for this service to exit, installs into
/// the same folder and brings the chain back up.
/// </summary>
internal static partial class Updater
{
    private const string LatestRelease="https://api.github.com/repos/timoncool/hota-mcp/releases/latest";
    private const string Title="HotA MCP — обновление";

    /// Started is true when the installer runs and the service must exit to let it replace the files.
    public readonly record struct Outcome(bool Started,string Log);

    public static async Task<Outcome> OfferAsync(CancellationToken ct)
    {
        // A developer install (tools/install.ps1) has no uninstaller and is updated from the source.
        string root=Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!;
        if(!File.Exists(Path.Combine(root,"uninstall.exe")))return new(false,"[OK] developer install: not updated");
        Version current=Assembly.GetEntryAssembly()!.GetName().Version!;
        // The installer that brought this version in has finished by now.
        string previous=Path.Combine(Path.GetTempPath(),$"HotaMcp-Setup-{current.ToString(3)}.exe");
        if(File.Exists(previous))File.Delete(previous);
        // A launcher or game opened by hand would keep the installer waiting and the update would not land.
        if(Process.GetProcessesByName("HD_Launcher").Length>0||Process.GetProcessesByName("h3hota HD").Length>0)
            return new(false,"[OK] HD Launcher or the game is already open: the update is offered on a start with both closed");
        // HOTA_MCP_RELEASE_API points a developer's test at a local copy of the release answer.
        string source=Environment.GetEnvironmentVariable("HOTA_MCP_RELEASE_API") is {Length:>0} test?test:LatestRelease;

        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(10)};
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HotaMcp",current.ToString(3)));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var release=JsonDocument.Parse(await http.GetStringAsync(source,ct));
        string tag=release.RootElement.GetProperty("tag_name").GetString()??"";
        if(!Version.TryParse(tag.TrimStart('v'),out var latest))
            throw new InvalidOperationException($"The latest release tag «{tag}» is not a version");
        if(new Version(latest.Major,latest.Minor,Math.Max(latest.Build,0))<=new Version(current.Major,current.Minor,Math.Max(current.Build,0)))
            return new(false,$"[OK] {current.ToString(3)} is the latest release");
        var asset=release.RootElement.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(a=>InstallerName().IsMatch(a.GetProperty("name").GetString()??""));
        if(asset.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException($"Release {tag} has no installer");
        string name=asset.GetProperty("name").GetString()!;
        string url=asset.GetProperty("browser_download_url").GetString()
            ??throw new InvalidOperationException($"Release {tag} gives no download address for {name}");
        string digest=asset.TryGetProperty("digest",out var d)?d.GetString()??"":"";
        if(!digest.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Release {tag} publishes no SHA-256 for {name}");

        if(!Ask($"Доступна HotA MCP {latest.ToString(3)} (установлена {current.ToString(3)}).\n\nУстановить сейчас? "
            +"Служба, HD Launcher и игра запустятся заново после установки; партии, планы и журналы останутся.",
            0x04|0x40))return new(false,$"[OK] {latest.ToString(3)} declined");

        string file=Path.Combine(Path.GetTempPath(),name);
        using(var download=new HttpClient{Timeout=TimeSpan.FromMinutes(10)})
        {
            download.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HotaMcp",current.ToString(3)));
            await using var output=File.Create(file);
            await using var input=await download.GetStreamAsync(url,ct);
            await input.CopyToAsync(output,ct);
        }
        string actual;
        await using(var check=File.OpenRead(file))actual=Convert.ToHexString(await SHA256.HashDataAsync(check,ct));
        if(!string.Equals(actual,digest["sha256:".Length..],StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(file);
            Ask($"Скачанный установщик {name} не совпал с опубликованным (SHA-256). Обновление не установлено; "
                +"HotA MCP запустится в текущей версии.",0x00|0x10);
            return new(false,$"[ERROR] {name} did not match its published SHA-256");
        }
        // The installer takes the folder as /D=, which must be the last argument and unquoted.
        ServiceBootstrap.StartDetached(file,$"/S /UPDATE /D={root}",Path.GetTempPath());
        return new(true,$"[OK] installer for {latest.ToString(3)} started");
    }

    private static bool Ask(string text,uint style)=>MessageBoxW(0,text,Title,style|0x10000|0x40000)==6;

    [GeneratedRegex(@"^HotaMcp-Setup-.+\.exe$")]private static partial Regex InstallerName();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int MessageBoxW(nint window,string text,string caption,uint style);
}
