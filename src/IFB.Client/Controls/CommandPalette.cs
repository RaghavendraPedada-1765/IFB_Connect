using Microsoft.Extensions.DependencyInjection;
using IFB.Shared.Enums;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;

namespace IFB.Client.Controls;

public enum CommandPaletteMode { Search, NewConversation, Actions }

/// <summary>
/// Floating glass command palette (Ctrl+K) — search conversations, users, and actions.
/// Renders as a centered overlay with frosted glass card and animated entry/exit.
/// </summary>
public sealed class CommandPalette : UserControl
{
    private Border _backdrop = null!;
    private Border _card = null!;
    private TextBox _searchBox = null!;
    private ListView _resultsList = null!;
    private bool _isVisible = false;

    public CommandPalette()
    {
        Build();
        Loaded += (_, _) => RefreshTheme();
        ActualThemeChanged += (_, _) => RefreshTheme();
    }

    private void RefreshTheme()
    {
        var dark = ActualTheme == ElementTheme.Dark;
        var tint = Services.ThemeTokens.Parse(dark ? "#182438" : "#F5F9FD");
        _card.Background = new AcrylicBrush { TintColor = tint, TintOpacity = .82, FallbackColor = tint,
            AlwaysUseFallback = App.Services.GetRequiredService<Services.IThemeService>().IsPerformanceMode };
        _card.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 135, 154, 175));
        _searchBox.Foreground = new SolidColorBrush(Services.ThemeTokens.Parse(dark ? "#F1F5F9" : "#172A40"));
        _resultsList.Foreground = _searchBox.Foreground;
    }

    private void Build()
    {
        // Semi-transparent backdrop to catch dismiss clicks
        _backdrop = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
            IsHitTestVisible = true
        };
        _backdrop.Tapped += (_, _) => Dismiss();

        // Glass card container
        _card = new Border
        {
            Width = 480,
            MaxHeight = 480,
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 80, 0, 0),
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0),
            RenderTransform = new ScaleTransform { ScaleX = 1, ScaleY = 0.95 }
        };

        // Acrylic background for the card
        _card.Background = new AcrylicBrush
        {
            TintColor = Color.FromArgb(255, 8, 15, 30),
            TintOpacity = 0.88,
            FallbackColor = Color.FromArgb(240, 15, 22, 40)
        };

        // Inner layout
        var stack = new StackPanel();

        // Search input row
        var inputRow = new Grid { Padding = new Thickness(16, 12, 16, 12) };
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var searchIcon = new TextBlock
        {
            Text = "\uE721",
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 18,
            Foreground = new SolidColorBrush(Color.FromArgb(160, 148, 163, 184)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(searchIcon, 0);

        _searchBox = new TextBox
        {
            PlaceholderText = "Search conversations, people, or commands...",
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            FontSize = 16,
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 241, 245, 249))
        };
        Grid.SetColumn(_searchBox, 1);
        _searchBox.TextChanged += OnSearchTextChanged;

        inputRow.Children.Add(searchIcon);
        inputRow.Children.Add(_searchBox);

        // Separator
        var separator = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            Margin = new Thickness(16, 0, 16, 0)
        };

        // Results list
        _resultsList = new ListView
        {
            Padding = new Thickness(8, 4, 8, 8),
            ItemContainerStyle = CreateResultItemStyle()
        };
        _resultsList.ItemTemplate = CreateResultItemTemplate();
        _resultsList.ItemClick += OnResultClick;
        _resultsList.IsItemClickEnabled = true;

        // Footer keyboard hint
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(16, 6, 16, 6),
            Spacing = 16
        };
        footer.Children.Add(CreateKbdHint("↑↓", "Navigate"));
        footer.Children.Add(CreateKbdHint("↵", "Open"));
        footer.Children.Add(CreateKbdHint("Esc", "Close"));

        stack.Children.Add(inputRow);
        stack.Children.Add(separator);
        stack.Children.Add(_resultsList);
        stack.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255))
        });
        stack.Children.Add(footer);

        _card.Child = stack;

        var rootGrid = new Grid();
        rootGrid.Children.Add(_backdrop);
        rootGrid.Children.Add(_card);

        Content = rootGrid;
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
    }

    public void Toggle()
    {
        if (_isVisible) Dismiss();
        else Show();
    }

    public void Show(CommandPaletteMode mode = CommandPaletteMode.Search)
    {
        RefreshTheme();
        _isVisible = true;
        Visibility = Visibility.Visible;
        IsHitTestVisible = true;
        if (!Services.ThemeTokens.AnimationsEnabled)
        {
            _card.Opacity = 1;
            _card.RenderTransform = new ScaleTransform();
            _searchBox.Focus(FocusState.Programmatic); LoadDefaultResults(); return;
        }

        // Animate card in
        var fadeIn = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(200)) };
        var slideIn = new DoubleAnimation
        {
            From = 0.92, To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var sb = new Storyboard();
        Storyboard.SetTarget(fadeIn, _card);
        Storyboard.SetTargetProperty(fadeIn, "Opacity");
        Storyboard.SetTarget(slideIn, _card);
        Storyboard.SetTargetProperty(slideIn, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)");
        sb.Children.Add(fadeIn);
        sb.Children.Add(slideIn);
        sb.Begin();

        _card.Opacity = 0;
        _searchBox.Focus(FocusState.Programmatic);
        LoadDefaultResults();
    }

    public void ShowInMode(CommandPaletteMode mode)
    {
        _searchBox.PlaceholderText = mode switch
        {
            CommandPaletteMode.NewConversation => "Search people to start a conversation...",
            CommandPaletteMode.Actions => "Type a command or action...",
            _ => "Search conversations, people, or commands..."
        };
        Show(mode);
    }

    public void Dismiss()
    {
        _isVisible = false;
        var fadeOut = new DoubleAnimation { To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(150)) };
        var slideOut = new DoubleAnimation
        {
            To = 0.95,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var sb = new Storyboard();
        Storyboard.SetTarget(fadeOut, _card);
        Storyboard.SetTargetProperty(fadeOut, "Opacity");
        Storyboard.SetTarget(slideOut, _card);
        Storyboard.SetTargetProperty(slideOut, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)");
        sb.Children.Add(fadeOut);
        sb.Children.Add(slideOut);
        sb.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            IsHitTestVisible = false;
            _searchBox.Text = string.Empty;
        };
        sb.Begin();
    }

    private int _searchRevision;
    private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var revision = ++_searchRevision; await Task.Delay(250);
        if (revision == _searchRevision) await LoadResultsAsync(_searchBox.Text);
    }
    private async void LoadDefaultResults() => await LoadResultsAsync("");
    private async Task LoadResultsAsync(string query)
    {
        try
        {
            var api = App.Services.GetRequiredService<Services.IApiService>();
            var conversations = await api.GetConversationsAsync();
            var people = await api.SearchUsersAsync(query, null, 30);
            if (query != _searchBox.Text) return;
            _resultsList.Items.Clear();
            foreach (var c in conversations.Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(15))
                _resultsList.Items.Add(new CommandPaletteResult(c.Name, c.Type.ToString(), c.Id, false));
            foreach (var u in people.Users.Where(u => u.Id != App.Services.GetRequiredService<Services.IClientAuthService>().CurrentUser?.Id))
                _resultsList.Items.Add(new CommandPaletteResult(u.DisplayName, u.Department ?? "Employee", u.Id, true));
        }
        catch { _resultsList.Items.Clear(); _resultsList.Items.Add(new CommandPaletteResult("Server unavailable", "Check your connection", "", false)); }
    }
    private async void OnResultClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not CommandPaletteResult item || item.Id == "") return;
        Dismiss();
        try
        {
            var id = item.IsPerson ? (await App.Services.GetRequiredService<Services.IApiService>().CreateDmAsync(item.Id)).Id : item.Id;
            await App.Current.MainWindow!.OpenConversationAsync(id);
        }
        catch (Exception ex) { await Views.Ui.Error(this, ex); }
    }
    private static Style CreateResultItemStyle()
    {
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(ListViewItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(ListViewItem.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(ListViewItem.MarginProperty, new Thickness(0, 2, 0, 2)));
        return style;
    }

    private static DataTemplate CreateResultItemTemplate()
    {
        // Build item template programmatically
        return Views.Ui.Template(Views.Ui.Text("Title", 16) + Views.Ui.Text("Subtitle", 12));
    }

    private static StackPanel CreateKbdHint(string key, string desc)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        panel.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBlock
            {
                Text = key,
                FontSize = 10,
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                Foreground = new SolidColorBrush(Color.FromArgb(160, 148, 163, 184))
            }
        });
        panel.Children.Add(new TextBlock
        {
            Text = desc,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(120, 148, 163, 184)),
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }
}

internal record CommandPaletteResult(string Title, string Subtitle, string Id, bool IsPerson);


