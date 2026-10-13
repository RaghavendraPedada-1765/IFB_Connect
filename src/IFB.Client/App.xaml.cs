using IFB.Client.Services;
using IFB.Client.ViewModels;
using IFB.Client.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Composition.SystemBackdrops;
using System.Runtime.InteropServices;
using Windows.UI;

namespace IFB.Client;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static new App Current => (App)Application.Current;
    public MainWindow? MainWindow { get; private set; }

    private IHost _host = null!;
    public TrayIcon? Tray { get; private set; }
    private bool _exiting;
    private Window? _startupErrorWindow;

    public App()
    {
        UnhandledException += (_, e) => LogStartup("Unhandled UI exception", e.Exception);
        try { InitializeComponent(); }
        catch (Exception ex) { LogStartup("Application resources", ex); throw; }
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try { await LaunchAsync(); }
        catch (Exception ex)
        {
            LogStartup("Application startup", ex);
            var errorWindow = (Window?)MainWindow ?? new Window();
            errorWindow.Title = "IFB Connect — startup error";
            errorWindow.Content = new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = "IFB Connect could not start.\n\n" + ex.Message + "\n\nDetails were saved to " + System.IO.Path.Combine(ClientProfile.DataDirectory, "startup.log"),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24)
            };
            _startupErrorWindow = errorWindow;
            errorWindow.Activate();
        }
    }

    private static void LogStartup(string stage, Exception exception)
    {
        try
        {
            var directory = ClientProfile.DataDirectory;
            System.IO.Directory.CreateDirectory(directory);
            System.IO.File.AppendAllText(System.IO.Path.Combine(directory, "startup.log"), $"{DateTimeOffset.Now:O} {stage}\n{exception}\n\n");
        }
        catch { }
    }

    private async Task LaunchAsync()
    {
        _host = CreateHost();
        await _host.StartAsync();
        Services = _host.Services;
        var preferences = Services.GetRequiredService<ClientPreferences>();

        // Determine performance mode (RDP, no glass support, or user override)
        var perfMode = IsPerformanceModeRequired() || preferences.PerformanceMode;
        Services.GetRequiredService<IThemeService>().SetPerformanceMode(perfMode);

        MainWindow = new MainWindow();
        ConfigureWindow(MainWindow);
        MainWindow.Activate();
        ThemeTokens.Initialize(preferences);
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
        void Open() { TrayIcon.ShowWindow(windowHandle, 9); MainWindow.Activate(); }
        Tray = new TrayIcon(windowHandle, Open, () => _ = ExitAsync());
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(windowHandle));
        appWindow.Closing += (_, e) => { if (!_exiting && preferences.CloseToTray) { e.Cancel = true; TrayIcon.ShowWindow(windowHandle, 0); } };
        try
        {
            Microsoft.Windows.AppNotifications.AppNotificationManager.Default.NotificationInvoked += (manager, e) =>
                MainWindow.Dispatch(() => { Open(); if (e.Arguments.TryGetValue("conversationId", out var id)) _ = MainWindow.OpenConversationAsync(id); });
            Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Register();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Notification registration: " + ex.Message); }
        MainWindow.Closed += async (_, _) => { if (!_exiting) await ExitAsync(); };
        var theme = Services.GetRequiredService<IThemeService>();
        theme.SetTheme(preferences.Theme);
        ThemeTokens.Apply(MainWindow, theme);
        theme.ThemeChanged += (_, _) => ThemeTokens.Apply(MainWindow, theme);
        theme.PerformanceModeChanged += (_, _) => ThemeTokens.Apply(MainWindow, theme);

        // Try to restore session from saved refresh token
        var auth = Services.GetRequiredService<IClientAuthService>();
        var nav  = Services.GetRequiredService<INavigationService>();

        // Wire navigation events to MainWindow
        nav.NavigationRequested += (_, e) =>
        {
            switch (e.Page)
            {
                case "Login":  MainWindow.NavigateToLogin(); break;
                case "Main":   MainWindow.NavigateToMain();  break;
            }
        };

        var restored = await auth.TryRefreshAsync();
        if (restored)
            MainWindow.NavigateToMain();
        else
            MainWindow.NavigateToLogin();
    }

    public async Task ExitAsync()
    {
        if (_exiting) return; _exiting = true;
        Tray?.Dispose(); Tray = null;
        try { Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Unregister(); } catch { }
        try { await Services.GetRequiredService<IHubConnectionService>().DisconnectAsync(); await _host.StopAsync(); } catch { }
        MainWindow?.Close(); Exit();
    }

    private static IHost CreateHost()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.client.json", optional: false)
            .Build();

        return Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<IConfiguration>(config);
                services.AddSingleton<ServerEndpoint>();
                services.AddSingleton(ClientPreferences.Load());
                services.AddSingleton<ClientRequests>();

                // Core client services
                services.AddSingleton<IClientAuthService, ClientAuthService>();
                services.AddSingleton<IHubConnectionService, HubConnectionService>();
                services.AddSingleton<IDiscoveryService, DiscoveryService>();
                services.AddSingleton<IThemeService, ThemeService>();
                services.AddSingleton<INotificationService, NotificationService>();
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IApiService, ApiService>();

                // HTTP client — JWT injected via DelegatingHandler
                services.AddHttpClient("IFBServer", (sp, c) =>
                {
                    c.BaseAddress = sp.GetRequiredService<ServerEndpoint>().Address;
                    c.Timeout = TimeSpan.FromSeconds(30);
                }).ConfigurePrimaryHttpMessageHandler(ServerEndpoint.CreateHandler).AddHttpMessageHandler(sp =>
                    new AuthHeaderHandler(sp.GetRequiredService<IClientAuthService>()));
                services.AddHttpClient("IFBAuth", (sp, c) =>
                { c.BaseAddress = sp.GetRequiredService<ServerEndpoint>().Address; c.Timeout = TimeSpan.FromSeconds(30); })
                    .ConfigurePrimaryHttpMessageHandler(ServerEndpoint.CreateHandler);

                // ViewModels
                services.AddSingleton<MainViewModel>();
                services.AddTransient<LoginViewModel>();
                services.AddSingleton<ChatViewModel>();
                services.AddTransient<DirectoryViewModel>();
                services.AddTransient<SettingsViewModel>();
            })
            .Build();
    }

    private static void ConfigureWindow(MainWindow window)
    {
        // Get the AppWindow for advanced customization
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        // Set minimum size
        appWindow.ResizeClient(new Windows.Graphics.SizeInt32(1100, 700));

        // Custom title bar (ExtendsContentIntoTitleBar is set in MainWindow.xaml)
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = appWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(40, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(80, 255, 255, 255);
            titleBar.ButtonForegroundColor = Colors.White;
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(150, 200, 210, 230);
        }

        // Mica backdrop (Win11 native material)
        if (MicaController.IsSupported())
        {
            window.SystemBackdrop = new MicaBackdrop
            {
                Kind = MicaKind.BaseAlt
            };
        }
        else if (DesktopAcrylicController.IsSupported())
        {
            window.SystemBackdrop = new DesktopAcrylicBackdrop();
        }
    }

    private static bool IsPerformanceModeRequired()
    {
        // Check if running in RDP session
        if (GetSystemMetrics(SM_REMOTESESSION) != 0) return true;

        // Check Windows transparency effects setting
        if (!SystemParameters.TransparencyEffects) return true;

        return false;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    private const int SM_REMOTESESSION = 0x1000;
}

internal static class SystemParameters
{
    public static bool TransparencyEffects
    {
        get
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return (int)(key?.GetValue("EnableTransparency") ?? 1) == 1;
            }
            catch { return true; }
        }
    }
}

