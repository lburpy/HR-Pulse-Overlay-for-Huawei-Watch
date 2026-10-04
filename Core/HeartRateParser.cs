namespace PulseOverlay.Core;

public readonly record struct HeartRateMeasurement(
    int Bpm,
    bool? SensorContact,
    int? EnergyExpended,
    IReadOnlyList<double> RrIntervalsMs);

/// <summary>Decodes the Bluetooth "Heart Rate Measurement" characteristic (0x2A37).</summary>
public static class HeartRateParser
{
    public static bool TryParse(ReadOnlySpan<byte> data, out HeartRateMeasurement measurement)
    {
        measurement = default;
        if (data.Length < 2) return false;

        byte flags = data[0];
        int i;
        int bpm;
        if ((flags & 0x01) != 0)
        {
            if (data.Length < 3) return false;
            bpm = data[1] | data[2] << 8;
            i = 3;
        }
        else
        {
            bpm = data[1];
            i = 2;
        }

        // Bit 2 = contact feature supported, bit 1 = contact detected
        bool? contact = (flags & 0x04) != 0 ? (flags & 0x02) != 0 : null;

        int? energy = null;
        if ((flags & 0x08) != 0)
        {
            if (data.Length < i + 2) return false;
            energy = data[i] | data[i + 1] << 8;
            i += 2;
        }

        var rr = new List<double>();
        if ((flags & 0x10) != 0)
        {
            // RR intervals arrive in 1/1024 s units
            for (; i + 1 < data.Length; i += 2)
                rr.Add((data[i] | data[i + 1] << 8) * 1000.0 / 1024.0);
        }

        if (bpm is <= 0 or > 300) return false;
        measurement = new HeartRateMeasurement(bpm, contact, energy, rr);
        return true;
    }
}
