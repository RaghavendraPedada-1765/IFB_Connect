using IFB.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
namespace IFB.Client.Views.Pages;
public sealed partial class FilesPage : Page
{
    private readonly ClientRequests _api = App.Services.GetRequiredService<ClientRequests>();
    private readonly ListView _files = new() { IsItemClickEnabled = true, ItemTemplate = Ui.Template(Ui.Text("FileName", 16) + Ui.Text("UploadedByName") + Ui.Text("SizeBytes", 12)) };
    public FilesPage()
    {
        InitializeComponent(); Content = Ui.Layout("Shared files", Ui.Row(new TextBlock { Text = "Select a file to save a verified copy." }, Ui.Button("Refresh", LoadAsync)), _files);
        Loaded += async (_, _) => { try { await LoadAsync(); } catch (Exception ex) { await Ui.Error(this, ex); } };
        _files.ItemClick += async (_, e) => { try { var f = (SharedFile)e.ClickedItem; var path = await Ui.SavePath(f.FileName, Path.GetExtension(f.FileName)); if (path != null) await _api.DownloadAsync(f, path); } catch (Exception ex) { await Ui.Error(this, ex); } };
    }
    private async Task LoadAsync() => _files.ItemsSource = await _api.GetAsync<List<SharedFile>>("api/v1/files");
}
