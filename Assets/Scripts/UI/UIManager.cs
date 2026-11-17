using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Controls the HUD (HP, terminals, ultimate meter, messages, screen flash, boss bar, victory screen)
// and the main menu buttons. One per scene.
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("HUD Text")]
    public Text healthText;
    public Text terminalText;
    [Tooltip("Small messages at the bottom of the screen")]
    public Text messageText;
    [Tooltip("Interaction hint, e.g. 'E - REBOOT'")]
    public Text promptText;
    [Tooltip("Big text at the top: room names, SYSTEM REBOOT COMPLETE, SYSTEM CLEANED")]
    public Text bannerText;

    [Header("Boss Health Bar")]
    public GameObject bossBarRoot;
    [Tooltip("The fill bar. Its width shrinks as the boss loses health")]
    public RectTransform bossBarFill;
    public Text bossNameText;

    [Header("Victory Screen")]
    public GameObject victoryPanel;
    public GameObject victoryFirstButton;
    public float victoryDelay = 3f;

    [Header("Room Intro")]
    public string roomTitle;
    [TextArea] public string roomHint;

    [Header("Scene Names")]
    public string firstRoomScene = "Room_01_Entrance";
    public string mainMenuScene = "MainMenu";

    private Coroutine messageRoutine;
    private Coroutine bannerRoutine;
    private Coroutine flashRoutine;
    private Image flashImage;
    private RectTransform ultimateFill;
    private Image ultimateFillImage;
    private Text ultimateLabel;
    private bool ultimateReady;
    private RectTransform healthCellRoot;
    private Image[] healthCells;
    private Image[] healthCellFrames;
    private int shownMaxHealth;
    // Level / XP panel
    private RectTransform xpPanel;
    private Text levelLabel;
    private Text xpLabel;
    private RectTransform xpFill;
    private Image xpFillImage;
    private float shownXpProgress;
    private float xpFlashUntil;
    private Text levelUpTitle;
    private Text levelUpDetails;
    private Coroutine levelUpRoutine;
    private float[] lostCellFlashUntil;
    private Text healthNumber;
    private int shownHealth;
    private Text bossHealthNumber;

    void Awake()
    {
        Instance = this;
    }

    void OnEnable()
    {
        PlayerProgression.XpChanged += OnXpChanged;
        PlayerProgression.LeveledUp += OnLeveledUp;
    }

    void OnDisable()
    {
        PlayerProgression.XpChanged -= OnXpChanged;
        PlayerProgression.LeveledUp -= OnLeveledUp;
    }

    void Start()
    {
        UpdateTerminals(TerminalManager.ActivatedCount, TerminalManager.TotalTerminals);
        SetVisible(messageText, false);
        SetVisible(promptText, false);
        SetVisible(bannerText, false);
        if (bossBarRoot != null) bossBarRoot.SetActive(false);
        if (victoryPanel != null) victoryPanel.SetActive(false);
        // Only the in-game HUD (it has the HP label) shows the level panel, not the main menu
        if (healthText != null)
        {
            BuildXpPanel();
            shownXpProgress = PlayerProgression.XpProgress;
            RefreshXpPanel();
        }

        if (!string.IsNullOrEmpty(roomTitle)) ShowBanner(roomTitle, 2.5f);
        if (!string.IsNullOrEmpty(roomHint)) ShowMessage(roomHint, 6f);
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
        shownMaxHealth = max;
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
            healthCellFrames[i].color = new Color(0.02f, 0.04f, 0.06f, 0.9f);

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
        healthNumber.color = new Color(0.8f, 0.9f, 1f);

        if (victoryPanel != null) victoryPanel.transform.SetAsLastSibling();
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
            healthCellFrames[i].color = Time.unscaledTime < lostCellFlashUntil[i] ? Color.white : new Color(0.02f, 0.04f, 0.06f, 0.9f);
            healthCells[i].color = critical ? Color.Lerp(new Color(1f, 0.25f, 0.25f), new Color(1f, 0.7f, 0.6f), pulse) : new Color(0.45f, 0.95f, 1f);
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

    public void UpdateTerminals(int count, int total)
    {
        if (terminalText != null) terminalText.text = "TERMINALS: " + count + "/" + total;
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

    public void ShowRebootComplete()
    {
        ShowBanner("SYSTEM REBOOT COMPLETE", 4f);
        ShowMessage("THE SERVER CORE IS NOW ACCESSIBLE", 4f);
    }

    // Full-screen colour flash that fades out (damage, ultimate, boss death)
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

    // Ultimate meter under the terminal counter. normalized = 0..1
    public void UpdateUltimate(float normalized, bool ready)
    {
        if (ultimateFill == null) BuildUltimateMeter();
        ultimateFill.anchorMax = new Vector2(Mathf.Clamp01(normalized), 1f);
        ultimateReady = ready;
        ultimateLabel.text = ready ? "ULTIMATE READY - K" : "ULTIMATE";
        if (!ready)
        {
            ultimateFillImage.color = new Color(0.3f, 0.75f, 0.85f);
            ultimateLabel.color = new Color(0.6f, 0.75f, 0.85f);
        }
    }

    void Update()
    {
        RefreshHealthCells();
        AnimateXpBar();

        // Pulse the meter while the ultimate is ready
        if (!ultimateReady || ultimateFillImage == null) return;
        float pulse = Mathf.PingPong(Time.unscaledTime * 3f, 1f);
        ultimateFillImage.color = Color.Lerp(new Color(0.45f, 0.95f, 1f), Color.white, pulse);
        ultimateLabel.color = Color.Lerp(new Color(1f, 0.9f, 0.3f), Color.white, pulse);
    }

    void BuildUltimateMeter()
    {
        RectTransform bar = NewUI("UltimateBar", transform);
        bar.anchorMin = bar.anchorMax = new Vector2(0f, 1f);
        bar.pivot = new Vector2(0f, 1f);
        bar.anchoredPosition = new Vector2(40f, -160f);
        bar.sizeDelta = new Vector2(320f, 22f);
        bar.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.1f, 0.14f, 0.9f);

        ultimateFill = NewUI("Fill", bar);
        ultimateFill.anchorMin = Vector2.zero;
        ultimateFill.anchorMax = new Vector2(0f, 1f);
        ultimateFill.offsetMin = new Vector2(3f, 3f);
        ultimateFill.offsetMax = new Vector2(-3f, -3f);
        ultimateFillImage = ultimateFill.gameObject.AddComponent<Image>();

        RectTransform labelRect = NewUI("Label", bar);
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(0f, 0.5f);
        labelRect.pivot = new Vector2(0f, 0.5f);
        labelRect.anchoredPosition = new Vector2(335f, 0f);
        labelRect.sizeDelta = new Vector2(500f, 40f);
        ultimateLabel = labelRect.gameObject.AddComponent<Text>();
        ultimateLabel.font = terminalText != null ? terminalText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        ultimateLabel.fontSize = 28;
        ultimateLabel.fontStyle = FontStyle.Bold;
        ultimateLabel.alignment = TextAnchor.MiddleLeft;
        ultimateLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        ultimateLabel.raycastTarget = false;
        labelRect.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);

        // Keep the victory screen on top of the meter
        if (victoryPanel != null) victoryPanel.transform.SetAsLastSibling();
    }

    // ---------------- Level / XP ----------------

    //   PLAYER LEVEL: 5
    //   [████████░░] XP 120 / 180  67%
    void BuildXpPanel()
    {
        Color gold = DamageNumber.LevelUpColor;
        xpPanel = NewUI("LevelPanel", transform);
        xpPanel.anchorMin = xpPanel.anchorMax = new Vector2(0f, 1f);
        xpPanel.pivot = new Vector2(0f, 1f);
        xpPanel.anchoredPosition = new Vector2(40f, -195f);
        xpPanel.sizeDelta = new Vector2(700f, 80f);

        RectTransform levelRect = NewUI("Level", xpPanel);
        levelRect.anchorMin = levelRect.anchorMax = new Vector2(0f, 1f);
        levelRect.pivot = new Vector2(0f, 1f);
        levelRect.anchoredPosition = Vector2.zero;
        levelRect.sizeDelta = new Vector2(500f, 40f);
        levelLabel = NewLabel(levelRect, 32, TextAnchor.MiddleLeft);
        levelLabel.color = gold;

        RectTransform bar = NewUI("XpBar", xpPanel);
        bar.anchorMin = bar.anchorMax = new Vector2(0f, 1f);
        bar.pivot = new Vector2(0f, 1f);
        bar.anchoredPosition = new Vector2(0f, -46f);
        bar.sizeDelta = new Vector2(320f, 20f);
        bar.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.07f, 0.03f, 0.9f);

        xpFill = NewUI("Fill", bar);
        xpFill.anchorMin = Vector2.zero;
        xpFill.anchorMax = new Vector2(0f, 1f);
        xpFill.offsetMin = new Vector2(3f, 3f);
        xpFill.offsetMax = new Vector2(-3f, -3f);
        xpFillImage = xpFill.gameObject.AddComponent<Image>();
        xpFillImage.color = gold;
        xpFillImage.raycastTarget = false;

        RectTransform xpRect = NewUI("Xp", xpPanel);
        xpRect.anchorMin = xpRect.anchorMax = new Vector2(0f, 1f);
        xpRect.pivot = new Vector2(0f, 1f);
        xpRect.anchoredPosition = new Vector2(335f, -36f);
        xpRect.sizeDelta = new Vector2(400f, 40f);
        xpLabel = NewLabel(xpRect, 26, TextAnchor.MiddleLeft);
        xpLabel.color = new Color(0.95f, 0.9f, 0.7f);

        if (victoryPanel != null) victoryPanel.transform.SetAsLastSibling();
    }

    void RefreshXpPanel()
    {
        if (levelLabel == null) return;
        levelLabel.text = "PLAYER LEVEL: " + PlayerProgression.Level;
        if (PlayerProgression.IsMaxLevel)
        {
            xpLabel.text = "XP MAX";
            return;
        }
        int percent = Mathf.FloorToInt(PlayerProgression.XpProgress * 100f);
        xpLabel.text = "XP: " + PlayerProgression.Xp + " / " + PlayerProgression.XpToNextLevel + "   " + percent + "%";
    }

    void OnXpChanged(int gained)
    {
        xpFlashUntil = Time.unscaledTime + 0.25f;
        RefreshXpPanel();
    }

    // The bar slides towards the real value; after a level up it fills to the end first, then restarts
    void AnimateXpBar()
    {
        if (xpFill == null) return;
        float target = PlayerProgression.XpProgress;
        if (target < shownXpProgress - 0.001f)
        {
            shownXpProgress = Mathf.MoveTowards(shownXpProgress, 1f, Time.unscaledDeltaTime * 3f);
            if (shownXpProgress >= 1f) shownXpProgress = 0f;
        }
        else shownXpProgress = Mathf.MoveTowards(shownXpProgress, target, Time.unscaledDeltaTime * 1.5f);

        xpFill.anchorMax = new Vector2(Mathf.Clamp01(shownXpProgress), 1f);
        bool flash = Time.unscaledTime < xpFlashUntil;
        xpFillImage.color = flash ? Color.white : DamageNumber.LevelUpColor;
    }

    void OnLeveledUp(int level)
    {
        RefreshXpPanel();
        if (levelLabel == null) return;
        if (levelUpRoutine != null) StopCoroutine(levelUpRoutine);
        levelUpRoutine = StartCoroutine(LevelUpRoutine(level));
    }

    // Big "LEVEL UP!" that pops in, glows and fades, with the new stats under it
    IEnumerator LevelUpRoutine(int level)
    {
        Color gold = DamageNumber.LevelUpColor;
        if (levelUpTitle == null)
        {
            RectTransform titleRect = NewUI("LevelUpTitle", transform);
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.anchoredPosition = new Vector2(0f, 150f);
            titleRect.sizeDelta = new Vector2(1400f, 160f);
            levelUpTitle = NewLabel(titleRect, 110, TextAnchor.MiddleCenter);

            RectTransform detailRect = NewUI("LevelUpDetails", transform);
            detailRect.anchorMin = detailRect.anchorMax = new Vector2(0.5f, 0.5f);
            detailRect.anchoredPosition = new Vector2(0f, 60f);
            detailRect.sizeDelta = new Vector2(1600f, 60f);
            levelUpDetails = NewLabel(detailRect, 34, TextAnchor.MiddleCenter);
        }
        if (victoryPanel != null) victoryPanel.transform.SetAsLastSibling();

        ProgressionSettings s = PlayerProgression.Settings;
        levelUpTitle.text = "LEVEL UP!";
        levelUpDetails.text = "LEVEL " + level + "   HP +" + s.healthPerLevel + "   DMG +" + Mathf.RoundToInt(s.damagePerLevel * 100f) +
            "%   DEF " + Mathf.RoundToInt(PlayerProgression.PlayerDefense * 100f) + "%   REGEN " + PlayerProgression.PlayerRegen.ToString("0.0") + "/s";
        levelUpTitle.gameObject.SetActive(true);
        levelUpDetails.gameObject.SetActive(true);
        Flash(new Color(gold.r, gold.g, gold.b, 0.35f), 0.5f);

        const float duration = 2.4f;
        float timer = 0f;
        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime;
            // Pop in with an overshoot, then a gentle pulse, then fade out
            float pop = timer < 0.25f ? Mathf.Lerp(2.2f, 0.9f, timer / 0.25f) : (timer < 0.4f ? Mathf.Lerp(0.9f, 1f, (timer - 0.25f) / 0.15f) : 1f + Mathf.Sin(timer * 8f) * 0.03f);
            float alpha = timer < duration - 0.5f ? 1f : (duration - timer) / 0.5f;
            Color glow = Color.Lerp(Color.white, gold, Mathf.Clamp01(timer / 0.3f));
            levelUpTitle.rectTransform.localScale = Vector3.one * pop;
            levelUpTitle.color = new Color(glow.r, glow.g, glow.b, alpha);
            levelUpDetails.color = new Color(1f, 1f, 1f, alpha * Mathf.Clamp01((timer - 0.2f) * 4f));
            // The level label in the corner pulses too
            levelLabel.rectTransform.localScale = Vector3.one * (1f + 0.25f * Mathf.Max(0f, 1f - timer / 0.6f));
            levelLabel.color = Color.Lerp(gold, Color.white, Mathf.PingPong(timer * 4f, 1f) * Mathf.Max(0f, 1f - timer / 1.2f));
            yield return null;
        }
        levelUpTitle.gameObject.SetActive(false);
        levelUpDetails.gameObject.SetActive(false);
        levelLabel.rectTransform.localScale = Vector3.one;
        levelLabel.color = gold;
        levelUpRoutine = null;
    }

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
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

    public void ShowBossDefeated()
    {
        SetBossHealth(0f);
        if (bossBarRoot != null) bossBarRoot.SetActive(false);
        ShowBanner("SYSTEM CLEANED", 0f);
        StartCoroutine(ShowVictoryAfterDelay());
    }

    // Mini-boss: no victory screen, the room exit just opens
    public void ShowMiniBossDefeated()
    {
        SetBossHealth(0f);
        if (bossBarRoot != null) bossBarRoot.SetActive(false);
        ShowBanner("THREAT PURGED", 3f);
        ShowMessage("THE EXIT IS UNLOCKED", 4f);
    }

    IEnumerator ShowVictoryAfterDelay()
    {
        yield return new WaitForSeconds(victoryDelay);
        if (victoryPanel != null) victoryPanel.SetActive(true);
        if (victoryFirstButton != null && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(victoryFirstButton);
        }
    }

    // ---------------- Buttons (Main Menu / Victory) ----------------

    public void StartGame()
    {
        TerminalManager.ResetProgress();
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
