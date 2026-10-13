using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NitroPulse;

internal sealed record UpdateSettings(string Repository = "", bool Automatic = true);
internal sealed record UpdateAsset(Uri Download, long Size, string Sha256, bool Zip);
internal sealed record ReleaseInfo(Version Version, string Tag, Uri Page, UpdateAsset? Asset = null);
internal sealed record UpdateQueryState(string Repository, DateTimeOffset NextAllowed, string? Json = null, string? Error = null);

internal static class UpdateChecker
{
    private static readonly SemaphoreSlim QueryLock = new(1, 1);
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NitroPulse", "actualizaciones.json");

    public static UpdateSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath)) return JsonSerializer.Deserialize<UpdateSettings>(File.ReadAllText(SettingsPath)) ?? new(AppVersion.DefaultRepository);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return new(AppVersion.DefaultRepository);
    }

    public static void Save(UpdateSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        string temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, SettingsPath, overwrite: true);
    }

    internal static bool TryRepository(string input, out string repository)
    {
        repository = input.Trim().TrimEnd('/');
        if (repository.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) repository = repository[19..];
        if (repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repository = repository[..^4];
        return Regex.IsMatch(repository, @"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9_.-]{1,100}$") &&
            repository.Split('/')[1] is not "." and not "..";
    }

    internal static bool TryVersion(string tag, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        string text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        if (!Regex.IsMatch(text, @"^\d+\.\d+\.\d+(?:\.\d+)?$") || !Version.TryParse(text, out var parsed)) return false;
        version = new Version(parsed.Major, parsed.Minor, parsed.Build, Math.Max(0, parsed.Revision));
        return true;
    }

    internal static ReleaseInfo? Parse(string json, string repository)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!TryVersion(tag, out var version)) return null;
        string page = root.GetProperty("html_url").GetString() ?? "";
        if (!Uri.TryCreate(page, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || uri.UserInfo.Length != 0 ||
            !uri.IsDefaultPort || !uri.AbsolutePath.StartsWith("/" + repository + "/releases/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub devolvió un enlace de actualización inesperado.");
        UpdateAsset? selected = null;
        if (root.TryGetProperty("assets", out var assets))
        foreach (var asset in assets.EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            bool zip = name.StartsWith("NitroPulse-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            if (!zip && !name.Equals("NitroPulse.exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (!asset.TryGetProperty("digest", out var digestValue)) continue;
            string digest = digestValue.GetString() ?? "";
            if (!Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$")) continue;
            long size = asset.GetProperty("size").GetInt64();
            if (size <= 0 || size > UpdateInstaller.MaximumBytes) continue;
            if (!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var download) ||
                download.Scheme != "https" || download.Host != "github.com" || !download.IsDefaultPort || download.UserInfo.Length != 0 ||
                !download.AbsolutePath.StartsWith("/" + repository + "/releases/download/", StringComparison.OrdinalIgnoreCase)) continue;
            if (selected is null || !zip) selected = new(download, size, digest[7..], zip);
        }
        return new ReleaseInfo(version, tag, uri, selected);
    }

    public static async Task<ReleaseInfo?> LatestAsync(string repository, CancellationToken cancellation)
        => await LatestCoreAsync(repository, cancellation, new HttpClientHandler { AllowAutoRedirect = false },
            Path.Combine(Path.GetDirectoryName(SettingsPath)!, "consulta_actualizaciones.json"), DateTimeOffset.UtcNow);

    internal static DateTimeOffset RetryAt(HttpResponseMessage response, DateTimeOffset now)
    {
        var next = now.AddMinutes(1);
        if (response.Headers.RetryAfter?.Delta is { } delay && now.Add(delay) > next) next = now.Add(delay);
        if (response.Headers.RetryAfter?.Date is { } date && date > next) next = date;
        if (response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) && remaining.Contains("0") &&
            response.Headers.TryGetValues("x-ratelimit-reset", out var reset) && long.TryParse(reset.FirstOrDefault(), out long seconds))
        {
            try { var dateReset = DateTimeOffset.FromUnixTimeSeconds(seconds); if (dateReset > next) next = dateReset; }
            catch (ArgumentOutOfRangeException) { }
        }
        return next;
    }

    internal static async Task<ReleaseInfo?> LatestCoreAsync(string repository, CancellationToken cancellation,
        HttpMessageHandler handler, string statePath, DateTimeOffset now)
    {
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12), MaxResponseContentBufferSize = 1024 * 1024 };
        if (!TryRepository(repository, out var normalized)) throw new ArgumentException("Indica el repositorio como usuario/NitroPulse o su URL de GitHub.");
        await QueryLock.WaitAsync(cancellation);
        try
        {
        UpdateQueryState? state = null;
        try { if (File.Exists(statePath)) state = JsonSerializer.Deserialize<UpdateQueryState>(File.ReadAllText(statePath)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        if (state is not null && state.Repository.Equals(normalized, StringComparison.OrdinalIgnoreCase) && state.NextAllowed > now)
        {
            if (state.Json is not null) return Parse(state.Json, normalized);
            throw new InvalidOperationException((state.Error ?? "Consulta en espera.") + " Próxima consulta: " + state.NextAllowed.ToLocalTime().ToString("g"));
        }
        void Store(UpdateQueryState value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(statePath))!);
            string temp = statePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value));
            File.Move(temp, statePath, overwrite: true);
        }
        // Persist before requesting: restarting the application must not cause a tight retry loop.
        Store(new(normalized, now.AddMinutes(1), Error: "La consulta anterior no se completó."));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NitroPulse/" + AppVersion.Display);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await client.GetAsync("https://api.github.com/repos/" + normalized + "/releases/latest", cancellation);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            bool limited = response.StatusCode == HttpStatusCode.TooManyRequests || response.Headers.RetryAfter is not null ||
                (response.Headers.TryGetValues("x-ratelimit-remaining", out var quota) && quota.Contains("0"));
            string error = limited ? "GitHub limita temporalmente las consultas." : "GitHub denegó el acceso; revisa los permisos o el estado del repositorio.";
            var next = RetryAt(response, now);
            if (!limited && next < now.AddHours(6)) next = now.AddHours(6);
            Store(new(normalized, next, Error: error));
            throw new InvalidOperationException(error + " Próxima consulta: " + next.ToLocalTime().ToString("g"));
        }
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            const string error = "El repositorio no existe, es privado o todavía no tiene una versión publicada en Releases.";
            Store(new(normalized, now.AddHours(6), Error: error));
            throw new InvalidOperationException(error);
        }
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellation);
        var release = Parse(json, normalized);
        Store(new(normalized, now.AddHours(6), json));
        return release;
        }
        finally { QueryLock.Release(); }
    }
}
