using System.Runtime.InteropServices;

namespace NitroPulse;

/// <summary>
/// Optional locale-independent PDH counters; absence is represented by null,
/// not by a false zero. Disk % deliberately omitted (may exceed 100%).
/// </summary>
internal sealed class WindowsCounters : IDisposable
{
    private IntPtr _query;
    private readonly Dictionary<string, IntPtr> _counters = new();

    public WindowsCounters()
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) return;
        Add("PageReads", @"\Memory\Page Reads/sec");
        Add("PagesInput", @"\Memory\Pages Input/sec");
        Add("DiskRead", @"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
        Add("DiskWrite", @"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
        Add("DiskQueue", @"\PhysicalDisk(_Total)\Avg. Disk Queue Length");
        PdhCollectQueryData(_query); // warm up rate counters
    }

    private void Add(string key, string path)
    {
        if (_query != IntPtr.Zero && PdhAddEnglishCounter(_query, path, IntPtr.Zero, out var counter) == 0)
            _counters[key] = counter;
    }

    public CounterSnapshot Read()
    {
        if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != 0)
            return new(null, null, null, null, null);
        double? Get(string key)
        {
            if (!_counters.TryGetValue(key, out var handle)) return null;
            if (PdhGetFormattedCounterValue(handle, PdhFmtDouble, out _, out var data) != 0 || (data.CStatus != 0 && data.CStatus != 1) || !double.IsFinite(data.Value))
                return null;
            return Math.Max(0, data.Value);
        }
        double? read = Get("DiskRead");
        double? write = Get("DiskWrite");
        return new CounterSnapshot(Get("PageReads"), Get("PagesInput"),
            read / 1_048_576d, write / 1_048_576d, Get("DiskQueue"));
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) PdhCloseQuery(_query);
        _query = IntPtr.Zero;
    }

    public sealed record CounterSnapshot(double? PageReadsPerSec, double? PagesInputPerSec,
        double? DiskReadMBps, double? DiskWriteMBps, double? DiskQueue);

    private const uint PdhFmtDouble = 0x00000200;
    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValue { public uint CStatus; public double Value; }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhOpenQueryW")]
    private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhFmtCounterValue value);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
