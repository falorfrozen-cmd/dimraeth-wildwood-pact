using System;
using System.IO;
using System.Text.Json;
using BepInEx;

namespace DimraethModMenu;

// Menu presentation only. Kept separate from gameplay settings and from Unity PlayerPrefs.
internal static class WildwoodPreferences
{
    internal sealed class State
    {
        public State() { }
        public int Version { get; set; } = 1;
        public float Scale { get; set; } = 1f;
        public float PositionX { get; set; } = .5f;
        public float PositionY { get; set; } = .5f;
    }

    static State _state = new();
    static bool _loaded, _dirty, _warned;
    internal static float Scale => _state.Scale;
    internal static float PositionX => _state.PositionX;
    internal static float PositionY => _state.PositionY;
    internal static string FilePath => Path.Combine(Paths.ConfigPath, "falor.dimraeth.wildwood-ui.json");

    static float Finite(float value, float fallback, float min, float max) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    internal static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(FilePath)) return;
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath));
            if (state == null || state.Version != 1) throw new InvalidDataException("Unsupported UI preference version.");
            state.Scale = Finite(state.Scale, 1, .8f, 1.5f);
            state.PositionX = Finite(state.PositionX, .5f, 0, 1);
            state.PositionY = Finite(state.PositionY, .5f, 0, 1);
            _state = state;
        }
        catch (Exception ex) { Warn(ex); }
    }

    internal static void SetScale(float value)
    {
        Load();
        value = Finite(value, 1, .8f, 1.5f);
        if (Math.Abs(value - Scale) < .0001f) return;
        _state.Scale = value; _dirty = true;
    }

    internal static void SetPosition(float x, float y)
    {
        Load();
        x = Finite(x, .5f, 0, 1); y = Finite(y, .5f, 0, 1);
        if (Math.Abs(x - PositionX) < .0001f && Math.Abs(y - PositionY) < .0001f) return;
        _state.PositionX = x; _state.PositionY = y; _dirty = true;
    }

    internal static void Reset()
    { _state = new State(); _loaded = true; _dirty = true; }

    internal static void Save()
    {
        if (!_dirty) return;
        try
        {
            Directory.CreateDirectory(Paths.ConfigPath);
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FilePath, true);
            _dirty = false;
        }
        catch (Exception ex) { Warn(ex); }
    }

    static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.Logger.LogWarning("[Wildwood] UI preferences: " + ex.GetBaseException().Message);
    }
}
