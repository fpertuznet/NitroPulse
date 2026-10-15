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
        var begin=DateTimeOffset.Now.AddMinutes(-80);
        for(int i=0;i<960;i++) {
            var current=i>=805?link with {Ssid="Casa · DEMO",Bssid="11:22:33:44:55:66"}:link;
            bool down=(i>=38 && i<=44) || (i>=638 && i<=644);
            var status=down?ProbeState.Failed:ProbeState.Ok;
            double latency=50+Math.Sin(i*.45)*12+(i%21==0?90:0);
            monitor.Record(begin.AddSeconds(i*5),new(new[]{current},current.Id,current.Id),new[]{
                new NetworkProbe("Router","192.168.1.1","ICMP",ProbeState.Ok,3+(i%5),"Success",current.Id),
                new NetworkProbe("Cloudflare","1.1.1.1","ICMP",status,down?null:latency,"DEMO",current.Id),
                new NetworkProbe("Google","8.8.8.8","ICMP",status,down?null:latency+12,"DEMO",current.Id),
                new NetworkProbe("Cloudflare","1.1.1.1:443","TCP",status,down?null:latency,"DEMO",current.Id),
                new NetworkProbe("Google","8.8.8.8:443","TCP",status,down?null:latency+12,"DEMO",current.Id)});
            if(i==810) monitor.RenameCurrent("Casa · DEMO");
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
        var nav=plot.Controls.OfType<PlotNavigation>().Single();
        if(nav.View.MaximumStart!=600 || !nav.View.IsLive)throw new Exception("La gráfica no conserva más de 30 minutos navegables");
        nav.ScrollTo(0); Application.DoEvents();
        if(nav.View.Start!=0 || nav.View.IsLive)throw new Exception("No se puede consultar el inicio");
        using(var older=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(older,new Rectangle(Point.Empty,form.Size)); older.Save(Path.Combine(args[0],"red-historial-1.0.17.png")); }
        monitor.Record(begin.AddSeconds(960*5),new(new[]{link},link.Id,link.Id),Array.Empty<NetworkProbe>());
        using(var render=new Bitmap(plot.Width,plot.Height))plot.DrawToBitmap(render,new Rectangle(Point.Empty,plot.Size));
        if(nav.View.Start!=0 || nav.View.IsLive)throw new Exception("Una muestra nueva desplazó la vista histórica");
        var wheel=typeof(Control).GetMethod("OnMouseWheel",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        wheel.Invoke(plot,new object[]{new MouseEventArgs(MouseButtons.None,0,plot.Width/2,100,-120)});
        if(nav.View.Start!=30)throw new Exception("Rueda sin desplazamiento");
        void FireMouse(string name,MouseEventArgs e)=>typeof(Control).GetMethod(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(plot,new object[]{e});
        FireMouse("OnMouseDown",new(MouseButtons.Left,1,plot.Width/2,100,0));
        FireMouse("OnMouseMove",new(MouseButtons.Left,0,plot.Width/2-80,100,0));
        FireMouse("OnMouseUp",new(MouseButtons.Left,1,plot.Width/2-80,100,0));
        if(nav.View.Start<=30)throw new Exception("Arrastre sin desplazamiento");
        nav.ReturnLive();
        if(!nav.View.IsLive || nav.View.Start!=601)throw new Exception("No vuelve al final");
        var endKey=typeof(Control).GetMethod("OnKeyDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        endKey.Invoke(plot,new object[]{new KeyEventArgs(Keys.Home)});
        if(nav.View.Start!=0)throw new Exception("Inicio no navega al principio");
        endKey.Invoke(plot,new object[]{new KeyEventArgs(Keys.End)});
        if(!nav.View.IsLive)throw new Exception("Fin no restaura en vivo");
        Console.WriteLine("PASS: red: historial, muestra entrante, rueda, arrastre, Inicio/Fin y retorno a vivo.");
        using var graph=new Bitmap(plot.Width,plot.Height); plot.DrawToBitmap(graph,new Rectangle(Point.Empty,graph.Size));
        using var png=new MemoryStream(); graph.Save(png,System.Drawing.Imaging.ImageFormat.Png);
        string exported=monitor.Export(png.ToArray());
        using(var zip=System.IO.Compression.ZipFile.OpenRead(exported)) {
            var entry=zip.GetEntry("grafica_ping.png") ?? throw new Exception("Informe sin gráfica");
            using var source=entry.Open(); using var image=new Bitmap(source);
            if(image.Width!=plot.Width || zip.GetEntry("red_nombres.json") is null) throw new Exception("Exportación incompleta");
        }
        File.Move(exported,Path.Combine(folder,Path.GetFileName(exported)));
        var history=Enumerable.Range(0,700).Select(i=>new MetricSnapshot(begin.DateTime.AddSeconds(i*2),i%101,30,91,7,.7,7.7,25,31,1024,700,0,0,0,0,0,55,48,"CPU","GPU","GPU","DEMO")).ToList();
        tabs.SelectedIndex=0; Application.DoEvents();
        foreach(bool temperatures in new[]{false,true}) {
            using var resources=new HistoryPlot(temperatures,history){Dock=DockStyle.Fill};
            tabs.TabPages[0].Controls.Add(resources);form.PerformLayout();Application.DoEvents();
            using var render=new Bitmap(resources.Width,resources.Height);resources.DrawToBitmap(render,new Rectangle(Point.Empty,resources.Size));
            var resourceNav=resources.Controls.OfType<PlotNavigation>().Single();
            if(resourceNav.View.Start!=400 || !resourceNav.View.IsLive)throw new Exception("Recursos no inicia al final");
            resourceNav.ScrollTo(10);resources.DrawToBitmap(render,new Rectangle(Point.Empty,resources.Size));
            if(resourceNav.View.Start!=10 || resourceNav.View.IsLive)throw new Exception("Recursos no permite navegar");
            render.Save(Path.Combine(args[0],temperatures?"temperaturas-historial-1.0.17.png":"recursos-historial-1.0.17.png"));
            resourceNav.ReturnLive();if(!resourceNav.View.IsLive)throw new Exception("Recursos no vuelve a vivo");
            tabs.TabPages[0].Controls.Remove(resources);
        }
        using(var sheet=new Bitmap(600,210)) {
            using var g=Graphics.FromImage(sheet);g.Clear(Theme.Bg);
            using var label=new Font("Segoe UI",12);
            int row=0;foreach(var (name,color) in new[]{("CPU",Theme.Cpu),("GPU",Theme.Gpu),("RAM",Theme.Ram)}) {
                using var brush=new SolidBrush(color);g.DrawString(name,label,brush,8,row*65+18);
                int column=0;foreach(var percentage in new[]{91d,99.6,100,99.6}) {
                    using var icon=new Bitmap(32,32);using(var ig=Graphics.FromImage(icon))TrayIconRenderer.DrawBar(ig,32,color,percentage);
                    g.DrawImageUnscaled(icon,100+column*115,row*65+5);g.DrawString(percentage+"%",label,brush,95+column*115,row*65+37);column++;
                }row++;
            }sheet.Save(Path.Combine(args[0],"barras-alertas-1.0.17.png"));
        }
        Console.WriteLine("PASS: gráfico PNG y etiquetas exportados; botones con Lucide; cambio de tamaño sin recortes.");
    }
}
