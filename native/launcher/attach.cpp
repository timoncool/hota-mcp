#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>
#include <iostream>
#include <fstream>
#include <bcrypt.h>
#pragma comment(lib, "bcrypt.lib")

namespace {
constexpr UINT kInit = WM_APP + 0x371;
constexpr UINT kInspect = WM_APP + 0x372;
constexpr UINT kRemove = WM_APP + 0x373;
DWORD wantedPid;
HWND target;
BOOL CALLBACK Find(HWND hwnd, LPARAM) {
    DWORD pid; GetWindowThreadProcessId(hwnd, &pid);
    wchar_t cls[64]{}; GetClassNameW(hwnd, cls, 64);
    if (pid == wantedPid && wcscmp(cls,L"#32770") == 0 && GetDlgItem(hwnd,1046)) target=hwnd;
    return TRUE;
}
std::string HashFile(const wchar_t* path) {
    std::ifstream file(path,std::ios::binary);
    if(!file) return {};
    std::string data((std::istreambuf_iterator<char>(file)),std::istreambuf_iterator<char>());
    UCHAR digest[32]{};
    if(BCryptHash(BCRYPT_SHA256_ALG_HANDLE,nullptr,0,reinterpret_cast<PUCHAR>(data.data()),
        static_cast<ULONG>(data.size()),digest,32)<0) return {};
    const char* hex="0123456789ABCDEF"; std::string result;
    for(auto b:digest){result+=hex[b>>4]; result+=hex[b&15];} return result;
}
}
int wmain(int argc,wchar_t** argv) {
    if(argc!=3){std::cerr<<"Usage: launcher-attach PID DLL_PATH\n";return 2;}
    wantedPid=wcstoul(argv[1],nullptr,10);
    HANDLE process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,wantedPid);
    if(!process){std::cerr<<"Cannot open launcher\n";return 3;}
    wchar_t path[32768]{}; DWORD length=32768;
    if(!QueryFullProcessImageNameW(process,0,path,&length) ||
       HashFile(path)!="9FCD3FA166047D5944358E07CF993402837CC6FD8C3083E9CA7C6E322504D74C"){
        std::cerr<<"Unsupported launcher binary\n";CloseHandle(process);return 4;
    }
    EnumWindows(Find,0);
    if(!target){std::cerr<<"Launcher tab control not found\n";CloseHandle(process);return 5;}
    if(GetPropW(target,L"HotAMcp.LauncherTab.v1")){std::cerr<<"Already attached\n";CloseHandle(process);return 6;}
    HMODULE dll=LoadLibraryW(argv[2]);
    if(!dll){std::cerr<<"DLL load failed: "<<GetLastError()<<"\n";CloseHandle(process);return 7;}
    auto proc=reinterpret_cast<HOOKPROC>(GetProcAddress(dll,"_LauncherHook@12"));
    if(!proc) proc=reinterpret_cast<HOOKPROC>(GetProcAddress(dll,"LauncherHook"));
    DWORD thread=GetWindowThreadProcessId(target,nullptr);
    HHOOK hook=proc?SetWindowsHookExW(WH_GETMESSAGE,proc,dll,thread):nullptr;
    if(!hook){std::cerr<<"Hook failed: "<<GetLastError()<<"\n";FreeLibrary(dll);CloseHandle(process);return 8;}
    PostMessageW(target,kInit,0,0);
    bool installed=false;
    for(int i=0;i<100;++i){
        if(GetPropW(target,L"HotAMcp.LauncherTab.v1")){installed=true;break;}
        Sleep(50);
    }
    std::cout<<(installed?"Attached\n":"Attach failed\n")<<std::flush;
    if(installed){
        // Keep the hook module alive while window subclass callbacks use its code.
        while(WaitForSingleObject(process,100)==WAIT_TIMEOUT && IsWindow(target) &&
            GetPropW(target,L"HotAMcp.LauncherTab.v1")) {
            MSG msg; while(PeekMessageW(&msg,nullptr,0,0,PM_REMOVE)){TranslateMessage(&msg);DispatchMessageW(&msg);}
        }
    }
    UnhookWindowsHookEx(hook);FreeLibrary(dll);CloseHandle(process);
    return installed?0:9;
}
