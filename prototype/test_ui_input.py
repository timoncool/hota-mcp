"""Bounded experiment: deliver a click to a memory-discovered UI element."""
import ctypes as c
from ctypes import wintypes as w
import subprocess,sys,json,time
from pathlib import Path
u=c.WinDLL('user32',use_last_error=True)
u.SetProcessDPIAware()
u.GetWindowThreadProcessId.argtypes=[w.HWND,c.POINTER(w.DWORD)]
u.GetClientRect.argtypes=[w.HWND,c.POINTER(w.RECT)]
u.PostMessageW.argtypes=[w.HWND,w.UINT,w.WPARAM,w.LPARAM]
u.GetForegroundWindow.restype=w.HWND
u.IsWindowVisible.argtypes=[w.HWND]
cbtype=c.WINFUNCTYPE(w.BOOL,w.HWND,w.LPARAM)
u.EnumWindows.argtypes=[cbtype,w.LPARAM]
pid=int(sys.argv[1]); windows=[]
@cbtype
def enum(hwnd,param):
    p=w.DWORD();u.GetWindowThreadProcessId(hwnd,c.byref(p))
    if p.value==pid and u.IsWindowVisible(hwnd): windows.append(hwnd)
    return True
u.EnumWindows(enum,0)
if len(windows)!=1: raise RuntimeError(('Expected one visible game window',windows))
hwnd=windows[0]; rect=w.RECT()
if not u.GetClientRect(hwnd,c.byref(rect)): raise c.WinError(c.get_last_error())
def snapshot():
    return json.loads(subprocess.check_output([sys.executable,str(Path(__file__).with_name('probe_ui.py')),str(pid)]))
before=snapshot(); surface=before['surface_size']
out={'hwnd':hwnd,'foreground':u.GetForegroundWindow(),'client':[rect.right,rect.bottom],'surface':surface}
if len(sys.argv)>2:
    itemid=int(sys.argv[2]); expected=sys.argv[3]
    active=[d for d in before['dialogs'] if d['address']==before['last']]
    if len(active)!=1: raise RuntimeError('Expected identifiable last dialog')
    dlg=active[0]
    matches=[i for i in dlg['items'] if i['id']==itemid and i.get('asset')==expected]
    if len(matches)!=1: raise RuntimeError('Target identity mismatch')
    item=matches[0]; x,y,width,height=item['xywh']
    if not width or not height or item['type_state'][1]&6!=6: raise RuntimeError('Inactive target')
    gx=dlg['xywh'][0]+x+width//2; gy=dlg['xywh'][1]+y+height//2
    if not 0<=gx<surface[0] or not 0<=gy<surface[1]: raise RuntimeError('Outside surface')
    # Experimental full-client scaling; effect must be confirmed by state.
    cx=round(gx*rect.right/surface[0]);cy=round(gy*rect.bottom/surface[1])
    if max(cx,cy)>32767: raise RuntimeError('Coordinate overflow')
    lp=cx|(cy<<16)
    for msg,wp in [(0x200,0),(0x201,1),(0x202,0)]:
        if not u.PostMessageW(hwnd,msg,wp,lp): raise c.WinError(c.get_last_error())
        time.sleep(.08)
    out['target']={'id':itemid,'asset':expected,'game_xy':[gx,gy],'client_xy':[cx,cy]}
    time.sleep(.5)
    after=snapshot()
    out['before_dialog']=dlg['address']
    out['after']=after
print(json.dumps(out,ensure_ascii=True,indent=2))
