using NitroPulse;
using System.IO.Compression;
using System.Text;

string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures-" + Guid.NewGuid().ToString("N"));
int count = 0;
void Assert(bool pass, string name) { if (!pass) throw new Exception(name); count++; }
var link = new LinkInfo("adapter-a",1,"Wi-Fi","Wireless80211",new[]{"192.168.1.20"},new[]{"192.168.1.1"},new[]{"192.168.1.1"},"Casa","AA:BB:CC:DD:EE:FF",75);
var context = new NetworkContext(new[]{link},link.Id,link.Id);
var other = new NetworkContext(new[]{link with {Ssid="Oficina"}},link.Id,link.Id);
NetworkProbe Probe(string protocol, ProbeState state, double? ms=20,string name="Cloudflare") => new(name,"target",protocol,state,ms,"test",link.Id);
var stats = new ProbeStatistics();
stats.Add(Probe("ICMP",ProbeState.Ok,10)); stats.Add(Probe("ICMP",ProbeState.Ok,30)); stats.Add(Probe("ICMP",ProbeState.Failed,null)); stats.Add(Probe("ICMP",ProbeState.Ok,50));
Assert(stats.Attempts==4 && stats.Failed==1 && stats.FailurePercent==25,"packet counts");
Assert(stats.Jitter==20 && stats.Average==30,"jitter skips losses");
Assert(context.Key != other.Key,"SSID differentiates networks");
Assert(context.Key == (context with {Links=new[]{link with {Signal=10}}}).Key,"signal does not split connection");
Assert(context.Key != (context with {Links=new[]{link with {Bssid="00:00:00:00:00:00"}}}).Key,"AP roaming changes connection");
Assert(context.Key != (context with {Links=new[]{link with {Dns=new[]{"8.8.8.8"}}}}).Key,"DNS changes connection");
Assert((context with {RouteCloudflare=""}).Main?.Id==link.Id,"secondary public route fallback");
Assert(context.Key != (context with {RouterCloudflare="192.168.1.254"}).Key,"gateway route changes connection");
Assert(!NetworkAssessment.Evaluate(context,new[]{Probe("ICMP",ProbeState.Failed),Probe("TCP",ProbeState.Ok)}).StartsWith("Posible"),"no false outage on ping loss");
Assert(NetworkAssessment.Evaluate(context,new[]{Probe("TCP",ProbeState.Failed),Probe("TCP",ProbeState.Failed,name:"Google"),Probe("ICMP",ProbeState.Ok,name:"Router")}).StartsWith("Posible fallo de salida"),"router vs internet");
Assert(NetworkAssessment.Evaluate(context,new[]{Probe("TCP",ProbeState.Ok),Probe("DNS/UDP",ProbeState.Failed)}).StartsWith("Posible problema DNS"),"DNS vs internet");
Assert(NetworkAssessment.Evaluate(context with {RouteCloudflare="",RouteGoogle=""},Array.Empty<NetworkProbe>()).Contains("IPv6 no evaluado"),"IPv4 failure is not internet failure");
Assert(NetworkAssessment.Csv("=malicious;\"SSID").StartsWith("\"'="),"CSV injection");
byte[] reply = new byte[12]; reply[0]=0x12; reply[1]=0x34; reply[2]=0x80; reply[7]=1;
Assert(NetworkProbes.DnsResponse(reply,0x1234).State==ProbeState.Ok,"DNS success");
reply[3]=2; Assert(NetworkProbes.DnsResponse(reply,0x1234).State==ProbeState.Failed,"DNS SERVFAIL");
reply[3]=0; reply[2]|=2; Assert(NetworkProbes.DnsResponse(reply,0x1234).State==ProbeState.Inconclusive,"DNS truncated");
Assert(NetworkProbes.DnsResponse(reply,1).State==ProbeState.Inconclusive,"DNS transaction");
var at = DateTimeOffset.Parse("2026-10-11T08:00:00-05:00");
using (var monitor = new NetworkMonitor(fixture,new NetworkLabels(Path.Combine(fixture,"labels.json"))))
{
    monitor.Record(at,context,new[]{Probe("TCP",ProbeState.Ok)});
    monitor.Record(at.AddSeconds(10),context,new[]{Probe("TCP",ProbeState.Ok)});
    monitor.Record(at.AddSeconds(20),other,new[]{Probe("TCP",ProbeState.Failed)});
    Assert(monitor.Rows().Length==2,"network segments");
    monitor.Record(at.AddSeconds(70),other,new[]{Probe("TCP",ProbeState.Ok)});
    Assert(monitor.Rows().Length==3,"sleep gaps segmented");
    var discarded=monitor.Record(at.AddSeconds(80),other,new[]{Probe("TCP",ProbeState.Failed)},changed:true);
    Assert(discarded.Probes.Length==0 && monitor.Rows().Length==4,"mid-probe change discarded");
    monitor.RenameCurrent("Operador oficina");
    Assert(monitor.Rows()[0].Network=="Operador oficina","operator label");
    var failed=new[]{Probe("TCP",ProbeState.Failed),Probe("TCP",ProbeState.Failed,name:"Google")};
    monitor.Record(at.AddSeconds(90),other,failed); monitor.Record(at.AddSeconds(100),other,failed);
    monitor.Record(at.AddSeconds(110),other,new[]{Probe("TCP",ProbeState.Ok)});
    Assert(monitor.Report().Contains("20s"),"episode boundaries");
    string exported=monitor.Export();
    using(var zip=ZipFile.OpenRead(exported)) {
        Assert(zip.GetEntry("Informe_red.txt") is not null && zip.GetEntry("red_mediciones.csv") is not null,"operator export");
        Assert(!zip.Entries.Any(e=>e.Name.Contains("procesos")),"export excludes processes");
    }
    // Keep simulated reports out of the operator's real export folder.
    File.Move(exported,Path.Combine(fixture,Path.GetFileName(exported)));
    Assert(monitor.Report().Contains("-05:00"),"timestamps offset");
    monitor.Record(at.AddSeconds(120),other with {Links=new[]{link with {Ssid="Oficina",Bssid="11:22:33:44:55:66",Addresses=new[]{"192.168.1.99"},Dns=new[]{"8.8.8.8"}}}},Array.Empty<NetworkProbe>());
    Assert(monitor.Rows()[0].Network=="Operador oficina","label survives roaming, DHCP and DNS changes");
    var reloaded=new NetworkLabels(Path.Combine(fixture,"labels.json"));
    Assert(reloaded.Get(other)=="Operador oficina","label survives restart");
    Assert(reloaded.Get(context)=="","different Wi-Fi not mislabeled");
    bool guarded=false; try { monitor.RenameCurrent("wrong",context.LabelKey); } catch(InvalidOperationException) {guarded=true;}
    Assert(guarded,"network changed while labeling guarded");
    var ethernet=link with {Type="Ethernet",Ssid="",ProfileId="profile-claro"};
    var ethernetContext=context with {Links=new[]{ethernet}};
    reloaded.Set(ethernetContext,"Claro Fibra");
    Assert(new NetworkLabels(Path.Combine(fixture,"labels.json")).Get(ethernetContext with {Links=new[]{ethernet with {Addresses=new[]{"192.168.40.99"},Bssid="",Gateways=new[]{"192.168.40.1"}}}})=="Claro Fibra","Ethernet profile binding stable");
    Assert(reloaded.Get(ethernetContext with {Links=new[]{ethernet with {ProfileId="profile-other"}}})=="","different Ethernet profile not mislabeled");
    string legacyRoot=Path.Combine(fixture,"legacy"), legacy=Path.Combine(legacyRoot,"Sesion_demo"); Directory.CreateDirectory(legacy);
    foreach(var name in new[]{"red_conexiones.jsonl","red_eventos.csv"}) File.Copy(Path.Combine(fixture,name),Path.Combine(legacy,name));
    var recovered=new NetworkLabels(Path.Combine(fixture,"recovered.json")); recovered.ImportLegacy(legacyRoot);
    Assert(recovered.Get(other)=="Operador oficina","legacy label recovered");
    Assert(recovered.Get(other with {Links=new[]{other.Links[0] with {ProfileId="new-windows-profile"}}})=="Operador oficina","legacy fallback with Windows profile");
    Assert(recovered.Get(other with {Links=new[]{other.Links[0] with {ProfileId="different-profile-same-gateway"}}})=="","legacy label not inherited by a different profile");
    var history=monitor.History(); Assert(history.Length>0,"timeline retained");
    Assert(!NetworkTimeline.CanJoin(history[1],history[2]),"chart does not connect networks");
    var a=new NetworkSample(at,1,context,new[]{Probe("ICMP",ProbeState.Ok,40)},"ok");
    var b=a with {At=at.AddSeconds(5),Probes=new[]{Probe("ICMP",ProbeState.Failed,null)}};
    Assert(NetworkTimeline.Summary(new[]{a,b}).Contains("1/2"),"chart ping loss summary");
    Assert(!NetworkTimeline.CanJoin(a,a with {At=at.AddMinutes(1)}),"chart does not bridge pauses");
    monitor.Record(at.AddSeconds(125),new NetworkContext(Array.Empty<LinkInfo>(),"",""),Array.Empty<NetworkProbe>());
    monitor.Record(at.AddSeconds(130),other,Array.Empty<NetworkProbe>());
    Assert(monitor.Rows()[0].Network=="Operador oficina","label survives disconnect and reconnect");
}
Console.WriteLine($"PASS: {count} pruebas de estabilidad, segmentación, DNS, informe y privacidad.");
if(args.Contains("--recover-labels")) {
    var saved=new NetworkLabels();
    saved.ImportLegacy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NitroPulse","Sesiones"));
    var active=NetworkIdentity.Read();
    Console.WriteLine("ETIQUETA RECUPERADA PARA RED ACTUAL: " + saved.Get(active));
}
if (args.Contains("--live"))
{
    string directory=Path.Combine(fixture,"live");
    using var live=new NetworkMonitor(directory,new NetworkLabels(Path.Combine(directory,"labels.json")));
    using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(25));
    for(int i=0;i<3;i++) {
        var sample=await live.SampleOnceAsync(cancel.Token);
        Console.WriteLine($"LIVE {i+1}: {sample.Context.Title}; {sample.Assessment}; interfaces={sample.Context.Links.Length}; perfil={sample.Context.Main?.ProfileId}");
        if(i==0) Console.WriteLine(sample.Context.Main?.ProfileNote);
        foreach(var p in sample.Probes) Console.WriteLine($" {p.Name} {p.Protocol} {p.Target}: {p.State} {p.Milliseconds:0.##}ms {p.Detail}");
        if(i<2) await Task.Delay(1000,cancel.Token);
    }
    live.Start(); await Task.Delay(1500,cancel.Token); await live.StopAsync();
    Assert(!live.Running,"safe stop lifecycle"); Console.WriteLine("PASS: inicio periódico y cierre/cancelación real sin acceder a sensores.");
    if(args.Contains("--trace")) Console.WriteLine(await live.TraceAsync(cancel.Token));
    Console.WriteLine("LIVE REPORT: " + live.Export());
}
