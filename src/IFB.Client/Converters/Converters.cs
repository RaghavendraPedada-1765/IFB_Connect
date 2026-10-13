using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace IFB.Client.Converters;
public sealed class MessageBubbleBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var dark = (App.Current.MainWindow?.Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark;
        var color = value is true ? (dark ? "#274A39" : "#E1F2CF") : (dark ? "#243247" : "#F8FBFF");
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Services.ThemeTokens.Parse(color));
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
public sealed class MessageAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is true ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>Returns Visible when value is true, Collapsed when false.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility.Visible;
}

/// <summary>Returns Collapsed when value is true (inverted).</summary>
public sealed class InvertedBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility.Collapsed;
}

/// <summary>Formats DateTime to a human-friendly short string (Today, Yesterday, or date).</summary>
public sealed class TimestampConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not DateTime dt) return string.Empty;
        var local = dt.ToLocalTime();
        var now = DateTime.Now;

        if (local.Date == now.Date)
            return local.ToString("HH:mm");
        if (local.Date == now.Date.AddDays(-1))
            return "Yesterday";
        if ((now - local).TotalDays < 7)
            return local.ToString("ddd");
        return local.ToString("dd/MM/yy");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>Hides element when string is null or empty.</summary>
public sealed class NullOrEmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>Returns "0" display as collapsed (unread badge).</summary>
public sealed class UnreadCountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>Caps unread count display at 99+.</summary>
public sealed class UnreadCountDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is int count ? (count > 99 ? "99+" : count.ToString()) : "0";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
