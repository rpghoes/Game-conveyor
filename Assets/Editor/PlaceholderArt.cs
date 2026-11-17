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
    public const string ForemanClaw = "Foreman_Claw";     // 64x48 manipulator head of the Foreman (hammer + claws, pointing down)
    public const string DirectorCalm = "Director_Calm";   // 160x96 monitor with the Director's face (3 expressions)
    public const string DirectorAngry = "Director_Angry";
    public const string DirectorBroken = "Director_Broken";
    public const string ControlPanel = "Control_Panel";   // 64x48 control desk (final target)
    public const string PanelShield = "Panel_Shield";     // 64x48 armour bars over the control panel
    public const string WallUnit = "Wall_Unit";           // 48x96 wall manipulator housing, facing right
    public const string CrusherWall = "Crusher_Wall";     // 32x32 tile of the moving crusher walls
    public const string WhitePixel = "White_Pixel";       // 4x4 white (light overlays)
    public const string ItemRepair = "Item_RepairKit";    // 24x24 item icons
    public const string ItemShield = "Item_Shield";
    public const string ItemOverdrive = "Item_Overdrive";
    public const string ItemModule = "Item_Module";
    public const string GlowSoft = "Glow_Soft";           // 32x32 soft white glow (item halo)
    public const string ShieldBubble = "Shield_Bubble";   // 64x64 ring (energy shield around the Robot)

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
        wrote |= Ensure(ForemanClaw, 64, 48, DrawForemanClaw);
        wrote |= Ensure(DirectorCalm, 160, 96, c => DrawDirector(c, 0));
        wrote |= Ensure(DirectorAngry, 160, 96, c => DrawDirector(c, 1));
        wrote |= Ensure(DirectorBroken, 160, 96, c => DrawDirector(c, 2));
        wrote |= Ensure(ControlPanel, 64, 48, DrawControlPanel);
        wrote |= Ensure(PanelShield, 64, 48, DrawPanelShield);
        wrote |= Ensure(WallUnit, 48, 96, DrawWallUnit);
        wrote |= Ensure(CrusherWall, 32, 32, DrawCrusherWall);
        wrote |= Ensure(WhitePixel, 4, 4, c => c.Rect(0, 0, 4, 4, Color.white));
        wrote |= Ensure(ItemRepair, 24, 24, DrawItemRepair);
        wrote |= Ensure(ItemShield, 24, 24, DrawItemShield);
        wrote |= Ensure(ItemOverdrive, 24, 24, DrawItemOverdrive);
        wrote |= Ensure(ItemModule, 24, 24, DrawItemModule);
        wrote |= Ensure(GlowSoft, 32, 32, c => Radial(c, 16f, d => Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d)));
        wrote |= Ensure(ShieldBubble, 64, 64, c => Radial(c, 32f, d => d > 1f ? 0f : Mathf.Clamp01(1f - Mathf.Abs(d - 0.88f) * 9f) + 0.15f * d));
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

    // Foreman manipulator: a heavy hammer block with two claws, hazard stripes, a socket for the cable on top
    static void DrawForemanClaw(Canvas c)
    {
        Color outline = Hex("15161a");
        c.Rect(26, 38, 12, 10, outline); c.Rect(28, 38, 8, 10, SteelLight);      // socket
        c.Rect(6, 16, 52, 24, outline); c.Rect(8, 18, 48, 20, Steel);           // hammer block
        c.Rect(8, 34, 48, 4, SteelLight);
        for (int x = 8; x < 56; x++)
            for (int y = 18; y < 24; y++)
                if (((x + y) / 4) % 2 == 0) c.Set(x, y, Hazard);
        c.Circle(14, 30, 2, SteelDark); c.Circle(50, 30, 2, SteelDark);
        // claws
        c.Rect(4, 0, 10, 18, outline); c.Rect(6, 2, 6, 16, SteelDark);
        c.Rect(50, 0, 10, 18, outline); c.Rect(52, 2, 6, 16, SteelDark);
        c.Rect(6, 0, 4, 3, Rust); c.Rect(54, 0, 4, 3, Rust);
        // impact plate
        c.Rect(16, 8, 32, 8, outline); c.Rect(18, 10, 28, 5, Rust);
    }

    // The Director: a big CRT monitor. mood 0 = calm (cyan), 1 = angry (red), 2 = broken (cracked, static)
    static void DrawDirector(Canvas c, int mood)
    {
        Color frame = Hex("1d1f24");
        c.Rect(0, 0, 160, 96, frame);
        c.Rect(4, 4, 152, 88, SteelDark);
        Color screen = mood == 1 ? Hex("1a0606") : Hex("041214");
        c.Rect(10, 10, 140, 76, screen);
        Color face = mood == 0 ? Hex("5ff0ff") : mood == 1 ? Hex("ff4030") : Hex("9aa0a6");
        var rng = new System.Random(7 + mood);
        // scanlines
        for (int y = 10; y < 86; y += 3) c.Rect(10, y, 140, 1, Color.Lerp(screen, Color.black, 0.4f));
        if (mood == 0)
        {
            c.Rect(42, 54, 22, 6, face); c.Rect(96, 54, 22, 6, face);          // flat, attentive eyes
            c.Rect(56, 28, 48, 4, face);                                      // straight mouth
        }
        else if (mood == 1)
        {
            for (int i = 0; i < 22; i++) { c.Rect(42 + i, 54 + i / 3, 2, 6, face); c.Rect(118 - i, 54 + i / 3, 2, 6, face); }  // slanted eyes
            c.Rect(52, 24, 56, 4, face); c.Rect(52, 28, 4, 6, face); c.Rect(104, 28, 4, 6, face);                             // frown
        }
        else
        {
            for (int i = 0; i < 400; i++) c.Set(10 + rng.Next(140), 10 + rng.Next(76), Color.Lerp(screen, face, (float)rng.NextDouble()));
            for (int i = 0; i < 14; i++) { c.Rect(44 + i, 50 + i, 3, 3, face); c.Rect(57 - i, 50 + i, 3, 3, face); c.Rect(98 + i, 50 + i, 3, 3, face); c.Rect(111 - i, 50 + i, 3, 3, face); } // X eyes
            int x = 80, y = 86;
            while (y > 10) { c.Rect(x, y, 2, 2, Color.white); x += rng.Next(-2, 3); y -= 2; }   // crack
        }
        c.Rect(70, 0, 20, 4, Hex("3a3d44"));
        c.Circle(146, 6, 2, mood == 2 ? Hex("333333") : Hex("ff3a2a"));
    }

    static void DrawControlPanel(Canvas c)
    {
        c.Rect(0, 0, 64, 36, SteelDark);
        c.Rect(2, 2, 60, 32, Steel);
        c.Rect(0, 36, 64, 12, Hex("15161a"));
        c.Rect(4, 38, 56, 8, Hex("103018"));
        for (int x = 6; x < 58; x += 8) c.Rect(x, 40, 4, 3, Hex("5fff7a"));
        for (int x = 8; x < 60; x += 12) { c.Circle(x, 24, 3, Hex("ff3a2a")); c.Circle(x, 12, 3, Hazard); }
    }

    static void DrawPanelShield(Canvas c)
    {
        for (int x = 0; x < 64; x += 10) c.Rect(x, 0, 6, 48, SteelLight);
        c.Rect(0, 20, 64, 6, Steel);
        for (int x = 0; x < 64; x++) for (int y = 20; y < 26; y++) if (((x + y) / 3) % 2 == 0) c.Set(x, y, Hazard);
    }

    static void DrawWallUnit(Canvas c)
    {
        c.Rect(0, 0, 48, 96, Hex("15161a"));
        c.Rect(2, 2, 40, 92, Steel);
        c.Rect(36, 30, 12, 24, SteelDark);   // piston socket on the right side
        c.Rect(40, 36, 8, 12, SteelLight);
        for (int y = 8; y < 90; y += 14) c.Rect(6, y, 26, 3, SteelLight);
        c.Circle(20, 70, 6, Hex("ff3a2a")); c.Circle(20, 70, 3, Hex("ffb0a0"));
        for (int x = 2; x < 42; x++) for (int y = 0; y < 6; y++) if (((x + y) / 3) % 2 == 0) c.Set(x, y, Hazard);
    }

    static void DrawCrusherWall(Canvas c)
    {
        c.Rect(0, 0, 32, 32, Hex("2f3238"));
        c.Rect(0, 0, 32, 2, SteelLight);
        c.Circle(8, 8, 2, SteelLight); c.Circle(24, 24, 2, SteelLight);
        for (int x = 0; x < 32; x++) for (int y = 12; y < 20; y++) if (((x + y) / 4) % 2 == 0) c.Set(x, y, Hazard);
    }

    // White pixels whose alpha depends on the distance from the centre (0 centre, 1 edge)
    static void Radial(Canvas c, float radius, System.Func<float, float> alpha)
    {
        int size = Mathf.RoundToInt(radius * 2f);
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius)) / radius;
                c.Set(x, y, new Color(1f, 1f, 1f, alpha(d)));
            }
    }

    // Repair kit: green case with a white cross
    static void DrawItemRepair(Canvas c)
    {
        c.Rect(2, 3, 20, 16, Hex("15161a")); c.Rect(3, 4, 18, 14, Hex("3fbf5a"));
        c.Rect(9, 19, 6, 3, Hex("15161a"));
        c.Rect(10, 6, 4, 10, Color.white); c.Rect(7, 9, 10, 4, Color.white);
    }

    // Energy shield cell: cyan hexagon battery
    static void DrawItemShield(Canvas c)
    {
        for (int y = 2; y < 22; y++)
        {
            int half = 9 - Mathf.Max(0, Mathf.Abs(y - 12) - 4);
            c.Rect(12 - half, y, half * 2, 1, Hex("15161a"));
            if (half > 1) c.Rect(13 - half, y, half * 2 - 2, 1, Hex("3fd8f0"));
        }
        c.Rect(10, 8, 4, 8, Color.white);
    }

    // Press overdrive: orange cartridge with a lightning bolt
    static void DrawItemOverdrive(Canvas c)
    {
        c.Rect(5, 1, 14, 22, Hex("15161a")); c.Rect(6, 2, 12, 20, Hex("e07a20"));
        int[] bolt = { 13, 12, 11, 10, 9, 12, 11, 10, 9, 8 };
        for (int i = 0; i < bolt.Length; i++) c.Rect(bolt[i], 19 - i * 1 - (i > 4 ? 2 : 0), 3, 2, Color.white);
    }

    // Module: a circuit chip with gold pins
    static void DrawItemModule(Canvas c)
    {
        c.Rect(4, 4, 16, 16, Hex("15161a")); c.Rect(5, 5, 14, 14, Hex("2b3a2e"));
        for (int i = 6; i < 19; i += 3) { c.Rect(i, 1, 1, 3, Hazard); c.Rect(i, 20, 1, 3, Hazard); c.Rect(1, i, 3, 1, Hazard); c.Rect(20, i, 3, 1, Hazard); }
        c.Rect(9, 9, 6, 6, Hazard); c.Rect(10, 10, 4, 4, Hex("fff0a0"));
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
