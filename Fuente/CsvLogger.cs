using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace NitroPulse;

/// <summary>
/// Flushes after every sample so files survive an unexpected BSOD/reboot.
/// Stores no document contents, tabs, URLs, credentials or command lines.
/// </summary>
internal sealed class CsvLogger : IDisposable
{
    private readonly object _sync = new();
    private readonly StreamWriter _system;
    private readonly StreamWriter _processes;
    public string SessionDirectory { get; }
    public string RootDirectory { get; }
    public bool LoggingEnabled { get; set; } = true;
    public long Samples { get; private set; }
    public const int AutomaticBackupMinutes = 10;
    public DateTime StartAt { get; } = DateTime.Now;
    public string AutomaticBackupDirectory => Path.Combine(RootDirectory, "Exportaciones", "Autoguardados");
    public string FinalBackupDirectory => Path.Combine(RootDirectory, "Exportaciones", "Finales");

    public CsvLogger()
    {
        RootDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NitroPulse");
        SessionDirectory = Path.Combine(RootDirectory, "Sesiones", "Sesion_" + StartAt.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(SessionDirectory);
        _system = Open(Path.Combine(SessionDirectory, "sistema.csv"));
        _processes = Open(Path.Combine(SessionDirectory, "procesos.csv"));
        _system.WriteLine("FechaHora;CPU_Pct;GPU_Pct;RAM_Pct;RAM_Usada_GB;RAM_Disponible_GB;RAM_Total_GB;CPU_Temp_C;GPU_Temp_C;Comprometida_GB;Limite_Compromiso_GB;Pool_NoPaginado_MB;Pool_Paginado_MB;PageReads_PorSeg;PagesInput_PorSeg;Disco_Lectura_MBps;Disco_Escritura_MBps;Cola_Disco;CPU_Sensor;GPU_Temp_Sensor;GPU_Uso_Sensor");
        _processes.WriteLine("FechaHora;Proceso;Instancias;RAM_MB;MemPrivada_MB;CPU_Pct_Equipo");
        WriteInfo();
    }

    private static StreamWriter Open(string path) => new(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(true))
    { AutoFlush = true };

    public void WriteSystem(MetricSnapshot s)
    {
        if (!LoggingEnabled) return;
        lock (_sync)
        {
            _system.WriteLine(Csv(
                s.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"), Num(s.CpuPercent), Num(s.GpuPercent), Num(s.RamPercent),
                Num(s.RamUsedGB), Num(s.RamAvailableGB), Num(s.RamTotalGB), Num(s.CpuTemperature), Num(s.GpuTemperature),
                Num(s.CommittedGB), Num(s.CommitLimitGB), Num(s.KernelNonpagedMB), Num(s.KernelPagedMB),
                Num(s.PageReadsPerSec), Num(s.PagesInputPerSec), Num(s.DiskReadMBps), Num(s.DiskWriteMBps), Num(s.DiskQueue),
                s.CpuTemperatureSensor, s.GpuTemperatureSensor, s.GpuLoadSensor));
            Samples++;
            // Force a durable checkpoint approximately every 10 seconds.
            if (Samples % 5 == 0) ((FileStream)_system.BaseStream).Flush(true);
        }
    }

    public void WriteProcesses(DateTime at, IEnumerable<ProcessSample> samples)
    {
        if (!LoggingEnabled) return;
        lock (_sync)
        {
            foreach (var p in samples.OrderByDescending(x => x.RamMB).Take(20)
                .Concat(samples.OrderByDescending(x => x.CpuPercent).Take(10))
                .DistinctBy(x => x.Name))
                _processes.WriteLine(Csv(at.ToString("yyyy-MM-dd HH:mm:ss"), p.Name, p.Instances.ToString(),
                    Num(p.RamMB), Num(p.PrivateMB), Num(p.CpuPercent)));
            ((FileStream)_processes.BaseStream).Flush(true);
        }
    }

    public void Error(string where, Exception e)
    {
        try
        {
            lock (_sync)
                File.AppendAllText(Path.Combine(SessionDirectory, "errores.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}: {e}\r\n", Encoding.UTF8);
        }
        catch { /* Best effort */ }
    }

    /// <summary>
    /// The rolling automatic backup is rebuilt every 10 minutes. The previous
    /// complete ZIP remains intact until the new archive is ready for replacement.
    /// Original CSV files are never deleted.
    /// </summary>
    public string ExportAutomaticBackup()
    {
        lock (_sync)
        {
            string name = Path.GetFileName(SessionDirectory) + "_autoguardado.zip";
            return CreateZipAtomic(AutomaticBackupDirectory, name, overwrite: true);
        }
    }

    /// <summary>Creates a final archive when the user chooses Exit from the tray.</summary>
    public string ExportFinalZip()
    {
        lock (_sync)
        {
            var endInfo = Path.Combine(SessionDirectory, "cierre.txt");
            File.WriteAllLines(endInfo, new[]
            {
                "NITROPULSE - CIERRE NORMAL",
                $"Inicio: {StartAt:yyyy-MM-dd HH:mm:ss}",
                $"Salida: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                $"Muestras de sistema: {Samples}",
                "CSV originales conservados en la carpeta de sesiones."
            }, Encoding.UTF8);
            string name = Path.GetFileName(SessionDirectory) + "_final.zip";
            return CreateZipAtomic(FinalBackupDirectory, name, overwrite: true);
        }
    }

    public string ExportZip()
    {
        lock (_sync)
        {
            string name = Path.GetFileName(SessionDirectory) + "_manual_"
                + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".zip";
            return CreateZipAtomic(Path.Combine(RootDirectory, "Exportaciones"), name, overwrite: false);
        }
    }

    /// <summary>
    /// Build into a temporary sibling file and rename only after ZipArchive
    /// closes successfully. An interruption cannot replace a complete backup
    /// with a truncated one. Running samples serialize through the same lock.
    /// </summary>
    private string CreateZipAtomic(string directory, string name, bool overwrite)
    {
        _system.Flush();
        _processes.Flush();
        ((FileStream)_system.BaseStream).Flush(true);
        ((FileStream)_processes.BaseStream).Flush(true);
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, name);
        string temporary = Path.Combine(directory, "." + name + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var zipStream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var source in Directory.EnumerateFiles(SessionDirectory))
                    {
                        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var output = archive.CreateEntry(Path.GetFileName(source), CompressionLevel.Fastest).Open();
                        input.CopyTo(output);
                    }
                }
                zipStream.Flush(true);
            }
            File.Move(temporary, destination, overwrite);
            return destination;
        }
        finally
        {
            // Only an incomplete temporary copy is removed; never delete the
            // last successfully created archive or source logs.
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { /* leave any locked partial file for manual cleanup */ }
        }
    }

    private void WriteInfo()
    {
        var details = new[]
        {
            "NITROPULSE " + AppVersion.Display + " - REGISTRO LOCAL", $"Inicio: {StartAt:yyyy-MM-dd HH:mm:ss}",
            "Sistema: " + Environment.OSVersion.VersionString,
            "Núcleos lógicos visibles: " + Environment.ProcessorCount,
            "Frecuencia: sistema ~2 segundos, procesos ~15 segundos",
            $"Estabilidad de red: pruebas cada {NetworkMonitor.IntervalSeconds} segundos; registro independiente, se pausa desde la pestaña Red.",
            "Red: SSID/BSSID, interfaces, IP/router/DNS y pruebas sintéticas ICMP/TCP/DNS. Revisar esos datos antes de compartir.",
            "CSV: guardado inmediato; sincronización con disco aproximadamente cada 10 segundos.",
            $"ZIP automático cada {AutomaticBackupMinutes} minutos (copias renovadas de forma segura).",
            "Al elegir Salir, exportación ZIP final. En apagados bruscos se conservan los CSV sincronizados.",
            "CPU %: GetSystemTimes (equipo completo)",
            "Paginación y disco: PDH counters en inglés independientes del idioma del sistema",
            "RAM: GlobalMemoryStatusEx; Kernel/Comprometida: GetPerformanceInfo",
            "GPU y temperaturas: LibreHardwareMonitorLib, fallback NVIDIA-SMI",
            "Una temperatura no disponible se deja VACÍA; no se estima mediante ACPI.",
            "Procesos: memoria residente / memoria privada; CPU por diferencias de tiempo del proceso.",
            "No se recopilan URLs, documentos, contenido de pestañas, comandos ni contraseñas.",
            "Formato CSV: UTF-8 con separador punto y coma; decimales con punto.",
            "Diagnóstico de drivers/sensores: usa el botón 'Ver sensores'."
        };
        File.WriteAllLines(Path.Combine(SessionDirectory, "equipo.txt"), details, Encoding.UTF8);
    }

    private static string Csv(params string[] fields) => string.Join(";", fields.Select(f =>
    {
        // Prevent spreadsheet formula injection if a process is named beginning with =, +, -, @.
        if (f.Length > 0 && (f[0] == '=' || f[0] == '+' || f[0] == '-' || f[0] == '@')) f = "'" + f;
        return f.Contains(';') || f.Contains('"') || f.Contains('\n') || f.Contains('\r')
            ? "\"" + f.Replace("\"", "\"\"") + "\"" : f;
    }));
    private static string Num(double? number) => number?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";

    public void Dispose()
    {
        lock (_sync)
        {
            _system.Dispose();
            _processes.Dispose();
        }
    }
}
