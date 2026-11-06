// KSAMainMenu: a kitten main menu for KSA (ksa-kerbol/PLAN.md WS6; ksa-menu/PLAN.md).
//
// User, 2026-10-26: "do our own riff on KSP/KSP2's menu, and give KSA its own kitten based menu looking down on the
// 'homeworld' with 3 kittens floating in space, and a proper settings menu, save load menu etc".
//
// KSA has no menu state: it loads one universe per launch (the system is chosen before any content loads) and then
// the sim runs. So the menu opens over the freshly loaded universe, paused, with the camera parked over the home world
// and three render-only kittens in front of it. Switching system means a restart, which the menu does itself (KSA's
// own restart can't relaunch under StarMap). Absorbs KSASaveLauncher (start-up save list + wrong-universe guard).

using Brutal.Logging;
using HarmonyLib;
using StarMap.API;

namespace KSAMainMenu;

[StarMapMod]
public sealed class Mod
{
    internal const string HarmonyId = "ksamainmenu";
    private Harmony? _harmony;

    internal static string Dir = ".";

    [StarMapBeforeMain]
    public void BeforeMain()
    {
        Dir = Path.GetDirectoryName(typeof(Mod).Assembly.Location) ?? ".";
        Restarter.StartDirectory = Directory.GetCurrentDirectory();
        MenuConfig.Load();
        _harmony = new Harmony(HarmonyId);
        TryPatch(() => Startup.Patch(_harmony), "start-up system choice");
        TryPatch(() => Restarter.Patch(_harmony), "restart under StarMap");
        TryPatch(() => LoadGuard.Patch(_harmony), "universe guard on load");
        TryPatch(() => MenuScene.Patch(_harmony), "menu scene");
        TryPatch(() => EscMenuButton.Patch(_harmony), "pause menu button");
        TryPatch(() => MenuBar.Patch(_harmony), "top-bar Game menu");
        TryPatch(() => Logo.Patch(_harmony), "logo fonts");
        TryPatch(() => LoadingScreen.Patch(_harmony), "loading screen");
    }

    [StarMapUnload]
    public void Unload()
    {
        _harmony?.UnpatchAll(HarmonyId);
        _harmony = null;
    }

    private static void TryPatch(Action patch, string what)
    {
        try
        {
            patch();
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] {what} not installed: {ex}");
        }
    }
}
