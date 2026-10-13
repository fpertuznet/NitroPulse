using System.IO.Compression;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;

namespace NitroPulse;

internal sealed class NetworkMonitor : IDisposable
{
    public const int IntervalSeconds = 5;
    private readonly object _sync = new();
    private readonly string _directory;
    private readonly StreamWriter _readings, _eventFile, _contexts;
    private readonly List<NetworkSegment> _segments = new();
    private readonly List<NetworkEvent> _events = new();
    private readonly List<NetworkSample> _history = new();
    private readonly NetworkLabels _labels;
    private CancellationTokenSource? _cancel;
    private Task? _loop;
    private readonly CancellationTokenSource _lifetime = new();
    private long _epoch;
    private DateTimeOffset? _lastAt;
    private bool _disposed;
    private NetworkSample? _latest;
    public bool Running { get { lock (_sync) return _loop is { IsCompleted: false }; } }
    public string Error { get; private set; } = "";
    public NetworkSample? Latest { get { lock (_sync) return _latest; } }

    public NetworkMonitor(string sessionDirectory, NetworkLabels? labels = null)
    {
        _labels = labels ?? new NetworkLabels();
        if (labels is null) _labels.ImportLegacy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NitroPulse", "Sesiones"));
        _directory = sessionDirectory;
        Directory.CreateDirectory(_directory);
        StreamWriter Open(string name) => new(new FileStream(Path.Combine(_directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(true)) { AutoFlush = true };
        _readings = Open("red_mediciones.csv"); _eventFile = Open("red_eventos.csv"); _contexts = Open("red_conexiones.jsonl");
        _readings.WriteLine("FechaISO;Tramo;Red;Prueba;Destino;Protocolo;Estado;Milisegundos;Detalle;Interfaz;SeñalWiFi;Evaluación");
        _eventFile.WriteLine("FechaISO;Tramo;Evento;Detalle");
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
    }
    private void OnAddressChanged(object? sender, EventArgs e) => Interlocked.Increment(ref _epoch);

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_loop is { IsCompleted: false }) return;
            _cancel?.Dispose(); _cancel = new();
            _lastAt = null; Error = "";
            Event(DateTimeOffset.Now, "Inicio", $"Monitoreo de red activo; comprobaciones cada {IntervalSeconds} segundos");
            _loop = Task.Run(() => RunAsync(_cancel.Token));
        }
    }
    public void Cancel() { lock (_sync) { _cancel?.Cancel(); _lifetime.Cancel(); } }
    public async Task StopAsync()
    {
        Task? loop;
        lock (_sync) { _cancel?.Cancel(); loop = _loop; }
        if (loop is not null) await loop;
    }
    private async Task RunAsync(CancellationToken cancel)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(IntervalSeconds));
            do
            {
                try { await SampleOnceAsync(cancel); Error = ""; }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { break; }
                catch (Exception e) when (e is SystemException)
                {
                    lock (_sync)
                    {
                        Error = "No se pudo medir la red: " + e.Message;
                        try { Event(DateTimeOffset.Now, "Error de medición", e.Message); } catch (IOException) { }
                    }
                }
            } while (await timer.WaitForNextTickAsync(cancel));
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        finally
        {
            lock (_sync) { _segments.LastOrDefault()?.EndObservation(); try { Event(DateTimeOffset.Now, "Pausa / cierre", "Fin de monitoreo; este intervalo no es una caída de Internet"); } catch (IOException) { } }
        }
    }
    internal async Task<NetworkSample> SampleOnceAsync(CancellationToken cancel)
    {
        long epoch = Interlocked.Read(ref _epoch);
        var context = NetworkIdentity.Read();
        var probes = await NetworkProbes.MeasureAsync(context, cancel);
        var after = NetworkIdentity.Read();
        bool changed = context.Key != after.Key || epoch != Interlocked.Read(ref _epoch);
        if (changed) { context = after; probes = Array.Empty<NetworkProbe>(); }
        return Record(DateTimeOffset.Now, context, probes, changed);
    }
    internal NetworkSample Record(DateTimeOffset at, NetworkContext context, NetworkProbe[] probes, bool changed = false)
    {
        lock (_sync)
        {
            if (changed) probes = Array.Empty<NetworkProbe>();
            bool gap = _lastAt is null || (at - _lastAt.Value).TotalSeconds > IntervalSeconds * 3 || at < _lastAt;
            if (_segments.Count == 0 || gap || _segments[^1].Context.Key != context.Key || changed)
            {
                _segments.LastOrDefault()?.EndObservation();
                var segment = new NetworkSegment(_segments.Count + 1, at, context) { Alias = _labels.Get(context) };
                _segments.Add(segment);
                _contexts.WriteLine(JsonSerializer.Serialize(new { segment = segment.Id, at, context }));
                Event(at, gap ? "Inicio / intervalo sin observación" : "Cambio de conexión", $"Tramo {segment.Id}: {context.Title}. No se suman pérdidas entre tramos.");
            }
            var current = _segments[^1];
            string assessment = changed ? "Cambio durante la prueba; resultados descartados" : NetworkAssessment.Evaluate(context, probes);
            if (_latest is not null && _latest.Assessment != assessment) Event(at, "Cambio de estado", assessment);
            var sample = new NetworkSample(at, current.Id, context, probes, assessment);
            current.Add(sample); _latest = sample; _lastAt = at;
            _history.Add(sample); if (_history.Count > 360) _history.RemoveAt(0);
            if (probes.Length == 0)
                _readings.WriteLine(NetworkAssessment.Csv(at.ToString("O"), current.Id.ToString(), current.Name, "Sin prueba", "", "", "Inconclusive", "", "", "", "", assessment));
            foreach (var probe in probes)
                _readings.WriteLine(NetworkAssessment.Csv(at.ToString("O"), current.Id.ToString(), current.Name, probe.Name, probe.Target,
                    probe.Protocol, probe.State.ToString(), probe.Milliseconds?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "", probe.Detail,
                    probe.InterfaceId, context.Links.FirstOrDefault(l => l.Id == probe.InterfaceId)?.Signal?.ToString() ?? "", assessment));
            DurableFlush();
            return sample;
        }
    }
    private void DurableFlush()
    {
        foreach (var writer in new[] { _readings, _eventFile, _contexts }) { writer.Flush(); ((FileStream)writer.BaseStream).Flush(true); }
    }
    private void Event(DateTimeOffset at, string kind, string detail)
    {
        var item = new NetworkEvent(at, _segments.LastOrDefault()?.Id ?? 0, kind, detail);
        _events.Add(item); if (_events.Count > 200) _events.RemoveAt(0);
        _eventFile.WriteLine(NetworkAssessment.Csv(at.ToString("O"), item.Segment.ToString(), kind, detail));
    }
    public NetworkEvent[] Events() { lock (_sync) return _events.AsEnumerable().Reverse().ToArray(); }
    public NetworkSample[] History() { lock (_sync) return _history.ToArray(); }
    public string NameFor(NetworkContext context) { lock (_sync) { string label = _labels.Get(context); return string.IsNullOrWhiteSpace(label) ? context.Title : label; } }
    public string CurrentLabel { get { lock (_sync) return _latest is null ? "" : _labels.Get(_latest.Context); } }
    public (int Id, string Network, string Start, string Last, int Samples, int Alerts)[] Rows()
    {
        lock (_sync) return _segments.AsEnumerable().Reverse().Select(s => (s.Id, s.Name, s.Start.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            s.Last.ToString("HH:mm:ss zzz"), s.Samples, s.SuspectedFailures)).ToArray();
    }
    public void RenameCurrent(string label, string? expectedKey = null)
    {
        lock (_sync)
        {
            if (_segments.Count == 0) return;
            var current = _segments[^1];
            if (expectedKey is not null && current.Context.LabelKey != expectedKey) throw new InvalidOperationException("La conexión cambió mientras escribías. Revisa la red actual y vuelve a guardar la etiqueta.");
            _labels.Set(current.Context, label);
            foreach (var segment in _segments.Where(s => s.Context.LabelKey == current.Context.LabelKey)) segment.Alias = label.Trim();
            Event(DateTimeOffset.Now, "Etiqueta del usuario", label.Trim());
            DurableFlush();
        }
    }
    internal string Report()
    {
        lock (_sync)
        {
            var report = new StringBuilder();
            report.AppendLine("NITROPULSE " + AppVersion.Display + " - ESTABILIDAD DE RED");
            report.AppendLine("Generado: " + DateTimeOffset.Now.ToString("O"));
            report.AppendLine("Informe local para revisión con el operador. No atribuye responsabilidades.");
            report.AppendLine($"Pruebas cada {IntervalSeconds}s: ICMP al router y 1.1.1.1/8.8.8.8; TCP443 a ambas IP; DNS/UDP53 example.com a hasta dos DNS configurados.");
            report.AppendLine("La ruta se consulta en Windows; puede ser VPN. Se anotan todas las interfaces activas.");
            report.AppendLine("Cobertura: Internet IPv4 y DNS configurado (también DNS IPv6); no prueba páginas, IPv6 general, HTTPS, proxy, DoH, velocidad ni portal cautivo.");
            report.AppendLine("Pérdida ICMP = porcentaje de ecos sin respuesta, no pérdida de tráfico web. Filtrado o límites de ICMP/UDP/TCP pueden causar fallos de prueba.");
            report.AppendLine("Variación media = diferencia absoluta de RTT de respuestas consecutivas; no jitter de voz certificado. Reloj local con zona horaria.");
            report.AppendLine($"Cambios detectados durante pruebas se descartan. Pausas/suspensión y huecos >{IntervalSeconds * 3}s se separan, no se cuentan como caídas. Cambios muy breves entre muestras pueden no identificarse.");
            report.AppendLine("SSID, BSSID e IP/DNS identifican redes y pueden ser privados; revisar antes de compartir. No incluye contraseñas ni historial de navegación.");
            report.AppendLine("Etiquetas persistentes por perfil de Windows; fallback por SSID o interfaz/gateway. Los CSV originales conservan el nombre registrado; red_nombres.json e Informe_red.txt contienen las etiquetas actuales.");
            foreach (var segment in _segments)
            {
                report.AppendLine($"\nTRAMO {segment.Id}: {segment.Name}\nDesde {segment.Start:O} hasta {segment.Last:O}; muestras={segment.Samples}; alertas orientativas={segment.SuspectedFailures}");
                foreach (var link in segment.Context.Links)
                    report.AppendLine($"Interfaz {link.Id} / {link.Name} / {link.Type}; perfil={link.ProfileId} {link.ProfileName} {link.ProfileNote}; SSID={link.Ssid}; BSSID={link.Bssid}; IP={string.Join(',', link.Addresses)}; router={string.Join(',', link.Gateways)}; DNS={string.Join(',', link.Dns)}; Wi-Fi={link.WifiNote}");
                report.AppendLine($"Ruta 1.1.1.1: {segment.Context.RouteCloudflare} vía {segment.Context.RouterCloudflare}; ruta 8.8.8.8: {segment.Context.RouteGoogle} vía {segment.Context.RouterGoogle}");
                foreach (var (target, stats) in segment.Statistics) report.AppendLine(target + ": " + stats.Describe());
                foreach (var incident in segment.Incidents)
                    report.AppendLine($"Episodio orientativo: {incident.Kind}; detectado {incident.Start:O}; fin {(incident.End ?? segment.Last):O}; " +
                        $"intervalo observado aprox. {Math.Max(0, ((incident.End ?? segment.Last) - incident.Start).TotalSeconds):0}s; {incident.EndReason}. Resolución de muestreo ~{IntervalSeconds}s, no duración exacta del corte.");
            }
            report.AppendLine("\nConsultar red_eventos.csv y red_mediciones.csv para horas exactas de cambios y fallos; red_conexiones.jsonl contiene contextos completos.");
            return report.ToString();
        }
    }
    public string Export(byte[]? graph = null)
    {
        lock (_sync)
        {
            DurableFlush();
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NitroPulse", "Exportaciones", "Red");
            Directory.CreateDirectory(folder);
            string destination = Path.Combine(folder, "Diagnostico_red_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..6] + ".zip");
            string temporary = destination + ".tmp";
            try
            {
                using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
                {
                    using (var writer = new StreamWriter(archive.CreateEntry("Informe_red.txt").Open(), new UTF8Encoding(true))) writer.Write(Report());
                    using (var writer = new StreamWriter(archive.CreateEntry("red_nombres.json").Open(), new UTF8Encoding(true)))
                        writer.Write(JsonSerializer.Serialize(_segments.Select(s => new { tramo = s.Id, red = s.Name, identidad = s.Context.LabelKey })));
                    if (graph is not null) { using var output = archive.CreateEntry("grafica_ping.png").Open(); output.Write(graph); }
                    foreach (string name in new[] { "red_mediciones.csv", "red_eventos.csv", "red_conexiones.jsonl", "red_ruta.txt" })
                    {
                        string source = Path.Combine(_directory, name);
                        if (!File.Exists(source)) continue;
                        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var output = archive.CreateEntry(name).Open(); input.CopyTo(output);
                    }
                }
                File.Move(temporary, destination);
                return destination;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    public async Task<string> TraceAsync(CancellationToken cancel)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, _lifetime.Token);
        cancel = linked.Token;
        var before = NetworkIdentity.Read(); long epoch = Interlocked.Read(ref _epoch);
        var text = new StringBuilder($"Ruta orientativa a 1.1.1.1 (IPv4)\nFecha {DateTimeOffset.Now:O}\nRed: {before.Title}; interfaz {before.RouteCloudflare}\n");
        text.AppendLine("Hasta 20 saltos, un eco/salto, 600ms. Un salto sin respuesta no identifica una avería: puede filtrar ICMP.");
        if (before.Main is null) return text + "Sin ruta IPv4; no se ejecutó la prueba.";
        using var ping = new Ping();
        for (int ttl = 1; ttl <= 20; ttl++)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var reply = await ping.SendPingAsync(System.Net.IPAddress.Parse("1.1.1.1"), TimeSpan.FromMilliseconds(600), new byte[16], new PingOptions(ttl, true), cancel);
            text.AppendLine($"{ttl}: {reply.Address} | {reply.Status} | {(reply.Status is IPStatus.Success or IPStatus.TtlExpired ? clock.Elapsed.TotalMilliseconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " ms (tiempo de prueba)" : "—")}");
            if (reply.Status == IPStatus.Success) break;
        }
        if (NetworkIdentity.Read().Key != before.Key || epoch != Interlocked.Read(ref _epoch)) text.AppendLine("CAMBIO DE RED durante trazado: ruta no atribuible a una sola conexión.");
        lock (_sync)
        {
            if (_disposed) throw new OperationCanceledException(cancel);
            File.AppendAllText(Path.Combine(_directory, "red_ruta.txt"), text + "\n", Encoding.UTF8);
            Event(DateTimeOffset.Now, "Ruta manual", "Trazado registrado; no diagnostica pérdida por salto");
        }
        return text.ToString();
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _cancel?.Cancel();
            if (_loop is { IsCompleted: false }) throw new InvalidOperationException("Detén el monitoreo antes de liberar los archivos.");
            _disposed = true; _lifetime.Cancel(); NetworkChange.NetworkAddressChanged -= OnAddressChanged;
            _readings.Dispose(); _eventFile.Dispose(); _contexts.Dispose(); _cancel?.Dispose();
        }
    }
}
