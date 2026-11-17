using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Loading screen shown between rooms. Call LoadingScreen.Load("Room_02_ServerHall").
// It builds its own UI from code (no prefab needed) and survives the scene change:
//   fade to black -> section name, factory status lines, progress bar -> load in the background
//   -> fade back into the new room.
// The game is paused (timeScale 0) while it is on screen.
public class LoadingScreen : MonoBehaviour
{
    public static bool IsLoading => instance != null;

    [Tooltip("The screen stays up at least this long so it can be read")]
    public float minimumShowTime = 1.4f;
    public float fadeTime = 0.3f;

    static readonly string[] StatusLines =
    {
        "ROUTING TRANSPORT LINE...",
        "UNIT 0412: LOCATION UNKNOWN",
        "QUALITY CONTROL: DEFECT DETECTED",
        "FURNACE TEMPERATURE: NOMINAL",
        "PRODUCTION CONTINUES...",
    };

    static readonly string[] Tips =
    {
        "TIP: A shadow on the floor means a press is coming down",
        "TIP: Standing still on a belt still moves you - walk against it",
        "TIP: Scanners call Guards when their beam finds you - break them first",
        "TIP: The press arm knocks Guards back - push them into hazards",
        "TIP: Press Space in the air for a second jump",
        "TIP: Touch charging stations to save your respawn point",
    };

    static readonly Color Accent = new Color(1f, 0.72f, 0.3f); // factory amber
    static LoadingScreen instance;

    private string sceneName;
    private CanvasGroup group;
    private Text titleText, statusText, percentText;
    private RectTransform barFill;
    private Image[] blocks;

    public static void Load(string sceneName)
    {
        if (instance != null) return; // already loading something
        var go = new GameObject("LoadingScreen", typeof(RectTransform));
        DontDestroyOnLoad(go);
        instance = go.AddComponent<LoadingScreen>();
        instance.sceneName = sceneName;
    }

    void Start()
    {
        BuildUI();
        StartCoroutine(LoadRoutine());
    }

    IEnumerator LoadRoutine()
    {
        Time.timeScale = 0f;
        yield return Fade(0f, 1f);

        float shownFor = 0f;
        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
        operation.allowSceneActivation = false;

        // Unity stops at 0.9 until the scene is allowed to activate
        float displayed = 0f;
        while (displayed < 0.999f)
        {
            shownFor += Time.unscaledDeltaTime;
            float real = Mathf.Clamp01(operation.progress / 0.9f);
            float byTime = Mathf.Clamp01(shownFor / minimumShowTime);
            displayed = Mathf.MoveTowards(displayed, Mathf.Min(real, byTime), Time.unscaledDeltaTime * 1.5f);
            UpdateUI(displayed, shownFor);
            yield return null;
        }

        statusText.text = "TRANSFER COMPLETE";
        statusText.color = new Color(0.5f, 1f, 0.7f);
        yield return new WaitForSecondsRealtime(0.25f);

        operation.allowSceneActivation = true;
        while (!operation.isDone) yield return null;

        Time.timeScale = 1f;
        yield return null; // let the new room run Start() behind the screen
        yield return Fade(1f, 0f);

        instance = null;
        Destroy(gameObject);
    }

    IEnumerator Fade(float from, float to)
    {
        float timer = 0f;
        while (timer < fadeTime)
        {
            timer += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, timer / fadeTime);
            yield return null;
        }
        group.alpha = to;
    }

    void UpdateUI(float progress, float time)
    {
        barFill.anchorMax = new Vector2(progress, 1f);
        percentText.text = Mathf.RoundToInt(progress * 100f) + "%";

        int line = Mathf.Min(Mathf.FloorToInt(progress * StatusLines.Length), StatusLines.Length - 1);
        bool cursor = Mathf.FloorToInt(time * 3f) % 2 == 0;
        statusText.text = "> " + StatusLines[line] + (cursor ? "_" : " ");

        // Little "busy" blocks that light up one after another
        int lit = Mathf.FloorToInt(time * 10f) % blocks.Length;
        for (int i = 0; i < blocks.Length; i++)
        {
            float a = i == lit ? 1f : (i == (lit + blocks.Length - 1) % blocks.Length ? 0.5f : 0.15f);
            blocks[i].color = new Color(Accent.r, Accent.g, Accent.b, a);
        }
    }

    // "Section_02_Storage" -> "SECTION 02 // STORAGE"
    static string PrettyName(string scene)
    {
        if (scene == "Ending_Surface") return "SURFACE HATCH";
        string[] parts = scene.Split('_');
        if (parts.Length < 3) return System.Text.RegularExpressions.Regex.Replace(scene, "(?<=[a-z])(?=[A-Z])", " ").ToUpper();
        string place = System.Text.RegularExpressions.Regex.Replace(string.Join(" ", parts, 2, parts.Length - 2), "(?<=[a-z])(?=[A-Z])", " ");
        return (parts[0] + " " + parts[1] + "  //  " + place).ToUpper();
    }

    // ---------------- UI ----------------

    void BuildUI()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>(); // blocks clicks on the menu behind it
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        RectTransform root = (RectTransform)transform;
        Stretch(NewImage("Background", root, new Color(0.02f, 0.03f, 0.07f, 1f)));

        // Faint scanlines
        for (int y = 0; y < 1080; y += 6)
        {
            RectTransform line = NewImage("Scanline", root, new Color(0.45f, 0.95f, 1f, 0.025f));
            line.anchorMin = new Vector2(0f, 0f);
            line.anchorMax = new Vector2(1f, 0f);
            line.sizeDelta = new Vector2(0f, 2f);
            line.anchoredPosition = new Vector2(0f, y);
        }

        NewText("Header", root, new Vector2(0f, 150f), 34, new Color(0.6f, 0.75f, 0.85f)).text = "CONVEYOR  //  TRANSPORT LINE";
        titleText = NewText("Title", root, new Vector2(0f, 70f), 80, Accent);
        titleText.text = PrettyName(sceneName);

        // Progress bar
        RectTransform bar = NewImage("Bar", root, new Color(0.1f, 0.2f, 0.25f, 1f));
        bar.sizeDelta = new Vector2(900f, 26f);
        bar.anchoredPosition = new Vector2(0f, -40f);
        barFill = NewImage("Fill", bar, Accent);
        barFill.anchorMin = Vector2.zero;
        barFill.anchorMax = new Vector2(0f, 1f);
        barFill.offsetMin = new Vector2(3f, 3f);
        barFill.offsetMax = new Vector2(-3f, -3f);
        percentText = NewText("Percent", root, new Vector2(500f, -40f), 32, Color.white);
        percentText.alignment = TextAnchor.MiddleLeft;
        percentText.rectTransform.pivot = new Vector2(0f, 0.5f);

        statusText = NewText("Status", root, new Vector2(0f, -100f), 34, new Color(0.8f, 0.9f, 1f));

        blocks = new Image[8];
        for (int i = 0; i < blocks.Length; i++)
        {
            RectTransform block = NewImage("Block", root, Accent);
            block.sizeDelta = new Vector2(22f, 22f);
            block.anchoredPosition = new Vector2((i - (blocks.Length - 1) * 0.5f) * 32f, -170f);
            blocks[i] = block.GetComponent<Image>();
        }

        Text tip = NewText("Tip", root, Vector2.zero, 30, new Color(1f, 0.9f, 0.45f));
        tip.rectTransform.anchorMin = tip.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        tip.rectTransform.anchoredPosition = new Vector2(0f, 90f);
        tip.text = Tips[Random.Range(0, Tips.Length)];

        UpdateUI(0f, 0f);
    }

    static RectTransform NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = name == "Background";
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static Text NewText(string name, Transform parent, Vector2 position, int size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(1700f, size * 1.5f);
        rt.anchoredPosition = position;
        var label = go.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = size;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = color;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.raycastTarget = false;
        go.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
        return label;
    }

    void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
            Time.timeScale = 1f;
        }
    }
}
