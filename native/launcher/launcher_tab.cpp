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
HMODULE module = nullptr;
HWND root = nullptr, tabs = nullptr, page = nullptr;
int tabIndex = -1;
bool ours = false;
std::vector<HWND> hiddenPages;

std::wstring GameStatus() {
    std::wstring result = L"MCP-сервер: ещё не подключён\r\n";
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return result + L"Не удалось проверить процесс игры.";
    PROCESSENTRY32W entry{}; entry.dwSize = sizeof(entry);
    DWORD pid = 0;
    if (Process32FirstW(snap, &entry)) do {
        if (_wcsicmp(entry.szExeFile, L"h3hota HD.exe") == 0) { pid = entry.th32ProcessID; break; }
    } while (Process32NextW(snap, &entry));
    CloseHandle(snap);
    result += pid ? L"HotA запущена, PID " + std::to_wstring(pid) : L"HotA не запущена";
    result += L"\r\n\r\nВкладка подключена к существующему HD Launcher.\r\n"
              L"Действия игры и MCP пока не доступны в этой сборке.";
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
    if (message == WM_COMMAND && LOWORD(wp) == kRefresh) { Refresh(); return 0; }
    if (message == WM_TIMER) { Refresh(); return 0; }
    return DefWindowProcW(hwnd, message, wp, lp);
}

void ShowPage() {
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
    wc.lpszClassName = L"HotAMcp.LauncherPage.v1";
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
    if (!SetWindowSubclass(root, RootProc, kSubclass, 0)) { DestroyWindow(page); return false; }
    TCITEMW item{}; item.mask = TCIF_TEXT; item.pszText = const_cast<wchar_t*>(L"MCP");
    tabIndex = static_cast<int>(SendMessageW(tabs, TCM_INSERTITEMW, count, reinterpret_cast<LPARAM>(&item)));
    if (tabIndex < 0) { RemoveWindowSubclass(root, RootProc, kSubclass); DestroyWindow(page); return false; }
    SetPropW(root, kProperty, page);
    SetTimer(page, 1, 2000, nullptr);
    Refresh();
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
