using System.Diagnostics;

using System.Net.Http;

namespace NitroPulse;

internal sealed class UpdateDialog : Form
{
    private readonly TextBox _repository;
    private readonly CheckBox _automatic;
    private readonly Label _status;
    private readonly Button _check;
    private readonly CancellationTokenSource _cancel = new();
    private readonly Action<ReadyUpdate> _onReady;

    public UpdateDialog(Action<ReadyUpdate> onReady)
    {
        _onReady = onReady;
        Text = "NitroPulse · Actualizaciones";
        ClientSize = new Size(500, 235);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        var settings = UpdateChecker.Load();
        Controls.Add(new Label { Text = "Versión instalada: " + AppVersion.Display, Left = 18, Top = 16, Width = 460 });
        Controls.Add(new Label { Text = "Repositorio de GitHub (usuario/repositorio o URL):", Left = 18, Top = 48, Width = 460 });
        _repository = new TextBox { Text = settings.Repository, Left = 18, Top = 74, Width = 460 };
        _automatic = new CheckBox { Text = "Buscar actualizaciones al abrir y cada 6 horas", Checked = settings.Automatic, Left = 18, Top = 107, Width = 460 };
        _status = new Label { Left = 18, Top = 139, Width = 460, Height = 43, Text = "Configura el repositorio cuando publiques NitroPulse en GitHub." };
        _check = new Button { Text = "Guardar y comprobar", Left = 18, Top = 191, Width = 185 };
        var close = new Button { Text = "Cerrar", Left = 378, Top = 191, Width = 100, DialogResult = DialogResult.Cancel };
        LucideIcons.Apply(_check, "refresh-cw"); LucideIcons.Apply(close, "x");
        _check.Click += async (_, _) => await CheckAsync();
        Controls.AddRange(new Control[] { _repository, _automatic, _status, _check, close });
        CancelButton = close;
        FormClosed += (_, _) => { _cancel.Cancel(); };
    }

    private async Task CheckAsync()
    {
        if (!UpdateChecker.TryRepository(_repository.Text, out var repository))
        { _status.Text = "Usa usuario/repositorio, por ejemplo: tuusuario/NitroPulse."; return; }
        _check.Enabled = false;
        try
        {
            UpdateChecker.Save(new(repository, _automatic.Checked));
            _status.Text = "Consultando GitHub...";
            var release = await UpdateChecker.LatestAsync(repository, _cancel.Token);
            if (IsDisposed || _cancel.IsCancellationRequested) return;
            _status.Text = release is null ? "La etiqueta debe tener el formato v1.0.11; solo se aceptan versiones estables." :
                release.Version > AppVersion.Current ? "Nueva versión disponible: " + release.Tag : "Ya tienes la versión más reciente.";
            if (release is not null && release.Version > AppVersion.Current)
            {
                _status.Text = "Descargando actualización en segundo plano...";
                var ready = await UpdateInstaller.DownloadAsync(release, _cancel.Token);
                if (IsDisposed || _cancel.IsCancellationRequested) return;
                _onReady(ready);
                _status.Text = "Descarga verificada. Pulsa «Actualización disponible · Instalar» en el panel.";
            }
        }
        catch (OperationCanceledException) { if (!IsDisposed) _status.Text = "Consulta cancelada o conexión agotada. Puedes volver a intentarlo."; }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException or KeyNotFoundException or System.ComponentModel.Win32Exception)
        { if (!IsDisposed) _status.Text = e.Message; }
        finally { if (!IsDisposed) _check.Enabled = true; }
    }

}
