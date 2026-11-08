using System.Net.Http;

namespace NitroPulse;

/// <summary>Update status UI. Repository is fixed in code (AppVersion.DefaultRepository).</summary>
internal sealed class UpdatesPage : UserControl
{
    private readonly Label _version;
    private readonly Label _status;
    private readonly Button _check;
    private readonly CancellationTokenSource _cancel = new();
    private readonly Action<ReadyUpdate> _onReady;

    public UpdatesPage(Action<ReadyUpdate> onReady)
    {
        _onReady = onReady;
        Dock = DockStyle.Fill;
        Padding = new Padding(24);
        BackColor = Theme.Bg;
        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true
        };

        _version = new Label
        {
            Text = "Versión instalada: " + AppVersion.Display,
            AutoSize = true, Margin = new Padding(0, 0, 0, 16)
        };
        _status = new Label
        {
            Text = "Pulsa comprobar para buscar una versión nueva.",
            AutoSize = true, MaximumSize = new Size(720, 0), Margin = new Padding(0, 0, 0, 20)
        };
        _check = new Button { Text = "Comprobar actualización", AutoSize = true, Height = 38, Margin = new Padding(0, 4, 0, 0) };
        _check.Click += async (_, _) => await CheckAsync();
        LucideIcons.Apply(_check, "refresh-cw");

        root.Controls.Add(_version);
        root.Controls.Add(_status);
        root.Controls.Add(_check);
        Controls.Add(root);
        ApplyTheme();
        Theme.Changed += ApplyTheme;
        Disposed += (_, _) =>
        {
            Theme.Changed -= ApplyTheme;
            _cancel.Cancel();
        };
    }

    internal void ApplyTheme()
    {
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.FontBody;
        _version.ForeColor = Theme.Text;
        _status.ForeColor = Theme.Muted;
        Theme.StyleButton(_check, accent: true);
        LucideIcons.Apply(_check, "refresh-cw");
    }

    private async Task CheckAsync()
    {
        string repository = AppVersion.DefaultRepository;
        if (!UpdateChecker.TryRepository(repository, out repository))
        {
            _status.Text = "No hay un repositorio de actualizaciones configurado en esta versión.";
            return;
        }
        _check.Enabled = false;
        try
        {
            // Keep automatic checks enabled; repository is always the built-in default.
            var prior = UpdateChecker.Load();
            UpdateChecker.Save(new(repository, prior.Automatic));
            _status.Text = "Consultando actualizaciones...";
            var release = await UpdateChecker.LatestAsync(repository, _cancel.Token);
            if (IsDisposed || _cancel.IsCancellationRequested) return;
            if (release is null)
            {
                _status.Text = "No se encontró una versión estable publicada.";
                return;
            }
            if (release.Version <= AppVersion.Current)
            {
                _status.Text = "Ya tienes la versión más reciente.";
                return;
            }
            _status.Text = "Nueva versión disponible: " + release.Tag + " · descargando...";
            var ready = await UpdateInstaller.DownloadAsync(release, _cancel.Token);
            if (IsDisposed || _cancel.IsCancellationRequested) return;
            _onReady(ready);
            _status.Text = "Actualización " + release.Tag + " lista. Pulsa «Actualización disponible · Instalar» arriba.";
        }
        catch (OperationCanceledException) { if (!IsDisposed) _status.Text = "Consulta cancelada o sin conexión. Puedes volver a intentarlo."; }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException or KeyNotFoundException or System.ComponentModel.Win32Exception)
        { if (!IsDisposed) _status.Text = e.Message; }
        finally { if (!IsDisposed) _check.Enabled = true; }
    }
}
