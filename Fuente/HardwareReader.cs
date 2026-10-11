using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;

namespace NitroPulse;

/// <summary>
/// Reads physical sensors using the OFFICIAL LibreHardwareMonitorLib 0.9.6 NuGet package.
/// Missing sensor readings remain null (never guessed from ACPI zones).
/// </summary>
internal sealed class HardwareReader : IDisposable
{
    private readonly object _sync = new();
    private Computer? _computer;
    private string _error = "";
    private string? _nvidiaSmiPath;
    private DateTime _nextFallback = DateTime.MinValue;
    private double? _lastNvLoad;
    private double? _lastNvTemp;
    private DateTime _nextCpuFallback = DateTime.MinValue;
    private double? _lastExternalCpuTemp;
    private string _lastExternalCpuSource = "";

    public bool IsElevated { get; }
    public bool SafeMode { get; }
    public bool PawnIoInstalled => LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;

    public HardwareReader(bool safeMode = false)
    {
        SafeMode = safeMode;
        using var identity = WindowsIdentity.GetCurrent();
        IsElevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        if (safeMode)
        {
            _error = "Modo seguro: lectura directa de sensores deshabilitada";
        }
        else try
        {
            _computer = new Computer
            {
                IsCpuEnabled = PawnIoInstalled,
                IsGpuEnabled = true,
                IsMemoryEnabled = false,
                IsMotherboardEnabled = false,
                IsControllerEnabled = false,
                IsNetworkEnabled = false,
                IsStorageEnabled = false
            };
            _computer.Open();
        }
        catch (Exception e)
        {
            _error = "Sensores: " + e.Message;
            try { _computer?.Close(); } catch { }
            _computer = null;
        }
        _nvidiaSmiPath = FindNvidiaSmi();
    }

    public HardwareReading Read()
    {
        lock (_sync)
        {
            double? cpuTemp = null, gpuTemp = null, gpuUsage = null;
            string cpuSource = "", gpuTempSource = "", gpuLoadSource = "";
            try
            {
                if (_computer is not null)
                {
                    _computer.Accept(new UpdateVisitor());
                    var all = _computer.Hardware.SelectMany(EnumerateHardware).ToArray();
                    var cpu = all.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
                    if (cpu is not null)
                    {
                        var temps = cpu.Sensors.Where(s => s.SensorType == SensorType.Temperature && Valid(s.Value)).ToArray();
                        var target = temps.FirstOrDefault(s => HasName(s, "CPU Package"))
                            ?? temps.FirstOrDefault(s => HasName(s, "Package"))
                            ?? temps.FirstOrDefault(s => HasName(s, "Core Max"))
                            ?? temps.FirstOrDefault(s => HasName(s, "Core Average"))
                            ?? temps.FirstOrDefault(s => HasName(s, "Core"))
                            ?? temps.FirstOrDefault();
                        if (target is not null)
                        {
                            cpuTemp = target.Value;
                            cpuSource = cpu.Name + ": " + target.Name;
                        }
                        else
                        {
                            var anyCpuTemp = all
                                .SelectMany(h => h.Sensors.Select(s => (Hardware: h, Sensor: s)))
                                .Where(x => x.Sensor.SensorType == SensorType.Temperature && Valid(x.Sensor.Value))
                                .FirstOrDefault(x => x.Hardware.HardwareType == HardwareType.Cpu || HasName(x.Sensor, "CPU") || HasName(x.Sensor, "Package"));
                            if (anyCpuTemp.Sensor is not null)
                            {
                                cpuTemp = anyCpuTemp.Sensor.Value;
                                cpuSource = anyCpuTemp.Hardware.Name + ": " + anyCpuTemp.Sensor.Name;
                            }
                        }
                    }

                    // Prefer discrete NVIDIA GPU; hybrid Intel graphics may also be present.
                    var gpu = all.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia)
                        ?? all.FirstOrDefault(h => h.HardwareType == HardwareType.GpuAmd)
                        ?? all.FirstOrDefault(h => h.HardwareType == HardwareType.GpuIntel);
                    if (gpu is not null)
                    {
                        var temps = gpu.Sensors.Where(s => s.SensorType == SensorType.Temperature && Valid(s.Value)).ToArray();
                        var t = temps.FirstOrDefault(s => HasName(s, "GPU Core"))
                            ?? temps.FirstOrDefault(s => HasName(s, "Core"))
                            ?? temps.FirstOrDefault(s => !HasName(s, "Hot Spot"))
                            ?? temps.FirstOrDefault();
                        if (t is not null)
                        {
                            gpuTemp = t.Value;
                            gpuTempSource = gpu.Name + ": " + t.Name;
                        }

                        var loads = gpu.Sensors.Where(s => s.SensorType == SensorType.Load && ValidLoad(s.Value)).ToArray();
                        var u = loads.FirstOrDefault(s => HasName(s, "GPU Core"))
                            ?? loads.FirstOrDefault(s => HasName(s, "GPU 3D"))
                            ?? loads.FirstOrDefault(s => HasName(s, "GPU Total"))
                            ?? loads.FirstOrDefault(s => HasName(s, "GPU") && !HasName(s, "Memory"))
                            ?? loads.FirstOrDefault(s => HasName(s, "3D"));
                        if (u is not null)
                        {
                            gpuUsage = u.Value;
                            gpuLoadSource = gpu.Name + ": " + u.Name;
                        }
                    }
                    _error = "";
                }
            }
            catch (Exception e) { _error = "Error de sensor: " + e.Message; }

            // NVIDIA fallback only if the sensor library did not expose the needed value.
            if ((gpuUsage is null || gpuTemp is null) && _nvidiaSmiPath is not null)
            {
                if (DateTime.UtcNow >= _nextFallback)
                {
                    _nextFallback = DateTime.UtcNow.AddSeconds(6);
                    var nv = NvidiaSmiRead(_nvidiaSmiPath);
                    _lastNvLoad = nv.Load;
                    _lastNvTemp = nv.Temperature;
                }
                if (gpuUsage is null && _lastNvLoad is not null)
                {
                    gpuUsage = _lastNvLoad;
                    gpuLoadSource = "NVIDIA-SMI: GPU Utilization";
                }
                if (gpuTemp is null && _lastNvTemp is not null)
                {
                    gpuTemp = _lastNvTemp;
                    gpuTempSource = "NVIDIA-SMI: GPU Temperature";
                }
            }
            // A non-invasive fallback is possible if an existing Afterburner
            // instance publishes real CPU temperature in shared memory.
            // No EC probing, ACPI guessing, or driver installation occurs.
            if (cpuTemp is null && !SafeMode)
            {
                if (DateTime.UtcNow >= _nextCpuFallback)
                {
                    _nextCpuFallback = DateTime.UtcNow.AddSeconds(4);
                    _lastExternalCpuTemp = AfterburnerTemperature.ReadCpuTemperature(out _lastExternalCpuSource);
                }
                if (_lastExternalCpuTemp.HasValue)
                {
                    cpuTemp = _lastExternalCpuTemp;
                    cpuSource = _lastExternalCpuSource;
                }
            }
            var hwinfoTemp = HwinfoTemperature.ReadCpuTemperature(out var hwinfoSource, out var hwinfoStatus);
            if (hwinfoTemp.HasValue)
            {
                cpuTemp = hwinfoTemp;
                cpuSource = hwinfoSource;
            }
            string status = _error;
            if (SafeMode) status = "Modo seguro · " + hwinfoStatus;
            if (string.IsNullOrEmpty(status) && cpuTemp is null)
                status = IsElevated ? "Sensor CPU no disponible en este hardware/controlador" : "Sin temperatura CPU: intenta ejecutar NitroPulse como administrador";
            if (cpuTemp is null && !SafeMode && !PawnIoInstalled)
                status = "Falta PawnIO: LibreHardwareMonitor 0.9.6 requiere este controlador para leer CPU. No se instala automáticamente.";
            return new HardwareReading(cpuTemp, gpuTemp, gpuUsage, cpuSource, gpuTempSource, gpuLoadSource, status);
        }
    }

    public string SensorReport()
    {
        lock (_sync)
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("NITROPULSE - SENSORES DETECTADOS");
            report.AppendLine("Versión: " + AppVersion.Display);
            report.AppendLine("PawnIO instalado (registro de instalación): " + (PawnIoInstalled ? "Sí" : "No"));
            if (!PawnIoInstalled) report.AppendLine("Lectura CPU directa: falta la dependencia PawnIO de LibreHardwareMonitor 0.9.6.");
            var hwTemp = HwinfoTemperature.ReadCpuTemperature(out var hwSource, out var hwStatus);
            report.AppendLine("HWiNFO: " + hwStatus);
            if (hwTemp.HasValue) report.AppendLine($"CPU: {hwTemp:0.#} °C ({hwSource})");
            report.AppendLine($"Fecha local: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine("Administrador: " + (IsElevated ? "Sí" : "No"));
            report.AppendLine("Modo seguro: " + (SafeMode ? "Sí" : "No"));
            report.AppendLine("Estado: " + (_error.Length > 0 ? _error : "Sin error de inicialización"));
            var externalTemp = AfterburnerTemperature.ReadCpuTemperature(out var externalSource);
            report.AppendLine("CPU via memoria compartida de Afterburner: " +
                (externalTemp.HasValue ? $"{externalTemp:0.#} C ({externalSource})" : "no disponible"));
            if (_computer is null)
            {
                report.AppendLine("LibreHardwareMonitor: sin acceso a hardware CPU");
                return report.ToString();
            }
            try
            {
                _computer.Accept(new UpdateVisitor());
                var devices = _computer.Hardware.SelectMany(EnumerateHardware).ToArray();
                report.AppendLine("Dispositivos detectados: " + devices.Length);
                var cpuDevices = devices.Where(h => h.HardwareType == HardwareType.Cpu).ToArray();
                report.AppendLine("CPU detectadas: " + cpuDevices.Length);
                report.AppendLine("Sensores de temperatura CPU validos: " +
                    cpuDevices.Sum(h => h.Sensors.Count(s => s.SensorType == SensorType.Temperature && Valid(s.Value))));
                foreach (var hardware in devices)
                {
                    report.AppendLine();
                    report.AppendLine($"[{hardware.HardwareType}] {hardware.Name}");
                    foreach (var sensor in hardware.Sensors)
                        report.AppendLine($"  {sensor.SensorType,-13} | {sensor.Name,-35} | {sensor.Value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—"}");
                }
            }
            catch (Exception e) { report.AppendLine("Error de lectura: " + e.Message); }
            return report.ToString();
        }
    }

    private static bool HasName(ISensor s, string needle) => s.Name.Contains(needle, StringComparison.OrdinalIgnoreCase);
    private static bool Valid(float? value) => value.HasValue && float.IsFinite(value.Value) && value.Value > -10 && value.Value < 150;
    private static bool ValidLoad(float? value) => value.HasValue && float.IsFinite(value.Value) && value.Value >= 0 && value.Value <= 100;

    private static IEnumerable<IHardware> EnumerateHardware(IHardware h)
    {
        yield return h;
        foreach (var sub in h.SubHardware)
            foreach (var item in EnumerateHardware(sub)) yield return item;
    }

    private static string? FindNvidiaSmi()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
        };
        return candidates.FirstOrDefault(File.Exists) ?? "nvidia-smi.exe";
    }

    private static (double? Load, double? Temperature) NvidiaSmiRead(string file)
    {
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = "--query-gpu=utilization.gpu,temperature.gpu --format=csv,noheader,nounits",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            if (!p.Start()) return (null, null);
            if (!p.WaitForExit(2500))
            {
                try { p.Kill(true); } catch { }
                return (null, null);
            }
            if (p.ExitCode != 0) return (null, null);
            string line = p.StandardOutput.ReadLine() ?? "";
            var fields = line.Split(',');
            if (fields.Length < 2) return (null, null);
            double? Try(string t) => double.TryParse(t.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
            var load = Try(fields[0]);
            var temp = Try(fields[1]);
            return (load is >= 0 and <= 100 ? load : null, temp is > -10 and < 150 ? temp : null);
        }
        catch { return (null, null); }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            try { _computer?.Close(); } catch { }
            _computer = null;
        }
    }

    public sealed record HardwareReading(
        double? CpuTemperature, double? GpuTemperature, double? GpuPercent,
        string CpuSource, string GpuTemperatureSource, string GpuLoadSource, string Status);

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
