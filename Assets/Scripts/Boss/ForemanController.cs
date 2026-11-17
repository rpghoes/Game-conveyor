using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// SECTION 03 mini-boss: the FOREMAN, a huge supervisor robot. It barely moves - it stands on its post
// and runs the machinery of the assembly hall. One attack cycle:
//   1. MACHINERY - drops the presses closest to the Robot (shadows warn ~1 s ahead) and, from phase 2,
//                  reverses the conveyor belts (the arrows flash red first)
//   2. TRACK     - its manipulator, hanging from a ceiling rail, follows the Robot; a faint shadow marks it
//   3. LOCK      - the shadow turns red and blinks, the Foreman raises its arm (Slam animation)
//   4. SLAM      - the manipulator crashes down... and gets STUCK in the floor
//   5. STUCK     - the only moment it can be damaged: hit the manipulator with the press arm
//   6. PULL OUT  - it tears free and rises; short rest
// Phases at 66% and 33% health: more presses, belt reversals, faster tracking, shorter openings.
// About three or four successful cycles defeat it (maxHealth 9 hits, ~3 hits per opening).
// Built from the Corrupted Core boss logic (wind-up -> slam -> vulnerable -> recover).
[RequireComponent(typeof(EnemyHealth))]
public class ForemanController : MonoBehaviour
{
    [System.Serializable]
    public class PhaseSettings
    {
        public string name = "Phase";
        [Tooltip("Presses dropped at the start of each cycle")]
        public int presses = 1;
        public bool reverseBelts = false;
        [Tooltip("How long the manipulator follows the Robot")]
        public float trackTime = 1.6f;
        public float trackSpeed = 7f;
        [Tooltip("Red blinking warning before the slam")]
        public float lockTime = 0.8f;
        [Tooltip("How long the manipulator stays stuck (open to attacks)")]
        public float stuckTime = 2.6f;
        public float restTime = 1.2f;
    }

    [Header("Setup")]
    public string bossName = "FOREMAN";
    [Tooltip("The fight starts when the Robot walks past this X")]
    public float activationX = 6f;
    [Tooltip("Arena limits for the manipulator")]
    public float minX = 2f;
    public float maxX = 34f;
    [Tooltip("World Y of the ceiling rail")]
    public float railY = 19f;
    public LayerMask groundLayer;

    [Header("Manipulator")]
    public Transform manipulator;
    public SpriteRenderer manipulatorRenderer;
    public SpriteRenderer cable;
    public SpriteRenderer targetShadow;
    public float headWidth = 2.2f;
    public float headHeight = 1.4f;
    [Tooltip("How far under the rail the manipulator waits")]
    public float restDrop = 3f;
    public float slamSpeed = 32f;
    public float riseSpeed = 5f;
    public int slamDamage = 1;

    [Header("Phases (start at 100%, 66% and 33% health)")]
    public PhaseSettings[] phases =
    {
        new PhaseSettings { name = "Phase 1", presses = 1, reverseBelts = false, trackTime = 1.6f, trackSpeed = 7f, lockTime = 0.9f, stuckTime = 2.6f, restTime = 1.3f },
        new PhaseSettings { name = "Phase 2", presses = 2, reverseBelts = true, trackTime = 1.4f, trackSpeed = 9f, lockTime = 0.8f, stuckTime = 2.3f, restTime = 1f },
        new PhaseSettings { name = "Phase 3", presses = 3, reverseBelts = true, trackTime = 1.2f, trackSpeed = 11f, lockTime = 0.7f, stuckTime = 2f, restTime = 0.8f },
    };

    [Header("Colors")]
    public Color armoredColor = new Color(0.75f, 0.72f, 0.68f);
    public Color stuckColor = Color.white;
    public Color lockColor = new Color(1f, 0.25f, 0.2f);

    [Header("Audio")]
    public AudioSource slamSound;
    public AudioSource strainSound;
    public AudioSource deathSound;

    public int CurrentPhase { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsStuck { get; private set; }

    private EnemyHealth health;
    private Animator animator;
    private Transform player;
    private PlayerHealth playerHealth;
    private bool deathHandled;
    private float headY;          // bottom of the manipulator head
    private float restY;
    private int cycle;

    void Start()
    {
        health = GetComponent<EnemyHealth>();
        animator = GetComponent<Animator>();
        if (groundLayer == 0) groundLayer = LayerMask.GetMask("Ground");
        health.invulnerable = true;
        health.damagePoint = manipulator;

        restY = railY - restDrop - headHeight;
        headY = restY;
        if (manipulator != null) manipulator.position = new Vector3(Mathf.Clamp(transform.position.x - 8f, minX, maxX), headY + headHeight * 0.5f, 0f);
        SetShadow(0f, Color.black);
        SetHeadColor(armoredColor);
        UpdateCable();

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            player = playerObject.transform;
            playerHealth = playerObject.GetComponent<PlayerHealth>();
        }
    }

    void Update()
    {
        if (health.IsDead)
        {
            if (!deathHandled) OnDeath();
            return;
        }
        if (player == null) return;

        if (!IsActive)
        {
            if (player.position.x > activationX) StartCoroutine(Activate());
            return;
        }

        if (UIManager.Instance != null) UIManager.Instance.SetBossHealth(health.CurrentHealth, health.MaxHealth);
        float healthPercent = (float)health.CurrentHealth / health.MaxHealth;
        int phase = healthPercent > 0.66f ? 0 : (healthPercent > 0.33f ? 1 : 2);
        if (phase != CurrentPhase) EnterPhase(phase);
    }

    IEnumerator Activate()
    {
        IsActive = true;
        UIManager ui = UIManager.Instance;
        if (ui != null)
        {
            ui.ShowBossBar(bossName);
            ui.ShowBanner(bossName, 2.5f);
            ui.ShowTerminalLog("FOREMAN // ASSEMBLY HALL", new[] { "DEFECTIVE UNIT ON THE LINE.", "REMOVING." }, 2f);
        }
        PlayAnim("Slam");
        CameraFollow.Shake(0.15f, 0.4f);
        yield return new WaitForSeconds(1.5f);
        StartCoroutine(AttackLoop());
    }

    void EnterPhase(int phase)
    {
        CurrentPhase = phase;
        if (UIManager.Instance != null) UIManager.Instance.ShowMessage("FOREMAN: LINE SPEED INCREASED", 2f);
    }

    IEnumerator AttackLoop()
    {
        while (!health.IsDead)
        {
            PhaseSettings p = phases[Mathf.Clamp(CurrentPhase, 0, phases.Length - 1)];
            cycle++;

            // 1. Machinery
            if (p.reverseBelts && cycle % 2 == 0)
            {
                ConveyorBelt.ReverseAll();
                yield return new WaitForSeconds(0.6f);
            }
            if (p.presses > 0)
            {
                foreach (IndustrialPress press in ClosestPresses(p.presses))
                {
                    press.Slam();
                    yield return new WaitForSeconds(0.3f);
                }
                yield return new WaitForSeconds(0.9f);
            }
            if (health.IsDead) yield break;

            // 2. Track the Robot
            float timer = 0f;
            while (timer < p.trackTime && !health.IsDead)
            {
                timer += Time.deltaTime;
                float targetX = Mathf.Clamp(player.position.x, minX, maxX);
                MoveHeadX(Mathf.MoveTowards(manipulator.position.x, targetX, p.trackSpeed * Time.deltaTime));
                SetShadow(0.35f, Color.black);
                yield return null;
            }

            // 3. Lock: the shadow blinks red, the Foreman raises its arm
            PlayAnim("Slam");
            timer = 0f;
            while (timer < p.lockTime && !health.IsDead)
            {
                timer += Time.deltaTime;
                bool on = Mathf.FloorToInt(timer * (8f + 10f * timer / p.lockTime)) % 2 == 0;
                SetShadow(Mathf.Lerp(0.5f, 1f, timer / p.lockTime), on ? lockColor : Color.black);
                SetHeadColor(on ? lockColor : armoredColor);
                yield return null;
            }
            SetHeadColor(armoredColor);
            if (health.IsDead) yield break;

            // 4. Slam
            float floorY = FloorUnder(manipulator.position.x);
            bool hit = false;
            while (headY > floorY && !health.IsDead)
            {
                headY = Mathf.Max(headY - slamSpeed * Time.deltaTime, floorY);
                UpdateHeadPosition();
                if (!hit) hit = CrushCheck();
                yield return null;
            }
            if (!hit) CrushCheck();
            if (health.IsDead) yield break;
            if (slamSound != null) slamSound.Play();
            CameraFollow.Shake(0.3f, 0.3f);
            Fx.Sparks(new Vector2(manipulator.position.x - headWidth * 0.5f, floorY), new Color(1f, 0.8f, 0.4f), 10, 6f);
            Fx.Sparks(new Vector2(manipulator.position.x + headWidth * 0.5f, floorY), new Color(1f, 0.8f, 0.4f), 10, 6f);
            Fx.Shockwave(new Vector2(manipulator.position.x, floorY + 0.2f), new Color(1f, 0.85f, 0.6f), 4f, 0.35f);
            SetShadow(0f, Color.black);

            // 5. Stuck: open to attacks
            IsStuck = true;
            health.invulnerable = false;
            if (strainSound != null) strainSound.Play();
            if (UIManager.Instance != null) UIManager.Instance.ShowMessage("THE MANIPULATOR IS STUCK - STRIKE IT", 1.5f);
            timer = 0f;
            float nextSpark = 0f;
            while (timer < p.stuckTime && !health.IsDead)
            {
                timer += Time.deltaTime;
                // It strains to pull free: shaking, bright, sparking
                float shake = Mathf.Sin(timer * 60f) * 0.05f;
                manipulator.position = new Vector3(manipulator.position.x, headY + headHeight * 0.5f + Mathf.Abs(shake), 0f);
                SetHeadColor(Color.Lerp(stuckColor, new Color(1f, 0.9f, 0.6f), Mathf.PingPong(timer * 4f, 1f)));
                if (timer >= nextSpark)
                {
                    nextSpark = timer + 0.15f;
                    Fx.Sparks(new Vector2(manipulator.position.x + Random.Range(-1f, 1f), floorY + 0.1f), new Color(1f, 0.75f, 0.3f), 3, 3f);
                }
                yield return null;
            }
            IsStuck = false;
            health.invulnerable = true;
            SetHeadColor(armoredColor);
            if (health.IsDead) yield break;

            // 6. Pull out and rest
            Fx.Sparks(new Vector2(manipulator.position.x, floorY + 0.2f), new Color(0.7f, 0.65f, 0.6f), 12, 4f);
            while (headY < restY && !health.IsDead)
            {
                headY = Mathf.Min(headY + riseSpeed * Time.deltaTime, restY);
                UpdateHeadPosition();
                yield return null;
            }
            PlayAnim("Idle");
            yield return new WaitForSeconds(p.restTime);
        }
    }

    // ---------------- Helpers ----------------

    IEnumerable<IndustrialPress> ClosestPresses(int count)
    {
        var list = new List<IndustrialPress>(IndustrialPress.All);
        list.RemoveAll(press => press.IsBusy);
        list.Sort((a, b) => Mathf.Abs(a.transform.position.x - player.position.x).CompareTo(Mathf.Abs(b.transform.position.x - player.position.x)));
        if (list.Count > count) list.RemoveRange(count, list.Count - count);
        return list;
    }

    float FloorUnder(float x)
    {
        RaycastHit2D hit = Physics2D.Raycast(new Vector2(x, restY - 0.1f), Vector2.down, 40f, groundLayer);
        return hit.collider != null ? hit.point.y : 2f;
    }

    bool CrushCheck()
    {
        if (playerHealth == null || playerHealth.IsDead) return false;
        Vector2 chest = (Vector2)player.position + Vector2.up * 0.8f;
        float x = manipulator.position.x;
        if (chest.x > x - headWidth * 0.5f + 0.1f && chest.x < x + headWidth * 0.5f - 0.1f && chest.y < headY + headHeight && chest.y > headY - 0.9f)
        {
            playerHealth.TakeDamage(slamDamage, new Vector2(x, chest.y + 1f), health.DamageMultiplier);
            return true;
        }
        return false;
    }

    void MoveHeadX(float x)
    {
        manipulator.position = new Vector3(x, manipulator.position.y, 0f);
        UpdateCable();
    }

    void UpdateHeadPosition()
    {
        manipulator.position = new Vector3(manipulator.position.x, headY + headHeight * 0.5f, 0f);
        UpdateCable();
    }

    void UpdateCable()
    {
        if (cable == null || manipulator == null) return;
        float top = railY;
        float bottom = headY + headHeight;
        cable.transform.position = new Vector3(manipulator.position.x, (top + bottom) * 0.5f, 0f);
        cable.size = new Vector2(cable.size.x, Mathf.Max(top - bottom, 0.01f));
        if (targetShadow != null) targetShadow.transform.position = new Vector3(manipulator.position.x, FloorUnder(manipulator.position.x) + 0.06f, 0f);
    }

    void SetShadow(float strength, Color color)
    {
        if (targetShadow == null) return;
        targetShadow.enabled = strength > 0f;
        Vector2 size = targetShadow.sprite != null ? (Vector2)targetShadow.sprite.bounds.size : Vector2.one;
        targetShadow.transform.localScale = new Vector3(headWidth * Mathf.Lerp(0.6f, 1.1f, strength) / size.x, 0.45f / size.y, 1f);
        targetShadow.color = new Color(color.r, color.g, color.b, 0.6f * strength);
    }

    void SetHeadColor(Color c)
    {
        if (manipulatorRenderer != null) manipulatorRenderer.color = c;
    }

    void PlayAnim(string state)
    {
        if (animator != null && !health.IsDead) animator.Play(state, 0, 0f);
    }

    void OnDeath()
    {
        deathHandled = true;
        IsStuck = false;
        StopAllCoroutines();
        SetShadow(0f, Color.black);
        if (deathSound != null) deathSound.Play();
        // The hall goes quiet: belts and presses stop
        foreach (ConveyorBelt belt in ConveyorBelt.All.ToArray()) belt.SetSpeed(0f, false);
        foreach (IndustrialPress press in IndustrialPress.All) press.enabled = false;
        StartCoroutine(ManipulatorFalls());
        if (UIManager.Instance != null)
        {
            UIManager.Instance.HideBossBar();
            UIManager.Instance.ShowBanner("FOREMAN: OFFLINE", 3f);
            UIManager.Instance.ShowMessage("THE WAY FORWARD IS OPEN", 3f);
        }
    }

    // The manipulator drops, dead, onto the floor
    IEnumerator ManipulatorFalls()
    {
        if (manipulator == null) yield break;
        SetHeadColor(new Color(0.4f, 0.38f, 0.36f));
        float floorY = FloorUnder(manipulator.position.x);
        float speed = 0f;
        while (headY > floorY)
        {
            speed += 30f * Time.deltaTime;
            headY = Mathf.Max(headY - speed * Time.deltaTime, floorY);
            UpdateHeadPosition();
            yield return null;
        }
        CameraFollow.Shake(0.2f, 0.25f);
        Fx.Sparks(manipulator.position, new Color(1f, 0.7f, 0.3f), 14, 5f);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawLine(new Vector3(activationX, 0f), new Vector3(activationX, railY));
        Gizmos.color = Color.red;
        Gizmos.DrawLine(new Vector3(minX, railY), new Vector3(maxX, railY));
    }
}
