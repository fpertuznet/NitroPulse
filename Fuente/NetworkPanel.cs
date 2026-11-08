using System.Diagnostics;

namespace NitroPulse;

internal sealed class NetworkPanel : UserControl
{
    private readonly NetworkMonitor _monitor;
    private readonly Label _status;
    private readonly Label _hint;
    private readonly TextBox _details, _route, _alias;
    private readonly DataGridView _probes, _segments, _events;
    private readonly Button _toggle, _save;
    private readonly List<Button> _actionButtons = new();
    private readonly NetworkPlot _plot;
    private readonly FlatTabHost _detailsTabs;
    private readonly Panel _routePage;
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 1000 };
    private readonly CancellationTokenSource _cancel = new();
    private bool _busy;
    private string _aliasNetworkKey = "";
    private int _routeTabIndex;

    internal NetworkPanel(NetworkMonitor monitor)
    {
        _monitor = monitor; Dock = DockStyle.Fill; BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.FontBody;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(12), BackColor = Theme.Bg };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100)); Controls.Add(root);
        _status = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Cpu, Font = Theme.FontBold, Text = "Esperando primera medición..." };
        root.Controls.Add(_status, 0, 0);
        _plot = new NetworkPlot(monitor) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) }; root.Controls.Add(_plot, 0, 1);
        _detailsTabs = new FlatTabHost { Dock = DockStyle.Fill };
        _probes = Grid("Prueba", "Destino", "Protocolo", "Estado", "RTT ms", "Detalle");
        _segments = Grid("Tramo", "Red / etiqueta", "Inicio", "Última muestra", "Muestras", "Alertas");
        _segments.Columns[1].MinimumWidth = 150; _segments.Columns[2].MinimumWidth = 190;
        _events = Grid("Hora", "Tramo", "Evento", "Detalle");
        Panel Wrap(DataGridView grid)
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            grid.Dock = DockStyle.Fill;
            page.Controls.Add(grid);
            return page;
        }
        _detailsTabs.Add("Router / DNS", Wrap(_probes));
        _detailsTabs.Add("Conexiones", Wrap(_segments));
        _detailsTabs.Add("Eventos", Wrap(_events));
        TextBox TextView() => new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.FontBody };
        _details = TextView();
        var connection = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        connection.Controls.Add(_details);
        _detailsTabs.Add("Datos de la red", connection);
        _route = TextView();
        _routePage = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        _routePage.Controls.Add(_route);
        _detailsTabs.Add("Ruta", _routePage);
        _routeTabIndex = 4;
        root.Controls.Add(_detailsTabs, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 8, 0, 0), BackColor = Theme.Bg };
        _toggle = Button("Pausar red", "pause", async () =>
        {
            if (_busy) return; _busy = true;
            try { if (_monitor.Running) await _monitor.StopAsync(); else _monitor.Start(); }
            finally { _busy = false; RefreshView(); }
        });
        _alias = new TextBox { Width = 180, PlaceholderText = "Nombre para esta red", AccessibleName = "Etiqueta persistente de la red",
            BackColor = Theme.Panel, ForeColor = Theme.Text, Margin = new Padding(8), MaxLength = 100, BorderStyle = BorderStyle.FixedSingle };
        _save = Button("Guardar etiqueta", "tag", () => { _monitor.RenameCurrent(_alias.Text, _aliasNetworkKey); RefreshView(); return Task.CompletedTask; });
        _save.Enabled = false;
        actions.Controls.Add(_toggle); actions.Controls.Add(_alias); actions.Controls.Add(_save);
        actions.Controls.Add(Button("Medir ruta", "route", async () =>
        {
            if (_busy) return; _busy = true; _detailsTabs.SelectedIndex = _routeTabIndex;
            try { _route.Text = "Midiendo ruta: hasta 20 saltos, sin interrumpir el monitoreo..."; string result = await _monitor.TraceAsync(_cancel.Token); if (!IsDisposed) _route.Text = result; }
            finally { _busy = false; }
        }));
        actions.Controls.Add(Button("Exportar para operador", "file-down", async () =>
        {
            if (MessageBox.Show(this, "El ZIP incluye nombres y etiquetas de redes, SSID/BSSID, direcciones IP, DNS, rutas y horarios. Se guardará solo en este equipo; no se enviará al operador. Revisa el contenido antes de compartirlo.\n\n¿Crear el informe?", "Exportar diagnóstico de red", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            using var bitmap = new Bitmap(_plot.Width, _plot.Height);
            _plot.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            using var png = new MemoryStream(); bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            byte[] graph = png.ToArray();
            string path = await Task.Run(() => _monitor.Export(graph));
            if (IsDisposed) return;
            if (MessageBox.Show(this, "Diagnóstico guardado:\n" + path + "\n\nIncluye SSID/BSSID, IP y DNS; revísalo antes de enviarlo. No incluye contraseñas ni registros de procesos.\n\n¿Abrir la carpeta?", "Informe de red", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Path.GetDirectoryName(path) + "\"") { UseShellExecute = true });
        }));
        _hint = new Label { AutoSize = true, ForeColor = Theme.Muted, Text = $"Pruebas cada {NetworkMonitor.IntervalSeconds}s · etiquetas guardadas por red · un ping perdido no demuestra una caída", Margin = new Padding(8) };
        actions.Controls.Add(_hint);
        root.Controls.Add(actions, 0, 3);
        _refresh.Tick += (_, _) => RefreshView();
        HandleCreated += (_, _) => { RefreshView(); _refresh.Start(); };
        VisibleChanged += (_, _) => { if (Visible) RefreshView(); };
        Theme.Changed += ApplyTheme;
        Disposed += (_, _) => Theme.Changed -= ApplyTheme;
    }

    internal void ApplyTheme()
    {
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.FontBody;
        Theme.ApplyControlTree(this);
        _status.Font = Theme.FontBold;
        _hint.ForeColor = Theme.Muted;
        _alias.BackColor = Theme.Panel;
        _alias.ForeColor = Theme.Text;
        foreach (var grid in new[] { _probes, _segments, _events }) Theme.StyleGrid(grid);
        foreach (var button in _actionButtons)
        {
            Theme.StyleButton(button);
            LucideIcons.Apply(button, LucideIcons.ForText(button.Text));
        }
        _detailsTabs.ApplyTheme();
        _plot.Invalidate();
        RefreshView();
    }

    private Button Button(string text, string icon, Func<Task> action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 34, Margin = new Padding(4) };
        Theme.StyleButton(button);
        LucideIcons.Apply(button, icon);
        button.Click += async (_, _) =>
        {
            button.Enabled = false;
            try { await action(); }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!IsDisposed) MessageBox.Show(this, e.Message, "NitroPulse · Red", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { if (!IsDisposed) button.Enabled = true; }
        };
        _actionButtons.Add(button);
        return button;
    }
    private static DataGridView Grid(params string[] names)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        Theme.StyleGrid(grid);
        foreach (string name in names) grid.Columns.Add(name, name);
        grid.Columns[^1].FillWeight = 220; return grid;
    }
    internal void RefreshView()
    {
        if (IsDisposed) return;
        // Skip heavy UI work when the Red tab or window is hidden (low resource).
        if (!Visible || !IsHandleCreated) return;
        var form = FindForm();
        if (form is null || !form.Visible || form.WindowState == FormWindowState.Minimized) return;

        _toggle.Text = _monitor.Running ? "Pausar red" : "Reanudar red"; LucideIcons.Apply(_toggle, _monitor.Running ? "pause" : "play");
        var latest = _monitor.Latest;
        _status.Text = !string.IsNullOrEmpty(_monitor.Error) ? _monitor.Error : latest is null ? "Esperando primera medición..." :
            $"{(_monitor.Running ? "ACTIVO" : "PAUSADO")} · {_monitor.NameFor(latest.Context)} · Tramo {latest.Segment}\n{latest.Assessment}";
        _status.ForeColor = !string.IsNullOrEmpty(_monitor.Error) || latest?.Context.Main is null ? Theme.Ram :
            latest.Assessment.StartsWith("Posible") ? Theme.Gpu : !_monitor.Running ? Theme.Muted : Theme.Cpu;
        if (latest is not null)
        {
            _details.Text = string.Join(Environment.NewLine, latest.Context.Links.Select(l =>
                $"{l.Title} · {l.Name} ({l.Type}) · perfil: {l.ProfileId} {l.ProfileNote}\r\nSSID: {l.Ssid} · BSSID: {l.Bssid} · señal: {l.Signal?.ToString() ?? "—"}%\r\nIP: {string.Join(", ", l.Addresses)} · router: {string.Join(", ", l.Gateways)} · DNS: {string.Join(", ", l.Dns)} {l.WifiNote}"));
            if (!_alias.Focused || string.IsNullOrEmpty(_aliasNetworkKey)) { _aliasNetworkKey = latest.Context.LabelKey; if (!_alias.Focused) _alias.Text = _monitor.CurrentLabel; }
            _save.Enabled = !_busy && !string.IsNullOrEmpty(latest.Context.LabelKey);
        }
        _probes.Rows.Clear();
        if (latest is not null) foreach (var p in latest.Probes) _probes.Rows.Add(p.Name, p.Target, p.Protocol,
            p.State == ProbeState.Ok ? "Responde" : p.State == ProbeState.Failed ? "Sin respuesta / error" : "Inconcluso", ProbeStatistics.Number(p.Milliseconds), p.Detail);
        _segments.Rows.Clear(); foreach (var row in _monitor.Rows()) _segments.Rows.Add(row.Id, row.Network, row.Start, row.Last, row.Samples, row.Alerts);
        _events.Rows.Clear(); foreach (var row in _monitor.Events()) _events.Rows.Add(row.At.ToString("HH:mm:ss zzz"), row.Segment, row.Kind, row.Detail);
        _plot.Invalidate();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _refresh.Stop(); _refresh.Dispose(); _cancel.Cancel(); }
        base.Dispose(disposing);
    }
}
