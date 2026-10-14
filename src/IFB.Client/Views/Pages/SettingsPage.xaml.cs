using IFB.Client.Services;
using IFB.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace IFB.Client.Views.Pages;
public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        var prefs = App.Services.GetRequiredService<ClientPreferences>();
        var themeService = App.Services.GetRequiredService<IThemeService>();
        var notifications = App.Services.GetRequiredService<INotificationService>();
        var panel = new StackPanel { Padding = new Thickness(32), Spacing = 26, MaxWidth = 850, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(Ui.Title("Make it yours."));
        panel.Children.Add(Ui.Caption("A comfortable workspace, with just the right amount of attention."));
        var themes = new ComboBox { ItemsSource = new[] { "Auto", "Light", "Dark" }, SelectedItem = prefs.Theme, MinWidth = 132 };
        themes.SelectionChanged += (_, _) => { prefs.Theme = themes.SelectedItem?.ToString() ?? "Auto"; themeService.SetTheme(prefs.Theme); prefs.Save(); };
        FrameworkElement Toggle(string title, string description, bool value, Action<bool> change)
        {
            var toggle = new ToggleSwitch { IsOn = value, OffContent = "", OnContent = "", MinWidth = 48 };
            toggle.Toggled += (_, _) => { change(toggle.IsOn); prefs.Save(); };
            return Ui.SettingRow(title, description, toggle);
        }
        var accent = new TextBox { Text = prefs.Accent ?? "#0055A5", MaxLength = 7, Width = 110, PlaceholderText = "#RRGGBB" };
        panel.Children.Add(Ui.Section("Appearance",
            Ui.SettingRow("Theme", "Follow Windows, or choose a light or dark workspace.", themes),
            Toggle("Solid surfaces", "Reduce transparency and motion for a quieter, faster experience.", prefs.PerformanceMode, v => { prefs.PerformanceMode = v; themeService.SetPerformanceMode(v); }),
            Ui.SettingRow("Accent colour", "Add a personal touch to your workspace.", Ui.Row(accent, Ui.Button("Apply", () => { ThemeTokens.SetAccent(accent.Text); prefs.Accent = accent.Text; prefs.Save(); return Task.CompletedTask; })))));
        panel.Children.Add(Ui.Section("Notifications",
            Toggle("Desktop notifications", "See incoming messages when you're working elsewhere.", prefs.Notifications, v => prefs.Notifications = v),
            Toggle("Message sounds", "A subtle sound when a message arrives.", prefs.Sounds, v => prefs.Sounds = v),
            Toggle("Do not disturb", "Keep your focus. Silence incoming alerts.", prefs.DoNotDisturb, v => { prefs.DoNotDisturb = v; notifications.SetDoNotDisturb(v); })));
        var presence = new ComboBox { ItemsSource = Enum.GetValues<PresenceStatus>().Where(s => s != PresenceStatus.Offline).ToArray(),
            SelectedItem = App.Services.GetRequiredService<IClientAuthService>().CurrentUser?.Presence ?? PresenceStatus.Online, MinWidth = 132 };
        presence.SelectionChanged += async (_, _) => {
            try {
                var status = (PresenceStatus)presence.SelectedItem;
                notifications.SetDoNotDisturb(prefs.DoNotDisturb || status == PresenceStatus.DoNotDisturb);
                await App.Services.GetRequiredService<IHubConnectionService>().UpdatePresenceAsync(new IFB.Shared.Contracts.UpdatePresenceDto(status));
            } catch (Exception ex) { await Ui.Error(this, ex); }
        };
        panel.Children.Add(Ui.Section("Your workspace",
            Ui.SettingRow("Availability", "Let your colleagues know when you're available.", presence),
            Toggle("Keep running when closed", "Receive messages while IFB Connect stays in the system tray.", prefs.CloseToTray, v => prefs.CloseToTray = v),
            Ui.SettingRow("Connected server", "Your company's messaging workspace.", Ui.Caption(App.Services.GetRequiredService<ServerEndpoint>().Address.ToString()))));
        var account = new StackPanel { Spacing = 14, Padding = new Thickness(12) };
        var user = App.Services.GetRequiredService<IClientAuthService>().CurrentUser;
        account.Children.Add(new TextBlock { Text = user?.DisplayName ?? "IFB Connect", Style = (Style)Application.Current.Resources["BodyStrongStyle"] });
        account.Children.Add(Ui.Caption("IFB Connect · Internal company communications"));
        if (user?.Role == UserRole.Admin)
            account.Children.Add(Ui.Button("Open administration", () => { App.Current.MainWindow!.OpenAdmin(); return Task.CompletedTask; }));
        account.Children.Add(Ui.Button("Sign out", async () => {
            await App.Services.GetRequiredService<IClientAuthService>().LogoutAsync();
            await App.Services.GetRequiredService<IHubConnectionService>().DisconnectAsync();
            App.Current.MainWindow!.NavigateToLogin();
        }));
        panel.Children.Add(Ui.Section("Account", account));
        var version = typeof(SettingsPage).Assembly.GetName().Version;
        var about = new StackPanel { Spacing = 6, Padding = new Thickness(12) };
        about.Children.Add(new TextBlock { Text = $"IFB Connect {(version == null ? "" : $"v{version.Major}.{version.Minor}.{version.Build}")}", Style = (Style)Application.Current.Resources["BodyStrongStyle"] });
        about.Children.Add(Ui.Caption($"© {DateTime.Now.Year} IFB Automotive. All rights reserved."));
        about.Children.Add(Ui.Caption("Developed by Raghavendra Pedada, Software Intern"));
        panel.Children.Add(Ui.Section("About", about));
        Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
}


