// Saves for the menu, and the wrong-universe guard (both from KSASaveLauncher, which this mod replaces).
//
// A save records its system (meta.toml `systems`), but KSA never checks it: a load pours the save into the running
// universe and CelestialSystem.DeserializeSave drops every vehicle whose parent body this universe lacks. So the menu
// restarts into the save's system when it differs, and a load from KSA's own save window into the wrong universe is
// refused once with an explanation (a second try within 15 s goes ahead, as KSA would).

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Brutal.Logging;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace KSAMainMenu;

/// A save folder's meta.toml, read directly.
internal sealed record SaveInfo(string Id, string Name, DateTime Updated, string[] Systems, string Version)
{
    public string System => Systems.FirstOrDefault() ?? "";
    public bool SystemInstalled => System.Length == 0 || SelectSystem.Systems.Any(s => s.Id == System);
}

internal static class SaveFolder
{
    private static readonly Regex Str = new(@"^\s*(\w+)\s*=\s*""([^""]*)""", RegexOptions.Multiline);
    private static readonly Regex Systems = new(@"^\s*systems\s*=\s*\[([^\]]*)\]", RegexOptions.Multiline);
    private static readonly Regex Updated = new(@"^\s*updated\s*=\s*(\S+)", RegexOptions.Multiline);

    public static List<SaveInfo> All()
    {
        var list = new List<SaveInfo>();
        string root = GameSaves.SaveFolderPath;
        if (!Directory.Exists(root))
            return list;
        foreach (string dir in Directory.GetDirectories(root))
        {
            string meta = Path.Combine(dir, "meta.toml");
            if (!File.Exists(meta))
                continue;
            try
            {
                string text = File.ReadAllText(meta);
                var kv = Str.Matches(text).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
                Match sys = Systems.Match(text);
                string[] systems = sys.Success
                    ? sys.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(s => s.Trim('"')).Where(s => s.Length > 0).ToArray()
                    : Array.Empty<string>();
                DateTime updated = Updated.Match(text) is { Success: true } u &&
                                   DateTime.TryParse(u.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime t)
                    ? t : Directory.GetLastWriteTime(dir);
                list.Add(new SaveInfo(Path.GetFileName(dir), kv.GetValueOrDefault("name", Path.GetFileName(dir)), updated,
                    systems, kv.GetValueOrDefault("version", "")));
            }
            catch (Exception ex)
            {
                DefaultCategory.Log.Warning($"[KSAMainMenu] unreadable save {dir}: {ex.Message}");
            }
        }
        return list.OrderByDescending(s => s.Updated).ToList();
    }

    public static bool Exists(string name) => Directory.Exists(Path.Combine(GameSaves.SaveFolderPath, name));

    public static string SystemName(string id)
    {
        if (id.Length == 0)
            return "unknown system";
        return SelectSystem.Systems.FirstOrDefault(s => s.Id == id)?.GetName() is { Length: > 0 } n ? n : id;
    }
}

/// Which saves fit which worlds. A save keeps its vehicles wherever their parent bodies exist (KSA drops the rest), so a
/// save from world A loads into world B when B has every body A has: Kerbol into KerbolInterstellar, Sol into
/// SolSystemInterstellar (coordinator, 2026-10-26: body-based, not by system id). Both must also use the same part
/// profile: KerbolInterstellar holds Sol's bodies too, but a Sol save's real-scale craft would load as stock parts
/// (same part ids) around the wrong home.
internal static class Worlds
{
    private static readonly Dictionary<string, HashSet<string>?> Cache = new();

    public static HashSet<string>? Bodies(string world)
    {
        if (Cache.TryGetValue(world, out HashSet<string>? known))
            return known;
        HashSet<string>? bodies = null;
        try
        {
            if (SystemLibrary.Find(world) is { } t)
                bodies = t.Bodies.Where(b => b is not VehicleTemplate).Select(b => b.Id).ToHashSet();
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] bodies of '{world}': {ex.Message}");
        }
        return Cache[world] = bodies is { Count: > 0 } ? bodies : null;
    }

    public static bool CanLoadInto(string saveWorld, string world) =>
        saveWorld.Length == 0 || saveWorld == world ||
        (Profile(saveWorld) == Profile(world) &&
         Bodies(saveWorld) is { } from && Bodies(world) is { } into && from.IsSubsetOf(into));

    private static MethodInfo? _profileFor;
    private static bool _profileSearched;

    /// The world's part profile: KSAWorld's World.ProfileFor(systemId) when it has one, else its rule (worlds whose id
    /// starts with "Kerbol" are stock, the rest real).
    public static string Profile(string world)
    {
        if (!_profileSearched)
        {
            _profileSearched = true;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                if (asm.GetName().Name == "KSAWorld")
                    _profileFor = asm.GetType("KSAWorld.World")?.GetMethod("ProfileFor", new[] { typeof(string) });
        }
        try
        {
            if (_profileFor?.Invoke(null, new object[] { world }) is string p)
                return p;
        }
        catch
        {
            // fall back to the rule
        }
        return world.StartsWith("Kerbol", StringComparison.Ordinal) ? "stock" : "real";
    }
}

/// Refuses (once) to load a save made in a different universe than the running one.
internal static class LoadGuard
{
    private static string? _confirmId;
    private static long _confirmUntil;

    public static void Patch(Harmony h) =>
        h.Patch(AccessTools.Method(typeof(UncompressedSave), nameof(UncompressedSave.Load)),
            prefix: new HarmonyMethod(typeof(LoadGuard), nameof(Prefix)));

    private static bool Prefix(UncompressedSave __instance)
    {
        try
        {
            string? running = Universe.CurrentSystem?.Id;
            string[] saved = __instance.MetaData?.Systems ?? Array.Empty<string>();
            if (running == null || saved.Length == 0 || saved.Any(w => Worlds.CanLoadInto(w, running)))
                return true;
            if (_confirmId == __instance.Id && Environment.TickCount64 < _confirmUntil)
            {
                _confirmId = null;
                DefaultCategory.Log.Warning($"[KSAMainMenu] loading '{__instance.Id}' ({string.Join(",", saved)}) " +
                                            $"into '{running}' as confirmed; vehicles around missing bodies will be dropped");
                return true;
            }
            _confirmId = __instance.Id;
            _confirmUntil = Environment.TickCount64 + 15000;
            TimedAlert.Create($"'{__instance.Id}' was made in {SaveFolder.SystemName(saved[0])}, but this is " +
                              $"{SaveFolder.SystemName(running)}. Load it from the main menu (pause menu > MAIN MENU > " +
                              "Load Game), which restarts into the right system. Load again within 15 s to load anyway " +
                              "(vehicles around bodies missing here are lost).",
                Color.Yellow, 12.0);
            return false;
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] guard failed, loading normally: {ex.Message}");
            return true;
        }
    }
}
