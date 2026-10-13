namespace NitroPulse;

internal static class NetworkTimeline
{
    internal static bool CanJoin(NetworkSample left, NetworkSample right) => left.Segment == right.Segment &&
        right.At >= left.At && (right.At - left.At).TotalSeconds <= NetworkMonitor.IntervalSeconds * 3;
    internal static NetworkProbe? Ping(NetworkSample sample, string name) => sample.Probes.FirstOrDefault(p => p.Protocol == "ICMP" && p.Name == name);
    internal static bool TcpFailure(NetworkSample sample)
    { var tcp = sample.Probes.Where(p => p.Protocol == "TCP").ToArray(); return tcp.Length >= 2 && tcp.All(p => p.State == ProbeState.Failed); }
    internal static string Summary(NetworkSample[] samples)
    {
        if (samples.Length == 0) return "Esperando pings · no se representan valores desconocidos como cero";
        var current = samples.Where(s => s.Segment == samples[^1].Segment).ToArray();
        var stats = new ProbeStatistics();
        foreach (var sample in current) if (Ping(sample, "Cloudflare") is { } ping) stats.Add(ping);
        return $"Tramo actual · Ping 1.1.1.1: media {ProbeStatistics.Number(stats.Average)} ms · máx. {(stats.Successes == 0 ? "—" : ProbeStatistics.Number(stats.MaxMs))} ms · " +
            $"variación {ProbeStatistics.Number(stats.Jitter)} ms · sin respuesta {stats.Failed}/{stats.Attempts} ({ProbeStatistics.Number(stats.FailurePercent)}%)";
    }
}
