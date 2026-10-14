using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Globalization;
using System.Text.Json;
using Windows.UI;

namespace IFB.Client.Services;
public static class ThemeTokens
{
    private static IEnumerable<ResourceDictionary> Dictionaries(ResourceDictionary root)
    {
        yield return root;
        foreach (var child in root.MergedDictionaries)
            foreach (var dictionary in Dictionaries(child)) yield return dictionary;
    }
    public static bool AnimationsEnabled => new Windows.UI.ViewManagement.UISettings().AnimationsEnabled &&
        !(App.Services?.GetService(typeof(ClientPreferences)) is ClientPreferences { PerformanceMode: true });
    public static bool SolidMaterials => !new Windows.UI.ViewManagement.UISettings().AdvancedEffectsEnabled ||
        new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast ||
        App.Services?.GetService(typeof(ClientPreferences)) is ClientPreferences { PerformanceMode: true };
    public static Color Parse(string value)
    {
        if (value.Length != 7 || value[0] != '#' || !uint.TryParse(value[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            throw new ArgumentException("Enter a color as #RRGGBB.");
        return Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
    public static void SetAccent(string value)
    {
        var color = Parse(value);
        foreach (var dictionary in Dictionaries(Application.Current.Resources))
        {
            if (dictionary.TryGetValue("AccentPrimaryBrush", out var brushValue) && brushValue is SolidColorBrush brush) brush.Color = color;
        }
    }
    public static void Initialize(ClientPreferences preferences)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Themes", "theme.json")));
        SetAccent(preferences.Accent ?? document.RootElement.GetProperty("accent").GetProperty("primary").GetString()!);
        foreach (var dictionary in Dictionaries(Application.Current.Resources))
            foreach (var themeName in new[] { "Dark", "Light" })
                if (dictionary.ThemeDictionaries.TryGetValue(themeName, out var value) && value is ResourceDictionary themed)
                {
                    var tokens = document.RootElement.GetProperty(themeName.ToLowerInvariant());
                    foreach (var pair in new Dictionary<string, string> { ["Bg0"] = "bg0", ["Bg1"] = "bg1", ["Bg2"] = "bg2", ["Bg3"] = "bg3",
                        ["TextPrimary"] = "text", ["TextSecondary"] = "textSecondary", ["TextDisabled"] = "textDisabled" })
                    { var color = Parse(tokens.GetProperty(pair.Value).GetString()!);
                        if (themed.TryGetValue(pair.Key + "Brush", out var brushValue) && brushValue is SolidColorBrush brush) brush.Color = color; }
                    foreach (var pair in new Dictionary<string, string> { ["Glass1Brush"] = "bg1", ["Glass2Brush"] = "bg2", ["Glass3Brush"] = "bg3", ["GlassCardBrush"] = "bg2", ["GlassOverlayBrush"] = "bg0" })
                        if (themed.TryGetValue(pair.Key, out var glassValue) && glassValue is AcrylicBrush acrylic)
                        { acrylic.TintColor = Parse(tokens.GetProperty(pair.Value).GetString()!); acrylic.FallbackColor = acrylic.TintColor; acrylic.TintOpacity = tokens.GetProperty("glassOpacity").GetDouble(); }
                }
        // Value-type tokens are compiled in XAML. Mutate existing brushes instead
        // of inserting boxed Color/CornerRadius structs through the WinRT map.
    }
    public static void Apply(Window window, IThemeService theme)
    {
        if (window.Content is FrameworkElement root) root.RequestedTheme = theme.CurrentTheme switch
        { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        if (SolidMaterials) window.SystemBackdrop = null;
        else if (Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported()) window.SystemBackdrop = new DesktopAcrylicBackdrop();
        else if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported()) window.SystemBackdrop = new MicaBackdrop();
        void Update(ResourceDictionary dictionary)
        {
            foreach (var key in new[] { "Glass1Brush", "Glass2Brush", "Glass3Brush", "GlassCardBrush", "GlassOverlayBrush" })
                if (dictionary.TryGetValue(key, out var value) && value is AcrylicBrush acrylic) acrylic.AlwaysUseFallback = SolidMaterials;
            foreach (var merged in dictionary.MergedDictionaries) Update(merged);
            foreach (var themed in dictionary.ThemeDictionaries.Values.OfType<ResourceDictionary>()) Update(themed);
        }
        Update(Application.Current.Resources);
    }
}
