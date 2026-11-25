// XBar - GPL-3.0-or-later
// Entry points. Loaded twice in different roles:
//  * inside XBar.exe: only XBarInject() is used, to get the DLL into Explorer;
//  * inside explorer.exe: DllMain starts XAML Diagnostics, which calls back into
//    DllGetClassObject -> TapSite -> VisualTreeWatcher (see tapsite.cpp).
// The injection technique follows TranslucentTB's ExplorerTAP (GPL-3.0).
#include "common.h"
#include <xamlOM.h>
#include <shlwapi.h>
#include <algorithm>
#include <atomic>
#include <cstdio>
#include <thread>

#pragma comment(lib, "shlwapi.lib")

namespace xbar
{
    HMODULE g_module = nullptr;
    static std::atomic<bool> s_started{ false };

    std::wstring ModulePath()
    {
        wchar_t buf[MAX_PATH * 2]{};
        GetModuleFileNameW(g_module, buf, ARRAYSIZE(buf));
        return buf;
    }

    std::wstring Tag()
    {
        std::wstring name = PathFindFileNameW(ModulePath().c_str());   // XBar.Tap.<hash>.dll
        constexpr std::wstring_view prefix = L"XBar.Tap.";
        std::wstring hash = L"dev";
        if (name.size() > prefix.size() + 4 && _wcsnicmp(name.c_str(), prefix.data(), prefix.size()) == 0)
            hash = name.substr(prefix.size(), name.size() - prefix.size() - 4);
        return L"XBarTap_" + hash;
    }

    static std::wstring ReadyEventName(std::wstring_view tag) { return L"Local\\" + std::wstring(tag) + L"_ready"; }

    void SignalReady() noexcept
    {
        if (HANDLE ev = OpenEventW(EVENT_MODIFY_STATE, FALSE, ReadyEventName(Tag()).c_str()))
        {
            SetEvent(ev);
            CloseHandle(ev);
        }
    }

    void Log(std::wstring_view msg) noexcept
    {
        try
        {
            wchar_t dir[MAX_PATH]{};
            if (!GetEnvironmentVariableW(L"LOCALAPPDATA", dir, MAX_PATH)) return;
            SYSTEMTIME t; GetLocalTime(&t);
            wchar_t path[MAX_PATH * 2];
            swprintf_s(path, L"%s\\XBar\\logs\\tap-%04d-%02d-%02d.log", dir, t.wYear, t.wMonth, t.wDay);
            wchar_t line[1400];
            swprintf_s(line, L"%02d:%02d:%02d.%03d [%lu] %.*s\r\n", t.wHour, t.wMinute, t.wSecond, t.wMilliseconds,
                GetCurrentThreadId(), static_cast<int>(std::min<size_t>(msg.size(), 1300)), msg.data());
            OutputDebugStringW(line);
            HANDLE f = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
            if (f == INVALID_HANDLE_VALUE) return;
            int n = WideCharToMultiByte(CP_UTF8, 0, line, -1, nullptr, 0, nullptr, nullptr);
            std::string utf8(n > 0 ? n - 1 : 0, '\0');
            if (n > 1) WideCharToMultiByte(CP_UTF8, 0, line, -1, utf8.data(), n, nullptr, nullptr);
            DWORD w;
            WriteFile(f, utf8.data(), static_cast<DWORD>(utf8.size()), &w, nullptr);
            CloseHandle(f);
        }
        catch (...) {}
    }

    std::map<std::wstring, std::wstring> ParseKv(std::wstring_view s)
    {
        std::map<std::wstring, std::wstring> m;
        size_t pos = 0;
        while (pos < s.size())
        {
            size_t end = s.find(L';', pos);
            if (end == std::wstring_view::npos) end = s.size();
            auto part = s.substr(pos, end - pos);
            auto eq = part.find(L'=');
            if (eq != std::wstring_view::npos)
                m.emplace(std::wstring(part.substr(0, eq)), std::wstring(part.substr(eq + 1)));
            pos = end + 1;
        }
        return m;
    }

    double ToDouble(const std::map<std::wstring, std::wstring>& m, const wchar_t* key, double fallback)
    {
        auto it = m.find(key);
        if (it == m.end() || it->second.empty()) return fallback;
        wchar_t* endp = nullptr;
        double v = wcstod(it->second.c_str(), &endp);
        return endp == it->second.c_str() ? fallback : v;
    }

    winrt::Windows::UI::Color ParseColor(const std::wstring& s, winrt::Windows::UI::Color fallback)
    {
        std::wstring h = s;
        if (!h.empty() && h[0] == L'#') h = h.substr(1);
        if (h.size() == 6) h = L"FF" + h;
        if (h.size() != 8) return fallback;
        wchar_t* endp = nullptr;
        unsigned long v = wcstoul(h.c_str(), &endp, 16);
        if (endp != h.c_str() + 8) return fallback;
        return { static_cast<uint8_t>(v >> 24), static_cast<uint8_t>(v >> 16), static_cast<uint8_t>(v >> 8), static_cast<uint8_t>(v) };
    }

    using PFN_IXDE = decltype(&InitializeXamlDiagnosticsEx);

    // Runs on a background thread inside explorer.exe.
    static DWORD WINAPI InstallThread(void*)
    {
        const std::wstring tag = Tag();
        Log(L"Installing XAML diagnostics as " + tag);

        HMODULE wux = LoadLibraryExW(L"Windows.UI.Xaml.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        auto ixde = wux ? reinterpret_cast<PFN_IXDE>(GetProcAddress(wux, "InitializeXamlDiagnosticsEx")) : nullptr;
        if (!ixde)
        {
            Log(L"InitializeXamlDiagnosticsEx not available");
            SignalReady();
            return 1;
        }

        const std::wstring path = ModulePath();
        const DWORD pid = GetCurrentProcessId();
        HRESULT hr = E_FAIL;
        for (int attempt = 1; attempt <= 40 && FAILED(hr); attempt++)
        {
            // XAML diagnostics can only be initialised once per thread; use a fresh one each try.
            std::wstring endpoint = tag + L"_" + std::to_wstring(attempt);
            std::thread([&] { hr = ixde(endpoint.c_str(), pid, nullptr, path.c_str(), CLSID_XBarTapSite, nullptr); }).join();
            if (FAILED(hr))
            {
                Logf(L"InitializeXamlDiagnosticsEx attempt %d failed: 0x%08X", attempt, static_cast<unsigned>(hr));
                Sleep(500);
            }
        }
        if (FAILED(hr)) SignalReady(); // let the injector stop waiting; it will report failure
        return 0;
    }
}

using namespace xbar;

BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = inst;
        DisableThreadLibraryCalls(inst);

        wchar_t exe[MAX_PATH]{};
        GetModuleFileNameW(nullptr, exe, MAX_PATH);
        if (_wcsicmp(PathFindFileNameW(exe), L"explorer.exe") == 0 && !s_started.exchange(true))
        {
            // Pin: the hook that brought us here is removed right after, and XAML keeps calling into us.
            HMODULE pinned;
            GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_PIN | GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
                reinterpret_cast<LPCWSTR>(&DllMain), &pinned);
            if (HANDLE t = CreateThread(nullptr, 0, InstallThread, nullptr, 0, nullptr)) CloseHandle(t);
        }
    }
    return TRUE;
}

extern "C" LRESULT CALLBACK XBarHookProc(int code, WPARAM wParam, LPARAM lParam)
{
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

/// Called from XBar.exe: get this DLL into Explorer's UI thread and wait until the TAP is ready.
extern "C" HRESULT __stdcall XBarInject(HWND taskbar, LPCWSTR tag, DWORD timeoutMs)
{
    DWORD pid = 0;
    const DWORD tid = GetWindowThreadProcessId(taskbar, &pid);
    if (!tid) return HRESULT_FROM_WIN32(ERROR_INVALID_WINDOW_HANDLE);

    HANDLE ready = CreateEventW(nullptr, TRUE, FALSE, ReadyEventName(tag).c_str());
    if (!ready) return HRESULT_FROM_WIN32(GetLastError());
    ResetEvent(ready);

    HHOOK hook = SetWindowsHookExW(WH_CALLWNDPROC, XBarHookProc, g_module, tid);
    if (!hook)
    {
        DWORD e = GetLastError();
        CloseHandle(ready);
        return HRESULT_FROM_WIN32(e);
    }
    // Trigger the hook on Explorer's UI thread without blocking on it.
    SendNotifyMessageW(taskbar, WM_NULL, 0, 0);

    DWORD wait = WaitForSingleObject(ready, timeoutMs);
    UnhookWindowsHookEx(hook);
    CloseHandle(ready);
    return wait == WAIT_OBJECT_0 ? S_OK : HRESULT_FROM_WIN32(WAIT_TIMEOUT);
}

STDAPI DllCanUnloadNow() { return S_FALSE; }
