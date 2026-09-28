#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <iostream>

namespace {
constexpr UINT kInit = WM_APP + 0x391;
constexpr UINT kInspect = WM_APP + 0x393;
constexpr UINT kRemove = WM_APP + 0x394;
DWORD wantedPid;
HWND target;
BOOL CALLBACK Find(HWND hwnd, LPARAM) {
    DWORD pid; GetWindowThreadProcessId(hwnd, &pid);
    wchar_t cls[64]{}; GetClassNameW(hwnd, cls, 64);
    if (pid == wantedPid && IsWindowVisible(hwnd) && GetWindow(hwnd,GW_OWNER)==nullptr) target=hwnd;
    return TRUE;
}
}
int wmain(int argc,wchar_t** argv) {
    if(argc!=3){std::cerr<<"Usage: game-attach PID DLL_PATH\n";return 2;}
    wantedPid=wcstoul(argv[1],nullptr,10);
    HANDLE process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,wantedPid);
    if(!process){std::cerr<<"Cannot open game\n";return 3;}
    // No file hashes: HotA and HD Mod update themselves and every hash moves. Each operation of the
    // adapter checks the class and state of the structures it touches before it acts, and the service
    // probes the build's structures on attach.
    EnumWindows(Find,0);
    if(!target){std::cerr<<"Game tab control not found\n";CloseHandle(process);return 5;}
    if(GetPropW(target,L"HotAMcp.GameBridge.v1")){std::cerr<<"Already attached\n";CloseHandle(process);return 6;}
    HMODULE dll=LoadLibraryW(argv[2]);
    if(!dll){std::cerr<<"DLL load failed: "<<GetLastError()<<"\n";CloseHandle(process);return 7;}
    auto proc=reinterpret_cast<HOOKPROC>(GetProcAddress(dll,"_GameHook@12"));
    if(!proc) proc=reinterpret_cast<HOOKPROC>(GetProcAddress(dll,"GameHook"));
    DWORD thread=GetWindowThreadProcessId(target,nullptr);
    HHOOK hook=proc?SetWindowsHookExW(WH_GETMESSAGE,proc,dll,thread):nullptr;
    if(!hook){std::cerr<<"Hook failed: "<<GetLastError()<<"\n";FreeLibrary(dll);CloseHandle(process);return 8;}
    PostMessageW(target,kInit,0,0);
    bool installed=false;
    for(int i=0;i<100;++i){
        if(GetPropW(target,L"HotAMcp.GameBridge.v1")){installed=true;break;}
        Sleep(50);
    }
    std::cout<<(installed?"Attached\n":"Attach failed\n")<<std::flush;
    if(installed){
        // Keep the hook module alive while window subclass callbacks use its code.
        while(WaitForSingleObject(process,100)==WAIT_TIMEOUT && IsWindow(target) &&
            GetPropW(target,L"HotAMcp.GameBridge.v1")) {
            MSG msg; while(PeekMessageW(&msg,nullptr,0,0,PM_REMOVE)){TranslateMessage(&msg);DispatchMessageW(&msg);}
        }
    }
    UnhookWindowsHookEx(hook);FreeLibrary(dll);CloseHandle(process);
    return installed?0:9;
}
