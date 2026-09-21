#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>
#include <string>
#include <iostream>
#include <fstream>
#include <bcrypt.h>
#pragma comment(lib, "bcrypt.lib")

namespace {
constexpr UINT kInit = WM_APP + 0x371;
constexpr UINT kInspect = WM_APP + 0x372;
constexpr UINT kRemove = WM_APP + 0x373;
constexpr wchar_t kInstalled[] = L"HotAMcp.LauncherTab.v1";
DWORD wantedPid;
HWND target;
BOOL CALLBACK Find(HWND hwnd, LPARAM) {
    DWORD pid; GetWindowThreadProcessId(hwnd, &pid);
    wchar_t cls[64]{}; GetClassNameW(hwnd, cls, 64);
    if (pid == wantedPid && wcscmp(cls,L"#32770") == 0 && GetDlgItem(hwnd,1046)) target=hwnd;
    return TRUE;
}
HWND FindLauncherWindow(DWORD pid){ wantedPid=pid; target=nullptr; EnumWindows(Find,0); return target; }

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
bool Exists(const std::wstring& path){return GetFileAttributesW(path.c_str())!=INVALID_FILE_ATTRIBUTES;}
// Compatibility is decided by the installation and by the controls the tab actually uses, not by
// byte equality with one HD Mod release: HD updates itself and every hash moves with it. The
// observed binaries are reported so a new release is visible instead of silently blocking the tab.
void Report(const char* name,const std::string& seen,const char* validated){
    std::cout<<name<<": "<<(seen.empty()?std::string("unreadable"):seen)
             <<(seen==validated?" (validated build)":" (new build; structural checks decide)")<<"\n";
}

// Exit codes double as diagnostics for the installer and the watch loop.
enum : int { kOk=0, kUsage=2, kNoProcess=3, kNotHota=4, kNoWindow=5,
             kAlready=6, kLoadFailed=7, kHookFailed=8, kNotInstalled=9, kDetachFailed=10 };

std::wstring InstallDirectory(HANDLE process,std::wstring& imagePath){
    wchar_t path[32768]{}; DWORD length=32768;
    if(!QueryFullProcessImageNameW(process,0,path,&length)) return {};
    imagePath=path;
    return imagePath.substr(0,imagePath.find_last_of(L"\\/"));
}

/// Attaches the tab to one launcher process and stays alive while the tab is in use: the hook
/// module's code is still referenced by the launcher's window subclass callbacks.
int AttachTo(DWORD pid,const wchar_t* dllPath,bool detach,bool quiet){
    HANDLE process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,pid);
    if(!process){ if(!quiet)std::cerr<<"Cannot open launcher\n"; return kNoProcess; }
    std::wstring imagePath;
    auto directory=InstallDirectory(process,imagePath);
    if(directory.empty()){ if(!quiet)std::cerr<<"Cannot read launcher image path\n"; CloseHandle(process); return kNotHota; }
    if(!Exists(directory+L"\\h3hota HD.exe")||!Exists(directory+L"\\HotA.dll")){
        if(!quiet)std::cerr<<"Launcher is not part of a HotA installation\n";
        CloseHandle(process); return kNotHota;
    }
    if(!quiet){
        Report("HD_Launcher.exe",HashFile(imagePath.c_str()),
            "9FCD3FA166047D5944358E07CF993402837CC6FD8C3083E9CA7C6E322504D74C");
        Report("HD_LauncherNative.dll",HashFile((directory+L"\\HD_LauncherNative.dll").c_str()),
            "8B61E97C68ABB15E2E209C0801CDFF2AA90A93E946999EF30BDF4A8B9BF706BC");
    }
    HWND window=FindLauncherWindow(pid);
    if(!window){ if(!quiet)std::cerr<<"Launcher tab control not found\n"; CloseHandle(process); return kNoWindow; }
    if(detach){
        DWORD_PTR result=0;
        bool ok=SendMessageTimeoutW(window,kRemove,0,0,SMTO_ABORTIFHUNG,2000,&result)!=0;
        CloseHandle(process);
        std::cout<<(ok?"Detached\n":"Detach failed\n");
        return ok?kOk:kDetachFailed;
    }
    if(GetPropW(window,kInstalled)){ if(!quiet)std::cerr<<"Already attached\n"; CloseHandle(process); return kAlready; }
    HMODULE dll=LoadLibraryW(dllPath);
    if(!dll){ if(!quiet)std::cerr<<"DLL load failed: "<<GetLastError()<<"\n"; CloseHandle(process); return kLoadFailed; }
    auto proc=reinterpret_cast<HOOKPROC>(GetProcAddress(dll,"_LauncherHook@12"));
    if(!proc) proc=reinterpret_cast<HOOKPROC>(GetProcAddress(dll,"LauncherHook"));
    DWORD thread=GetWindowThreadProcessId(window,nullptr);
    HHOOK hook=proc?SetWindowsHookExW(WH_GETMESSAGE,proc,dll,thread):nullptr;
    if(!hook){ if(!quiet)std::cerr<<"Hook failed: "<<GetLastError()<<"\n"; FreeLibrary(dll); CloseHandle(process); return kHookFailed; }
    PostMessageW(window,kInit,0,0);
    bool installed=false;
    for(int i=0;i<100;++i){
        if(GetPropW(window,kInstalled)){installed=true;break;}
        Sleep(50);
    }
    std::cout<<(installed?"Attached\n":"Attach failed\n")<<std::flush;
    if(installed){
        while(WaitForSingleObject(process,100)==WAIT_TIMEOUT && IsWindow(window) && GetPropW(window,kInstalled)){
            MSG msg; while(PeekMessageW(&msg,nullptr,0,0,PM_REMOVE)){TranslateMessage(&msg);DispatchMessageW(&msg);}
        }
    }
    UnhookWindowsHookEx(hook); FreeLibrary(dll); CloseHandle(process);
    return installed?kOk:kNotInstalled;
}

DWORD FindLauncherProcess(){
    HANDLE snapshot=CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS,0);
    if(snapshot==INVALID_HANDLE_VALUE) return 0;
    PROCESSENTRY32W entry{sizeof(PROCESSENTRY32W)};
    DWORD found=0;
    if(Process32FirstW(snapshot,&entry))
        do{
            if(_wcsicmp(entry.szExeFile,L"HD_Launcher.exe")!=0) continue;
            HWND window=FindLauncherWindow(entry.th32ProcessID);
            if(window&&!GetPropW(window,kInstalled)){found=entry.th32ProcessID;break;}
        }while(Process32NextW(snapshot,&entry));
    CloseHandle(snapshot);
    return found;
}

/// The installed mode: the tab must be there every time the player opens their own launcher,
/// including after HD Mod updates itself and restarts the launcher. Nothing in the game folder is
/// modified; the watcher only waits for the launcher window and hooks its UI thread.
int Watch(const wchar_t* dllPath){
    std::cout<<"Watching for HD Launcher\n"<<std::flush;
    for(;;){
        DWORD pid=FindLauncherProcess();
        if(!pid){ Sleep(1500); continue; }
        int result=AttachTo(pid,dllPath,false,true);
        // AttachTo returns when the launcher closes or the tab is removed by hand; either way the
        // watcher goes back to waiting instead of exiting.
        std::cout<<"Launcher "<<pid<<" released with code "<<result<<"\n"<<std::flush;
        Sleep(1500);
    }
}
}

int wmain(int argc,wchar_t** argv) {
    if(argc==3&&wcscmp(argv[1],L"--watch")==0) return Watch(argv[2]);
    if(argc!=3){
        std::cerr<<"Usage: launcher-attach PID DLL_PATH | launcher-attach PID --detach"
                   " | launcher-attach --watch DLL_PATH\n";
        return kUsage;
    }
    return AttachTo(wcstoul(argv[1],nullptr,10),argv[2],wcscmp(argv[2],L"--detach")==0,false);
}
