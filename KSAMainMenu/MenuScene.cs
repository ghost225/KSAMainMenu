// The 3D scene behind the menu: the home world seen from orbit and three kittens floating in front of the camera.
//
// - Camera: a prefix on Camera.OnFrame (after the camera controller ran, before the view matrices are built) parks the
//   main camera over the home body's day side and pitches it up so the planet fills the lower part of the screen, like
//   KSP's title screen. Sim time is frozen while the menu is open (so a new game starts at t=0); a slow drift around the
//   planet stands in for its rotation.
// - Kittens: KittenRenderable is self-contained (no Vehicle, nothing in the universe, nothing saved). KSA draws EVA
//   kittens right after SuperMeshRenderSystem.ClearBuckets for the viewport, so ours go in a postfix there. Their
//   animation time is scaled by the sim speed, which is 0 while paused, so the speed fields read 1 just for our call.
// - HUD: while the menu is open KSA runs in its own hidden-UI state for whole frames (see the HUD section); the menu,
//   KSA's settings window and its popups are drawn from a DrawFps postfix, which runs either way.

using Brutal.Logging;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace KSAMainMenu;

internal static class MenuScene
{
    public static bool Active;

    private static readonly AccessTools.FieldRef<double> SimSpeedField = AccessTools.StaticFieldRefAccess<double>(AccessTools.Field(typeof(Universe), "_simulationSpeed"));
    private static readonly AccessTools.FieldRef<double> AchievedField = AccessTools.StaticFieldRefAccess<double>(AccessTools.Field(typeof(Universe), "_achievedSpeedFraction"));

    private sealed class Kitten
    {
        public required KittenRenderable Renderable;
        public required KittenPlacement Place;
        public double Phase;
        public double3 TumbleAxisCam;   // in camera axes (right, up, back)
    }

    private static readonly List<Kitten> _kittens = new();
    private static double _clock;
    private static double3 _camFwd, _camUp, _camRight;
    private static bool _cameraValid;
    private static bool _kittenFailed;

    public static void Patch(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(Camera), nameof(Camera.OnFrame)),
            prefix: new HarmonyMethod(typeof(MenuScene), nameof(BeforeCameraFrame)));
        h.Patch(AccessTools.Method(typeof(SuperMeshRenderSystem), nameof(SuperMeshRenderSystem.ClearBuckets)),
            postfix: new HarmonyMethod(typeof(MenuScene), nameof(AfterClearBuckets)));
        // The menu draws from DrawFps, which KSA calls every frame even with its UI hidden (see the HUD notes below).
        h.Patch(AccessTools.Method(typeof(Program), "DrawFps"),
            postfix: new HarmonyMethod(typeof(MenuScene), nameof(AfterDrawFps)));
    }

    public static void Enter()
    {
        Active = true;
        HideUi();
        _clock = 0;
        _cameraValid = false;
        try
        {
            if (HomeBody() is Celestial home)
                Program.GetCamera().SetFollow(home, tidalLocking: false, changeControl: false, alert: false);
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] couldn't follow the home body: {ex.Message}");
        }
        if (MenuConfig.Settings.Kittens && _kittens.Count == 0 && !_kittenFailed)
            BuildKittens();
    }

    public static void Leave()
    {
        Active = false;
        RestoreUi();
        _cameraValid = false;
    }

    /// Per-frame bookkeeping from the UI pass.
    public static void Tick(double dt)
    {
        if (!Active)
            return;
        double before = _clock;
        _clock += dt;
        Universe.SetSimulationSpeed(0.0, alert: false);
        EscMenu.Open = false;
        if (Program.DrawUI)       // the toggle-UI key while on the menu
            HideUi();
        if (before < 3.0 && _clock >= 3.0)
            LogView();
    }

    // Once per menu visit: where the planet and the kittens land on screen (NDC, x right / y up, -1..1 is on screen).
    private static void LogView()
    {
        try
        {
            Camera cam = Program.GetCamera();
            var parts = new List<string>
            {
                $"fov {cam.GetFieldOfView() * 180f / MathF.PI:F1} deg, {cam.FramebufferSize.X}x{cam.FramebufferSize.Y}, " +
                $"following {cam.Following?.Id ?? "nothing"}, kittens {_kittens.Count}{(_kittenFailed ? " (failed)" : "")}",
            };
            if (HomeBody() is Celestial home)
            {
                double3 ego = home.GetPositionEcl() - cam.PositionEcl;
                parts.Add($"{home.Id} centre at ndc {Fmt(cam.EgoToNdc(ego))}, {ego.Length() / 1000:F0} km away");
            }
            foreach (Kitten k in _kittens)
            {
                double3 pos = _camRight * k.Place.Right + _camUp * k.Place.Up + _camFwd * k.Place.Forward;
                parts.Add($"{k.Place.Character} at ndc {Fmt(cam.EgoToNdc(pos))}");
            }
            DefaultCategory.Log.Info("[KSAMainMenu] view: " + string.Join("; ", parts));
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] view log: {ex.Message}");
        }
    }

    private static string Fmt(float3 v) => $"({v.X:F2}, {v.Y:F2}, z {v.Z:F3})";

    private static Celestial? HomeBody() => Universe.CurrentSystem?.HomeBody as Celestial;

    private static void BuildKittens()
    {
        try
        {
            var rng = new Random(7);
            foreach (KittenPlacement p in MenuConfig.Settings.KittenPlacements)
            {
                double3 axis = double3.Normalize(new double3(rng.NextDouble() - 0.5, 1.0, rng.NextDouble() - 0.5));
                _kittens.Add(new Kitten
                {
                    Renderable = new KittenRenderable(p.Character),
                    Place = p,
                    Phase = rng.NextDouble() * Math.Tau,
                    TumbleAxisCam = axis,
                });
            }
        }
        catch (Exception ex)
        {
            _kittenFailed = true;
            _kittens.Clear();
            DefaultCategory.Log.Warning($"[KSAMainMenu] no kittens: {ex}");
        }
    }

    // --- camera ----------------------------------------------------------------------------------------------------

    private static void BeforeCameraFrame(Camera __instance)
    {
        if (!Active || !ViewportRegistry.IsMainCamera(__instance))
            return;
        try
        {
            CelestialSystem? sys = Universe.CurrentSystem;
            Celestial? home = HomeBody();
            StellarBody? sun = sys?.GetWorldSun();
            if (home == null || sun == null)
                return;
            MenuSettings s = MenuConfig.Settings;
            double3 c = home.GetPositionEcl();
            double3 toSun = double3.Normalize(sun.GetPositionEcl() - c);
            double3 north = double3.UnitZ;
            if (double3.Cross(north, toSun).Length() < 0.1)
                north = double3.UnitY;
            double3 east = double3.Normalize(double3.Cross(north, toSun));
            double a = (s.SunAngleDeg + s.DriftDegPerMin * _clock / 60.0) * Math.PI / 180.0;
            double3 dir = double3.Normalize(toSun * Math.Cos(a) + east * Math.Sin(a) + north * 0.25);
            double3 p = c + dir * home.MeanRadius * (1.0 + s.AltitudeRadii);

            double3 toC = double3.Normalize(c - p);
            double3 right = double3.Normalize(double3.Cross(toC, north));
            double3 up0 = double3.Cross(right, toC);
            double b = s.PlanetBelowDeg * Math.PI / 180.0;
            double3 fwd = double3.Normalize(toC * Math.Cos(b) + up0 * Math.Sin(b));
            double3 up = double3.Normalize(double3.Cross(right, fwd));

            __instance.PositionEcl = p;
            __instance.LocalRotation = Camera.LookAtRotation(fwd, up);
            _camFwd = fwd;
            _camUp = up;
            _camRight = right;
            _cameraValid = true;
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] menu camera: {ex.Message}");
            _cameraValid = false;
        }
    }

    // --- kittens ---------------------------------------------------------------------------------------------------

    private static void AfterClearBuckets(IViewport viewport)
    {
        if (!Active || !_cameraValid || _kittens.Count == 0 || Program.Editor != null || !viewport.IsMain())
            return;
        ref double speed = ref SimSpeedField();
        ref double achieved = ref AchievedField();
        double oldSpeed = speed, oldAchieved = achieved;
        speed = 1.0;
        achieved = 1.0;
        try
        {
            double dt = Program.GetPlayerDeltaTime();
            foreach (Kitten k in _kittens)
                k.Renderable.UpdateRenderData(viewport, 0, dt, KittenMatrix(k), double3.Zero, double3.Zero,
                    default(LocomotionState), default(KittenAnimInputs));
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] kittens stopped: {ex}");
            _kittens.Clear();
            _kittenFailed = true;
        }
        finally
        {
            speed = oldSpeed;
            achieved = oldAchieved;
        }
    }

    private static float4x4 KittenMatrix(Kitten k)
    {
        KittenPlacement p = k.Place;
        double t = _clock;
        double3 pos = _camRight * p.Right + _camUp * p.Up + _camFwd * p.Forward;
        pos += _camUp * (p.Bob * Math.Sin(t * 0.7 + k.Phase)) + _camRight * (p.Bob * 0.6 * Math.Sin(t * 0.43 + k.Phase * 1.7));

        // Kitten body axes (KittenEva): +X is where it faces, +Z points down, +Y to its right. Start facing the camera,
        // head up; then the placement's yaw/pitch/roll; then a gentle wobble (so it never turns its back) and a slow
        // roll in the screen plane, which keeps the face in view while it turns head over heels.
        double3 x = -_camFwd, z = -_camUp;
        double3 y = double3.Cross(z, x);
        double wobbleYaw = 22.0 * Math.Sin(t * 0.31 + k.Phase) * k.TumbleAxisCam.Y;
        double wobblePitch = 14.0 * Math.Sin(t * 0.23 + k.Phase * 2.3) + 8.0 * k.TumbleAxisCam.X;
        Rotate(ref x, ref y, ref z, _camUp, p.Yaw + wobbleYaw);
        Rotate(ref x, ref y, ref z, _camRight, p.Pitch + wobblePitch);
        Rotate(ref x, ref y, ref z, _camFwd, p.Roll + p.SpinDegPerSec * t + k.Phase * 30.0);

        doubleQuat body2Ecl = doubleQuat.CreateFromRotationMatrix(new double4x4(
            x.X, x.Y, x.Z, 0.0,
            y.X, y.Y, y.Z, 0.0,
            z.X, z.Y, z.Z, 0.0,
            0.0, 0.0, 0.0, 1.0));
        return float4x4.CreateFromQuaternion(floatQuat.Pack(body2Ecl)) * float4x4.CreateTranslation(float3.Pack(in pos));
    }

    private static void Rotate(ref double3 x, ref double3 y, ref double3 z, double3 axis, double degrees)
    {
        if (degrees == 0)
            return;
        doubleQuat q = doubleQuat.CreateFromAxisAngle(axis, degrees * Math.PI / 180.0);
        x = x.Transform(q);
        y = y.Transform(q);
        z = z.Transform(q);
    }

    // --- HUD -------------------------------------------------------------------------------------------------------
    //
    // While the menu is open KSA runs in its own hidden-UI state (Program.DrawUI = false, what its toggle-UI key does)
    // for whole frames: no menu bar, gauges, labels, windows or orbit lines, and the same for other mods' StarMap GUI
    // hooks. It has to be whole frames: orbit lines are queued in OnPreRender and only cleared when drawn in
    // RenderGame, both gated by DrawUI, so hiding them for the render alone let the queue grow until it overflowed
    // (crash 2026-10-26 after ~90 s on the menu). KSA's settings window and popups are drawn by us instead.

    private static bool _restoreUi;

    internal static void HideUi()
    {
        if (Program.DrawUI)
        {
            Program.DrawUI = false;
            _restoreUi = true;
        }
    }

    internal static void RestoreUi()
    {
        if (_restoreUi && !Program.DrawUI)
            Program.DrawUI = true;
        _restoreUi = false;
    }

    private static void AfterDrawFps()
    {
        if (!Program.IsLoaded)
            return;
        Menu.Frame(Program.GetPlayerDeltaTime());
        if (Active && !Program.DrawUI)
        {
            Popup.DrawAll();
            GameSettings.OnDrawUi(Program.GetCamera());
        }
    }
}
