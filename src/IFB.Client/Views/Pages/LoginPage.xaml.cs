using IFB.Client.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;

namespace IFB.Client.Views.Pages;

public sealed partial class LoginPage : Page
{
    private readonly LoginViewModel _vm;

    public LoginPage()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<LoginViewModel>();
        DataContext = _vm;
        SizeChanged += (_, e) =>
        {
            var compact = e.NewSize.Width < 900;
            BrandPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            BrandColumn.Width = compact ? new GridLength(0) : new GridLength(.9, GridUnitType.Star);
        };
        IFB.Client.Services.FluidMotion.Attach(LoginButton);

        // Keep PasswordBox in sync — PasswordBox.Password can't be data-bound
        _vm.PropertyChanged += (_, e) =>
        {
            LoginButton.IsEnabled = !_vm.IsLoading;
            if (e.PropertyName == nameof(LoginViewModel.Password) &&
                PasswordBox.Password != _vm.Password)
                PasswordBox.Password = _vm.Password;
        };

        // Observe ErrorMessage to show/shake the error panel
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LoginViewModel.ErrorMessage))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_vm.ErrorMessage != null)
                    {
                        ErrorLabel.Text = _vm.ErrorMessage;
                        ErrorBorder.Visibility = Visibility.Visible;
                        PlayShakeAnimation();
                    }
                    else
                    {
                        ErrorBorder.Visibility = Visibility.Collapsed;
                    }
                });
            }
        };

        // Auto-discover on load
        _ = _vm.DiscoverServerCommand.ExecuteAsync(null);
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // Bridge the one-way binding gap — PasswordBox doesn't support {Binding}
        if (_vm != null) _vm.Password = PasswordBox.Password;
    }

    private async void Login_Click(object sender, RoutedEventArgs e) =>
        await ExecuteLoginAsync();

    private async void InputField_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
            await ExecuteLoginAsync();
    }

    private async Task ExecuteLoginAsync()
    {
        if (_vm.IsLoading) return;
        await _vm.LoginCommand.ExecuteAsync(null);
        if (App.Services.GetRequiredService<Services.IClientAuthService>().IsAuthenticated) PasswordBox.Password = "";
    }

    private async void ManualConfig_Click(object sender, RoutedEventArgs e)
    {
        var input = new TextBox { Header = "Company server address", Text = _vm.ServerAddress };
        var dialog = new ContentDialog { RequestedTheme = ActualTheme, XamlRoot = XamlRoot, Title = "Connect to server", Content = input,
            PrimaryButtonText = "Save", CloseButtonText = "Cancel" };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) _vm.ServerAddress = input.Text;
    }

    private async void WindowsLogin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            App.Services.GetRequiredService<Services.ServerEndpoint>().SetAddress(_vm.ServerAddress);
            if (await App.Services.GetRequiredService<Services.IClientAuthService>().WindowsLoginAsync())
                App.Current.MainWindow!.NavigateToMain();
            else _vm.ErrorMessage = "Windows sign-in is unavailable. Use your company username and password.";
        }
        catch (Exception ex) { await Ui.Error(this, ex); }
    }

    private void PlayShakeAnimation() { /* Keep inline errors steady and readable. */ }
}


