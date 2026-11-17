using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The final boss: the Core Sentinel, a heavy walker that guards the Core Chamber (Room 10).
// It walks towards the Robot and picks one of five attacks, each with a clear warning:
//   LASER    - charges its core, a thin red line shows the aim, then fires a beam across the arena (jump over it)
//   SLASH    - short wind-up flash, then a crescent swipe in front of it (only when the Robot is close)
//   MISSILES - launches its head as homing rockets
//   SLAM     - stomps the floor: eruption around itself + shockwaves along the floor (+ eruption pillars in phase 2/3)
//   DASH     - crouches, then charges across the arena (jump over it)
// The Sentinel can be hit at any time. Phases at 66% and 33% health make it faster and more aggressive.
// The art faces RIGHT; the sprite is flipped to face left.
[RequireComponent(typeof(EnemyHealth), typeof(Rigidbody2D))]
public class SentinelBossController : MonoBehaviour
{
    [System.Serializable]
    public class PhaseSettings
    {
        public string name = "Phase";
        public float walkSpeed = 2f;
        [Tooltip("Pause between attacks")]
        public float restTime = 1.2f;
        [Tooltip("Multiplies the speed of all animations and warnings")]
        public float tempo = 1f;
        public int rockets = 1;
        public int eruptionPillars = 0;
        public float shockwaveSpeed = 7f;
        public float dashSpeed = 14f;
        [Tooltip("Laser shots in a row")]
        public int laserShots = 1;
    }

    [Header("Setup")]
    public string bossName = "CORE SENTINEL";
    [Tooltip("The fight starts when the player gets this close")]
    public float activationRange = 16f;
    [Tooltip("World Y of the arena floor surface (the boss feet)")]
    public float floorY = 2f;
    [Tooltip("Left/right limits of the boss movement")]
    public float minX = 4f;
    public float maxX = 44f;

    [Header("Body (local, art facing right)")]
    [Tooltip("Where the laser and core effects come from")]
    public Vector2 coreOffset = new Vector2(0.65f, 1.05f);
    [Tooltip("Where the rockets launch from")]
    public Vector2 headOffset = new Vector2(-0.3f, 2.6f);
    [Tooltip("Half width / height of the body for contact damage")]
    public Vector2 bodyHalfSize = new Vector2(1.3f, 1.4f);

    [Header("Attacks")]
    public int contactDamage = 1;
    public float slashRange = 3.6f;
    public float laserLength = 60f;
    public float laserWidth = 0.55f;
    public float laserTime = 0.6f;
    public float rocketSpeed = 7f;
    public float rocketTurnRate = 140f;
    public float rocketScale = 0.8f;
    public Sprite[] rocketFrames;
    public float slamRadius = 3.2f;

    [Header("Phases (start at 100%, 66% and 33% health)")]
    public PhaseSettings[] phases =
    {
        new PhaseSettings { name = "Phase 1", walkSpeed = 2f, restTime = 1.3f, tempo = 1f, rockets = 1, eruptionPillars = 0, shockwaveSpeed = 7f, dashSpeed = 13f, laserShots = 1 },
        new PhaseSettings { name = "Phase 2", walkSpeed = 2.6f, restTime = 0.9f, tempo = 1.2f, rockets = 2, eruptionPillars = 2, shockwaveSpeed = 8.5f, dashSpeed = 16f, laserShots = 1 },
        new PhaseSettings { name = "Phase 3", walkSpeed = 3.2f, restTime = 0.55f, tempo = 1.4f, rockets = 3, eruptionPillars = 3, shockwaveSpeed = 10f, dashSpeed = 19f, laserShots = 2 },
    };

    [Header("Colors")]
    public Color effectColor = new Color(1f, 0.15f, 0.2f);
    public Color windupColor = new Color(1f, 0.45f, 0.45f);

    [Header("Audio")]
    public AudioSource attackSound;
    public AudioSource laserSound;
    public AudioSource deathSound;

    public int CurrentPhase { get; private set; }
    public bool IsActive { get; private set; }

    enum Attack { Laser, Slash, Missiles, Slam, Dash }
    static readonly int HurtHash = Animator.StringToHash("Hurt");

    private EnemyHealth health;
    private Rigidbody2D rb;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private Transform player;
    private PlayerHealth playerHealth;
    private int facing = 1;               // 1 = right, -1 = left
    private bool deathHandled;
    private Attack lastAttack = Attack.Slam;
    private float comboChance;            // chance to chain the next attack without resting
    // Highest animation/warning speed-up, so every attack keeps a readable warning
    const float MaxTempo = 1.8f;
    const float MeltdownHealth = 0.15f;
    private readonly List<GameObject> telegraphs = new List<GameObject>();

    PhaseSettings Settings => phases[CurrentPhase];
    Vector2 Core => rb.position + new Vector2(coreOffset.x * facing, coreOffset.y);
    Vector2 PlayerChest => (Vector2)player.position + Vector2.up * 0.8f;

    void Start()
    {
        health = GetComponent<EnemyHealth>();
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            player = playerObject.transform;
            playerHealth = playerObject.GetComponent<PlayerHealth>();
        }
        FacePlayer();
        ApplyLevelScaling();
    }

    // A higher-level Robot fights a stronger Sentinel (ProgressionSettings boss tiers):
    //   - faster tempo, walk, dash and shockwaves, shorter rests (warnings never drop below a fair minimum)
    //   - from bossExtraShotsLevel: more rockets, pillars and laser shots
    //   - from bossComboLevel: chains a second attack without resting
    //   - from bossFinalPhaseLevel: 4th phase CORE MELTDOWN below 15% HP
    void ApplyLevelScaling()
    {
        ProgressionSettings.Tier s = health.Scaling;
        bool extraShots = PlayerProgression.BossExtraShotsUnlocked;
        float tempo = Mathf.Lerp(1f, s.attackSpeed, 0.6f);
        foreach (PhaseSettings phase in phases)
        {
            phase.walkSpeed *= s.moveSpeed;
            phase.dashSpeed *= s.moveSpeed;
            phase.shockwaveSpeed *= s.moveSpeed;
            phase.restTime /= s.attackSpeed;
            phase.tempo = Mathf.Min(phase.tempo * tempo, MaxTempo);
            if (extraShots)
            {
                phase.rockets++;
                if (phase.eruptionPillars > 0) phase.eruptionPillars++;
            }
        }
        if (s.aggression >= 1.45f && phases.Length > 0 && phases[0].eruptionPillars == 0) phases[0].eruptionPillars = 1;
        if (extraShots && phases.Length > 2) phases[2].laserShots++;
        comboChance = PlayerProgression.BossComboChance(s.aggression);

        if (PlayerProgression.BossFinalPhaseUnlocked && phases.Length == 3)
        {
            PhaseSettings last = phases[2];
            var meltdown = new PhaseSettings
            {
                name = "Core Meltdown",
                walkSpeed = last.walkSpeed * 1.15f,
                restTime = last.restTime * 0.6f,
                tempo = Mathf.Min(last.tempo * 1.15f, MaxTempo),
                rockets = last.rockets + 1,
                eruptionPillars = last.eruptionPillars + 1,
                shockwaveSpeed = last.shockwaveSpeed * 1.1f,
                dashSpeed = last.dashSpeed * 1.1f,
                laserShots = last.laserShots + 1,
            };
            phases = new[] { phases[0], phases[1], phases[2], meltdown };
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
            if (Vector2.Distance(player.position, transform.position) < activationRange) Activate();
            return;
        }

        float healthPercent = (float)health.CurrentHealth / health.MaxHealth;
        if (UIManager.Instance != null) UIManager.Instance.SetBossHealth(health.CurrentHealth, health.MaxHealth);

        int phase = healthPercent > 0.66f ? 0 : (healthPercent > 0.33f ? 1 : 2);
        if (phase == 2 && phases.Length > 3 && healthPercent <= MeltdownHealth) phase = 3;
        if (phase > CurrentPhase) EnterPhase(phase);

        // Touching the body hurts
        if (!playerHealth.IsDead)
        {
            Vector2 offset = PlayerChest - (rb.position + Vector2.up * bodyHalfSize.y);
            if (Mathf.Abs(offset.x) < bodyHalfSize.x && Mathf.Abs(offset.y) < bodyHalfSize.y + 0.6f)
            {
                Hurt(contactDamage, rb.position + Vector2.up * bodyHalfSize.y);
            }
        }
    }

    // Every Sentinel hit uses its level damage scaling
    void Hurt(int hits, Vector2 source)
    {
        playerHealth.TakeDamage(hits, source, health.DamageMultiplier);
    }

    void Activate()
    {
        IsActive = true;
        if (UIManager.Instance != null) UIManager.Instance.ShowBossBar(bossName);
        StartCoroutine(AttackLoop());
    }

    void EnterPhase(int phase)
    {
        CurrentPhase = phase;
        bool meltdown = phase == 3;
        if (UIManager.Instance != null)
        {
            if (meltdown) UIManager.Instance.ShowBanner("CORE MELTDOWN", 2f);
            UIManager.Instance.ShowMessage(meltdown ? "THE SENTINEL IS OVERLOADING - FINISH IT!" : "SENTINEL OVERDRIVE - PHASE " + (phase + 1), 2f);
        }
        Fx.Shockwave(rb.position + Vector2.up * 1.5f, effectColor, meltdown ? 14f : 8f, 0.5f);
        CameraFollow.Shake(meltdown ? 0.5f : 0.3f, 0.3f);
    }

    // =====================================================================
    // ATTACK LOOP
    // =====================================================================

    IEnumerator AttackLoop()
    {
        PlayAnim("Idle");
        yield return new WaitForSeconds(1f);

        while (!health.IsDead)
        {
            if (playerHealth.IsDead)
            {
                PlayAnim("Idle");
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            // Combo (high Robot level): the next attack follows almost without a pause
            bool combo = Random.value < comboChance;
            yield return WalkTowardsPlayer(combo ? 0.15f : Settings.restTime);

            Attack attack = ChooseAttack();
            lastAttack = attack;
            switch (attack)
            {
                case Attack.Laser: yield return LaserAttack(); break;
                case Attack.Slash: yield return SlashAttack(); break;
                case Attack.Missiles: yield return MissileAttack(); break;
                case Attack.Slam: yield return SlamAttack(); break;
                case Attack.Dash: yield return DashAttack(); break;
            }
            if (health.IsDead) yield break;
            PlayAnim("Idle");
        }
    }

    Attack ChooseAttack()
    {
        float distance = Mathf.Abs(player.position.x - rb.position.x);
        if (distance < slashRange && lastAttack != Attack.Slash) return Attack.Slash;

        var options = new List<Attack> { Attack.Laser, Attack.Missiles, Attack.Slam, Attack.Dash };
        options.Remove(lastAttack);
        // The Robot standing on a platform can't be dashed into - prefer ranged attacks
        if (player.position.y > floorY + 2.5f) options.Remove(Attack.Dash);
        return options[Random.Range(0, options.Count)];
    }

    // Walk towards the player for a while (stops at a comfortable distance)
    IEnumerator WalkTowardsPlayer(float duration)
    {
        float timer = 0f;
        while (timer < duration && !health.IsDead)
        {
            FacePlayer();
            float dx = player.position.x - rb.position.x;
            bool walking = Mathf.Abs(dx) > 2.5f;
            PlayAnim(walking ? "Walk" : "Idle");
            if (walking) MoveX(rb.position.x + facing * Settings.walkSpeed * Time.fixedDeltaTime);
            timer += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
    }

    // ---------------- LASER ----------------

    IEnumerator LaserAttack()
    {
        for (int shot = 0; shot < Settings.laserShots && !health.IsDead; shot++)
        {
            FacePlayer();
            PlayAnim("Laser", Settings.tempo);

            // Warning: thin blinking aim line while the core charges
            Vector2 from = Core;
            Vector2 to = from + Vector2.right * facing * laserLength;
            SpriteRenderer aim = Fx.Line(from, to, new Color(1f, 0.2f, 0.25f, 0.6f), 0.08f);
            telegraphs.Add(aim.gameObject);
            float charge = 0.75f / Settings.tempo, timer = 0f;
            while (timer < charge)
            {
                aim.enabled = Mathf.FloorToInt(timer * 14f) % 2 == 0;
                if (Random.value < 0.5f)
                {
                    Vector2 spark = from + Random.insideUnitCircle.normalized * 1.2f;
                    Fx.Spawn(Fx.Pixel, spark, (from - spark) * 4f, 0.25f, 1f, 0.3f, Color.white, effectColor, 0f, 0f, 2);
                }
                timer += Time.deltaTime;
                yield return null;
            }
            DestroyTelegraph(aim.gameObject);

            // Fire
            if (laserSound != null) laserSound.Play();
            CameraFollow.Shake(0.2f, laserTime);
            SpriteRenderer glow = Fx.Line(from, to, new Color(effectColor.r, effectColor.g, effectColor.b, 0.5f), laserWidth * 2.2f);
            SpriteRenderer beam = Fx.Line(from, to, new Color(1f, 0.85f, 0.85f), laserWidth * 0.5f);
            telegraphs.Add(glow.gameObject);
            telegraphs.Add(beam.gameObject);
            Fx.Explosion(from, effectColor, 0.5f);
            timer = 0f;
            while (timer < laserTime)
            {
                float pulse = 1f + Mathf.Sin(timer * 60f) * 0.15f;
                glow.transform.localScale = new Vector3(glow.transform.localScale.x, laserWidth * 2.2f * pulse / 0.125f, 1f);
                if (BeamHits(from, facing)) Hurt(1, from);
                if (Random.value < 0.6f) Fx.Sparks(from + Vector2.right * facing * Random.Range(0f, 20f), effectColor, 2, 3f);
                timer += Time.deltaTime;
                yield return null;
            }
            DestroyTelegraph(glow.gameObject);
            DestroyTelegraph(beam.gameObject);
            PlayAnim("Idle");
            yield return new WaitForSeconds(0.35f / Settings.tempo);
        }
    }

    bool BeamHits(Vector2 from, int direction)
    {
        if (playerHealth.IsDead) return false;
        // The Robot's body spans its feet .. feet + 1.6
        float bottom = player.position.y, top = player.position.y + 1.6f;
        bool vertical = from.y + laserWidth * 0.5f > bottom && from.y - laserWidth * 0.5f < top;
        bool ahead = (player.position.x - from.x) * direction > -0.3f;
        return vertical && ahead;
    }

    // ---------------- SLASH ----------------

    IEnumerator SlashAttack()
    {
        FacePlayer();
        yield return Windup(0.3f / Settings.tempo);
        PlayAnim("Slash", Settings.tempo);
        yield return new WaitForSeconds(0.2f / Settings.tempo);

        if (attackSound != null) attackSound.Play();
        Vector2 center = rb.position + new Vector2(facing * 1.4f, 1.3f);
        FxParticle arc = Fx.Spawn(Fx.Crescent, center, Vector2.right * facing * 2f, 0.3f, 4.2f, 4.8f,
            new Color(1f, 0.9f, 0.9f), new Color(effectColor.r, effectColor.g, effectColor.b, 0f), 0f, 0f, 1);
        arc.GetComponent<SpriteRenderer>().flipX = facing < 0;
        Fx.Sparks(center + Vector2.right * facing * 1.5f, effectColor, 10, 7f);

        Vector2 toPlayer = PlayerChest - (rb.position + Vector2.up * 1.3f);
        if (!playerHealth.IsDead && toPlayer.x * facing > -0.5f && Mathf.Abs(toPlayer.x) < slashRange && Mathf.Abs(toPlayer.y) < 2.4f)
        {
            Hurt(1, rb.position);
        }
        yield return new WaitForSeconds(0.45f / Settings.tempo);
    }

    // ---------------- MISSILES ----------------

    IEnumerator MissileAttack()
    {
        FacePlayer();
        PlayAnim("Missile", Settings.tempo);
        yield return new WaitForSeconds(0.45f / Settings.tempo);

        for (int i = 0; i < Settings.rockets && !health.IsDead; i++)
        {
            Vector2 head = rb.position + new Vector2(headOffset.x * facing, headOffset.y);
            // Launch up and backwards, then the rockets turn towards the Robot
            Vector2 launch = new Vector2(-facing * Random.Range(0.3f, 0.7f), 1f).normalized;
            SpawnRocket(head, launch * rocketSpeed);
            Fx.Explosion(head, effectColor, 0.4f);
            if (attackSound != null) attackSound.Play();
            yield return new WaitForSeconds(0.25f);
        }
        yield return new WaitForSeconds(0.5f / Settings.tempo);
    }

    void SpawnRocket(Vector2 position, Vector2 velocity)
    {
        var go = new GameObject("SentinelRocket") { tag = "Hazard" };
        go.transform.position = position;
        go.transform.localScale = Vector3.one * rocketScale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = Fx.SortingOrder - 2;
        if (rocketFrames != null && rocketFrames.Length > 0) sr.sprite = rocketFrames[0];
        var body = go.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.linearVelocity = velocity;
        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.35f;
        var projectile = go.AddComponent<EnemyProjectile>();
        projectile.damage = 1;
        projectile.damageMultiplier = health.DamageMultiplier;
        projectile.color = effectColor;
        projectile.lifetime = 5f;
        projectile.trailInterval = 0.05f;
        projectile.popExplosion = 0.6f;
        var rocket = go.AddComponent<SentinelRocket>();
        rocket.frames = rocketFrames;
        rocket.target = player;
        rocket.speed = rocketSpeed;
        rocket.turnRate = rocketTurnRate;
        rocket.color = effectColor;
    }

    // ---------------- SLAM ----------------

    IEnumerator SlamAttack()
    {
        FacePlayer();
        yield return Windup(0.25f / Settings.tempo);
        PlayAnim("Slam", Settings.tempo);
        yield return new WaitForSeconds(0.5f / Settings.tempo);

        // Eruption around the boss
        if (attackSound != null) attackSound.Play();
        CameraFollow.Shake(0.35f, 0.3f);
        Vector2 feet = rb.position;
        Fx.Shockwave(feet + Vector2.up * 0.3f, effectColor, slamRadius * 2.5f, 0.4f);
        for (int i = 0; i < 18; i++)
        {
            Vector2 velocity = new Vector2(Random.Range(-6f, 6f), Random.Range(6f, 12f));
            Fx.Spawn(Fx.Pixel, feet + Vector2.right * Random.Range(-slamRadius, slamRadius), velocity, 0.6f, 1.4f, 0.3f,
                Color.white, new Color(effectColor.r, effectColor.g, effectColor.b, 0f), 20f, 1f, 2);
        }
        if (!playerHealth.IsDead && Mathf.Abs(player.position.x - feet.x) < slamRadius && player.position.y < floorY + 1.2f)
        {
            Hurt(1, feet);
        }

        // Shockwaves sliding along the floor
        float y = floorY + 0.35f;
        Fx.SpawnProjectile(new Vector2(feet.x - 1.8f, y), Vector2.left * Settings.shockwaveSpeed, effectColor, new Vector2(1.4f, 0.8f), 1, 4f, health.DamageMultiplier);
        Fx.SpawnProjectile(new Vector2(feet.x + 1.8f, y), Vector2.right * Settings.shockwaveSpeed, effectColor, new Vector2(1.4f, 0.8f), 1, 4f, health.DamageMultiplier);

        // Phase 2/3: eruption pillars under the Robot
        if (Settings.eruptionPillars > 0) StartCoroutine(EruptionPillars(Settings.eruptionPillars));
        yield return new WaitForSeconds(0.45f / Settings.tempo);
    }

    IEnumerator EruptionPillars(int count)
    {
        for (int i = 0; i < count && !health.IsDead; i++)
        {
            float x = Mathf.Clamp(player.position.x + (i == 0 ? 0f : Random.Range(-3f, 3f)), minX, maxX);
            StartCoroutine(EruptionPillar(x));
            yield return new WaitForSeconds(0.35f);
        }
    }

    IEnumerator EruptionPillar(float x)
    {
        // Warning mark on the floor
        Vector2 baseCenter = new Vector2(x, floorY + 0.05f);
        SpriteRenderer mark = Fx.Line(baseCenter + Vector2.left * 0.9f, baseCenter + Vector2.right * 0.9f, effectColor, 0.15f);
        telegraphs.Add(mark.gameObject);
        float timer = 0f;
        while (timer < 0.8f)
        {
            mark.enabled = Mathf.FloorToInt(timer * 12f) % 2 == 0;
            if (Random.value < 0.3f) Fx.Sparks(baseCenter, effectColor, 1, 2f);
            timer += Time.deltaTime;
            yield return null;
        }
        DestroyTelegraph(mark.gameObject);
        if (health.IsDead) yield break;

        // Pillar of energy
        CameraFollow.Shake(0.15f, 0.15f);
        for (int i = 0; i < 6; i++)
        {
            Fx.Spawn(Fx.Circle, baseCenter + Vector2.up * (i * 0.6f), Vector2.up * 6f, 0.35f, 1.6f, 0.6f,
                new Color(1f, 0.8f, 0.8f), new Color(effectColor.r, effectColor.g, effectColor.b, 0f), 0f, 0f, 1);
        }
        Fx.Sparks(baseCenter + Vector2.up, effectColor, 10, 8f);
        if (!playerHealth.IsDead && Mathf.Abs(player.position.x - x) < 0.95f && player.position.y < floorY + 3.5f)
        {
            Hurt(1, baseCenter);
        }
    }

    // ---------------- DASH ----------------

    IEnumerator DashAttack()
    {
        FacePlayer();
        PlayAnim("Dash", Settings.tempo);
        yield return Windup(0.45f / Settings.tempo);

        if (attackSound != null) attackSound.Play();
        float targetX = facing > 0 ? maxX : minX;
        float dustTimer = 0f;
        while (!health.IsDead && Mathf.Abs(rb.position.x - targetX) > 0.05f)
        {
            MoveX(Mathf.MoveTowards(rb.position.x, targetX, Settings.dashSpeed * Time.fixedDeltaTime));
            dustTimer -= Time.fixedDeltaTime;
            if (dustTimer <= 0f)
            {
                dustTimer = 0.05f;
                Color dust = new Color(0.6f, 0.6f, 0.65f, 0.6f);
                Fx.Spawn(Fx.Circle, rb.position + new Vector2(-facing * 1.2f, 0.3f), new Vector2(-facing * 2f, 1f), 0.4f, 0.6f, 1.4f,
                    dust, new Color(dust.r, dust.g, dust.b, 0f), 0f, 1f, -1);
            }
            yield return new WaitForFixedUpdate();
        }

        // Hit the arena wall
        CameraFollow.Shake(0.3f, 0.25f);
        Fx.Sparks(rb.position + new Vector2(facing * 1.5f, 1.2f), effectColor, 14, 8f);
        PlayAnim("Idle");
        yield return new WaitForSeconds(0.6f / Settings.tempo);
    }

    // =====================================================================
    // HELPERS
    // =====================================================================

    void FacePlayer()
    {
        if (player == null) return;
        facing = player.position.x >= rb.position.x ? 1 : -1;
        spriteRenderer.flipX = facing < 0;
    }

    void MoveX(float x)
    {
        rb.MovePosition(new Vector2(Mathf.Clamp(x, minX, maxX), floorY));
    }

    void PlayAnim(string state, float speed = 1f)
    {
        if (animator == null || health.IsDead) return;
        animator.speed = speed;
        int hash = Animator.StringToHash(state);
        int current = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        // Idle/Walk never restart themselves or cut off the short hurt flinch
        if ((state == "Idle" || state == "Walk") && (current == hash || current == HurtHash)) return;
        animator.ResetTrigger("Hurt");
        animator.Play(hash, 0, 0f);
    }

    // Red flashing warning
    IEnumerator Windup(float duration)
    {
        float timer = 0f;
        while (timer < duration && !health.IsDead)
        {
            spriteRenderer.color = Mathf.FloorToInt(timer * 14f) % 2 == 0 ? windupColor : Color.white;
            timer += Time.deltaTime;
            yield return null;
        }
        if (!health.IsDead) spriteRenderer.color = Color.white;
    }

    void DestroyTelegraph(GameObject telegraph)
    {
        telegraphs.Remove(telegraph);
        if (telegraph != null) Destroy(telegraph);
    }

    void OnDeath()
    {
        deathHandled = true;
        StopAllCoroutines();
        foreach (GameObject telegraph in telegraphs) if (telegraph != null) Destroy(telegraph);
        telegraphs.Clear();
        if (animator != null) animator.speed = 1f;
        if (deathSound != null) deathSound.Play();
        if (UIManager.Instance != null) UIManager.Instance.ShowBossDefeated();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, activationRange);
        Gizmos.color = Color.red;
        Gizmos.DrawLine(new Vector3(minX, floorY), new Vector3(maxX, floorY));
        Gizmos.DrawWireSphere(transform.position + (Vector3)coreOffset, 0.3f);
    }
}
