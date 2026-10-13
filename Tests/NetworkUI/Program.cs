using NitroPulse;
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string folder=Path.Combine(AppContext.BaseDirectory,"fixtures-"+Guid.NewGuid().ToString("N"));
        using var monitor=new NetworkMonitor(folder,new NetworkLabels(Path.Combine(folder,"labels.json")));
        var link=new LinkInfo("demo",1,"Wi-Fi","Wireless80211",new[]{"192.168.1.20"},new[]{"192.168.1.1"},new[]{"192.168.1.1"},"Oficina · demo","AA:BB:CC:DD:EE:FF",75);
        var begin=DateTimeOffset.Now.AddMinutes(-10);
        for(int i=0;i<120;i++) {
            var current=i>=65?link with {Ssid="Casa · DEMO",Bssid="11:22:33:44:55:66"}:link;
            bool down=i>=38 && i<=44;
            var status=down?ProbeState.Failed:ProbeState.Ok;
            double latency=50+Math.Sin(i*.45)*12+(i%21==0?90:0);
            monitor.Record(begin.AddSeconds(i*5),new(new[]{current},current.Id,current.Id),new[]{
                new NetworkProbe("Router","192.168.1.1","ICMP",ProbeState.Ok,3+(i%5),"Success",current.Id),
                new NetworkProbe("Cloudflare","1.1.1.1","ICMP",status,down?null:latency,"DEMO",current.Id),
                new NetworkProbe("Google","8.8.8.8","ICMP",status,down?null:latency+12,"DEMO",current.Id),
                new NetworkProbe("Cloudflare","1.1.1.1:443","TCP",status,down?null:latency,"DEMO",current.Id),
                new NetworkProbe("Google","8.8.8.8:443","TCP",status,down?null:latency+12,"DEMO",current.Id)});
            if(i==70) monitor.RenameCurrent("Casa · DEMO");
        }
        using var form=new Form {Text="NitroPulse · DEMOSTRACIÓN de interfaz",BackColor=Theme.Bg,Font=new Font("Segoe UI",9.5f),
            ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000)};
        var tabs=NitroTabs.Create(); tabs.TabPages.Add(new TabPage("Recursos")); var page=new TabPage("Red");
        using var panel=new NetworkPanel(monitor); page.Controls.Add(panel); tabs.TabPages.Add(page);form.Controls.Add(tabs); tabs.SelectedIndex=1;
        panel.RefreshView();
        void Handles(Control control) { _=control.Handle; foreach(Control child in control.Controls) Handles(child); }
        Handles(form);
        // Test-only form is offscreen, without sensors or a running network monitor.
        form.Show(); Application.DoEvents();
        foreach(var size in new[]{new Size(1100,780),new Size(860,650)}) {
            form.Size=size; form.PerformLayout(); Application.DoEvents();
            void Layout(Control control) { control.PerformLayout(); foreach(Control child in control.Controls) Layout(child); }
            Layout(form); panel.RefreshView();
            // Invisible forms do not update native tab-page bounds; size the test viewport explicitly.
            page.Bounds=tabs.DisplayRectangle; page.PerformLayout(); panel.PerformLayout(); Layout(panel);
            var root=(TableLayoutPanel)panel.Controls[0];
            if(root.GetColumnWidths()[0]>panel.Width || root.Controls.OfType<NetworkPlot>().Single().Right>panel.ClientSize.Width) throw new Exception("Panel de red recortado al redimensionar");
            void CheckIcons(Control control) { if(control is Button button && button.Image is null) throw new Exception("Botón sin icono: "+button.Text); foreach(Control child in control.Controls) CheckIcons(child); }
            CheckIcons(panel);
            using var bitmap=new Bitmap(form.Width,form.Height); form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));
            string output=Path.Combine(args[0],"red-ui-"+size.Width+".png"); bitmap.Save(output);
            Console.WriteLine("RENDER: " + output);
        }
        var plot=panel.Controls[0].Controls.OfType<NetworkPlot>().Single();
        using var graph=new Bitmap(plot.Width,plot.Height); plot.DrawToBitmap(graph,new Rectangle(Point.Empty,graph.Size));
        using var png=new MemoryStream(); graph.Save(png,System.Drawing.Imaging.ImageFormat.Png);
        string exported=monitor.Export(png.ToArray());
        using(var zip=System.IO.Compression.ZipFile.OpenRead(exported)) {
            var entry=zip.GetEntry("grafica_ping.png") ?? throw new Exception("Informe sin gráfica");
            using var source=entry.Open(); using var image=new Bitmap(source);
            if(image.Width!=plot.Width || zip.GetEntry("red_nombres.json") is null) throw new Exception("Exportación incompleta");
        }
        File.Move(exported,Path.Combine(folder,Path.GetFileName(exported)));
        Console.WriteLine("PASS: gráfico PNG y etiquetas exportados; botones con Lucide; cambio de tamaño sin recortes.");
    }
}
