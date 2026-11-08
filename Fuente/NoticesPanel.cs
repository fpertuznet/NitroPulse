using System.Reflection;

namespace NitroPulse;

internal sealed class NoticesPanel : UserControl
{
    private readonly FlatTabHost _tabs = new() { Dock = DockStyle.Fill };

    internal NoticesPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        Controls.Add(_tabs);
        var assembly = Assembly.GetExecutingAssembly();
        var names = assembly.GetManifestResourceNames().Where(n => n.StartsWith("NitroPulse.Avisos.", StringComparison.Ordinal))
            .OrderBy(n => n.EndsWith("PRIVACIDAD.md") ? 0 : 1).ThenBy(n => n);
        foreach (string name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            string title = name["NitroPulse.Avisos.".Length..];
            string caption = title == "PRIVACIDAD.md" ? "Privacidad" : title;
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            page.Controls.Add(new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Both, WordWrap = true, Text = reader.ReadToEnd(),
                BackColor = Theme.Panel, ForeColor = Theme.Text, Font = Theme.FontBody,
                BorderStyle = BorderStyle.None
            });
            _tabs.Add(caption, page);
        }
        Theme.Changed += ApplyTheme;
        Disposed += (_, _) => Theme.Changed -= ApplyTheme;
    }

    internal void ApplyTheme()
    {
        BackColor = Theme.Bg;
        _tabs.ApplyTheme();
        foreach (Control page in _tabs.Controls.OfType<Panel>())
        {
            // pages live inside FlatTabHost body; walk text boxes via host ApplyTheme already sets page bg
        }
        foreach (var box in GetAllTextBoxes(_tabs))
        {
            box.BackColor = Theme.Panel;
            box.ForeColor = Theme.Text;
            box.Font = Theme.FontBody;
        }
    }

    private static IEnumerable<TextBox> GetAllTextBoxes(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is TextBox box) yield return box;
            foreach (var nested in GetAllTextBoxes(child)) yield return nested;
        }
    }
}
