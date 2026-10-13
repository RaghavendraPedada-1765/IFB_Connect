using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace IFB.Client.Controls;

/// <summary>
/// Slim icon navigation rail button with icon, label tooltip, selection indicator,
/// and animated hover/press states.
/// </summary>
public sealed class NavRailButton : UserControl
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(NavRailButton),
            new PropertyMetadata("", OnIconChanged));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(NavRailButton),
            new PropertyMetadata("", (d, e) => { ToolTipService.SetToolTip((NavRailButton)d, e.NewValue); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(d, (string)e.NewValue); }));

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(NavRailButton),
            new PropertyMetadata(false, OnSelectedChanged));

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public event RoutedEventHandler? Click;

    public NavRailButton()
    {
        InitializeComponent();
        IsTabStop = true;
        KeyDown += (_, e) => { if (e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space) { Click?.Invoke(this, new RoutedEventArgs()); e.Handled = true; } };
        ActualThemeChanged += (_, _) => UpdateSelectedState(IsSelected);
        PointerEntered += (_, _) => AnimateHover(true);
        PointerExited += (_, _) => AnimateHover(false);
        PointerPressed += (_, _) => AnimatePress(true);
        PointerReleased += (s, e) =>
        {
            AnimatePress(false);
            Click?.Invoke(this, new RoutedEventArgs());
        };
    }

    private void AnimateHover(bool entering)
    {
        if (!Services.ThemeTokens.AnimationsEnabled) { HoverBackground.Opacity = entering ? 1 : 0; return; }
        if (XamlRoot == null) return;
        var anim = new DoubleAnimation
        {
            To = entering ? 1 : 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, HoverBackground);
        Storyboard.SetTargetProperty(anim, "Opacity");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Begin();
    }

    private void AnimatePress(bool pressing)
    {
        if (!Services.ThemeTokens.AnimationsEnabled) return;
        var scale = pressing ? 0.88 : 1.0;
        var anim = new DoubleAnimation
        {
            To = scale,
            Duration = new Duration(TimeSpan.FromMilliseconds(80)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, IconPresenter);
        Storyboard.SetTargetProperty(anim, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)");
        var animY = new DoubleAnimation { To = scale, Duration = anim.Duration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(animY, IconPresenter);
        Storyboard.SetTargetProperty(animY, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Children.Add(animY);
        sb.Begin();
    }

    private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavRailButton btn && btn.IconPresenter != null)
            btn.IconPresenter.Text = (string)e.NewValue;
    }

    private static void OnSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavRailButton btn)
            btn.UpdateSelectedState((bool)e.NewValue);
    }

    private void UpdateSelectedState(bool selected)
    {
        if (SelectionIndicator == null) return;
        SelectionIndicator.Opacity = selected ? 1 : 0;
        IconPresenter.Foreground = selected
            ? (Brush)Application.Current.Resources["AccentPrimaryBrush"]
            : (Brush)Application.Current.Resources["TextSecondaryBrush"];
    }

    // UI elements — assigned in InitializeComponent (code-only control for simplicity)
    private Border HoverBackground = null!;
    private TextBlock IconPresenter = null!;
    private Border SelectionIndicator = null!;

    private void InitializeComponent()
    {
        // Build UI programmatically to avoid needing a separate .xaml file for this small control
        var root = new Grid
        {
            Width = 40,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // Hover background
        HoverBackground = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(30, 255, 255, 255)),
            Opacity = 0
        };

        // Selection indicator (left accent bar)
        SelectionIndicator = new Border
        {
            Width = 3,
            Height = 20,
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(-18, 0, 0, 0),
            Background = (Brush)Application.Current.Resources["AccentPrimaryBrush"],
            Opacity = 0
        };

        // Icon text
        IconPresenter = new TextBlock
        {
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Text = Icon,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = new ScaleTransform()
        };
        ToolTipService.SetToolTip(root, Label);

        root.Children.Add(HoverBackground);
        root.Children.Add(SelectionIndicator);
        root.Children.Add(IconPresenter);

        Content = root;

        UpdateSelectedState(IsSelected);
    }
}

