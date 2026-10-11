using System.Diagnostics;

namespace NitroPulse;

internal sealed class ProcessSampler
{
    private readonly Dictionary<string, TimeSpan> _lastCpu = new();
    private DateTime _lastAt = DateTime.MinValue;

    public List<ProcessSample> Read()
    {
        DateTime now = DateTime.UtcNow;
        double elapsed = _lastAt == DateTime.MinValue ? 0 : (now - _lastAt).TotalSeconds;
        int logicalCount = Environment.ProcessorCount;
        var currentCpu = new Dictionary<string, TimeSpan>();
        var groups = new Dictionary<string, MutableGroup>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    string name = p.ProcessName;
                    string id = p.Id.ToString() + "|" + name;
                    var g = groups.TryGetValue(name, out var existing) ? existing : (groups[name] = new MutableGroup());
                    g.Instances++;
                    g.RamMB += p.WorkingSet64 / 1_048_576d;
                    g.PrivateMB += p.PrivateMemorySize64 / 1_048_576d;
                    TimeSpan cpu = p.TotalProcessorTime;
                    currentCpu[id] = cpu;
                    if (elapsed > 0 && _lastCpu.TryGetValue(id, out var previous) && cpu >= previous)
                        g.CpuPercent += 100 * (cpu - previous).TotalSeconds / elapsed / logicalCount;
                }
                catch { /* Some protected system processes cannot be read. */ }
            }
        }
        _lastAt = now;
        _lastCpu.Clear();
        foreach (var (key, value) in currentCpu) _lastCpu[key] = value;

        return groups.Select(kv => new ProcessSample(kv.Key, kv.Value.Instances,
                Math.Round(kv.Value.RamMB, 0), Math.Round(kv.Value.PrivateMB, 0),
                Math.Clamp(Math.Round(kv.Value.CpuPercent, 1), 0, 100)))
            .OrderByDescending(x => x.RamMB).ToList();
    }

    private sealed class MutableGroup
    {
        public int Instances;
        public double RamMB;
        public double PrivateMB;
        public double CpuPercent;
    }
}
