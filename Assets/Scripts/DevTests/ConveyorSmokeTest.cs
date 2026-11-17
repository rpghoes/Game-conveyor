#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

// DEVELOPMENT TEST (editor only, not in builds).
// Plays through CONVEYOR automatically in Play Mode and checks that the mechanics really work at runtime.
// Started by ConveyorSmokeTestLauncher (CONVEYOR > Run Smoke Test, or -executeMethod ConveyorSmokeTestLauncher.RunBatch).
// Writes Logs/ConveyorSmokeTest.txt. Every check is a real observation of the running game, not a code assertion.
public class ConveyorSmokeTest : MonoBehaviour
{
    public static bool ExitWhenDone;

    private readonly List<string> lines = new List<string>();
    private readonly List<string> logErrors = new List<string>();
    private int passed, failed;

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Application.logMessageReceived += OnLog;
    }

    void OnDestroy() { Application.logMessageReceived -= OnLog; }

    void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
        {
            if (message.Contains("Licensing")) return;
            logErrors.Add(SceneManager.GetActiveScene().name + ": " + type + ": " + message + "\n" + stack);
        }
    }

    void Check(bool ok, string what)
    {
        if (ok) passed++; else failed++;
        lines.Add((ok ? "  PASS  " : "  FAIL  ") + what);
    }

    IEnumerator Start()
    {
        float started = Time.realtimeSinceStartup;
        yield return Run("Section 01 - Factory Floor", Section1());
        yield return Run("Section 02 - Storage", Section2());
        yield return Run("Section 03 - Foreman", Section3());
        yield return Run("Section 04 - Relay 3", Section4());
        yield return Run("Section 05 - Director", Section5());
        yield return Run("Ending", Ending());
        yield return Run("Main menu", MainMenu());

        Check(logErrors.Count == 0, "no errors or exceptions in the log (" + logErrors.Count + ")");
        var report = new StringBuilder();
        report.AppendLine("CONVEYOR smoke test - " + System.DateTime.Now + "  (" + (Time.realtimeSinceStartup - started).ToString("0") + " s)");
        foreach (string l in lines) report.AppendLine(l);
        report.AppendLine("RESULT: " + (failed == 0 ? "PASSED" : "FAILED") + "  (" + passed + " passed, " + failed + " failed)");
        foreach (string e in logErrors) report.AppendLine("  LOG: " + e);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/ConveyorSmokeTest.txt", report.ToString());
        Debug.Log(report.ToString());
        GameInput.ClearSimulation();
        Time.timeScale = 1f;
        if (ExitWhenDone) UnityEditor.EditorApplication.Exit(failed == 0 ? 0 : 1);
        else UnityEditor.EditorApplication.isPlaying = false;
    }

    IEnumerator Run(string title, IEnumerator body)
    {
        lines.Add(title);
        GameInput.ClearSimulation();
        yield return body;
        GameInput.ClearSimulation();
    }

    // ---------------- Helpers ----------------

    IEnumerator Load(string scene)
    {
        SceneManager.LoadScene(scene);
        yield return null;
        yield return null;
        Time.timeScale = 1f;
    }

    static GameObject Player => GameObject.FindGameObjectWithTag("Player");

    // Renders the main camera (world only, no UI overlay) to Logs/Screenshots/<name>.png
    static void Capture(string name)
    {
        Camera cam = Camera.main;
        if (cam == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        var rt = new RenderTexture(1280, 720, 24);
        RenderTexture previous = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        cam.targetTexture = previous;
        RenderTexture.active = null;
        Directory.CreateDirectory("Logs/Screenshots");
        File.WriteAllBytes("Logs/Screenshots/" + name + ".png", tex.EncodeToPNG());
        Destroy(tex);
        rt.Release();
    }

    static void Teleport(Vector2 position)
    {
        GameObject p = Player;
        var rb = p.GetComponent<Rigidbody2D>();
        rb.position = position;
        p.transform.position = position;
        rb.linearVelocity = Vector2.zero;
    }

    static IEnumerator Wait(float seconds) { yield return new WaitForSeconds(seconds); }

    IEnumerator WaitUntilOrTimeout(System.Func<bool> condition, float timeout)
    {
        float t = 0f;
        while (!condition() && t < timeout)
        {
            t += Time.deltaTime;
            yield return null;
        }
    }

    // Stand left of target and swing the press arm a few times
    IEnumerator Attack(Vector2 targetPosition, int swings)
    {
        Teleport(new Vector2(targetPosition.x - 1.1f, targetPosition.y));
        var controller = Player.GetComponent<PlayerController>();
        controller.LookTowards(true);
        for (int i = 0; i < swings; i++)
        {
            GameInput.SimulatePress(GameInput.Action.Attack);
            yield return Wait(0.65f);
        }
    }

    // Follows a moving target (knockback, patrol): steps up to it before every swing
    IEnumerator Attack(Transform target, int swings)
    {
        var controller = Player.GetComponent<PlayerController>();
        for (int i = 0; i < swings && target != null; i++)
        {
            Teleport(new Vector2(target.position.x - 1.1f, target.position.y));
            controller.LookTowards(true);
            yield return Wait(0.05f);
            if (target == null) yield break;
            GameInput.SimulatePress(GameInput.Action.Attack);
            yield return Wait(0.65f);
        }
    }

    // Internal state of a Guard for failure reports
    static string Describe(GuardAI guard)
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var sb = new StringBuilder();
        sb.Append(guard.name).Append(" pos=").Append(guard.transform.position).Append(" vel=").Append(guard.GetComponent<Rigidbody2D>().linearVelocity);
        foreach (string field in new[] { "attacking", "noticedPlayer", "pausedUntil", "moveDirection", "leftX", "rightX" })
            sb.Append(' ').Append(field).Append('=').Append(typeof(GuardAI).GetField(field, flags).GetValue(guard));
        sb.Append(" time=").Append(Time.time.ToString("0.00")).Append(" stunned=").Append(guard.GetComponent<EnemyHealth>().IsStunned);
        return sb.ToString();
    }

    void MakePlayerSafe(bool safe)
    {
        var health = Player.GetComponent<PlayerHealth>();
        health.invulnerable = safe;
    }

    // ---------------- Sections ----------------

    IEnumerator Section1()
    {
        RelayManager.ResetProgress();
        PlayerUpgrades.ResetProgress();
        yield return Load("Section_01_FactoryFloor");
        var controller = Player.GetComponent<PlayerController>();
        float startX = Player.transform.position.x;
        Check(!controller.canControl, "intro: no control while riding the transport line");
        yield return Wait(2.5f);
        Check(Player.transform.position.x < startX - 1f, "intro: the belt carries the Robot towards the furnace (" + startX.ToString("0.0") + " -> " + Player.transform.position.x.ToString("0.0") + ")");
        yield return WaitUntilOrTimeout(() => controller.canControl, 6f);
        Check(controller.canControl, "intro: control returns after the belt jams");
        Capture("01_intro_furnace");
        var intro = FindFirstObjectByType<IntroSequence>();
        int recycled = 0;
        foreach (Transform unit in intro.cargo) if (!unit.gameObject.activeSelf) recycled++;
        Check(recycled == 1, "intro: one defective unit recycled in the furnace, the other waits on the jammed belt (" + recycled + " of " + intro.cargo.Length + ")");

        // Walk and jump
        Teleport(new Vector2(25f, 2.1f));
        yield return Wait(0.3f);
        GameInput.SimulateHorizontal(1f);
        yield return Wait(0.5f);
        GameInput.SimulateHorizontal(0f);
        Check(Player.transform.position.x > 27f, "walk: the Robot moves right (x=" + Player.transform.position.x.ToString("0.0") + ")");
        float y0 = Player.transform.position.y;
        GameInput.SimulatePress(GameInput.Action.Jump);
        yield return Wait(0.25f);
        Check(Player.transform.position.y > y0 + 1.5f, "jump: the Robot leaves the ground");
        GameInput.SimulateJumpHeld(false);
        yield return Wait(1f);
        Check(controller.IsGrounded, "jump: the Robot lands again");

        // Dash
        float x0 = Player.transform.position.x;
        controller.LookTowards(true);
        GameInput.SimulatePress(GameInput.Action.Dash);
        yield return Wait(0.25f);
        Check(Player.transform.position.x > x0 + 2f, "dash: a fast burst forward (+" + (Player.transform.position.x - x0).ToString("0.0") + ")");

        // Guard patrol + attack + knockback
        GuardAI guard = FindFirstObjectByType<GuardAI>();
        Check(guard != null, "guards are present");
        if (guard != null)
        {
            // Every guard patrols, also the one on the belt (player parked far away, safe, out of sight)
            MakePlayerSafe(true);
            Teleport(new Vector2(82f, 2.1f));
            GuardAI[] all = FindObjectsByType<GuardAI>(FindObjectsSortMode.None);
            var starts = new Vector2[all.Length];
            for (int i = 0; i < all.Length; i++) starts[i] = all[i].transform.position;
            float[] travelled = new float[all.Length];
            for (int step = 0; step < 30; step++)
            {
                yield return Wait(0.1f);
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null) travelled[i] = Mathf.Max(travelled[i], Vector2.Distance(starts[i], all[i].transform.position));
            }
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                bool patrols = travelled[i] > 0.5f;
                Check(patrols, "guard: " + all[i].name + " at x=" + starts[i].x.ToString("0") + " patrols (" + travelled[i].ToString("0.0") + ")");
                if (!patrols) lines.Add("        diag: " + Describe(all[i]));
            }
            MakePlayerSafe(false);
            MakePlayerSafe(true);
            var hp = guard.GetComponent<EnemyHealth>();
            int before = hp.CurrentHealth;
            yield return Attack(guard.transform, 1);
            Check(hp.CurrentHealth < before || hp.IsDead, "press arm: damages a guard (" + before + " -> " + hp.CurrentHealth + ")");
            yield return Attack(guard.transform, 4);
            yield return Wait(1.5f);
            Check(guard == null || hp.IsDead, "guard: destroyed after a few hits");
            MakePlayerSafe(false);
        }

        Teleport(new Vector2(48f, 2.6f));
        yield return Wait(0.6f);
        Capture("02_section1_belt");
        // Relay 1 + door
        LevelExit exit = FindFirstObjectByType<LevelExit>();
        Check(!exit.IsOpen, "door: locked before relay 1");
        PowerRelay relay = FindFirstObjectByType<PowerRelay>();
        FactoryAmbience ambience = FindFirstObjectByType<FactoryAmbience>();
        Check(ambience != null && ambience.LampCount > 0, "atmosphere: ceiling lamps hang in the hall (" + (ambience != null ? ambience.LampCount : 0) + ")");
        int litBefore = ambience != null ? ambience.PoweredLamps : 0;
        Teleport((Vector2)relay.transform.position + Vector2.up * 0.1f);
        yield return Wait(0.3f);
        GameInput.SimulatePress(GameInput.Action.Interact);
        yield return Wait(0.5f);
        Check(RelayManager.ActivatedCount == 1, "relay 1: activated with the interact button");
        Check(UIManager.Instance != null && UIManager.Instance.IsLogOpen, "relay 1: the factory log window opens");
        Check(exit.IsOpen, "door: opens with relay 1");
        yield return Wait(3.5f);
        if (ambience != null)
        {
            Check(ambience.PoweredLamps > litBefore, "atmosphere: relay 1 brings broken lamps back (" + litBefore + " -> " + ambience.PoweredLamps + " of " + ambience.LampCount + ")");
            string pa = UIManager.Instance != null ? UIManager.Instance.LastAnnouncement : null;
            Check(pa != null && pa.Contains("POWER GRID: 1/3"), "atmosphere: the factory PA announces the power grid (" + pa + ")");
        }

        // Module pickup over the pit
        var module = GameObject.Find("Module_ReinforcedPlating");
        if (module != null)
        {
            var health = Player.GetComponent<PlayerHealth>();
            int maxBefore = health.MaxHealth;
            Teleport(module.transform.position + Vector3.down * 0.5f);
            yield return Wait(0.3f);
            Check(PlayerUpgrades.Has(PlayerUpgrades.Module.ReinforcedPlating) && health.MaxHealth > maxBefore, "module: reinforced plating raises max HP (" + maxBefore + " -> " + health.MaxHealth + ")");
        }
        else Check(false, "module: Module_ReinforcedPlating exists in section 01");

        // Falling into the pit costs a hit and respawns
        {
            var health = Player.GetComponent<PlayerHealth>();
            int before = health.CurrentHealth;
            Teleport(new Vector2(37.5f, 1f));
            yield return Wait(1.5f);
            Check(health.CurrentHealth < before && Player.transform.position.y > 1.5f, "pit: costs health and puts the Robot back");
        }
    }

    IEnumerator Section2()
    {
        yield return Load("Section_02_Storage");
        var health = Player.GetComponent<PlayerHealth>();
        int guardsAtStart = FindObjectsByType<GuardAI>(FindObjectsSortMode.None).Length;
        int alarms = 0;
        System.Action<Vector2> countAlarm = _ => alarms++;
        AlarmSystem.Raised += countAlarm;

        // Press: shadow warns, slam hurts
        IndustrialPress press = FindFirstObjectByType<IndustrialPress>();
        Check(press != null, "presses are present");
        if (press != null)
        {
            press.autoCycle = false;
            yield return WaitUntilOrTimeout(() => !press.IsBusy, 5f);
            Teleport(new Vector2(press.transform.position.x, 2.1f));
            yield return Wait(0.2f);
            int before = health.CurrentHealth;
            press.Slam();
            yield return Wait(0.3f);
            Check(press.shadow != null && press.shadow.enabled, "press: the warning shadow is visible before the slam");
            Capture("03_press_warning");
            Check(health.CurrentHealth == before, "press: no damage during the warning");
            yield return Wait(1.2f);
            Check(health.CurrentHealth < before, "press: the slam hurts the Robot under it (" + before + " -> " + health.CurrentHealth + ")");
            yield return Wait(2f);
        }

        // Scanner beam -> alarm -> guards from the service doors
        // The scanner that patrols over the main floor (the loft scanner can't see the floor through the loft - by design)
        ScannerAI scanner = null;
        foreach (ScannerAI candidate in FindObjectsByType<ScannerAI>(FindObjectsSortMode.None))
            if (scanner == null || candidate.transform.position.y < scanner.transform.position.y) scanner = candidate;
        Check(scanner != null, "scanners are present");
        if (scanner != null)
        {
            MakePlayerSafe(true);
            float scannerX = scanner.transform.position.x;
            Teleport(new Vector2(scannerX, 2.1f));
            yield return WaitUntilOrTimeout(() => scanner == null || scanner.IsAlarmed, 8f);
            Check(scanner != null && scanner.IsAlarmed, "scanner: finds the Robot with its beam and raises the alarm");
            Capture("04_scanner_alarm");
            Check(AlarmSystem.IsActive, "alarm: is active");
            yield return Wait(2f);
            // Compared with the section start: an earlier alarm may already have used the doors (1 Guard per door)
            int guardsAfter = FindObjectsByType<GuardAI>(FindObjectsSortMode.None).Length;
            Check(guardsAfter > guardsAtStart, "alarm: guards come out of the service doors (" + guardsAtStart + " at start -> " + guardsAfter + ", alarms raised: " + alarms + ")");
            if (scanner != null)
            {
                var sh = scanner.GetComponent<EnemyHealth>();
                yield return Attack(scanner.transform.position + Vector3.down * 0.8f, 1);
                yield return Wait(0.5f);
                Check(sh == null || sh.IsDead, "scanner: disabled by one hit");
            }
            MakePlayerSafe(false);
        }

        AlarmSystem.Raised -= countAlarm;

        // Belt carries the Robot
        ConveyorBelt belt = FindFirstObjectByType<ConveyorBelt>();
        if (belt != null)
        {
            Teleport(new Vector2(belt.transform.position.x, belt.transform.position.y + 0.6f));
            yield return Wait(0.2f);
            float bx = Player.transform.position.x;
            yield return Wait(0.8f);
            float moved = Player.transform.position.x - bx;
            Check(Mathf.Sign(moved) == Mathf.Sign(belt.CurrentSpeed) && Mathf.Abs(moved) > 0.8f, "belt: carries the standing Robot (" + moved.ToString("0.0") + ")");
        }

        // Relay 2
        PowerRelay relay = FindFirstObjectByType<PowerRelay>();
        Teleport((Vector2)relay.transform.position + Vector2.up * 0.1f);
        yield return Wait(0.3f);
        GameInput.SimulatePress(GameInput.Action.Interact);
        yield return Wait(0.3f);
        Check(RelayManager.ActivatedCount == 2, "relay 2: activated");
        Check(FindFirstObjectByType<LevelExit>().IsOpen, "door: opens with two relays");

        // Energy shield absorbs a hit
        var shield = GameObject.Find("Item_EnergyShield");
        if (shield != null)
        {
            Teleport(shield.transform.position + Vector3.down * 0.5f);
            yield return Wait(0.3f);
            var buffs = Player.GetComponent<PlayerBuffs>();
            Check(buffs.ShieldHitsLeft > 0, "energy shield: picked up");
            yield return Wait(1.3f);  // let the respawn/hit invincibility end
            int before = health.CurrentHealth;
            health.TakeDamage(1, (Vector2)Player.transform.position + Vector2.right);
            Check(health.CurrentHealth == before, "energy shield: absorbs a hit");
        }
    }

    IEnumerator Section3()
    {
        yield return Load("Section_03_Foreman");
        ForemanController foreman = FindFirstObjectByType<ForemanController>();
        Check(foreman != null, "foreman: present");
        if (foreman == null) yield break;
        var fh = foreman.GetComponent<EnemyHealth>();
        LevelExit exit = FindFirstObjectByType<LevelExit>();
        Check(!exit.IsOpen, "door: locked while the Foreman is on duty");
        MakePlayerSafe(true);

        Teleport(new Vector2(12f, 2.1f));
        yield return WaitUntilOrTimeout(() => foreman.IsActive, 3f);
        Check(foreman.IsActive, "foreman: the fight starts");
        int before = fh.CurrentHealth;
        fh.TakeDamage(PlayerProgression.HitValue, Vector2.zero);
        Check(fh.CurrentHealth == before, "foreman: armoured while not stuck");

        // The camera leans towards the Foreman: its post stays in view even from the left of the hall
        Teleport(new Vector2(7f, 2.1f));
        yield return Wait(1.2f);
        Camera view = Camera.main;
        CameraFollow follow = CameraFollow.Instance;
        float halfWidth = view.orthographicSize * view.aspect;
        float px = Player.transform.position.x;
        // As far towards the Foreman as the Robot's edge margin (and the room bounds) allow
        float expected = Mathf.Min(px + halfWidth - follow.focusEdgeMargin, (px + foreman.transform.position.x) * 0.5f, follow.maxBounds.x - halfWidth);
        float camX = view.transform.position.x;
        Check(follow.focus == foreman.transform && camX > expected - 0.8f && px - (camX - halfWidth) > follow.focusEdgeMargin - 0.8f,
            "camera: leans towards the Foreman, the Robot stays in view (cam " + camX.ToString("0.0") + ", expected " + expected.ToString("0.0")
            + ", right edge " + (camX + halfWidth).ToString("0.0") + ", Foreman " + foreman.transform.position.x.ToString("0.0") + ", aspect " + view.aspect.ToString("0.00") + ")");

        bool belts = false;
        int cycles = 0;
        float t = 0f;
        float[] beltSpeeds = new float[ConveyorBelt.All.Count];
        for (int i = 0; i < beltSpeeds.Length; i++) beltSpeeds[i] = ConveyorBelt.All[i].CurrentSpeed;
        while (!fh.IsDead && t < 90f)
        {
            yield return WaitUntilOrTimeout(() => foreman.IsStuck || fh.IsDead, 20f);
            if (fh.IsDead) break;
            if (!foreman.IsStuck) { Check(false, "foreman: the manipulator gets stuck in the floor"); break; }
            cycles++;
            if (cycles == 1) Capture("05_foreman_stuck");
            yield return Attack(foreman.manipulator.position + Vector3.down * 0.6f, 3);
            t += 2f;
            for (int i = 0; i < beltSpeeds.Length && i < ConveyorBelt.All.Count; i++)
                if (Mathf.Sign(ConveyorBelt.All[i].CurrentSpeed) != Mathf.Sign(beltSpeeds[i])) belts = true;
            yield return WaitUntilOrTimeout(() => !foreman.IsStuck, 5f);
        }
        Check(fh.IsDead, "foreman: defeated by striking the stuck manipulator (" + cycles + " cycles)");
        Check(cycles >= 3 && cycles <= 5, "foreman: takes about 3-4 cycles (" + cycles + ")");
        Check(belts, "foreman: reversed the conveyor belts during the fight");
        yield return Wait(2f);
        Check(exit.IsOpen, "door: opens after the Foreman");
        MakePlayerSafe(false);
    }

    IEnumerator Section4()
    {
        yield return Load("Section_04_Relay");
        var module = GameObject.Find("Module_ServoOverdrive");
        if (module != null)
        {
            Teleport(module.transform.position + Vector3.down * 0.5f);
            yield return Wait(0.3f);
            Check(PlayerUpgrades.Has(PlayerUpgrades.Module.ServoOverdrive), "reward: servo overdrive module installed");
        }
        PowerRelay relay = FindFirstObjectByType<PowerRelay>();
        Teleport((Vector2)relay.transform.position + Vector2.up * 0.1f);
        yield return Wait(0.3f);
        GameInput.SimulatePress(GameInput.Action.Interact);
        yield return Wait(0.3f);
        Check(RelayManager.ActivatedCount == 3, "relay 3: activated, all relays online");
        Check(FindFirstObjectByType<LevelExit>().IsOpen, "door to the control room: open");
    }

    IEnumerator Section5()
    {
        yield return Load("Section_05_ControlRoom");
        DirectorController director = FindFirstObjectByType<DirectorController>();
        Check(director != null, "director: present");
        if (director == null) yield break;
        LevelExit exit = FindFirstObjectByType<LevelExit>();
        Check(!exit.IsOpen, "surface hatch: locked by the Director");
        MakePlayerSafe(true);

        Teleport(new Vector2(12f, 2.1f));
        yield return WaitUntilOrTimeout(() => director.Phase == 1, 3f);
        Check(director.Phase == 1, "director: phase 1 starts");
        yield return Wait(1.5f);
        Capture("06_director_phase1");
        // Destroy the wall manipulators with the press arm
        foreach (WallManipulator m in director.wallManipulators)
        {
            var mh = m.GetComponent<EnemyHealth>();
            Vector2 pos = m.transform.position;
            // stand on the inner side
            float side = pos.x < 24f ? 1f : -1f;
            for (int i = 0; i < 8 && !mh.IsDead; i++)
            {
                Teleport(new Vector2(pos.x + side * 1.1f, 2.1f));
                Player.GetComponent<PlayerController>().LookTowards(side < 0f);
                GameInput.SimulatePress(GameInput.Action.Attack);
                yield return Wait(0.65f);
            }
            Check(mh.IsDead, "director phase 1: " + m.name + " destroyed by the press arm");
        }
        yield return WaitUntilOrTimeout(() => director.Phase == 2, 4f);
        Check(director.Phase == 2, "director: phase 2 (security)");
        yield return Wait(3f);
        int guards = FindObjectsByType<GuardAI>(FindObjectsSortMode.None).Length;
        int scanners = FindObjectsByType<ScannerAI>(FindObjectsSortMode.None).Length;
        Check(guards > 0 && scanners > 0, "director phase 2: summons guards and scanners (" + guards + ", " + scanners + ")");
        // Clear both waves
        float t = 0f;
        while (director.Phase == 2 && t < 30f)
        {
            foreach (EnemyHealth e in FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None))
                if (!e.CompareTag("Boss") && !e.IsDead) e.TakeDamage(e.CurrentHealth, Vector2.zero, true);
            yield return Wait(0.5f);
            t += 0.5f;
        }
        Check(director.Phase == 3, "director: phase 3 after both waves");
        yield return Wait(1.5f);
        Check(director.redLight != null && director.redLight.enabled, "director phase 3: red light");
        Check(!director.controlPanel.invulnerable, "director phase 3: the control panel opens");
        Vector3 crusherStart = director.leftCrusher.position;
        yield return Wait(3f);
        Check(director.leftCrusher.position.x > crusherStart.x + 1f, "director phase 3: the arena narrows");
        Capture("07_director_phase3");
        for (int i = 0; i < 10 && !director.controlPanel.IsDead; i++)
            yield return Attack(director.controlPanel.transform.position, 1);
        Check(director.controlPanel.IsDead, "director: final strikes on the control panel");
        yield return Wait(6f);
        Check(director.Phase == 4, "director: defeat sequence");
        Check(exit.IsOpen, "surface hatch: opens");
        MakePlayerSafe(false);
    }

    IEnumerator Ending()
    {
        yield return Load("Ending_Surface");
        var ending = FindFirstObjectByType<EndingSequence>();
        Check(ending != null, "ending: sequence present");
        yield return Wait(4f);
        Check(GameObject.Find("Unit0412") != null, "ending: Unit 0412 on the surface");
        yield return Wait(6f);
        Capture("08_ending_surface");
        yield return Wait(14f);
        Capture("09_ending_factory");
        yield return Wait(20f);
        GameObject button = GameObject.Find("MainMenuButton");
        var final = GameObject.Find("Final");
        Check(final != null && final.GetComponent<UnityEngine.UI.Text>().text.Contains("LEVEL 1 COMPLETED"), "ending: final screen 'LEVEL 1 COMPLETED. NEXT: SURFACE.'");
        Check(button != null && button.activeInHierarchy, "ending: main menu button shown");
    }

    IEnumerator MainMenu()
    {
        yield return Load("MainMenu");
        Check(FindFirstObjectByType<UIManager>() != null, "main menu: loads");
        yield return Wait(1f);
    }
}
#endif
