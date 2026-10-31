using System;
using System.IO;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace DimraethModMenu;

// Presentation-only atlas importer. Native sprites are extracted once, never in the frame loop.
internal static class WildwoodSprites
{
    internal static readonly Texture2D[] Tiles = new Texture2D[20];
    internal static readonly GUIStyle[] Icons = new GUIStyle[12];
    internal static GUIStyle Ornament;
    internal static bool Ready { get; private set; }
    // Authored sprite regions in the 1122 x 1402 atlas. Decorative tips cross the nominal grid;
    // explicit regions preserve those tips and exclude neighbouring buttons/icons.
    static readonly int[,] Regions = {
        { 20, 120, 275, 163 }, { 295, 120, 264, 163 }, { 560, 150, 273, 114 }, { 840, 150, 266, 114 },
        { 20, 408, 280, 84 }, { 335, 380, 207, 131 }, { 617, 369, 156, 150 }, { 827, 406, 278, 76 },
        { 55, 600, 208, 198 }, { 332, 585, 211, 213 }, { 597, 581, 205, 215 }, { 871, 587, 199, 211 },
        { 47, 846, 219, 224 }, { 328, 846, 220, 220 }, { 591, 846, 206, 218 }, { 882, 846, 180, 218 },
        { 60, 1126, 194, 184 }, { 315, 1126, 236, 184 }, { 587, 1094, 216, 231 }, { 874, 1094, 196, 231 }
    };

    internal static Texture2D Load(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DimraethModMenu." + name);
        if (stream == null) throw new InvalidOperationException("Embedded UI asset missing: " + name);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, new Il2CppStructArray<byte>(bytes.ToArray())))
        { UnityEngine.Object.Destroy(texture); throw new InvalidOperationException("PNG decoding failed: " + name); }
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        return texture;
    }

    internal static Texture2D LoadTrimmed(string name)
    {
        var source = Load(name);
        try
        {
            int width = source.width, height = source.height;
            var pixels = source.GetPixels32();
            int x0 = width, y0 = height, x1 = -1, y1 = -1;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (pixels[y * width + x].a > 20)
                    { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
            if (x1 < x0) throw new InvalidOperationException("Empty UI sprite: " + name);
            int w = x1 - x0 + 3, h = y1 - y0 + 3;
            var data = new Color32[w * h];
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++) data[(y - y0 + 1) * w + x - x0 + 1] = pixels[y * width + x];
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(new Il2CppStructArray<Color32>(data)); texture.Apply(false, true);
            return texture;
        }
        finally { UnityEngine.Object.Destroy(source); }
    }

    internal static void Initialize()
    {
        if (Ready) return;
        var atlas = Load("WildwoodAtlasV2.png");
        try
        {
            var pixels = atlas.GetPixels32();
            int width = atlas.width, height = atlas.height;
            for (int index = 0; index < Tiles.Length; index++)
                {
                    int left = Regions[index, 0] * width / 1122;
                    int right = (Regions[index, 0] + Regions[index, 2]) * width / 1122;
                    // Authored regions start at the top; Unity pixels start at the bottom.
                    int bottom = height - (Regions[index, 1] + Regions[index, 3]) * height / 1402;
                    int top = height - Regions[index, 1] * height / 1402;
                    int x0 = right, x1 = left, y0 = top, y1 = bottom;
                    for (int y = bottom + 2; y < top - 2; y++)
                        for (int x = left + 2; x < right - 2; x++)
                            if (pixels[y * width + x].a > 20)
                            { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
                    if (x1 < x0 || y1 < y0) throw new InvalidOperationException("Empty UI atlas tile: " + index);
                    int w = x1 - x0 + 3, h = y1 - y0 + 3;
                    var tilePixels = new Color32[w * h];
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                            tilePixels[(y - y0 + 1) * w + x - x0 + 1] = pixels[y * width + x];
                    var tile = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    tile.hideFlags = HideFlags.HideAndDontSave;
                    tile.filterMode = FilterMode.Bilinear;
                    tile.wrapMode = TextureWrapMode.Clamp;
                    tile.SetPixels32(new Il2CppStructArray<Color32>(tilePixels));
                    tile.Apply(false, true);
                    Tiles[index] = tile;
                }
            for (int i = 0; i < Icons.Length; i++) Icons[i] = Surface(Tiles[8 + i]);
            Ornament = Surface(Tiles[7]);
            Ready = true;
            Plugin.Logger.LogInfo("[Wildwood] V2 atlas ready: 20 isolated sprites; textures cached.");
        }
        finally { UnityEngine.Object.Destroy(atlas); }
    }

    internal static GUIStyle Surface(Texture2D texture, int border = 0, int verticalBorder = -1)
    {
        var style = new GUIStyle(GUI.skin.label);
        style.padding = new RectOffset(0, 0, 0, 0);
        int vertical = verticalBorder < 0 ? border : verticalBorder;
        style.border = new RectOffset(border, border, vertical, vertical);
        style.normal.background = texture;
        return style;
    }

    internal static void Icon(Rect rect, int icon)
    {
        if (!Ready || !WildwoodTheme.Ready || icon < 0 || icon >= Icons.Length) return;
        var texture = Tiles[8 + icon];
        float fit = Mathf.Min(rect.width / texture.width, rect.height / texture.height);
        float w = texture.width * fit, h = texture.height * fit;
        GUI.Label(new Rect(rect.x + (rect.width - w) * .5f, rect.y + (rect.height - h) * .5f, w, h), "", Icons[icon]);
    }
}
