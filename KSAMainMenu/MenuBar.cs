// A "Game" tab in KSA's top menu bar (user, via the Kerbol coordinator, 2026-10-27: in-game menu access as a top-bar
// tab, the way KSAScience does it): back to the main menu, save/load, settings, quit. Injected right after the game's
// own menus by a transpiler on Program.DrawMenuBar - DrawProgramMenusHook is empty and may be inlined, so it can't be
// postfixed; KSAFar, KSAScience and KSAKonstructs chain on the same call. The pause menu's MAIN MENU button stays too.

using System.Reflection;
using System.Reflection.Emit;
using Brutal.ImGuiApi;
using HarmonyLib;
using KSA;

namespace KSAMainMenu;

internal static class MenuBar
{
    public static void Patch(Harmony h) =>
        h.Patch(AccessTools.Method(typeof(Program), nameof(Program.DrawMenuBar)),
            transpiler: new HarmonyMethod(typeof(MenuBar), nameof(Transpile)));

    private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo hook = AccessTools.Method(typeof(Program), nameof(Program.DrawProgramMenusHook));
        MethodInfo ours = AccessTools.Method(typeof(MenuBar), nameof(Draw));
        int hooked = 0;
        foreach (CodeInstruction ci in instructions)
        {
            yield return ci;
            if (ci.Calls(hook))
            {
                yield return new CodeInstruction(OpCodes.Call, ours);
                hooked++;
            }
        }
        if (hooked == 0)
            throw new InvalidOperationException("Program.DrawMenuBar no longer calls DrawProgramMenusHook.");
    }

    private static void Draw()
    {
        if (Menu.IsOpen || !ImGui.BeginMenu("Game"))
            return;
        bool editor = Program.Editor != null;
        if (ImGui.MenuItem("Main Menu", "", false, !editor))
            Menu.Open(fromGame: true);
        if (editor)
            ImGui.TextDisabled("  (leave the editor first)");
        if (ImGui.MenuItem("Save / Load...", "", false, true))
        {
            if (editor)
                VehicleSaves.Toggle();
            else
                GameSaves.Toggle();
        }
        if (ImGui.MenuItem("Settings...", "", false, true))
            GameSettings.Show = true;
        ImGui.Separator();
        if (ImGui.MenuItem("Quit KSA", "", false, true))
            ExitPopup.Create();
        ImGui.EndMenu();
    }
}
