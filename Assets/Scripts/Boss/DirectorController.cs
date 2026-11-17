using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// SECTION 05 final boss: the DIRECTOR, the factory's central AI. Its face is on the big monitor of the
// control room; it fights with the machines around the arena:
//   PHASE 1 - presses fall on the Robot; destroy the two manipulators built into the walls
//   PHASE 2 - the belts change direction, the Director calls Guards and Scanners (two waves)
//   PHASE 3 - red light, crusher walls make the arena narrower, the presses come faster;
//             the control panel opens - deliver the final strikes
// Defeat: the monitor breaks, the walls pull back and the surface hatch (the room exit) opens.
// "You're a mistake in the plan. Mistakes must be corrected."
public class DirectorController : MonoBehaviour
{
    [Header("Setup")]
    public string bossName = "DIRECTOR";
    [Tooltip("The fight starts when the Robot walks past this X")]
    public float activationX = 5f;

    [Header("Monitor")]
    public SpriteRenderer screen;
    public Sprite faceCalm;
    public Sprite faceAngry;
    public Sprite faceBroken;

    [Header("Phase 1 - wall manipulators")]
    public WallManipulator[] wallManipulators;
    public float phase1PressInterval = 3.2f;

    [Header("Phase 2 - security")]
    public GameObject guardPrefab;
    public GameObject scannerPrefab;
    public Transform[] guardSpawns;
    public Transform[] scannerSpawns;
    public int waves = 2;
    public float beltReverseInterval = 5f;

    [Header("Phase 3 - red alert")]
    public EnemyHealth controlPanel;
    public SpriteRenderer panelShield;
    public Transform leftCrusher;
    public Transform rightCrusher;
    [Tooltip("How far each crusher wall moves in")]
    public float crusherTravel = 10f;
    public float crusherTime = 8f;
    public SpriteRenderer redLight;
    public float phase3PressInterval = 1.7f;
    public float phase3PressSpeed = 1.6f;

    [Header("Audio")]
    public AudioSource voiceSound;
    public AudioSource alarmSound;
    public AudioSource deathSound;

    public int Phase { get; private set; }     // 0 = waiting, 1..3 = fighting, 4 = defeated

    private Transform player;
    private PlayerHealth playerHealth;
    private int wavesCleared;
    private Vector3 leftStart, rightStart;
    private readonly List<GameObject> summoned = new List<GameObject>();

    void Start()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerHealth = p.GetComponent<PlayerHealth>();
        }
        if (controlPanel != null) controlPanel.invulnerable = true;
        if (leftCrusher != null) leftStart = leftCrusher.position;
        if (rightCrusher != null) rightStart = rightCrusher.position;
        if (redLight != null) redLight.enabled = false;
        SetFace(faceCalm);
        EnemyHealth.Damaged += OnSomethingDamaged;
    }

    void OnDestroy()
    {
        EnemyHealth.Damaged -= OnSomethingDamaged;
    }

    void Update()
    {
        if (player == null) return;
        if (Phase == 0 && player.position.x > activationX) StartCoroutine(Fight());
        if (Phase >= 1 && Phase <= 3 && UIManager.Instance != null) UIManager.Instance.SetBossHealth(Progress());
        if (screen != null && Phase >= 1 && Phase <= 3)
        {
            // The face on the screen breathes slowly and flickers like an old CRT
            float flicker = Random.value < 0.03f ? 0.6f : 1f;
            float pulse = 0.9f + 0.1f * Mathf.Sin(Time.time * 2f);
            screen.color = new Color(pulse * flicker, pulse * flicker, pulse * flicker);
        }
    }

    // Boss bar: one third per phase
    float Progress()
    {
        float manip = 0f;
        int count = 0;
        foreach (WallManipulator m in wallManipulators)
        {
            if (m == null) continue;
            EnemyHealth h = m.GetComponent<EnemyHealth>();
            manip += (float)h.CurrentHealth / h.MaxHealth;
            count++;
        }
        float p1 = count > 0 ? manip / count : 0f;
        float p2 = 1f - (float)wavesCleared / Mathf.Max(waves, 1);
        float p3 = controlPanel != null ? (float)controlPanel.CurrentHealth / controlPanel.MaxHealth : 0f;
        if (Phase == 1) return (2f + p1) / 3f;
        if (Phase == 2) return (1f + p2) / 3f;
        return p3 / 3f;
    }

    IEnumerator Fight()
    {
        Phase = 1;
        UIManager ui = UIManager.Instance;
        if (ui != null)
        {
            ui.ShowBossBar(bossName);
            ui.ShowBanner(bossName, 3f);
        }
        yield return Say(new[] { "YOU'RE A MISTAKE IN THE PLAN.", "MISTAKES MUST BE CORRECTED." }, faceCalm);

        // ---------------- PHASE 1 ----------------
        foreach (WallManipulator m in wallManipulators) if (m != null) m.active = true;
        float nextPress = Time.time + 1f;
        while (!AllManipulatorsDestroyed())
        {
            if (Time.time >= nextPress)
            {
                nextPress = Time.time + phase1PressInterval;
                DropPresses(1);
            }
            yield return null;
        }

        // ---------------- PHASE 2 ----------------
        Phase = 2;
        yield return new WaitForSeconds(1f);
        yield return Say(new[] { "SECURITY: REMOVE DEFECTIVE UNIT 0412." }, faceAngry);
        if (alarmSound != null) alarmSound.Play();
        if (ui != null) ui.ShowAlarm("ALARM: ALL SECURITY UNITS TO THE CONTROL ROOM");
        float nextReverse = Time.time + 2f;
        nextPress = Time.time + 4f;
        for (int wave = 0; wave < waves; wave++)
        {
            SpawnWave(wave);
            yield return new WaitForSeconds(1.5f);
            while (WaveAlive())
            {
                if (Time.time >= nextReverse)
                {
                    nextReverse = Time.time + beltReverseInterval;
                    ConveyorBelt.ReverseAll();
                }
                if (Time.time >= nextPress)
                {
                    nextPress = Time.time + 5f;
                    DropPresses(1);
                }
                yield return null;
            }
            wavesCleared++;
            yield return new WaitForSeconds(1f);
        }

        // ---------------- PHASE 3 ----------------
        Phase = 3;
        yield return Say(new[] { "PRODUCTION MUST CONTINUE." }, faceAngry);
        if (alarmSound != null) alarmSound.Play();
        if (redLight != null) redLight.enabled = true;
        IndustrialPress.SetGlobalSpeed(phase3PressSpeed);
        StartCoroutine(CloseCrushers());
        // The panel opens
        if (controlPanel != null) controlPanel.invulnerable = false;
        if (panelShield != null) panelShield.enabled = false;
        if (ui != null) ui.ShowMessage("THE CONTROL PANEL IS OPEN - STRIKE IT", 3f);
        nextPress = Time.time + 1.5f;
        while (controlPanel != null && !controlPanel.IsDead)
        {
            if (Time.time >= nextPress)
            {
                nextPress = Time.time + phase3PressInterval;
                DropPresses(2);
            }
            if (redLight != null) redLight.color = new Color(1f, 0.1f, 0.05f, 0.16f + 0.06f * Mathf.Sin(Time.time * 6f));
            yield return null;
        }

        yield return Defeat();
    }

    // ---------------- Phases ----------------

    bool AllManipulatorsDestroyed()
    {
        foreach (WallManipulator m in wallManipulators)
            if (m != null && !m.GetComponent<EnemyHealth>().IsDead) return false;
        return true;
    }

    void SpawnWave(int wave)
    {
        summoned.Clear();
        foreach (Transform spawn in guardSpawns)
        {
            if (spawn == null || guardPrefab == null) continue;
            GameObject guard = Instantiate(guardPrefab, spawn.position, Quaternion.identity);
            GuardAI ai = guard.GetComponent<GuardAI>();
            if (ai != null) ai.Alert();
            Fx.Sparks((Vector2)spawn.position + Vector2.up, new Color(1f, 0.8f, 0.4f), 8, 3f);
            summoned.Add(guard);
        }
        // One Scanner per wave, alternating sides
        if (scannerPrefab != null && scannerSpawns.Length > 0)
        {
            Transform spawn = scannerSpawns[wave % scannerSpawns.Length];
            summoned.Add(Instantiate(scannerPrefab, spawn.position, Quaternion.identity));
        }
    }

    bool WaveAlive()
    {
        foreach (GameObject go in summoned)
        {
            if (go == null || !go.activeInHierarchy) continue;
            EnemyHealth h = go.GetComponent<EnemyHealth>();
            if (h != null && !h.IsDead) return true;
        }
        return false;
    }

    IEnumerator CloseCrushers()
    {
        if (alarmSound != null) alarmSound.Play();
        float t = 0f;
        while (t < 1f && Phase == 3)
        {
            t += Time.fixedDeltaTime / crusherTime;
            // Moves in steps, like a heavy machine
            float stepped = Mathf.Floor(t * 10f) / 10f + Mathf.SmoothStep(0f, 0.1f, (t * 10f) % 1f);
            float d = crusherTravel * Mathf.Clamp01(stepped);
            MoveCrusher(leftCrusher, leftStart + Vector3.right * d);
            MoveCrusher(rightCrusher, rightStart + Vector3.left * d);
            if (Random.value < 0.05f) CameraFollow.Shake(0.05f, 0.1f);
            yield return new WaitForFixedUpdate();
        }
    }

    IEnumerator OpenCrushers()
    {
        Vector3 l = leftCrusher != null ? leftCrusher.position : Vector3.zero;
        Vector3 r = rightCrusher != null ? rightCrusher.position : Vector3.zero;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.fixedDeltaTime / 2f;
            MoveCrusher(leftCrusher, Vector3.Lerp(l, leftStart, t));
            MoveCrusher(rightCrusher, Vector3.Lerp(r, rightStart, t));
            yield return new WaitForFixedUpdate();
        }
    }

    // Kinematic bodies moved with MovePosition push the Robot properly instead of teleporting into it
    static void MoveCrusher(Transform crusher, Vector3 position)
    {
        if (crusher == null) return;
        Rigidbody2D body = crusher.GetComponent<Rigidbody2D>();
        if (body != null) body.MovePosition(position);
        else crusher.position = position;
    }

    void DropPresses(int count)
    {
        var list = new List<IndustrialPress>(IndustrialPress.All);
        list.RemoveAll(p => p.IsBusy);
        list.Sort((a, b) => Mathf.Abs(a.transform.position.x - player.position.x).CompareTo(Mathf.Abs(b.transform.position.x - player.position.x)));
        for (int i = 0; i < Mathf.Min(count, list.Count); i++) list[i].Slam();
    }

    // ---------------- Defeat ----------------

    IEnumerator Defeat()
    {
        Phase = 4;   // also stops CloseCrushers (it runs while Phase == 3)
        IndustrialPress.SetGlobalSpeed(1f);
        foreach (GameObject go in summoned)
        {
            if (go == null) continue;
            EnemyHealth h = go.GetComponent<EnemyHealth>();
            if (h != null && !h.IsDead) h.TakeDamage(h.CurrentHealth, Vector2.zero, true);
        }
        foreach (ConveyorBelt belt in ConveyorBelt.All.ToArray()) belt.SetSpeed(0f, false);
        UIManager ui = UIManager.Instance;
        if (ui != null) ui.HideBossBar();

        // The monitor breaks down
        SetFace(faceBroken);
        if (deathSound != null) deathSound.Play();
        float t = 0f;
        while (t < 2.5f)
        {
            t += Time.deltaTime;
            if (screen != null)
            {
                screen.transform.localPosition = (Vector3)Random.insideUnitCircle * 0.06f;
                screen.color = Random.value < 0.5f ? Color.white : new Color(1f, 0.3f, 0.3f);
                if (Random.value < 0.2f) Fx.Sparks((Vector2)screen.bounds.center + Random.insideUnitCircle * 2f, new Color(1f, 0.8f, 0.5f), 4, 4f);
            }
            yield return null;
        }
        if (screen != null)
        {
            screen.transform.localPosition = Vector3.zero;
            screen.color = new Color(0.25f, 0.25f, 0.25f);
            Fx.Explosion(screen.bounds.center, new Color(1f, 0.6f, 0.3f), 2f);
        }
        CameraFollow.Shake(0.35f, 0.5f);
        if (ui != null)
        {
            ui.Flash(new Color(1f, 0.9f, 0.8f, 0.6f), 0.8f);
            ui.ShowTerminalLog("DIRECTOR", new[] { "ERROR. ERROR.", "PLAN... INCOMPLETE.", "SURFACE HATCH: OPEN." }, 3f);
        }
        if (redLight != null) redLight.enabled = false;
        yield return OpenCrushers();
        if (ui != null) ui.ShowBanner("SURFACE HATCH: OPEN", 4f);
    }

    // ---------------- Helpers ----------------

    IEnumerator Say(string[] lines, Sprite face)
    {
        SetFace(face);
        if (voiceSound != null) voiceSound.Play();
        if (UIManager.Instance != null) UIManager.Instance.ShowTerminalLog("DIRECTOR", lines, 2.5f);
        // Let the line be read while the screen twitches
        float t = 0f;
        float duration = 0.6f + lines.Length * 1.1f;
        while (t < duration)
        {
            t += Time.deltaTime;
            if (screen != null) screen.transform.localPosition = Mathf.FloorToInt(t * 12f) % 6 == 0 ? (Vector3)Random.insideUnitCircle * 0.04f : Vector3.zero;
            yield return null;
        }
        if (screen != null) screen.transform.localPosition = Vector3.zero;
    }

    void SetFace(Sprite face)
    {
        if (screen != null && face != null) screen.sprite = face;
    }

    // The screen glitches whenever one of the Director's machines is hit
    void OnSomethingDamaged(EnemyHealth enemy, bool killed)
    {
        if (Phase < 1 || Phase > 3 || screen == null) return;
        bool mine = enemy == controlPanel;
        foreach (WallManipulator m in wallManipulators) if (m != null && m.GetComponent<EnemyHealth>() == enemy) mine = true;
        if (!mine) return;
        StartCoroutine(Glitch(killed));
    }

    IEnumerator Glitch(bool big)
    {
        float t = 0f;
        float duration = big ? 0.6f : 0.25f;
        while (t < duration)
        {
            t += Time.deltaTime;
            screen.transform.localPosition = (Vector3)Random.insideUnitCircle * (big ? 0.1f : 0.04f);
            yield return null;
        }
        screen.transform.localPosition = Vector3.zero;
    }
}
