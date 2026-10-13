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
        var panel = new StackPanel { Padding = new Thickness(32), Spacing = 20, MaxWidth = 650, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Ui.Title("Settings"));
        var themes = new ComboBox { Header = "Appearance", ItemsSource = new[] { "Auto", "Dark", "Light" }, SelectedItem = prefs.Theme, MinWidth = 200 };
        themes.SelectionChanged += (_, _) => { prefs.Theme = themes.SelectedItem?.ToString() ?? "Auto"; themeService.SetTheme(prefs.Theme); prefs.Save(); }; panel.Children.Add(themes);
        void Toggle(string label, bool value, Action<bool> change)
        { var toggle = new ToggleSwitch { Header = label, IsOn = value }; toggle.Toggled += (_, _) => { change(toggle.IsOn); prefs.Save(); }; panel.Children.Add(toggle); }
        Toggle("Performance mode (solid surfaces)", prefs.PerformanceMode, v => { prefs.PerformanceMode = v; themeService.SetPerformanceMode(v); });
        Toggle("Desktop notifications", prefs.Notifications, v => prefs.Notifications = v);
        Toggle("Notification sounds", prefs.Sounds, v => prefs.Sounds = v);
        Toggle("Do not disturb", prefs.DoNotDisturb, v => { prefs.DoNotDisturb = v; notifications.SetDoNotDisturb(v); });
        Toggle("Keep running in the system tray when closed", prefs.CloseToTray, v => prefs.CloseToTray = v);
        var presence = new ComboBox { Header = "Presence", ItemsSource = Enum.GetValues<PresenceStatus>().Where(s => s != PresenceStatus.Offline).ToArray(), SelectedIndex = 0 };
        presence.SelectionChanged += async (_, _) => { try { var status = (PresenceStatus)presence.SelectedItem;
            notifications.SetDoNotDisturb(status == PresenceStatus.DoNotDisturb); await App.Services.GetRequiredService<IHubConnectionService>().UpdatePresenceAsync(new IFB.Shared.Contracts.UpdatePresenceDto(status)); } catch (Exception ex) { await Ui.Error(this, ex); } }; panel.Children.Add(presence);
        var accent = new TextBox { Header = "Personal accent (#RRGGBB)", Text = prefs.Accent ?? "#0055A5", MaxLength = 7 };
        panel.Children.Add(Ui.Row(accent, Ui.Button("Apply accent", () => { ThemeTokens.SetAccent(accent.Text); prefs.Accent = accent.Text; prefs.Save(); return Task.CompletedTask; })));
        panel.Children.Add(new TextBlock { Text = "Server: " + App.Services.GetRequiredService<ServerEndpoint>().Address, TextWrapping = TextWrapping.Wrap });
        if (App.Services.GetRequiredService<IClientAuthService>().CurrentUser?.Role == UserRole.Admin)
            panel.Children.Add(Ui.Button("Administration", () => { App.Current.MainWindow!.OpenAdmin(); return Task.CompletedTask; }));
        panel.Children.Add(Ui.Button("Sign out", async () => { await App.Services.GetRequiredService<IClientAuthService>().LogoutAsync(); await App.Services.GetRequiredService<IHubConnectionService>().DisconnectAsync(); App.Current.MainWindow!.NavigateToLogin(); }));
        Content = new ScrollViewer { Content = panel };
    }
}
