using System.Drawing.Drawing2D;

namespace NitroPulse;

internal sealed class NetworkPlot : Control
{
    private readonly NetworkMonitor _monitor;
    private readonly ToolTip _tooltip = new() { AutoPopDelay = 16000 };
    private NetworkSample[] _samples = Array.Empty<NetworkSample>();
    private DateTimeOffset _from, _to;
    private RectangleF _area;
    private static readonly Color Google = Color.FromArgb(96, 165, 250);
    internal NetworkPlot(NetworkMonitor monitor)
    {
        _monitor = monitor; DoubleBuffered = true; BackColor = Theme.Panel; ForeColor = Theme.Text;
        MouseMove += (_, e) =>
        {
            if (_samples.Length == 0 || _area.Width <= 1) return;
            var hovered = _from.AddSeconds(Math.Clamp((e.X - _area.Left) / _area.Width, 0, 1) * (_to - _from).TotalSeconds);
            var sample = _samples.MinBy(s => Math.Abs((s.At - hovered).Ticks))!;
            _tooltip.SetToolTip(this, $"{sample.At:HH:mm:ss zzz} · {_monitor.NameFor(sample.Context)} · tramo {sample.Segment}\n{sample.Assessment}\n" +
                string.Join("\n", sample.Probes.Where(p => p.Protocol == "ICMP").Select(p => $"{p.Name}: {p.State} · {ProbeStatistics.Number(p.Milliseconds)} ms")));
        };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias; _samples = _monitor.History();
        using var font = new Font("Segoe UI", 8); using var title = new Font("Segoe UI Semibold", 10, FontStyle.Bold);
        using var text = new SolidBrush(Theme.Text); using var muted = new SolidBrush(Theme.Muted);
        g.DrawString("ESTABILIDAD DE PING · hasta 30 min", title, text, 14, 10);
        g.DrawString(NetworkTimeline.Summary(_samples), font, muted, 14, 32);
        var series = new[] { ("Router", Theme.Ram), ("Cloudflare", Theme.Cpu), ("Google", Google) };
        int legendX = 16;
        foreach (var (name, color) in series)
        { using var pen = new Pen(color, 2); g.DrawLine(pen, legendX, 56, legendX + 20, 56); g.DrawString(name, font, muted, legendX + 25, 48); legendX += 115; }
        g.DrawString("× ping sin respuesta (no equivale a caída general)", font, muted, legendX, 48);
        var area = new RectangleF(58, 76, Math.Max(1, Width - 78), Math.Max(20, Height - 132));
        double maximum = Math.Max(100, Math.Ceiling((_samples.SelectMany(s => s.Probes).Where(p => p.Protocol == "ICMP" && p.State == ProbeState.Ok)
            .Select(p => p.Milliseconds ?? 0).DefaultIfEmpty().Max()) / 50) * 50);
        var end = _samples.LastOrDefault()?.At ?? DateTimeOffset.Now;
        var start = _samples.Length == 0 ? end.AddMinutes(-2) : (end - _samples[0].At).TotalMinutes < 2 ? end.AddMinutes(-2) : end.AddMinutes(-30) > _samples[0].At ? end.AddMinutes(-30) : _samples[0].At;
        _from = start; _to = end; _area = area;
        float X(DateTimeOffset at) => area.Left + (float)((at - start).TotalSeconds / Math.Max(1, (end - start).TotalSeconds)) * area.Width;
        float Y(double ms) => area.Bottom - (float)(ms / maximum) * area.Height;
        using var grid = new Pen(Theme.Border, 1);
        for (int i = 0; i <= 4; i++)
        {
            float y = area.Top + area.Height * i / 4; g.DrawLine(grid, area.Left, y, area.Right, y);
            g.DrawString((maximum * (4 - i) / 4).ToString("0") + " ms", font, muted, 4, y - 6);
            var tick = start.AddSeconds((end - start).TotalSeconds * i / 4); float x = area.Left + area.Width * i / 4;
            g.DrawString(tick.ToString("HH:mm:ss"), font, muted, x - 26, area.Bottom + 25);
        }
        NetworkSample? previous = null; int markerCount = 0;
        foreach (var sample in _samples)
        {
            if (sample.At < start) { previous = sample; continue; }
            float x = X(sample.At);
            bool noLink = sample.Context.Links.Length == 0;
            if (noLink || NetworkTimeline.TcpFailure(sample))
            { using var band = new SolidBrush(Color.FromArgb(45, Theme.Gpu)); g.FillRectangle(band, Math.Max(area.Left, x - 4), area.Top, 8, area.Height + 15); }
            else if (sample.Context.Main is null)
            { using var band = new SolidBrush(Color.FromArgb(45, Theme.Ram)); g.FillRectangle(band, Math.Max(area.Left, x - 4), area.Top, 8, area.Height + 15); }
            if (previous is not null && !NetworkTimeline.CanJoin(previous, sample))
            {
                using var marker = new Pen(Theme.Ram, 1) { DashStyle = DashStyle.Dash }; g.DrawLine(marker, x, area.Top, x, area.Bottom + 18);
                string label = previous.Context.LabelKey == sample.Context.LabelKey ? "Reconexión / pausa" : _monitor.NameFor(sample.Context);
                // Every transition stays marked; alternate compact labels to avoid hiding the curves.
                g.DrawString(label.Length > 22 ? label[..21] + "…" : label, font, muted, Math.Clamp(x + 3, area.Left, Math.Max(area.Left, area.Right - 145)), area.Top + (markerCount++ % 3) * 15);
            }
            foreach (var (name, color) in series)
            {
                var probe = NetworkTimeline.Ping(sample, name);
                using var pen = new Pen(color, 1.8f);
                if (probe?.State == ProbeState.Ok && probe.Milliseconds is double ms)
                {
                    var prior = previous is null ? null : NetworkTimeline.Ping(previous, name);
                    if (previous is not null && previous.At >= start && NetworkTimeline.CanJoin(previous, sample) && prior?.State == ProbeState.Ok && prior.Milliseconds is double before)
                        g.DrawLine(pen, X(previous.At), Y(before), x, Y(ms));
                    using var dot = new SolidBrush(color); g.FillEllipse(dot, x - 2, Y(ms) - 2, 4, 4);
                }
                else if (probe?.State == ProbeState.Failed)
                {
                    using var lost = new Pen(Theme.Gpu, 1.5f); float y = area.Bottom + 9;
                    g.DrawLine(lost, x - 3, y - 3, x + 3, y + 3); g.DrawLine(lost, x - 3, y + 3, x + 3, y - 3);
                }
            }
            previous = sample;
        }
        if (_samples.Length == 0) g.DrawString("El historial aparecerá tras la primera medición.", font, muted, area.Left + 12, area.Top + 40);
        g.DrawString("Ámbar: cambio/reconexión · rojo: enlace/TCP falló · las pausas y pérdidas no se unen", font, muted, 14, Height - 17);
    }
    protected override void Dispose(bool disposing) { if (disposing) _tooltip.Dispose(); base.Dispose(disposing); }
}
