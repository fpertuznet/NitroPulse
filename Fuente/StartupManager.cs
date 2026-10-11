using Microsoft.Win32;

namespace NitroPulse;

internal static class StartupManager
{
    private const string Name = "NitroPulse";
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(Name) is string current && string.Equals(current, ExecutableCommand(), StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        string? path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) || Path.GetFileName(path).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Publica NitroPulse.exe antes de habilitar el inicio automático.");
        using var key = Registry.CurrentUser.OpenSubKey(Key, writable: true)
            ?? throw new InvalidOperationException("No se pudo abrir la clave de inicio del usuario.");
        if (enabled) key.SetValue(Name, ExecutableCommand(), RegistryValueKind.String);
        else key.DeleteValue(Name, throwOnMissingValue: false);
    }

    private static string ExecutableCommand() => "\"" + Environment.ProcessPath + "\" --minimized";
}
