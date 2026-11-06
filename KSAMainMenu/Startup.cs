// Start-up: choose the system ourselves instead of KSA's Configuration popup.
//
// Program's constructor asks GameSettings.ShowSystemSelection() once, after the mods are prepared and before
// ModLibrary.LoadAll (Program.cs ~943). When it says no, KSA uses settings' LastSystemId (SystemLibrary.Default), which
// is also what KSAWorld reads - so setting LastSystemId here keeps every mod's answer consistent. A launch request from
// a restart picks the system (and the new universe's GameType); otherwise the world picker asks (WorldPicker), or it's
// the last system played. KSA's popup is still there with Shift pressed while KSA loads, from the picker, or the
// "classic popup" setting.

using Brutal.GlfwApi;
using Brutal.Logging;
using HarmonyLib;
using KSA;

namespace KSAMainMenu;

internal static class Startup
{
    private static bool _shiftSeen;
    private static bool _decided;

    /// The system the menu loaded this launch, once known.
    public static string? SystemId;

    public static void Patch(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(GameSettings), nameof(GameSettings.ShowSystemSelection)),
            prefix: new HarmonyMethod(typeof(Startup), nameof(BeforeShowSystemSelection)));
        h.Patch(AccessTools.Method(typeof(Program), nameof(Program.OnKey)),
            prefix: new HarmonyMethod(typeof(Startup), nameof(BeforeKey)));
    }

    private static void BeforeKey(GlfwKey key, GlfwKeyAction action)
    {
        if (!Program.IsLoaded && action == GlfwKeyAction.Press && key.ToString().Contains("Shift"))
            _shiftSeen = true;
    }

    private static bool BeforeShowSystemSelection(ref bool __result)
    {
        if (Program.IsLoaded || _decided)
            return true;
        _decided = true;
        try
        {
            GameSettings.SystemSettings sys = GameSettings.Current.System;
            LaunchRequest? req = MenuConfig.Launch;
            if (req != null && SelectSystem.Systems.Any(s => s.Id == req.System))
            {
                sys.LastSystemId = req.System;
                if (req.TestRocket is bool rocket)
                    sys.StartGameType = rocket ? GameSettings.GameType.Testing : GameSettings.GameType.Sandbox;
                GameSettings.Current.Save();
                DefaultCategory.Log.Info($"[KSAMainMenu] starting '{req.System}' for a {req.Action}");
                __result = false;
                return false;
            }
            if (MenuConfig.Settings.ClassicPopup || _shiftSeen)
            {
                DefaultCategory.Log.Info("[KSAMainMenu] showing KSA's Configuration popup");
                MenuConfig.Launch = null;
                __result = true;
                return false;
            }
            if (!SelectSystem.Systems.Any(s => s.Id == sys.LastSystemId))
                sys.LastSystemId = SelectSystem.Systems.FirstOrDefault()?.Id ?? sys.LastSystemId;
            if (MenuConfig.Settings.AskWorld && Loading.Current != null)
            {
                WorldPicker.Run(sys.LastSystemId);
                if (WorldPicker.WantClassic)
                {
                    DefaultCategory.Log.Info("[KSAMainMenu] showing KSA's Configuration popup (asked for in the world picker)");
                    __result = true;
                    return false;
                }
                sys.LastSystemId = WorldPicker.Chosen;
                GameSettings.Current.Save();
                DefaultCategory.Log.Info($"[KSAMainMenu] world chosen: '{sys.LastSystemId}'");
            }
            else
                DefaultCategory.Log.Info($"[KSAMainMenu] starting the last system, '{sys.LastSystemId}'");
            __result = false;
            return false;
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] start-up choice failed, KSA decides: {ex.Message}");
            return true;
        }
    }
}
