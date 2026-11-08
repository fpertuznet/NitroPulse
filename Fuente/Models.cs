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
