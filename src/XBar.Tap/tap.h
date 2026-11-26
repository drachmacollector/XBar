// XBar - GPL-3.0-or-later
#pragma once
#include "common.h"
#include <ocidl.h>
#include <xamlOM.h>
#include <unordered_set>
#include <vector>

namespace xbar
{
    struct PillSpec { double l, t, r, b, radius; };

    struct Look
    {
        std::wstring accent = L"acrylic";
        winrt::Windows::UI::Color color{ 0x40, 0x10, 0x10, 0x10 };
        double blur = 20;
        bool line = false;
        bool border = true;
        winrt::Windows::UI::Color borderColor{ 0x33, 0xFF, 0xFF, 0xFF };
        double borderW = 1;
        bool glow = false;
        bool shadow = true;
        double shadowOpacity = 0.35;
        double shadowBlur = 12;
        bool gradient = false;
        winrt::Windows::UI::Color gradientColor{ 0x40, 0x30, 0x30, 0x60 };
        double gradientAngle = 90;
        double floatPx = 0;
        double animMs = 180;
        std::vector<PillSpec> pills;
    };

    /// One Explorer taskbar (Shell_TrayWnd / Shell_SecondaryTrayWnd) as seen from XAML.
    struct TaskbarState
    {
        HWND taskbar = nullptr;                 // top-level taskbar window
        HWND island = nullptr;                  // the XAML island hosting TaskbarFrame
        wux::FrameworkElement frame{ nullptr }; // Taskbar.TaskbarFrame
        wux::Shapes::Shape fill{ nullptr };     // BackgroundFill
        wux::Shapes::Shape stroke{ nullptr };   // BackgroundStroke
        wuxm::Brush originalFill{ nullptr };
        wuxm::Brush originalStroke{ nullptr };
        wuxc::Canvas canvas{ nullptr };         // our pills
        bool styled = false;
        double opacity = 1.0;
    };

    /// Receives every visual tree change in Explorer's XAML and owns all styling state.
    /// Everything here runs on Explorer's XAML UI thread.
    struct Tap : winrt::implements<Tap, IVisualTreeServiceCallback2, winrt::non_agile>
    {
        explicit Tap(winrt::com_ptr<IXamlDiagnostics> diag);
        ~Tap();

        // IVisualTreeServiceCallback2
        HRESULT STDMETHODCALLTYPE OnVisualTreeChange(ParentChildRelation relation, VisualElement element, VisualMutationType mutation) override;
        HRESULT STDMETHODCALLTYPE OnElementStateChanged(InstanceHandle, VisualElementState, LPCWSTR) noexcept override { return S_OK; }

        static Tap* Instance;

        LRESULT HandleCommand(int cmd, std::wstring_view payload);
        void RestoreAll() noexcept;
        void OwnerDied() noexcept;

    private:
        template <typename T> T FromHandle(InstanceHandle h)
        {
            wf::IInspectable obj;
            winrt::check_hresult(m_diag->GetIInspectableFromHandle(h, reinterpret_cast<::IInspectable**>(winrt::put_abi(obj))));
            return obj.as<T>();
        }
        InstanceHandle HandleOf(const wf::IInspectable& o);

        void EnsureWindow();
        TaskbarState* FindByTaskbar(HWND taskbar);
        TaskbarState* FindByFrame(const wux::FrameworkElement& frame);
        void Apply(TaskbarState& tb, const Look& look);
        void Restore(TaskbarState& tb) noexcept;
        void SetOpacity(TaskbarState& tb, double value, double ms);
        void ApplyFloat(TaskbarState& tb, double px);
        wuxm::Brush MakeMaterial(TaskbarState& tb, const Look& look);
        void DecorateLiquid(wuxc::Border& pill, double radius, double scale, const Look& look);
        void DumpTree();
        void SetOwner(DWORD pid);

        winrt::com_ptr<IXamlDiagnostics> m_diag;
        std::vector<TaskbarState> m_taskbars;
        std::unordered_set<InstanceHandle> m_sources; // DesktopWindowXamlSource handles not yet matched
        HWND m_window = nullptr;
        HANDLE m_ownerProcess = nullptr;
        HANDLE m_ownerWait = nullptr;
        bool m_dormant = false;
        UINT m_restoreMsg = 0;
    };

    /// IObjectWithSite object that XAML Diagnostics instantiates via DllGetClassObject.
    struct TapSite : winrt::implements<TapSite, IObjectWithSite, winrt::non_agile>
    {
        HRESULT STDMETHODCALLTYPE SetSite(IUnknown* site) override;
        HRESULT STDMETHODCALLTYPE GetSite(REFIID riid, void** ppv) noexcept override;
    private:
        winrt::com_ptr<IUnknown> m_site;
    };
}
