using IFB.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
namespace IFB.Client.Views.Pages;
public sealed partial class ChannelsPage : Page
{
    private readonly ClientRequests _api = App.Services.GetRequiredService<ClientRequests>();
    private readonly ListView _channels = new() { IsItemClickEnabled = true, ItemTemplate = Ui.Template("<Grid ColumnSpacing='16'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><Border Background='{ThemeResource AccentSubtleBrush}' CornerRadius='14' Width='44' Height='44'><TextBlock Text='#' FontSize='24' Foreground='{ThemeResource AccentPrimaryBrush}' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><StackPanel Grid.Column='1' Spacing='5' VerticalAlignment='Center'>" + Ui.Text("Name", 16) + "<TextBlock Text='{Binding Description}' Style='{StaticResource CaptionStyle}' TextWrapping='Wrap' Visibility='{Binding Description, Converter={StaticResource NullOrEmptyToCollapsedConverter}}'/></StackPanel></Grid>") };
    public ChannelsPage()
    {
        InitializeComponent(); Content = Ui.Layout("Channels", Ui.Row(Ui.Button("Refresh channels", LoadAsync)), _channels, "A shared space for every department. Select a channel to join the conversation.");
        Loaded += async (_, _) => { try { await LoadAsync(); } catch (Exception ex) { await Ui.Error(this, ex); } };
        _channels.ItemClick += async (_, e) => { try { var c = (ChannelInfo)e.ClickedItem; await _api.SendAsync(HttpMethod.Post, $"api/v1/conversations/{c.Id}/join"); await App.Current.MainWindow!.OpenConversationAsync(c.Id); } catch (Exception ex) { await Ui.Error(this, ex); } };
    }
    private async Task LoadAsync() => _channels.ItemsSource = await _api.GetAsync<List<ChannelInfo>>("api/v1/conversations/channels");
}

