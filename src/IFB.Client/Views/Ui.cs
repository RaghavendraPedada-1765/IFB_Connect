using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace IFB.Client.Views;
internal static class Ui
{
    public static TextBlock Title(string text) => new() { Text = text, FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) };
    public static Button Button(string text, Func<Task> action)
    {
        var button = new Button { Content = text, MinHeight = 36 };
        button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } catch (Exception ex) { await Error(button, ex); } finally { button.IsEnabled = true; } };
        return button;
    }
    public static Grid Layout(string title, FrameworkElement toolbar, FrameworkElement body)
    {
        var grid = new Grid { Padding = new Thickness(24), RowSpacing = 12 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(Title(title)); Grid.SetRow(toolbar, 1); grid.Children.Add(toolbar); Grid.SetRow(body, 2); grid.Children.Add(body); return grid;
    }
    public static StackPanel Row(params UIElement[] elements)
    { var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; foreach (var element in elements) panel.Children.Add(element); return panel; }
    public static DataTemplate Template(string fields) => (DataTemplate)XamlReader.Load(
        "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Spacing='4' Padding='12'>" + fields + "</StackPanel></DataTemplate>");
    public static string Text(string property, int size = 14) => $"<TextBlock Text='{{Binding {property}}}' FontSize='{size}' TextWrapping='Wrap'/>";
    public static async Task Error(FrameworkElement owner, Exception exception)
    { try { await new ContentDialog { XamlRoot = owner.XamlRoot, Title = "Unable to complete action", Content = exception is HttpRequestException ? "The server could not complete this request. Check your connection and permissions." : exception.Message, CloseButtonText = "OK" }.ShowAsync(); } catch { } }
    public static async Task<string?> SavePath(string name, string extension)
    {
        var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(name) };
        picker.FileTypeChoices.Add("File", [string.IsNullOrEmpty(extension) ? ".bin" : extension]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.Current.MainWindow!));
        return (await picker.PickSaveFileAsync())?.Path;
    }
}
