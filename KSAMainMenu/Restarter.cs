// Restarting KSA from inside the game.
//
// KSA's own restart (RequiresRestartPopup) runs Process.Start(MainModule.FileName) with no arguments. Under Borea that
// main module is `dotnet` and the game is `dotnet StarMap.dll -InstancePath <instance>`, so KSA's restart starts a bare
// dotnet and the game never comes back. We re-run our own command line instead (/proc/self/cmdline, same working
// directory and environment), from a detached shell that waits for this process to exit first, so two copies never
// hold the GPU at once. Borea loses track of the relaunched game (running state, playtime).
//
// That relaunch is Linux-only (/proc, sh, setsid). Elsewhere the menu remembers the request, says KSA will close, and
// the next start (from Borea or any launcher) picks it up; KSA's own restart button is left as it is.

using System.Diagnostics;
using Brutal.Logging;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace KSAMainMenu;

internal static class Restarter
{
    /// Can we relaunch KSA ourselves on this machine?
    public static bool CanRelaunch => OperatingSystem.IsLinux() && File.Exists("/proc/self/cmdline") && File.Exists("/bin/sh");

    public static void Patch(Harmony h) =>
        h.Patch(AccessTools.Method(typeof(Process), nameof(Process.Start), new[] { typeof(string) }),
            prefix: new HarmonyMethod(typeof(Restarter), nameof(BeforeProcessStart)));

    // KSA's "Restart" button: replace the bare relaunch with ours (KSA exits right after).
    private static bool BeforeProcessStart(string fileName, ref Process? __result)
    {
        try
        {
            if (!Program.IsLoaded || fileName != Environment.ProcessPath || !CanRelaunch)
                return true;
            Spawn();
            __result = null;
            return false;
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] restart helper failed, KSA's own restart runs: {ex.Message}");
            return true;
        }
    }

    /// Hand the request to the next launch, start it once we're gone, and exit.
    public static void Restart(LaunchRequest request)
    {
        try
        {
            MenuConfig.WriteLaunch(request);
            if (!CanRelaunch)
            {
                string world = SaveFolder.SystemName(request.System);
                ConfirmActionPopup.Create(() =>
                {
                    Menu.IsRestarting = true;
                    Program.Instance.RequestExit(0);
                }, "RESTART NEEDED",
                    $"KSA has to restart for this. Close it now, then start it again (from Borea or your usual launcher): it opens {world} " +
                    "and carries on straight away. If you stay, that happens on your next start instead.");
                return;
            }
            Spawn();
            Menu.IsRestarting = true;
            DefaultCategory.Log.Info($"[KSAMainMenu] restarting into '{request.System}' for a {request.Action}");
            Program.Instance.RequestExit(0);
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Error($"[KSAMainMenu] restart failed: {ex}");
            TimedAlert.Create($"Couldn't restart KSA: {ex.Message}. Close it and start it again from Borea.", Color.Red, 10.0);
        }
    }

    private static void Spawn()
    {
        string[] args = CommandLine();
        string dir = LaunchDirectory(args);
        string log = Path.Combine(Mod.Dir, "relaunch.log");
        var psi = new ProcessStartInfo(File.Exists("/usr/bin/setsid") ? "/usr/bin/setsid" : "/bin/sh")
        {
            UseShellExecute = false,
            WorkingDirectory = dir,
        };
        if (psi.FileName != "/bin/sh")
            psi.ArgumentList.Add("/bin/sh");
        psi.ArgumentList.Add("-c");
        // waits for KSA to exit, then starts it again; relaunch.log shows each step (and anything the launcher prints)
        psi.ArgumentList.Add("pid=$1; dir=$2; log=$3; shift 3; " +
                             "echo \"$(date '+%F %T') waiting for KSA (pid $pid) to exit\" > \"$log\"; " +
                             "while kill -0 \"$pid\" 2>/dev/null; do sleep 0.5; done; " +
                             "echo \"$(date '+%F %T') starting in $dir: $*\" >> \"$log\"; " +
                             "cd \"$dir\" && exec \"$@\" </dev/null >> \"$log\" 2>&1");
        psi.ArgumentList.Add("ksa-restart");
        psi.ArgumentList.Add(Environment.ProcessId.ToString());
        psi.ArgumentList.Add(dir);
        psi.ArgumentList.Add(log);
        foreach (string a in args)
            psi.ArgumentList.Add(a);
        Process.Start(psi);
        DefaultCategory.Log.Info($"[KSAMainMenu] relaunch queued in {dir}: {string.Join(' ', args)} (log: {log})");
    }

    // Where the launcher has to start. KSA moves the working directory to the game folder while it runs, but StarMap
    // reads StarMapConfig.json from its own folder (Borea starts it there): a relaunch from the game folder exited at
    // once without a word (2026-10-26). So: the folder of StarMap.dll when that's what was launched, else the
    // directory this process started in.
    private static string LaunchDirectory(string[] args)
    {
        foreach (string a in args.Skip(1))
            if (a.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && Path.IsPathRooted(a) && File.Exists(a))
                return Path.GetDirectoryName(a)!;
        return StartDirectory ?? Directory.GetCurrentDirectory();
    }

    /// The working directory when the mod loaded (before KSA's own start-up moves it).
    internal static string? StartDirectory;

    private static string[] CommandLine()
    {
        try
        {
            string[] parts = File.ReadAllText("/proc/self/cmdline").Split('\0');
            int n = parts.Length;
            while (n > 0 && parts[n - 1].Length == 0)
                n--;
            if (n > 0)
                return parts[..n];
        }
        catch
        {
            // not Linux: fall through
        }
        return new[] { Environment.ProcessPath ?? "dotnet" }.Concat(Environment.GetCommandLineArgs().Skip(1)).ToArray();
    }
}
