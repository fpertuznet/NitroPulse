using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace NitroPulse;

internal sealed class NetworkLabels
{
    private readonly string _path;
    private readonly Dictionary<string, string> _labels = new(StringComparer.Ordinal);
    private string? _loadError;
    internal NetworkLabels(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NitroPulse", "red_etiquetas.json");
        try
        {
            if (File.Exists(_path)) foreach (var item in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? new()) _labels[item.Key] = item.Value;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { _loadError = e.Message; }
    }
    internal string Get(NetworkContext context)
    {
        if (context.Main is not { } link) return "";
        if (_labels.TryGetValue(link.LabelKey, out var saved)) return saved;
        string legacy = "legacy:" + link.FallbackLabelKey;
        if (!_labels.TryGetValue(legacy, out var recovered)) return "";
        if (string.IsNullOrEmpty(link.ProfileId)) return recovered;
        // Bind the recovered label once; a different Windows profile with the same gateway must not inherit it.
        var promoted = new Dictionary<string, string>(_labels) { [link.LabelKey] = recovered };
        promoted.Remove(legacy);
        try { Save(promoted); _labels.Clear(); foreach (var item in promoted) _labels[item.Key] = item.Value; return recovered; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _loadError = e.Message; return ""; }
    }
    internal void Set(NetworkContext context, string label)
    {
        if (context.Main is not { } link) throw new InvalidOperationException("Espera a que se identifique una conexión antes de etiquetarla.");
        if (_loadError is not null) throw new IOException("No se sobrescribió el archivo de etiquetas porque no pudo leerse: " + _loadError);
        var updated = new Dictionary<string, string>(_labels) { [link.LabelKey] = label.Trim() };
        Save(updated);
        _labels.Clear(); foreach (var item in updated) _labels[item.Key] = item.Value;
    }
    private void Save(Dictionary<string, string> labels)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(labels, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal void ImportLegacy(string sessions)
    {
        // Existing 1.0.14 labels only lived in per-session events; recover those without rewriting logs.
        if (_loadError is not null || File.Exists(_path) || !Directory.Exists(sessions)) return;
        var imported = new Dictionary<string, string>();
        foreach (string directory in Directory.GetDirectories(sessions).OrderBy(d => d, StringComparer.Ordinal).TakeLast(100))
        {
            string contexts = Path.Combine(directory, "red_conexiones.jsonl"), events = Path.Combine(directory, "red_eventos.csv");
            if (!File.Exists(contexts) || !File.Exists(events)) continue;
            try
            {
                var networks = new Dictionary<int, NetworkContext>();
                using var contextInput = new StreamReader(new FileStream(contexts, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), Encoding.UTF8);
                string? line;
                while ((line = contextInput.ReadLine()) is not null)
                {
                    if (line.Length > 128000) continue;
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    var context = root.GetProperty("context").Deserialize<NetworkContext>();
                    if (context is not null) networks[root.GetProperty("segment").GetInt32()] = context;
                }
                using var eventInput = new FileStream(events, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var parser = new TextFieldParser(eventInput, Encoding.UTF8) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
                parser.SetDelimiters(";");
                while (!parser.EndOfData)
                {
                    var fields = parser.ReadFields();
                    if (fields?.Length != 4 || fields[2] != "Etiqueta del usuario" || !int.TryParse(fields[1], out int segment)) continue;
                    if (networks.TryGetValue(segment, out var context) && context.Main is { } link) imported["legacy:" + link.FallbackLabelKey] = fields[3];
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or MalformedLineException) { }
        }
        if (imported.Count == 0) return;
        try { Save(imported); foreach (var item in imported) _labels[item.Key] = item.Value; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _loadError = e.Message; }
    }
}
