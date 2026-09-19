using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace HotaMcp;

internal static class LauncherActions
{
    public static object Graphics(int pid,string? renderer)
    {
        var controls=new List<object>();bool changed=false;
        EnumWindows((root,_)=>{
            GetWindowThreadProcessId(root,out uint owner);if(owner!=(uint)pid)return true;
            EnumChildWindows(root,(window,unused)=>{
                var cls=new StringBuilder(64);GetClassNameW(window,cls,64);
                if(cls.ToString()!="ComboBox")return true;
                int count=(int)SendMessageW(window,0x146,0,0);
                if(count<1||count>256)return true;
                var options=new List<string>();
                for(int i=0;i<count;i++)
                {
                    int length=(int)SendMessageW(window,0x149,(nuint)i,0);
                    if(length<0||length>512)return true;
                    var text=new StringBuilder(length+1);SendText(window,0x148,(nuint)i,text);options.Add(text.ToString());
                }
                if(!options.Any(s=>s.Contains("DirectDraw",StringComparison.OrdinalIgnoreCase)))return true;
                if(renderer!=null)
                {
                    int index=options.IndexOf(renderer);if(index<0)throw new InvalidOperationException("Renderer must match a reported option");
                    SendMessageW(window,0x14e,(nuint)index,0);
                    SendMessageW(GetParent(window),0x111,(nuint)(GetDlgCtrlID(window)|(1<<16)),window);
                    changed=true;
                }
                int selected=(int)SendMessageW(window,0x147,0,0);
                controls.Add(new{selected=selected>=0&&selected<options.Count?options[selected]:null,options});return true;
            },0);return true;
        },0);
        if(controls.Count!=1)throw new InvalidOperationException("Unique renderer control not found");
        return new{changed,controls};
    }
    public static void Play(int pid)
    {
        using var process=Process.GetProcessById(pid);
        if(!string.Equals(process.ProcessName,"HD_Launcher",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Parent is not HD Launcher");
        nint root=0,button=0;
        EnumWindows((window,_)=>{
            GetWindowThreadProcessId(window,out uint owner);
            if(owner!=(uint)pid)return true;
            nint candidate=GetDlgItem(window,1007);
            var name=new StringBuilder(64);GetClassNameW(candidate,name,name.Capacity);
            if(candidate!=0&&name.ToString()=="Button"&&IsWindowEnabled(candidate)){root=window;button=candidate;return false;}
            return true;
        },0);
        if(root==0)throw new InvalidOperationException("Launcher Play action is unavailable");
        // The existing Play button's command. No focus, cursor or keyboard input.
        if(!PostMessageW(root,0x111,1007,button))throw new InvalidOperationException("Launcher command failed");
    }
    private delegate bool Visitor(nint window,nint argument);
    [DllImport("user32.dll")]private static extern bool EnumWindows(Visitor visitor,nint argument);
    [DllImport("user32.dll")]private static extern bool EnumChildWindows(nint parent,Visitor visitor,nint argument);
    [DllImport("user32.dll")]private static extern nint GetParent(nint window);
    [DllImport("user32.dll")]private static extern int GetDlgCtrlID(nint window);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint SendMessageW(nint window,uint message,nuint wp,nint lp);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="SendMessageW")]private static extern nint SendText(nint window,uint message,nuint wp,StringBuilder text);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint window,out uint pid);
    [DllImport("user32.dll")]private static extern nint GetDlgItem(nint window,int id);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetClassNameW(nint window,StringBuilder name,int capacity);
    [DllImport("user32.dll")]private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll")]private static extern bool PostMessageW(nint window,uint message,nuint wp,nint lp);
}
