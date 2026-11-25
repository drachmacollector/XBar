// XBar - GPL-3.0-or-later
// Explorer-side styling. Tree watching follows TranslucentTB's ExplorerTAP (GPL-3.0);
// the pill drawing, protocol and restore logic are XBar's own.
#include "tap.h"
#include "protocol.h"
#include <windows.ui.xaml.hosting.desktopwindowxamlsource.h>
#include <chrono>
#include <cmath>
#include <thread>
#include <algorithm>
#include <functional>
#include "effects/XamlBlurBrush.h"

using namespace xbar;
namespace wuc_ = winrt::Windows::UI;

Tap* Tap::Instance = nullptr;

namespace
{
    winrt::com_ptr<Tap> g_tap;
    constexpr UINT WM_OWNER_DIED = WM_APP + 1;

    std::wstring Utf8ToWide(const char* p, int n)
    {
        while (n > 0 && p[n - 1] == 0) n--;
        int w = MultiByteToWideChar(CP_UTF8, 0, p, n, nullptr, 0);
        std::wstring s(w, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, p, n, s.data(), w);
        return s;
    }

    wuxm::SolidColorBrush Solid(wuc_::Color c)
    {
        wuxm::SolidColorBrush b;
        b.Color(c);
        return b;
    }

    LRESULT CALLBACK TapWndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
    {
        Tap* tap = Tap::Instance;
        try
        {
            if (msg == WM_COPYDATA && tap)
            {
                auto cds = reinterpret_cast<const COPYDATASTRUCT*>(lp);
                std::wstring payload = cds->cbData > 0 ? Utf8ToWide(static_cast<const char*>(cds->lpData), static_cast<int>(cds->cbData)) : L"";
                return tap->HandleCommand(static_cast<int>(cds->dwData), payload);
            }
            if (msg == WM_OWNER_DIED && tap)
            {
                tap->OwnerDied();
                return 0;
            }
            static UINT restoreMsg = RegisterWindowMessageW(protocol::RestoreMessage);
            if (msg == restoreMsg && tap)
            {
                tap->RestoreAll();
                return 1;
            }
        }
        catch (...)
        {
            Log(L"Exception in TAP window procedure (swallowed)");
            return 0;
        }
        return DefWindowProcW(hwnd, msg, wp, lp);
    }

    void CALLBACK OwnerWaitCallback(void* ctx, BOOLEAN)
    {
        PostMessageW(static_cast<HWND>(ctx), WM_OWNER_DIED, 0, 0);
    }
}

// ============================================================ site

HRESULT TapSite::SetSite(IUnknown* site)
{
    try
    {
        m_site.copy_from(site);
        if (site && !g_tap)
        {
            auto diag = m_site.as<IXamlDiagnostics>();
            g_tap = winrt::make_self<Tap>(diag);
            // AdviseVisualTreeChange must not run on the UI thread (it can hang); callbacks come back on it.
            std::thread([diag, tap = g_tap]
            {
                HRESULT hr = diag.as<IVisualTreeService3>()->AdviseVisualTreeChange(tap.get());
                Logf(L"AdviseVisualTreeChange: 0x%08X", static_cast<unsigned>(hr));
                SignalReady();
            }).detach();
        }
        return S_OK;
    }
    catch (...)
    {
        SignalReady();
        return winrt::to_hresult();
    }
}

HRESULT TapSite::GetSite(REFIID riid, void** ppv) noexcept
{
    return m_site ? m_site->QueryInterface(riid, ppv) : E_FAIL;
}

struct TapSiteFactory : winrt::implements<TapSiteFactory, IClassFactory>
{
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override
    {
        if (outer) return CLASS_E_NOAGGREGATION;
        try { return winrt::make<TapSite>().as<IUnknown>()->QueryInterface(riid, ppv); }
        catch (...) { return winrt::to_hresult(); }
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL) override { return S_OK; }
};

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, LPVOID* ppv)
{
    if (clsid != CLSID_XBarTapSite) return CLASS_E_CLASSNOTAVAILABLE;
    try
    {
        *ppv = nullptr;
        return winrt::make<TapSiteFactory>().as<IUnknown>()->QueryInterface(riid, ppv);
    }
    catch (...) { return winrt::to_hresult(); }
}

// ============================================================ watcher

Tap::Tap(winrt::com_ptr<IXamlDiagnostics> diag) : m_diag(std::move(diag))
{
    Instance = this;
    Log(L"TAP created");
}

Tap::~Tap()
{
    RestoreAll();
    Instance = nullptr;
}

InstanceHandle Tap::HandleOf(const wf::IInspectable& o)
{
    InstanceHandle h = 0;
    m_diag->GetHandleFromIInspectable(static_cast<::IInspectable*>(winrt::get_abi(o)), &h);
    return h;
}

static wux::FrameworkElement FindParentNamed(wux::DependencyObject el, std::wstring_view name)
{
    for (auto p = wuxm::VisualTreeHelper::GetParent(el); p; p = wuxm::VisualTreeHelper::GetParent(p))
        if (auto fe = p.try_as<wux::FrameworkElement>(); fe && fe.Name() == name) return fe;
    return nullptr;
}

HRESULT Tap::OnVisualTreeChange(ParentChildRelation relation, VisualElement element, VisualMutationType mutation)
{
    // We own these strings.
    auto freeStr = [](BSTR s) { if (s) SysFreeString(s); };
    std::wstring type = element.Type ? std::wstring(element.Type, SysStringLen(element.Type)) : L"";
    std::wstring name = element.Name ? std::wstring(element.Name, SysStringLen(element.Name)) : L"";
    freeStr(element.Type); freeStr(element.Name); freeStr(element.SrcInfo.FileName); freeStr(element.SrcInfo.Hash);

    try
    {
        if (mutation == Remove)
        {
            m_sources.erase(element.Handle);
            for (auto it = m_taskbars.begin(); it != m_taskbars.end(); ++it)
            {
                if (it->frame && HandleOf(it->frame) == element.Handle)
                {
                    Log(L"Taskbar frame removed");
                    m_taskbars.erase(it);
                    break;
                }
            }
            return S_OK;
        }

        if (type == winrt::name_of<wuxh::DesktopWindowXamlSource>())
        {
            m_sources.insert(element.Handle);
        }
        else if (type == L"Taskbar.TaskbarFrame")
        {
            auto frame = FromHandle<wux::FrameworkElement>(element.Handle);
            auto root = frame.XamlRoot() ? frame.XamlRoot().Content() : nullptr;
            for (auto it = m_sources.begin(); it != m_sources.end(); ++it)
            {
                wux::UIElement content{ nullptr };
                wuxh::DesktopWindowXamlSource src{ nullptr };
                try { src = FromHandle<wuxh::DesktopWindowXamlSource>(*it); content = src.Content(); }
                catch (...) { continue; }
                if (!root || content != root) continue;

                HWND island = nullptr;
                src.as<IDesktopWindowXamlSourceNative>()->get_WindowHandle(&island);
                TaskbarState tb;
                tb.island = island;
                tb.taskbar = GetAncestor(island, GA_ROOT);
                tb.frame = frame;
                m_taskbars.push_back(std::move(tb));
                m_sources.erase(it);
                Logf(L"Taskbar frame registered: taskbar=%p island=%p", tb.taskbar, island);
                EnsureWindow();
                break;
            }
        }
        else if (type == winrt::name_of<wux::Shapes::Rectangle>() && (name == L"BackgroundFill" || name == L"BackgroundStroke"))
        {
            auto shape = FromHandle<wux::Shapes::Shape>(element.Handle);
            if (auto frame = FindParentNamed(shape, L"TaskbarFrame"))
            {
                if (auto tb = FindByFrame(frame))
                {
                    if (name == L"BackgroundFill") { tb->fill = shape; tb->originalFill = shape.Fill(); }
                    else { tb->stroke = shape; tb->originalStroke = shape.Fill(); }
                    Logf(L"Registered %s", name.c_str());
                }
            }
        }
    }
    catch (...)
    {
        Logf(L"OnVisualTreeChange error for %s (0x%08X)", type.c_str(), static_cast<unsigned>(winrt::to_hresult()));
    }
    return S_OK;
}

TaskbarState* Tap::FindByTaskbar(HWND taskbar)
{
    for (auto& t : m_taskbars) if (t.taskbar == taskbar) return &t;
    return nullptr;
}

TaskbarState* Tap::FindByFrame(const wux::FrameworkElement& frame)
{
    for (auto& t : m_taskbars) if (t.frame == frame) return &t;
    return nullptr;
}

void Tap::EnsureWindow()
{
    if (m_window) return;
    WNDCLASSEXW wc{ sizeof(wc) };
    wc.lpfnWndProc = TapWndProc;
    wc.hInstance = g_module;
    wc.lpszClassName = protocol::TapWindowClass;
    RegisterClassExW(&wc); // may already exist from an older dormant TAP with the same class: fine
    m_window = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, protocol::TapWindowClass, Tag().c_str(), WS_POPUP,
        0, 0, 0, 0, nullptr, nullptr, g_module, nullptr);
    Logf(L"Control window %p created on thread %lu", m_window, GetCurrentThreadId());
}

// ============================================================ commands

LRESULT Tap::HandleCommand(int cmd, std::wstring_view payload)
{
    auto kv = ParseKv(payload);
    switch (cmd)
    {
    case protocol::CmdHello:
        m_dormant = false;
        SetOwner(static_cast<DWORD>(ToDouble(kv, L"pid", 0)));
        Logf(L"Hello from pid %s (taskbars known: %zu)", kv[L"pid"].c_str(), m_taskbars.size());
        return protocol::Version;

    case protocol::CmdAppearance:
    {
        if (m_dormant) return 0;
        HWND hwnd = reinterpret_cast<HWND>(static_cast<intptr_t>(_wtoi64(kv[L"hwnd"].c_str())));
        auto tb = FindByTaskbar(hwnd);
        if (!tb) { Logf(L"Appearance for unknown taskbar %p", hwnd); return 0; }

        Look look;
        look.accent = kv[L"accent"];
        look.color = ParseColor(kv[L"color"], look.color);
        look.blur = ToDouble(kv, L"blur", look.blur);
        look.line = ToDouble(kv, L"line", 0) != 0;
        look.border = ToDouble(kv, L"border", 1) != 0;
        look.borderColor = ParseColor(kv[L"bordercolor"], look.borderColor);
        look.borderW = ToDouble(kv, L"borderw", 1);
        look.glow = ToDouble(kv, L"glow", 0) != 0;
        look.shadow = ToDouble(kv, L"shadow", 1) != 0;
        look.shadowOpacity = ToDouble(kv, L"shadowop", 0.35);
        look.shadowBlur = ToDouble(kv, L"shadowblur", 12);
        look.gradient = ToDouble(kv, L"grad", 0) != 0;
        look.gradientColor = ParseColor(kv[L"gradcolor"], look.gradientColor);
        look.gradientAngle = ToDouble(kv, L"gradangle", 90);
        look.floatPx = ToDouble(kv, L"float", 0);
        look.animMs = ToDouble(kv, L"anim", 180);
        for (auto& part : [&] {
                 std::vector<std::wstring> v; std::wstring s = kv[L"pills"]; size_t p = 0;
                 while (p <= s.size() && !s.empty()) { size_t e = s.find(L'|', p); if (e == std::wstring::npos) e = s.size(); v.push_back(s.substr(p, e - p)); p = e + 1; }
                 return v; }())
        {
            PillSpec ps{};
            if (swscanf_s(part.c_str(), L"%lf,%lf,%lf,%lf,%lf", &ps.l, &ps.t, &ps.r, &ps.b, &ps.radius) == 5)
                look.pills.push_back(ps);
        }
        Apply(*tb, look);
        return 1;
    }

    case protocol::CmdOpacity:
    {
        if (m_dormant) return 0;
        HWND hwnd = reinterpret_cast<HWND>(static_cast<intptr_t>(_wtoi64(kv[L"hwnd"].c_str())));
        if (auto tb = FindByTaskbar(hwnd)) SetOpacity(*tb, ToDouble(kv, L"opacity", 1), ToDouble(kv, L"ms", 150));
        return 1;
    }

    case protocol::CmdRestore:
        RestoreAll();
        return 1;

    case protocol::CmdDumpTree:
        DumpTree();
        return 1;

    case protocol::CmdShutdown:
        Log(L"Going dormant");
        RestoreAll();
        m_dormant = true;
        SetOwner(0);
        return 1;
    }
    return 0;
}

void Tap::SetOwner(DWORD pid)
{
    if (m_ownerWait) { UnregisterWaitEx(m_ownerWait, nullptr); m_ownerWait = nullptr; }
    if (m_ownerProcess) { CloseHandle(m_ownerProcess); m_ownerProcess = nullptr; }
    if (!pid) return;
    m_ownerProcess = OpenProcess(SYNCHRONIZE, FALSE, pid);
    if (m_ownerProcess)
        RegisterWaitForSingleObject(&m_ownerWait, m_ownerProcess, OwnerWaitCallback, m_window, INFINITE, WT_EXECUTEONLYONCE);
}

void Tap::OwnerDied() noexcept
{
    Log(L"XBar process ended: restoring the taskbar");
    RestoreAll();
    for (auto& t : m_taskbars) if (t.taskbar) SetWindowRgn(t.taskbar, nullptr, TRUE);
    SetOwner(0);
}

// ============================================================ painting

wuxm::Brush Tap::MakeMaterial(TaskbarState& tb, const Look& look)
{
    const auto c = look.color;
    if (look.accent == L"normal")
        return tb.originalFill ? tb.originalFill : wuxm::Brush(Solid({ 0xF0, 0x20, 0x20, 0x20 }));
    if (look.accent == L"opaque")
        return Solid({ 0xFF, c.R, c.G, c.B });
    if (look.accent == L"clear")
        return Solid(c);
    if (look.accent == L"blur")
    {
        auto compositor = wuxh::ElementCompositionPreview::GetElementVisual(tb.frame).Compositor();
        const wfn::float4 tint{ c.R / 255.f, c.G / 255.f, c.B / 255.f, c.A / 255.f };
        return winrt::make<XamlBlurBrush>(compositor, static_cast<float>(std::max(0.0, look.blur / 3.0)), tint);
    }
    // acrylic: Backdrop source samples what is behind the transparent taskbar window.
    wuxm::AcrylicBrush acrylic;
    acrylic.BackgroundSource(wuxm::AcrylicBackgroundSource::Backdrop);
    acrylic.TintColor({ 0xFF, c.R, c.G, c.B });
    acrylic.TintOpacity(c.A / 255.0);
    acrylic.FallbackColor(c);
    return acrylic;
}

void Tap::Apply(TaskbarState& tb, const Look& look)
{
    if (!tb.fill || !tb.frame) { Log(L"Apply: taskbar background not registered yet"); return; }

    if (!tb.styled)
    {
        tb.originalFill = tb.fill.Fill();
        if (tb.stroke) tb.originalStroke = tb.stroke.Fill();
    }
    tb.styled = true;

    // Hide the stock full-width background: we draw our own pills.
    tb.fill.Fill(Solid({ 0, 0, 0, 0 }));
    if (tb.stroke) tb.stroke.Fill(look.line ? tb.originalStroke : wuxm::Brush(Solid({ 0, 0, 0, 0 })));

    auto panel = wuxm::VisualTreeHelper::GetParent(tb.fill).try_as<wuxc::Panel>();
    if (!panel)
    {
        Log(L"BackgroundFill has no Panel parent; painting the full background instead");
        tb.fill.Fill(MakeMaterial(tb, look));
        return;
    }

    if (!tb.canvas)
    {
        tb.canvas = wuxc::Canvas();
        tb.canvas.IsHitTestVisible(false);
        uint32_t idx = 0;
        panel.Children().IndexOf(tb.fill, idx);
        panel.Children().InsertAt(idx + 1, tb.canvas);
        // Span the whole panel (Grid rows/columns)
        wuxc::Grid::SetRowSpan(tb.canvas, 100);
        wuxc::Grid::SetColumnSpan(tb.canvas, 100);
    }
    auto children = tb.canvas.Children();
    children.Clear();

    // Map window pixels -> canvas DIPs.
    const double scale = tb.frame.XamlRoot() ? tb.frame.XamlRoot().RasterizationScale() : (GetDpiForWindow(tb.taskbar) / 96.0);
    RECT tbr{}, isr{};
    GetWindowRect(tb.taskbar, &tbr);
    GetWindowRect(tb.island, &isr);
    const double islandX = isr.left - tbr.left, islandY = isr.top - tbr.top;
    wf::Point origin{ 0, 0 };
    try { origin = tb.canvas.TransformToVisual(nullptr).TransformPoint({ 0, 0 }); } catch (...) {}

    const auto bc = look.borderColor;
    for (const auto& p : look.pills)
    {
        const double x = (p.l - islandX) / scale - origin.X;
        const double y = (p.t - islandY) / scale - origin.Y;
        const double w = (p.r - p.l) / scale, h = (p.b - p.t) / scale;
        const double r = p.radius / scale;
        if (w <= 0 || h <= 0) continue;

        // Soft shadow / glow: concentric strokes fading outwards (never fills the inside,
        // so translucent pills stay see-through).
        auto rings = [&](wuc_::Color color, double strength, double extent, double dy)
        {
            const int n = std::clamp(static_cast<int>(extent / scale / 1.5), 1, 3); // region pad is 3 px
            for (int i = n; i >= 1; i--)
            {
                const double f = 1.0 - (i - 1.0) / n;
                wuxc::Border ring;
                ring.Width(w + 2.0 * i);
                ring.Height(h + 2.0 * i);
                ring.CornerRadius(wux::CornerRadiusHelper::FromUniformRadius(r + i));
                ring.BorderThickness(wux::ThicknessHelper::FromUniformLength(1));
                ring.BorderBrush(Solid({ static_cast<uint8_t>(std::clamp(strength * 255.0 * f * f, 0.0, 255.0)), color.R, color.G, color.B }));
                wuxc::Canvas::SetLeft(ring, x - i);
                wuxc::Canvas::SetTop(ring, y - i + dy);
                children.Append(ring);
            }
        };
        if (look.shadow) rings({ 0xFF, 0, 0, 0 }, look.shadowOpacity * 0.45, look.shadowBlur, 1.0);
        if (look.glow) rings(bc, 0.55, std::max(8.0, look.shadowBlur), 0.0);

        wuxc::Border pill;
        pill.Width(w);
        pill.Height(h);
        pill.CornerRadius(wux::CornerRadiusHelper::FromUniformRadius(r));
        pill.Background(MakeMaterial(tb, look));
        if (look.border)
        {
            pill.BorderThickness(wux::ThicknessHelper::FromUniformLength(std::max(0.5, look.borderW / scale)));
            pill.BorderBrush(Solid(bc));
        }
        if (look.gradient)
        {
            const double a = look.gradientAngle * 3.14159265358979 / 180.0;
            wuxm::LinearGradientBrush g;
            g.StartPoint({ static_cast<float>(0.5 - std::cos(a) / 2), static_cast<float>(0.5 - std::sin(a) / 2) });
            g.EndPoint({ static_cast<float>(0.5 + std::cos(a) / 2), static_cast<float>(0.5 + std::sin(a) / 2) });
            wuxm::GradientStop s0; s0.Color({ 0, look.gradientColor.R, look.gradientColor.G, look.gradientColor.B }); s0.Offset(0);
            wuxm::GradientStop s1; s1.Color(look.gradientColor); s1.Offset(1);
            g.GradientStops().Append(s0);
            g.GradientStops().Append(s1);
            wuxc::Border overlay;
            overlay.CornerRadius(wux::CornerRadiusHelper::FromUniformRadius(r));
            overlay.Background(g);
            pill.Child(overlay);
        }
        wuxc::Canvas::SetLeft(pill, x);
        wuxc::Canvas::SetTop(pill, y);
        children.Append(pill);
    }

    ApplyFloat(tb, look.floatPx / scale);
}

void Tap::ApplyFloat(TaskbarState& tb, double dips)
{
    if (!tb.frame.XamlRoot()) return;
    auto root = tb.frame.XamlRoot().Content();
    if (!root) return;
    if (dips <= 0.01) { root.RenderTransform(nullptr); return; }
    wuxm::TranslateTransform t;
    t.Y(-dips);
    root.RenderTransform(t);
}

void Tap::SetOpacity(TaskbarState& tb, double value, double ms)
{
    if (!tb.frame || !tb.frame.XamlRoot()) return;
    auto root = tb.frame.XamlRoot().Content();
    if (!root) return;
    tb.opacity = value;
    if (ms <= 1) { root.Opacity(value); return; }

    wuxm::Animation::DoubleAnimation anim;
    anim.To(value);
    anim.Duration(wux::DurationHelper::FromTimeSpan(std::chrono::milliseconds(static_cast<int>(ms))));
    wuxm::Animation::Storyboard sb;
    sb.Children().Append(anim);
    wuxm::Animation::Storyboard::SetTarget(anim, root);
    wuxm::Animation::Storyboard::SetTargetProperty(anim, L"Opacity");
    sb.Begin();
}

void Tap::Restore(TaskbarState& tb) noexcept
{
    try
    {
        if (tb.canvas)
        {
            if (auto panel = wuxm::VisualTreeHelper::GetParent(tb.canvas).try_as<wuxc::Panel>())
            {
                uint32_t idx = 0;
                if (panel.Children().IndexOf(tb.canvas, idx)) panel.Children().RemoveAt(idx);
            }
            tb.canvas = nullptr;
        }
        if (tb.styled)
        {
            if (tb.fill && tb.originalFill) tb.fill.Fill(tb.originalFill);
            if (tb.stroke && tb.originalStroke) tb.stroke.Fill(tb.originalStroke);
        }
        if (tb.frame && tb.frame.XamlRoot())
            if (auto root = tb.frame.XamlRoot().Content())
            {
                root.Opacity(1.0);
                root.RenderTransform(nullptr);
            }
        tb.styled = false;
        tb.opacity = 1.0;
    }
    catch (...) { Log(L"Restore: exception (ignored)"); }
}

void Tap::RestoreAll() noexcept
{
    for (auto& t : m_taskbars) Restore(t);
}

void Tap::DumpTree()
{
    int lines = 0;
    std::function<void(wux::DependencyObject, int)> walk = [&](wux::DependencyObject o, int depth)
    {
        if (!o || depth > 16 || lines > 800) return;
        std::wstring indent(depth * 2, L' ');
        std::wstring nm;
        double w = 0, h = 0;
        wf::Point pos{};
        if (auto fe = o.try_as<wux::FrameworkElement>())
        {
            nm = std::wstring(fe.Name());
            w = fe.ActualWidth(); h = fe.ActualHeight();
            try { pos = fe.TransformToVisual(nullptr).TransformPoint({ 0, 0 }); } catch (...) {}
        }
        Logf(L"%s%s '%s' at %.0f,%.0f size %.0fx%.0f", indent.c_str(), std::wstring(winrt::get_class_name(o)).c_str(), nm.c_str(), pos.X, pos.Y, w, h);
        lines++;
        int n = wuxm::VisualTreeHelper::GetChildrenCount(o);
        for (int i = 0; i < n; i++) walk(wuxm::VisualTreeHelper::GetChild(o, i), depth + 1);
    };
    for (auto& t : m_taskbars)
    {
        Logf(L"=== tree for taskbar %p (island %p)", t.taskbar, t.island);
        if (t.frame && t.frame.XamlRoot()) walk(t.frame.XamlRoot().Content(), 0);
    }
}
