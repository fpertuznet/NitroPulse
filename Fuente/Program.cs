using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace NitroPulse;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var args = Environment.GetCommandLineArgs();
        int checkArg = Array.FindIndex(args, a => a.Equals("--check-dependencies", StringComparison.OrdinalIgnoreCase));
        if (checkArg >= 0 && checkArg + 1 < args.Length)
        {
            var original = DependencyBootstrap.InstallerBytes();
            bool intact = DependencyBootstrap.InstallerValid(original);
            original[0] ^= 1;
            bool alteredRejected = !DependencyBootstrap.InstallerValid(original);
            var safe = DependencyBootstrap.Prepare(new[] { "--safe-mode", "--setup-pawnio" }) == DependencyBootstrap.Result.Safe;
            File.WriteAllLines(Path.GetFullPath(args[checkArg + 1]), new[] {
                "NitroPulse " + AppVersion.Display + " · comprobación sin instalación",
                "PawnIO instalado: " + (DependencyBootstrap.InstalledVersion()?.ToString() ?? "No"),
                "Administrador: " + DependencyBootstrap.IsElevated(),
                "Instalador incluido íntegro: " + intact,
                "Instalador alterado rechazado: " + alteredRejected,
                "Modo seguro prevalece y evita instalación/elevación: " + safe,
                "SHA256 esperado: " + DependencyBootstrap.InstallerHash
            });
            Environment.ExitCode = intact && alteredRejected && safe ? 0 : 1;
            return;
        }
        int reportArg = Array.FindIndex(args, a => a.Equals("--sensor-report", StringComparison.OrdinalIgnoreCase));
        if (reportArg >= 0 && reportArg + 1 < args.Length)
        {
            // Bounded diagnostic run, separate from the tray singleton.
            bool direct = args.Any(a => a.Equals("--direct-sensors", StringComparison.OrdinalIgnoreCase)) &&
                !args.Any(a => a.Equals("--safe-mode", StringComparison.OrdinalIgnoreCase));
            using var hardware = new HardwareReader(safeMode: !direct);
            var readings = new System.Text.StringBuilder();
            for (int i = 0; i < 3; i++)
            {
                var value = hardware.Read();
                readings.AppendLine($"Lectura {i + 1}: CPU={value.CpuTemperature?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "vacío"} °C; GPU={value.GpuTemperature?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "vacío"} °C; fuente={value.CpuSource}; estado={value.Status}");
                if (i < 2) Thread.Sleep(2000);
            }
            File.WriteAllText(Path.GetFullPath(args[reportArg + 1]), readings + hardware.SensorReport());
            return;
        }
        using var singleton = new Mutex(true, "Local\\NitroPulse_Monitor_2026", out bool firstInstance);
        if (!firstInstance && args.Any(a => a.Equals("--bootstrap-relaunch", StringComparison.OrdinalIgnoreCase)))
        {
            try { firstInstance = singleton.WaitOne(10000); }
            catch (AbandonedMutexException) { firstInstance = true; }
        }
        if (!firstInstance)
        {
            MessageBox.Show(
                "NitroPulse ya está ejecutándose. Busca sus indicadores junto al reloj de Windows o en la flecha ^.",
                "NitroPulse", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        var bootstrap = DependencyBootstrap.Prepare(args);
        if (bootstrap == DependencyBootstrap.Result.Relaunched)
        {
            singleton.ReleaseMutex();
            return;
        }
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            try { MessageBox.Show(e.Exception.Message, "Error de NitroPulse", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            catch { /* El equipo puede estar apagándose. */ }
        };
        Application.Run(new MonitorForm(safeMode: bootstrap != DependencyBootstrap.Result.Direct));
    }
}
