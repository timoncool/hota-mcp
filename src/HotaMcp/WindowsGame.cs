using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HotaMcp;

internal sealed class WindowsGame : IDisposable
{
    private readonly SafeProcessHandle handle;
    public Process Process { get; }
    public nint Window { get; }
    public BuildFingerprint Build { get; }
    public WindowsGame(int pid)
    {
        Process = Process.GetProcessById(pid);
        string path = Process.MainModule?.FileName ?? throw new InvalidOperationException("Game path unavailable");
        handle = OpenProcess(0x410, false, pid);
        if (handle.IsInvalid) throw new InvalidOperationException("Cannot read game process");
        Window = Process.MainWindowHandle;
        if (Window == 0) { handle.Dispose(); throw new InvalidOperationException("Game window unavailable"); }
        SetProcessDPIAware();
        Build = BuildProfile.Probe(this, Hash(path), Hash(Path.Combine(Path.GetDirectoryName(path)!, "HotA.dll")));
        if (!Build.Usable) { handle.Dispose(); throw new InvalidOperationException(Build.Summary); }
    }
    private static string Hash(string path)
    {
        try { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
        catch (IOException) { return ""; }
    }
    public byte[] Read(uint address, int length)
    {
        if (address < 0x10000 || length < 1 || length > 1048576 || (ulong)address+(uint)length > uint.MaxValue)
            throw new InvalidOperationException("Invalid game layout");
        byte[] bytes = new byte[length];
        if (!ReadProcessMemory(handle, (nint)address, bytes, (nuint)length, out nuint count) || count != (nuint)length)
            throw new InvalidOperationException("Game state unavailable or changing");
        return bytes;
    }
    public uint U32(uint address) => BitConverter.ToUInt32(Read(address,4));
    public int I32(uint address) => BitConverter.ToInt32(Read(address,4));
    public string? Text(uint address, int maximum = 1024)
    {
        if(address < 0x10000) return null;
        var bytes = new List<byte>();
        for(int i=0;i<maximum;i++)
        {
            byte b=Read(address+(uint)i,1)[0];
            if(b==0) return Encoding.GetEncoding(1251).GetString(bytes.ToArray());
            bytes.Add(b);
        }
        return Encoding.GetEncoding(1251).GetString(bytes.ToArray());
    }
    /// Turns a point on the game's drawing surface into a point in the window.
    ///
    /// The window and the surface do not have to share an aspect ratio: a 800x600 surface inside a
    /// 3840x1600 client is drawn scaled to fit and centred, with bars on the sides. Stretching to
    /// the full client instead happens to be right in the middle of the screen and wrong at the
    /// edges, which is why presses near a window's own exit button used to miss.
    private (int X,int Y) ToWindow(int gameX,int gameY,int width,int height)
    {
        if(!GetClientRect(Window,out Rect rect) || IsIconic(Window))
            throw new InvalidOperationException("Game window unavailable or minimized");
        if(gameX<0 || gameY<0 || gameX>=width || gameY>=height || width<1 || height<1)
            throw new InvalidOperationException("Target is outside game surface");
        if(rect.Right<1||rect.Bottom<1)throw new InvalidOperationException("Game window has no client area");
        double scale=Math.Min((double)rect.Right/width,(double)rect.Bottom/height);
        int drawnWidth=(int)Math.Round(width*scale),drawnHeight=(int)Math.Round(height*scale);
        int x=(rect.Right-drawnWidth)/2+(int)Math.Round((gameX+0.5)*scale);
        int y=(rect.Bottom-drawnHeight)/2+(int)Math.Round((gameY+0.5)*scale);
        if(x>32767||y>32767)throw new InvalidOperationException("Window dimensions unsupported");
        return(x,y);
    }

    public async Task MouseAsync(int gameX,int gameY,int width,int height,bool click,CancellationToken ct)
    {
        var (x,y)=ToWindow(gameX,gameY,width,height);
        nint lp=(nint)(x|(y<<16));
        if(!PostMessageW(Window,0x200,0,lp)) throw new InvalidOperationException("Mouse dispatch failed");
        if(!click) return;
        ct.ThrowIfCancellationRequested();
        if(!PostMessageW(Window,0x201,1,lp)) throw new InvalidOperationException("Mouse down failed");
        try { await Task.Delay(60,CancellationToken.None); }
        finally { if(!PostMessageW(Window,0x202,0,lp)) throw new InvalidOperationException("Mouse release failed"); }
    }
    private nint rightButton;
    public Task RightMouseDownAsync(int gameX,int gameY,int width,int height,CancellationToken ct)
    {
        // In-game right button: the info card of a control stays on screen while the button is
        // held, so down and up are separate steps and the card is read in between.
        var (x,y)=ToWindow(gameX,gameY,width,height);
        nint lp=(nint)(x|(y<<16));
        if(!PostMessageW(Window,0x200,0,lp)) throw new InvalidOperationException("Mouse dispatch failed");
        ct.ThrowIfCancellationRequested();
        if(!PostMessageW(Window,0x204,2,lp)) throw new InvalidOperationException("Right mouse down failed");
        rightButton=lp;
        return Task.CompletedTask;
    }
    public async Task RightMouseUpAsync()
    {
        if(rightButton==0) return;
        nint lp=rightButton;
        rightButton=0;
        if(!PostMessageW(Window,0x205,0,lp)) throw new InvalidOperationException("Right mouse release failed");
        await Task.Delay(60,CancellationToken.None);
    }
    public async Task DragAsync(int fromX,int fromY,int toX,int toY,int width,int height,CancellationToken ct)
    {
        var (fx,fy)=ToWindow(fromX,fromY,width,height);
        var (tx,ty)=ToWindow(toX,toY,width,height);
        if(!PostMessageW(Window,0x200,0,(nint)(fx|(fy<<16)))) throw new InvalidOperationException("Mouse dispatch failed");
        if(!PostMessageW(Window,0x201,1,(nint)(fx|(fy<<16)))) throw new InvalidOperationException("Mouse down failed");
        try
        {
            await Task.Delay(120,CancellationToken.None);
            const int steps=12;
            for(int i=1;i<=steps;i++)
            {
                ct.ThrowIfCancellationRequested();
                int x=fx+(tx-fx)*i/steps,y=fy+(ty-fy)*i/steps;
                if(!PostMessageW(Window,0x200,1,(nint)(x|(y<<16)))) throw new InvalidOperationException("Mouse move failed");
                await Task.Delay(55,CancellationToken.None);
            }
            await Task.Delay(120,CancellationToken.None);
        }
        finally { if(!PostMessageW(Window,0x202,0,(nint)(tx|(ty<<16)))) throw new InvalidOperationException("Mouse release failed"); }
    }
    public void Dispose(){handle.Dispose();Process.Dispose();}
    public bool NativeReady=>GetPropW(Window,"HotAMcp.GameBridge.v1")!=0;
    public void NativeAction(int operation,int player,int argument=0)
    {
        if(operation is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 9 or 10 or 20 or 21 or 22 or 23 or 24 or 25 or 26 or 29 or 30 or 31)||player is <0 or >7||argument is <0 or >131071)throw new InvalidOperationException("Unsupported native command");
        if(GetPropW(Window,"HotAMcp.GameBridge.v1")==0)throw new InvalidOperationException("Native game adapter is not attached");
        if(!PostMessageW(Window,0x8392,(nuint)operation,(nint)(player|(argument<<8))))throw new InvalidOperationException("Native command dispatch failed");
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern nint GetPropW(nint window,string name);
    public async Task KeyAsync(ushort key,ushort scan)
    {
        nint data=(nint)((scan<<16)|1);
        if(!PostMessageW(Window,0x100,key,data))throw new InvalidOperationException("Key dispatch failed");
        try{await Task.Delay(60);}
        finally{PostMessageW(Window,0x101,key,(nint)((long)data|0xc0000000));}
        await Task.Delay(100);
    }
    /// Holds Ctrl while the key is pressed. The game reads the modifier from the message flags of
    /// the key it receives, which is how "Ctrl + Arrow Keys - Scrolls Adventure Map" reaches it.
    public async Task KeyWithControlAsync(ushort key,ushort scan)
    {
        const ushort control=0x11;
        nint controlData=(nint)((0x1d<<16)|1);
        if(!PostMessageW(Window,0x100,control,controlData))throw new InvalidOperationException("Key dispatch failed");
        try
        {
            await Task.Delay(30,CancellationToken.None);
            await KeyAsync(key,scan);
        }
        finally{PostMessageW(Window,0x101,control,(nint)((long)controlData|0xc0000000));}
        await Task.Delay(60,CancellationToken.None);
    }

    public async Task TextAsync(string text)
    {
        // Ordinary window character messages into the focused game edit control.
        // No global input injection, no window activation.
        foreach(char character in text)
        {
            if(!PostMessageW(Window,0x102,(nuint)character,1))throw new InvalidOperationException("Character dispatch failed");
            await Task.Delay(40,CancellationToken.None);
        }
    }
    public async Task ClickAsync(int gameX,int gameY,int width,int height,CancellationToken ct)=>await MouseAsync(gameX,gameY,width,height,true,ct);
    [StructLayout(LayoutKind.Sequential)] private struct Rect {public int Left,Top,Right,Bottom;}
    [DllImport("kernel32.dll",SetLastError=true)] private static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool ReadProcessMemory(SafeProcessHandle process,nint address,byte[] buffer,nuint length,out nuint read);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window,out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll",SetLastError=true)] private static extern bool PostMessageW(nint window,uint message,nuint wp,nint lp);
}
