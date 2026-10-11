using System.IO.MemoryMappedFiles;
using System.Text;

namespace NitroPulse;

/// <summary>
/// Optional, read-only CPU temperature from MSI Afterburner's published sensor
/// data. If Afterburner isn't running, or it doesn't publish CPU temperature,
/// this provider returns null. It does not launch Afterburner or a driver.
/// </summary>
internal static class AfterburnerTemperature
{
    private const int NameLength = 260;
    private const int DataOffset = NameLength * 5;
    private const int MinEntryBytes = DataOffset + 24;

    public static double? ReadCpuTemperature(out string source)
    {
        source = "";
        try
        {
            using var map = MemoryMappedFile.OpenExisting("MAHMSharedMemory", MemoryMappedFileRights.Read);
            using var view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            long length = view.Capacity;
            if (length < 32 || view.ReadUInt32(0) != 0x4D41484D) return null;
            uint header = view.ReadUInt32(8);
            uint count = view.ReadUInt32(12);
            uint entrySize = view.ReadUInt32(16);
            if (header < 32 || header > 4096 || count == 0 || count > 1024 ||
                entrySize < MinEntryBytes || entrySize > 8192) return null;
            if ((long)header + (long)count * entrySize > length) return null;

            double? candidate = null;
            string candidateName = "";
            for (uint i = 0; i < count; i++)
            {
                long offset = (long)header + (long)i * entrySize;
                // Sensor name and data are fixed fields in MAHM version 2.x.
                byte[] nameBuffer = new byte[NameLength];
                view.ReadArray(offset, nameBuffer, 0, NameLength);
                string name = Encoding.ASCII.GetString(nameBuffer).TrimEnd('\0').Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                bool isCpuTemperature =
                    (name.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Package", StringComparison.OrdinalIgnoreCase)) &&
                    (name.Contains("temp", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("thermal", StringComparison.OrdinalIgnoreCase));
                if (!isCpuTemperature) continue;
                float raw = view.ReadSingle(offset + DataOffset);
                if (!float.IsFinite(raw) || raw < 5f || raw > 110f) continue;
                if (name.Equals("CPU temperature", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("CPU Package", StringComparison.OrdinalIgnoreCase))
                {
                    source = "MSI Afterburner (solo lectura): " + name;
                    return raw;
                }
                candidate ??= raw;
                candidateName = name;
            }
            if (candidate.HasValue) source = "MSI Afterburner (solo lectura): " + candidateName;
            return candidate;
        }
        catch (FileNotFoundException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
        catch (ArgumentException) { return null; }
        catch { return null; }
    }
}
