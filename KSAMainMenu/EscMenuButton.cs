// MAIN MENU in KSA's pause menu: back to the kitten menu over the running game.
//
// EscMenu.Draw sizes its modal for a fixed button count (7 in flight, 5 in the editor) and draws SETTINGS, a rule and
// QUIT last. The transpiler counts one more button and draws ours just before the rule. In the editor the button is
// off (the menu scene needs the flight view), so it only shows in flight.

using System.Reflection;
using System.Reflection.Emit;
using Brutal.ImGuiApi;
using Brutal.Logging;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace KSAMainMenu;

internal static class EscMenuButton
{
    public static void Patch(Harmony h) =>
        h.Patch(AccessTools.Method(typeof(EscMenu), nameof(EscMenu.Draw)),
            transpiler: new HarmonyMethod(typeof(EscMenuButton), nameof(Transpile)));

    private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> code = instructions.ToList();
        int seven = code.FindIndex(c => c.opcode == OpCodes.Ldc_I4_7);
        int rule = code.FindIndex(c => c.operand is MethodInfo m && m.DeclaringType == typeof(ConsoleWidgets) && m.Name == "Rule");
        if (seven < 0 || rule < 0)
        {
            DefaultCategory.Log.Warning("[KSAMainMenu] pause menu layout changed; no MAIN MENU button");
            return code;
        }
        code[seven].opcode = OpCodes.Ldc_I4_8;
        var call = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(EscMenuButton), nameof(Draw)));
        call.labels.AddRange(code[rule].labels);   // keep jumps that land on the rule landing on our button first
        code[rule].labels.Clear();
        code.Insert(rule, call);
        return code;
    }

    private static void Draw()
    {
        if (Program.Editor != null)
            return;
        float2 size = new float2(ImGui.GetContentRegionAvail().X, 32f * GameSettings.GetInterfaceScale());
        if (ConsoleWidgets.Button("MAIN MENU", size))
            Menu.Open(fromGame: true);
    }
}
