using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace IFB.Client.Views;
internal static class Ui
{
    public static TextBlock Title(string text) => new() { Text = text, Style = (Style)Application.Current.Resources["TitleStyle"], TextWrapping = TextWrapping.Wrap };
    public static TextBlock Caption(string text) => new() { Text = text, Style = (Style)Application.Current.Resources["CaptionStyle"], TextWrapping = TextWrapping.Wrap };
    public static Button Button(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Style = (Style)Application.Current.Resources["GlassButtonStyle"] };
        IFB.Client.Services.FluidMotion.Attach(button);
        button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } catch (Exception ex) { await Error(button, ex); } finally { button.IsEnabled = true; } };
        return button;
    }
    public static Grid Layout(string title, FrameworkElement toolbar, FrameworkElement body, string? description = null)
    {
        var grid = new Grid { Padding = new Thickness(32), RowSpacing = 24 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var heading = new StackPanel { Spacing = 8 }; heading.Children.Add(Title(title));
        if (description != null) heading.Children.Add(Caption(description));
        grid.Children.Add(heading);
        var tools = new ScrollViewer { Content = toolbar, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(tools, 1); grid.Children.Add(tools);
        UIElement content = body;
        if (body is ListView list)
        {
            var stack = new Grid(); stack.Children.Add(list);
            var status = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 12, Margin = new Thickness(24) };
            status.Children.Add(Caption("Loading your workspace…")); stack.Children.Add(status);
            list.RegisterPropertyChangedCallback(ItemsControl.ItemsSourceProperty, (_, _) => {
                var empty = list.Items.Count == 0;
                ((TextBlock)status.Children[0]).Text = title == "People" ? "No people match your search." : title == "Files" ? "No shared files yet. Attach a file in a conversation to get started." : "Nothing to show here yet.";
                status.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            });
            content = stack;
        }
        var card = Card(content); Grid.SetRow(card, 2); grid.Children.Add(card); return grid;
    }
    public static Border Card(UIElement content) => new() { Child = content, Style = (Style)Application.Current.Resources["GlassCardStyle"], Padding = new Thickness(12) };
    public static FrameworkElement SettingRow(string title, string description, FrameworkElement control)
    {
        var row = new Grid { Padding = new Thickness(12, 14, 12, 14), ColumnSpacing = 24 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["BodyStrongStyle"], TextWrapping = TextWrapping.Wrap });
        label.Children.Add(Caption(description)); row.Children.Add(label);
        control.VerticalAlignment = VerticalAlignment.Center;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, title);
        Grid.SetColumn(control, 1); row.Children.Add(control); return row;
    }
    public static StackPanel Section(string title, params UIElement[] rows)
    {
        var section = new StackPanel { Spacing = 10 };
        section.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["BodyStrongStyle"], Margin = new Thickness(4, 4, 0, 0) });
        var contents = new StackPanel(); foreach (var row in rows) contents.Children.Add(row);
        section.Children.Add(Card(contents)); return section;
    }
    public static StackPanel Row(params UIElement[] elements)
    { var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; foreach (var element in elements) panel.Children.Add(element); return panel; }
    public static DataTemplate Template(string fields) => (DataTemplate)XamlReader.Load(
        "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Spacing='6' Padding='16,12'>" + fields + "</StackPanel></DataTemplate>");
    public static string Text(string property, int size = 14) => $"<TextBlock Text='{{Binding {property}}}' FontSize='{size}' FontWeight='{(size >= 16 ? "SemiBold" : "Normal")}' Foreground='{{ThemeResource {(size >= 16 ? "TextPrimaryBrush" : "TextSecondaryBrush")}}}' TextWrapping='Wrap'/>";
    public static async Task Error(FrameworkElement owner, Exception exception)
    { try { await new ContentDialog { RequestedTheme = owner.ActualTheme, XamlRoot = owner.XamlRoot, Title = "Unable to complete action", Content = exception is HttpRequestException ? "The server could not complete this request. Check your connection and permissions." : exception.Message, CloseButtonText = "OK" }.ShowAsync(); } catch { } }
    public static async Task<string?> SavePath(string name, string extension)
    {
        var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(name) };
        picker.FileTypeChoices.Add("File", [string.IsNullOrEmpty(extension) ? ".bin" : extension]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.Current.MainWindow!));
        return (await picker.PickSaveFileAsync())?.Path;
    }
}

