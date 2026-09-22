namespace Pulse.Core.Models;

using System;
using System.IO;
using System.Text.Json;

public class AppSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse");
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

    public bool IsPanelVisible { get; set; } = true;
    public bool HideInFullScreen { get; set; } = true;
    public string PanelSize { get; set; } = "standard"; // small, standard, large
    public string RailSpacing { get; set; } = "standard"; // tight, standard, loose
    public string Theme { get; set; } = "dark"; // dark, light, purple, glass
    public bool UsesGlass { get; set; } = false;
    public bool AutoCollapse { get; set; } = true;
    public string Position { get; set; } = "right"; // left, top, free, right
    public bool SideRailShowsPercentages { get; set; } = true;

    public event Action? SettingsChanged;

    public void NotifyChanged()
    {
        SettingsChanged?.Invoke();
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch { }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
            NotifyChanged();
        }
        catch { }
    }
}
