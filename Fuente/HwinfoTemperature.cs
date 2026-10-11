using System.IO.MemoryMappedFiles;
using System.Text;

namespace NitroPulse;

// HWiNFO SM2 packed layout: header 44+ bytes, sensor 264+, reading 316+.
// Opens an existing producer only; never loads a hardware driver.
internal static class HwinfoTemperature
{
    public static double? ReadCpuTemperature(out string source, out string status)
    {
        source = "";
        bool acquired = false;
        Mutex? mutex = null;
        try
        {
            using var map = MemoryMappedFile.OpenExisting(@"Global\HWiNFO_SENS_SM2", MemoryMappedFileRights.Read);
            mutex = Mutex.OpenExisting(@"Global\HWiNFO_SM2_MUTEX");
            try { acquired = mutex.WaitOne(50); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) { status = "HWiNFO ocupado; esperando siguiente lectura"; return null; }
            using var view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            if (view.Capacity < 44 || view.Capacity > 16 * 1024 * 1024)
            { status = "HWiNFO: tamaño de memoria inválido"; return null; }
            var bytes = new byte[(int)view.Capacity];
            view.ReadArray(0, bytes, 0, bytes.Length);
            return Parse(bytes, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out source, out status);
        }
        catch (FileNotFoundException) { status = "HWiNFO: Windows no encuentra la memoria compartida (error 2); no confirma que la opción esté desactivada"; return null; }
        catch (WaitHandleCannotBeOpenedException) { status = "HWiNFO: sincronización no disponible"; return null; }
        catch (UnauthorizedAccessException) { status = "HWiNFO: acceso a memoria compartida denegado"; return null; }
        catch (IOException) { status = "HWiNFO: memoria compartida no disponible"; return null; }
        finally { if (acquired) mutex!.ReleaseMutex(); mutex?.Dispose(); }
    }

    internal static double? Parse(byte[] bytes, long now, out string source, out string status)
    {
        source = "";
        status = "HWiNFO: formato no compatible";
        if (bytes.Length < 44 || BitConverter.ToUInt32(bytes, 0) != 0x48576953 ||
            BitConverter.ToUInt32(bytes, 4) != 1) return null;
        long age = now - BitConverter.ToInt64(bytes, 12);
        if (age < -5 || age > 15) { status = "HWiNFO: datos caducados (más de 15 segundos)"; return null; }
        uint so = BitConverter.ToUInt32(bytes, 20), ss = BitConverter.ToUInt32(bytes, 24), sn = BitConverter.ToUInt32(bytes, 28);
        uint ro = BitConverter.ToUInt32(bytes, 32), rs = BitConverter.ToUInt32(bytes, 36), rn = BitConverter.ToUInt32(bytes, 40);
        if (so < 44 || ro < 44 || ss < 264 || rs < 316 || sn > 4096 || rn > 32768 ||
            (ulong)so + (ulong)ss * sn > (ulong)bytes.Length ||
            (ulong)ro + (ulong)rs * rn > (ulong)bytes.Length ||
            ((ulong)so < (ulong)ro + (ulong)rs * rn && (ulong)ro < (ulong)so + (ulong)ss * sn)) return null;
        string Text(int offset, int count) => Encoding.Latin1.GetString(bytes, offset, count).Split('\0')[0].Trim();
        double? best = null;
        int bestRank = 0;
        for (uint i = 0; i < rn; i++)
        {
            int p = checked((int)(ro + (ulong)i * rs));
            uint index = BitConverter.ToUInt32(bytes, p + 4);
            if (BitConverter.ToUInt32(bytes, p) != 1 || index >= sn) continue;
            string sensor = Text(checked((int)(so + (ulong)index * ss)) + 8, 128);
            string label = Text(p + 12, 128);
            string unit = Text(p + 268, 16);
            // Original labels avoid user renames. Reject thermal headroom and GPU readings.
            if (!sensor.StartsWith("CPU", StringComparison.OrdinalIgnoreCase) ||
                !(unit == "°C" || unit == "C")) continue;
            int rank = label.Equals("CPU Package", StringComparison.OrdinalIgnoreCase) ? 3 :
                label.Equals("Core Max", StringComparison.OrdinalIgnoreCase) ? 2 :
                label.Equals("Core Temperatures", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            double value = BitConverter.ToDouble(bytes, p + 284);
            if (rank <= bestRank || !double.IsFinite(value) || value < 0 || value >= 150) continue;
            best = value;
            bestRank = rank;
            source = "HWiNFO (solo lectura): " + sensor + ": " + label;
        }
        status = best.HasValue ? "HWiNFO: temperatura CPU disponible" : "HWiNFO: no publica CPU Package/Core Max en Celsius";
        return best;
    }
}
