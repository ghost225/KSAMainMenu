// The menu's own settings (settings.json) and the launch request a restart hands to the next launch (launch.json),
// both in the mod's folder.

using System.Text.Json;
using Brutal.Logging;

namespace KSAMainMenu;

internal sealed class KittenPlacement
{
    public string Character { get; set; } = "HunterKitten";
    /// Metres from the camera: right, up, forward.
    public double Right { get; set; }
    public double Up { get; set; }
    public double Forward { get; set; } = 6;
    /// Tumble rate (degrees per second) and bob amplitude (m); each kitten gets its own phase.
    public double SpinDegPerSec { get; set; } = 4;
    public double Bob { get; set; } = 0.15;
    /// Start orientation relative to "facing the camera, head up" (degrees).
    public double Yaw { get; set; }
    public double Pitch { get; set; }
    public double Roll { get; set; }
}

internal sealed class MenuSettings
{
    /// Go straight into the game (no menu) at launch; the pause menu's MAIN MENU still opens it.
    public bool SkipMenu;
    /// Show KSA's own start-up Configuration popup instead of choosing the system ourselves.
    public bool ClassicPopup;
    /// Ask which world (Sol, Kerbol, ...) before KSA loads; off = the last world played.
    public bool AskWorld = true;
    public bool Kittens = true;
    /// Camera height above the home body's surface, in body radii.
    public double AltitudeRadii = 0.6;
    /// How far below the view centre the planet's centre sits (degrees); larger shows more sky.
    public double PlanetBelowDeg = 44;
    /// Angle from the sub-solar point to the camera's ground point (90 = over the terminator).
    public double SunAngleDeg = 62;
    /// Slow drift of the camera around the planet (degrees per minute) so the world seems to turn.
    public double DriftDegPerMin = 1.5;
    public List<KittenPlacement> KittenPlacements { get; set; } = new()
    {
        new() { Character = "HunterKitten", Right = 1.6, Up = 0.2, Forward = 5.5, SpinDegPerSec = 3, Yaw = -15, Roll = 10 },
        new() { Character = "BanjoKitten", Right = 3.6, Up = 1.4, Forward = 8.5, SpinDegPerSec = -4, Yaw = 20, Pitch = 10, Roll = -25 },
        new() { Character = "PolarisKitten", Right = 0.3, Up = -1.3, Forward = 9.5, SpinDegPerSec = 5, Yaw = -25, Pitch = -8, Roll = 160 },
    };
}

/// What the next launch should do: written before a restart, consumed (deleted) at start-up.
internal sealed class LaunchRequest
{
    public string System { get; set; } = "";
    /// "menu", "continue" (load Save) or "new" (start a new game called Name).
    public string Action { get; set; } = "menu";
    public string? Save { get; set; }
    public string? Name { get; set; }
    public bool Career { get; set; }
    /// KSA's GameType for the new universe: true = Testing (KSA's start rocket), false = Sandbox (no vehicles).
    public bool? TestRocket { get; set; }
}

internal static class MenuConfig
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, IncludeFields = true };

    public static MenuSettings Settings = new();
    /// The request this launch was started with (null for a normal launch).
    public static LaunchRequest? Launch;

    private static string SettingsPath => Path.Combine(Mod.Dir, "settings.json");
    private static string LaunchPath => Path.Combine(Mod.Dir, "launch.json");

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                Settings = JsonSerializer.Deserialize<MenuSettings>(File.ReadAllText(SettingsPath), Json) ?? new();
            else
                Save();
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] settings.json unreadable, using defaults: {ex.Message}");
            Settings = new();
        }
        try
        {
            if (File.Exists(LaunchPath))
            {
                Launch = JsonSerializer.Deserialize<LaunchRequest>(File.ReadAllText(LaunchPath), Json);
                File.Delete(LaunchPath);   // one launch only, even if this one crashes
                DefaultCategory.Log.Info($"[KSAMainMenu] launch request: {Launch?.Action} in '{Launch?.System}' " +
                                         $"{Launch?.Save ?? Launch?.Name}");
            }
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] launch.json unreadable: {ex.Message}");
            Launch = null;
        }
    }

    public static void Save()
    {
        try
        {
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings, Json));
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] couldn't write settings.json: {ex.Message}");
        }
    }

    public static void WriteLaunch(LaunchRequest request) =>
        File.WriteAllText(LaunchPath, JsonSerializer.Serialize(request, Json));
}
