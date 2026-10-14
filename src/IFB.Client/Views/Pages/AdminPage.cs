using IFB.Client.Services;
using IFB.Shared.DTOs;
using IFB.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;

namespace IFB.Client.Views.Pages;
public record AdminUser(string Id, string Username, string DisplayName, string Email, string? Department, string? Title, UserRole Role, bool IsActive);
public record AdminConversation(string Id, string Name, ConversationType Type, string? Description, int RetentionDays, bool IsArchived, int Members);
public record AuditEntry(string Id, string UserId, string Action, string Details, string? IpAddress, DateTime Timestamp);
public sealed class AdminPage : Page
{
    private readonly ClientRequests _api = App.Services.GetRequiredService<ClientRequests>();
    private readonly ComboBox _section = new() { ItemsSource = new[] { "Users", "Groups and channels", "Announcements", "Audit log", "Connected clients" }, SelectedIndex = 0, MinWidth = 180 };
    private readonly ListView _list = new();
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
    public AdminPage()
    {
        if (App.Services.GetRequiredService<IClientAuthService>().CurrentUser?.Role != UserRole.Admin) { Content = Ui.Title("Administrator access required"); return; }
        Content = Ui.Layout("Administration", Ui.Row(_section, Ui.Button("Refresh", LoadAsync), _actions), _list, "Manage your people, shared spaces and company announcements.");
        _section.SelectionChanged += async (_, _) => { try { await LoadAsync(); } catch (Exception ex) { await Ui.Error(this, ex); } };
        Loaded += async (_, _) => { try { await LoadAsync(); } catch (Exception ex) { await Ui.Error(this, ex); } };
    }
    private async Task LoadAsync()
    {
        _actions.Children.Clear();
        switch (_section.SelectedIndex)
        {
            case 0:
                _list.ItemTemplate = Ui.Template(Ui.Text("DisplayName", 16) + Ui.Text("Username") + Ui.Text("Role") + Ui.Text("IsActive", 12));
                _list.ItemsSource = await _api.GetAsync<List<AdminUser>>("api/v1/admin/users");
                _actions.Children.Add(Ui.Button("Create user", CreateUserAsync)); _actions.Children.Add(Ui.Button("Edit selected", EditUserAsync));
                _actions.Children.Add(Ui.Button("Force logout", async () => { if (_list.SelectedItem is AdminUser u) await _api.SendAsync(HttpMethod.Post, $"api/v1/admin/users/{u.Id}/logout"); })); break;
            case 1:
                _list.ItemTemplate = Ui.Template(Ui.Text("Name", 16) + Ui.Text("Type") + Ui.Text("RetentionDays", 12));
                _list.ItemsSource = await _api.GetAsync<List<AdminConversation>>("api/v1/admin/conversations");
                _actions.Children.Add(Ui.Button("Create channel", CreateChannelAsync)); _actions.Children.Add(Ui.Button("Edit selected", EditConversationAsync));
                _actions.Children.Add(Ui.Button("Members", async () => { if (_list.SelectedItem is AdminConversation c) await App.Current.MainWindow!.ManageMembersAsync(c.Id); })); break;
            case 2:
                _list.ItemTemplate = Ui.Template(Ui.Text("Title", 16) + Ui.Text("Content") + Ui.Text("Priority", 12));
                _list.ItemsSource = await _api.GetAsync<List<AnnouncementDto>>("api/v1/announcements?active=false");
                _actions.Children.Add(Ui.Button("Post announcement", PostAnnouncementAsync));
                _actions.Children.Add(Ui.Button("Delete selected", async () => { if (_list.SelectedItem is AnnouncementDto a) { await _api.SendAsync(HttpMethod.Delete, $"api/v1/announcements/{a.Id}"); await LoadAsync(); } })); break;
            case 3:
                _list.ItemTemplate = Ui.Template(Ui.Text("Timestamp") + Ui.Text("Action") + Ui.Text("UserId", 12) + Ui.Text("Details", 12));
                _list.ItemsSource = await _api.GetAsync<List<AuditEntry>>("api/v1/admin/audit");
                _actions.Children.Add(Ui.Button("Export CSV", async () => { var path = await Ui.SavePath("ifb-audit.csv", ".csv"); if (path == null) return;
                    using var client = App.Services.GetRequiredService<IHttpClientFactory>().CreateClient("IFBServer"); await File.WriteAllBytesAsync(path, await client.GetByteArrayAsync("api/v1/admin/audit/export")); })); break;
            default:
                _list.ItemTemplate = null;
                var connections = await _api.GetAsync<List<JsonElement>>("api/v1/admin/connections");
                _list.ItemsSource = connections.Select(c => $"{c.GetProperty("userId").GetString()} — {c.GetProperty("connections").GetInt32()} connections").ToList(); break;
        }
    }
    private async Task<bool> Form(string title, params UIElement[] controls)
    { var p = new StackPanel { Spacing = 12 }; foreach (var control in controls) p.Children.Add(control);
        return await new ContentDialog { RequestedTheme = ActualTheme, XamlRoot = XamlRoot, Title = title, Content = new ScrollViewer { Content = p }, PrimaryButtonText = "Save", CloseButtonText = "Cancel" }.ShowAsync() == ContentDialogResult.Primary; }
    private async Task CreateUserAsync()
    {
        var username = new TextBox { Header = "Username" }; var name = new TextBox { Header = "Display name" }; var email = new TextBox { Header = "Email" };
        var password = new PasswordBox { Header = "Password (12+ characters)" }; var role = new ComboBox { Header = "Role", ItemsSource = Enum.GetValues<UserRole>(), SelectedIndex = 0 };
        if (await Form("Create local account", username, name, email, password, role))
        { await _api.SendAsync(HttpMethod.Post, "api/v1/users", new { username = username.Text, password = password.Password, displayName = name.Text, email = email.Text, role = (UserRole)role.SelectedItem }); await LoadAsync(); }
    }
    private async Task EditUserAsync()
    {
        if (_list.SelectedItem is not AdminUser u) return;
        var displayName = new TextBox { Header = "Display name", Text = u.DisplayName, MaxLength = 128 };
        var role = new ComboBox { Header = "Role", ItemsSource = Enum.GetValues<UserRole>(), SelectedItem = u.Role };
        var active = new ToggleSwitch { Header = "Account enabled", IsOn = u.IsActive };
        var department = new TextBox { Header = "Department", Text = u.Department ?? "" }; var title = new TextBox { Header = "Job title", Text = u.Title ?? "" };
        var password = new PasswordBox { Header = "New local password (leave blank to keep current)" };
        if (await Form("Edit " + u.DisplayName, displayName, role, active, department, title, password))
        { await _api.SendAsync(HttpMethod.Patch, $"api/v1/admin/users/{u.Id}", new { displayName = displayName.Text, role = (UserRole)role.SelectedItem, isActive = active.IsOn, department = department.Text, title = title.Text, newPassword = string.IsNullOrEmpty(password.Password) ? null : password.Password }); await LoadAsync(); }
    }
    private async Task CreateChannelAsync()
    { var name = new TextBox { Header = "Channel name" }; if (await Form("Create department channel", name)) { await _api.SendAsync(HttpMethod.Post, "api/v1/admin/channels", new { name = name.Text, retentionDays = 90 }); await LoadAsync(); } }
    private async Task EditConversationAsync()
    { if (_list.SelectedItem is not AdminConversation c) return; var name = new TextBox { Header = "Name", Text = c.Name }; var days = new NumberBox { Header = "Retention days", Value = c.RetentionDays, Minimum = 1, Maximum = 3650 };
        var archived = new ToggleSwitch { Header = "Archived", IsOn = c.IsArchived };
        if (await Form("Edit conversation", name, days, archived)) { await _api.SendAsync(HttpMethod.Patch, $"api/v1/admin/conversations/{c.Id}", new { name = name.Text, retentionDays = (int)days.Value, isArchived = archived.IsOn }); await LoadAsync(); } }
    private async Task PostAnnouncementAsync()
    { var title = new TextBox { Header = "Title" }; var content = new TextBox { Header = "Message", AcceptsReturn = true, Height = 140 }; var priority = new ComboBox { Header = "Priority", ItemsSource = Enum.GetValues<AnnouncementPriority>(), SelectedIndex = 0 };
        if (await Form("Broadcast announcement", title, content, priority)) { await _api.SendAsync(HttpMethod.Post, "api/v1/announcements", new { title = title.Text, content = content.Text, priority = (AnnouncementPriority)priority.SelectedItem }); await LoadAsync(); } }
}

