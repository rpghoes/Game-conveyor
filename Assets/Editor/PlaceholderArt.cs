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
    public const string Guard = "Guard";                  // 48x64 factory security robot, faces RIGHT, feet at the bottom
    public const string ScanCone = "Scan_Cone";           // 32x64 white cone, apex at the top centre (scanner beam)
    public const string ServiceDoor = "Service_Door";     // 32x64 door in the wall (guard spawn point)

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
        wrote |= Ensure(Guard, 48, 64, DrawGuard);
        wrote |= Ensure(ScanCone, 32, 64, DrawScanCone);
        wrote |= Ensure(ServiceDoor, 32, 64, DrawServiceDoor);
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

    // Guard: boxy security robot (head with a red visor, striped torso, short legs, baton in the front hand)
    static void DrawGuard(Canvas c)
    {
        Color outline = Hex("15161a");
        // legs
        c.Rect(14, 0, 8, 4, outline); c.Rect(27, 0, 8, 4, outline);
        c.Rect(15, 2, 6, 14, Steel); c.Rect(28, 2, 6, 14, Steel);
        c.Rect(15, 2, 6, 2, SteelDark); c.Rect(28, 2, 6, 2, SteelDark);
        // torso
        c.Rect(10, 15, 28, 26, outline);
        c.Rect(11, 16, 26, 24, Steel);
        c.Rect(11, 36, 26, 4, SteelLight);
        for (int x = 11; x < 37; x++)
            for (int y = 18; y < 24; y++)
                if (((x + y) / 3) % 2 == 0) c.Set(x, y, Hazard);
        c.Rect(20, 27, 8, 6, SteelDark); c.Rect(22, 29, 4, 2, Hex("ff5030"));
        // head with a visor (faces right)
        c.Rect(14, 41, 20, 16, outline);
        c.Rect(15, 42, 18, 14, SteelLight);
        c.Rect(22, 47, 12, 5, outline);
        c.Rect(23, 48, 10, 3, Hex("ff3a2a"));
        c.Rect(18, 57, 2, 4, SteelDark); c.Circle(19, 61, 1, Hex("ff3a2a"));
        // back arm
        c.Rect(7, 22, 5, 14, SteelDark);
        // front arm + baton
        c.Rect(36, 24, 5, 12, Steel);
        c.Rect(39, 20, 4, 4, SteelDark);
        c.Rect(40, 24, 3, 18, Hex("2b2b2b"));
        c.Rect(40, 40, 3, 3, Hex("9fd8ff"));
    }

    // Scanner beam: a cone that is brightest near the lens and fades out at the far end
    static void DrawScanCone(Canvas c)
    {
        for (int y = 0; y < 64; y++)
        {
            float fromTop = (63 - y) / 63f;             // 0 at the apex, 1 at the far end
            float half = 1f + fromTop * 15f;
            for (int x = 0; x < 32; x++)
            {
                float d = Mathf.Abs(x + 0.5f - 16f);
                if (d > half) continue;
                float edge = d > half - 1.5f ? 1f : 0.55f;          // brighter edges
                float a = edge * Mathf.Lerp(1f, 0.25f, fromTop);
                if (y % 4 == 0) a *= 0.7f;                          // scan lines
                c.Set(x, y, new Color(1f, 1f, 1f, a));
            }
        }
    }

    // Service door: steel frame, two panels, a lamp slot on top (the lamp itself is a separate sprite)
    static void DrawServiceDoor(Canvas c)
    {
        c.Rect(0, 0, 32, 64, SteelDark);
        c.Rect(3, 0, 26, 58, Steel);
        c.Rect(15, 0, 2, 58, SteelDark);
        for (int y = 6; y < 54; y += 12) { c.Rect(5, y, 8, 2, SteelLight); c.Rect(19, y, 8, 2, SteelLight); }
        for (int x = 3; x < 29; x++)
            for (int y = 0; y < 5; y++)
                if (((x + y) / 3) % 2 == 0) c.Set(x, y, Hazard);
        c.Rect(12, 59, 8, 4, Hex("15161a"));
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
