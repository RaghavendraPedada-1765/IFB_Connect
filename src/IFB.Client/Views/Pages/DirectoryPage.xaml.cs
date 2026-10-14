using IFB.Client.Services;
using IFB.Shared.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace IFB.Client.Views.Pages;
public sealed partial class DirectoryPage : Page
{
    private readonly IApiService _api = App.Services.GetRequiredService<IApiService>();
    private readonly ListView _people = new() { IsItemClickEnabled = true,
        ItemTemplate = Ui.Template("<Grid ColumnSpacing='16'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>" +
            "<PersonPicture DisplayName='{Binding DisplayName}' Width='44' Height='44'/>" +
            "<StackPanel Grid.Column='1' Spacing='4' VerticalAlignment='Center'>" + Ui.Text("DisplayName", 16) +
            "<TextBlock Text='{Binding Department}' Style='{StaticResource CaptionStyle}' Visibility='{Binding Department, Converter={StaticResource NullOrEmptyToCollapsedConverter}}'/>" +
            "<TextBlock Text='{Binding Title}' Style='{StaticResource CaptionStyle}' Visibility='{Binding Title, Converter={StaticResource NullOrEmptyToCollapsedConverter}}'/></StackPanel>" +
            "<TextBlock Grid.Column='2' Text='{Binding Presence}' Style='{StaticResource CaptionStyle}' VerticalAlignment='Center'/></Grid>") };
    private readonly TextBox _search = new() { PlaceholderText = "Search employees", MinWidth = 220 };
    private readonly ComboBox _department = new() { PlaceholderText = "Department", MinWidth = 130 };
    public DirectoryPage()
    {
        InitializeComponent();
        Content = Ui.Layout("People", Ui.Row(_search, _department, Ui.Button("Search", LoadAsync), Ui.Button("New group", CreateGroupAsync)), _people, "Find a colleague. Start a conversation. Bring your team together.");
        Loaded += async (_, _) => { try { _department.Items.Add("All departments"); foreach (var d in await _api.GetDepartmentsAsync()) _department.Items.Add(d); _department.SelectedIndex = 0; await LoadAsync(); } catch (Exception ex) { await Ui.Error(this, ex); } };
        _people.ItemClick += async (_, e) => { try { var user = (UserDto)e.ClickedItem; if (user.Id == App.Services.GetRequiredService<IClientAuthService>().CurrentUser?.Id) return;
            var conv = await _api.CreateDmAsync(user.Id); await App.Current.MainWindow!.OpenConversationAsync(conv.Id); } catch (Exception ex) { await Ui.Error(this, ex); } };
    }
    private async Task LoadAsync() => _people.ItemsSource = (await _api.SearchUsersAsync(_search.Text,
        _department.SelectedIndex > 0 ? _department.SelectedItem?.ToString() : null, 200)).Users;
    private async Task CreateGroupAsync()
    {
        var name = new TextBox { Header = "Group name", MaxLength = 128 };
        var members = new ListView { SelectionMode = ListViewSelectionMode.Multiple, MaxHeight = 300,
            ItemTemplate = Ui.Template(Ui.Text("DisplayName") + Ui.Text("Department", 12)),
            ItemsSource = (await _api.SearchUsersAsync(null, null, 200)).Users.Where(u => u.Id != App.Services.GetRequiredService<IClientAuthService>().CurrentUser?.Id).ToList() };
        var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(name); panel.Children.Add(members);
        var dialog = new ContentDialog { RequestedTheme = ActualTheme, XamlRoot = XamlRoot, Title = "Create group", Content = panel, PrimaryButtonText = "Create", CloseButtonText = "Cancel" };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        { var conv = await _api.CreateGroupAsync(name.Text, null, members.SelectedItems.Cast<UserDto>().Select(u => u.Id).ToList()); await App.Current.MainWindow!.OpenConversationAsync(conv.Id); }
    }
}

