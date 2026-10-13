using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace NitroPulse;

internal static class NetworkIdentity
{
    // Typed dual interfaces avoid VARIANT marshaling of GUID return values.
    [ComImport, Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INlmConnection
    {
        INlmNetwork GetNetwork();
        [return: MarshalAs(UnmanagedType.VariantBool)] bool GetIsConnectedToInternet();
        [return: MarshalAs(UnmanagedType.VariantBool)] bool GetIsConnected();
        int GetConnectivity();
        Guid GetConnectionId(); Guid GetAdapterId();
    }
    [ComImport, Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INlmNetwork
    {
        [return: MarshalAs(UnmanagedType.BStr)] string GetName();
        void SetName([MarshalAs(UnmanagedType.BStr)] string name);
        [return: MarshalAs(UnmanagedType.BStr)] string GetDescription();
        void SetDescription([MarshalAs(UnmanagedType.BStr)] string description);
        Guid GetNetworkId();
    }
    [DllImport("iphlpapi.dll")] private static extern uint GetBestInterface(uint address, out uint index);
    [DllImport("iphlpapi.dll")] private static extern uint GetBestInterfaceEx(byte[] address, out uint index);
    [DllImport("iphlpapi.dll")] private static extern uint GetBestRoute(uint address, uint source, out ForwardRow route);
    [StructLayout(LayoutKind.Sequential)] private struct ForwardRow
    {
        public uint Destination, Mask, Policy, NextHop, Index, Type, Protocol, Age, NextHopAs, Metric1, Metric2, Metric3, Metric4, Metric5;
    }
    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr client);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr client, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanQueryInterface(IntPtr client, ref Guid id, int opcode, IntPtr reserved, out uint size, out IntPtr data, out int valueType);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr memory);
    [StructLayout(LayoutKind.Sequential)] private struct Ssid
    { public uint Length; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Bytes; }
    [StructLayout(LayoutKind.Sequential)] private struct Association
    {
        public Ssid Ssid; public int BssType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid;
        public int PhyType; public uint PhyIndex, Signal, RxRate, TxRate;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Connection
    {
        public int State, Mode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Profile;
        public Association Association;
        public int SecurityEnabled, OneXEnabled, Auth, Cipher;
    }

    internal static NetworkContext Read()
    {
        var links = new List<LinkInfo>();
        var profiles = Profiles(out var profileNote);
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            try
            {
                var properties = nic.GetIPProperties();
                int index = -1, index6 = -1;
                try { index = properties.GetIPv4Properties()?.Index ?? -1; } catch (NetworkInformationException) { }
                try { index6 = properties.GetIPv6Properties()?.Index ?? -1; } catch (NetworkInformationException) { }
                var link = new LinkInfo(nic.Id, index, nic.Name, nic.NetworkInterfaceType.ToString(),
                    properties.UnicastAddresses.Select(a => a.Address.ToString()).OrderBy(a => a, StringComparer.Ordinal).ToArray(),
                    properties.GatewayAddresses.Select(a => a.Address.ToString()).Where(a => a != "0.0.0.0" && a != "::").OrderBy(a => a, StringComparer.Ordinal).ToArray(),
                    properties.DnsAddresses.Select(a => a.ToString()).ToArray(), Ipv6Index: index6);
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) link = Wifi(link);
                if (profiles.TryGetValue(nic.Id, out var profile)) link = link with { ProfileId = profile.Id, ProfileName = profile.Name };
                else link = link with { ProfileNote = string.IsNullOrEmpty(profileNote) ? "Windows no proporcionó perfil; etiqueta vinculada por SSID o interfaz/gateway" : profileNote };
                links.Add(link);
            }
            catch (NetworkInformationException) { /* Adapter may have disappeared during enumeration. */ }
        }
        string Route(string address)
        {
            uint result = GetBestInterface(BitConverter.ToUInt32(IPAddress.Parse(address).GetAddressBytes()), out uint index);
            return result == 0 ? links.FirstOrDefault(l => l.Index == index)?.Id ?? "" : "";
        }
        string Gateway(string address) => GetBestRoute(BitConverter.ToUInt32(IPAddress.Parse(address).GetAddressBytes()), 0, out var row) == 0 && row.NextHop != 0
            ? new IPAddress(BitConverter.GetBytes(row.NextHop)).ToString() : "";
        return new(links.ToArray(), Route("1.1.1.1"), Route("8.8.8.8"), Gateway("1.1.1.1"), Gateway("8.8.8.8"));
    }

    private static Dictionary<string, (string Id, string Name)> Profiles(out string note)
    {
        note = "";
        var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        object? manager = null, connections = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"));
            manager = Activator.CreateInstance(type!);
            connections = ((dynamic)manager!).GetNetworkConnections();
            foreach (object connection in (System.Collections.IEnumerable)connections)
            {
                object? network = null;
                try
                {
                    var connected = (INlmConnection)connection;
                    if (!connected.GetIsConnected()) continue;
                    var profile = connected.GetNetwork(); network = profile;
                    Guid adapter = connected.GetAdapterId();
                    result[adapter.ToString("B")] = (profile.GetNetworkId().ToString("D"), profile.GetName());
                }
                finally { Release(network); Release(connection); }
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException or ArgumentException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or PlatformNotSupportedException)
        { note = "Perfil de Windows no consultable (se usa SSID o interfaz/gateway): " + e.GetType().Name + " " + e.Message; }
        finally { Release(connections); Release(manager); }
        return result;
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

    internal static string RouteId(string address, NetworkContext context)
    {
        var ip = IPAddress.Parse(address);
        byte[] bytes = new byte[ip.AddressFamily == AddressFamily.InterNetwork ? 16 : 28];
        BitConverter.GetBytes((ushort)ip.AddressFamily).CopyTo(bytes, 0);
        ip.GetAddressBytes().CopyTo(bytes, ip.AddressFamily == AddressFamily.InterNetwork ? 4 : 8);
        if (ip.AddressFamily == AddressFamily.InterNetworkV6) BitConverter.GetBytes((uint)ip.ScopeId).CopyTo(bytes, 24);
        uint result = GetBestInterfaceEx(bytes, out uint index);
        return result == 0 ? context.Links.FirstOrDefault(l => (ip.AddressFamily == AddressFamily.InterNetwork ? l.Index : l.Ipv6Index) == index)?.Id ?? "" : "";
    }

    private static LinkInfo Wifi(LinkInfo link)
    {
        if (!Guid.TryParse(link.Id, out var id)) return link with { WifiNote = "Identificador Wi-Fi no disponible" };
        uint opened = WlanOpenHandle(2, IntPtr.Zero, out _, out var client);
        if (opened != 0) return link with { WifiNote = "Wi-Fi no consultable: " + opened };
        IntPtr data = IntPtr.Zero;
        try
        {
            uint queried = WlanQueryInterface(client, ref id, 7, IntPtr.Zero, out var size, out data, out _);
            if (queried != 0 || size < Marshal.SizeOf<Connection>()) return link with { WifiNote = "SSID/señal no disponibles: " + queried };
            var value = Marshal.PtrToStructure<Connection>(data);
            if (value.State != 1) return link with { WifiNote = "Wi-Fi cambió durante la consulta" };
            return link with { Ssid = Encoding.UTF8.GetString(value.Association.Ssid.Bytes, 0, (int)Math.Min(32, value.Association.Ssid.Length)),
                Bssid = string.Join(':', value.Association.Bssid.Select(b => b.ToString("X2"))), Signal = (int)value.Association.Signal };
        }
        finally { if (data != IntPtr.Zero) WlanFreeMemory(data); WlanCloseHandle(client, IntPtr.Zero); }
    }
}
