// XBar - GPL-3.0-or-later
// Must match src/XBar.Core/Engine/TapProtocol.cs
#pragma once

namespace xbar::protocol
{
    constexpr int Version = 1;
    constexpr wchar_t TapWindowClass[] = L"XBarTapWindow";
    constexpr wchar_t RestoreMessage[] = L"XBar_TapRestore";

    constexpr int CmdHello = 1;
    constexpr int CmdAppearance = 2;
    constexpr int CmdRestore = 3;
    constexpr int CmdDumpTree = 4;
    constexpr int CmdOpacity = 5;
    constexpr int CmdQueryGeometry = 6;
    constexpr int CmdShutdown = 7;
}
