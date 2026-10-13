using IFB.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
namespace IFB.Client.Views.Pages;
public sealed partial class ChannelsPage : Page
{
    private readonly ClientRequests _api = App.Services.GetRequiredService<ClientRequests>();
    private readonly ListView _channels = new() { IsItemClickEnabled = true, ItemTemplate = Ui.Template(Ui.Text("Name", 18) + Ui.Text("Description")) };
    public ChannelsPage()
    {
        InitializeComponent(); Content = Ui.Layout("Department channels", Ui.Row(new TextBlock { Text = "Select a channel to join and open it." }, Ui.Button("Refresh", LoadAsync)), _channels);
        Loaded += async (_, _) => { try { await LoadAsync(); } catch (Exception ex) { await Ui.Error(this, ex); } };
        _channels.ItemClick += async (_, e) => { try { var c = (ChannelInfo)e.ClickedItem; await _api.SendAsync(HttpMethod.Post, $"api/v1/conversations/{c.Id}/join"); await App.Current.MainWindow!.OpenConversationAsync(c.Id); } catch (Exception ex) { await Ui.Error(this, ex); } };
    }
    private async Task LoadAsync() => _channels.ItemsSource = await _api.GetAsync<List<ChannelInfo>>("api/v1/conversations/channels");
}
