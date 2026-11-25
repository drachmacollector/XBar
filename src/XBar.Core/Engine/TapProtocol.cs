// XBar - GPL-3.0-or-later
namespace XBar.Core.Engine;

/// <summary>
/// Wire contract between XBar.exe and XBar.Tap.dll (inside explorer.exe).
/// Must match src/XBar.Tap/protocol.h.
/// Commands are sent with WM_COPYDATA: dwData = command id, payload = UTF-8 "key=value;key=value".
/// </summary>
public static class TapProtocol
{
    public const int Version = 1;
    public const string TapWindowClass = "XBarTapWindow";
    public const string AppWindowTitle = "XBarAppWindow";
    public const string RestoreMessage = "XBar_TapRestore";

    public const int CmdHello = 1;      // pid=<owner pid>;version=<n>  -> tap replies CmdHelloReply
    public const int CmdAppearance = 2; // full appearance spec
    public const int CmdRestore = 3;    // restore stock look
    public const int CmdDumpTree = 4;   // write visual tree to log
    public const int CmdOpacity = 5;    // opacity=<0..1>;ms=<duration>
    public const int CmdQueryGeometry = 6;

    // Tap -> app
    public const int EvtHelloReply = 101;  // version=<n>
    public const int EvtGeometry = 102;    // apps=l,t,r,b;tray=l,t,r,b;hwnd=<taskbar>
    public const int EvtLog = 103;         // free text
}
