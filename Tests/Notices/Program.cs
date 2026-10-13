using NitroPulse;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var panel = new NoticesPanel();
        var tabs = panel.Controls.OfType<TabControl>().Single();
        if (tabs.TabPages.Count != 7 || tabs.TabPages[0].Text != "Privacidad") throw new Exception("Missing embedded notices");
        foreach (TabPage page in tabs.TabPages)
        {
            var text = page.Controls.OfType<TextBox>().Single();
            if (!text.ReadOnly || text.Text.Length < 100) throw new Exception("Notice not readable: " + page.Text);
        }
        Console.WriteLine("PASS: privacidad, MIT y cinco avisos de terceros integrados y legibles; sin sensores ni conexiones.");
    }
}
