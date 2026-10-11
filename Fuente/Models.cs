using System.Drawing;

namespace NitroPulse;

internal sealed record MetricSnapshot(
    DateTime Timestamp,
    double CpuPercent,
    double? GpuPercent,
    double RamPercent,
    double RamUsedGB,
    double RamAvailableGB,
    double RamTotalGB,
    double CommittedGB,
    double CommitLimitGB,
    double KernelNonpagedMB,
    double KernelPagedMB,
    double? PageReadsPerSec,
    double? PagesInputPerSec,
    double? DiskReadMBps,
    double? DiskWriteMBps,
    double? DiskQueue,
    double? CpuTemperature,
    double? GpuTemperature,
    string CpuTemperatureSensor,
    string GpuTemperatureSensor,
    string GpuLoadSensor,
    string HardwareStatus);

internal sealed record ProcessSample(
    string Name, int Instances, double RamMB, double PrivateMB, double CpuPercent);

internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(13, 19, 32);
    public static readonly Color Panel = Color.FromArgb(24, 33, 51);
    public static readonly Color PanelAlt = Color.FromArgb(30, 41, 62);
    public static readonly Color Border = Color.FromArgb(51, 63, 85);
    public static readonly Color Text = Color.FromArgb(235, 242, 250);
    public static readonly Color Muted = Color.FromArgb(147, 163, 184);
    public static readonly Color Cpu = Color.FromArgb(69, 230, 114);
    public static readonly Color Gpu = Color.FromArgb(245, 71, 77);
    public static readonly Color Ram = Color.FromArgb(188, 131, 77);
    public static readonly Color CpuTemp = Cpu;
    public static readonly Color GpuTemp = Gpu;
}
