using System.IO;
using System.Text.Json;

namespace PulseOverlay.Core;

public sealed class SavedDevice
{
    /// <summary>Stable id (first address seen); also used in per-watch overlay URLs.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Last address that delivered data; empty = same as Id.</summary>
    public string Address { get; set; } = "";
    public bool? RandomAddress { get; set; }
}

/// <summary>Stored in %APPDATA%\PulseOverlay\settings.json.</summary>
public sealed class AppSettings
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public int Port { get; set; } = 8790;
    public List<SavedDevice> Devices { get; set; } = [];

    static string FolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PulseOverlay");

    static string FilePath => Path.Combine(FolderPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { /* corrupt file: start fresh */ }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { /* settings are a convenience; never crash over them */ }
    }
}
