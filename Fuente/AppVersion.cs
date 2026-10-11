using System.Reflection;

namespace NitroPulse;

internal static class AppVersion
{
    public static Version Current { get; } = typeof(AppVersion).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
    public static string Display => Current.ToString(Current.Revision > 0 ? 4 : 3);
    public static string DefaultRepository => typeof(AppVersion).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "GitHubRepository")?.Value ?? "";
}
