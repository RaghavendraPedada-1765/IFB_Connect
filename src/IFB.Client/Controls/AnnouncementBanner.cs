using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;

namespace IFB.Client.Controls;

/// <summary>
/// Priority announcement banner — glowing glass card for admin broadcasts.
/// Supports Normal (blue glow), Important (amber glow), and Urgent (red glow + pulse).
/// </summary>
public sealed class AnnouncementBanner : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(AnnouncementBanner),
            new PropertyMetadata("", (d, _) => ((AnnouncementBanner)d).UpdateContent()));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(AnnouncementBanner),
            new PropertyMetadata("", (d, _) => ((AnnouncementBanner)d).UpdateContent()));

    public static readonly DependencyProperty PriorityProperty =
        DependencyProperty.Register(nameof(Priority), typeof(IFB.Shared.Enums.AnnouncementPriority),
            typeof(AnnouncementBanner),
            new PropertyMetadata(IFB.Shared.Enums.AnnouncementPriority.Normal,
                (d, e) => ((AnnouncementBanner)d).UpdatePriority((IFB.Shared.Enums.AnnouncementPriority)e.NewValue)));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public IFB.Shared.Enums.AnnouncementPriority Priority
    {
        get => (IFB.Shared.Enums.AnnouncementPriority)GetValue(PriorityProperty);
        set => SetValue(PriorityProperty, value);
    }

    public event EventHandler? Acknowledged;

    private Border _card = null!;
    private Border _accentBar = null!;
    private TextBlock _titleText = null!;
    private TextBlock _messageText = null!;
    private TextBlock _priorityIcon = null!;
    private Storyboard? _pulseStoryboard;

    public AnnouncementBanner()
    {
        Build();
        ActualThemeChanged += (_, _) => UpdatePriority(Priority);
    }

    private void Build()
    {
        _card = new Border
        {
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18, 16, 18, 16),
            BorderThickness = new Thickness(1)
        };

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Accent bar (left side)
        _accentBar = new Border
        {
            Width = 4,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(_accentBar, 0);

        // Priority icon
        _priorityIcon = new TextBlock
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(_priorityIcon, 0); // will be in column 1 inner stack

        // Content
        var content = new StackPanel { Spacing = 2 };
        Grid.SetColumn(content, 1);

        _titleText = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 241, 245, 249))
        };

        _messageText = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromArgb(200, 148, 163, 184))
        };

        content.Children.Add(_titleText);
        content.Children.Add(_messageText);

        // Acknowledge button
        var ackBtn = new Button
        {
            Content = "Acknowledge",
            FontSize = 11,
            Padding = new Thickness(10, 5, 10, 5),
            CornerRadius = new CornerRadius(6),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(ackBtn, 2);
        ackBtn.Click += (_, _) =>
        {
            Acknowledged?.Invoke(this, EventArgs.Empty);
        };

        var innerGrid = new Grid();
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(_accentBar, 0);
        innerGrid.Children.Add(_accentBar);
        Grid.SetColumn(content, 1);
        innerGrid.Children.Add(content);
        Grid.SetColumn(ackBtn, 2);
        innerGrid.Children.Add(ackBtn);

        _card.Child = innerGrid;
        Content = _card;

        UpdatePriority(Priority);
        UpdateContent();
    }

    private void UpdateContent()
    {
        if (_titleText != null) _titleText.Text = Title;
        if (_messageText != null) _messageText.Text = Message;
    }

    private void UpdatePriority(IFB.Shared.Enums.AnnouncementPriority priority)
    {
        if (_card == null) return;

        _pulseStoryboard?.Stop();
        var dark = ActualTheme == ElementTheme.Dark;
        _titleText.Foreground = new SolidColorBrush(dark ? Colors.White : Color.FromArgb(255, 30, 49, 70));
        _messageText.Foreground = new SolidColorBrush(dark ? Color.FromArgb(255, 213, 225, 239) : Color.FromArgb(255, 57, 76, 95));

        switch (priority)
        {
            case IFB.Shared.Enums.AnnouncementPriority.Normal:
                ApplyStyle(
                    bgColor: dark ? Color.FromArgb(200, 12, 30, 58) : Color.FromArgb(210, 225, 242, 253),
                    borderColor: Color.FromArgb(100, 30, 127, 224),
                    accentColor: Color.FromArgb(255, 59, 130, 246),
                    icon: "\uE8BD");
                break;

            case IFB.Shared.Enums.AnnouncementPriority.Important:
                ApplyStyle(
                    bgColor: Color.FromArgb(245, 246, 183, 80),
                    borderColor: Color.FromArgb(100, 245, 158, 11),
                    accentColor: Color.FromArgb(255, 245, 158, 11),
                    icon: "\uE7BA");
                _card.Background = new SolidColorBrush(Services.ThemeTokens.Parse(dark ? "#3B3023" : "#FFF4DD"));
                _titleText.Foreground = new SolidColorBrush(Services.ThemeTokens.Parse(dark ? "#FFE2A5" : "#77501B"));
                _messageText.Foreground = _titleText.Foreground;
                break;

            case IFB.Shared.Enums.AnnouncementPriority.Urgent:
                ApplyStyle(
                    bgColor: dark ? Color.FromArgb(200, 50, 10, 10) : Color.FromArgb(235, 255, 226, 222),
                    borderColor: Color.FromArgb(150, 239, 68, 68),
                    accentColor: Color.FromArgb(255, 239, 68, 68),
                    icon: "\uE814");
                // Persistent priority colour is sufficient; no looping opacity pulses.
                break;
        }
    }

    private void ApplyStyle(Color bgColor, Color borderColor, Color accentColor, string icon)
    {
        _card.Background = new SolidColorBrush(bgColor);
        _card.BorderBrush = new SolidColorBrush(borderColor);
        _accentBar.Background = new SolidColorBrush(accentColor);
        if (_priorityIcon != null)
        {
            _priorityIcon.Text = icon;
            _priorityIcon.Foreground = new SolidColorBrush(accentColor);
        }
    }

    private void StartUrgentPulse()
    {
        if (!Services.ThemeTokens.AnimationsEnabled) return;
        _pulseStoryboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var pulse = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTarget(pulse, _card);
        Storyboard.SetTargetProperty(pulse, "Opacity");
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 1 });
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.6)), Value = 0.8 });
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.2)), Value = 1 });
        _pulseStoryboard.Children.Add(pulse);
        _pulseStoryboard.Begin();
    }

    private void AnimateDismiss()
    {
        var sb = new Storyboard();
        var slide = new DoubleAnimation { To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(200)) };
        var fade = new DoubleAnimation { To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(200)) };
        Storyboard.SetTarget(slide, this);
        Storyboard.SetTargetProperty(slide, "Height");
        Storyboard.SetTarget(fade, this);
        Storyboard.SetTargetProperty(fade, "Opacity");
        sb.Children.Add(fade);
        sb.Completed += (_, _) => Visibility = Visibility.Collapsed;
        sb.Begin();
    }

    public void ShowAnnouncement(string title, string message, IFB.Shared.Enums.AnnouncementPriority priority)
    {
        Title = title;
        Message = message;
        Priority = priority;
        Visibility = Visibility.Visible;
        if (!Services.ThemeTokens.AnimationsEnabled) { Opacity = 1; return; }
        Opacity = 0;
        var fadeIn = new DoubleAnimation { To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(300)) };
        var sb = new Storyboard();
        Storyboard.SetTarget(fadeIn, this);
        Storyboard.SetTargetProperty(fadeIn, "Opacity");
        sb.Children.Add(fadeIn);
        sb.Begin();
    }
}

