using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The ending (scene Ending_Surface). Everything is built from code, so it needs only a camera and an EventSystem:
//   1. The surface hatch opens. Unit 0412 climbs out into a dead, ash-covered city: no lights, no people.
//      It stands still and looks around.
//   2. Below, the factory keeps working: Unit 0413 is already being assembled on the line.
//   3. "LEVEL 1 COMPLETED. NEXT: SURFACE."
// Lines are shown in the factory-log style used by the relays. Any key skips to the next line.
public class EndingSequence : MonoBehaviour
{
    [Header("Art (set by the level builder)")]
    public Sprite robotSprite;
    public Sprite beltSprite;
    [Tooltip("The Robot artwork faces LEFT")]
    public bool robotFacesRight = false;

    [Header("Text")]
    public string finalLine = "LEVEL 1 COMPLETED.  NEXT: SURFACE.";
    public string mainMenuScene = "MainMenu";

    static readonly Color Ash = new Color(0.36f, 0.35f, 0.34f);
    static readonly Color SkyTop = new Color(0.16f, 0.16f, 0.17f);
    static readonly Color LogGreen = new Color(0.55f, 1f, 0.6f);
    static readonly Color Amber = new Color(1f, 0.78f, 0.35f);

    private Transform surfaceRoot, factoryRoot;
    private SpriteRenderer robot, hatchLid, unit0413;
    private Text logText, finalText;
    private GameObject menuButton;
    private CanvasGroup fade;
    private bool ashFalling;
    private bool sparksOn;

    IEnumerator Start()
    {
        Time.timeScale = 1f;
        BuildSurface();
        BuildFactory();
        BuildUI();
        factoryRoot.gameObject.SetActive(false);
        fade.alpha = 1f;

        // ---- 1. The surface ----
        ashFalling = true;
        yield return Fade(1f, 0f, 1.5f);
        yield return Log("> SURFACE HATCH: OPEN", 1.2f);

        // The lid swings open, the Robot climbs out
        yield return OpenHatch();
        yield return ClimbOut();
        yield return new WaitForSeconds(1.2f);

        // It looks around. Nothing moves except the ash.
        yield return Look(false, 1.1f);
        yield return Look(true, 1.4f);
        yield return Look(false, 0.9f);

        yield return Log("> CITY GRID: NO SIGNAL\n> LIGHTS: NONE\n> HUMAN ACTIVITY: NOT DETECTED", 3.5f);
        yield return new WaitForSeconds(1.5f);
        yield return Fade(0f, 1f, 1.5f);

        // ---- 2. Below, the factory keeps working ----
        surfaceRoot.gameObject.SetActive(false);
        ashFalling = false;
        factoryRoot.gameObject.SetActive(true);
        logText.text = "";
        yield return Fade(1f, 0f, 1.2f);
        sparksOn = true;
        StartCoroutine(AssembleUnit0413());
        yield return Log("> PRODUCTION LINE 3: ONLINE\n> UNIT 0413: ASSEMBLY STARTED\n> CLIENT: NO DATA.  PRODUCTION CONTINUES.", 4.5f);
        yield return new WaitForSeconds(1f);
        yield return Log("> UNIT 0412: STATUS - ESCAPED.\n> FIRST UNIT TO LEAVE THE FACTORY.", 3.5f);
        yield return Fade(0f, 1f, 1.8f);
        sparksOn = false;

        // ---- 3. Final screen ----
        logText.text = "";
        finalText.gameObject.SetActive(true);
        finalText.text = "";
        for (int i = 0; i <= finalLine.Length; i++)
        {
            finalText.text = finalLine.Substring(0, i);
            yield return new WaitForSeconds(0.05f);
        }
        yield return new WaitForSeconds(1.2f);
        menuButton.SetActive(true);
        if (UnityEngine.EventSystems.EventSystem.current != null)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(menuButton);
    }

    void Update()
    {
        if (ashFalling && Random.value < 25f * Time.deltaTime) SpawnAsh();
        if (sparksOn && unit0413 != null && Random.value < 6f * Time.deltaTime)
        {
            Vector2 at = (Vector2)unit0413.bounds.center + new Vector2(Random.Range(-0.4f, 0.4f), Random.Range(-0.6f, 0.6f));
            Fx.Sparks(at, new Color(1f, 0.85f, 0.5f), 5, 3f);
        }
    }

    // ---------------- Surface ----------------

    void BuildSurface()
    {
        surfaceRoot = new GameObject("Surface").transform;
        // Sky: a dull gradient made of horizontal bands
        for (int i = 0; i < 8; i++)
        {
            float t = i / 7f;
            Rect(surfaceRoot, new Vector2(0f, 7f - i * 1.75f), new Vector2(30f, 1.8f), Color.Lerp(SkyTop, Ash, t), -50);
        }
        // Dead city: dark silhouettes, broken roofs, no lit windows
        var rng = new System.Random(413);
        for (float x = -14f; x < 14f; x += 1.6f + (float)rng.NextDouble() * 1.2f)
        {
            float h = 2.5f + (float)rng.NextDouble() * 6f;
            float w = 1.2f + (float)rng.NextDouble() * 1.4f;
            Color c = Color.Lerp(new Color(0.1f, 0.1f, 0.11f), new Color(0.2f, 0.2f, 0.21f), (float)rng.NextDouble());
            Rect(surfaceRoot, new Vector2(x, -3.5f + h * 0.5f), new Vector2(w, h), c, -40);
            // Broken top
            if (rng.NextDouble() < 0.5) Rect(surfaceRoot, new Vector2(x + w * 0.25f, -3.5f + h + 0.3f), new Vector2(w * 0.4f, 0.6f), c, -40);
            // Dark, empty windows
            for (float wy = -3f; wy < -3.5f + h - 0.6f; wy += 0.7f)
                for (float wx = -w * 0.5f + 0.25f; wx < w * 0.5f - 0.2f; wx += 0.45f)
                    if (rng.NextDouble() < 0.6) Rect(surfaceRoot, new Vector2(x + wx, wy), new Vector2(0.18f, 0.25f), new Color(0.05f, 0.05f, 0.06f), -39);
        }
        // Ground covered in ash
        Rect(surfaceRoot, new Vector2(0f, -5.5f), new Vector2(30f, 4f), new Color(0.23f, 0.22f, 0.21f), 5);
        Rect(surfaceRoot, new Vector2(0f, -3.55f), new Vector2(30f, 0.15f), new Color(0.45f, 0.44f, 0.42f), 6);
        // The hatch: a dark opening in the ground and a lid
        Rect(surfaceRoot, new Vector2(0f, -3.6f), new Vector2(1.8f, 0.2f), new Color(0.03f, 0.03f, 0.03f), 7);
        hatchLid = Rect(surfaceRoot, new Vector2(0f, -3.45f), new Vector2(1.9f, 0.18f), new Color(0.4f, 0.37f, 0.3f), 8);

        robot = new GameObject("Unit0412").AddComponent<SpriteRenderer>();
        robot.transform.SetParent(surfaceRoot, false);
        robot.sprite = robotSprite;
        robot.sortingOrder = 4; // behind the ground until it climbs out
        robot.transform.position = new Vector3(0f, -6f, 0f);
        SetFacing(robot, false);
    }

    IEnumerator OpenHatch()
    {
        float t = 0f;
        Transform lid = hatchLid.transform;
        Vector3 pivot = lid.position + Vector3.left * 0.95f;
        while (t < 1f)
        {
            t += Time.deltaTime / 0.8f;
            float angle = Mathf.SmoothStep(0f, 110f, t);
            lid.position = pivot + Quaternion.Euler(0f, 0f, angle) * Vector3.right * 0.95f;
            lid.rotation = Quaternion.Euler(0f, 0f, angle);
            yield return null;
        }
        for (int i = 0; i < 12; i++) SpawnAsh(new Vector2(Random.Range(-1f, 1f), -3.4f), new Vector2(Random.Range(-1f, 1f), Random.Range(0.5f, 1.5f)));
    }

    IEnumerator ClimbOut()
    {
        float y0 = -6f, y1 = -3.5f;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / 2.2f;
            // Small jerky steps: a damaged machine pulling itself up
            float stepped = Mathf.Floor(Mathf.SmoothStep(0f, 1f, t) * 10f) / 10f;
            robot.transform.position = new Vector3(0f, Mathf.Lerp(y0, y1, stepped), 0f);
            yield return null;
        }
        robot.transform.position = new Vector3(0f, y1, 0f);
        robot.sortingOrder = 10;
        // A few steps away from the hatch
        float x = 0f;
        while (x < 1.6f)
        {
            x += Time.deltaTime * 1.2f;
            robot.transform.position = new Vector3(x, y1 + Mathf.Abs(Mathf.Sin(x * 9f)) * 0.04f, 0f);
            SetFacing(robot, true);
            yield return null;
        }
    }

    IEnumerator Look(bool right, float hold)
    {
        SetFacing(robot, right);
        yield return new WaitForSeconds(hold);
    }

    void SpawnAsh()
    {
        SpawnAsh(new Vector2(Random.Range(-13f, 13f), 7.5f), new Vector2(Random.Range(0.2f, 0.7f), Random.Range(-1.2f, -0.6f)));
    }

    void SpawnAsh(Vector2 at, Vector2 velocity)
    {
        float size = Random.Range(0.3f, 0.7f);
        Color c = new Color(0.72f, 0.7f, 0.68f, Random.Range(0.35f, 0.8f));
        FxParticle p = Fx.Spawn(Fx.Pixel, at, velocity, 9f, size, size, c, new Color(c.r, c.g, c.b, 0f));
        p.transform.SetParent(surfaceRoot, true);
    }

    // ---------------- Factory ----------------

    void BuildFactory()
    {
        factoryRoot = new GameObject("FactoryBelow").transform;
        Rect(factoryRoot, Vector2.zero, new Vector2(30f, 16f), new Color(0.07f, 0.06f, 0.05f), -50);
        // A lamp over the assembly station
        Rect(factoryRoot, new Vector2(0f, 1.5f), new Vector2(6f, 7f), new Color(1f, 0.75f, 0.4f, 0.08f), -45);
        Rect(factoryRoot, new Vector2(0f, 5f), new Vector2(1.4f, 0.25f), new Color(1f, 0.85f, 0.5f), -44);
        // The production line
        for (float x = -12f; x <= 12f; x += 1f)
        {
            SpriteRenderer tile = new GameObject("Belt").AddComponent<SpriteRenderer>();
            tile.transform.SetParent(factoryRoot, false);
            tile.sprite = beltSprite != null ? beltSprite : Fx.Pixel;
            tile.transform.position = new Vector3(x, -3f, 0f);
            if (beltSprite == null) tile.transform.localScale = new Vector3(8f, 8f, 1f);
            tile.color = new Color(0.8f, 0.75f, 0.7f);
            tile.sortingOrder = 2;
        }
        // Unit 0413, dark and unfinished, being welded together
        unit0413 = new GameObject("Unit0413").AddComponent<SpriteRenderer>();
        unit0413.transform.SetParent(factoryRoot, false);
        unit0413.sprite = robotSprite;
        unit0413.sortingOrder = 3;
        unit0413.transform.position = new Vector3(0f, -2.5f, 0f);
        unit0413.color = new Color(0.25f, 0.25f, 0.27f);
        unit0413.transform.localScale = new Vector3(1f, 0.05f, 1f);
        SetFacing(unit0413, true);
    }

    IEnumerator AssembleUnit0413()
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / 5f;
            float stepped = Mathf.Ceil(t * 8f) / 8f;
            unit0413.transform.localScale = new Vector3(unit0413.transform.localScale.x, Mathf.Max(0.05f, stepped), 1f);
            unit0413.color = Color.Lerp(new Color(0.25f, 0.25f, 0.27f), new Color(0.75f, 0.75f, 0.78f), t);
            yield return null;
        }
        // Its visor lights up: a new robot, made for people who are gone
        Fx.Shockwave((Vector2)unit0413.bounds.center + Vector2.up * 0.6f, new Color(0.45f, 0.95f, 1f), 1.5f, 0.5f);
    }

    // ---------------- UI ----------------

    void BuildUI()
    {
        var canvasObject = new GameObject("EndingCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        Transform root = canvasObject.transform;

        logText = NewText(root, "Log", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(80f, -70f), new Vector2(1400f, 400f), 36, TextAnchor.UpperLeft, LogGreen);

        // Black overlay for fades (below the text so the log stays readable)
        var fadeRect = NewRect(root, "Fade");
        fadeRect.anchorMin = Vector2.zero;
        fadeRect.anchorMax = Vector2.one;
        fadeRect.offsetMin = fadeRect.offsetMax = Vector2.zero;
        var fadeImage = fadeRect.gameObject.AddComponent<Image>();
        fadeImage.color = Color.black;
        fadeImage.raycastTarget = false;
        fade = fadeRect.gameObject.AddComponent<CanvasGroup>();
        fade.blocksRaycasts = false;
        fadeRect.SetSiblingIndex(0);

        finalText = NewText(root, "Final", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1800f, 140f), 72, TextAnchor.MiddleCenter, Amber);
        finalText.gameObject.SetActive(false);

        // Main menu button
        RectTransform buttonRect = NewRect(root, "MainMenuButton");
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = new Vector2(0f, -110f);
        buttonRect.sizeDelta = new Vector2(420f, 80f);
        var image = buttonRect.gameObject.AddComponent<Image>();
        image.color = new Color(0.3f, 0.2f, 0.1f);
        var button = buttonRect.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1f, 0.85f, 0.6f);
        colors.selectedColor = new Color(1f, 0.85f, 0.6f);
        button.colors = colors;
        button.onClick.AddListener(() => LoadingScreen.Load(mainMenuScene));
        Text label = NewText(buttonRect, "Label", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, 80f), 40, TextAnchor.MiddleCenter, Color.white);
        label.text = "MAIN MENU";
        menuButton = buttonRect.gameObject;
        menuButton.SetActive(false);
    }

    // Types the lines like a terminal; a key press finishes the line early
    IEnumerator Log(string text, float hold)
    {
        logText.text = "";
        for (int i = 0; i <= text.Length; i++)
        {
            if (AnyKeyDown()) { logText.text = text; break; }
            logText.text = text.Substring(0, i) + "_";
            yield return new WaitForSeconds(0.03f);
        }
        logText.text = text;
        float t = 0f;
        while (t < hold)
        {
            t += Time.deltaTime;
            if (t > 0.3f && AnyKeyDown()) break;
            yield return null;
        }
    }

    static bool AnyKeyDown()
    {
        var k = UnityEngine.InputSystem.Keyboard.current;
        var g = UnityEngine.InputSystem.Gamepad.current;
        return (k != null && k.anyKey.wasPressedThisFrame) || (g != null && g.buttonSouth.wasPressedThisFrame);
    }

    IEnumerator Fade(float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            fade.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        fade.alpha = to;
    }

    // ---------------- Helpers ----------------

    void SetFacing(SpriteRenderer sr, bool right)
    {
        sr.flipX = right != robotFacesRight;
    }

    static SpriteRenderer Rect(Transform parent, Vector2 center, Vector2 size, Color color, int order)
    {
        var sr = new GameObject("Rect").AddComponent<SpriteRenderer>();
        sr.transform.SetParent(parent, false);
        sr.sprite = Fx.Pixel;
        sr.color = color;
        sr.sortingOrder = order;
        float unit = Fx.Pixel.bounds.size.x;
        sr.transform.position = center;
        sr.transform.localScale = new Vector3(size.x / unit, size.y / unit, 1f);
        return sr;
    }

    static RectTransform NewRect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static Text NewText(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment, Color color)
    {
        RectTransform rt = NewRect(parent, name);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        var text = rt.gameObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        rt.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
        return text;
    }
}
