using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace IFB.Client.Controls;

public sealed class ChatListItem : UserControl
{
    public static readonly DependencyProperty ConversationNameProperty = Register(nameof(ConversationName), typeof(string), "");
    public static readonly DependencyProperty LastMessageProperty = Register(nameof(LastMessage), typeof(string), "");
    public static readonly DependencyProperty TimestampProperty = Register(nameof(Timestamp), typeof(DateTime), DateTime.MinValue);
    public static readonly DependencyProperty UnreadCountProperty = Register(nameof(UnreadCount), typeof(int), 0);
    public static readonly DependencyProperty AvatarUrlProperty = Register(nameof(AvatarUrl), typeof(string), "");
    public static readonly DependencyProperty IsGroupProperty = Register(nameof(IsGroup), typeof(bool), false);
    private static DependencyProperty Register(string name, Type type, object value) => DependencyProperty.Register(
        name, type, typeof(ChatListItem), new PropertyMetadata(value, (d, _) => ((ChatListItem)d).Update()));
    public string ConversationName { get => (string)GetValue(ConversationNameProperty); set => SetValue(ConversationNameProperty, value); }
    public string LastMessage { get => (string)GetValue(LastMessageProperty); set => SetValue(LastMessageProperty, value); }
    public DateTime Timestamp { get => (DateTime)GetValue(TimestampProperty); set => SetValue(TimestampProperty, value); }
    public int UnreadCount { get => (int)GetValue(UnreadCountProperty); set => SetValue(UnreadCountProperty, value); }
    public string AvatarUrl { get => (string)GetValue(AvatarUrlProperty); set => SetValue(AvatarUrlProperty, value); }
    public bool IsGroup { get => (bool)GetValue(IsGroupProperty); set => SetValue(IsGroupProperty, value); }
    private readonly TextBlock _name = new() { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock _preview = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = .8 };
    private readonly TextBlock _time = new() { FontSize = 11, Opacity = .8 };
    private readonly TextBlock _unread = new() { FontWeight = Microsoft.UI.Text.FontWeights.Bold };
    private readonly PresenceAvatar _avatar = new() { Size = 32 };
    public ChatListItem()
    {
        var row = new Grid { Padding = new Thickness(8), ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var avatar = _avatar;
        var text = new StackPanel { Spacing = 4 }; text.Children.Add(_name); text.Children.Add(_preview);
        var info = new StackPanel { Spacing = 4 }; info.Children.Add(_time); info.Children.Add(_unread);
        Grid.SetColumn(text, 1); Grid.SetColumn(info, 2);
        row.Children.Add(avatar); row.Children.Add(text); row.Children.Add(info); Content = row;
    }
    private void Update()
    {
        _name.Text = ConversationName ?? ""; _preview.Text = LastMessage ?? "";
        _avatar.DisplayName = ConversationName ?? "";
        _time.Text = Timestamp == DateTime.MinValue ? "" : Timestamp.ToLocalTime().ToString("t");
        _unread.Text = UnreadCount > 0 ? UnreadCount.ToString() : "";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, $"{ConversationName}, {UnreadCount} unread messages");
    }
}
