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

internal sealed class PlotNavigation : UserControl
{
    private readonly HScrollBar _scroll = new() { Dock = DockStyle.Fill, SmallChange = 10 };
    private readonly Button _live = new()
    {
        Text = "En vivo", Dock = DockStyle.Right, Width = 104,
        FlatStyle = FlatStyle.Flat, BackColor = Theme.PanelAlt, ForeColor = Theme.Text,
        AccessibleName = "Volver al final del historial en vivo"
    };
    private long[] _times = Array.Empty<long>();
    internal PlotViewport View { get; }
    internal event Action? Changed;

    internal PlotNavigation(int capacity)
    {
        View = new(capacity);
        Dock = DockStyle.Bottom; Height = 28; BackColor = Theme.Panel;
        Padding = new Padding(14, 2, 14, 2);
        _live.FlatAppearance.BorderColor = Theme.Border;
        LucideIcons.Apply(_live, "activity");
        _scroll.AccessibleName = "Desplazar el historial de la gráfica";
        Controls.Add(_scroll); Controls.Add(_live);
        _scroll.Scroll += (_, e) => ScrollTo(e.NewValue);
        _live.Click += (_, _) => { View.Live(_times); Sync(); Changed?.Invoke(); };
    }

    internal void SetTimes(long[] times) { _times = times; View.Update(times); Sync(); }
    internal void ScrollTo(int index) { View.ScrollTo(index, _times); Sync(); Changed?.Invoke(); }
    internal void ReturnLive() { View.Live(_times); Sync(); Changed?.Invoke(); }

    private void Sync()
    {
        _scroll.LargeChange = Math.Max(1, View.Count);
        _scroll.Maximum = View.MaximumStart + _scroll.LargeChange - 1;
        _scroll.Value = Math.Clamp(View.Start, _scroll.Minimum, Math.Max(_scroll.Minimum, _scroll.Maximum - _scroll.LargeChange + 1));
        _scroll.Enabled = View.MaximumStart > 0;
        _live.Enabled = !View.IsLive;
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
