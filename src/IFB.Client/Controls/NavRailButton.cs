using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace IFB.Client.Controls;

public sealed class NavRailButton : UserControl
{
    private readonly Button _button;
    private readonly TextBlock _icon;
    private readonly TextBlock _label;
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string), typeof(NavRailButton), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(NavRailButton), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(NavRailButton), new PropertyMetadata(false, Changed));
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((NavRailButton)d).Refresh();
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public event RoutedEventHandler? Click;

    public NavRailButton()
    {
        _icon = new TextBlock { FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 18, VerticalAlignment = VerticalAlignment.Center };
        _label = new TextBlock { FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(_icon); row.Children.Add(_label);
        _button = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 11, 14, 11), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(0), MinHeight = 44 };
        _button.Click += (_, e) => Click?.Invoke(this, e);
        Services.FluidMotion.Attach(_button);
        Content = _button;
        Loaded += (_, _) => Refresh();
        ActualThemeChanged += (_, _) => Refresh();
    }

    private void Refresh()
    {
        if (_button == null) return;
        _icon.Text = Icon; _label.Text = Label;
        if (new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast)
        {
            var colours = new Windows.UI.ViewManagement.UISettings();
            _button.Background = new SolidColorBrush(colours.GetColorValue(IsSelected ? Windows.UI.ViewManagement.UIColorType.Foreground : Windows.UI.ViewManagement.UIColorType.Background));
            _icon.Foreground = _label.Foreground = new SolidColorBrush(colours.GetColorValue(IsSelected ? Windows.UI.ViewManagement.UIColorType.Background : Windows.UI.ViewManagement.UIColorType.Foreground));
            return;
        }
        var dark = ActualTheme == ElementTheme.Dark;
        _button.Background = new SolidColorBrush(IsSelected ? Services.ThemeTokens.Parse(dark ? "#263B50" : "#E0EBF7") : Microsoft.UI.Colors.Transparent);
        _icon.Foreground = _label.Foreground = new SolidColorBrush(Services.ThemeTokens.Parse(IsSelected ? (dark ? "#85BCF0" : "#0055A5") : (dark ? "#C6C9CE" : "#50545C")));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_button, Label + (IsSelected ? ", selected" : ""));
    }
}

