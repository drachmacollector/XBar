// XBar - GPL-3.0-or-later
using System.Windows;
using System.Windows.Controls;

namespace XBar.App.UI;

public partial class AppearanceEditor : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(AppearanceEditor), new PropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(AppearanceEditor), new PropertyMetadata(""));
    public static readonly DependencyProperty ShowEnableProperty = DependencyProperty.Register(nameof(ShowEnable), typeof(bool), typeof(AppearanceEditor), new PropertyMetadata(true));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public bool ShowEnable { get => (bool)GetValue(ShowEnableProperty); set => SetValue(ShowEnableProperty, value); }

    public AppearanceEditor() => InitializeComponent();
}
