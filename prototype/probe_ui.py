"""Read-only UI layout experiment. Not a player-facing API."""
import ctypes as c
from ctypes import wintypes as w
import struct, json, sys
k = c.WinDLL('kernel32', use_last_error=True)
k.OpenProcess.argtypes = [w.DWORD,w.BOOL,w.DWORD]
k.OpenProcess.restype = w.HANDLE
k.ReadProcessMemory.argtypes = [w.HANDLE,c.c_void_p,c.c_void_p,c.c_size_t,c.POINTER(c.c_size_t)]
k.CloseHandle.argtypes = [w.HANDLE]
h = k.OpenProcess(0x410,False,int(sys.argv[1]))
if not h: raise c.WinError(c.get_last_error())
def read(a,n):
    if not 0x10000 <= a < 2**32 or not 0 < n <= 65536: raise ValueError((a,n))
    b=c.create_string_buffer(n); got=c.c_size_t()
    if not k.ReadProcessMemory(h,a,b,n,c.byref(got)) or got.value != n: raise c.WinError(c.get_last_error())
    return b.raw
def u(a): return struct.unpack('<I',read(a,4))[0]
def string(a):
    if a < 65536: return None
    try:
        out=bytearray()
        for i in range(1024):
            ch=read(a+i,1)
            if ch==b'\0': return out.decode('cp1251',errors='replace')
            out.extend(ch)
    except OSError: return None
    return None
try:
    manager=u(0x6992d0)
    first,last=struct.unpack('<2I',read(manager+0x50,8))
    result={'manager':hex(manager),'first':hex(first),'last':hex(last),'dialogs':[]}
    result['surface_size']=struct.unpack('<2i',read(u(manager+0x40)+0x24,8))
    result['code_size']=[u(0x403401),u(0x4033fc)]
    for ptr in dict.fromkeys([first,last]):
        if not ptr: continue
        d=read(ptr,0x4c)
        start,end,cap=struct.unpack_from('<3I',d,0x34)
        if not start<=end<=cap or (end-start)%4 or (end-start)>8192: raise ValueError('Invalid vector')
        dialog={'address':hex(ptr),'vtable':hex(struct.unpack_from('<I',d)[0]),'xywh':struct.unpack_from('<4i',d,0x18),'items':[]}
        for pos in range(start,end,4):
            a=u(pos); b=read(a,0x30)
            if struct.unpack_from('<I',b,4)[0]!=ptr: raise ValueError('Wrong parent')
            v=u(a)
            item={'address':hex(a),'vtable':hex(v),'id':struct.unpack_from('<H',b,0x10)[0],'type_state':struct.unpack_from('<2H',b,0x14),'xywh':struct.unpack_from('<2h2H',b,0x18)}
            hint=string(u(a+0x20))
            if hint: item['hint']=hint
            if v in (0x642dc0,0x642df8): item['text']=string(u(a+0x34))
            if v==0x63bb54:
                item['asset']=string(u(a+0x30)+4)
                hs,he=struct.unpack('<2I',read(a+0x4c,8))
                if hs and hs<=he<=hs+64:
                    item['hotkeys']=[u(p) for p in range(hs,he,4)]
            dialog['items'].append(item)
        result['dialogs'].append(dialog)
    print(json.dumps(result,ensure_ascii=True,indent=2))
finally: k.CloseHandle(h)
