using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Security.Principal;

namespace NitroPulse;

internal sealed class MonitorForm : Form
{
    private readonly HardwareReader _hardware;
    private readonly SystemMetrics _system = new();
    private readonly WindowsCounters _counters = new();
    private readonly ProcessSampler _processSampler = new();
    private readonly CsvLogger _logger = new();
    private readonly NetworkMonitor _network;
    private readonly TabControl _pages;
    private readonly NetworkPanel _networkPanel;
    private readonly List<MetricSnapshot> _history = new();
    private readonly List<NotifyIcon> _trayIcons = new();
    private readonly string[] _lastTrayText = new string[5];
    private readonly MetricCard[] _cards;
    private readonly HistoryPlot _loadPlot;
    private readonly HistoryPlot _tempPlot;
    private readonly DataGridView _processGrid;
    private readonly Label _status;
    private readonly Label _footer;
    private readonly Label _sensorAlert;
    private readonly Button _pauseButton;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 21600000 };
    private readonly CancellationTokenSource _updateCancellation = new();
    private bool _checkingUpdate;
    private ReadyUpdate? _readyUpdate;
    private readonly Button _installUpdate = new() { Text = "Actualización disponible · Instalar", Visible = false, AutoSize = true, BackColor = Theme.Cpu, ForeColor = Color.Black, FlatStyle = FlatStyle.Flat };
    private ToolStripMenuItem? _installUpdateMenu;
    private readonly ContextMenuStrip _trayMenu = new();
    private ToolStripMenuItem? _toggleLogging;
    private ToolStripMenuItem? _autoStart;
    private bool _busy;
    private bool _exiting;
    private DateTime _lastProcessAt = DateTime.MinValue;
    private bool _firstMinimizeHint;
    private int _errorsShown;
    private bool _cpuSensorReportSaved;
    private bool _autoSaveBusy;
    private bool _autoSaveFailed;
    private DateTime? _lastAutoSaveAt;
    private DateTime _nextAutoSaveUtc = DateTime.UtcNow.AddMinutes(CsvLogger.AutomaticBackupMinutes);
    private long _lastAutoSaveSamples;

    // Tray order requested by the user: CPU C, GPU C, CPU %, GPU %, RAM %.
    private static readonly Color[] IconColors = { Theme.CpuTemp, Theme.GpuTemp, Theme.Cpu, Theme.Gpu, Theme.Ram };
    private static readonly string[] IconLabels = { "Temperatura CPU", "Temperatura GPU", "Uso CPU", "Uso GPU", "Uso RAM" };
    private static readonly TrayMetricKind[] IconKinds =
    {
        TrayMetricKind.TemperatureNumber,
        TrayMetricKind.TemperatureNumber,
        TrayMetricKind.VerticalBar,
        TrayMetricKind.VerticalBar,
        TrayMetricKind.VerticalBar
    };

    public MonitorForm(bool safeMode = true)
    {
        _hardware = new HardwareReader(safeMode);
        _network = new NetworkMonitor(_logger.SessionDirectory);
        Text = "NitroPulse " + AppVersion.Display + " · Recursos y red";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Size = new Size(1080, 780);
        MinimumSize = new Size(855, 650);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.5f);

        _pages = NitroTabs.Create();
        var resourcesPage = new TabPage("Recursos") { BackColor = Theme.Bg };
        var networkPage = new TabPage("Red") { BackColor = Theme.Bg };
        _networkPanel = new NetworkPanel(_network); networkPage.Controls.Add(_networkPanel);
        _pages.TabPages.Add(resourcesPage); _pages.TabPages.Add(networkPage); Controls.Add(_pages);
        var noticesPage = new TabPage("Privacidad y licencias") { BackColor = Theme.Bg };
        noticesPage.Controls.Add(new NoticesPanel()); _pages.TabPages.Add(noticesPage);
        // Resource monitoring remains independent of the network tab.
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18, 12, 18, 8),
            BackColor = Theme.Bg
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 144));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 228));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
        resourcesPage.Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        var heading = new Label
        {
            Text = "NitroPulse", ForeColor = Theme.Text,
            Font = new Font("Segoe UI Semibold", 22, FontStyle.Bold),
            Location = new Point(7, 2), Size = new Size(320, 40)
        };
        var tagline = new Label
        {
            Text = "Monitor en tiempo real · Windows 10/11 · datos locales",
            ForeColor = Theme.Muted, Location = new Point(10, 43), AutoSize = true
        };
        _status = new Label
        {
            Text = "● Inicializando sensores...", TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Theme.Cpu, AutoSize = false, Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Size = new Size(390, 32), Location = new Point(620, 17)
        };
        header.Resize += (_, _) => _status.Left = Math.Max(335, header.Width - _status.Width - 10);
        header.Controls.AddRange(new Control[] { heading, tagline, _status });
        _installUpdate.Location = new Point(335, 38);
        _installUpdate.Click += async (_, _) => await InstallUpdateAsync();
        LucideIcons.Apply(_installUpdate, "download");
        header.Controls.Add(_installUpdate);
        root.Controls.Add(header, 0, 0);

        var cardsPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1 };
        for (int i = 0; i < 5; i++) cardsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        cardsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _cards = new[]
        {
            new MetricCard("CPU", Theme.Cpu),
            new MetricCard("GPU NVIDIA", Theme.Gpu),
            new MetricCard("RAM", Theme.Ram),
            new MetricCard("TEMP. CPU", Theme.CpuTemp),
            new MetricCard("TEMP. GPU", Theme.GpuTemp)
        };
        for (int i = 0; i < _cards.Length; i++)
        {
            _cards[i].Dock = DockStyle.Fill;
            cardsPanel.Controls.Add(_cards[i], i, 0);
        }
        root.Controls.Add(cardsPanel, 0, 1);

        var charts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        charts.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _loadPlot = new HistoryPlot(false, _history) { Dock = DockStyle.Fill };
        _tempPlot = new HistoryPlot(true, _history) { Dock = DockStyle.Fill };
        charts.Controls.Add(_loadPlot, 0, 0);
        charts.Controls.Add(_tempPlot, 1, 0);
        root.Controls.Add(charts, 0, 2);

        var lowerPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5, 7, 5, 0), BackColor = Theme.Bg };
        var label = new Label
        {
            Dock = DockStyle.Top, Height = 31, Text = "Procesos principales  ·  memoria y CPU (cada 15 segundos)",
            Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Theme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _processGrid = BuildGrid();
        lowerPanel.Controls.Add(_processGrid);
        lowerPanel.Controls.Add(label);
        root.Controls.Add(lowerPanel, 0, 3);

        var footerPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1 };
        footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));
        footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        _footer = new Label { ForeColor = Theme.Muted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        _pauseButton = CreateButton("Pausar registro", Theme.PanelAlt, () => ToggleLogging());
        var exportButton = CreateButton("Exportar ZIP", Theme.Gpu, async () => await ExportAsync());
        var sensorButton = CreateButton("Ver sensores", Theme.PanelAlt, ShowSensors);
        footerPanel.Controls.Add(_footer, 0, 0);
        footerPanel.Controls.Add(_pauseButton, 1, 0);
        footerPanel.Controls.Add(exportButton, 2, 0);
        footerPanel.Controls.Add(sensorButton, 3, 0);
        footerPanel.Controls.Add(CreateButton("Actualizaciones", Theme.PanelAlt, ShowUpdates), 4, 0);
        root.Controls.Add(footerPanel, 0, 4);

        _sensorAlert = new Label
        {
            ForeColor = Theme.Muted,
            Text = "— = sensor no disponible; puede requerir permisos de administrador.",
            Font = new Font("Segoe UI", 8), Dock = DockStyle.Bottom, Height = 18,
            TextAlign = ContentAlignment.MiddleRight
        };
        // Status note appears as footer tooltip as not to reduce process table space.
        var statusTooltip = new ToolTip { AutoPopDelay = 12000 };
        _footer.MouseHover += (_, _) => statusTooltip.SetToolTip(_footer, _sensorAlert.Text);

        SetupTrayMenu();
        SetupTrayIcons();
        _timer.Tick += async (_, _) => await SampleAsync();
        _updateTimer.Tick += async (_, _) => await CheckUpdatesAsync();
        Shown += async (_, _) =>
        {
            _network.Start();
            if (Environment.GetCommandLineArgs().Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)))
                BeginInvoke(new Action(HideToTray));
            await SampleAsync();
            _timer.Start();
            _updateTimer.Start();
            await CheckUpdatesAsync();
        };
        FormClosing += OnClosing;
    }

    private static Button CreateButton(string text, Color back, Action action)
    {
        var b = new Button
        {
            Text = text, BackColor = back, ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat, Dock = DockStyle.Fill,
            Margin = new Padding(4, 5, 4, 5), Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        b.FlatAppearance.BorderSize = 0;
        LucideIcons.Apply(b, LucideIcons.ForText(text));
        b.Click += (_, _) => action();
        return b;
    }

    private static DataGridView BuildGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            MultiSelect = false, RowHeadersVisible = false,
            BackgroundColor = Theme.Panel, BorderStyle = BorderStyle.None,
            GridColor = Theme.Border, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = 34, RowTemplate = { Height = 29 },
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.PanelAlt, ForeColor = Theme.Text, SelectionBackColor = Theme.PanelAlt,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold), Padding = new Padding(5)
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Panel, ForeColor = Theme.Text,
            SelectionBackColor = Color.FromArgb(54, 69, 96), SelectionForeColor = Theme.Text,
            Font = new Font("Segoe UI", 9f), Padding = new Padding(5)
        };
        grid.Columns.Add("name", "Proceso");
        grid.Columns.Add("instances", "Instancias");
        grid.Columns.Add("ram", "RAM MB");
        grid.Columns.Add("private", "Privada MB");
        grid.Columns.Add("cpu", "CPU %");
        grid.Columns[0].FillWeight = 160;
        foreach (DataGridViewColumn column in grid.Columns.Cast<DataGridViewColumn>().Skip(1))
        {
            column.FillWeight = 65;
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        return grid;
    }

    private void SetupTrayMenu()
    {
        var open = new ToolStripMenuItem("Abrir panel NitroPulse", null, (_, _) => ShowDashboard());
        _toggleLogging = new ToolStripMenuItem("Pausar registro", null, (_, _) => ToggleLogging());
        _autoStart = new ToolStripMenuItem("Iniciar con Windows", null, (_, _) => ToggleStartup())
        {
            Checked = StartupManager.Enabled, CheckOnClick = false
        };
        _trayMenu.Items.Add(open);
        _trayMenu.Items.Add(_toggleLogging);
        _trayMenu.Items.Add(new ToolStripMenuItem("Exportar sesión ZIP", null, async (_, _) => await ExportAsync()));
        _trayMenu.Items.Add(new ToolStripMenuItem("Abrir carpeta de registros", null, (_, _) => OpenFolder(_logger.SessionDirectory)));
        _trayMenu.Items.Add(new ToolStripMenuItem("Abrir copias automáticas", null, (_, _) =>
        {
            Directory.CreateDirectory(_logger.AutomaticBackupDirectory);
            OpenFolder(_logger.AutomaticBackupDirectory);
        }));
        _trayMenu.Items.Add(new ToolStripMenuItem("Abrir copias finales", null, (_, _) =>
        {
            Directory.CreateDirectory(_logger.FinalBackupDirectory);
            OpenFolder(_logger.FinalBackupDirectory);
        }));
        _trayMenu.Items.Add(new ToolStripMenuItem("Ver sensores detectados", null, (_, _) => ShowSensors()));
        _trayMenu.Items.Add(new ToolStripMenuItem("Red / Internet · diagnóstico", null, (_, _) => ShowNetwork()));
        _trayMenu.Items.Add(new ToolStripMenuItem("Actualizaciones · " + AppVersion.Display, null, (_, _) => ShowUpdates()));
        _trayMenu.Items.Add(new ToolStripMenuItem("Privacidad y licencias", null, (_, _) => { _pages.SelectedIndex = 2; ShowDashboard(); }));
        _installUpdateMenu = new ToolStripMenuItem("Actualización disponible · Instalar", null, async (_, _) => await InstallUpdateAsync()) { Visible = false };
        _trayMenu.Items.Add(_installUpdateMenu);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(_autoStart);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(new ToolStripMenuItem("Salir", null, async (_, _) => await ExitAsync()));
        _trayMenu.ImageScalingSize = new Size(18, 18);
        foreach (var item in _trayMenu.Items.OfType<ToolStripMenuItem>())
        {
            string caption = item.Text ?? "";
            string icon = caption.StartsWith("Abrir panel") ? "monitor" : caption.StartsWith("Abrir") ? "folder-open" :
                caption.StartsWith("Red") ? "network" : caption is "Salir" or "Iniciar con Windows" ? "power" : LucideIcons.ForText(caption);
            item.Image = LucideIcons.Get(icon, item.ForeColor, 18);
        }
    }

    private void SetupTrayIcons()
    {
        for (int i = 0; i < IconLabels.Length; i++)
        {
            var icon = new NotifyIcon
            {
                Icon = TrayIconRenderer.Create("--", IconColors[i], IconKinds[i], null),
                Text = $"NitroPulse · {IconLabels[i]}: esperando",
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            icon.MouseDoubleClick += (_, _) => ShowDashboard();
            icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowDashboard(); };
            _trayIcons.Add(icon);
        }
    }

    private async Task SampleAsync()
    {
        if (_busy || _exiting) return;
        _busy = true;
        try
        {
            var result = await Task.Run(() =>
            {
                DateTime now = DateTime.Now;
                var sys = _system.Read();
                var hardware = _hardware.Read();
                var io = _counters.Read();
                var sample = new MetricSnapshot(now, sys.Cpu, hardware.GpuPercent,
                    sys.RamPercent, sys.RamUsedGB, sys.RamAvailableGB, sys.RamTotalGB,
                    sys.CommittedGB, sys.CommitLimitGB, sys.NonpagedMB, sys.PagedMB,
                    io.PageReadsPerSec, io.PagesInputPerSec, io.DiskReadMBps, io.DiskWriteMBps, io.DiskQueue,
                    hardware.CpuTemperature, hardware.GpuTemperature, hardware.CpuSource,
                    hardware.GpuTemperatureSource, hardware.GpuLoadSource, hardware.Status);
                _logger.WriteSystem(sample);
                List<ProcessSample>? processes = null;
                if (_lastProcessAt == DateTime.MinValue || (now - _lastProcessAt).TotalSeconds >= 15)
                {
                    processes = _processSampler.Read();
                    _logger.WriteProcesses(now, processes);
                    _lastProcessAt = now;
                }
                return (sample, processes);
            });
            if (_exiting || IsDisposed || Disposing) return;
            UpdateUi(result.sample, result.processes);
            if (!_exiting && !_autoSaveBusy && DateTime.UtcNow >= _nextAutoSaveUtc
                && _logger.Samples > _lastAutoSaveSamples)
                _ = AutoSaveAsync();
        }
        catch (Exception e)
        {
            _logger.Error("Muestreo", e);
            if (!_exiting && _errorsShown++ < 1)
                MessageBox.Show("Error leyendo métricas: " + e.Message + "\nRevisa errores.log en la sesión.",
                    "NitroPulse", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { _busy = false; }
    }

    private void UpdateUi(MetricSnapshot s, List<ProcessSample>? processes)
    {
        _history.Add(s);
        if (_history.Count > 350) _history.RemoveRange(0, _history.Count - 350);
        string Pct(double? val) => val.HasValue ? Math.Round(val.Value).ToString(CultureInfo.InvariantCulture) + "%" : "—";
        string Degrees(double? val) => val.HasValue ? Math.Round(val.Value).ToString(CultureInfo.InvariantCulture) + "°" : "—";
        _cards[0].SetValue(Pct(s.CpuPercent), "8 núcleos · 12 hilos", s.CpuPercent);
        _cards[1].SetValue(Pct(s.GpuPercent), s.GpuPercent.HasValue ? "RTX 2050" : "Sensor no disponible", s.GpuPercent);
        _cards[2].SetValue(Pct(s.RamPercent), $"{s.RamUsedGB:0.0} / {s.RamTotalGB:0.0} GB", s.RamPercent);
        _cards[3].SetValue(Degrees(s.CpuTemperature), s.CpuTemperature.HasValue ? "Sensor: " + (s.CpuTemperatureSensor.Contains("HWiNFO", StringComparison.OrdinalIgnoreCase) ? "HWiNFO" : s.CpuTemperatureSensor.Contains("Afterburner", StringComparison.OrdinalIgnoreCase) ? "Afterburner" : "CPU") : "Ver sensores", s.CpuTemperature.HasValue ? s.CpuTemperature / 110d * 100 : null);
        _cards[4].SetValue(Degrees(s.GpuTemperature), s.GpuTemperature.HasValue ? "Temperatura real" : "No disponible", s.GpuTemperature.HasValue ? s.GpuTemperature / 110d * 100 : null);
        _loadPlot.Invalidate();
        _tempPlot.Invalidate();
        _status.Text = _logger.LoggingEnabled ? "● REGISTRANDO · cada 2 s" : "● PAUSADO · medición activa";
        _status.ForeColor = _logger.LoggingEnabled ? Theme.Cpu : Theme.Ram;
        string backupStatus = _autoSaveFailed ? "ZIP ERROR" : (_lastAutoSaveAt.HasValue ? _lastAutoSaveAt.Value.ToString("HH:mm") : "pendiente");
        _footer.Text = $"Sesión {_logger.StartAt:HH:mm} · {_logger.Samples:N0} muestras · Compromiso {s.CommittedGB:0.0} GB · ZIP {backupStatus}";
        _sensorAlert.Text = string.IsNullOrWhiteSpace(s.HardwareStatus) ? "Temperaturas leídas desde sensores del equipo" : s.HardwareStatus;
        if (s.CpuTemperature is null && !_cpuSensorReportSaved)
        {
            _cpuSensorReportSaved = true;
            SaveSensorReport(_hardware.SensorReport());
        }

        if (processes is not null)
        {
            _processGrid.Rows.Clear();
            foreach (var p in processes.Take(15))
                _processGrid.Rows.Add(p.Name, p.Instances, p.RamMB.ToString("N0"), p.PrivateMB.ToString("N0"), p.CpuPercent.ToString("0.0"));
        }
        double?[] values = { s.CpuTemperature, s.GpuTemperature, s.CpuPercent, s.GpuPercent, s.RamPercent };
        for (int i = 0; i < values.Length; i++)
        {
            bool isTemperature = IconKinds[i] == TrayMetricKind.TemperatureNumber;
            string exactValue = values[i].HasValue ? Math.Round(values[i]!.Value).ToString(CultureInfo.InvariantCulture) : "--";
            string tooltip = $"NitroPulse | {IconLabels[i]}: " +
                (values[i].HasValue ? exactValue + (isTemperature ? " °C" : " %") : "sin sensor");
            // On temperatures >= 100C show HI instead of an incorrect 2-digit number.
            string iconText = isTemperature && values[i] >= 100 ? "HI" : exactValue;
            if (_lastTrayText[i] != iconText)
            {
                var tray = _trayIcons[i];
                var oldIcon = tray.Icon;
                tray.Icon = TrayIconRenderer.Create(iconText, IconColors[i], IconKinds[i], values[i]);
                oldIcon?.Dispose();
                _lastTrayText[i] = iconText;
            }
            _trayIcons[i].Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
        }
    }

    private async Task AutoSaveAsync()
    {
        if (_autoSaveBusy || _exiting) return;
        _autoSaveBusy = true;
        long currentSamples = _logger.Samples;
        try
        {
            await Task.Run(() => _logger.ExportAutomaticBackup());
            _lastAutoSaveSamples = currentSamples;
            _lastAutoSaveAt = DateTime.Now;
            _autoSaveFailed = false;
        }
        catch (Exception e)
        {
            _autoSaveFailed = true;
            _logger.Error("Autoguardado ZIP", e);
            // Keep CSV recording normally; retry the snapshot after one minute.
        }
        finally
        {
            _nextAutoSaveUtc = DateTime.UtcNow.AddMinutes(_autoSaveFailed ? 1 : CsvLogger.AutomaticBackupMinutes);
            _autoSaveBusy = false;
        }
    }

    private void ToggleLogging()
    {
        _logger.LoggingEnabled = !_logger.LoggingEnabled;
        _pauseButton.Text = _logger.LoggingEnabled ? "Pausar registro" : "Reanudar registro";
        LucideIcons.Apply(_pauseButton, _logger.LoggingEnabled ? "pause" : "play");
        if (_toggleLogging is not null) { _toggleLogging.Text = _pauseButton.Text; _toggleLogging.Image = LucideIcons.Get(_logger.LoggingEnabled ? "pause" : "play", _toggleLogging.ForeColor, 18); }
    }

    private void ToggleStartup()
    {
        try
        {
            StartupManager.SetEnabled(!StartupManager.Enabled);
            if (_autoStart is not null) _autoStart.Checked = StartupManager.Enabled;
        }
        catch (Exception e) { MessageBox.Show(e.Message, "Inicio automático", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private async Task ExportAsync()
    {
        if (MessageBox.Show(this, "El ZIP contiene métricas, nombres de procesos y datos de diagnóstico del equipo. Se guarda localmente y no se envía automáticamente. Revisa el contenido antes de compartirlo.\n\n¿Crear la exportación?", "NitroPulse · Exportar sesión", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            string zip = await Task.Run(() => _logger.ExportZip());
            var result = MessageBox.Show($"Sesión exportada:\n{zip}\n\n¿Abrir la carpeta?",
                "NitroPulse · Exportación completada", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (result == DialogResult.Yes) OpenFolder(Path.GetDirectoryName(zip)!);
        }
        catch (Exception e)
        {
            _logger.Error("Exportar", e);
            MessageBox.Show("No se pudo generar el ZIP: " + e.Message, "NitroPulse", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (Exception e) { MessageBox.Show(e.Message, "Abrir carpeta", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SaveSensorReport(string report)
    {
        try { File.WriteAllText(Path.Combine(_logger.SessionDirectory, "sensores.txt"), report); }
        catch (Exception e) { _logger.Error("Guardar diagnostico de sensores", e); }
    }

    private void ShowSensors()
    {
        string report = _hardware.SensorReport();
        SaveSensorReport(report);
        using var dialog = new Form
        {
            Text = "NitroPulse · Sensores detectados", Size = new Size(800, 600),
            StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Bg,
            ForeColor = Theme.Text, Font = new Font("Consolas", 9)
        };
        var view = new TextBox
        {
            Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false,
            ReadOnly = true, Dock = DockStyle.Fill, BackColor = Theme.Panel,
            ForeColor = Theme.Text, Text = report
        };
        var copy = CreateButton("Copiar diagnóstico", Theme.PanelAlt, () =>
        {
            try { Clipboard.SetText(report); }
            catch (Exception e) { MessageBox.Show(e.Message, "Portapapeles", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        });
        copy.Dock = DockStyle.Bottom;
        copy.Height = 43;
        dialog.Controls.Add(view);
        dialog.Controls.Add(copy);
        dialog.ShowDialog(this);
    }

    private void ShowUpdates()
    {
        using var dialog = new UpdateDialog(SetReadyUpdate);
        dialog.ShowDialog(this);
    }

    private void ShowNetwork()
    {
        _pages.SelectedIndex = 1; _networkPanel.RefreshView(); ShowDashboard();
    }

    private async Task CheckUpdatesAsync()
    {
        if (_checkingUpdate || _exiting) return;
        var settings = UpdateChecker.Load();
        if (!settings.Automatic || !UpdateChecker.TryRepository(settings.Repository, out var repository)) return;
        _checkingUpdate = true;
        try
        {
            var release = await UpdateChecker.LatestAsync(repository, _updateCancellation.Token);
            if (!_exiting && !IsDisposed && release is not null && release.Version > AppVersion.Current &&
                (_readyUpdate is null || release.Version > _readyUpdate.Release.Version))
            {
                var ready = await UpdateInstaller.DownloadAsync(release, _updateCancellation.Token);
                if (!_exiting && !IsDisposed) SetReadyUpdate(ready);
            }
        }
        catch (Exception e) when (e is IOException or HttpRequestException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException or System.Text.Json.JsonException or KeyNotFoundException or System.ComponentModel.Win32Exception)
        {
            // A failed update check must not interrupt hardware monitoring.
        }
        finally { _checkingUpdate = false; }
    }

    private void SetReadyUpdate(ReadyUpdate ready)
    {
        if (_exiting) return;
        _readyUpdate = ready;
        _installUpdate.Visible = true;
        if (_installUpdateMenu is not null) _installUpdateMenu.Visible = true;
    }

    private async Task InstallUpdateAsync()
    {
        if (_readyUpdate is null || _exiting) return;
        _installUpdate.Enabled = false;
        try
        {
            using var installer = UpdateInstaller.StartInstall(_readyUpdate);
            await Task.Delay(800);
            if (installer.HasExited) throw new IOException("El instalador no pudo iniciarse. La aplicación actual no se modificó.");
            await ExitAsync();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(e.Message, "NitroPulse · Actualización", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _installUpdate.Enabled = true;
        }
    }

    private void ShowDashboard()
    {
        if (_exiting) return;
        if (!Visible) Show();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        if (!_firstMinimizeHint && _trayIcons.Count > 0)
        {
            _firstMinimizeHint = true;
            _trayIcons[0].ShowBalloonTip(3500, "NitroPulse activo",
                "Los cinco indicadores siguen junto al reloj. Revisa también la flecha ^ para mostrar iconos ocultos.",
                ToolTipIcon.Info);
        }
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason != CloseReason.WindowsShutDown && e.CloseReason != CloseReason.TaskManagerClosing)
        {
            e.Cancel = true;
            HideToTray();
        }
        else if (e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing)
        {
            // Never block Windows shutdown. CSV data is flushed on every sample.
            _exiting = true;
            _timer.Stop();
            _updateTimer.Stop();
            _updateCancellation.Cancel();
            _network.Cancel();
        }
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        _timer.Stop();
        _updateTimer.Stop();
        _updateCancellation.Cancel();
        await _network.StopAsync();
        _status.Text = "● GUARDANDO SESIÓN FINAL...";
        _status.ForeColor = Theme.Ram;
        // Wait for the in-flight sample and rolling archive before finalizing.
        while (_busy || _autoSaveBusy) await Task.Delay(75);

        string? finalBackupError = null;
        try
        {
            await Task.Run(() => _logger.ExportFinalZip());
        }
        catch (Exception e)
        {
            _logger.Error("Copia final al salir", e);
            finalBackupError = e.Message;
        }

        foreach (var icon in _trayIcons)
        {
            icon.Visible = false;
            var native = icon.Icon;
            icon.Dispose();
            native?.Dispose();
        }
        _trayMenu.Dispose();
        _hardware.Dispose();
        _network.Dispose();
        _counters.Dispose();
        _logger.Dispose();
        if (finalBackupError is not null)
            MessageBox.Show("No se pudo crear el ZIP final: " + finalBackupError +
                "\nLos CSV originales siguen guardados en:\n" + _logger.SessionDirectory,
                "NitroPulse · Registros conservados", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        Close();
    }
}
