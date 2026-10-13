using System.Text.RegularExpressions;

namespace IFB.Client.Services;

public static class ClientProfile
{
    public static string Name { get; } = ReadName();
    public static bool IsDefault => Name == "default";
    public static string DataDirectory => IsDefault
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IFBConnect")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IFBConnect", "profiles", Name);
    private static string ReadName()
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--profile");
        if (index < 0) return "default";
        if (index + 1 >= args.Length || !Regex.IsMatch(args[index + 1], "^[a-zA-Z0-9_-]{1,32}$"))
            throw new ArgumentException("Profile names must contain 1–32 letters, numbers, underscores or hyphens.");
        return args[index + 1].ToLowerInvariant();
    }
}
