namespace NitroPulse;

/// <summary>A bounded, timestamp-anchored viewport: incoming samples never pull a historical view forward.</summary>
internal sealed class PlotViewport(int capacity)
{
    internal int Capacity { get; } = Math.Max(2, capacity);
    internal int Start { get; private set; }
    internal int Count { get; private set; }
    internal int MaximumStart { get; private set; }
    internal bool IsLive { get; private set; } = true;
    private long? _anchor;

    internal void Update(long[] times)
    {
        Count = Math.Min(Capacity, times.Length);
        MaximumStart = Math.Max(0, times.Length - Count);
        if (IsLive) Start = MaximumStart;
        else
        {
            int index = _anchor.HasValue ? Array.FindIndex(times, t => t >= _anchor.Value) : 0;
            Start = Math.Clamp(index < 0 ? MaximumStart : index, 0, MaximumStart);
        }
        _anchor = times.Length == 0 ? null : times[Start];
    }

    internal void ScrollTo(int index, long[] times)
    {
        Update(times);
        Start = Math.Clamp(index, 0, MaximumStart);
        IsLive = Start == MaximumStart;
        _anchor = times.Length == 0 ? null : times[Start];
    }

    internal void Live(long[] times) { IsLive = true; Update(times); }
}

/// <summary>Dark flat scrubber — replaces the bright native HScrollBar.</summary>
internal sealed class FlatScrubber : Control
{
    private int _maximum;
    private int _largeChange = 1;
    private int _value;
    private bool _dragging;

    internal int Maximum
    {
        get => _maximum;
        set { _maximum = Math.Max(0, value); Invalidate(); }
    }
    internal int LargeChange
    {
        get => _largeChange;
        set { _largeChange = Math.Max(1, value); Invalidate(); }
    }
    internal int Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, Math.Max(0, Maximum - LargeChange + 1)); Invalidate(); }
    }
    internal bool CanScroll
    {
        get => Enabled;
        set { Enabled = value; Invalidate(); }
    }

    internal event Action<int>? Scrolled;

    public FlatScrubber()
    {
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        Height = 12;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var track = new Rectangle(0, Height / 2 - 2, Width - 1, 4);
        using var trackBrush = new SolidBrush(Theme.Grid);
        g.FillRectangle(trackBrush, track);
        if (!Enabled || Maximum <= 0) return;
        float span = Math.Max(1, Maximum - LargeChange + 1);
        float thumbW = Math.Max(28, Width * (LargeChange / (float)(Maximum + 1)));
        float x = (Width - thumbW) * (_value / span);
        var thumb = new RectangleF(x, Height / 2f - 3, thumbW, 6);
        using var thumbBrush = new SolidBrush(Theme.Muted);
        g.FillRectangle(thumbBrush, thumb);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled || e.Button != MouseButtons.Left) return;
        _dragging = true;
        Capture = true;
        JumpTo(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) JumpTo(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
    }

    private void JumpTo(int x)
    {
        float span = Math.Max(1, Maximum - LargeChange + 1);
        float thumbW = Math.Max(28, Width * (LargeChange / (float)(Maximum + 1)));
        float ratio = Math.Clamp((x - thumbW / 2) / Math.Max(1, Width - thumbW), 0, 1);
        int next = (int)Math.Round(ratio * span);
        if (next == _value) return;
        Value = next;
        Scrolled?.Invoke(Value);
    }
}

internal sealed class PlotNavigation : UserControl
{
    private readonly FlatScrubber _scroll = new() { Dock = DockStyle.Fill };
    private readonly Button _live = new()
    {
        Text = "En vivo", Dock = DockStyle.Right, Width = 108,
        FlatStyle = FlatStyle.Flat,
        AccessibleName = "Volver al final del historial en vivo"
    };
    private long[] _times = Array.Empty<long>();
    internal PlotViewport View { get; }
    internal event Action? Changed;

    internal PlotNavigation(int capacity)
    {
        View = new(capacity);
        Dock = DockStyle.Bottom; Height = 32; BackColor = Theme.Panel;
        Padding = new Padding(14, 5, 10, 5);
        ApplyThemeColors();
        _scroll.AccessibleName = "Desplazar el historial de la gráfica";
        Controls.Add(_scroll); Controls.Add(_live);
        _scroll.Scrolled += ScrollTo;
        _live.Click += (_, _) => { View.Live(_times); Sync(); Changed?.Invoke(); };
        Theme.Changed += ApplyThemeColors;
        Disposed += (_, _) => Theme.Changed -= ApplyThemeColors;
    }

    internal void ApplyThemeColors()
    {
        BackColor = Theme.Panel;
        StyleLiveButton();
        _scroll.Invalidate();
    }

    private void StyleLiveButton()
    {
        bool live = View.IsLive;
        _live.FlatStyle = FlatStyle.Flat;
        _live.FlatAppearance.BorderSize = 0;
        _live.Enabled = true;
        _live.Cursor = live ? Cursors.Default : Cursors.Hand;
        // Active = green chip; history = brighter panel so it stays readable.
        _live.BackColor = live ? Theme.Cpu : Theme.PanelAlt;
        _live.ForeColor = live ? Theme.OnAccent : Theme.Text;
        _live.FlatAppearance.MouseOverBackColor = live ? Theme.Cpu : ControlPaint.Light(Theme.PanelAlt, 0.10f);
        _live.FlatAppearance.MouseDownBackColor = live ? Theme.Cpu : ControlPaint.Dark(Theme.PanelAlt, 0.04f);
        LucideIcons.Apply(_live, "activity");
    }

    internal void SetTimes(long[] times) { _times = times; View.Update(times); Sync(); }
    internal void ScrollTo(int index) { View.ScrollTo(index, _times); Sync(); Changed?.Invoke(); }
    internal void ReturnLive() { View.Live(_times); Sync(); Changed?.Invoke(); }

    private void Sync()
    {
        _scroll.LargeChange = Math.Max(1, View.Count);
        _scroll.Maximum = View.MaximumStart + _scroll.LargeChange - 1;
        _scroll.Value = Math.Clamp(View.Start, 0, Math.Max(0, _scroll.Maximum - _scroll.LargeChange + 1));
        _scroll.CanScroll = View.MaximumStart > 0;
        StyleLiveButton();
    }

    internal void Bind(Control plot)
    {
        plot.TabStop = true;
        plot.AccessibleDescription = "Arrastra horizontalmente, usa la rueda o la barra inferior para revisar el historial. En vivo vuelve al final. Flechas, Inicio y Fin disponibles.";
        Changed += plot.Invalidate;
        bool dragging = false; int origin = 0, start = 0;
        plot.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            plot.Focus(); dragging = true; origin = e.X; start = View.Start; plot.Capture = true;
        };
        plot.MouseMove += (_, e) =>
        {
            if (dragging) ScrollTo(start - (int)Math.Round((e.X - origin) * (View.Count - 1d) / Math.Max(1, plot.Width - 80)));
        };
        plot.MouseUp += (_, _) => { dragging = false; plot.Capture = false; };
        plot.MouseCaptureChanged += (_, _) => { if (!plot.Capture) dragging = false; };
        plot.MouseWheel += (_, e) => ScrollTo(View.Start - Math.Sign(e.Delta) * 30);
        plot.PreviewKeyDown += (_, e) => { if (e.KeyCode is Keys.Left or Keys.Right or Keys.Home or Keys.End) e.IsInputKey = true; };
        plot.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Left) ScrollTo(View.Start - 30);
            else if (e.KeyCode == Keys.Right) ScrollTo(View.Start + 30);
            else if (e.KeyCode == Keys.Home) ScrollTo(0);
            else if (e.KeyCode == Keys.End) ReturnLive();
            else return;
            e.Handled = e.SuppressKeyPress = true;
        };
    }
}
