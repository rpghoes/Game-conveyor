using System.Collections;
using UnityEngine;

// The Corrupted Core boss. It repeats one clear attack cycle:
//   1. fly above the player   2. (phase 2+) fire a projectile spread
//   3. flash red (warning)    4. slam down onto the floor
//   5. stay open (vulnerable) so the Robot can hit it   6. rise and rest
// Each phase makes it faster with shorter openings. Phase 3 also turns on
// electric floor hazards in the arena.
[RequireComponent(typeof(EnemyHealth), typeof(Rigidbody2D))]
public class BossController : MonoBehaviour
{
    [System.Serializable]
    public class PhaseSettings
    {
        public string name = "Phase";
        public float moveSpeed = 4f;
        [Tooltip("Red flashing warning before the slam")]
        public float slamWindup = 0.8f;
        [Tooltip("How long the core stays open to attacks after slamming")]
        public float vulnerableTime = 2.5f;
        [Tooltip("Projectiles fired in a spread before each slam (0 = none)")]
        public int projectileCount = 0;
        public float projectileSpeed = 6f;
        [Tooltip("Send shockwaves along the floor when slamming")]
        public bool slamShockwaves = false;
        [Tooltip("Pause at the top before the next cycle")]
        public float restTime = 1.2f;
    }

    [Header("Setup")]
    public string bossName = "CORRUPTED CORE";
    [Tooltip("Mini-boss: dying only unlocks the room exit instead of ending the game")]
    public bool isMiniBoss = false;
    [Tooltip("The fight starts when the player gets this close")]
    public float activationRange = 14f;
    [Tooltip("World Y the boss hovers at")]
    public float hoverY = 12f;
    [Tooltip("World Y of the arena floor surface")]
    public float floorY = 2f;
    [Tooltip("World Y of the boss centre when it lands a slam")]
    public float slamY = 3.4f;
    [Tooltip("Left/right limits of the boss movement")]
    public float minX = 4f;
    public float maxX = 36f;
    public float slamSpeed = 24f;

    [Header("Damage")]
    public int contactDamage = 1;
    public float contactRadius = 1.4f;
    public GameObject projectilePrefab;
    public float projectileLifetime = 5f;

    [Header("Phases (start at 100%, 66% and 33% health)")]
    public PhaseSettings[] phases =
    {
        new PhaseSettings { name = "Phase 1", moveSpeed = 4f, slamWindup = 0.9f, vulnerableTime = 2.6f, projectileCount = 0, projectileSpeed = 6f, slamShockwaves = false, restTime = 1.4f },
        new PhaseSettings { name = "Phase 2", moveSpeed = 6f, slamWindup = 0.65f, vulnerableTime = 2f, projectileCount = 3, projectileSpeed = 7f, slamShockwaves = true, restTime = 1f },
        new PhaseSettings { name = "Phase 3", moveSpeed = 8f, slamWindup = 0.45f, vulnerableTime = 1.4f, projectileCount = 5, projectileSpeed = 9f, slamShockwaves = true, restTime = 0.6f },
    };

    [Header("Phase 3 Arena Hazards")]
    [Tooltip("Objects with a SpriteRenderer + trigger Collider2D tagged 'Hazard'")]
    public GameObject[] arenaHazards;
    public float hazardWarningTime = 1f;
    public float hazardActiveTime = 2f;
    public float hazardOffTime = 2.5f;

    [Header("Colors")]
    public Color armoredColor = new Color(0.55f, 0.45f, 0.75f);
    public Color vulnerableColor = Color.white;
    public Color windupColor = new Color(1f, 0.35f, 0.35f);

    [Header("Audio")]
    public AudioSource attackSound;
    public AudioSource deathSound;

    public int CurrentPhase { get; private set; }
    public bool IsActive { get; private set; }

    private EnemyHealth health;
    private Rigidbody2D rb;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private Transform player;
    private PlayerHealth playerHealth;
    private bool dangerous;     // touching the boss hurts while this is true
    private bool deathHandled;
    private float comboChance;      // chance to skip the rest and attack again right away

    void Start()
    {
        health = GetComponent<EnemyHealth>();
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        health.invulnerable = true;
        spriteRenderer.color = armoredColor;
        SetHazards(false, false);

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            player = playerObject.transform;
            playerHealth = playerObject.GetComponent<PlayerHealth>();
        }

        ApplyLevelScaling();
    }

    // A higher-level Robot fights a faster, more aggressive core (ProgressionSettings mini-boss / boss tiers).
    // Warnings and openings shrink less than the speed grows, and never below a fair minimum.
    void ApplyLevelScaling()
    {
        ProgressionSettings.Tier s = health.Scaling;
        float telegraph = Mathf.Lerp(1f, s.attackSpeed, 0.5f);
        float opening = Mathf.Lerp(1f, s.attackSpeed, 0.35f);
        int extraShots = (s.aggression >= 1.25f ? 1 : 0) + (PlayerProgression.BossExtraShotsUnlocked ? 1 : 0);
        foreach (PhaseSettings phase in phases)
        {
            phase.moveSpeed *= s.moveSpeed;
            phase.projectileSpeed *= s.moveSpeed;
            phase.slamWindup = Mathf.Max(phase.slamWindup / telegraph, 0.35f);
            phase.vulnerableTime = Mathf.Max(phase.vulnerableTime / opening, 1.1f);
            phase.restTime /= s.attackSpeed;
            if (phase.projectileCount > 0) phase.projectileCount += extraShots;
        }
        // Aggressive cores send shockwaves from the first phase
        if (s.aggression >= 1.3f && phases.Length > 0) phases[0].slamShockwaves = true;
        hazardOffTime /= s.attackSpeed;
        comboChance = PlayerProgression.BossComboChance(s.aggression);
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

        // Health bar + phase changes
        float healthPercent = (float)health.CurrentHealth / health.MaxHealth;
        if (UIManager.Instance != null) UIManager.Instance.SetBossHealth(health.CurrentHealth, health.MaxHealth);

        int phase = healthPercent > 0.66f ? 0 : (healthPercent > 0.33f ? 1 : 2);
        if (phase != CurrentPhase) EnterPhase(phase);

        // Contact damage
        if (dangerous && !playerHealth.IsDead)
        {
            Vector2 playerChest = (Vector2)player.position + Vector2.up * 0.8f;
            if (Vector2.Distance(playerChest, transform.position) < contactRadius)
            {
                playerHealth.TakeDamage(contactDamage, transform.position, health.DamageMultiplier);
            }
        }
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
        if (UIManager.Instance != null) UIManager.Instance.ShowMessage("CORE INSTABILITY - PHASE " + (phase + 1), 2f);
        if (phase == 2 && arenaHazards != null && arenaHazards.Length > 0) StartCoroutine(HazardLoop());
    }

    IEnumerator AttackLoop()
    {
        yield return new WaitForSeconds(1f);

        while (!health.IsDead)
        {
            PhaseSettings settings = phases[CurrentPhase];
            dangerous = true;

            // 1. Fly above the player
            yield return MoveTo(new Vector2(TargetX(), hoverY), settings.moveSpeed * 2f, 2.5f);

            // 2. Projectile spread (phase 2 and 3)
            if (settings.projectileCount > 0)
            {
                FireSpread(settings.projectileCount, settings.projectileSpeed);
                yield return new WaitForSeconds(0.6f);
                yield return MoveTo(new Vector2(TargetX(), hoverY), settings.moveSpeed * 2f, 1.5f);
            }

            // 3. Warning flash
            if (animator != null) animator.SetTrigger("Attack");
            yield return Windup(settings.slamWindup);

            // 4. Slam
            yield return MoveTo(new Vector2(transform.position.x, slamY), slamSpeed, 2f);
            if (health.IsDead) yield break;
            if (attackSound != null) attackSound.Play();
            if (settings.slamShockwaves) FireShockwaves(settings.projectileSpeed);

            // 5. Vulnerable window
            dangerous = false;
            SetVulnerable(true);
            yield return new WaitForSeconds(settings.vulnerableTime);
            if (health.IsDead) yield break;
            SetVulnerable(false);

            // 6. Rise and rest
            yield return MoveTo(new Vector2(transform.position.x, hoverY), settings.moveSpeed, 3f);
            dangerous = false;

            // Combo (high Robot level): a quick projectile volley instead of the rest
            if (Random.value < comboChance)
            {
                yield return Windup(0.3f);
                FireSpread(Mathf.Max(settings.projectileCount, 3), settings.projectileSpeed);
                yield return new WaitForSeconds(0.4f);
                continue;
            }
            yield return new WaitForSeconds(settings.restTime);
        }
    }

    IEnumerator MoveTo(Vector2 target, float speed, float maxTime)
    {
        float timer = 0f;
        while (timer < maxTime && !health.IsDead && Vector2.Distance(rb.position, target) > 0.05f)
        {
            rb.MovePosition(Vector2.MoveTowards(rb.position, target, speed * Time.fixedDeltaTime));
            timer += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
    }

    IEnumerator Windup(float duration)
    {
        float timer = 0f;
        while (timer < duration)
        {
            spriteRenderer.color = Mathf.FloorToInt(timer * 12f) % 2 == 0 ? windupColor : armoredColor;
            timer += Time.deltaTime;
            yield return null;
        }
        spriteRenderer.color = armoredColor;
    }

    void SetVulnerable(bool vulnerable)
    {
        health.invulnerable = !vulnerable;
        spriteRenderer.color = vulnerable ? vulnerableColor : armoredColor;
        if (animator != null) animator.SetBool("Vulnerable", vulnerable);
    }

    float TargetX()
    {
        return Mathf.Clamp(player.position.x, minX, maxX);
    }

    // Fan of projectiles aimed at the player
    void FireSpread(int count, float speed)
    {
        Vector2 toPlayer = ((Vector2)player.position + Vector2.up * 0.8f - rb.position).normalized;
        float spreadAngle = 15f;
        float startAngle = -(count - 1) * 0.5f * spreadAngle;
        for (int i = 0; i < count; i++)
        {
            Vector2 direction = Quaternion.Euler(0f, 0f, startAngle + i * spreadAngle) * toPlayer;
            SpawnProjectile(rb.position, direction * speed);
        }
        if (attackSound != null) attackSound.Play();
    }

    // Two projectiles sliding left and right along the floor
    void FireShockwaves(float speed)
    {
        float y = floorY + 0.35f;
        SpawnProjectile(new Vector2(rb.position.x - 1.5f, y), Vector2.left * speed);
        SpawnProjectile(new Vector2(rb.position.x + 1.5f, y), Vector2.right * speed);
    }

    void SpawnProjectile(Vector2 position, Vector2 velocity)
    {
        if (projectilePrefab == null) return;
        GameObject projectile = Instantiate(projectilePrefab, position, Quaternion.identity);
        // The shot hurts with the core's level scaling (see PlayerHealth.OnTriggerStay2D)
        HazardDamage scaled = projectile.GetComponent<HazardDamage>();
        if (scaled == null) scaled = projectile.AddComponent<HazardDamage>();
        scaled.multiplier = health.DamageMultiplier;
        Rigidbody2D projectileBody = projectile.GetComponent<Rigidbody2D>();
        if (projectileBody != null) projectileBody.linearVelocity = velocity;
        Destroy(projectile, projectileLifetime);
    }

    // Phase 3: floor hazards blink as a warning, turn on, then turn off again
    IEnumerator HazardLoop()
    {
        while (!health.IsDead)
        {
            SetHazards(true, false);
            yield return new WaitForSeconds(hazardWarningTime);
            SetHazards(true, true);
            yield return new WaitForSeconds(hazardActiveTime);
            SetHazards(false, false);
            yield return new WaitForSeconds(hazardOffTime);
        }
    }

    void SetHazards(bool visible, bool active)
    {
        if (arenaHazards == null) return;
        foreach (GameObject hazard in arenaHazards)
        {
            if (hazard == null) continue;
            hazard.SetActive(visible);
            SpriteRenderer hazardSprite = hazard.GetComponent<SpriteRenderer>();
            if (hazardSprite != null) hazardSprite.color = new Color(1f, 1f, 1f, active ? 1f : 0.3f);
            Collider2D hazardCollider = hazard.GetComponent<Collider2D>();
            if (hazardCollider != null) hazardCollider.enabled = active;
        }
    }

    void OnDeath()
    {
        deathHandled = true;
        dangerous = false;
        StopAllCoroutines();
        SetHazards(false, false);
        if (deathSound != null) deathSound.Play();
        if (UIManager.Instance != null)
        {
            if (isMiniBoss) UIManager.Instance.ShowMiniBossDefeated();
            else UIManager.Instance.ShowBossDefeated();
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, activationRange);
        Gizmos.color = Color.red;
        Gizmos.DrawLine(new Vector3(minX, hoverY), new Vector3(maxX, hoverY));
        Gizmos.DrawLine(new Vector3(minX, slamY), new Vector3(maxX, slamY));
    }
}
