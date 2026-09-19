#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <commctrl.h>
#include <tlhelp32.h>
#include <string>
#include <vector>

namespace {
constexpr wchar_t kProperty[] = L"HotAMcp.LauncherTab.v1";
constexpr UINT kInit = WM_APP + 0x371;
constexpr UINT kInspect = WM_APP + 0x372;
constexpr UINT kRemove = WM_APP + 0x373;
constexpr UINT_PTR kSubclass = 0x484D4350;
constexpr int kStatus = 49001;
constexpr int kRefresh = 49002;
constexpr int kStart = 49003;
constexpr int kStop = 49004;
constexpr int kAutostart = 49005;
HMODULE module = nullptr;
HWND root = nullptr, tabs = nullptr, page = nullptr;
int tabIndex = -1;
bool ours = false;
HANDLE serverProcess = nullptr;
std::wstring settingsPath;
std::vector<HWND> hiddenPages;

std::wstring ServerCommand(const char* command) {
    std::wstring name=L"\\\\.\\pipe\\hota-mcp-control-"+std::to_wstring(GetCurrentProcessId());
    char response[2048]{}; DWORD received=0;
    if(!CallNamedPipeW(name.c_str(),const_cast<char*>(command),static_cast<DWORD>(strlen(command)),
        response,sizeof(response)-1,&received,100)) return {};
    int count=MultiByteToWideChar(CP_UTF8,0,response,received,nullptr,0);
    std::wstring result(count,L'\0');
    MultiByteToWideChar(CP_UTF8,0,response,received,result.data(),count);
    return result;
}

void StartServer() {
    if(!ServerCommand("status").empty()){return;}
    if(serverProcess) {
        if(WaitForSingleObject(serverProcess,0)==WAIT_TIMEOUT)return;
        CloseHandle(serverProcess);serverProcess=nullptr;
    }
    wchar_t file[32768]{}; GetModuleFileNameW(module,file,32768);
    std::wstring dir(file);dir.resize(dir.find_last_of(L"\\/"));
    std::wstring exe=dir+L"\\HotaMcp.exe";
    std::wstring command=L"\""+exe+L"\" --launcher-pid "+std::to_wstring(GetCurrentProcessId());
    STARTUPINFOW startup{};startup.cb=sizeof(startup);PROCESS_INFORMATION process{};
    if(!CreateProcessW(exe.c_str(),command.data(),nullptr,nullptr,FALSE,CREATE_NO_WINDOW,
        nullptr,dir.c_str(),&startup,&process)) {
        SetDlgItemTextW(page,kStatus,L"Не удалось запустить MCP-сервер. Проверьте установку HotaMcp.exe.");return;
    }
    CloseHandle(process.hThread);serverProcess=process.hProcess;
    SetDlgItemTextW(page,kStatus,L"Запуск MCP-сервера...");
}

std::wstring GameStatus() {
    auto server=ServerCommand("status");
    if(!server.empty()) return server;
    if(serverProcess) {
        DWORD code=0;
        if(GetExitCodeProcess(serverProcess,&code) && code==STILL_ACTIVE)return L"MCP-сервер запускается...";
        CloseHandle(serverProcess);serverProcess=nullptr;
        if(code!=0)return L"MCP-сервер завершился с ошибкой "+std::to_wstring(code)+L". Проверьте журнал сервера.";
    }
    std::wstring result = L"MCP-сервер остановлен\r\n";
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return result + L"Не удалось проверить процесс игры.";
    PROCESSENTRY32W entry{}; entry.dwSize = sizeof(entry);
    DWORD pid = 0;
    if (Process32FirstW(snap, &entry)) do {
        if (_wcsicmp(entry.szExeFile, L"h3hota HD.exe") == 0) { pid = entry.th32ProcessID; break; }
    } while (Process32NextW(snap, &entry));
    CloseHandle(snap);
    result += pid ? L"HotA запущена, PID " + std::to_wstring(pid) : L"HotA не запущена";
    result += L"\r\n\r\nНажмите «Запустить MCP» для подключения агентов.";
    return result;
}

void Refresh() { SetDlgItemTextW(page, kStatus, GameStatus().c_str()); }

HWND Control(const wchar_t* cls, const wchar_t* label, DWORD style,
             int x, int y, int w, int h, int id) {
    HWND control = CreateWindowExW(0, cls, label, WS_CHILD | WS_VISIBLE | style,
        x, y, w, h, page, reinterpret_cast<HMENU>(id), module, nullptr);
    SendMessageW(control, WM_SETFONT, SendMessageW(root, WM_GETFONT, 0, 0), TRUE);
    return control;
}

LRESULT CALLBACK PageProc(HWND hwnd, UINT message, WPARAM wp, LPARAM lp) {
    if (message == WM_COMMAND && LOWORD(wp) == kAutostart) {
        bool enabled=IsDlgButtonChecked(page,kAutostart)==BST_CHECKED;
        if(!WritePrivateProfileStringW(L"MCP",L"Autostart",enabled?L"1":L"0",settingsPath.c_str()))
            SetDlgItemTextW(page,kStatus,L"Не удалось сохранить настройку автозапуска.");
        return 0;
    }
    if (message == WM_COMMAND && LOWORD(wp) == kStart) { StartServer(); return 0; }
    if (message == WM_COMMAND && LOWORD(wp) == kStop) { ServerCommand("stop"); return 0; }
    if (message == WM_COMMAND && LOWORD(wp) == kRefresh) { Refresh(); return 0; }
    if (message == WM_TIMER) { Refresh(); return 0; }
    return DefWindowProcW(hwnd, message, wp, lp);
}

void ShowPage() {
    if (ours) { Refresh(); return; }
    hiddenPages.clear();
    for (HWND child = GetWindow(root, GW_CHILD); child; child = GetWindow(child, GW_HWNDNEXT)) {
        wchar_t cls[64]{}; GetClassNameW(child, cls, 64);
        if (child != page && wcscmp(cls, L"#32770") == 0 && IsWindowVisible(child)) {
            hiddenPages.push_back(child); ShowWindow(child, SW_HIDE);
        }
    }
    RECT area{}; GetClientRect(tabs, &area); TabCtrl_AdjustRect(tabs, FALSE, &area);
    MapWindowPoints(tabs, root, reinterpret_cast<POINT*>(&area), 2);
    SetWindowPos(page, HWND_TOP, area.left, area.top, area.right-area.left,
        area.bottom-area.top, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    ours = true; Refresh();
}

void HidePage() {
    ShowWindow(page, SW_HIDE);
    for (HWND child : hiddenPages) if (IsWindow(child)) ShowWindow(child, SW_SHOWNA);
    hiddenPages.clear(); ours = false;
}

void RemovePage();
LRESULT CALLBACK RootProc(HWND hwnd, UINT message, WPARAM wp, LPARAM lp, UINT_PTR, DWORD_PTR) {
    if (message == kInspect) return page && IsWindow(page) ? tabIndex + 1 : 0;
    if (message == kRemove) { RemovePage(); return 1; }
    if (message == WM_NOTIFY && lp) {
        auto* notify = reinterpret_cast<NMHDR*>(lp);
        if (notify->hwndFrom == tabs) {
            if (notify->code == TCN_SELCHANGING && ours) return FALSE;
            if (notify->code == TCN_SELCHANGE) {
                if (TabCtrl_GetCurSel(tabs) == tabIndex) { ShowPage(); return 0; }
                if (ours) HidePage();
            }
        }
    }
    if (message == WM_NCDESTROY) {
        if(serverProcess){CloseHandle(serverProcess);serverProcess=nullptr;}
        RemovePropW(hwnd, kProperty);
        RemoveWindowSubclass(hwnd, RootProc, kSubclass);
        root = nullptr; page = nullptr;
    }
    return DefSubclassProc(hwnd, message, wp, lp);
}

void RemovePage() {
    if (!root) return;
    if (ours) HidePage();
    if (TabCtrl_GetCurSel(tabs) == tabIndex) {
        TabCtrl_SetCurSel(tabs, 0);
        NMHDR notify{tabs, static_cast<UINT_PTR>(GetDlgCtrlID(tabs)), TCN_SELCHANGE};
        SendMessageW(root, WM_NOTIFY, notify.idFrom, reinterpret_cast<LPARAM>(&notify));
    }
    TabCtrl_DeleteItem(tabs, tabIndex);
    KillTimer(page, 1); DestroyWindow(page); page = nullptr;
    UnregisterClassW(L"HotAMcp.LauncherPage.v4", module);
    if(serverProcess){CloseHandle(serverProcess);serverProcess=nullptr;}
    RemovePropW(root, kProperty);
    RemoveWindowSubclass(root, RootProc, kSubclass);
    root = nullptr;
}

bool Install(HWND hwnd) {
    if (GetPropW(hwnd, kProperty)) return true;
    HWND targetTabs = GetDlgItem(hwnd, 1046);
    wchar_t cls[64]{}; GetClassNameW(targetTabs, cls, 64);
    if (wcscmp(cls, L"SysTabControl32") != 0) return false;
    int count = TabCtrl_GetItemCount(targetTabs);
    if (count < 1 || count > 16) return false;
    root = hwnd; tabs = targetTabs;
    WNDCLASSW wc{}; wc.lpfnWndProc = PageProc; wc.hInstance = module;
    wc.lpszClassName = L"HotAMcp.LauncherPage.v4";
    wc.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_BTNFACE + 1);
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    if (!RegisterClassW(&wc) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) return false;
    page = CreateWindowExW(WS_EX_CONTROLPARENT, wc.lpszClassName, L"MCP", WS_CHILD,
        0, 0, 440, 320, root, nullptr, module, nullptr);
    if (!page) return false;
    Control(L"STATIC", L"HotA MCP", 0, 16, 16, 400, 24, 49000);
    Control(L"STATIC", L"", 0, 16, 52, 400, 150, kStatus);
    Control(L"BUTTON", L"Обновить состояние", WS_TABSTOP | BS_PUSHBUTTON,
        16, 220, 170, 28, kRefresh);
    Control(L"BUTTON", L"Запустить MCP", WS_TABSTOP | BS_PUSHBUTTON,16,258,170,28,kStart);
    Control(L"BUTTON", L"Остановить MCP", WS_TABSTOP | BS_PUSHBUTTON,200,258,170,28,kStop);
    Control(L"BUTTON", L"Запускать MCP вместе с лаунчером", WS_TABSTOP | BS_AUTOCHECKBOX,16,296,390,26,kAutostart);
    wchar_t localAppData[32768]{};
    GetEnvironmentVariableW(L"LOCALAPPDATA",localAppData,32768);
    std::wstring settingsDirectory=std::wstring(localAppData)+L"\\HotaMcp";
    CreateDirectoryW(settingsDirectory.c_str(),nullptr);
    settingsPath=settingsDirectory+L"\\launcher.ini";
    bool autostart=GetPrivateProfileIntW(L"MCP",L"Autostart",1,settingsPath.c_str())!=0;
    CheckDlgButton(page,kAutostart,autostart?BST_CHECKED:BST_UNCHECKED);
    if (!SetWindowSubclass(root, RootProc, kSubclass, 0)) { DestroyWindow(page); return false; }
    TCITEMW item{}; item.mask = TCIF_TEXT; item.pszText = const_cast<wchar_t*>(L"MCP");
    tabIndex = static_cast<int>(SendMessageW(tabs, TCM_INSERTITEMW, count, reinterpret_cast<LPARAM>(&item)));
    if (tabIndex < 0) { RemoveWindowSubclass(root, RootProc, kSubclass); DestroyWindow(page); return false; }
    SetPropW(root, kProperty, page);
    // Window procedures must remain mapped even if the companion process exits.
    HMODULE pinned = nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(&PageProc), &pinned);
    SetTimer(page, 1, 2000, nullptr);
    Refresh();
    if(autostart)StartServer();
    return true;
}
}

extern "C" __declspec(dllexport) LRESULT CALLBACK LauncherHook(int code, WPARAM wp, LPARAM lp) {
    if (code >= 0 && wp == PM_REMOVE) {
        auto* msg = reinterpret_cast<MSG*>(lp);
        if (msg->message == kInit && msg->hwnd) Install(msg->hwnd);
    }
    return CallNextHookEx(nullptr, code, wp, lp);
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) { module = instance; DisableThreadLibraryCalls(instance); }
    return TRUE;
}
