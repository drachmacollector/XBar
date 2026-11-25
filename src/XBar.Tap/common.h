// XBar - GPL-3.0-or-later
#pragma once
#include "winrt.hpp"
#include <string>
#include <string_view>
#include <map>

namespace xbar
{
    extern HMODULE g_module;

    // {B7C4E3A1-5D2F-4C8E-9A61-3F0D2E7B9C45}
    constexpr GUID CLSID_XBarTapSite = { 0xb7c4e3a1, 0x5d2f, 0x4c8e, { 0x9a, 0x61, 0x3f, 0x0d, 0x2e, 0x7b, 0x9c, 0x45 } };

    void Log(std::wstring_view msg) noexcept;
    template <typename... Args>
    void Logf(const wchar_t* fmt, Args... args) noexcept
    {
        wchar_t buf[1024];
        swprintf_s(buf, fmt, args...);
        Log(buf);
    }

    std::wstring ModulePath();
    /// "XBar.Tap.<hash>.dll" -> "XBarTap_<hash>"
    std::wstring Tag();

    /// Parses "k=v;k=v;" into a map.
    std::map<std::wstring, std::wstring> ParseKv(std::wstring_view s);
    double ToDouble(const std::map<std::wstring, std::wstring>& m, const wchar_t* key, double fallback);
    winrt::Windows::UI::Color ParseColor(const std::wstring& s, winrt::Windows::UI::Color fallback);

    // Called on the XAML thread once the visual tree watcher is running.
    void SignalReady() noexcept;
}
