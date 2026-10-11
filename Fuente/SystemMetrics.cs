using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NitroPulse;

/// <summary>
/// Windows API: independent of the language of Windows and of performance counters.
/// GetSystemTimes reports the total system CPU load; GlobalMemoryStatusEx physical RAM.
/// </summary>
internal sealed class SystemMetrics
{
    private ulong _previousIdle;
    private ulong _previousTotal;

    public SystemSnapshot Read()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo leer CPU con GetSystemTimes");

        ulong idleTicks = AsUInt64(idle);
        ulong totalTicks = AsUInt64(kernel) + AsUInt64(user);
        double cpu = 0;
        if (_previousTotal != 0 && totalTicks > _previousTotal)
        {
            double deltaTotal = totalTicks - _previousTotal;
            double deltaIdle = idleTicks - _previousIdle;
            cpu = Math.Clamp(100d * (deltaTotal - deltaIdle) / deltaTotal, 0, 100);
        }
        _previousIdle = idleTicks;
        _previousTotal = totalTicks;

        var memory = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref memory))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo leer la memoria física");

        double ramTotal = BytesToGB(memory.TotalPhysical);
        double ramAvailable = BytesToGB(memory.AvailablePhysical);
        double ramUsed = Math.Max(0, ramTotal - ramAvailable);
        double ramPercent = memory.MemoryLoad; // physical memory load reported by Windows

        var perf = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
        bool hasPerf = GetPerformanceInfo(out perf, perf.Size);
        double pageSize = hasPerf ? perf.PageSize.ToUInt64() : 0;
        double committed = hasPerf ? perf.CommitTotal.ToUInt64() * pageSize / 1_073_741_824 : 0;
        double commitLimit = hasPerf ? perf.CommitLimit.ToUInt64() * pageSize / 1_073_741_824 : 0;
        double paged = hasPerf ? perf.KernelPaged.ToUInt64() * pageSize / 1_048_576 : 0;
        double nonpaged = hasPerf ? perf.KernelNonpaged.ToUInt64() * pageSize / 1_048_576 : 0;

        return new SystemSnapshot(cpu, ramPercent, ramUsed, ramAvailable, ramTotal,
            committed, commitLimit, nonpaged, paged);
    }

    private static double BytesToGB(ulong value) => value / 1_073_741_824d;
    private static ulong AsUInt64(FileTime t) => ((ulong)t.High << 32) | t.Low;

    internal record SystemSnapshot(double Cpu, double RamPercent, double RamUsedGB,
        double RamAvailableGB, double RamTotalGB, double CommittedGB,
        double CommitLimitGB, double NonpagedMB, double PagedMB);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public uint Size;
        public UIntPtr CommitTotal;
        public UIntPtr CommitLimit;
        public UIntPtr CommitPeak;
        public UIntPtr PhysicalTotal;
        public UIntPtr PhysicalAvailable;
        public UIntPtr SystemCache;
        public UIntPtr KernelTotal;
        public UIntPtr KernelPaged;
        public UIntPtr KernelNonpaged;
        public UIntPtr PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo(out PerformanceInformation performanceInfo, uint size);
}
