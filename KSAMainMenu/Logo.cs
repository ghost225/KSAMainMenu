// The menu's logo: KSP's wordmark, kitten edition (user, 2026-10-26: "see the ksp and ksp2 logo, a bit close to that";
// picked the wordmark). tools/make_logo.py draws it to Assets/logo_wordmark.png (red KITTEN, the rocket as the K's stem,
// KSP's star flare and swoosh, SPACE AGENCY); the mod draws that picture. The kitten planet with a craft going round
// it (DrawEmblem) is the loading icon.
//
// Bebas Neue (SIL Open Font License, in Fonts/) is the heading font for the loading screen and the logo's fallback.
// KSA rebuilds its ImGui font atlas from scratch in FontManager.RegenerateFonts (start-up and font setting changes), so
// it's added in a postfix there; KSA's ImGui sizes fonts on demand, so one load serves every size.

using Brutal.ImGuiApi;
using Brutal.Logging;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace KSAMainMenu;

internal static class Logo
{
    private static ImFontPtr _condensed;
    private static bool _hasCondensed;
    private static ImageTexture? _wordmark;
    private static bool _wordmarkTried;

    public static void Patch(Harmony h) =>
        h.Patch(AccessTools.Method(typeof(FontManager), nameof(FontManager.RegenerateFonts)),
            postfix: new HarmonyMethod(typeof(Logo), nameof(AfterRegenerateFonts)));

    private static void AfterRegenerateFonts() => _hasCondensed = Load("BebasNeue-Regular.ttf", out _condensed);

    private static bool Load(string file, out ImFontPtr font)
    {
        font = default;
        try
        {
            string path = Path.Combine(Mod.Dir, "Fonts", file);
            if (!File.Exists(path))
            {
                DefaultCategory.Log.Warning($"[KSAMainMenu] font missing: {path}");
                return false;
            }
            font = FontManager.LoadFont(path, ImGui.GetIO().Fonts, 48f, pixelSnapH: false, fontDensity: 1f);
            return true;
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] font {file} not loaded: {ex.Message}");
            return false;
        }
    }

    private static uint Col(float r, float g, float b, float a = 1f) => ImGui.ColorConvertFloat4ToU32(new float4(r, g, b, a));

    /// Draws the wordmark `width` wide with its top-left at origin; returns its height. (Kept loaded: ~2 MB.)
    public static float Draw(ImDrawListPtr dl, float2 origin, float width)
    {
        if (!_wordmarkTried)
        {
            _wordmarkTried = true;
            _wordmark = ImageTexture.Load(Path.Combine(Mod.Dir, "Assets", "logo_wordmark.png"));
        }
        if (_wordmark != null)
        {
            float height = width / _wordmark.Aspect;
            dl.AddImage(_wordmark.Handle, origin, origin + new float2(width, height), new float2(0f), new float2(1f), Col(1f, 1f, 1f));
            return height;
        }
        // fallback: plain text
        float size = width * 0.16f;
        PushTitleFont(size);
        dl.AddText(origin + new float2(3f, 3f), Col(0f, 0f, 0f, 0.6f), "KITTEN SPACE AGENCY");
        dl.AddText(origin, Col(0.85f, 0.12f, 0.1f), "KITTEN SPACE AGENCY");
        ImGui.PopFont();
        return size * 1.2f;
    }

    private static void PushFont(ImFontPtr font, float size) => ImGui.PushFont(font, size);

    /// The condensed title font (Bebas Neue) at a size, for headings elsewhere; pop with ImGui.PopFont.
    public static void PushTitleFont(float size) => PushFont(_hasCondensed ? _condensed : ImGui.GetFont(), size);

    /// A planet with cat ears, eyes and whiskers, inside a tilted orbit ring with a little craft going round (a rocket
    /// pointing along the orbit for the loading icon).
    public static void DrawEmblem(ImDrawListPtr dl, float2 c, float r, double time, bool rocket = false)
    {
        uint ocean = Col(0.20f, 0.47f, 0.88f), deep = Col(0.13f, 0.32f, 0.66f), land = Col(0.36f, 0.72f, 0.42f);
        uint ring = Col(0.98f, 0.62f, 0.22f), ringBack = Col(0.98f, 0.62f, 0.22f, 0.45f);
        float tilt = -0.32f, a = r * 1.75f, b = r * 0.46f;
        float2 RingPoint(float t) =>
            c + new float2(a * MathF.Cos(t) * MathF.Cos(tilt) - b * MathF.Sin(t) * MathF.Sin(tilt),
                           a * MathF.Cos(t) * MathF.Sin(tilt) + b * MathF.Sin(t) * MathF.Cos(tilt));
        const int n = 64;

        // far half of the ring, behind the planet
        for (int i = 0; i < n / 2; i++)
            dl.AddLine(RingPoint(MathF.PI + i * MathF.PI / (n / 2)), RingPoint(MathF.PI + (i + 1) * MathF.PI / (n / 2)), ringBack, 3f);

        // ears (outer, then inner pink)
        for (int side = -1; side <= 1; side += 2)
        {
            float2 baseOut = c + new float2(side * r * 0.92f, -r * 0.38f);
            float2 tip = c + new float2(side * r * 0.80f, -r * 1.42f);
            float2 baseIn = c + new float2(side * r * 0.18f, -r * 0.92f);
            dl.AddTriangleFilled(baseOut, tip, baseIn, deep);
            dl.AddTriangleFilled(c + new float2(side * r * 0.74f, -r * 0.62f), c + new float2(side * r * 0.76f, -r * 1.18f),
                c + new float2(side * r * 0.38f, -r * 0.88f), Col(0.98f, 0.66f, 0.72f));
        }

        // planet: ocean, a darker limb, two continents and a highlight
        dl.AddCircleFilled(c, r, deep, 48);
        dl.AddCircleFilled(c + new float2(-r * 0.06f, -r * 0.06f), r * 0.92f, ocean, 48);
        dl.AddCircleFilled(c + new float2(-r * 0.42f, r * 0.38f), r * 0.30f, land, 24);
        dl.AddCircleFilled(c + new float2(r * 0.48f, -r * 0.52f), r * 0.20f, land, 24);
        dl.AddCircleFilled(c + new float2(-r * 0.38f, -r * 0.42f), r * 0.16f, Col(1f, 1f, 1f, 0.35f), 20);

        // face
        for (int side = -1; side <= 1; side += 2)
        {
            float2 eye = c + new float2(side * r * 0.36f, -r * 0.02f);
            dl.AddCircleFilled(eye, r * 0.15f, Col(0.06f, 0.08f, 0.14f), 20);
            dl.AddCircleFilled(eye + new float2(-r * 0.05f, -r * 0.05f), r * 0.05f, Col(1f, 1f, 1f), 12);
            for (int k = 0; k < 3; k++)
            {
                float2 from = c + new float2(side * r * 0.42f, r * (0.30f + 0.10f * k));
                float2 to = c + new float2(side * r * 1.32f, r * (0.16f + 0.22f * k));
                dl.AddLine(from, to, Col(1f, 1f, 1f, 0.85f), 1.6f);
            }
        }
        dl.AddTriangleFilled(c + new float2(-r * 0.09f, r * 0.20f), c + new float2(r * 0.09f, r * 0.20f),
            c + new float2(0f, r * 0.31f), Col(0.98f, 0.55f, 0.65f));

        // near half of the ring, in front, and a craft going round it
        for (int i = 0; i < n / 2; i++)
            dl.AddLine(RingPoint(i * MathF.PI / (n / 2)), RingPoint((i + 1) * MathF.PI / (n / 2)), ring, 3.5f);
        float ct = (float)(time * 0.6 % MathF.Tau);
        float2 craft = RingPoint(ct);
        bool front = ct < MathF.PI;
        if (!front && (craft - c).Length() <= r)
            return;   // hidden while behind the planet
        float alpha = front ? 1f : 0.6f;
        if (!rocket)
        {
            dl.AddCircleFilled(craft, r * 0.11f, Col(1f, 0.92f, 0.75f, alpha), 12);
            dl.AddCircleFilled(craft, r * 0.05f, Col(1f, 0.55f, 0.15f, alpha), 8);
            return;
        }
        // a little rocket along the orbit (direction of travel = increasing ct), with a flickering flame
        float2 ahead = RingPoint(ct + 0.05f) - craft;
        float2 f = ahead / MathF.Max(ahead.Length(), 1e-3f), nrm = new float2(-f.Y, f.X);
        float len = r * 0.62f, wid = r * 0.16f;
        float2 Pt(float along, float side) => craft + f * along + nrm * side;
        float flame = len * (0.35f + 0.12f * MathF.Sin((float)time * 23f));
        dl.AddTriangleFilled(Pt(-len * 0.5f, -wid * 0.6f), Pt(-len * 0.5f, wid * 0.6f), Pt(-len * 0.5f - flame, 0f), Col(1f, 0.6f, 0.15f, alpha));
        dl.AddTriangleFilled(Pt(-len * 0.5f, -wid * 0.35f), Pt(-len * 0.5f, wid * 0.35f), Pt(-len * 0.5f - flame * 0.55f, 0f), Col(1f, 0.95f, 0.6f, alpha));
        uint hull = Col(0.96f, 0.96f, 0.98f, alpha), trim = Col(0.98f, 0.45f, 0.2f, alpha);
        dl.AddTriangleFilled(Pt(-len * 0.5f, -wid), Pt(len * 0.25f, -wid), Pt(len * 0.25f, wid), hull);
        dl.AddTriangleFilled(Pt(-len * 0.5f, -wid), Pt(len * 0.25f, wid), Pt(-len * 0.5f, wid), hull);
        dl.AddTriangleFilled(Pt(len * 0.25f, -wid), Pt(len * 0.5f, 0f), Pt(len * 0.25f, wid), trim);
        dl.AddTriangleFilled(Pt(-len * 0.5f, -wid), Pt(-len * 0.2f, -wid), Pt(-len * 0.55f, -wid * 2f), trim);
        dl.AddTriangleFilled(Pt(-len * 0.5f, wid), Pt(-len * 0.2f, wid), Pt(-len * 0.55f, wid * 2f), trim);
        dl.AddCircleFilled(Pt(len * 0.02f, 0f), wid * 0.45f, Col(0.3f, 0.6f, 0.95f, alpha), 10);
    }
}
