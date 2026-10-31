using System;
using System.Globalization;
using BepInEx.Configuration;
using Il2CppInterop.Runtime;
using UnityEngine;
using S = DimraethModMenu.Settings;

namespace DimraethModMenu;

/// <summary>
/// F1 menu plus the per-frame upkeep for toggles that are state on game objects rather than patches.
/// Only GUI.Window / GUI.Button / GUI.Label are used: the game build stripped GUILayout sliders, toggles
/// and horizontal groups, so everything is laid out by hand.
/// </summary>
public class ModMenu : MonoBehaviour
{
    public ModMenu(IntPtr ptr) : base(ptr) { }
    // Keeps the original injected managed instance available to development-time render checks.
    internal static ModMenu ManagedInstance;
    void Awake() { ManagedInstance = this; WildwoodPreferences.Load(); }

    static readonly float[] Steps = { 0f, 0.1f, 0.25f, 0.5f, 0.75f, 1f, 1.5f, 2f, 3f, 5f, 10f, 25f, 50f, 100f };
    static readonly float[] SpeedSteps = { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f, 2.5f, 3f };
    static readonly int[] Amounts = { 1, 5, 10, 25, 50, 100 };
    static readonly string[] Tabs = { "Economy", "Combat", "Survival", "Unlocks", "Items", "World", "Tools" };
    const float MenuWidth = 720, MenuHeight = 760, ContentLeft = 205, ContentRight = 52;

    // Pickers for the QA spawners (IMGUI here has no dropdowns or text fields).
    static readonly ItemType[] ItemTypes = Values<ItemType>();
    static readonly Runes.RuneSet[] RuneSets = Values<Runes.RuneSet>();
    static readonly Runes.SlotType[] RuneSlots = (Runes.SlotType[])Enum.GetValues(typeof(Runes.SlotType));
    static readonly PetType[] PetTypes = Values<PetType>();
    static readonly MonsterType[] MonsterTypes = (MonsterType[])Enum.GetValues(typeof(MonsterType));
    static readonly Weathers[] WeatherTypes = Values<Weathers>();
    int _item, _amount, _runeSet, _runeSlot, _pet, _monster, _weather, _checkpoint;
    bool _runeFullSet, _monsterNoAggro, _monsterImmune, _monsterInfo;

    /// <summary>All enum values except a leading "None".</summary>
    static T[] Values<T>() where T : Enum
    {
        var all = (T[])Enum.GetValues(typeof(T));
        return Array.FindAll(all, v => v.ToString() != "None");
    }

    /// <summary>Read by the input patches so menu clicks don't reach the game.</summary>
    public static bool MouseOverMenu;

    /// <summary>Set once the player picks a weather, so the game's early-game sunny lock stops overriding it.</summary>
    public static bool ForceWeather;

    bool _open;
    int _tab;
    Rect _window = new(28, 36, MenuWidth, MenuHeight);
    GUI.WindowFunction _drawFn;
    float _y;
    string _status = "";
    float _statusUntil;
    float _lastUiScale = 1, _lastRequestedScale = -1;
    int _screenWidth, _screenHeight;
    bool _placeWindow = true;

    float _nextTick, _nextHungerFill;
    DeveloperMode _devMode;
    IntPtr _devModeOwner;
    bool? _appliedGod, _appliedNoCd, _appliedUntargetable;
    float _appliedSpeed = 1f, _appliedGameSpeed = 1f;
    GameObject _qaHost;

    void Update()
    {
        if (Time.unscaledTime < _nextTick) return;
        _nextTick = Time.unscaledTime + 0.5f;
        try
        {
            if (_pendingCheckpoint != null) FinishCheckpointLoad();
            Tick();
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"Tick: {e.Message}"); }
    }

    void Tick()
    {
        if (!Game.Active) return;

        // Wait until we're in a world: game statics touched on the main menu can crash IL2CPP class init.
        var lp = Game.LocalPlayer;
        if (lp == null) { _devMode = null; return; }

        ApplyGameSpeed();

        // Travelling replaces the player object; follow it so the switches aren't written to the old one.
        if (_devMode == null || _devMode.WasCollected || _devModeOwner != lp.Pointer)
        {
            _devMode = lp.GetComponent<DeveloperMode>();
            _devModeOwner = lp.Pointer;
            _appliedGod = _appliedNoCd = _appliedUntargetable = null;
            _appliedSpeed = 1f; // the speed-up effect lived on the old player too
            Game.Trace("World", $"local player found, DeveloperMode component: {_devMode != null}");
        }
        if (_devMode != null)
        {
            Sync(ref _appliedGod, S.GodMode.Value, v => _devMode.Invulnerability = v, () => _devMode.Invulnerability);
            Sync(ref _appliedNoCd, S.NoCooldowns.Value, v => _devMode.NoCooldownsEnabled = v, () => _devMode.NoCooldownsEnabled);
            Sync(ref _appliedUntargetable, S.Untargetable.Value, v => _devMode.Untargetable = v, () => _devMode.Untargetable);
        }

        if (S.NoHunger.Value && Time.unscaledTime >= _nextHungerFill)
        {
            _nextHungerFill = Time.unscaledTime + 10f;
            lp.Consumption?.ResetHungerThirstToFull();
        }

        if (!Mathf.Approximately(_appliedSpeed, S.SpeedMult.Value))
        {
            var qa = GetQA(() => QAEffect.Instance);
            if (qa != null)
            {
                if (Mathf.Approximately(S.SpeedMult.Value, 1f)) qa.RemovePlayerSpeedUp();
                else qa.AddPlayerSpeedUp(S.SpeedMult.Value);
                _appliedSpeed = S.SpeedMult.Value;
            }
        }
    }

    /// <summary>Writes a bool switch when our setting changes, and re-asserts it while on (respawns reset it).</summary>
    static void Sync(ref bool? applied, bool wanted, Action<bool> set, Func<bool> get)
    {
        if (applied != wanted || (wanted && !get()))
        {
            set(wanted);
            applied = wanted;
        }
    }

    void ApplyGameSpeed()
    {
        // Only on change, so the game's own pause/slow-mo isn't fought every tick.
        if (Mathf.Approximately(_appliedGameSpeed, S.GameSpeed.Value)) return;
        Time.timeScale = S.GameSpeed.Value;
        _appliedGameSpeed = S.GameSpeed.Value;
    }

    /// <summary>The game's own limit (our patch passes it through while the setting is 0).</summary>
    static int DefaultFarmLimit()
    {
        try { return PlayerBaseManager.Singleton?.GetFarmPlotLimit() ?? 0; }
        catch { return 0; }
    }

    static ServerRPC FindServerRpc() =>
        Game.LocalPlayer?.GetComponent<ServerRPC>() ?? FindFirstObjectByType<ServerRPC>()
        ?? throw new InvalidOperationException("ServerRPC bulunamadı");

    /// <summary>The game ships its QA tool components; use the scene instance or spawn our own.</summary>
    T GetQA<T>(Func<T> instance) where T : Component
    {
        var existing = instance();
        if (existing != null) return existing;
        if (_qaHost == null)
        {
            _qaHost = new GameObject("DimraethMod_QA");
            DontDestroyOnLoad(_qaHost);
        }
        return _qaHost.GetComponent<T>() ?? _qaHost.AddComponent<T>();
    }

    void Run(string label, Action action)
    {
        try
        {
            if (!Game.Active) { Say("Başkasının oyununda kapalı."); return; }
            action();
            Say($"OK: {label}");
        }
        catch (Exception e)
        {
            Say($"Hata: {label}");
            Plugin.Logger.LogWarning($"{label}: {e}");
        }
    }

    void Say(string msg)
    {
        _status = msg;
        _statusUntil = Time.unscaledTime + 4f;
        Plugin.Logger.LogInfo(msg);
    }

    // ───────────── GUI ─────────────

    internal float PresentationScale() => Mathf.Max(.1f, Mathf.Min(
        Mathf.Max(1, Screen.height / 1080f) * WildwoodPreferences.Scale,
        Mathf.Min((Screen.width - 16f) / MenuWidth, (Screen.height - 16f) / MenuHeight)));

    void RememberPlacement(float scale)
    {
        if (_screenWidth <= 0 || scale <= 0) return;
        float margin = 8 / scale;
        float xRange = Mathf.Max(0, Screen.width / scale - MenuWidth - 2 * margin);
        float yRange = Mathf.Max(0, Screen.height / scale - MenuHeight - 2 * margin);
        WildwoodPreferences.SetPosition(xRange > .01f ? (_window.x - margin) / xRange : WildwoodPreferences.PositionX,
            yRange > .01f ? (_window.y - margin) / yRange : WildwoodPreferences.PositionY);
    }

    internal void SetPresentationScale(float value)
    {
        if (!_placeWindow) RememberPlacement(_lastUiScale);
        WildwoodPreferences.SetScale(value); WildwoodPreferences.Save(); _placeWindow = true;
    }

    internal void SetPresentationPosition(float x, float y)
    { WildwoodPreferences.SetPosition(x, y); WildwoodPreferences.Save(); _placeWindow = true; }

    internal void ResetPresentation()
    { WildwoodPreferences.Reset(); WildwoodPreferences.Save(); _placeWindow = true; }

    void OnApplicationQuit()
    { if (!_placeWindow) RememberPlacement(_lastUiScale); WildwoodPreferences.Save(); }

    void OnGUI()
    {
        var e = Event.current;
        bool mouseReleased = e != null && e.type == EventType.MouseUp;
        if (e != null && e.type == EventType.KeyDown && (e.keyCode == KeyCode.F1 || e.keyCode == KeyCode.Insert))
        {
            _open = !_open;
            if (!_open) WildwoodPreferences.Save();
            e.Use();
        }
        var previousMatrix = GUI.matrix;
        var previousColor = GUI.color;
        var previousContentColor = GUI.contentColor;
        var previousBackgroundColor = GUI.backgroundColor;
        bool previousEnabled = GUI.enabled;
        var mouse = e != null ? e.mousePosition : new Vector2(-10000, -10000);
        float scale = PresentationScale();
        try
        {
            // The existing monster HUD retains its old scale; the new preference applies to the menu.
            float hudScale = Mathf.Max(.1f, Mathf.Min(Mathf.Max(1f, Screen.height / 1080f),
                Mathf.Min((Screen.width - 16f) / 620f, (Screen.height - 16f) / 700f)));
            GUI.matrix = Matrix4x4.Scale(new Vector3(hudScale, hudScale, 1f));
            if (_monsterInfo && e != null && e.type == EventType.Repaint) DrawMonsterInfo(hudScale);
            if (!_open) { MouseOverMenu = false; return; }
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            // Other game GUI passes can leave a tint or disabled state behind. Scope our own
            // drawing state so labels and native buttons retain their intended colors/usability.
            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            WildwoodTheme.Ensure();
            _window.width = MenuWidth; _window.height = MenuHeight;
            if (_placeWindow || _screenWidth != Screen.width || _screenHeight != Screen.height
                || !Mathf.Approximately(_lastRequestedScale, WildwoodPreferences.Scale))
            {
                float margin = 8 / scale;
                _window.x = margin + WildwoodPreferences.PositionX * Mathf.Max(0, Screen.width / scale - MenuWidth - 2 * margin);
                _window.y = margin + WildwoodPreferences.PositionY * Mathf.Max(0, Screen.height / scale - MenuHeight - 2 * margin);
                _screenWidth = Screen.width; _screenHeight = Screen.height;
                _lastRequestedScale = WildwoodPreferences.Scale; _placeWindow = false;
            }
            _lastUiScale = scale;
            _window.x = Mathf.Clamp(_window.x, 8f / scale, Mathf.Max(8f / scale, Screen.width / scale - MenuWidth - 8f / scale));
            _window.y = Mathf.Clamp(_window.y, 8f / scale, Mathf.Max(8f / scale, Screen.height / scale - MenuHeight - 8f / scale));
            _drawFn ??= DelegateSupport.ConvertDelegate<GUI.WindowFunction>(new Action<int>(DrawWindow));
            _window = WildwoodTheme.Ready
                ? GUI.Window(0x0D1A, _window, _drawFn, "", WildwoodTheme.Window)
                : GUI.Window(0x0D1A, _window, _drawFn, "Wildwood Pact / F1");
            if (!_placeWindow) RememberPlacement(scale);
            if (mouseReleased || !_open) WildwoodPreferences.Save();
            MouseOverMenu = _open && _window.Contains(new Vector2(mouse.x / scale, mouse.y / scale));
        }
        catch (Exception ex) when (!ex.GetType().Name.Contains("ExitGUI") && !ex.Message.Contains("ExitGUI"))
        {
            WildwoodTheme.Disable(ex);
            MouseOverMenu = false;
        }
        finally
        {
            GUI.enabled = previousEnabled;
            GUI.backgroundColor = previousBackgroundColor;
            GUI.contentColor = previousContentColor;
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }
    }

    Monster[] _monsters = Array.Empty<Monster>();
    float _nextMonsterScan;

    /// <summary>HP label over each monster (the game's QAVision overlay draws nothing outside the editor).</summary>
    void DrawMonsterInfo(float scale)
    {
        var cam = Camera.main;
        if (cam == null || Game.LocalPlayer == null) return;
        if (Time.unscaledTime >= _nextMonsterScan)
        {
            _nextMonsterScan = Time.unscaledTime + 1f;
            _monsters = FindObjectsByType<Monster>(FindObjectsSortMode.None);
        }
        foreach (var m in _monsters)
        {
            if (m == null || m.WasCollected || m.IsDead()) continue;
            var p = cam.WorldToScreenPoint(m.transform.position + Vector3.up * 1.2f);
            if (p.z < 0 || p.x < 0 || p.x > Screen.width || p.y < 0 || p.y > Screen.height) continue;
            float hp = m.Health, max = m.MaxHealth;
            var r = new Rect(p.x / scale - 80, (Screen.height - p.y) / scale - 20, 160, 20);
            GUI.Label(r, $"{m.gameObject.name.Replace("(Clone)", "").Trim()}  {hp:0}/{max:0}");
        }
    }

    void DrawWindow(int id)
    {
        WildwoodTheme.Fill(new Rect(52, 108, 616, 624), WildwoodTheme.Body);
        if (WildwoodTheme.FrameReady) WildwoodTheme.Fill(new Rect(0, 0, MenuWidth, MenuHeight), WildwoodTheme.Frame);
        WildwoodTheme.Write(new Rect(150, 53, 420, 37), "WILDWOOD PACT", WildwoodTheme.Title);
        WildwoodTheme.Write(new Rect(150, 89, 420, 16), "DIMRAETH MODLAB  /  BY FALOR", WildwoodTheme.Subtitle);
        WildwoodTheme.Fill(new Rect(607, 71, 30, 29), WildwoodTheme.Value);
        WildwoodTheme.Write(new Rect(607, 71, 30, 29), "F1", WildwoodTheme.Footer);
        if (WildwoodTheme.Click(new Rect(645, 71, 29, 29), "X", WildwoodTheme.SmallButton))
        { _open = false; MouseOverMenu = false; return; }
        WildwoodTheme.Fill(new Rect(51, 122, 618, 28), WildwoodTheme.Row);
        WildwoodTheme.Write(new Rect(65, 123, 210, 26), Game.Active ? "SOLO / HOST  ·  ACTIVE" : "SOLO / HOST ONLY", WildwoodTheme.Muted);
        var lp = Game.Active ? Game.LocalPlayer : null;
        WildwoodTheme.Write(new Rect(290, 123, 336, 26), lp != null
            ? $"Gold {(int)lp.Gold}   XP {(int)lp.XP}   Points {(int)lp.SkillPoints}"
            : "Enter your world to use actions", WildwoodTheme.Right);
        WildwoodSprites.Icon(new Rect(642, 127, 17, 18), 11);
        WildwoodTheme.Write(new Rect(76, 163, 112, 21), "YOUR PACT", WildwoodTheme.Muted);
        for (int i = 0; i < Tabs.Length; i++)
        {
            var tabRect = new Rect(43, 185 + i * 60, 146, 58);
            if (WildwoodTheme.Click(tabRect, Tabs[i], i == _tab ? WildwoodTheme.SelectedTab : WildwoodTheme.Tab)) _tab = i;
            WildwoodSprites.Icon(new Rect(tabRect.x + 12, tabRect.y + 17, 23, 24), i);
        }
        bool screenLimited = _lastUiScale + .001f < Mathf.Max(1, Screen.height / 1080f) * WildwoodPreferences.Scale;
        WildwoodTheme.Write(new Rect(76, 617, 112, 21), screenLimited ? "UI SCALE · FIT" : "UI SCALE", WildwoodTheme.Muted);
        if (WildwoodTheme.Click(new Rect(72, 641, 28, 30), "−", WildwoodTheme.SmallButton))
            SetPresentationScale(Mathf.Round((WildwoodPreferences.Scale - .1f) * 10) / 10);
        WildwoodTheme.Fill(new Rect(104, 641, 54, 30), WildwoodTheme.Value);
        WildwoodTheme.Write(new Rect(104, 641, 54, 30), Mathf.RoundToInt(WildwoodPreferences.Scale * 100) + "%", WildwoodTheme.Number);
        if (WildwoodTheme.Click(new Rect(162, 641, 28, 30), "+", WildwoodTheme.SmallButton))
            SetPresentationScale(Mathf.Round((WildwoodPreferences.Scale + .1f) * 10) / 10);
        if (WildwoodTheme.Click(new Rect(72, 678, 118, 24), "Reset layout", WildwoodTheme.Action)) ResetPresentation();
        WildwoodTheme.Write(new Rect(ContentLeft + 4, 159, 459, 35), Tabs[_tab], WildwoodTheme.Heading);
        _y = 201;
        switch (_tab)
        {
            case 0: DrawEconomy(); break;
            case 1: DrawCombat(); break;
            case 2: DrawSurvival(); break;
            case 3: DrawUnlocks(); break;
            case 4: DrawItems(); break;
            case 5: DrawWorld(); break;
            case 6: DrawTools(); break;
        }
        WildwoodTheme.Fill(new Rect(54, 706, 612, 27), WildwoodTheme.Row);
        WildwoodSprites.Icon(new Rect(65, 712, 15, 16), 11);
        string status = Time.unscaledTime < _statusUntil ? _status.Replace("Hata:", "Error:")
            .Replace("Başkasının oyununda kapalı.", "Actions require your own world or host session.")
            .Replace("Yedek yüklendi, Continue'ya bas", "Checkpoint restored. Select Continue.")
            : "Settings save automatically     |     F1 / INSERT: CLOSE";
        WildwoodTheme.Write(new Rect(89, 707, 563, 25), status, WildwoodTheme.Footer);
        GUI.DragWindow(new Rect(150, 48, 420, 59));
    }

    void DrawEconomy()
    {
        Section("GROWTH");
        Mult("Player XP", S.XpMult);
        Mult("Pet XP", S.PetXpMult);
        Mult("Proficiency XP", S.ProfXpMult);
        Section("FORTUNE");
        Mult("Gold rewards", S.GoldMult);
        Mult("Item / rune drops", S.LootMult);
        Mult("Essence gain", S.EssenceMult);
        Section("PRIVILEGES");
        Toggle("Free shop purchases", S.FreeShop);
        Toggle("Free rune / pet / forge upgrades", S.FreeUpgrades);
        Button("Grant 1,000 XP", () => XP.GrantXPToPlayer(Game.LocalPlayer, 1000, "ModMenu", true));
        Button("Grant 1,000 gold", () => FindServerRpc().AddGoldToPlayerServerRpc(1000, default));
    }

    void DrawCombat()
    {
        Toggle("God mode", S.GodMode);
        Toggle("No cooldowns", S.NoCooldowns);
        Toggle("Untargetable", S.Untargetable);
        Toggle("Always critical", S.AlwaysCrit);
        Mult("Damage dealt", S.DamageMult);
        Mult("Damage taken", S.IncomingDamageMult);
        Mult("Monster health", S.MonsterHpMult);
        Mult("Critical damage", S.CritDamageMult);
        Mult("Cooldown length", S.CooldownMult);
        Mult("Movement speed", S.SpeedMult, SpeedSteps);
    }

    void DrawSurvival()
    {
        Toggle("No hunger or thirst", S.NoHunger);
        Toggle("Unlimited tool durability", S.NoToolDurability);
        Stepper($"Farm plots: {(S.FarmPlotLimit.Value > 0 ? S.FarmPlotLimit.Value.ToString() : "Default")}",
            () => S.FarmPlotLimit.Value = Math.Max(0, S.FarmPlotLimit.Value - 5),
            () => S.FarmPlotLimit.Value = (S.FarmPlotLimit.Value > 0 ? S.FarmPlotLimit.Value : DefaultFarmLimit()) + 5);
        Mult("Game speed", S.GameSpeed, SpeedSteps);
        Toggle("Unlimited carry capacity", S.UnlimitedCarry);
        Button("Mature all crops", () =>
        {
            var qa = QAManager.Instance;
            if (qa != null) qa.MatureAllCrops();
            else PlayerBaseManager.Singleton.AdvanceAllFarmGrowth(1_000_000, out _, out _);
        });
        Button("Refill hunger and thirst", () => Game.LocalPlayer.Consumption.ResetHungerThirstToFull());
        Button("Refill health, mana and stamina", () => Game.LocalPlayer.GetComponent<DeveloperMode>().RefillVitals());
        Buttons(("Sleep", () => GetQA(() => QAFood.Instance).Sleep()), ("Rest", () => GetQA(() => QAFood.Instance).Rest()));
        Button("Give shield", () => GetQA(() => QAManager.Instance).GivePlayerShield());
    }

    void DrawUnlocks()
    {
        Button("Unlock all recipes", () => GetQA(() => QABuild.Instance).UnlockAllRecipes());
        Button("Unlock all emotes", () => GetQA(() => QAEmoji.Instance).UnlockAllEmojis());
        Button("Complete Codex", () => GetQA(() => QAItem.Instance).CompleteCodex());
        Button("Unlock all Deed tiers", () => GetQA(() => QADeed.Instance).UnlockAllTiers());
        Button("Reset Deed cooldowns", () => GetQA(() => QADeed.Instance).ClearAllCooldowns());
        Button("Grant 1,000 skill points (risky)", () => GetQA(() => QASkill.Instance).AddThousandSkillPoints());
        Buttons(("Add all passives", () => GetQA(() => QASkill.Instance).AddAllPassives()),
            ("Remove all passives", () => GetQA(() => QASkill.Instance).RemoveAllPassives()));    }

    void DrawItems()
    {
        Picker("Item", ref _item, ItemTypes.Length, ItemTypes[_item].ToString());
        Picker("Amount", ref _amount, Amounts.Length, Amounts[_amount].ToString());
        Button("Give selected item", () =>
        {
            var qa = GetQA(() => QAItem.Instance);
            qa.SelectedItem = ItemTypes[_item];
            qa.Amount = Amounts[_amount];
            qa.SendItemToInventory();
        });
        Button("Fill inventory with random items", () => GetQA(() => QAItem.Instance).FillInventoryRandomly());

        Picker("Rune set", ref _runeSet, RuneSets.Length, RuneSets[_runeSet].ToString());
        Picker("Rune slot", ref _runeSlot, RuneSlots.Length, RuneSlots[_runeSlot].ToString());
        LocalToggle("Generate full rune set", ref _runeFullSet);
        Button("Give selected rune", () =>
        {
            var qa = GetQA(() => QARune.Instance);
            qa.SelectedSet = RuneSets[_runeSet];
            qa.SelectedSlot = RuneSlots[_runeSlot];
            qa.Amount = Amounts[_amount];
            qa.GenerateFullSet = _runeFullSet;
            qa.SendRuneToInventory();
        });

        Picker("Pet", ref _pet, PetTypes.Length, PetTypes[_pet].ToString());
        Button("Give selected pet", () =>
        {
            var qa = GetQA(() => QAPet.Instance);
            qa.SelectedPetType = PetTypes[_pet];
            qa.GivePetToPlayer();
        });
    }

    void DrawWorld()
    {
        Buttons(("Travel to Earlwood", () => GetQA(() => QAManager.Instance).TeleportToEarlwoodWaypoint()),
            ("Travel to Sanctum", () => GetQA(() => QAManager.Instance).TeleportToSanctumWaypoint()));
        Button("Travel to map beacon", () => GetQA(() => QAManager.Instance).TeleportToBeacon());

        var time = Game.LocalPlayer != null ? TimeManager.Singleton?.CurrentGameTime : null;
        WildwoodTheme.Write(Row(20), time != null ? $"World time: {time.Hour:00}:{time.Minute:00}" : "World time: -", WildwoodTheme.Small);
        Buttons(("Morning", () => SetHour(6)), ("Noon", () => SetHour(12)), ("Evening", () => SetHour(18)), ("Night", () => SetHour(0)));
        Picker("Weather", ref _weather, WeatherTypes.Length, WeatherTypes[_weather].ToString());
        Button("Apply weather to every region", () =>
        {
            var wm = WeatherManager.Singleton ?? throw new InvalidOperationException("WeatherManager yok");
            ForceWeather = true;
            foreach (Kingdom k in Enum.GetValues(typeof(Kingdom)))
                if (k != Kingdom.None) wm.ChangeWeather(k, WeatherTypes[_weather]);
        });

        Button("Respawn trees, rocks and plants", () => GetQA(() => QAManager.Instance).RespawnAllHarvestables());
        // Hidden for the first release: no visible effect in game yet (Befr's shop unchanged). Kept for later work.
        if (ShowExperimental) Button("Maximize shops and facilities", MaxAllFacilities);
        Buttons(("Zoom out", () => GetQA(() => QACamera.Instance).StepUp()),
            ("Zoom in", () => GetQA(() => QACamera.Instance).StepDown()),
            ("Reset", () => GetQA(() => QACamera.Instance).ResetToRaceDefault()));
    }

    void DrawTools()
    {
        Picker("Monster", ref _monster, MonsterTypes.Length, MonsterTypes[_monster].ToString());
        LocalToggle("No aggression", ref _monsterNoAggro);
        LocalToggle("Damage immune", ref _monsterImmune);
        Button("Spawn monster near me", () =>
        {
            var qa = GetQA(() => QAMonster.Instance);
            qa.SelectedMonsterType = MonsterTypes[_monster];
            qa.NoAggro = _monsterNoAggro;
            qa.DamageImmune = _monsterImmune;
            // Many enum entries (e.g. plain "Goblin") have no prefab; the game only logs that, so check the result.
            if (qa.SpawnMonsterAtPosition(Game.LocalPlayer.transform.position) == null)
                throw new InvalidOperationException($"{MonsterTypes[_monster]} için prefab yok");
        });
        Buttons(("Spawn group", () => GetQA(() => QAMonster.Instance).SpawnPremadeBrawl()),
            ("Despawn group", () => GetQA(() => QAMonster.Instance).DespawnAllQAMonsters()));
        LocalToggle("Show monster health", ref _monsterInfo);

        WildwoodTheme.Write(Row(20), "CHECKPOINTS", WildwoodTheme.Small);
        Button("Create checkpoint", () =>
        {
            var qa = GetQA(() => QACheckpoint.Instance);
            qa.CreateCheckpoint($"ModMenu_{DateTime.Now:yyyyMMdd_HHmmss}");
            RefreshCheckpoints(qa);
        });
        if (_checkpoints.Length > 0)
        {
            Picker("Checkpoint", ref _checkpoint, _checkpoints.Length, _checkpoints[Math.Min(_checkpoint, _checkpoints.Length - 1)]);
            Button("Load checkpoint (then Continue)", () =>
            {
                // In a world the game only drops to the main menu; the actual copy needs a second call there (see Update).
                if (Game.LocalPlayer != null) _pendingCheckpoint = _checkpoints[_checkpoint];
                GetQA(() => QACheckpoint.Instance).LoadCheckpoint(_checkpoints[_checkpoint]);
            });
        }
        Button("Refresh checkpoints", () => RefreshCheckpoints(GetQA(() => QACheckpoint.Instance)));

        WildwoodTheme.Write(Row(20), "QUESTS  /  Make a checkpoint first", WildwoodTheme.Small);
        Buttons(("Skip tutorial", () => GetQA(() => QAManager.Instance).SkipTutorial()),
            ("Activate all quests", () => GetQA(() => QAManager.Instance).ActivateAllQuests()));
    }

    string[] _checkpoints = Array.Empty<string>();
    string _pendingCheckpoint;
    float _mainMenuSince = -1f;

    /// <summary>Finishes a checkpoint load started in a world, once we're back on the main menu.</summary>
    void FinishCheckpointLoad()
    {
        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (Game.LocalPlayer != null || (nm != null && nm.IsListening)) { _mainMenuSince = -1f; return; }
        if (_mainMenuSince < 0) { _mainMenuSince = Time.unscaledTime; return; }
        if (Time.unscaledTime - _mainMenuSince < 3f) return;

        string name = _pendingCheckpoint;
        _pendingCheckpoint = null;
        _mainMenuSince = -1f;
        Run("Yedek yüklendi, Continue'ya bas", () => GetQA(() => QACheckpoint.Instance).LoadCheckpoint(name));
        _statusUntil = Time.unscaledTime + 15f;
    }

    void RefreshCheckpoints(QACheckpoint qa)
    {
        qa.RefreshList();
        var list = qa.ExistingCheckpoints;
        _checkpoints = new string[list?.Count ?? 0];
        for (int i = 0; i < _checkpoints.Length; i++) _checkpoints[i] = list[i];
        _checkpoint = Math.Max(0, _checkpoints.Length - 1);
    }

    static void SetHour(int hour)
    {
        var tm = TimeManager.Singleton ?? throw new InvalidOperationException("TimeManager yok");
        var now = tm.CurrentGameTime;
        tm.ChangeGameTime(new GameTime(now.Year, now.Month, now.Day, hour, 0));
    }

    static readonly bool ShowExperimental = false;

    void MaxAllFacilities()
    {
        var qa = GetQA(() => QAShop.Instance);
        var fm = FacilityManager.Singleton ?? throw new InvalidOperationException("FacilityManager yok");
        foreach (FacilityType t in Enum.GetValues(typeof(FacilityType)))
        {
            if (t == FacilityType.None) continue;
            var def = fm.GetByType(t);
            if (def != null) qa.SetFacilityLevel(t, def.MaxLevel);
        }
    }

    // ───────────── row helpers ─────────────

    Rect Row(float h)
    {
        float height = h <= 20 ? 24 : 34;
        var r = new Rect(ContentLeft, _y, _window.width - ContentLeft - ContentRight, height);
        _y += height + 5;
        if (h > 20) WildwoodTheme.Fill(r, WildwoodTheme.Row);
        return r;
    }

    void Section(string title)
    {
        var r = Row(20);
        WildwoodTheme.Write(new Rect(r.x + 3, r.y, 135, r.height), title, WildwoodTheme.Section);
        WildwoodTheme.Fill(new Rect(r.x + 141, r.y + 8, r.width - 144, 11), WildwoodSprites.Ornament);
    }

    static int IconFor(string label)
    {
        if (label.Contains("upgrade", StringComparison.OrdinalIgnoreCase) || label.Contains("durability", StringComparison.OrdinalIgnoreCase) || label.Contains("building", StringComparison.OrdinalIgnoreCase)) return 10;
        if (label.Contains("Pet", StringComparison.OrdinalIgnoreCase) || label.Contains("Monster", StringComparison.OrdinalIgnoreCase)) return 8;
        if (label.Contains("Proficiency", StringComparison.OrdinalIgnoreCase) || label.Contains("quest", StringComparison.OrdinalIgnoreCase)) return 9;
        if (label.Contains("Gold", StringComparison.OrdinalIgnoreCase) || label.Contains("shop", StringComparison.OrdinalIgnoreCase)) return 0;
        if (label.Contains("Essence", StringComparison.OrdinalIgnoreCase) || label.Contains("rune", StringComparison.OrdinalIgnoreCase)) return 7;
        if (label.Contains("damage", StringComparison.OrdinalIgnoreCase) || label.Contains("critical", StringComparison.OrdinalIgnoreCase) || label.Contains("God", StringComparison.OrdinalIgnoreCase)) return 1;
        if (label.Contains("cooldown", StringComparison.OrdinalIgnoreCase) || label.Contains("target", StringComparison.OrdinalIgnoreCase)) return 3;
        if (label.Contains("capacity", StringComparison.OrdinalIgnoreCase) || label.Contains("drop", StringComparison.OrdinalIgnoreCase)) return 4;
        if (label.Contains("speed", StringComparison.OrdinalIgnoreCase)) return 6;
        return 2;
    }

    void SwitchContent(Rect r, string label, bool value)
    {
        WildwoodSprites.Icon(new Rect(r.x + 10, r.y + 6, 22, 22), IconFor(label));
        WildwoodTheme.Write(new Rect(r.x + 40, r.y, r.width - 124, r.height), label, WildwoodTheme.Label);
        WildwoodTheme.Write(new Rect(r.xMax - 76, r.y + 4, 68, 26), value ? "◆ ON" : "◇ OFF", value ? WildwoodTheme.OnButton : WildwoodTheme.OffButton);
    }

    void Toggle(string label, ConfigEntry<bool> entry)
    {
        var r = Row(24);
        if (WildwoodTheme.Click(r, "", WildwoodTheme.SettingButton)) entry.Value = !entry.Value;
        SwitchContent(r, label, entry.Value);
    }

    void Button(string label, Action action)
    {
        int kind = WildwoodLabels.ActionKind(label);
        if (WildwoodTheme.Click(Row(24), kind == 2 ? "!  " + label : label, ActionStyle(kind))) Run(label, action);
    }

    static GUIStyle ActionStyle(int kind) => kind == 2 ? WildwoodTheme.DangerAction
        : kind == 1 ? WildwoodTheme.PrimaryAction : WildwoodTheme.Action;

    /// <summary>Several buttons sharing one row.</summary>
    void Buttons(params (string label, Action action)[] buttons)
    {
        var r = Row(24);
        float w = r.width / buttons.Length;
        for (int i = 0; i < buttons.Length; i++)
        {
            int kind = WildwoodLabels.ActionKind(buttons[i].label);
            if (WildwoodTheme.Click(new Rect(r.x + i * w, r.y, w - 5, r.height), kind == 2 ? "!  " + buttons[i].label : buttons[i].label,
                ActionStyle(kind))) Run(buttons[i].label, buttons[i].action);
        }
    }

    void LocalToggle(string label, ref bool value)
    {
        var r = Row(24);
        if (WildwoodTheme.Click(r, "", WildwoodTheme.SettingButton)) value = !value;
        SwitchContent(r, label, value);
    }

    /// <summary>Cycles an index with big (±10) and small (±1) steps; wraps around.</summary>
    void Picker(string label, ref int index, int count, string text)
    {
        var r = Row(24);
        string display = label + ": " + (label == "Checkpoint" || label == "Amount" ? text : WildwoodLabels.Friendly(text));
        WildwoodTheme.Write(new Rect(r.x + 10, r.y, r.width - 146, r.height), display,
            display.Length > 38 ? WildwoodTheme.Small : WildwoodTheme.Label);
        int step = 0;
        if (WildwoodTheme.Click(new Rect(r.xMax - 132, r.y + 3, 30, 28), "<<", WildwoodTheme.SmallButton)) step = -10;
        if (WildwoodTheme.Click(new Rect(r.xMax - 99, r.y + 3, 30, 28), "<", WildwoodTheme.SmallButton)) step = -1;
        if (WildwoodTheme.Click(new Rect(r.xMax - 66, r.y + 3, 30, 28), ">", WildwoodTheme.SmallButton)) step = 1;
        if (WildwoodTheme.Click(new Rect(r.xMax - 33, r.y + 3, 30, 28), ">>", WildwoodTheme.SmallButton)) step = 10;
        if (count > 0) index = ((index + step) % count + count) % count;
    }

    void Mult(string label, ConfigEntry<float> entry, float[] steps = null)
    {
        steps ??= Steps;
        Stepper($"{label}: {entry.Value.ToString("0.0#", CultureInfo.InvariantCulture)}x",
            () => entry.Value = StepTo(steps, entry.Value, -1),
            () => entry.Value = StepTo(steps, entry.Value, +1));
    }

    void Stepper(string text, Action dec, Action inc)
    {
        var r = Row(24);
        int split = text.LastIndexOf(": ", StringComparison.Ordinal);
        string label = split >= 0 ? text[..split] : text;
        string number = split >= 0 ? text[(split + 2)..] : "";
        WildwoodSprites.Icon(new Rect(r.x + 10, r.y + 6, 22, 22), IconFor(label));
        WildwoodTheme.Write(new Rect(r.x + 40, r.y, r.width - 195, r.height), label, WildwoodTheme.Label);
        WildwoodTheme.Fill(new Rect(r.xMax - 112, r.y + 3, 76, 28), WildwoodTheme.Value);
        WildwoodTheme.Write(new Rect(r.xMax - 112, r.y + 3, 76, 28), number, WildwoodTheme.Number);
        if (WildwoodTheme.Click(new Rect(r.xMax - 146, r.y + 3, 28, 28), "−", WildwoodTheme.SmallButton)) dec();
        if (WildwoodTheme.Click(new Rect(r.xMax - 32, r.y + 3, 30, 28), "+", WildwoodTheme.SmallButton)) inc();
    }

    static float StepTo(float[] steps, float current, int dir)
    {
        if (dir > 0) { foreach (var s in steps) if (s > current + 0.001f) return s; return steps[^1]; }
        for (int i = steps.Length - 1; i >= 0; i--) if (steps[i] < current - 0.001f) return steps[i];
        return steps[0];
    }
}
