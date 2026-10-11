using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace NitroPulse;

internal sealed record ReadyUpdate(ReleaseInfo Release, string Executable, string Hash);

internal static class UpdateInstaller
{
    internal const long MaximumBytes = 256L * 1024 * 1024;
    private static readonly SemaphoreSlim DownloadLock = new(1, 1);
    internal static bool AllowedDownload(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        (uri.Host == "github.com" || uri.Host == "release-assets.githubusercontent.com" || uri.Host == "objects.githubusercontent.com");

    internal static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal static void ValidateExecutable(string file, Version expected)
    {
        using (var stream = File.OpenRead(file))
        { if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z') throw new InvalidDataException("El archivo no es un ejecutable de Windows."); }
        var info = FileVersionInfo.GetVersionInfo(file);
        var actual = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
        if (actual != expected || info.ProductName != "NitroPulse") throw new InvalidDataException("El ejecutable no coincide con NitroPulse y la versión anunciada.");
    }

    internal static void ExtractExecutable(string archive, string target)
    {
        using var zip = ZipFile.OpenRead(archive);
        var candidates = zip.Entries.Where(e => e.FullName.Equals("Aplicacion/NitroPulse.exe", StringComparison.OrdinalIgnoreCase) ||
            e.FullName.Equals("NitroPulse.exe", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length != 1 || candidates[0].Length <= 0 || candidates[0].Length > MaximumBytes)
            throw new InvalidDataException("El ZIP debe incluir un único NitroPulse.exe portable en Aplicacion/ o en la raíz.");
        // Extract only the allowlisted executable, never arbitrary ZIP paths or scripts.
        candidates[0].ExtractToFile(target, overwrite: true);
    }

    public static Task<ReadyUpdate> DownloadAsync(ReleaseInfo release, CancellationToken cancel) =>
        DownloadCoreAsync(release, cancel, new HttpClientHandler { AllowAutoRedirect = false },
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NitroPulse", "Actualizaciones", release.Version.ToString()));

    internal static async Task<ReadyUpdate> DownloadCoreAsync(ReleaseInfo release, CancellationToken cancel, HttpMessageHandler handler, string directory)
    {
        var asset = release.Asset ?? throw new InvalidDataException("La Release no incluye un portable con SHA256 verificable.");
        await DownloadLock.WaitAsync(cancel);
        try
        {
            Directory.CreateDirectory(directory);
            string payload = Path.Combine(directory, "descarga.part");
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NitroPulse/" + AppVersion.Display);
            Uri location = asset.Download;
            for (int redirects = 0; ; redirects++)
            {
                if (!AllowedDownload(location) || redirects > 5) throw new InvalidDataException("Destino de descarga no permitido.");
                using var response = await client.GetAsync(location, HttpCompletionOption.ResponseHeadersRead, cancel);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    var next = response.Headers.Location ?? throw new InvalidDataException("Redirección sin destino.");
                    location = next.IsAbsoluteUri ? next : new Uri(location, next);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(cancel);
                await using var output = new FileStream(payload, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                byte[] buffer = new byte[81920]; long total = 0; int read;
                while ((read = await input.ReadAsync(buffer, cancel)) > 0)
                {
                    total += read;
                    if (total > asset.Size || total > MaximumBytes) throw new InvalidDataException("Tamaño de descarga inesperado.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancel);
                }
                if (total != asset.Size) throw new InvalidDataException("La descarga está incompleta.");
                break;
            }
            if (!Hash(payload).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("La verificación SHA256 de la actualización falló.");
            string executable = Path.Combine(directory, "NitroPulse.exe");
            if (asset.Zip) ExtractExecutable(payload, executable); else File.Copy(payload, executable, true);
            ValidateExecutable(executable, release.Version);
            return new(release, executable, Hash(executable));
        }
        finally { DownloadLock.Release(); }
    }

    public static Process StartInstall(ReadyUpdate update)
    {
        ValidateExecutable(update.Executable, update.Release.Version);
        if (!Hash(update.Executable).Equals(update.Hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("La actualización descargada fue modificada.");
        string target = Environment.ProcessPath ?? throw new InvalidOperationException("No se encontró la aplicación actual.");
        if (!Path.GetFileName(target).Equals("NitroPulse.exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Actualiza desde el ejecutable portable publicado.");
        string directory = Path.GetDirectoryName(update.Executable)!;
        string script = Path.Combine(directory, "Instalar.ps1"), config = Path.Combine(directory, "instalacion.json");
        using (var source = typeof(UpdateInstaller).Assembly.GetManifestResourceStream("NitroPulse.Install-Update.ps1")!)
        using (var reader = new StreamReader(source)) File.WriteAllText(script, reader.ReadToEnd());
        File.WriteAllText(config, JsonSerializer.Serialize(new { pid = Environment.ProcessId, source = update.Executable, target, hash = update.Hash,
            backup = target + ".anterior-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".bak" }));
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (string arg in new[] { "-NoProfile", "-File", script, "-Config", config }) start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new IOException("No se pudo iniciar el instalador.");
    }
}
