using IFB.Shared.Enums;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace IFB.Client.Controls;

/// <summary>
/// Circular avatar with presence status ring/dot, initials fallback, and pulse animation for online status.
/// </summary>
public sealed class PresenceAvatar : UserControl
{
    public static readonly DependencyProperty DisplayNameProperty =
        DependencyProperty.Register(nameof(DisplayName), typeof(string), typeof(PresenceAvatar),
            new PropertyMetadata("", (d, _) => ((PresenceAvatar)d).UpdateInitials()));

    public static readonly DependencyProperty AvatarUrlProperty =
        DependencyProperty.Register(nameof(AvatarUrl), typeof(string), typeof(PresenceAvatar),
            new PropertyMetadata(null));

    public static readonly DependencyProperty PresenceStatusProperty =
        DependencyProperty.Register(nameof(PresenceStatus), typeof(PresenceStatus), typeof(PresenceAvatar),
            new PropertyMetadata(PresenceStatus.Offline, (d, e) => ((PresenceAvatar)d).UpdatePresence((PresenceStatus)e.NewValue)));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double), typeof(PresenceAvatar),
            new PropertyMetadata(40.0, (d, _) => ((PresenceAvatar)d).UpdateSize()));

    public string DisplayName
    {
        get => (string)GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    public string? AvatarUrl
    {
        get => (string?)GetValue(AvatarUrlProperty);
        set => SetValue(AvatarUrlProperty, value);
    }

    public PresenceStatus PresenceStatus
    {
        get => (PresenceStatus)GetValue(PresenceStatusProperty);
        set => SetValue(PresenceStatusProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private Grid _root = null!;
    private Ellipse _avatarCircle = null!;
    private TextBlock _initialsText = null!;
    private Ellipse _presenceDot = null!;
    private Ellipse _presencePulse = null!;

    public PresenceAvatar() => Build();

    private void Build()
    {
        _root = new Grid();

        // Avatar circle background (gradient based on name hash)
        _avatarCircle = new Ellipse
        {
            Width = Size,
            Height = Size
        };
        _avatarCircle.Fill = GetAvatarGradient(DisplayName);

        // Initials text
        _initialsText = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = Size * 0.35
        };
        UpdateInitials();

        // Presence pulse ring (animated, shown when online)
        _presencePulse = new Ellipse
        {
            Width = 12,
            Height = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -1, -1),
            Opacity = 0
        };

        // Presence dot
        _presenceDot = new Ellipse
        {
            Width = 10,
            Height = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 0),
            Stroke = new SolidColorBrush(Color.FromArgb(255, 17, 24, 39)),
            StrokeThickness = 1.5
        };

        _root.Children.Add(_avatarCircle);
        _root.Children.Add(_initialsText);
        _root.Children.Add(_presencePulse);
        _root.Children.Add(_presenceDot);

        Content = _root;
        Width = Size + 2;
        Height = Size + 2;

        UpdatePresence(PresenceStatus);
    }

    private void UpdateInitials()
    {
        if (_initialsText == null) return;
        var name = DisplayName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            _initialsText.Text = "?";
            return;
        }
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _initialsText.Text = parts.Length >= 2
            ? $"{parts[0][0]}{parts[^1][0]}".ToUpper()
            : name[..1].ToUpper();

        _avatarCircle.Fill = GetAvatarGradient(name);
    }

    private void UpdatePresence(PresenceStatus status)
    {
        if (_presenceDot == null) return;

        // Stop any existing pulse animation
        _presencePulse.Opacity = 0;

        Color dotColor = status switch
        {
            PresenceStatus.Online => Color.FromArgb(255, 34, 197, 94),
            PresenceStatus.Away => Color.FromArgb(255, 245, 158, 11),
            PresenceStatus.Busy or PresenceStatus.DoNotDisturb => Color.FromArgb(255, 239, 68, 68),
            _ => Color.FromArgb(255, 107, 114, 128)
        };
        _presenceDot.Fill = new SolidColorBrush(dotColor);
        _presenceDot.Visibility = Visibility.Visible;

        // Animated pulse ring for Online status
        if (status == PresenceStatus.Online)
        {
            _presencePulse.Fill = new SolidColorBrush(Color.FromArgb(120, 34, 197, 94));
            StartPulseAnimation();
        }
    }

    private void StartPulseAnimation()
    {
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

        var scaleX = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTarget(scaleX, _presencePulse);
        Storyboard.SetTargetProperty(scaleX, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)");
        scaleX.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 0.8 });
        scaleX.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1)), Value = 1.8 });
        scaleX.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2)), Value = 0.8 });

        var opacity = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTarget(opacity, _presencePulse);
        Storyboard.SetTargetProperty(opacity, "Opacity");
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 0.8 });
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1)), Value = 0 });
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2)), Value = 0 });

        _presencePulse.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        _presencePulse.RenderTransform = new ScaleTransform();

        sb.Children.Add(scaleX);
        sb.Children.Add(opacity);
        sb.Begin();
    }

    private void UpdateSize()
    {
        if (_avatarCircle == null) return;
        _avatarCircle.Width = _avatarCircle.Height = Size;
        _initialsText.FontSize = Size * 0.35;
        Width = Height = Size + 2;
    }

    private static readonly Color[] AvatarPalette =
    [
        Color.FromArgb(255, 0, 85, 165),   // IFB Blue
        Color.FromArgb(255, 88, 28, 135),  // Purple
        Color.FromArgb(255, 14, 116, 144), // Teal
        Color.FromArgb(255, 162, 28, 28),  // Red
        Color.FromArgb(255, 5, 95, 70),    // Green
        Color.FromArgb(255, 146, 64, 14),  // Amber
        Color.FromArgb(255, 30, 58, 138),  // Indigo
        Color.FromArgb(255, 127, 29, 29),  // Rose
    ];

    private static LinearGradientBrush GetAvatarGradient(string name)
    {
        int hash = Math.Abs(name?.GetHashCode() ?? 0);
        var c1 = AvatarPalette[hash % AvatarPalette.Length];
        var c2 = AvatarPalette[(hash / 8) % AvatarPalette.Length];
        return new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop { Color = c1, Offset = 0 },
                new GradientStop { Color = c2, Offset = 1 }
            }, 135);
    }
}

