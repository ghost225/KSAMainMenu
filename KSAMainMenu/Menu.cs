// The menu itself: title, a KSP2-style column of buttons over the orbit scene, and its screens (New Game, Load Game,
// menu options, credits). KSA's own settings window and popups (quit, confirm, restart) draw over it.
//
// A new game in the system that's loaded can start in place only while the universe is untouched (nothing has run
// since it loaded) and has the GameType the player asked for; anything else restarts KSA with a launch request.

using System.Reflection;
using Brutal.ImGuiApi;
using Brutal.Logging;
using Brutal.Numerics;
using KSA;

namespace KSAMainMenu;

public static class Menu
{
    /// Is the main menu showing? (For other mods, e.g. menu music.)
    public static bool IsOpen => MenuScene.Active;

    /// The Credits screen is showing (KSAMusic plays the credits track).
    public static bool IsCreditsOpen => MenuScene.Active && _screen == Screen.Credits;

    /// KSA is about to exit and relaunch into another system or a fresh universe.
    public static bool IsRestarting { get; internal set; }

    private enum Screen { Main, NewGame, Load, Options, Credits }

    private static Screen _screen;
    private static bool _started;
    private static int _frames;
    private static bool _pristine;
    private static bool _fromGame;
    private static string _loadedSystem = "";
    private static GameSettings.GameType _loadedGameType;
    private static List<SaveInfo>? _saves;
    private static string _loadFilter = "";

    // New Game choices
    private static string _newSystem = "";
    private static bool _newCareer = true;
    private static readonly ImInputString NameBuffer = new ImInputString(64, "");

    // deferred actions (a couple of frames after the menu closes, like a menu click)
    private static Action? _pending;
    private static int _pendingFrames;

    internal static void Frame(double dt)
    {
        try
        {
            if (!Program.IsLoaded || Universe.CurrentSystem == null)
                return;
            if (!_started)
            {
                if (++_frames < 3)
                    return;
                _started = true;
                OnUniverseReady();
            }
            LoadingScreen.ReleaseAll();
            LoadingScreen.TickAfterLoad();
            if (_pending != null && --_pendingFrames <= 0)
            {
                Action a = _pending;
                _pending = null;
                a();
            }
            MenuScene.Tick(dt);
            if (MenuScene.Active)
                Draw();
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Error($"[KSAMainMenu] menu frame: {ex}");
            if (MenuScene.Active)
                Close();
        }
    }

    private static void OnUniverseReady()
    {
        _loadedSystem = Universe.CurrentSystem!.Id;
        _loadedGameType = GameSettings.Current.System.StartGameType;
        _pristine = true;
        Startup.SystemId = _loadedSystem;
        try
        {
            // the untouched universe, for later new games in this world; kept serialized (KSA's own save serializer) so
            // each restore gets its own copy and playing never changes the snapshot
            var buffer = new MemoryStream();
            GameSaves.UniverseSerializer.Serialize(buffer, UniverseData.Create());
            _fresh = buffer.ToArray();
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] no fresh-universe snapshot (a later New Game restarts): {ex.Message}");
        }
        LaunchRequest? req = MenuConfig.Launch;
        MenuConfig.Launch = null;
        if (req?.Action == "continue" && req.Save != null && SaveFolder.Exists(req.Save))
        {
            LoadInPlace(req.Save);
            return;
        }
        if (req?.Action == "new")
        {
            StartInPlace(req.Name ?? "", req.Career);
            return;
        }
        if (req != null || !MenuConfig.Settings.SkipMenu)
            Open(fromGame: false);
    }

    /// Open the menu over the running universe (also from the pause menu's MAIN MENU button).
    internal static void Open(bool fromGame)
    {
        _fromGame = fromGame;
        _screen = Screen.Main;
        _saves = null;
        EscMenu.Open = false;
        MenuScene.Enter();
    }

    private static void Close()
    {
        MenuScene.Leave();
        Universe.SetSimulationSpeed(1.0, alert: false);
        try
        {
            Camera camera = Program.GetCamera();
            if (Program.ControlledVehicle is Vehicle v)
                camera.SetFollow(v, tidalLocking: false, changeControl: false, alert: false);
            else if (Universe.CurrentSystem?.HomeBody is Celestial home)
                camera.SetFollow(home, tidalLocking: false, changeControl: false, alert: false);
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] camera after menu: {ex.Message}");
        }
    }

    private static void Later(int frames, Action a)
    {
        _pending = a;
        _pendingFrames = frames;
    }

    // --- actions ---------------------------------------------------------------------------------------------------

    private static void Continue(SaveInfo save)
    {
        if (Worlds.CanLoadInto(save.System, _loadedSystem))
        {
            MenuScene.Leave();
            LoadInPlace(save.Id);
        }
        else
        {
            Restarter.Restart(new LaunchRequest { System = save.System, Action = "continue", Save = save.Id });
        }
    }

    private static void LoadInPlace(string id)
    {
        _pristine = false;
        Later(2, () =>
        {
            try
            {
                GameSaves.Refresh();
                GameSaves.LoadSaveGame(id);
            }
            catch (Exception ex)
            {
                DefaultCategory.Log.Warning($"[KSAMainMenu] couldn't load '{id}': {ex.Message}");
                TimedAlert.Create($"Couldn't load save '{id}': {ex.Message}", Color.Red, 8.0);
                Open(fromGame: false);
            }
        });
    }

    // A copy of the universe as it loaded, before anything ran: a new game in the same world restores it (the way a save
    // load restores a save) instead of restarting KSA.
    private static byte[]? _fresh;

    private static void NewGame(string system, string name, bool career)
    {
        if (system == _loadedSystem && (_pristine || RestoreFresh()))
        {
            StartInPlace(name, career, restored: !_pristine);
            return;
        }
        Restarter.Restart(new LaunchRequest { System = system, Action = "new", Name = name, Career = career });
    }

    private static bool RestoreFresh()
    {
        if (_fresh == null)
            return false;
        try
        {
            var data = (UniverseData)GameSaves.UniverseSerializer.Deserialize(new MemoryStream(_fresh))!;
            data.OnDataLoad(KSA.Mod.Empty);   // as UniverseData.LoadFrom does for a save
            Universe.DeserializeSave(data);
            Program.OnGameLoaded();
            DefaultCategory.Log.Info("[KSAMainMenu] new game: universe reset to its fresh start");
            return true;
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] couldn't reset the universe, restarting instead: {ex}");
            return false;
        }
    }

    private static void StartInPlace(string name, bool career, bool restored = false)
    {
        _pristine = false;
        SetProgression(career);
        if (restored)
            ResetScience();
        if (MenuScene.Active)
            Close();
        else
            Universe.SetSimulationSpeed(1.0, alert: false);
        if (name.Trim().Length == 0)
            return;
        Later(3, () =>
        {
            try
            {
                if (!SaveName.TryAccept(name, out string clean))
                    clean = SaveName.Sanitize(name);
                if (SaveFolder.Exists(clean))
                    clean = SaveName.Sanitize($"{clean} {DateTime.Now:yyyy-MM-dd HHmm}");
                GameSaves.MakeUncompressedSave(clean);
                TimedAlert.Create($"New game '{clean}' started and saved.", Color.Green, 5.0);
            }
            catch (Exception ex)
            {
                DefaultCategory.Log.Warning($"[KSAMainMenu] first save failed: {ex.Message}");
            }
        });
    }

    // KSAScience (optional): career or sandbox for the game that's starting.
    private static MethodInfo? _setProgression;
    private static bool _scienceSearched;

    private static MethodInfo? ScienceSetProgression()
    {
        if (!_scienceSearched)
        {
            _scienceSearched = true;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                if (asm.GetName().Name == "KSAScience" &&
                    asm.GetType("KSAScience.ScienceApi")?.GetMethod("SetProgression", new[] { typeof(bool) }) is { } m)
                    _setProgression = m;
        }
        return _setProgression;
    }

    // KSAScience starts a blank science state when a world loads (a postfix on Universe.LoadSystem); a new game from the
    // fresh snapshot doesn't go through LoadSystem, so it asks for the same reset (ScienceApi.StartNewGame, after
    // SetProgression).
    private static void ResetScience()
    {
        try
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name != "KSAScience")
                    continue;
                asm.GetType("KSAScience.ScienceApi")?.GetMethod("StartNewGame", Type.EmptyTypes)?.Invoke(null, null);
            }
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] KSAScience reset failed: {ex.Message}");
        }
    }

    private static void SetProgression(bool career)
    {
        try
        {
            ScienceSetProgression()?.Invoke(null, new object[] { career });
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] KSAScience SetProgression failed: {ex.Message}");
        }
    }

    // --- drawing ---------------------------------------------------------------------------------------------------

    private static float S => GameSettings.GetInterfaceScale();

    private static void Draw()
    {
        if (GameSettings.Show || Popup.AnyOpen)
            return;   // KSA's settings window or a popup is up: leave the screen to it

        ImGuiViewportPtr vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(vp.Pos, ImGuiCond.Always, (float2?)null);
        ImGui.SetNextWindowSize(vp.Size, ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new float4(0f));
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings |
                                       ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoBackground;
        bool open = ImGui.Begin("###KSAMainMenu", flags);
        ImGui.PopStyleColor();
        if (open)
        {
            DrawTitle(vp);
            float width = (_screen is Screen.Load or Screen.NewGame or Screen.Options or Screen.Credits ? 620f : 340f) * S;
            float top = 270f * S;
            ImGui.SetCursorPos(new float2(56f * S, top));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new float4(0.02f, 0.03f, 0.06f, 0.72f));
            // the main column fits its buttons; the other screens get a fixed height (the save list scrolls)
            bool fit = _screen == Screen.Main;
            float height = fit ? 0f : MathF.Min(vp.Size.Y - top - 60f * S, 640f * S);
            if (ImGui.BeginChild("##menupanel", new float2?(new float2(width, height)),
                    ImGuiChildFlags.AlwaysUseWindowPadding | (fit ? ImGuiChildFlags.AutoResizeY : ImGuiChildFlags.None),
                    ImGuiWindowFlags.None))
            {
                ConsoleStyle.PushWidgetStyle();
                switch (_screen)
                {
                    case Screen.Main: DrawMain(); break;
                    case Screen.NewGame: DrawNewGame(); break;
                    case Screen.Load: DrawLoad(); break;
                    case Screen.Options: DrawOptions(); break;
                    case Screen.Credits: DrawCredits(); break;
                }
                ConsoleStyle.PopWidgetStyle();
                if (_screen != Screen.Main && ImGui.IsKeyPressed(ImGuiKey.Escape, false))
                    _screen = Screen.Main;
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
            DrawFooter(vp);
        }
        ImGui.End();
    }

    private static void DrawTitle(ImGuiViewportPtr vp)
    {
        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        float2 p = vp.Pos + new float2(40f * S, 40f * S);
        float h = Logo.Draw(dl, p, 560f * S);
        ImGui.PushFont(ImGui.GetFont(), 22f * S);
        string sub = $"{SaveFolder.SystemName(_loadedSystem).ToUpperInvariant()}   ·   {(_fromGame ? "PAUSED" : "MAIN MENU")}";
        float2 at = p + new float2(160f * S, h + 10f * S);
        dl.AddText(at + new float2(1f, 1f), ImGui.ColorConvertFloat4ToU32(new float4(0f, 0f, 0f, 0.7f)), sub);
        dl.AddText(at, ImGui.ColorConvertFloat4ToU32(new float4(0.75f, 0.85f, 1f, 0.95f)), sub);
        ImGui.PopFont();
    }

    private static void DrawFooter(ImGuiViewportPtr vp)
    {
        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        string text = "Hold Shift while KSA loads for KSA's own start-up popup";
        float2 at = vp.Pos + new float2(56f * S, vp.Size.Y - 36f * S);
        dl.AddText(at + new float2(1f, 1f), ImGui.ColorConvertFloat4ToU32(new float4(0f, 0f, 0f, 0.8f)), text);
        dl.AddText(at, ImGui.ColorConvertFloat4ToU32(new float4(1f, 1f, 1f, 0.6f)), text);
    }

    private static float2 ButtonSize => new float2(ImGui.GetContentRegionAvail().X, 40f * S);

    private static void DrawMain()
    {
        _saves ??= SaveFolder.All();
        SaveInfo? newest = _saves.FirstOrDefault(s => Worlds.CanLoadInto(s.System, _loadedSystem));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new float2(0f, 10f * S));
        if (_fromGame && ConsoleWidgets.PositiveButton("RESUME", ButtonSize))
            Close();
        ConsoleStyle.PushDisabled(newest == null);
        bool cont = ConsoleWidgets.Button("CONTINUE", ButtonSize);
        ConsoleStyle.PopDisabled();
        if (newest != null)
        {
            ImGui.TextDisabled($"  {newest.Name} · {SaveFolder.SystemName(newest.System)} · {newest.Updated:yyyy-MM-dd HH:mm}");
        }
        if (cont && newest != null)
            Confirmed("CONTINUE", $"Load '{newest.Name}'?", () => Continue(newest));
        if (ConsoleWidgets.Button("NEW GAME", ButtonSize))
            OpenNewGame();
        if (ConsoleWidgets.Button("LOAD GAME", ButtonSize))
        {
            _saves = SaveFolder.All();
            _screen = Screen.Load;
        }
        if (ConsoleWidgets.Button("SETTINGS", ButtonSize))
            GameSettings.Show = true;
        if (ConsoleWidgets.Button("MODS", ButtonSize))
            OpenModsTab();
        if (ConsoleWidgets.Button("MENU OPTIONS", ButtonSize))
            _screen = Screen.Options;
        if (ConsoleWidgets.Button("CREDITS", ButtonSize))
            _screen = Screen.Credits;
        ConsoleWidgets.Rule();
        if (ConsoleWidgets.DangerButton("QUIT", ButtonSize))
            ExitPopup.Create();
        ImGui.PopStyleVar();
    }

    // In a game, anything that replaces it asks first (KSA keeps no unsaved state on disk).
    private static void Confirmed(string title, string question, Action a)
    {
        if (_fromGame)
            ConfirmActionPopup.Create(a, title, question + " Anything since your last save is lost.");
        else
            a();
    }

    private static void OpenNewGame()
    {
        _newSystem = _loadedSystem;
        _newCareer = ScienceSetProgression() != null;
        _saves ??= SaveFolder.All();
        int n = 1;
        while (_saves.Any(s => s.Id.Equals($"Kitten Program {n}", StringComparison.OrdinalIgnoreCase)))
            n++;
        NameBuffer.SetValue($"Kitten Program {n}");
        _screen = Screen.NewGame;
    }

    private static void DrawNewGame()
    {
        Heading("NEW GAME");
        ImGui.TextDisabled("World");
        foreach (SystemInfo sys in SelectSystem.Systems)
        {
            string label = sys.GetName() is { Length: > 0 } n ? n : sys.Id;
            if (ImGui.RadioButton($"{label}##sys_{sys.Id}", _newSystem == sys.Id))
                _newSystem = sys.Id;
        }
        ImGui.Spacing();
        ImGui.TextDisabled("Mode");
        bool science = ScienceSetProgression() != null;
        ConsoleStyle.PushDisabled(!science);
        if (ImGui.RadioButton("Career (science unlocks parts)", _newCareer && science))
            _newCareer = true;
        if (ImGui.RadioButton("Sandbox (every part)", !_newCareer || !science))
            _newCareer = false;
        ConsoleStyle.PopDisabled();
        if (!science)
            ImGui.TextDisabled("  KSAScience isn't installed: sandbox only.");
        ImGui.Spacing();
        ImGui.TextDisabled("Save name");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("##newname", NameBuffer);
        ImGui.Spacing();

        ImGui.TextWrapped(_newSystem == _loadedSystem
            ? "Starts right away in this world."
            : $"KSA restarts into {SaveFolder.SystemName(_newSystem)} and starts the game there (a world is chosen when KSA " +
              "loads: next time, pick it in the world picker).");
        ImGui.Spacing();
        float half = (ImGui.GetContentRegionAvail().X - 10f * S) / 2f;
        if (ConsoleWidgets.PositiveButton("START", new float2(half, 40f * S)))
        {
            string name = NameBuffer.ToString();
            string sys = _newSystem;
            bool career = _newCareer && science;
            Confirmed("NEW GAME", $"Start '{name}'?", () => NewGame(sys, name, career));
        }
        ImGui.SameLine(0f, 10f * S);
        if (ConsoleWidgets.Button("BACK", new float2(half, 40f * S)))
            _screen = Screen.Main;
    }

    private static void DrawLoad()
    {
        Heading("LOAD GAME");
        _saves ??= SaveFolder.All();
        var systems = _saves.Select(s => s.System).Where(s => s.Length > 0).Distinct().ToList();
        if (ImGui.RadioButton("All", _loadFilter.Length == 0))
            _loadFilter = "";
        foreach (string sys in systems)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{SaveFolder.SystemName(sys)}##f_{sys}", _loadFilter == sys))
                _loadFilter = sys;
        }
        ImGui.Spacing();
        float listH = ImGui.GetContentRegionAvail().Y - 84f * S;
        if (ImGui.BeginChild("##saves", new float2?(new float2(0f, listH)), ImGuiChildFlags.None, ImGuiWindowFlags.None))
        {
            int shown = 0;
            foreach (SaveInfo s in _saves)
            {
                if (_loadFilter.Length > 0 && s.System != _loadFilter)
                    continue;
                shown++;
                ImGui.PushID(s.Id);
                ConsoleStyle.PushDisabled(!s.SystemInstalled);
                bool load = ConsoleWidgets.Button("LOAD", new float2(80f * S, 30f * S));
                ConsoleStyle.PopDisabled();
                ImGui.SameLine(0f, 6f * S);
                if (ConsoleWidgets.DangerButton("DELETE", new float2(80f * S, 30f * S)))
                {
                    SaveInfo doomed = s;
                    ConfirmActionPopup.Create(() => TrashSave(doomed), "DELETE SAVE",
                        $"Delete '{s.Name}'? It goes to the trash (or a saves-deleted folder), so you can still get it back.");
                }
                ImGui.SameLine(0f, 12f * S);
                ImGui.BeginGroup();
                ImGui.Text(s.Name);
                string where = SaveFolder.SystemName(s.System);
                if (!s.SystemInstalled)
                    where += " (not installed)";
                else if (!Worlds.CanLoadInto(s.System, _loadedSystem))
                    where += " (restarts KSA)";
                ImGui.TextDisabled($"{where} · {s.Updated:yyyy-MM-dd HH:mm}{(s.Version.Length > 0 ? " · " + s.Version : "")}");
                ImGui.EndGroup();
                ImGui.PopID();
                ImGui.Spacing();
                if (load && s.SystemInstalled)
                {
                    SaveInfo pick = s;
                    Confirmed("LOAD GAME", $"Load '{s.Name}'?", () => Continue(pick));
                }
            }
            if (shown == 0)
                ImGui.TextDisabled("No saves yet.");
        }
        ImGui.EndChild();
        if (_loadStatus.Length > 0)
            ImGui.TextDisabled(_loadStatus);
        if (ConsoleWidgets.Button("BACK", new float2(ImGui.GetContentRegionAvail().X, 40f * S)))
        {
            _screen = Screen.Main;
            _loadStatus = "";
        }
    }

    private static string _loadStatus = "";   // the last delete's outcome (KSA's alerts don't show on the menu)

    // Deleting a save never erases it: on Linux it goes to the desktop trash (gio), elsewhere (or without gio) into a
    // saves-deleted folder next to the saves, so a slip can be undone. (KSA's own delete is permanent.)
    private static void TrashSave(SaveInfo save)
    {
        string dir = Path.Combine(GameSaves.SaveFolderPath, save.Id);
        try
        {
            if (OperatingSystem.IsLinux() && TryGioTrash(dir))
            {
                _loadStatus = $"'{save.Name}' moved to the trash.";
            }
            else
            {
                string bin = Path.Combine(KSA.Constants.DocumentsFolderPath, "saves-deleted");
                Directory.CreateDirectory(bin);
                string target = Path.Combine(bin, $"{save.Id} {DateTime.Now:yyyy-MM-dd HHmmss}");
                Directory.Move(dir, target);
                _loadStatus = $"'{save.Name}' moved to {bin}.";
            }
            DefaultCategory.Log.Info($"[KSAMainMenu] save '{save.Id}' deleted: {_loadStatus}");
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] couldn't delete save '{save.Id}': {ex.Message}");
            _loadStatus = $"Couldn't delete '{save.Name}': {ex.Message}. It's still there.";
        }
        _saves = SaveFolder.All();
        try
        {
            GameSaves.Refresh();
        }
        catch
        {
            // KSA's own save list catches up next time it opens
        }
    }

    private static bool TryGioTrash(string dir)
    {
        try
        {
            using var gio = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("gio")
            {
                ArgumentList = { "trash", dir },
                UseShellExecute = false,
                RedirectStandardError = true,
            });
            if (gio == null)
                return false;
            gio.StandardError.ReadToEnd();
            gio.WaitForExit(10000);
            return gio.ExitCode == 0 && !Directory.Exists(dir);
        }
        catch
        {
            return false;   // no gio: use the saves-deleted folder
        }
    }

    private static void DrawOptions()
    {
        Heading("MENU OPTIONS");
        MenuSettings s = MenuConfig.Settings;
        bool changed = false;
        changed |= ImGui.Checkbox("Skip the menu at launch (go straight into the game)", ref s.SkipMenu);
        changed |= ImGui.Checkbox("Ask which world (Sol, Kerbol, ...) when KSA starts", ref s.AskWorld);
        changed |= ImGui.Checkbox("Use KSA's own start-up popup instead", ref s.ClassicPopup);
        changed |= ImGui.Checkbox("Kittens", ref s.Kittens);
        ImGui.Spacing();
        ImGui.TextDisabled("View");
        changed |= Slider("Height (body radii)", ref s.AltitudeRadii, 0.05f, 3f);
        changed |= Slider("Planet below centre (deg)", ref s.PlanetBelowDeg, 10f, 80f);
        changed |= Slider("Angle from the sun (deg)", ref s.SunAngleDeg, 0f, 150f);
        changed |= Slider("Drift (deg/min)", ref s.DriftDegPerMin, 0f, 20f);
        if (changed)
            MenuConfig.Save();
        ImGui.Spacing();
        ImGui.TextWrapped("Kitten positions and poses are in settings.json in the mod's folder. Changing \"Kittens\" " +
                          "takes effect next launch.");
        ImGui.Spacing();
        if (ConsoleWidgets.Button("BACK", new float2(ImGui.GetContentRegionAvail().X, 40f * S)))
            _screen = Screen.Main;
    }

    private static bool Slider(string label, ref double value, float min, float max)
    {
        float v = (float)value;
        ImGui.SetNextItemWidth(260f * S);
        if (!ImGui.SliderFloat(label, ref v, min, max))
            return false;
        value = v;
        return true;
    }

    private static void DrawCredits()
    {
        Heading("CREDITS");
        ImGui.TextWrapped("Kitten Space Agency by RocketWerkz: the game, its kittens (Hunter, Banjo and Polaris), " +
                          "planets and everything this menu stands on.");
        ImGui.Spacing();
        ImGui.TextWrapped("Kerbal Space Program and KSP2 by Squad / Intercept Games, whose title screens inspired this one.");
        ImGui.Spacing();
        ImGui.TextWrapped("StarMap (mod loader), Borea (launcher), Harmony, and the community mods in the Mods tab.");
        ImGui.Spacing();
        ImGui.TextWrapped("Bebas Neue font by Dharma Type (SIL Open Font License).");
        ImGui.Spacing();
        ImGui.TextWrapped("Kitten Main Menu by ghost225.");
        ImGui.Spacing();
        if (ConsoleWidgets.Button("BACK", new float2(ImGui.GetContentRegionAvail().X, 40f * S)))
            _screen = Screen.Main;
    }

    private static void Heading(string text)
    {
        ImGui.PushFont(ImGui.GetFont(), 30f * S);
        ImGui.Text(text);
        ImGui.PopFont();
        ConsoleWidgets.Rule();
    }

    private static void OpenModsTab()
    {
        GameSettings.Show = true;
        try
        {
            FieldInfo? tab = typeof(GameSettings).GetField("_openTab", BindingFlags.NonPublic | BindingFlags.Static);
            if (tab != null)
                tab.SetValue(null, Enum.Parse(tab.FieldType, "Mods"));
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] couldn't open the Mods tab: {ex.Message}");
        }
    }
}
