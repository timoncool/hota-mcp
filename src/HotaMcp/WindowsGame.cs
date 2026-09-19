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
    public WindowsGame(int pid)
    {
        Process = Process.GetProcessById(pid);
        string path = Process.MainModule?.FileName ?? throw new InvalidOperationException("Game path unavailable");
        RequireHash(path, "5AAAB925F06CCCF23BB09814767590A95B84A557EB33D244800520BE4F1F18DE");
        RequireHash(Path.Combine(Path.GetDirectoryName(path)!, "HotA.dll"),
            "0A1DAA1D8F29870B5CB72EBBA54A88C43A366473530B908BD23FAFC7968223A7");
        handle = OpenProcess(0x410, false, pid);
        if (handle.IsInvalid) throw new InvalidOperationException("Cannot read game process");
        Window = Process.MainWindowHandle;
        if (Window == 0) { handle.Dispose(); throw new InvalidOperationException("Game window unavailable"); }
        SetProcessDPIAware();
    }
    private static void RequireHash(string path, string expected)
    {
        using var file = File.OpenRead(path);
        if (Convert.ToHexString(SHA256.HashData(file)) != expected)
            throw new InvalidOperationException("Unsupported game build; adapter update required");
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
    public async Task MouseAsync(int gameX,int gameY,int width,int height,bool click,CancellationToken ct)
    {
        if(!GetClientRect(Window,out Rect rect) || IsIconic(Window))
            throw new InvalidOperationException("Game window unavailable or minimized");
        if(gameX<0 || gameY<0 || gameX>=width || gameY>=height || width<1 || height<1)
            throw new InvalidOperationException("Target is outside game surface");
        int x=(int)Math.Round((double)gameX*rect.Right/width);
        int y=(int)Math.Round((double)gameY*rect.Bottom/height);
        if(x>32767 || y>32767) throw new InvalidOperationException("Window dimensions unsupported");
        nint lp=(nint)(x|(y<<16));
        if(!PostMessageW(Window,0x200,0,lp)) throw new InvalidOperationException("Mouse dispatch failed");
        if(!click) return;
        ct.ThrowIfCancellationRequested();
        if(!PostMessageW(Window,0x201,1,lp)) throw new InvalidOperationException("Mouse down failed");
        try { await Task.Delay(60,CancellationToken.None); }
        finally { if(!PostMessageW(Window,0x202,0,lp)) throw new InvalidOperationException("Mouse release failed"); }
    }
    public void Dispose(){handle.Dispose();Process.Dispose();}
    public bool NativeReady=>GetPropW(Window,"HotAMcp.GameBridge.v1")!=0;
    public void NativeAction(int operation,int player,int argument=0)
    {
        if(operation is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 9 or 10 or 20 or 21 or 22)||player is <0 or >7||argument is <0 or >255)throw new InvalidOperationException("Unsupported native command");
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
    [StructLayout(LayoutKind.Sequential)] private struct Rect {public int Left,Top,Right,Bottom;}
    [DllImport("kernel32.dll",SetLastError=true)] private static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool ReadProcessMemory(SafeProcessHandle process,nint address,byte[] buffer,nuint length,out nuint read);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window,out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll",SetLastError=true)] private static extern bool PostMessageW(nint window,uint message,nuint wp,nint lp);
}
