using System.IO;
using UnityEditor;
using UnityEngine;

// EDITOR-ONLY TOOL (not part of the game).
// Draws simple pixel-art PLACEHOLDER sprites for CONVEYOR objects that have no real art yet
// (conveyor belt, industrial press, furnace...). Files are written to Assets/Art/Placeholders/ and are
// NEVER overwritten: to use real art, replace the PNG with a file of the same name (and size ratio)
// and run CONVEYOR > 1. Setup Project again.
public static class PlaceholderArt
{
    public const string Folder = "Assets/Art/Placeholders/";

    // name -> (width, height) in pixels at 32 px per world unit
    public const string ConveyorBelt = "Conveyor_Belt";   // 32x32, tiled horizontally
    public const string PressHead = "Press_Head";         // 96x48, stretched to the press width
    public const string PressRod = "Press_Rod";           // 16x32, tiled vertically
    public const string Furnace = "Furnace";              // 128x128
    public const string ShadowBlob = "Shadow_Blob";       // 64x16 soft black ellipse (press warning shadow)
    public const string LampDot = "Lamp_Dot";            // 8x8 white dot (warning lamps), tinted in code

    // Palette: old factory metal, rust, hazard yellow
    static readonly Color Steel = Hex("4a4f57");
    static readonly Color SteelLight = Hex("6b727c");
    static readonly Color SteelDark = Hex("2a2d33");
    static readonly Color Rubber = Hex("1b1c1f");
    static readonly Color RubberLight = Hex("33353a");
    static readonly Color Rust = Hex("7a4a2a");
    static readonly Color Hazard = Hex("e0b030");
    static readonly Color Brick = Hex("4a2e26");
    static readonly Color BrickDark = Hex("2e1c17");
    static readonly Color Glow = Hex("ff8a2a");
    static readonly Color GlowHot = Hex("ffd27a");

    // Creates every missing placeholder. Returns true if a file was written.
    public static bool EnsureAll()
    {
        Directory.CreateDirectory(Folder);
        bool wrote = false;
        wrote |= Ensure(ConveyorBelt, 32, 32, DrawBelt);
        wrote |= Ensure(PressHead, 96, 48, DrawPressHead);
        wrote |= Ensure(PressRod, 16, 32, DrawPressRod);
        wrote |= Ensure(Furnace, 128, 128, DrawFurnace);
        wrote |= Ensure(ShadowBlob, 64, 16, DrawShadow);
        wrote |= Ensure(LampDot, 8, 8, c => c.Circle(4, 4, 3, Color.white));
        if (wrote) AssetDatabase.Refresh();
        return wrote;
    }

    public static string PathOf(string name) => Folder + name + ".png";

    static bool Ensure(string name, int w, int h, System.Action<Canvas> draw)
    {
        string path = PathOf(name);
        if (File.Exists(path)) return false;
        var canvas = new Canvas(w, h);
        draw(canvas);
        File.WriteAllBytes(path, canvas.EncodePNG());
        return true;
    }

    // ---------------- Drawings ----------------

    // Belt tile: rubber top with tread marks, steel frame with a roller in the middle
    static void DrawBelt(Canvas c)
    {
        c.Rect(0, 0, 32, 32, SteelDark);
        c.Rect(0, 4, 32, 20, Steel);
        c.Rect(0, 22, 32, 2, SteelLight);
        c.Circle(16, 13, 7, SteelDark);
        c.Circle(16, 13, 5, SteelLight);
        c.Circle(16, 13, 2, SteelDark);
        c.Rect(0, 24, 32, 8, Rubber);
        for (int x = 2; x < 32; x += 8) c.Rect(x, 26, 4, 4, RubberLight);
        c.Rect(0, 31, 32, 1, RubberLight);
        c.Rect(0, 0, 32, 2, Rust);
    }

    // Press head: heavy steel block, bolts, hazard stripes along the bottom edge
    static void DrawPressHead(Canvas c)
    {
        c.Rect(0, 0, 96, 48, SteelDark);
        c.Rect(2, 10, 92, 36, Steel);
        c.Rect(2, 44, 92, 2, SteelLight);
        for (int x = 8; x < 96; x += 20) { c.Circle(x, 36, 2, SteelLight); c.Circle(x, 20, 2, SteelLight); }
        c.Rect(36, 14, 24, 28, SteelDark);
        c.Rect(38, 16, 20, 24, Rust);
        for (int x = 0; x < 96; x += 12)
            for (int y = 0; y < 10; y++)
                for (int k = 0; k < 6; k++)
                {
                    int px = x + k + y;
                    if (px < 96) c.Set(px, y, Hazard);
                }
    }

    // Piston rod: chrome cylinder with a dark seam (tiles vertically)
    static void DrawPressRod(Canvas c)
    {
        c.Rect(0, 0, 16, 32, SteelDark);
        c.Rect(2, 0, 12, 32, SteelLight);
        c.Rect(5, 0, 3, 32, Hex("9aa1ab"));
        c.Rect(2, 0, 12, 1, SteelDark);
    }

    // Furnace mouth: brick wall with a glowing arched opening
    static void DrawFurnace(Canvas c)
    {
        c.Rect(0, 0, 128, 128, Brick);
        for (int y = 0; y < 128; y += 8)
        {
            c.Rect(0, y, 128, 1, BrickDark);
            int offset = (y / 8) % 2 == 0 ? 0 : 8;
            for (int x = offset; x < 128; x += 16) c.Rect(x, y, 1, 8, BrickDark);
        }
        // Arch opening with a vertical heat gradient
        for (int y = 0; y < 100; y++)
        {
            for (int x = 20; x < 108; x++)
            {
                float dx = (x - 64) / 44f;
                float top = y > 60 ? (y - 60) / 40f : 0f;
                if (dx * dx + top * top > 1f) continue;
                float heat = 1f - y / 100f;
                Color col = Color.Lerp(Glow, GlowHot, heat * heat);
                if ((x + y * 3) % 7 == 0) col = Color.Lerp(col, Color.white, 0.25f);
                c.Set(x, y, col);
            }
        }
        c.Rect(16, 100, 96, 6, SteelDark);
        c.Rect(16, 106, 96, 2, Rust);
    }

    // Soft ellipse: opaque in the middle, fading to the edge
    static void DrawShadow(Canvas c)
    {
        for (int x = 0; x < 64; x++)
            for (int y = 0; y < 16; y++)
            {
                float dx = (x + 0.5f - 32f) / 32f, dy = (y + 0.5f - 8f) / 8f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                c.Set(x, y, new Color(0f, 0f, 0f, Mathf.Clamp01((1f - d) * 2.5f)));
            }
    }

    // ---------------- Tiny canvas ----------------

    class Canvas
    {
        readonly int w, h;
        readonly Color[] pixels;

        public Canvas(int w, int h)
        {
            this.w = w;
            this.h = h;
            pixels = new Color[w * h];
        }

        public void Set(int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            pixels[y * w + x] = c;
        }

        public void Rect(int x, int y, int rw, int rh, Color c)
        {
            for (int i = x; i < x + rw; i++)
                for (int j = y; j < y + rh; j++) Set(i, j, c);
        }

        public void Circle(int cx, int cy, int r, Color c)
        {
            for (int i = -r; i <= r; i++)
                for (int j = -r; j <= r; j++)
                    if (i * i + j * j <= r * r) Set(cx + i, cy + j, c);
        }

        public byte[] EncodePNG()
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            return png;
        }
    }

    static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }
}
