using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace NitroPulse;

internal static class NetworkProbes
{
    internal static async Task<NetworkProbe[]> MeasureAsync(NetworkContext context, CancellationToken cancel)
    {
        var tasks = new List<Task<NetworkProbe>>();
        foreach (var (name, target, id) in new[] { ("Cloudflare", "1.1.1.1", context.RouteCloudflare), ("Google", "8.8.8.8", context.RouteGoogle) })
        {
            if (string.IsNullOrEmpty(id)) continue;
            tasks.Add(PingAsync(name, target, id, cancel));
            tasks.Add(TcpAsync(name, target, id, cancel));
        }
        if (context.Main is { } link)
        {
            string gateway = !string.IsNullOrEmpty(context.RouterCloudflare) ? context.RouterCloudflare : context.RouterGoogle;
            if (!string.IsNullOrEmpty(gateway)) tasks.Add(PingAsync("Router", gateway, NetworkIdentity.RouteId(gateway, context), cancel));
            foreach (string dns in link.Dns.Take(2)) tasks.Add(DnsAsync(dns, NetworkIdentity.RouteId(dns, context), cancel));
        }
        return await Task.WhenAll(tasks);
    }

    internal static async Task<NetworkProbe> PingAsync(string name, string target, string id, CancellationToken cancel)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(IPAddress.Parse(target), TimeSpan.FromMilliseconds(1200), new byte[16], null, cancel);
            return new(name, target, "ICMP", reply.Status == IPStatus.Success ? ProbeState.Ok : ProbeState.Failed,
                reply.Status == IPStatus.Success ? reply.RoundtripTime : null, reply.Status.ToString(), id);
        }
        catch (Exception e) when (e is PingException or SocketException) { return new(name, target, "ICMP", ProbeState.Inconclusive, null, e.GetType().Name, id); }
    }

    private static async Task<NetworkProbe> TcpAsync(string name, string target, string id, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel); timeout.CancelAfter(2000);
        var clock = Stopwatch.StartNew();
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(IPAddress.Parse(target), 443, timeout.Token);
            return new(name, target + ":443", "TCP", ProbeState.Ok, clock.Elapsed.TotalMilliseconds, "Conexión TCP; no verifica HTTPS ni portal cautivo", id);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { return new(name, target + ":443", "TCP", ProbeState.Failed, null, "Timeout 2000ms", id); }
        catch (SocketException e) { return new(name, target + ":443", "TCP", e.SocketErrorCode == SocketError.AccessDenied ? ProbeState.Inconclusive : ProbeState.Failed, null, e.SocketErrorCode.ToString(), id); }
    }

    internal static byte[] DnsQuery(ushort transaction)
    {
        var query = new List<byte> { (byte)(transaction >> 8), (byte)transaction, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (string label in "example.com".Split('.')) { query.Add((byte)label.Length); query.AddRange(Encoding.ASCII.GetBytes(label)); }
        query.AddRange(new byte[] { 0, 0, 1, 0, 1 }); return query.ToArray();
    }
    internal static (ProbeState State, string Detail) DnsResponse(byte[] bytes, ushort transaction)
    {
        if (bytes.Length < 12 || bytes[0] != (byte)(transaction >> 8) || bytes[1] != (byte)transaction || (bytes[2] & 0x80) == 0)
            return (ProbeState.Inconclusive, "Respuesta DNS no válida");
        if ((bytes[2] & 2) != 0) return (ProbeState.Inconclusive, "Respuesta truncada; requiere TCP, no probado");
        int code = bytes[3] & 15;
        if (code != 0) return (ProbeState.Failed, "RCODE=" + code);
        return (bytes[6] != 0 || bytes[7] != 0) ? (ProbeState.Ok, "Respuesta DNS para example.com") : (ProbeState.Inconclusive, "DNS responde sin registros de respuesta");
    }
    private static async Task<NetworkProbe> DnsAsync(string target, string id, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel); timeout.CancelAfter(2000);
        var clock = Stopwatch.StartNew();
        try
        {
            var ip = IPAddress.Parse(target);
            using var udp = new UdpClient(ip.AddressFamily); udp.Connect(ip, 53);
            ushort transaction = (ushort)RandomNumberGenerator.GetInt32(65536);
            byte[] query = DnsQuery(transaction);
            await udp.SendAsync(query, timeout.Token);
            var reply = await udp.ReceiveAsync(timeout.Token);
            var parsed = DnsResponse(reply.Buffer, transaction);
            return new("DNS example.com", target + ":53", "DNS/UDP", parsed.State, clock.Elapsed.TotalMilliseconds, parsed.Detail, id);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { return new("DNS example.com", target + ":53", "DNS/UDP", ProbeState.Failed, null, "Timeout 2000ms; UDP53 puede estar filtrado", id); }
        catch (SocketException e) { return new("DNS example.com", target + ":53", "DNS/UDP", e.SocketErrorCode == SocketError.AccessDenied ? ProbeState.Inconclusive : ProbeState.Failed, null, e.SocketErrorCode.ToString(), id); }
    }
}
