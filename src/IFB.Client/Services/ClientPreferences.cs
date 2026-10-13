using System.Text.Json;

namespace IFB.Client.Services;
public sealed class ClientPreferences
{
    private static readonly string FilePath = Path.Combine(ClientProfile.DataDirectory, "preferences.json");
    public string Theme { get; set; } = "Auto";
    public string? Server { get; set; }
    public bool Notifications { get; set; } = true;
    public bool Sounds { get; set; } = true;
    public bool DoNotDisturb { get; set; }
    public bool PerformanceMode { get; set; }
    public bool CloseToTray { get; set; } = true;
    public string? Accent { get; set; }
    public static ClientPreferences Load()
    { try { return JsonSerializer.Deserialize<ClientPreferences>(File.ReadAllText(FilePath)) ?? new(); } catch { return new(); } }
    public void Save() { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllText(FilePath, JsonSerializer.Serialize(this)); }
}
