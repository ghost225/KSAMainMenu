// The loading screen (user, 2026-10-26: "let's improve the loading screen as well ... have a proper loading icon, maybe
// a rocket icon orbiting a planet, with the loading info at the bottom of the page").
//
// KSA's own loading screen is centred grey text on black (Loading.DrawUi). This replaces it while KSA loads:
// - a slideshow of screenshots, crossfading with a slow zoom. Pictures come from the mod's LoadingImages/ folder (drop
//   any png/jpg in) and from the installed KSA_Splash_Screen mod's Assets (credits as that mod gives them);
// - the logo top-left;
// - a bottom bar: the kitten planet with a rocket going round it, KSA's loading state, the tasks under way, VRAM, and
//   the picture's credit.
// Pictures go to the GPU (ImageTexture) one per frame, but only before ModLibrary.LoadAll: on this GPU KSA's texture
// streaming shares the graphics queue, and uploading while KSA binds every mod's textures lost the device on the first
// Kerbol load (2026-10-27). So up to MaxSlides pictures are uploaded early (during the world picker or the first
// loading steps), the slideshow cycles through those, and nothing is uploaded or freed again until the game runs.

using System.Diagnostics;
using Brutal.ImGuiApi;
using Brutal.Logging;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace KSAMainMenu;

internal static class LoadingScreen
{
    private sealed record Slide(string Path, string Credit);

    private static readonly Dictionary<string, string> SplashCredits = new()
    {
        ["image1.png"] = "ecruteakcity", ["image2.png"] = "Guitarkittens", ["image3.png"] = "unknown",
        ["image4.png"] = "Daishi", ["image5.png"] = "Daishi", ["image6.png"] = "Daishi",
        ["image7.png"] = "RocketWerkz", ["image8.png"] = "RocketWerkz", ["image9.png"] = "RocketWerkz",
        ["image10.png"] = "RocketWerkz", ["image11.png"] = "RocketWerkz",
    };

    private const double SlideSeconds = 7.0, FadeSeconds = 1.2;

    private static readonly AccessTools.FieldRef<Loading, List<LoadTask>> Tasks = AccessTools.FieldRefAccess<Loading, List<LoadTask>>("_tasks");
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static List<Slide>? _slides;
    private static readonly Dictionary<int, ImageTexture> _gpu = new();
    private static readonly List<(ImageTexture Tex, int Frames)> _retire = new();
    private static readonly HashSet<int> _failed = new();
    private static bool _broken;
    public static bool Broken => _broken;
    private static bool _released;

    private const int MaxSlides = 5;
    private static bool _frozen;          // KSA is loading mod content: no GPU work from us until the game runs
    private static int _nextToTry;
    private static readonly List<int> _ready = new();   // slides on the GPU, in show order

    public static void Patch(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(Loading), nameof(Loading.DrawUi)),
            prefix: new HarmonyMethod(typeof(LoadingScreen), nameof(BeforeDrawUi)));
        h.Patch(AccessTools.Method(typeof(ModLibrary), nameof(ModLibrary.LoadAll)),
            prefix: new HarmonyMethod(typeof(LoadingScreen), nameof(Freeze)));
    }

    private static void Freeze() => _frozen = true;

    private static List<Slide> FindSlides()
    {
        var list = new List<Slide>();
        string own = Path.Combine(Mod.Dir, "LoadingImages");
        if (Directory.Exists(own))
            foreach (string f in Directory.GetFiles(own).Where(IsImage).OrderBy(f => f))
                list.Add(new Slide(f, CreditFor(f)));
        string gameDir = Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? ".";
        string splash = Path.Combine(gameDir, "Content", "KSA_Splash_Screen-v1.0.0", "Assets");
        if (Directory.Exists(splash))
            foreach (string f in Directory.GetFiles(splash).Where(IsImage).OrderBy(f => f))
                list.Add(new Slide(f, SplashCredits.GetValueOrDefault(Path.GetFileName(f), "")));
        // a different order every launch
        var rng = new Random();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        DefaultCategory.Log.Info($"[KSAMainMenu] loading screen: {list.Count} pictures");
        return list;
    }

    private static bool IsImage(string f) => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                             f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                             f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);

    /// "credit.txt" next to the pictures: lines of "file = credit"; else no credit.
    private static string CreditFor(string file)
    {
        try
        {
            string txt = Path.Combine(Path.GetDirectoryName(file)!, "credits.txt");
            if (File.Exists(txt))
                foreach (string line in File.ReadAllLines(txt))
                {
                    string[] kv = line.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (kv.Length == 2 && kv[0].Equals(Path.GetFileName(file), StringComparison.OrdinalIgnoreCase))
                        return kv[1];
                }
        }
        catch
        {
            // no credit
        }
        return "";
    }

    private static bool BeforeDrawUi(Loading __instance)
    {
        if (_broken)
            return true;
        try
        {
            Draw(__instance);
            return false;
        }
        catch (Exception ex)
        {
            _broken = true;
            DefaultCategory.Log.Warning($"[KSAMainMenu] loading screen off, KSA's own is back: {ex}");
            return true;
        }
    }

    private static void Draw(Loading loading)
    {
        _slides ??= FindSlides();
        double t = Clock.Elapsed.TotalSeconds;
        // upload a few pictures while it's safe (see the header), one per frame
        if (!_frozen && _ready.Count < MaxSlides && _nextToTry < _slides.Count)
        {
            int i = _nextToTry++;
            Upload(i);
            if (_gpu.ContainsKey(i))
                _ready.Add(i);
        }
        int count = _ready.Count;
        int slot = count == 0 ? -1 : (int)(t / SlideSeconds) % count;
        int index = slot < 0 ? -1 : _ready[slot];
        double inSlide = t % SlideSeconds;
        int previous = count == 0 ? -1 : _ready[(slot - 1 + count) % count];
        bool fading = count > 1 && t >= SlideSeconds && inSlide < FadeSeconds;

        ImGuiViewportPtr vp = ImGui.GetMainViewport();
        float2 pos = vp.Pos, size = vp.Size;
        float s = GameSettings.GetInterfaceScale();
        ImDrawListPtr bg = ImGui.GetBackgroundDrawList(vp);
        bg.AddRectFilled(pos, pos + size, Col(0.02f, 0.02f, 0.04f));
        if (index >= 0)
        {
            if (fading)
            {
                DrawSlide(bg, previous, pos, size, 1f, SlideSeconds + inSlide);
                DrawSlide(bg, index, pos, size, (float)(inSlide / FadeSeconds), inSlide);
            }
            else
                DrawSlide(bg, index, pos, size, 1f, inSlide);
        }

        if (WorldPicker.Active)
        {
            WorldPicker.Draw(pos, size, s, t);
            return;
        }

        ImGui.SetNextWindowPos(pos, ImGuiCond.Always, (float2?)null);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.SetNextWindowViewport(vp.ID);
        ImGui.Begin("###KSAMainMenuLoading", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoMove |
                                             ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings);
        ImDrawListPtr dl = ImGui.GetWindowDrawList();

        // top shade for the logo, bottom bar for the info
        float barH = 128f * s;
        for (int i = 0; i < 24; i++)
        {
            float a = 0.55f * (1f - i / 24f);
            dl.AddRectFilled(pos + new float2(0f, i * 10f * s), pos + new float2(size.X, (i + 1) * 10f * s), Col(0f, 0f, 0f, a * 0.6f));
            float yb = size.Y - barH - (i + 1) * 8f * s;
            dl.AddRectFilled(pos + new float2(0f, yb), pos + new float2(size.X, yb + 8f * s), Col(0f, 0f, 0f, 0.82f * (1f - i / 24f)));
        }
        dl.AddRectFilled(pos + new float2(0f, size.Y - barH), pos + size, Col(0f, 0f, 0f, 0.82f));
        dl.AddLine(pos + new float2(0f, size.Y - barH), pos + new float2(size.X, size.Y - barH), Col(0.98f, 0.62f, 0.22f, 0.6f), 2f);

        Logo.Draw(dl, pos + new float2(40f * s, 34f * s), 520f * s);

        // the loading icon (the kitten planet with a rocket going round) with the loading text right beside it, the
        // two centred on the bar together
        float iconR = 30f * s;
        float2 icon = new float2(pos.X + 40f * s + iconR * 1.8f, pos.Y + size.Y - barH * 0.5f);
        Logo.DrawEmblem(dl, icon, iconR, t * 2.4, rocket: true);

        string tasks;
        List<LoadTask> list = Tasks(loading);
        lock (list)
            tasks = string.Join("   ·   ", list.Select(k => k.Name));
        string vram = Program.GetRenderer().GetDeviceLocalMemoryUsage(out var used, out var budget) ? $"VRAM ~{used} / ~{budget}" : "";
        float stateH = 34f * s, lineH = 22f * s;
        float blockH = stateH + (tasks.Length > 0 ? lineH : 0f) + (vram.Length > 0 ? lineH : 0f);
        float x = icon.X + iconR * 1.75f + 22f * s;     // just clear of the orbit ring
        float y = icon.Y - blockH / 2f - 2f * s;
        Logo.PushTitleFont(stateH);
        dl.AddText(new float2(x, y), Col(1f, 0.98f, 0.93f), loading.StateText.ToUpperInvariant() + Dots(t));
        ImGui.PopFont();
        y += stateH;
        ImGui.PushFont(ImGui.GetFont(), 19f * s);
        if (tasks.Length > 0)
        {
            dl.AddText(new float2(x, y), Col(0.62f, 0.84f, 1f, 0.9f), tasks);
            y += lineH;
        }
        if (vram.Length > 0)
            dl.AddText(new float2(x, y), Col(1f, 1f, 1f, 0.45f), vram);
        if (index >= 0 && _slides[index].Credit.Length > 0)
        {
            string credit = "Picture: " + _slides[index].Credit;
            float w = ImGui.CalcTextSize(credit).X;
            dl.AddText(pos + new float2(size.X - w - 32f * s, size.Y - 40f * s), Col(1f, 1f, 1f, 0.55f), credit);
        }
        ImGui.PopFont();
        ImGui.End();
    }

    private static string Dots(double t) => new string('.', 1 + (int)(t * 2.0) % 3);

    // cover the screen (crop to its aspect) with a slow zoom over the slide
    private static void DrawSlide(ImDrawListPtr dl, int i, float2 pos, float2 size, float alpha, double age)
    {
        if (!_gpu.TryGetValue(i, out ImageTexture? g) || alpha <= 0f)
            return;
        float screen = size.X / size.Y;
        float2 uv = g.Aspect > screen ? new float2(screen / g.Aspect, 1f) : new float2(1f, g.Aspect / screen);
        float zoom = 1f + 0.06f * (float)Math.Clamp(age / (SlideSeconds + FadeSeconds), 0.0, 1.0);
        uv /= zoom;
        float2 drift = new float2(0.012f, -0.008f) * (float)(age / SlideSeconds) * ((i & 1) == 0 ? 1f : -1f);
        float2 c = new float2(0.5f) + drift;
        float2 uv0 = c - uv * 0.5f, uv1 = c + uv * 0.5f;
        dl.AddImage(g.Handle, pos, pos + size, uv0, uv1, Col(1f, 1f, 1f, alpha));
    }

    private static void Upload(int i)
    {
        if (ImageTexture.Load(_slides![i].Path) is { } tex)
            _gpu[i] = tex;
        else
            _failed.Add(i);
    }

    // A texture may still be in use by frames in flight: forget it now, free it a few game frames later (TickAfterLoad).
    private static void Retire(int i)
    {
        if (_gpu.Remove(i, out ImageTexture? g))
            _retire.Add((g, 6));
    }

    /// Once the game runs (called every frame from the menu; acts once, a few frames in).
    public static void ReleaseAll()
    {
        if (_released)
            return;
        _released = true;
        foreach (int k in _gpu.Keys.ToList())
            Retire(k);
        // the loading screen no longer ticks, so free them on the game's frames
        _pendingFree = 8;
    }

    private static int _pendingFree = -1;

    public static void TickAfterLoad()
    {
        if (_pendingFree < 0)
            return;
        if (--_pendingFree > 0)
            return;
        _pendingFree = -1;
        foreach ((ImageTexture tex, _) in _retire)
            tex.Free();
        _retire.Clear();
    }

    private static uint Col(float r, float g, float b, float a = 1f) => ImGui.ColorConvertFloat4ToU32(new float4(r, g, b, a));
}
