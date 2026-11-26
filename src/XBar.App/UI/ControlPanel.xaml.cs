// XBar - GPL-3.0-or-later
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using XBar.Core;
using XBar.Core.Config;
using XBar.Core.Engine;
using XBar.Core.Native;

namespace XBar.App.UI;

public partial class ControlPanel : Window
{
    readonly AppHost _host;
    readonly DispatcherTimer _editThrottle = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(33) };
    bool _dirty, _previewQueued;
    readonly DispatcherTimer _diagTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    bool _loading;

    Settings S => _host.Store.Current;

    public ControlPanel(AppHost host)
    {
        _host = host;
        InitializeComponent();

        // Navigation built from the pages' Tag ("Title|glyph")
        foreach (FrameworkElement page in Pages.Children)
        {
            var parts = ((string)page.Tag).Split('|');
            var item = new ListBoxItem
            {
                Tag = page,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock { Text = parts[1], FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 16, Width = 28, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = parts[0], VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            Nav.Items.Add(item);
        }
        Nav.SelectedIndex = 0;

        // Any edit anywhere -> apply live (debounced)
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnEdited));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(OnEdited));
        AddHandler(RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>((s, e) => OnEdited(s, e)));
        AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler(OnSelection));
        AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnEdited));
        AddHandler(ColorField.ColorChangedEvent, new RoutedEventHandler(OnEdited));
        _editThrottle.Tick += (_, _) =>
        {
            if (_dirty) { _dirty = false; Commit(); }
            else _editThrottle.Stop();   // nothing new since the last tick: the drag is over
        };

        Scroller.PreviewMouseWheel += Scroller_PreviewMouseWheel;
        _diagTimer.Tick += (_, _) => RefreshDiagnostics();
        _host.StatusChanged += RefreshStatus;
        if (_host.Tap is not null) _host.Tap.Changed += RefreshStatus;
        Closed += (_, _) =>
        {
            _host.StatusChanged -= RefreshStatus;
            if (_host.Tap is not null) _host.Tap.Changed -= RefreshStatus;
            _diagTimer.Stop();
        };

        foreach (var preview in new[] { HomePreview, ShapePreview, StylePreview })
            preview.IsVisibleChanged += (_, _) => RefreshPreviews();   // draw when its page is shown

        VersionText.Text = $"XBar {typeof(App).Assembly.GetName().Version?.ToString(3)}";
        BuildPresets();
        Reload();
    }

    /// <summary>Rebind everything to the current settings object.</summary>
    public void Reload()
    {
        _loading = true;
        try
        {
            DataContext = null;
            DataContext = S;
            RulesList.ItemsSource = null;
            RulesList.ItemsSource = S.Rules;
            NoRules.Visibility = S.Rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ProfilesList.ItemsSource = null;
            ProfilesList.ItemsSource = S.Profiles;
            RunAtLogin.IsChecked = Shortcuts.IsRunAtLogin();
            RefreshRunningApps();
            RefreshPreviews();
            RefreshStatus();
        }
        finally
        {
            Dispatcher.BeginInvoke(() => _loading = false, DispatcherPriority.Background);
        }
    }

    // ------------------------------------------------------------------ edits

    void OnSelection(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource == Nav || e.OriginalSource == RunningApps) return;
        OnEdited(sender, e);
    }

    void OnEdited(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        // ScrollBars are RangeBases too: scrolling the page must not re-apply the settings.
        if (e.OriginalSource is ScrollBar) return;
        if (e.OriginalSource == RunAtLogin || e.OriginalSource == PreviewMaximized || e.OriginalSource == ProfileName) return;
        if (e.OriginalSource is TextBox tb && (tb.IsReadOnly && tb is not HotkeyBox)) return;
        QueuePreviews();
        // Throttle, not debounce: apply while the slider is still moving (<= ~30 times a second)
        // so the taskbar follows the thumb instead of jumping once the drag pauses.
        _dirty = true;
        if (!_editThrottle.IsEnabled) _editThrottle.Start();
    }

    void Commit()
    {
        // One height / roundness for both pills (the page only edits the apps pill).
        S.Shape.Tray.Height = S.Shape.Apps.Height;
        S.Shape.Tray.CornerRadius = S.Shape.Apps.CornerRadius;
        _host.SettingsEdited();
        HotkeyWarning.Text = _host.Engine?.FailedHotkeys.Count > 0
            ? $"Could not register: {string.Join(", ", _host.Engine.FailedHotkeys)} (in use by another app)."
            : "";
    }

    /// <summary>Redraw the preview once per render pass however many edits came in.</summary>
    void QueuePreviews()
    {
        if (_previewQueued) return;
        _previewQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () => { _previewQueued = false; RefreshPreviews(); });
    }

    void RefreshPreviews()
    {
        // Only the page being looked at needs redrawing; the others redraw when they are shown.
        foreach (var (preview, maximised) in new (TaskbarPreview, bool)[] { (HomePreview, false), (ShapePreview, false), (StylePreview, PreviewMaximized.IsChecked == true) })
            if (preview.IsVisible) preview.Show(S, maximised);
    }

    void PreviewToggle(object sender, RoutedEventArgs e) => RefreshPreviews();

    // ------------------------------------------------------------------ navigation & status

    void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not ListBoxItem { Tag: FrameworkElement page }) return;
        foreach (FrameworkElement p in Pages.Children) p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
        Scroller.ScrollToTop();
        RefreshPreviews();
        Diag_Toggled(null, null);
        if (page == PageRules) RefreshRunningApps();
    }

    void Diag_Toggled(object? sender, RoutedEventArgs? e)
    {
        if (PageAdvanced.Visibility == Visibility.Visible && DiagExpander.IsExpanded) { RefreshDiagnostics(); _diagTimer.Start(); }
        else _diagTimer.Stop();
    }

    /// <summary>Smooth, predictable wheel / two-finger scrolling: a fixed pixel step per wheel notch instead of
    /// WPF's multi-line jump (precision touchpads send many small deltas and overshoot otherwise).</summary>
    void Scroller_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d is not null && d != Scroller;
             d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is ComboBox { IsDropDownOpen: true } || d is TextBox { VerticalScrollBarVisibility: not ScrollBarVisibility.Disabled }) return;   // they scroll themselves
        Scroller.ScrollToVerticalOffset(Math.Clamp(Scroller.VerticalOffset - e.Delta * 0.35, 0, Scroller.ScrollableHeight));
        e.Handled = true;
    }

    void RefreshStatus()
    {
        var eng = _host.Engine;
        if (eng is null) return;
        bool paused = eng.Paused;
        NavStatus.Text = paused ? "Paused" : S.Overlay.Enabled ? "Overlay active" : "Reserve mode";
        PauseButton.Content = paused ? "Resume" : "Pause";
        HomePauseButton.Content = paused ? "Resume" : "Pause";
        StatusTitle.Text = paused ? "XBar is paused" : "XBar is active";
        var tap = _host.Tap;
        StatusDetail.Text = paused
            ? $"Taskbar is back to normal ({eng.PauseReason})."
            : $"{(S.Overlay.Enabled ? "Overlay: windows use the full screen." : "Reserve mode: classic strip.")}  Styling: {tap?.Status ?? "n/a"}";
        ConnectTapButton.Visibility = tap is { IsAvailable: false } && !paused ? Visibility.Visible : Visibility.Collapsed;

        var warnings = new List<string>();
        if (eng.AutoHideConflict) warnings.Add("Taskbar auto-hide is on. Turn it off in Settings > Personalization > Taskbar so the bar stays visible.");
        if (eng.FailedHotkeys.Count > 0) warnings.Add($"Hotkeys in use by another app: {string.Join(", ", eng.FailedHotkeys)}.");
        StatusWarning.Text = string.Join("\n", warnings);
        StatusWarning.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void RefreshDiagnostics()
    {
        if (_host.Engine is null) return;
        DiagText.Text = _host.Engine.Diagnostics();
        LogText.Text = string.Join(Environment.NewLine, Log.RecentLines.TakeLast(200));
        LogText.ScrollToEnd();
    }

    // ------------------------------------------------------------------ buttons

    void Pause_Click(object sender, RoutedEventArgs e) => _host.Engine?.TogglePause();
    void Panic_Click(object sender, RoutedEventArgs e) => _host.Engine?.Panic();
    void OpenLogs_Click(object sender, RoutedEventArgs e) => Log.OpenFolder();
    void CopyDiag_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(DiagText.Text + Environment.NewLine + LogText.Text);
    void DumpTree_Click(object sender, RoutedEventArgs e) { _host.Tap?.DumpTree(); Log.Info("Requested visual tree dump (see explorer log lines tagged [tap])"); }

    async void ConnectTap_Click(object sender, RoutedEventArgs e)
    {
        if (_host.Tap is null) return;
        if (await _host.Tap.ConnectAsync()) _host.Engine?.Rebuild("tap connected");
        RefreshStatus();
    }

    void RunAtLogin_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Shortcuts.SetRunAtLogin(RunAtLogin.IsChecked == true);
        S.RunAtLogin = RunAtLogin.IsChecked == true;
    }

    void RestartExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Restart Explorer now? Open File Explorer windows will close.", "XBar",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        _host.Engine?.Pause("restarting Explorer");
        foreach (var p in Process.GetProcessesByName("explorer")) { try { p.Kill(); } catch { } }
        Thread.Sleep(800);
        if (Process.GetProcessesByName("explorer").Length == 0) Process.Start("explorer.exe");
    }

    // ---- rules
    void RefreshRunningApps()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        Win32.EnumWindows((h, _) =>
        {
            if (Win32.IsWindowVisible(h) && (Win32.Style(h) & Win32.WS_CAPTION) == Win32.WS_CAPTION)
            {
                var n = Win32.ProcessNameOfWindow(h);
                if (!string.IsNullOrEmpty(n)) names.Add(n);
            }
            return true;
        }, IntPtr.Zero);
        RunningApps.ItemsSource = names.ToList();
    }

    void AddRule_Click(object sender, RoutedEventArgs e) => AddRule("");

    void AddRuleForApp_Click(object sender, RoutedEventArgs e)
    {
        if (RunningApps.SelectedItem is string n) AddRule(n);
    }

    void AddRule(string pattern)
    {
        S.Rules.Add(new AppRule { Pattern = pattern, Appearance = S.Style.Desktop.Clone() });
        Reload();
        Commit();
    }

    void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is AppRule r) { S.Rules.Remove(r); Reload(); Commit(); }
    }

    // ---- profiles
    Profile Snapshot(string name)
    {
        var c = SettingsStore.Clone(S);
        return new Profile { Name = name, Shape = c.Shape, Style = c.Style, Behavior = c.Behavior };
    }

    void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(ProfileName.Text) ? $"Profile {S.Profiles.Count + 1}" : ProfileName.Text.Trim();
        S.Profiles.RemoveAll(p => p.Name == name);
        S.Profiles.Add(Snapshot(name));
        S.ActiveProfile = name;
        Reload();
        Commit();
    }

    void ApplyProfile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Profile p) _host.ApplyProfile(p);
    }

    void UpdateProfile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Profile p) return;
        int i = S.Profiles.IndexOf(p);
        S.Profiles[i] = Snapshot(p.Name);
        Reload();
        Commit();
    }

    void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Profile p) { S.Profiles.Remove(p); Reload(); Commit(); }
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = "xbar-settings.json", Filter = "JSON|*.json" };
        if (dlg.ShowDialog(this) == true) File.WriteAllText(dlg.FileName, SettingsStore.Export(S));
    }

    void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON|*.json" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            _host.ReplaceSettings(SettingsStore.Import(File.ReadAllText(dlg.FileName)));
            Reload();
        }
        catch (Exception ex) { MessageBox.Show(this, $"Could not import: {ex.Message}", "XBar"); }
    }

    void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Reset all settings to defaults? Profiles and rules are removed.", "XBar", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        _host.ReplaceSettings(new Settings());
        Reload();
    }

    // ---- presets
    void BuildPresets()
    {
        foreach (var preset in Presets.All)
        {
            var preview = new Border
            {
                Width = 150, Height = 46, CornerRadius = new CornerRadius(6), ClipToBounds = true,
                Background = new System.Windows.Media.LinearGradientBrush(
                    System.Windows.Media.Color.FromRgb(0x1d, 0x2b, 0x64), System.Windows.Media.Color.FromRgb(0xf0, 0x80, 0x5a), 20),
            };
            var (a, r, g, b) = ColorText.Parse(preset.Look.Color, (0x80, 0, 0, 0));
            var (ba, br, bg, bb) = ColorText.Parse(preset.Border, (0, 0, 0, 0));
            preview.Child = new Border
            {
                Width = 110, Height = 18, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6),
                CornerRadius = new CornerRadius(Math.Min(9, preset.Radius)),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)Math.Max((int)a, 0x30), r, g, b)),
                BorderThickness = new Thickness(preset.Border is null ? 0 : 1),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(ba, br, bg, bb)),
            };
            if (preset.Look.Accent == XBar.Core.Config.AccentMode.Liquid && preview.Child is Border lg)
            {
                lg.BorderBrush = new System.Windows.Media.LinearGradientBrush(
                    new System.Windows.Media.GradientStopCollection
                    {
                        new(System.Windows.Media.Color.FromArgb(0xE0, 255, 255, 255), 0), new(System.Windows.Media.Color.FromArgb(0x40, 255, 255, 255), 0.35),
                        new(System.Windows.Media.Color.FromArgb(0x40, 255, 255, 255), 0.65), new(System.Windows.Media.Color.FromArgb(0xC0, 255, 255, 255), 1),
                    }, new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
                lg.BorderThickness = new Thickness(1.5);
                lg.CornerRadius = new CornerRadius(9);
            }
            var button = new Button
            {
                Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(8),
                Content = new StackPanel { Children = { preview, new TextBlock { Text = preset.Name, Margin = new Thickness(2, 6, 0, 0) } } },
            };
            button.Click += (_, _) => { preset.ApplyTo(S); Reload(); Commit(); };
            PresetPanel.Children.Add(button);
        }
    }
}
