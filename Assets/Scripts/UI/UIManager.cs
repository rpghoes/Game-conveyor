using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Controls the HUD (HP, relay counter, messages, factory log window, screen flash, boss bar)
// and the main menu buttons. One per scene.
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("HUD Text")]
    public Text healthText;
    public Text relayText;
    [Tooltip("Small messages at the bottom of the screen")]
    public Text messageText;
    [Tooltip("Interaction hint, e.g. 'E - ACTIVATE RELAY'")]
    public Text promptText;
    [Tooltip("Big text at the top: section names, boss lines")]
    public Text bannerText;

    [Header("Boss Health Bar")]
    public GameObject bossBarRoot;
    [Tooltip("The fill bar. Its width shrinks as the boss loses health")]
    public RectTransform bossBarFill;
    public Text bossNameText;

    [Header("Section Intro")]
    public string roomTitle;
    [TextArea] public string roomHint;

    [Header("Scene Names")]
    public string firstRoomScene = "Section_01_FactoryFloor";
    public string mainMenuScene = "MainMenu";

    static readonly Color Amber = new Color(1f, 0.78f, 0.35f);
    static readonly Color LogGreen = new Color(0.55f, 1f, 0.6f);

    private Coroutine messageRoutine;
    private Coroutine bannerRoutine;
    private Coroutine flashRoutine;
    private Coroutine logRoutine;
    private Image flashImage;
    private RectTransform healthCellRoot;
    private Image[] healthCells;
    private Image[] healthCellFrames;
    private float[] lostCellFlashUntil;
    private Text healthNumber;
    private int shownHealth;
    private Text bossHealthNumber;
    // Factory log window
    private RectTransform logPanel;
    private Text logTitleLabel;
    private Text logBody;

    // True while the factory log window is printing or on screen
    public bool IsLogOpen => logPanel != null && logPanel.gameObject.activeSelf;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        UpdateRelays(RelayManager.ActivatedCount, RelayManager.TotalRelays);
        SetVisible(messageText, false);
        SetVisible(promptText, false);
        SetVisible(bannerText, false);
        if (bossBarRoot != null) bossBarRoot.SetActive(false);

        if (!string.IsNullOrEmpty(roomTitle)) ShowBanner(roomTitle, 2.5f);
        if (!string.IsNullOrEmpty(roomHint)) ShowMessage(roomHint, 6f);
    }

    void Update()
    {
        RefreshHealthCells();
    }

    // ---------------- HUD ----------------

    public void UpdateHealth(int current, int max)
    {
        if (healthText != null) healthText.text = "HP";
        int hit = PlayerProgression.HitValue;
        int cellCount = Mathf.Max(1, Mathf.CeilToInt((float)max / hit));
        if (healthCells == null || healthCells.Length != cellCount) BuildHealthCells(cellCount);
        // A cell that lost HP flashes white for a moment
        for (int i = 0; i < healthCells.Length; i++)
        {
            int cellTop = (i + 1) * hit;
            if (current < cellTop && shownHealth > i * hit && current < shownHealth) lostCellFlashUntil[i] = Time.unscaledTime + 0.35f;
        }
        shownHealth = current;
        if (healthNumber != null) healthNumber.text = current + "/" + max;
        RefreshHealthCells();
    }

    // One cell per hit (hitValue HP), to the right of the "HP" label. The last one can be partly filled.
    void BuildHealthCells(int max)
    {
        if (healthCellRoot != null) Destroy(healthCellRoot.gameObject);
        healthCellRoot = NewUI("HealthCells", transform);
        healthCellRoot.anchorMin = healthCellRoot.anchorMax = new Vector2(0f, 1f);
        healthCellRoot.pivot = new Vector2(0f, 1f);
        healthCellRoot.anchoredPosition = new Vector2(115f, -38f);
        healthCellRoot.sizeDelta = new Vector2(max * 58f, 40f);

        healthCells = new Image[max];
        healthCellFrames = new Image[max];
        lostCellFlashUntil = new float[max];
        for (int i = 0; i < max; i++)
        {
            RectTransform frame = NewUI("Cell" + i, healthCellRoot);
            frame.anchorMin = frame.anchorMax = new Vector2(0f, 1f);
            frame.pivot = new Vector2(0f, 1f);
            frame.anchoredPosition = new Vector2(i * 58f, 0f);
            frame.sizeDelta = new Vector2(50f, 36f);
            healthCellFrames[i] = frame.gameObject.AddComponent<Image>();
            healthCellFrames[i].color = new Color(0.06f, 0.05f, 0.04f, 0.9f);

            RectTransform fill = NewUI("Fill", frame);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = new Vector2(4f, 4f);
            fill.offsetMax = new Vector2(-4f, -4f);
            healthCells[i] = fill.gameObject.AddComponent<Image>();
            healthCells[i].raycastTarget = false;
        }

        RectTransform numberRect = NewUI("HealthNumber", healthCellRoot);
        numberRect.anchorMin = numberRect.anchorMax = new Vector2(0f, 1f);
        numberRect.pivot = new Vector2(0f, 1f);
        numberRect.anchoredPosition = new Vector2(max * 58f + 8f, 2f);
        numberRect.sizeDelta = new Vector2(200f, 40f);
        healthNumber = NewLabel(numberRect, 30, TextAnchor.MiddleLeft);
        healthNumber.color = new Color(0.9f, 0.88f, 0.8f);
    }

    void RefreshHealthCells()
    {
        if (healthCells == null) return;
        int hit = PlayerProgression.HitValue;
        // One hit or less left: the cells pulse red
        bool critical = shownHealth > 0 && shownHealth <= hit;
        float pulse = Mathf.PingPong(Time.unscaledTime * 4f, 1f);
        for (int i = 0; i < healthCells.Length; i++)
        {
            float part = Mathf.Clamp01((shownHealth - i * hit) / (float)hit);
            // The fill shrinks from the right; the cell frame behind it shows the lost part
            healthCells[i].rectTransform.offsetMax = new Vector2(-4f - 42f * (1f - part), -4f);
            healthCells[i].enabled = part > 0f;
            healthCellFrames[i].color = Time.unscaledTime < lostCellFlashUntil[i] ? Color.white : new Color(0.06f, 0.05f, 0.04f, 0.9f);
            healthCells[i].color = critical ? Color.Lerp(new Color(1f, 0.25f, 0.25f), new Color(1f, 0.7f, 0.6f), pulse) : Amber;
        }
    }

    Text NewLabel(RectTransform rect, int fontSize, TextAnchor alignment)
    {
        var label = rect.gameObject.AddComponent<Text>();
        label.font = healthText != null ? healthText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = fontSize;
        label.fontStyle = FontStyle.Bold;
        label.alignment = alignment;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        rect.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
        return label;
    }

    public void UpdateRelays(int count, int total)
    {
        if (relayText != null) relayText.text = "POWER RELAYS: " + count + "/" + total;
    }

    public void ShowMessage(string text, float duration = 2f)
    {
        if (messageText == null) return;
        if (messageRoutine != null) StopCoroutine(messageRoutine);
        messageRoutine = StartCoroutine(ShowTextFor(messageText, text, duration));
    }

    // duration <= 0 keeps the banner on screen
    public void ShowBanner(string text, float duration)
    {
        if (bannerText == null) return;
        if (bannerRoutine != null) StopCoroutine(bannerRoutine);
        bannerRoutine = StartCoroutine(ShowTextFor(bannerText, text, duration));
    }

    public void ShowPrompt(string text)
    {
        if (promptText == null) return;
        promptText.text = text;
        SetVisible(promptText, true);
    }

    public void HidePrompt()
    {
        SetVisible(promptText, false);
    }

    // Full-screen colour flash that fades out (damage, relay surge, boss death)
    public void Flash(Color color, float duration)
    {
        if (flashImage == null)
        {
            RectTransform rt = NewUI("ScreenFlash", transform);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            flashImage = rt.gameObject.AddComponent<Image>();
            flashImage.raycastTarget = false;
        }
        flashImage.transform.SetAsLastSibling();
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine(color, duration));
    }

    IEnumerator FlashRoutine(Color color, float duration)
    {
        flashImage.gameObject.SetActive(true);
        float timer = 0f;
        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime;
            flashImage.color = new Color(color.r, color.g, color.b, color.a * (1f - timer / duration));
            yield return null;
        }
        flashImage.gameObject.SetActive(false);
    }

    // Security alarm (a Scanner found the Robot): three red pulses and a warning line
    public void ShowAlarm(string text)
    {
        ShowMessage(text, 3f);
        StartCoroutine(AlarmPulses());
    }

    IEnumerator AlarmPulses()
    {
        for (int i = 0; i < 3; i++)
        {
            Flash(new Color(1f, 0.1f, 0.05f, 0.22f), 0.45f);
            yield return new WaitForSeconds(0.55f);
        }
    }

    // ---------------- Factory log (relay terminals) ----------------

    // A terminal window on the right side of the screen. Lines are typed out one by one,
    // the window stays for a few seconds and then closes by itself. The game keeps running.
    public void ShowTerminalLog(string title, string[] lines, float holdTime = 4f)
    {
        if (logPanel == null) BuildLogPanel();
        if (logRoutine != null) StopCoroutine(logRoutine);
        logRoutine = StartCoroutine(TerminalLogRoutine(title, lines ?? new string[0], holdTime));
    }

    void BuildLogPanel()
    {
        logPanel = NewUI("FactoryLog", transform);
        logPanel.anchorMin = logPanel.anchorMax = new Vector2(1f, 1f);
        logPanel.pivot = new Vector2(1f, 1f);
        logPanel.anchoredPosition = new Vector2(-40f, -40f);
        logPanel.sizeDelta = new Vector2(760f, 300f);
        var bg = logPanel.gameObject.AddComponent<Image>();
        bg.color = new Color(0.02f, 0.05f, 0.03f, 0.88f);
        bg.raycastTarget = false;
        logPanel.gameObject.AddComponent<Outline>().effectColor = new Color(0.3f, 0.8f, 0.4f, 0.6f);

        RectTransform titleRect = NewUI("Title", logPanel);
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.anchoredPosition = new Vector2(20f, -14f);
        titleRect.sizeDelta = new Vector2(720f, 40f);
        logTitleLabel = NewLabel(titleRect, 28, TextAnchor.UpperLeft);
        logTitleLabel.color = Amber;

        RectTransform bodyRect = NewUI("Body", logPanel);
        bodyRect.anchorMin = bodyRect.anchorMax = new Vector2(0f, 1f);
        bodyRect.pivot = new Vector2(0f, 1f);
        bodyRect.anchoredPosition = new Vector2(20f, -60f);
        bodyRect.sizeDelta = new Vector2(720f, 230f);
        logBody = NewLabel(bodyRect, 26, TextAnchor.UpperLeft);
        logBody.color = LogGreen;
        logBody.lineSpacing = 1.15f;
        logPanel.gameObject.SetActive(false);
    }

    IEnumerator TerminalLogRoutine(string title, string[] lines, float holdTime)
    {
        logPanel.gameObject.SetActive(true);
        logPanel.SetAsLastSibling();
        logTitleLabel.text = title;
        var shown = new System.Text.StringBuilder();
        logBody.text = "";

        foreach (string line in lines)
        {
            yield return new WaitForSeconds(0.25f);
            string prefix = "> ";
            for (int i = 0; i <= line.Length; i++)
            {
                logBody.text = shown + prefix + line.Substring(0, i) + "_";
                yield return new WaitForSeconds(0.025f);
            }
            shown.Append(prefix).Append(line).Append('\n');
            logBody.text = shown.ToString();
        }

        // Blinking cursor while the window stays open
        float timer = 0f;
        while (timer < holdTime)
        {
            timer += Time.deltaTime;
            logBody.text = shown + (Mathf.FloorToInt(timer * 3f) % 2 == 0 ? "> _" : "> ");
            yield return null;
        }
        logPanel.gameObject.SetActive(false);
        logRoutine = null;
    }

    // ---------------- Boss ----------------

    public void ShowBossBar(string bossName)
    {
        if (bossBarRoot != null) bossBarRoot.SetActive(true);
        if (bossNameText != null) bossNameText.text = bossName;
        SetBossHealth(1f);
    }

    public void SetBossHealth(float normalized)
    {
        if (bossBarFill != null) bossBarFill.anchorMax = new Vector2(Mathf.Clamp01(normalized), 1f);
    }

    // Same, plus "28 / 40" in the middle of the bar
    public void SetBossHealth(int current, int max)
    {
        SetBossHealth((float)current / max);
        if (bossBarRoot == null) return;
        if (bossHealthNumber == null)
        {
            RectTransform rect = NewUI("BossHealthNumber", bossBarRoot.transform);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            bossHealthNumber = NewLabel(rect, 24, TextAnchor.MiddleCenter);
            bossHealthNumber.color = Color.white;
        }
        bossHealthNumber.text = current + " / " + max;
    }

    public void HideBossBar()
    {
        SetBossHealth(0f);
        if (bossBarRoot != null) bossBarRoot.SetActive(false);
    }

    // ---------------- Buttons (Main Menu) ----------------

    public void StartGame()
    {
        RelayManager.ResetProgress();
        PlayerProgression.ResetProgress();
        LoadingScreen.Load(firstRoomScene);
    }

    public void LoadMainMenu()
    {
        LoadingScreen.Load(mainMenuScene);
    }

    public void QuitGame()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // ---------------- Helpers ----------------

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    IEnumerator ShowTextFor(Text label, string text, float duration)
    {
        label.text = text;
        SetVisible(label, true);
        if (duration > 0f)
        {
            yield return new WaitForSeconds(duration);
            SetVisible(label, false);
        }
    }

    void SetVisible(Text label, bool visible)
    {
        if (label != null) label.gameObject.SetActive(visible);
    }
}
