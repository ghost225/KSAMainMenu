// "Which world?" before KSA loads (user, 2026-10-26: "Can we not launch without restarting?!" - picked "world picker
// first"). KSA loads one system's planets, parts and textures per launch and can't unload them, so the world has to be
// chosen before ModLibrary.LoadAll. This screen runs where KSA's own start-up popup would (Startup), inside KSA's
// loading-screen frame loop, over the loading slideshow: a card per world with its last save, the last world chosen
// after a countdown unless the player moves the mouse or presses a key. Then KSA loads that world once and the 3D menu
// opens; New Game and Load inside it don't restart.

using System.Diagnostics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSAMainMenu;

internal static class WorldPicker
{
    public static bool Active;
    public static string Chosen = "";
    public static bool WantClassic;

    private const double CountdownSeconds = 8.0;
    private static readonly Stopwatch Clock = new();
    private static bool _countdown = true;
    private static List<SaveInfo>? _saves;

    private static readonly string[] Primary = { "Sol", "Kerbol", "KerbolInterstellar" };

    /// Runs the picker until a world is chosen (blocking, like KSA's own popup).
    public static void Run(string lastSystem)
    {
        Chosen = lastSystem;
        Active = true;
        Clock.Restart();
        _countdown = true;
        _saves = SaveFolder.All();
        while (Active && Loading.Current != null && !LoadingScreen.Broken)
            Loading.Current.OnFrame();
        Active = false;
    }

    private static uint Col(float r, float g, float b, float a = 1f) => ImGui.ColorConvertFloat4ToU32(new float4(r, g, b, a));

    private static string Blurb(string id) => id switch
    {
        "Sol" => "The real Solar System at real scale",
        "Kerbol" => "KSP's Kerbol system: Kerbin, the Mun and friends",
        "KerbolInterstellar" => "Kerbol, plus Promised Worlds, Kcalbeloh and a distant Sol",
        _ => "",
    };

    /// Drawn by LoadingScreen over its slideshow while Active.
    public static void Draw(float2 pos, float2 size, float s, double time)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        if (_countdown && (io.MouseDelta.X != 0f || io.MouseDelta.Y != 0f || ImGui.IsAnyItemActive() ||
                           ImGui.IsKeyPressed(ImGuiKey.Tab, false) || ImGui.IsKeyPressed(ImGuiKey.LeftArrow, false) ||
                           ImGui.IsKeyPressed(ImGuiKey.RightArrow, false)))
            _countdown = Clock.Elapsed.TotalSeconds < 0.5;   // ignore the first jiggle as the window appears
        double left = CountdownSeconds - Clock.Elapsed.TotalSeconds;
        if (_countdown && left <= 0)
        {
            Active = false;
            return;
        }

        ImGui.SetNextWindowPos(pos, ImGuiCond.Always, (float2?)null);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.Begin("###KSAMainMenuWorlds", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBackground |
                                            ImGuiWindowFlags.NoSavedSettings);
        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + size, Col(0.01f, 0.02f, 0.05f, 0.55f));

        // logo, centred
        float logoW = MathF.Min(620f * s, size.X * 0.6f);
        float logoH = Logo.Draw(dl, pos + new float2((size.X - logoW) / 2f, size.Y * 0.10f), logoW);
        float y = pos.Y + size.Y * 0.10f + logoH + 46f * s;

        Logo.PushTitleFont(40f * s);
        Centred(dl, pos.X + size.X / 2f, y, "CHOOSE YOUR WORLD", Col(1f, 0.98f, 0.93f));
        ImGui.PopFont();
        y += 64f * s;

        // the main worlds as cards
        var worlds = SelectSystem.Systems.Where(w => Primary.Contains(w.Id)).OrderBy(w => Array.IndexOf(Primary, w.Id)).ToList();
        var others = SelectSystem.Systems.Where(w => !Primary.Contains(w.Id)).ToList();
        if (worlds.Count == 0)
            worlds = SelectSystem.Systems.Take(2).ToList();
        float cardW = 380f * s, cardH = 190f * s, gap = 36f * s;
        float total = worlds.Count * cardW + (worlds.Count - 1) * gap;
        float x = pos.X + (size.X - total) / 2f;
        foreach (SystemInfo w in worlds)
        {
            DrawCard(dl, w, new float2(x, y), new float2(cardW, cardH), s);
            x += cardW + gap;
        }
        y += cardH + 28f * s;

        // other worlds (KSA's test and lite systems) as small buttons
        if (others.Count > 0)
        {
            ImGui.PushFont(ImGui.GetFont(), 18f * s);
            string label = "Other worlds:";
            float width = ImGui.CalcTextSize(label).X + others.Sum(o => ImGui.CalcTextSize(Name(o)).X + 30f * s) + 10f * s;
            ImGui.SetCursorScreenPos(new float2(pos.X + (size.X - width) / 2f, y));
            ImGui.TextDisabled(label);
            foreach (SystemInfo o in others)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"{Name(o)}##w_{o.Id}"))
                    Choose(o.Id);
            }
            ImGui.PopFont();
            y += 40f * s;
        }

        // countdown / hint, and KSA's own start-up options
        ImGui.PushFont(ImGui.GetFont(), 20f * s);
        string hint = _countdown
            ? $"Starting {Name(Chosen)} in {Math.Max(1, (int)Math.Ceiling(left))}...   (move the mouse to choose)"
            : "Click a world, or press Enter for the highlighted one";
        Centred(dl, pos.X + size.X / 2f, y, hint, Col(1f, 1f, 1f, 0.7f));
        string adv = "KSA's own start-up options";
        float aw = ImGui.CalcTextSize(adv).X + 16f * s;
        ImGui.SetCursorScreenPos(new float2(pos.X + (size.X - aw) / 2f, y + 36f * s));
        if (ImGui.SmallButton(adv))
        {
            WantClassic = true;
            Active = false;
        }
        ImGui.PopFont();

        if (ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false))
            Active = false;
        if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow, false) || ImGui.IsKeyPressed(ImGuiKey.RightArrow, false))
        {
            int i = worlds.FindIndex(w => w.Id == Chosen);
            int step = ImGui.IsKeyPressed(ImGuiKey.LeftArrow, false) ? -1 : 1;
            if (worlds.Count > 0)
                Chosen = worlds[((i < 0 ? 0 : i) + step + worlds.Count) % worlds.Count].Id;
        }
        ImGui.End();
    }

    private static string Name(SystemInfo w) => w.GetName() is { Length: > 0 } n ? n : w.Id;
    private static string Name(string id) => SaveFolder.SystemName(id);

    private static void Choose(string id)
    {
        Chosen = id;
        Active = false;
    }

    private static void DrawCard(ImDrawListPtr dl, SystemInfo w, float2 at, float2 size, float s)
    {
        ImGui.SetCursorScreenPos(at);
        bool clicked = ImGui.InvisibleButton($"##card_{w.Id}", size);
        bool hovered = ImGui.IsItemHovered();
        if (hovered)
            Chosen = w.Id;
        bool selected = Chosen == w.Id;
        dl.AddRectFilled(at, at + size, Col(0.03f, 0.05f, 0.11f, hovered ? 0.92f : 0.82f), 10f * s);
        dl.AddRect(at, at + size, selected ? Col(0.98f, 0.62f, 0.22f) : Col(1f, 1f, 1f, 0.25f), 10f * s, ImDrawFlags.None,
            selected ? 3f * s : 1.5f * s);

        float2 p = at + new float2(24f * s, 18f * s);
        Logo.PushTitleFont(46f * s);
        dl.AddText(p, selected ? Col(1f, 0.98f, 0.93f) : Col(1f, 1f, 1f, 0.85f), Name(w).ToUpperInvariant());
        ImGui.PopFont();
        ImGui.PushFont(ImGui.GetFont(), 18f * s);
        dl.AddText(p + new float2(2f * s, 56f * s), Col(0.62f, 0.84f, 1f, 0.95f), Blurb(w.Id));
        SaveInfo? last = _saves?.FirstOrDefault(sv => Worlds.CanLoadInto(sv.System, w.Id));
        string saveLine = last != null ? $"Last played: {last.Name} · {last.Updated:yyyy-MM-dd HH:mm}" : "No saves yet: a fresh start";
        dl.AddText(p + new float2(2f * s, 90f * s), Col(1f, 1f, 1f, 0.6f), saveLine);
        if (selected)
            dl.AddText(p + new float2(2f * s, 128f * s), Col(0.98f, 0.62f, 0.22f), ">  LAUNCH");
        ImGui.PopFont();
        if (clicked)
            Choose(w.Id);
    }

    private static void Centred(ImDrawListPtr dl, float cx, float y, string text, uint col)
    {
        float w = ImGui.CalcTextSize(text).X;
        dl.AddText(new float2(cx - w / 2f + 2f, y + 2f), Col(0f, 0f, 0f, 0.7f), text);
        dl.AddText(new float2(cx - w / 2f, y), col, text);
    }
}
