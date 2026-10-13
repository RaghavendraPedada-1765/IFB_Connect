using IFB.Client.Services;
using IFB.Client.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;
using IFB.Shared.DTOs;
using IFB.Shared.Enums;
using Microsoft.UI.Xaml.Controls;

namespace IFB.Client.Views;

public sealed partial class MainWindow : Window
{
    public void Dispatch(Action action) => DispatcherQueue.TryEnqueue(() => action());
    public object DataContext { get => RootGrid.DataContext; set => RootGrid.DataContext = value; }
    private readonly IHubConnectionService _hub;
    private readonly INavigationService _nav;
    private AnnouncementDto? _announcement;
    private bool _windowActive;

    public MainWindow()
    {
        InitializeComponent();
        if (!ClientProfile.IsDefault) Title = "IFB Connect — " + ClientProfile.Name;
        Activated += async (_, e) =>
        {
            _windowActive = e.WindowActivationState != WindowActivationState.Deactivated;
            if (_windowActive && MainFrame.Content is ChatPage)
                await App.Services.GetRequiredService<ViewModels.ChatViewModel>().MarkVisibleMessagesReadAsync();
        };
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppTitleBar.SizeChanged += (_, _) => UpdateTitleBarDragRegions();

        _hub = App.Services.GetRequiredService<IHubConnectionService>();
        _nav = App.Services.GetRequiredService<INavigationService>();
        _nav.NavigationRequested += (_, e) =>
        { if (e.Page == "Chat" && e.Parameter is string id) Dispatch(() => _ = OpenConversationAsync(id)); };
        _hub.AnnouncementPosted += (_, _) => _ = RefreshAnnouncementsAsync();
        _hub.MessageReceived += (_, message) =>
        {
            var auth = App.Services.GetRequiredService<IClientAuthService>();
            if (message.SenderId != auth.CurrentUser?.Id && (!_windowActive || MainFrame.Content is not ChatPage ||
                (DataContext as ViewModels.MainViewModel)?.SelectedConversation?.Id != message.ConversationId))
                App.Services.GetRequiredService<INotificationService>().ShowToast(message.SenderDisplayName, message.Content, message.ConversationId);
        };
        AnnouncementBannerControl.Acknowledged += async (_, _) =>
        { try { if (_announcement != null) await App.Services.GetRequiredService<IApiService>().AcknowledgeAnnouncementAsync(_announcement.Id); await RefreshAnnouncementsAsync(); } catch (Exception ex) { await Ui.Error(RootGrid, ex); } };

        // Subscribe to connection state changes
        _hub.ConnectionStateChanged += OnConnectionStateChanged;

        // Keyboard shortcuts
        RootGrid.KeyDown += RootGrid_KeyDown;
    }

    public bool IsConversationVisible(string id) => _windowActive && MainFrame.Content is ChatPage &&
        (DataContext as ViewModels.MainViewModel)?.SelectedConversation?.Id == id;

    public void NavigateToLogin()
    {
        ExtendsContentIntoTitleBar = false;
        // Collapse the three-pane shell and show just the login frame
        if (RootGrid != null) RootGrid.Visibility = Visibility.Collapsed;
        LoginFrame.Visibility = Visibility.Visible;
        LoginFrame.Navigate(typeof(LoginPage), null, new DrillInNavigationTransitionInfo());
    }

    public async void NavigateToMain()
    {
        ExtendsContentIntoTitleBar = true;
        // Restore the three-pane shell
        LoginFrame.Visibility = Visibility.Collapsed;
        if (RootGrid != null) RootGrid.Visibility = Visibility.Visible;

        // Initialize main shell ViewModel
        var vm = App.Services.GetRequiredService<ViewModels.MainViewModel>();
        DataContext = vm;
        ConversationList.ItemsSource = vm.Conversations;
        try { await vm.InitializeCommand.ExecuteAsync(null); await RefreshAnnouncementsAsync(); }
        catch (Exception ex) { await Ui.Error(RootGrid!, ex); }

        SelfAvatar.DisplayName = App.Services.GetRequiredService<IClientAuthService>().CurrentUser?.DisplayName ?? "IFB";
        SelfAvatar.PresenceStatus = PresenceStatus.Online;
        MainFrame.Content = null;
        EmptyState.Visibility = Visibility.Visible;
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Controls.NavRailButton btn) return;

        // Update selection state on all nav buttons
        foreach (var child in (IconRail.Child as FrameworkElement)?.GetChildrenOfType<Controls.NavRailButton>()
                 ?? Enumerable.Empty<Controls.NavRailButton>())
        {
            child.IsSelected = child == btn;
        }

        PaneTitle.Text = btn.Tag?.ToString() ?? "Chats";
        EmptyState.Visibility = Visibility.Collapsed;

        switch (btn.Tag?.ToString())
        {
            case "Chats":
                ConversationList.Visibility = Visibility.Visible;
                if (DataContext is ViewModels.MainViewModel { SelectedConversation: not null } selected)
                    MainFrame.Navigate(typeof(ChatPage), selected.SelectedConversation.Id);
                else { MainFrame.Content = null; EmptyState.Visibility = Visibility.Visible; }
                break;

            case "Channels":
                MainFrame.Navigate(typeof(ChannelsPage), null, new SlideNavigationTransitionInfo
                {
                    Effect = SlideNavigationTransitionEffect.FromRight
                });
                break;

            case "Directory":
                MainFrame.Navigate(typeof(DirectoryPage), null, new SlideNavigationTransitionInfo
                {
                    Effect = SlideNavigationTransitionEffect.FromRight
                });
                break;

            case "Files":
                MainFrame.Navigate(typeof(FilesPage), null, new SlideNavigationTransitionInfo
                {
                    Effect = SlideNavigationTransitionEffect.FromRight
                });
                break;

            case "Settings":
                MainFrame.Navigate(typeof(SettingsPage), null, new SlideNavigationTransitionInfo
                {
                    Effect = SlideNavigationTransitionEffect.FromRight
                });
                break;
        }
    }

    private void Conversation_Selected(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        if (e.AddedItems.FirstOrDefault() is ViewModels.ConversationItemViewModel conv)
        {
            if (DataContext is ViewModels.MainViewModel main) main.SelectConversationCommand.Execute(conv);
            EmptyState.Visibility = Visibility.Collapsed;
            MainFrame.Navigate(typeof(ChatPage), conv.Id,
                new SlideNavigationTransitionInfo
                {
                    Effect = SlideNavigationTransitionEffect.FromRight
                });
        }
    }

    private void NewConversation_Click(object sender, RoutedEventArgs e)
    {
        // Show command palette in "new conversation" mode
        CommandPaletteControl.ShowInMode(Controls.CommandPaletteMode.NewConversation);
    }

    private void GlobalSearch_Click(object sender, RoutedEventArgs e) => CommandPaletteControl.Show();

    private void UpdateTitleBarDragRegions()
    {
        if (GlobalSearchButton.ActualWidth == 0 || RootGrid.XamlRoot == null) return;
        var search = GlobalSearchButton.TransformToVisual(AppTitleBar).TransformPoint(new Windows.Foundation.Point());
        var status = ConnectionLabel.TransformToVisual(AppTitleBar).TransformPoint(new Windows.Foundation.Point());
        var scale = RootGrid.XamlRoot.RasterizationScale;
        var height = (int)(AppTitleBar.ActualHeight * scale);
        var regions = new List<Windows.Graphics.RectInt32>
        { new(0, 0, Math.Max(0, (int)(search.X * scale)), height) };
        var rightStart = (int)((search.X + GlobalSearchButton.ActualWidth) * scale);
        var rightEnd = (int)((status.X - 16) * scale);
        if (rightEnd > rightStart) regions.Add(new Windows.Graphics.RectInt32(rightStart, 0, rightEnd - rightStart, height));
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle));
        appWindow.TitleBar.SetDragRectangles(regions.ToArray());
    }

    private void Search_TextChanged(Microsoft.UI.Xaml.Controls.AutoSuggestBox sender,
        Microsoft.UI.Xaml.Controls.AutoSuggestBoxTextChangedEventArgs args)
    {
        // Filter the current conversation list.
        if (DataContext is ViewModels.MainViewModel vm)
        {
            ConversationList.ItemsSource = string.IsNullOrWhiteSpace(sender.Text) ? vm.Conversations :
                vm.Conversations.Where(c => c.Name.Contains(sender.Text, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Ctrl+K → Command palette
        if (e.Key == VirtualKey.K &&
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            e.Handled = true;
            CommandPaletteControl.Toggle();
        }

        // Escape → dismiss command palette
        if (e.Key == VirtualKey.Escape)
            CommandPaletteControl.Dismiss();
    }

    private void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ConnectionLabel.Text = state switch
            {
                ConnectionState.Connected => "Connected",
                ConnectionState.Connecting => "Connecting...",
                ConnectionState.Reconnecting => "Reconnecting...",
                ConnectionState.Disconnected => "Offline",
                _ => "Unknown"
            };

            ConnectionDot.Fill = state switch
            {
                ConnectionState.Connected => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PresenceOnlineBrush"],
                ConnectionState.Connecting or ConnectionState.Reconnecting =>
                    (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PresenceAwayBrush"],
                _ => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PresenceOfflineBrush"]
            };
        });
    }

    public void OpenAdmin() { EmptyState.Visibility = Visibility.Collapsed; MainFrame.Navigate(typeof(AdminPage)); }
    private async void Reconnect_Click(object sender, TappedRoutedEventArgs e)
    { try { await _hub.ConnectAsync(); await App.Services.GetRequiredService<ViewModels.MainViewModel>().RefreshConversationsAsync(); } catch (Exception ex) { await Ui.Error(RootGrid, ex); } }

    public async Task OpenConversationAsync(string id)
    {
        try
        {
            var main = App.Services.GetRequiredService<ViewModels.MainViewModel>();
            await main.RefreshConversationsAsync(); ConversationList.ItemsSource = main.Conversations;
            var conversation = main.Conversations.FirstOrDefault(c => c.Id == id);
            if (conversation != null) { main.SelectedConversation = conversation; ConversationList.SelectedItem = conversation; }
            await _hub.JoinConversationAsync(id); EmptyState.Visibility = Visibility.Collapsed;
            MainFrame.Navigate(typeof(ChatPage), id, new DrillInNavigationTransitionInfo());
        }
        catch (Exception ex) { await Ui.Error(RootGrid, ex); }
    }

    private async Task RefreshAnnouncementsAsync()
    {
        try
        {
            var announcements = await App.Services.GetRequiredService<IApiService>().GetAnnouncementsAsync();
            _announcement = announcements.FirstOrDefault(a => !a.IsAcknowledged);
            AnnouncementBannerControl.Visibility = _announcement == null ? Visibility.Collapsed : Visibility.Visible;
            if (_announcement != null) AnnouncementBannerControl.ShowAnnouncement(_announcement.Title, _announcement.Content, _announcement.Priority);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.Message); }
    }

    public async Task ManageMembersAsync(string id)
    {
        var api = App.Services.GetRequiredService<ClientRequests>();
        var members = await api.GetAsync<List<MemberInfo>>($"api/v1/conversations/{id}/members");
        var list = new ListView { ItemsSource = members, ItemTemplate = Ui.Template(Ui.Text("DisplayName") + Ui.Text("Presence", 12)), MaxHeight = 260 };
        var users = new ComboBox { Header = "Add an employee", DisplayMemberPath = "DisplayName", MinWidth = 250,
            ItemsSource = (await App.Services.GetRequiredService<IApiService>().SearchUsersAsync(null, null, 200)).Users };
        var status = new TextBlock { Text = "Members can be managed by the group owner or an administrator.", TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(status); panel.Children.Add(list); panel.Children.Add(users);
        panel.Children.Add(Ui.Row(Ui.Button("Add", async () => { if (users.SelectedItem is UserDto u) await api.SendAsync(HttpMethod.Post, $"api/v1/conversations/{id}/members/{u.Id}"); list.ItemsSource = await api.GetAsync<List<MemberInfo>>($"api/v1/conversations/{id}/members"); }),
            Ui.Button("Remove selected", async () => { if (list.SelectedItem is MemberInfo m) await api.SendAsync(HttpMethod.Delete, $"api/v1/conversations/{id}/members/{m.Id}"); list.ItemsSource = await api.GetAsync<List<MemberInfo>>($"api/v1/conversations/{id}/members"); })));
        await new ContentDialog { XamlRoot = RootGrid.XamlRoot, Title = "Conversation members", Content = panel, CloseButtonText = "Close" }.ShowAsync();
    }
}
public record MemberInfo(string Id, string DisplayName, string? AvatarUrl, bool IsAdmin, PresenceStatus Presence, DateTime JoinedAt);

// Extension to find children of a specific type in the visual tree
public static class VisualTreeExtensions
{
    public static IEnumerable<T> GetChildrenOfType<T>(this DependencyObject element) where T : DependencyObject
    {
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i);
            if (child is T typed) yield return typed;
            foreach (var nested in GetChildrenOfType<T>(child))
                yield return nested;
        }
    }
}
