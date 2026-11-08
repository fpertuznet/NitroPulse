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
    private readonly FlatTabHost _pages;
    private readonly NetworkPanel _networkPanel;
    private readonly SettingsPanel _settingsPanel;
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
    private readonly Label _heading;
    private readonly Label _tagline;
    private readonly Label _processLabel;
    private readonly Panel _header;
    private readonly Panel _lowerPanel;
    private readonly TableLayoutPanel _root;
    private readonly Button _pauseButton;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 21600000 };
    private readonly CancellationTokenSource _updateCancellation = new();
    private bool _checkingUpdate;
    private ReadyUpdate? _readyUpdate;
    private readonly Button _installUpdate = new() { Text = "Actualización disponible · Instalar", Visible = false, AutoSize = true, FlatStyle = FlatStyle.Flat };
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
        Theme.Initialize();
        _hardware = new HardwareReader(safeMode);
        _network = new NetworkMonitor(_logger.SessionDirectory);
        Text = "NitroPulse " + AppVersion.Display + " · Recursos y red";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Size = new Size(1080, 780);
        MinimumSize = new Size(855, 650);
        StartPosition = FormStartPosition.CenterScreen;
        Font = Theme.FontBody;

        _pages = new FlatTabHost { Dock = DockStyle.Fill };
        var resourcesPage = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        var networkPage = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        _networkPanel = new NetworkPanel(_network); networkPage.Controls.Add(_networkPanel);
        var settingsPage = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        _settingsPanel = new SettingsPanel(ExportAsync, () => _hardware.SensorReport(), SaveSensorReport, SetReadyUpdate);
        settingsPage.Controls.Add(_settingsPanel);
        _pages.Add("Recursos", resourcesPage, "monitor");
        _pages.Add("Red", networkPage, "network");
        _pages.Add("Configuración", settingsPage, "settings");
        Controls.Add(_pages);
        // Resource monitoring remains independent of the network tab.
        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18, 12, 18, 8)
        };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 144));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 228));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
        resourcesPage.Controls.Add(_root);

        _header = new Panel { Dock = DockStyle.Fill };
        _heading = new Label
        {
            Text = "NitroPulse",
            Font = Theme.FontTitle,
            AutoSize = true,
            Location = new Point(4, 2)
        };
        _tagline = new Label
        {
            Text = "Monitor en tiempo real",
            AutoSize = true, Location = new Point(6, 42)
        };
        _status = new Label
        {
            Text = "● Inicializando sensores...", TextAlign = ContentAlignment.MiddleRight,
            AutoSize = false, Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Size = new Size(390, 32), Location = new Point(620, 12)
        };
        _header.Resize += (_, _) =>
        {
            _status.Left = Math.Max(_heading.Right + 16, _header.Width - _status.Width - 8);
            _installUpdate.Left = Math.Max(_heading.Right + 16, _header.Width - _installUpdate.Width - 8);
        };
        _header.Controls.AddRange(new Control[] { _heading, _tagline, _status });
        _installUpdate.Location = new Point(335, 42);
        _installUpdate.Click += async (_, _) => await InstallUpdateAsync();
        LucideIcons.Apply(_installUpdate, "download");
        _header.Controls.Add(_installUpdate);
        _root.Controls.Add(_header, 0, 0);

        var cardsPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1 };
        for (int i = 0; i < 5; i++) cardsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        cardsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _cards = new[]
        {
            new MetricCard("CPU", MetricAccent.Cpu),
            new MetricCard("GPU NVIDIA", MetricAccent.Gpu),
            new MetricCard("RAM", MetricAccent.Ram),
            new MetricCard("TEMP. CPU", MetricAccent.CpuTemp),
            new MetricCard("TEMP. GPU", MetricAccent.GpuTemp)
        };
        for (int i = 0; i < _cards.Length; i++)
        {
            _cards[i].Dock = DockStyle.Fill;
            cardsPanel.Controls.Add(_cards[i], i, 0);
        }
        _root.Controls.Add(cardsPanel, 0, 1);

        var charts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        charts.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _loadPlot = new HistoryPlot(false, _history) { Dock = DockStyle.Fill };
        _tempPlot = new HistoryPlot(true, _history) { Dock = DockStyle.Fill };
        charts.Controls.Add(_loadPlot, 0, 0);
        charts.Controls.Add(_tempPlot, 1, 0);
        _root.Controls.Add(charts, 0, 2);

        _lowerPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5, 7, 5, 0) };
        _processLabel = new Label
        {
            Dock = DockStyle.Top, Height = 31, Text = "Procesos principales  ·  memoria y CPU (cada 15 segundos)",
            Font = Theme.FontBold, TextAlign = ContentAlignment.MiddleLeft
        };
        _processGrid = BuildGrid();
        _lowerPanel.Controls.Add(_processGrid);
        _lowerPanel.Controls.Add(_processLabel);
        _root.Controls.Add(_lowerPanel, 0, 3);

        var footerPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        _footer = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        _pauseButton = CreateButton("Pausar registro", false, () => ToggleLogging());
        footerPanel.Controls.Add(_footer, 0, 0);
        footerPanel.Controls.Add(_pauseButton, 1, 0);
        _root.Controls.Add(footerPanel, 0, 4);

        _sensorAlert = new Label
        {
            Text = "— = sensor no disponible; puede requerir permisos de administrador.",
            Font = Theme.FontSmall, Dock = DockStyle.Bottom, Height = 18,
            TextAlign = ContentAlignment.MiddleRight
        };
        // Status note appears as footer tooltip as not to reduce process table space.
        var statusTooltip = new ToolTip { AutoPopDelay = 12000 };
        _footer.MouseHover += (_, _) => statusTooltip.SetToolTip(_footer, _sensorAlert.Text);

        SetupTrayMenu();
        SetupTrayIcons();
        HandleCreated += (_, _) => WindowChrome.Apply(this);
        ApplyTheme();
        Theme.Changed += OnThemeChanged;
        FormClosed += (_, _) => Theme.Changed -= OnThemeChanged;
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

    private void OnThemeChanged()
    {
        if (IsDisposed || _exiting) return;
        if (InvokeRequired) { BeginInvoke(OnThemeChanged); return; }
        ApplyTheme();
        Array.Fill(_lastTrayText, null);
    }

    private void ApplyTheme()
    {
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.FontBody;
        WindowChrome.Apply(this);
        _pages.ApplyTheme();
        _root.BackColor = Theme.Bg;
        _header.BackColor = Theme.Bg;
        _heading.ForeColor = Theme.Text;
        _heading.Font = Theme.FontTitle;
        _tagline.ForeColor = Theme.Muted;
        _status.ForeColor = _logger.LoggingEnabled ? Theme.Cpu : Theme.Ram;
        _lowerPanel.BackColor = Theme.Bg;
        _processLabel.ForeColor = Theme.Text;
        _processLabel.Font = Theme.FontBold;
        _footer.ForeColor = Theme.Muted;
        _sensorAlert.ForeColor = Theme.Muted;
        Theme.StyleGrid(_processGrid);
        Theme.StyleButton(_pauseButton);
        Theme.StyleButton(_installUpdate, accent: true);
        LucideIcons.Apply(_installUpdate, "download");
        LucideIcons.Apply(_pauseButton, LucideIcons.ForText(_pauseButton.Text));
        Theme.StyleMenu(_trayMenu);
        RefreshTrayMenuIcons();
        _networkPanel.ApplyTheme();
        _settingsPanel.ApplyTheme();
        foreach (var card in _cards) card.Invalidate();
        _loadPlot.Invalidate();
        _tempPlot.Invalidate();
    }

    private Button CreateButton(string text, bool accent, Action action, Color? back = null)
    {
        var b = new Button
        {
            Text = text, Dock = DockStyle.Fill,
            Margin = new Padding(4, 5, 4, 5)
        };
        Theme.StyleButton(b, accent, back);
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
            CellBorderStyle = DataGridViewCellBorderStyle.None,
            ColumnHeadersHeight = 34, RowTemplate = { Height = 29 },
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
        Theme.StyleGrid(grid);
        grid.Columns.Add("name", "Proceso");
        grid.Columns.Add("instances", "Instancias");
        grid.Columns.Add("ram", "RAM MB");
        grid.Columns.Add("private", "Privada MB");
        grid.Columns.Add("cpu", "CPU %");
        grid.Columns[0].FillWeight = 220;
        grid.Columns[0].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.Columns[0].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleLeft;
        // Numeric columns: header and values share the same right edge so they stay under their title.
        float[] weights = { 70, 85, 95, 70 };
        for (int i = 1; i < grid.Columns.Count; i++)
        {
            var column = grid.Columns[i];
            column.FillWeight = weights[i - 1];
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.DefaultCellStyle.Padding = new Padding(6, 0, 14, 0);
            column.HeaderCell.Style.Padding = new Padding(6, 0, 14, 0);
        }
        return grid;
    }

    private void SetupTrayMenu()
    {
        var open = new ToolStripMenuItem("Abrir panel", null, (_, _) => ShowDashboard());
        _toggleLogging = new ToolStripMenuItem("Pausar registro", null, (_, _) => ToggleLogging());
        _autoStart = new ToolStripMenuItem("Iniciar con Windows", null, (_, _) => ToggleStartup())
        {
            Checked = StartupManager.Enabled,
            CheckOnClick = false,
            ToolTipText = "Al activarlo, Windows puede pedir autorización una sola vez. Luego inicia minimizado con sensores, sin pedir administrador en cada arranque."
        };
        _installUpdateMenu = new ToolStripMenuItem("Actualización disponible · Instalar", null, async (_, _) => await InstallUpdateAsync()) { Visible = false };
        _trayMenu.Items.Add(open);
        _trayMenu.Items.Add(_toggleLogging);
        _trayMenu.Items.Add(new ToolStripMenuItem("Configuración", null, (_, _) => OpenSettings(SettingsSection.Export)));
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(_autoStart);
        _trayMenu.Items.Add(new ToolStripMenuItem("Salir", null, async (_, _) => await ExitAsync()));
        _trayMenu.ImageScalingSize = new Size(18, 18);
        Theme.StyleMenu(_trayMenu);
        RefreshTrayMenuIcons();
    }

    private void RefreshTrayMenuIcons()
    {
        foreach (var item in _trayMenu.Items.OfType<ToolStripMenuItem>())
        {
            string caption = item.Text ?? "";
            string icon = caption.StartsWith("Abrir panel") ? "monitor" :
                caption is "Salir" or "Iniciar con Windows" ? "power" :
                caption.StartsWith("Configuración") ? "settings" :
                LucideIcons.ForText(caption);
            item.Image = LucideIcons.Get(icon, Theme.Text, 18);
            Theme.StyleMenuItem(item);
        }
    }

    private void OpenSettings(SettingsSection section)
    {
        _pages.SelectedIndex = 2;
        _settingsPanel.ShowSection(section);
        ShowDashboard();
    }

    private void SetupTrayIcons()
    {
        var accents = Theme.TrayAccents;
        for (int i = 0; i < IconLabels.Length; i++)
        {
            var icon = new NotifyIcon
            {
                Icon = TrayIconRenderer.Create("--", accents[i], IconKinds[i], null),
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
        if (_history.Count > 3600) _history.RemoveRange(0, _history.Count - 3600);
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
        var accents = Theme.TrayAccents;
        for (int i = 0; i < values.Length; i++)
        {
            bool isTemperature = IconKinds[i] == TrayMetricKind.TemperatureNumber;
            string exactValue = !values[i].HasValue ? "--" : isTemperature ? Math.Round(values[i]!.Value).ToString(CultureInfo.InvariantCulture) : TrayIconRenderer.PercentText(values[i]!.Value);
            string tooltip = $"NitroPulse | {IconLabels[i]}: " +
                (values[i].HasValue ? exactValue + (isTemperature ? " °C" : " %") : "sin sensor");
            // On temperatures >= 100C show HI instead of an incorrect 2-digit number.
            string iconText = isTemperature && values[i] >= 100 ? "HI" : exactValue;
            string renderKey = Theme.IsDark + ":" + accents[i].ToArgb() + ":" + TrayIconRenderer.CacheKey(IconKinds[i], values[i], iconText);
            if (_lastTrayText[i] != renderKey)
            {
                var tray = _trayIcons[i];
                var oldIcon = tray.Icon;
                tray.Icon = TrayIconRenderer.Create(iconText, accents[i], IconKinds[i], values[i]);
                oldIcon?.Dispose();
                _lastTrayText[i] = renderKey;
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
        if (_toggleLogging is not null) { _toggleLogging.Text = _pauseButton.Text; _toggleLogging.Image = LucideIcons.Get(_logger.LoggingEnabled ? "pause" : "play", Theme.Text, 18); }
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

    private void ShowNetwork()
    {
        _pages.SelectedIndex = 1; _networkPanel.RefreshView(); ShowDashboard();
    }

    private async Task CheckUpdatesAsync()
    {
        if (_checkingUpdate || _exiting) return;
        var settings = UpdateChecker.Load();
        if (!settings.Automatic || !UpdateChecker.TryRepository(AppVersion.DefaultRepository, out var repository)) return;
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
