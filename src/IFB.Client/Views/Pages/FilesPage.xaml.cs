using IFB.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
namespace IFB.Client.Views.Pages;
public sealed partial class FilesPage : Page
{
    private readonly ClientRequests _api = App.Services.GetRequiredService<ClientRequests>();
    private readonly ListView _files = new() { IsItemClickEnabled = true, ItemTemplate = Ui.Template("<Grid ColumnSpacing='16'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><Border Width='44' Height='44' CornerRadius='14' Background='{ThemeResource AccentSubtleBrush}'><FontIcon Glyph='&#xE8A5;' FontSize='20' Foreground='{ThemeResource AccentPrimaryBrush}'/></Border><StackPanel Grid.Column='1' Spacing='5'>" + Ui.Text("FileName", 16) + Ui.Text("UploadedByName", 12) + "</StackPanel><TextBlock Grid.Column='2' Text='{Binding SizeBytes, Converter={StaticResource FileSizeConverter}}' Style='{StaticResource CaptionStyle}' VerticalAlignment='Center'/></Grid>") };
    public FilesPage()
    {
        InitializeComponent(); Content = Ui.Layout("Files", Ui.Row(Ui.Button("Refresh files", LoadAsync)), _files, "Everything shared in your conversations. Select a file to save a verified copy.");
        Loaded += async (_, _) => { try { await LoadAsync(); } catch (Exception ex) { await Ui.Error(this, ex); } };
        _files.ItemClick += async (_, e) => { try { var f = (SharedFile)e.ClickedItem; var path = await Ui.SavePath(f.FileName, Path.GetExtension(f.FileName)); if (path != null) await _api.DownloadAsync(f, path); } catch (Exception ex) { await Ui.Error(this, ex); } };
    }
    private async Task LoadAsync() => _files.ItemsSource = await _api.GetAsync<List<SharedFile>>("api/v1/files");
}

