using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32;

namespace NitroPulse;

internal static class DependencyBootstrap
{
    internal const string InstallerHash = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";
    internal enum Result { Safe, Direct, Relaunched }

    internal static Version? InstalledVersion()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            if (Version.TryParse(key?.GetValue("DisplayVersion") as string, out var version)) return version;
        }
        return null;
    }

    internal static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal static bool InstallerValid(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)) == InstallerHash;

    internal static byte[] InstallerBytes()
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("NitroPulse.PawnIO_setup.exe")
            ?? throw new IOException("No se encuentra el instalador incluido de PawnIO.");
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        var bytes = buffer.ToArray();
        if (!InstallerValid(bytes)) throw new IOException("El instalador de PawnIO no supera la verificación de integridad.");
        return bytes;
    }

    internal static Result Prepare(string[] args)
    {
        bool Has(string flag) => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        if (Has("--safe-mode")) return Result.Safe;
        try
        {
            var installed = InstalledVersion();
            if (installed is not null && installed < new Version(2, 2))
            {
                MessageBox.Show($"PawnIO {installed} está instalado. Esta versión requiere PawnIO 2.2 o posterior. Actualízalo desde su sitio oficial. NitroPulse continuará en modo seguro.",
                    "NitroPulse · PawnIO", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return Result.Safe;
            }
            bool setup = installed is null;
            if (setup && !Has("--setup-pawnio"))
            {
                if (MessageBox.Show("Para mostrar la temperatura del CPU, NitroPulse necesita PawnIO.\n\n¿Instalar PawnIO 2.2 oficial firmado, incluido en este ejecutable? Es un controlador de Windows y requiere autorización de administrador.\n\nSi eliges No, NitroPulse abrirá en modo seguro sin lectura directa del CPU.",
                    "NitroPulse · Instalar componente de sensores", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes) return Result.Safe;
            }
            if (!IsElevated())
            {
                var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new IOException("No se encuentra NitroPulse.exe."))
                { UseShellExecute = true, Verb = "runas" };
                start.ArgumentList.Add("--bootstrap-relaunch");
                if (setup) start.ArgumentList.Add("--setup-pawnio");
                if (Has("--minimized")) start.ArgumentList.Add("--minimized");
                using var child = Process.Start(start) ?? throw new IOException("No se pudo abrir NitroPulse como administrador.");
                return Result.Relaunched;
            }
            if (setup)
            {
                var bytes = InstallerBytes();
                string folder = Path.Combine(Path.GetTempPath(), "NitroPulse-PawnIO-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                string installer = Path.Combine(folder, "PawnIO_setup.exe");
                File.WriteAllBytes(installer, bytes);
                bool finished = false;
                try
                {
                    // Official edition: no unrestricted/debug flags, no network or global security changes.
                    using var process = Process.Start(new ProcessStartInfo(installer, "-install -silent")
                    { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })
                        ?? throw new IOException("No se pudo iniciar el instalador de PawnIO.");
                    finished = process.WaitForExit(60000);
                    if (!finished) throw new IOException("La instalación sigue en curso. NitroPulse abrirá en modo seguro; vuelve a abrirlo cuando termine.");
                    if (process.ExitCode != 0 || InstalledVersion() is not { } version || version < new Version(2, 2))
                        throw new IOException($"PawnIO no quedó instalado correctamente (código {process.ExitCode}).");
                }
                finally
                {
                    // Remove only the installer file this invocation created, after it has exited.
                    if (finished) { try { File.Delete(installer); Directory.Delete(folder); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
                }
            }
            return Result.Direct;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { return Result.Safe; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception or System.Security.SecurityException)
        {
            MessageBox.Show(e.Message + "\n\nNitroPulse continuará en modo seguro.", "NitroPulse · Sensores", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return Result.Safe;
        }
    }
}
