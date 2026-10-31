using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace DimraethModMenu;

// Presentation only. No gameplay hooks, settings, input polling or background work.
// Textures and native GUIStyles are created once, inside OnGUI, and reused.
internal static class WildwoodTheme
{
    internal static bool Ready { get; private set; }
    internal static bool FrameReady { get; private set; }
    internal static bool ActionArtworkReady { get; private set; }
    static bool _attempted;
    internal static GUIStyle Window, Body, Frame, Sidebar, Divider, Row, Value;
    internal static GUIStyle Title, Subtitle, Heading, Section, Label, Small, Muted, Right, Footer, Number;
    internal static GUIStyle Tab, SelectedTab, Action, PrimaryAction, DangerAction, SmallButton, SettingButton, OnButton, OffButton;
    internal static readonly Color Ivory = Rgb(248, 243, 226);
    internal static readonly Color Sage = Rgb(185, 205, 168);
    internal static readonly Color Gold = Rgb(235, 192, 112);

    internal static bool Ensure()
    {
        if (_attempted) return Ready;
        _attempted = true;
        try
        {
            // The source skin is never mutated: other game windows retain their styles.
            Title = Text(29, Ivory, TextAnchor.MiddleCenter);
            Subtitle = Text(11, Sage, TextAnchor.MiddleCenter);
            Heading = Text(27, Ivory, TextAnchor.MiddleLeft);
            Section = Text(13, Sage, TextAnchor.MiddleLeft, true);
            Label = Text(15, Ivory, TextAnchor.MiddleLeft);
            Small = Text(13, Sage, TextAnchor.MiddleLeft);
            Muted = Text(12, Sage, TextAnchor.MiddleLeft);
            Right = Text(12, Sage, TextAnchor.MiddleRight);
            Footer = Text(11, Sage, TextAnchor.MiddleCenter);
            Number = Text(14, Gold, TextAnchor.MiddleCenter, true);
            Window = new GUIStyle(GUI.skin.window);
            Window.padding = new RectOffset(0, 0, 0, 0);
            Window.border = new RectOffset(0, 0, 0, 0);
            var clear = Texture(new Color(0, 0, 0, 0), new Color(0, 0, 0, 0));
            States(Window, clear, clear, clear, Ivory);
            Body = Panel(Rgb(13, 34, 28), Rgb(13, 34, 28));
            Sidebar = Panel(Rgb(18, 42, 31), Rgb(45, 69, 42));
            Row = Panel(Rgb(19, 43, 34), Rgb(52, 72, 45));
            Value = Panel(Rgb(10, 29, 24), Rgb(84, 106, 67));
            Divider = Panel(Rgb(92, 113, 64), Rgb(92, 113, 64));
            Tab = Button(12, Rgb(25, 52, 35), Rgb(87, 106, 54), Sage);
            SelectedTab = Button(12, Rgb(48, 66, 32), Gold, Ivory);
            Action = Button(13, Rgb(25, 49, 34), Rgb(95, 118, 66), Ivory);
            PrimaryAction = Button(13, Rgb(55, 47, 28), Gold, Ivory);
            DangerAction = Button(13, Rgb(67, 28, 29), Rgb(168, 89, 64), Ivory);
            SmallButton = Button(15, Rgb(27, 50, 34), Rgb(109, 124, 65), Ivory);
            OnButton = Button(12, Rgb(45, 83, 45), Gold, Ivory);
            OffButton = Button(12, Rgb(28, 41, 31), Rgb(82, 96, 60), Sage);
            SettingButton = Button(11, Rgb(19, 43, 34), Rgb(52, 72, 45), Ivory);
            Ready = true;
            Plugin.Logger.LogInfo("[Wildwood] Native GUI styles initialized; no GUILayout or slider/text-field calls.");
        }
        catch (Exception ex)
        {
            Disable(ex);
            return false;
        }
        try
        {
            var texture = WildwoodSprites.Load("WildwoodFrameV3.png");
            texture.Apply(false, true);
            Frame = WildwoodSprites.Surface(texture);
            FrameReady = true;
            Plugin.Logger.LogInfo($"[Wildwood] Embedded rootwood frame loaded: {texture.width} x {texture.height}.");
        }
        catch (Exception ex)
        {
            // Keep the working themed controls if an optional decorative texture fails.
            Plugin.Logger.LogWarning("[Wildwood] Frame decoration unavailable: " + ex.GetBaseException().Message);
        }
        try
        {
            WildwoodSprites.Initialize();
            var tiles = WildwoodSprites.Tiles;
            Tab = ArtButton(15, tiles[0], tiles[1], tiles[1], Sage, 0);
            SelectedTab = ArtButton(15, tiles[1], tiles[1], tiles[0], Ivory, 0);
            Tab.alignment = SelectedTab.alignment = TextAnchor.MiddleLeft;
            Tab.padding = new RectOffset(38, 10, 0, 0);
            SelectedTab.padding = new RectOffset(38, 10, 0, 0);
            Action = ArtButton(13, tiles[4], tiles[5], tiles[4], Ivory, 12);
            PrimaryAction = ArtButton(13, tiles[2], tiles[3], tiles[2], Ivory, 10);
            var danger = WildwoodSprites.LoadTrimmed("WildwoodDangerV3.png");
            DangerAction = ArtButton(13, danger, danger, danger, Rgb(255, 224, 205), 12);
            SmallButton = ArtButton(15, tiles[6], tiles[6], tiles[5], Ivory, 7);
            Row = WildwoodSprites.Surface(tiles[4], 12, 2);
            Value = WildwoodSprites.Surface(tiles[5], 8, 3);
            SettingButton = ArtButton(11, tiles[4], tiles[4], tiles[5], Ivory, 8);
            OnButton = ArtButton(12, tiles[5], tiles[5], tiles[5], Gold, 7);
            OffButton = ArtButton(12, tiles[5], tiles[5], tiles[5], Sage, 7);
            ActionArtworkReady = true;
            Plugin.Logger.LogInfo("[Wildwood] V3 normal, primary and danger action artwork initialized.");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[Wildwood] Sprite controls unavailable; styled basic controls retained: " + ex.GetBaseException().Message);
        }
        try
        {
            // The string overload is stripped in this build; use the preserved native array overload.
            var font = Font.CreateDynamicFontFromOSFont(new Il2CppStringArray(new[] { "Georgia", "Palatino Linotype", "Times New Roman" }), 26);
            if (font != null)
            {
                font.hideFlags = HideFlags.HideAndDontSave;
                Title.font = Heading.font = Section.font = Tab.font = SelectedTab.font = font;
                Plugin.Logger.LogInfo("[Wildwood] Native serif display font initialized.");
            }
        }
        catch (Exception ex) { Plugin.Logger.LogWarning("[Wildwood] Using the game font: " + ex.GetBaseException().Message); }
        return Ready;
    }

    internal static void Disable(Exception ex)
    {
        Ready = false;
        FrameReady = false;
        Plugin.Logger.LogWarning("[Wildwood] Native styling disabled; basic controls remain available: " + ex.GetBaseException().Message);
    }

    static Color Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1);

    static GUIStyle Text(int size, Color color, TextAnchor alignment, bool bold = false)
    {
        var style = new GUIStyle(GUI.skin.label);
        style.fontSize = size;
        style.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        style.alignment = alignment;
        style.wordWrap = false;
        style.clipping = TextClipping.Clip;
        style.padding = new RectOffset(0, 0, 0, 0);
        style.normal.textColor = color;
        return style;
    }

    static Texture2D Texture(Color fill, Color border)
    {
        const int size = 8;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.hideFlags = HideFlags.HideAndDontSave;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                var color = edge ? border : fill;
                if (!edge && color.a > 0)
                {
                    float grain = ((x * 7 + y * 3) % 5 - 2) / 255f;
                    color.r += grain; color.g += grain; color.b += grain;
                }
                texture.SetPixel(x, y, color);
            }
        texture.Apply();
        return texture;
    }

    static GUIStyle Panel(Color fill, Color border)
    {
        var style = new GUIStyle(GUI.skin.label);
        style.normal.background = Texture(fill, border);
        style.border = new RectOffset(1, 1, 1, 1);
        style.padding = new RectOffset(0, 0, 0, 0);
        return style;
    }

    static GUIStyle Button(int size, Color fill, Color border, Color text)
    {
        var style = new GUIStyle(GUI.skin.button);
        style.fontSize = size;
        style.alignment = TextAnchor.MiddleCenter;
        style.wordWrap = false;
        style.clipping = TextClipping.Clip;
        style.border = new RectOffset(1, 1, 1, 1);
        style.padding = new RectOffset(5, 5, 0, 0);
        var hover = new Color(fill.r + .055f, fill.g + .07f, fill.b + .025f, 1);
        var pressed = new Color(fill.r * .75f, fill.g * .75f, fill.b * .75f, 1);
        States(style, Texture(fill, border), Texture(hover, Gold), Texture(pressed, Gold), text);
        return style;
    }

    static GUIStyle ArtButton(int size, Texture2D normal, Texture2D hover, Texture2D active, Color text, int border)
    {
        var style = new GUIStyle(GUI.skin.button);
        style.fontSize = size;
        style.alignment = TextAnchor.MiddleCenter;
        style.wordWrap = false;
        style.clipping = TextClipping.Clip;
        int vertical = Math.Min(border, 3);
        style.border = new RectOffset(border, border, vertical, vertical);
        style.padding = new RectOffset(8, 8, 0, 0);
        States(style, normal, hover, active, text);
        return style;
    }

    static void States(GUIStyle style, Texture2D normal, Texture2D hover, Texture2D active, Color text)
    {
        style.normal.background = normal; style.normal.textColor = text;
        style.hover.background = hover; style.hover.textColor = Ivory;
        style.active.background = active; style.active.textColor = Gold;
        style.focused.background = hover; style.focused.textColor = Ivory;
        style.onNormal.background = normal; style.onNormal.textColor = text;
        style.onHover.background = hover; style.onHover.textColor = Ivory;
        style.onActive.background = active; style.onActive.textColor = Gold;
        style.onFocused.background = hover; style.onFocused.textColor = Ivory;
    }

    internal static void Fill(Rect rect, GUIStyle style)
    { if (Ready && style != null) GUI.Label(rect, "", style); }

    internal static void Write(Rect rect, string text, GUIStyle style)
    { if (Ready) GUI.Label(rect, text, style); else GUI.Label(rect, text); }

    internal static bool Click(Rect rect, string text, GUIStyle style)
    { return Ready ? GUI.Button(rect, text, style) : GUI.Button(rect, text); }
}
