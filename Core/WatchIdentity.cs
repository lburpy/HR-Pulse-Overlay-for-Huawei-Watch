namespace PulseOverlay.Core;

/// <summary>
/// Recognises a saved watch when it shows up under another address or name. Huawei watches use a
/// different random address (and name) in HR broadcast mode, e.g. "HUAWEI WATCH FIT 5 Pro-A1B" at
/// 5C1D3E4F2A1B becomes "HUAWEI WATCH HR-A1B" at DC1F3E4F2A1B.
/// </summary>
public static class WatchIdentity
{
    public static bool IsSameWatch(string savedName, IEnumerable<ulong> savedAddresses, ulong seenAddress, string? seenName)
    {
        foreach (var saved in savedAddresses)
        {
            if (saved == seenAddress) return true;
            // The lower 4 bytes survive the switch to the broadcast address
            if ((saved & 0xFFFFFFFF) == (seenAddress & 0xFFFFFFFF)) return true;
        }
        if (seenName == null) return false;
        return string.Equals(savedName.Trim(), seenName.Trim(), StringComparison.OrdinalIgnoreCase)
            || SameFamily(savedName, seenName);
    }

    /// <summary>Same brand word and same "-A1B"-style suffix.</summary>
    static bool SameFamily(string a, string b)
    {
        var (brandA, suffixA) = Split(a);
        var (brandB, suffixB) = Split(b);
        return brandA.Length >= 3 && suffixA.Length >= 3
            && brandA.Equals(brandB, StringComparison.OrdinalIgnoreCase)
            && suffixA.Equals(suffixB, StringComparison.OrdinalIgnoreCase);
    }

    static (string brand, string suffix) Split(string name)
    {
        name = name.Trim();
        int space = name.IndexOf(' ');
        int dash = name.LastIndexOf('-');
        return (space > 0 ? name[..space] : "", dash >= 0 && dash < name.Length - 1 ? name[(dash + 1)..] : "");
    }
}
