using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NitroPulse;

internal sealed record LinkInfo(string Id, int Index, string Name, string Type, string[] Addresses, string[] Gateways,
    string[] Dns, string Ssid = "", string Bssid = "", int? Signal = null, string WifiNote = "", int Ipv6Index = -1, string ProfileId = "", string ProfileName = "", string ProfileNote = "")
{
    public string Title => !string.IsNullOrEmpty(Ssid) ? Ssid : !string.IsNullOrEmpty(ProfileName) ? ProfileName : Name;
    internal string Identity => string.Join('|', Id, Type, Ssid, Bssid, ProfileId, string.Join(',', Addresses), string.Join(',', Gateways), string.Join(',', Dns));
    internal string LabelKey => Hash(!string.IsNullOrEmpty(ProfileId) ? "profile|" + ProfileId.ToUpperInvariant() : FallbackIdentity);
    internal string FallbackLabelKey => Hash(FallbackIdentity);
    private string FallbackIdentity => !string.IsNullOrEmpty(Ssid) ? "wifi|" + Ssid : "link|" + Id.ToUpperInvariant() + "|" + Type + "|" + string.Join(',', Gateways);
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
internal sealed record NetworkContext(LinkInfo[] Links, string RouteCloudflare, string RouteGoogle, string RouterCloudflare = "", string RouterGoogle = "")
{
    public LinkInfo? Main => Links.FirstOrDefault(l => l.Id == RouteCloudflare) ?? Links.FirstOrDefault(l => l.Id == RouteGoogle);
    public string Title => Main?.Title ?? (Links.Length == 0 ? "Sin enlace activo" : "Sin ruta IPv4 a la prueba");
    internal string LabelKey => Main?.LabelKey ?? "";
    public string Key => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
        Links.OrderBy(l => l.Id, StringComparer.Ordinal).Select(l => l.Identity)) + "\n" + RouteCloudflare + "\n" + RouteGoogle + "\n" + RouterCloudflare + "\n" + RouterGoogle)))[..16];
}
internal enum ProbeState { Ok, Failed, Inconclusive, Skipped }
internal sealed record NetworkProbe(string Name, string Target, string Protocol, ProbeState State, double? Milliseconds, string Detail, string InterfaceId);
internal sealed record NetworkSample(DateTimeOffset At, int Segment, NetworkContext Context, NetworkProbe[] Probes, string Assessment);
internal sealed record NetworkEvent(DateTimeOffset At, int Segment, string Kind, string Detail);

internal sealed class ProbeStatistics
{
    public int Attempts { get; private set; }
    public int Failed { get; private set; }
    public int Inconclusive { get; private set; }
    public int Successes { get; private set; }
    public double TotalMs { get; private set; }
    public double MaxMs { get; private set; }
    private double? _previous;
    private double _jitterTotal;
    private int _jitterPairs;
    public double? Average => Successes == 0 ? null : TotalMs / Successes;
    public double? Jitter => _jitterPairs == 0 ? null : _jitterTotal / _jitterPairs;
    public double? FailurePercent => Attempts == 0 ? null : 100d * Failed / Attempts;
    public void Add(NetworkProbe probe)
    {
        if (probe.State is ProbeState.Inconclusive or ProbeState.Skipped) { Inconclusive++; _previous = null; return; }
        Attempts++;
        if (probe.State == ProbeState.Failed) { Failed++; _previous = null; return; }
        Successes++;
        if (probe.Milliseconds is not double ms) return;
        TotalMs += ms; MaxMs = Math.Max(MaxMs, ms);
        if (_previous is double previous) { _jitterTotal += Math.Abs(ms - previous); _jitterPairs++; }
        _previous = ms;
    }
    public string Describe() => $"intentos={Attempts}; respuestas={Successes}; fallos={Failed}; fallos%={Number(FailurePercent)}; " +
        $"media_ms={Number(Average)}; max_ms={(Successes == 0 ? "—" : Number(MaxMs))}; variación_media_ms={Number(Jitter)}; no_concluyentes={Inconclusive}";
    internal static string Number(double? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—";
}

internal sealed class NetworkSegment(int id, DateTimeOffset start, NetworkContext context)
{
    public int Id { get; } = id;
    public DateTimeOffset Start { get; } = start;
    public DateTimeOffset Last { get; set; } = start;
    public NetworkContext Context { get; } = context;
    public string Alias { get; set; } = "";
    public int Samples { get; set; }
    public int SuspectedFailures { get; set; }
    public Dictionary<string, ProbeStatistics> Statistics { get; } = new();
    public List<NetworkIncident> Incidents { get; } = new();
    public string Name => string.IsNullOrWhiteSpace(Alias) ? Context.Title : Alias;
    public void Add(NetworkSample sample)
    {
        Last = sample.At; Samples++;
        if (sample.Assessment.StartsWith("Posible", StringComparison.Ordinal)) SuspectedFailures++;
        var tcp = sample.Probes.Where(p => p.Protocol == "TCP").ToArray();
        var dns = sample.Probes.Where(p => p.Protocol == "DNS/UDP").ToArray();
        string failure = tcp.Length >= 2 && tcp.All(p => p.State == ProbeState.Failed) ? "Ambas conexiones TCP fallaron" :
            tcp.Any(p => p.State == ProbeState.Ok) && dns.Length > 0 && dns.All(p => p.State == ProbeState.Failed) ? "DNS/UDP falló con salida TCP disponible" : "";
        var open = Incidents.LastOrDefault(i => i.End is null);
        if (open is not null && open.Kind != failure) { open.End = sample.At; open.EndReason = "El patrón dejó de observarse en la siguiente muestra"; }
        if (!string.IsNullOrEmpty(failure) && (open is null || open.End is not null)) Incidents.Add(new(sample.At, failure));
        foreach (var probe in sample.Probes)
        {
            // Include target and routed adapter: never pool different resolvers or routes.
            string key = probe.Name + " | " + probe.Target + " | " + probe.InterfaceId;
            if (!Statistics.TryGetValue(key, out var stats)) Statistics[key] = stats = new();
            stats.Add(probe);
        }
    }
    public void EndObservation()
    {
        var open = Incidents.LastOrDefault(i => i.End is null);
        if (open is not null) { open.End = Last; open.EndReason = "Fin de observación / cambio de red; recuperación NO confirmada"; }
    }
}
internal sealed class NetworkIncident(DateTimeOffset start, string kind)
{
    public DateTimeOffset Start { get; } = start;
    public string Kind { get; } = kind;
    public DateTimeOffset? End { get; set; }
    public string EndReason { get; set; } = "Sigue observado en la última muestra";
}

internal static class NetworkAssessment
{
    public static string Evaluate(NetworkContext context, NetworkProbe[] probes)
    {
        if (context.Links.Length == 0) return "Sin enlace activo";
        if (context.Main is null) return "Sin ruta IPv4; Internet IPv6 no evaluado";
        var tcp = probes.Where(p => p.Protocol == "TCP").ToArray();
        bool reachable = tcp.Any(p => p.State == ProbeState.Ok);
        bool bothFailed = tcp.Length >= 2 && tcp.All(p => p.State == ProbeState.Failed);
        bool gatewayOk = probes.Any(p => p.Name == "Router" && p.State == ProbeState.Ok);
        var dns = probes.Where(p => p.Protocol == "DNS/UDP").ToArray();
        if (bothFailed && gatewayOk) return "Posible fallo de salida IPv4 / filtrado: router responde, ambos destinos TCP fallan";
        if (bothFailed) return "Posible fallo local o de salida IPv4; ICMP y TCP no bastan para localizarlo";
        if (reachable && dns.Length > 0 && dns.All(p => p.State == ProbeState.Failed)) return "Posible problema DNS/UDP: salida TCP responde";
        if (reachable && probes.Any(p => p.State == ProbeState.Failed)) return "Salida TCP disponible; hay pruebas sin respuesta (posible filtrado o fallo parcial)";
        if (reachable) return "Salida TCP disponible; no garantiza acceso a todas las páginas";
        return "Prueba inconclusa";
    }
    public static string Csv(params string[] fields) => string.Join(';', fields.Select(field =>
    {
        if (field.Length > 0 && "=+-@".Contains(field[0])) field = "'" + field;
        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }));
}
