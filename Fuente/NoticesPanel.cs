using System.Reflection;

namespace NitroPulse;

internal sealed class NoticesPanel : UserControl
{
    internal NoticesPanel()
    {
        Dock = DockStyle.Fill;
        var tabs = new TabControl { Dock = DockStyle.Fill };
        Controls.Add(tabs);
        var assembly = Assembly.GetExecutingAssembly();
        var names = assembly.GetManifestResourceNames().Where(n => n.StartsWith("NitroPulse.Avisos.", StringComparison.Ordinal))
            .OrderBy(n => n.EndsWith("PRIVACIDAD.md") ? 0 : 1).ThenBy(n => n);
        foreach (string name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            string title = name["NitroPulse.Avisos.".Length..];
            var page = new TabPage(title == "PRIVACIDAD.md" ? "Privacidad" : title);
            page.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Both, WordWrap = true, Text = reader.ReadToEnd(),
                BackColor = Theme.Panel, ForeColor = Theme.Text, Font = new Font("Segoe UI", 10) });
            tabs.TabPages.Add(page);
        }
    }
}
