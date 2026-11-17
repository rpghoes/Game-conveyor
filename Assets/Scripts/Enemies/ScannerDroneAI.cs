using System.Collections;
using UnityEngine;

// Flying enemy. Floats and patrols left/right. When it spots the player it hovers
// above them and uses one of three attacks (never the same one twice in a row):
//   DIVE  - short pause, then dives straight at the player
//   LASER - a red aiming line tracks the player, locks, then a fast bolt is fired along it
//   BURST - glows orange and fires a spread of slow energy orbs
// No pathfinding.
[RequireComponent(typeof(Rigidbody2D), typeof(EnemyHealth))]
public class ScannerDroneAI : MonoBehaviour
{
    [Header("Patrol")]
    [Tooltip("How far left/right of its start position the drone patrols")]
    public float patrolDistance = 4f;
    public float patrolSpeed = 2.5f;
    public float hoverAmplitude = 0.3f;
    public float hoverSpeed = 2f;

    [Header("Chase")]
    public float detectionRange = 8f;
    [Tooltip("Stop chasing when this far from the start position")]
    public float leashRange = 14f;
    public float chaseSpeed = 4.5f;
    [Tooltip("How high above the player the drone hovers before diving")]
    public float hoverHeightAbovePlayer = 2.8f;

    [Header("Dive Attack")]
    [Tooltip("Pause before diving so the player can react")]
    public float diveWarning = 0.3f;
    public float diveSpeed = 11f;
    public float diveDuration = 0.45f;
    public float attackCooldown = 1.8f;
    [Tooltip("Distance at which the dive hits the player")]
    public float hitRange = 0.9f;
    public int damage = 1;

    [Header("Laser Attack")]
    [Tooltip("The aiming line follows the player for this long...")]
    public float laserAimTime = 0.7f;
    [Tooltip("...then stops moving for this long before firing")]
    public float laserLockTime = 0.25f;
    public float laserBoltSpeed = 16f;
    public Color laserColor = new Color(1f, 0.2f, 0.25f);

    [Header("Burst Attack")]
    public int burstCount = 3;
    public float burstSpreadAngle = 22f;
    public float burstSpeed = 6f;
    public float burstWindup = 0.5f;
    public Color burstColor = new Color(1f, 0.6f, 0.15f);

    [Header("Ground (blocks line of sight)")]
    public LayerMask groundLayer;

    [Header("Art")]
    [Tooltip("The provided Scanner Drone artwork faces LEFT")]
    public bool artFacesRight = false;

    [Header("Audio")]
    public AudioSource attackSound;

    enum Pattern { Dive, Laser, Burst }

    private Rigidbody2D rb;
    private EnemyHealth health;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private Transform player;
    private PlayerHealth playerHealth;
    private Vector2 startPosition;
    private int patrolDirection = 1;
    private bool attacking;
    private float nextAttackTime;
    private Pattern lastPattern = Pattern.Burst;
    private SpriteRenderer laserLine;

    // The point on the player the drone aims for (roughly the chest)
    Vector2 PlayerTarget => (Vector2)player.position + Vector2.up * 0.8f;
    bool Interrupted => health.IsDead || health.IsStunned;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        health = GetComponent<EnemyHealth>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        startPosition = rb.position;
        if (groundLayer == 0) groundLayer = LayerMask.GetMask("Ground");
        nextAttackTime = Time.time + Random.Range(0.3f, 1.2f);

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            player = playerObject.transform;
            playerHealth = playerObject.GetComponent<PlayerHealth>();
        }

        ApplyLevelScaling();
    }

    // A higher-level Robot meets faster, more aggressive drones (wind-ups shrink only half as much as
    // cooldowns, so the warnings stay readable). Very aggressive drones fire one more bolt per burst.
    void ApplyLevelScaling()
    {
        ProgressionSettings.Tier s = health.Scaling;
        patrolSpeed *= s.moveSpeed;
        chaseSpeed *= s.moveSpeed;
        diveSpeed *= s.moveSpeed;
        attackCooldown /= s.attackSpeed;
        float telegraph = Mathf.Lerp(1f, s.attackSpeed, 0.5f);
        diveWarning /= telegraph;
        laserAimTime /= telegraph;
        burstWindup /= telegraph;
        detectionRange *= s.aggression;
        leashRange *= s.aggression;
        if (s.aggression >= 1.4f) burstCount++;
    }

    void FixedUpdate()
    {
        if (health.IsDead || health.IsStunned || attacking) return;

        Vector2 position = rb.position;
        Vector2 target;
        float speed;

        if (CanSeePlayer())
        {
            // --- Chase: hover above the player ---
            target = (Vector2)player.position + Vector2.up * hoverHeightAbovePlayer;
            speed = chaseSpeed;

            if (Time.time >= nextAttackTime)
            {
                bool inDivePosition = Vector2.Distance(position, target) < 1.2f;
                bool hasLineOfSight = HasLineOfSight();
                Pattern? pattern = ChoosePattern(inDivePosition, hasLineOfSight);
                if (pattern.HasValue)
                {
                    StartCoroutine(RunPattern(pattern.Value));
                    return;
                }
            }
        }
        else
        {
            // --- Patrol: fly left/right around the start point with a gentle bob ---
            if (position.x > startPosition.x + patrolDistance) patrolDirection = -1;
            else if (position.x < startPosition.x - patrolDistance) patrolDirection = 1;

            float bob = Mathf.Sin(Time.time * hoverSpeed) * hoverAmplitude;
            target = new Vector2(position.x + patrolDirection, startPosition.y + bob);
            speed = patrolSpeed;
        }

        Vector2 toTarget = target - position;
        Vector2 desiredVelocity = toTarget.magnitude > 0.1f ? toTarget.normalized * speed : Vector2.zero;
        rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, desiredVelocity, 20f * Time.fixedDeltaTime);

        if (Mathf.Abs(desiredVelocity.x) > 0.1f) Face(desiredVelocity.x > 0f ? 1 : -1);
        if (animator != null) animator.SetFloat("Speed", rb.linearVelocity.magnitude);
    }

    Pattern? ChoosePattern(bool inDivePosition, bool hasLineOfSight)
    {
        // Ranged attacks need a clear shot; the dive needs to be right above the player
        var options = new System.Collections.Generic.List<Pattern>();
        if (inDivePosition) options.Add(Pattern.Dive);
        if (hasLineOfSight)
        {
            options.Add(Pattern.Laser);
            options.Add(Pattern.Burst);
        }
        if (options.Count > 1) options.Remove(lastPattern);
        if (options.Count == 0) return null;
        return options[Random.Range(0, options.Count)];
    }

    IEnumerator RunPattern(Pattern pattern)
    {
        attacking = true;
        lastPattern = pattern;
        nextAttackTime = Time.time + attackCooldown;
        if (animator != null) animator.SetTrigger("Attack");

        switch (pattern)
        {
            case Pattern.Dive: yield return Dive(); break;
            case Pattern.Laser: yield return Laser(); break;
            case Pattern.Burst: yield return Burst(); break;
        }

        HideLaserLine();
        if (!health.IsDead && spriteRenderer != null) spriteRenderer.color = Color.white;
        attacking = false;
    }

    // ---------------- DIVE ----------------
    IEnumerator Dive()
    {
        // Short pause = warning for the player
        rb.linearVelocity = Vector2.zero;
        yield return Glow(diveWarning, Color.white, new Color(1f, 0.5f, 0.5f));
        if (Interrupted || player == null) yield break;

        Vector2 direction = (PlayerTarget - rb.position).normalized;
        Face(direction.x > 0f ? 1 : -1);
        if (attackSound != null) attackSound.Play();

        float timer = 0f;
        bool hitPlayer = false;
        while (timer < diveDuration && !Interrupted)
        {
            rb.linearVelocity = direction * diveSpeed;

            if (!hitPlayer && !playerHealth.IsDead && Vector2.Distance(rb.position, PlayerTarget) < hitRange)
            {
                playerHealth.TakeDamage(damage, rb.position, health.DamageMultiplier);
                hitPlayer = true;
            }

            timer += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        // Pull back up after the dive
        if (!Interrupted) rb.linearVelocity = Vector2.up * chaseSpeed;
    }

    // ---------------- LASER ----------------
    IEnumerator Laser()
    {
        rb.linearVelocity = Vector2.zero;
        Vector2 aim = PlayerTarget;

        // Aim: a thin line follows the player
        float timer = 0f;
        while (timer < laserAimTime && !Interrupted && player != null)
        {
            timer += Time.deltaTime;
            aim = Vector2.Lerp(aim, PlayerTarget, 10f * Time.deltaTime);
            Face(aim.x > rb.position.x ? 1 : -1);
            ShowLaserLine(aim, new Color(laserColor.r, laserColor.g, laserColor.b, 0.35f), 0.04f);
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, Vector2.zero, 20f * Time.deltaTime);
            yield return null;
        }

        // Lock: the line stops and blinks - time to dodge!
        timer = 0f;
        while (timer < laserLockTime && !Interrupted)
        {
            timer += Time.deltaTime;
            bool on = Mathf.FloorToInt(timer * 30f) % 2 == 0;
            ShowLaserLine(aim, on ? laserColor : Color.white, 0.08f);
            if (spriteRenderer != null) spriteRenderer.color = on ? laserColor : Color.white;
            yield return null;
        }
        HideLaserLine();
        if (Interrupted) yield break;

        // Fire
        Vector2 direction = (aim - rb.position).normalized;
        Fx.SpawnProjectile(rb.position + direction * 0.5f, direction * laserBoltSpeed, laserColor, new Vector2(0.9f, 0.25f), damage, 3f, health.DamageMultiplier);
        Fx.Sparks(rb.position + direction * 0.5f, laserColor, 6, 4f);
        if (attackSound != null) attackSound.Play();

        // Recoil
        rb.linearVelocity = -direction * 3f;
        yield return new WaitForSeconds(0.3f);
    }

    // ---------------- BURST ----------------
    IEnumerator Burst()
    {
        rb.linearVelocity = Vector2.zero;
        // Charge up: glow and pull in particles
        float timer = 0f;
        while (timer < burstWindup && !Interrupted)
        {
            timer += Time.deltaTime;
            if (spriteRenderer != null) spriteRenderer.color = Color.Lerp(Color.white, burstColor, Mathf.PingPong(timer * 8f, 1f));
            if (Random.value < 0.4f)
            {
                Vector2 from = rb.position + Random.insideUnitCircle.normalized * 1.2f;
                Fx.Spawn(Fx.Pixel, from, (rb.position - from) * 4f, 0.25f, 1f, 0.3f, burstColor, Color.clear);
            }
            yield return null;
        }
        if (Interrupted || player == null) yield break;

        Vector2 toPlayer = (PlayerTarget - rb.position).normalized;
        Face(toPlayer.x > 0f ? 1 : -1);
        float startAngle = -(burstCount - 1) * 0.5f * burstSpreadAngle;
        for (int i = 0; i < burstCount; i++)
        {
            Vector2 direction = Quaternion.Euler(0f, 0f, startAngle + i * burstSpreadAngle) * toPlayer;
            Fx.SpawnProjectile(rb.position + direction * 0.4f, direction * burstSpeed, burstColor, new Vector2(0.45f, 0.45f), damage, 4f, health.DamageMultiplier);
        }
        if (attackSound != null) attackSound.Play();
        rb.linearVelocity = -toPlayer * 2f;
        yield return new WaitForSeconds(0.35f);
    }

    // ---------------- Helpers ----------------

    IEnumerator Glow(float duration, Color a, Color b)
    {
        float timer = 0f;
        while (timer < duration && !Interrupted)
        {
            timer += Time.deltaTime;
            if (spriteRenderer != null) spriteRenderer.color = Mathf.FloorToInt(timer * 20f) % 2 == 0 ? a : b;
            yield return null;
        }
    }

    void ShowLaserLine(Vector2 aim, Color color, float width)
    {
        // The line runs from the drone through the aim point until it hits a wall
        Vector2 from = rb.position;
        Vector2 direction = (aim - from).normalized;
        RaycastHit2D wall = Physics2D.Raycast(from, direction, 30f, groundLayer);
        Vector2 to = wall.collider != null ? wall.point : from + direction * 30f;

        if (laserLine == null) laserLine = Fx.Line(from, to, color, width);
        Vector2 delta = to - from;
        laserLine.transform.position = (from + to) * 0.5f;
        laserLine.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        laserLine.transform.localScale = new Vector3(delta.magnitude / 0.125f, width / 0.125f, 1f);
        laserLine.color = color;
    }

    void HideLaserLine()
    {
        if (laserLine != null) Destroy(laserLine.gameObject);
        laserLine = null;
    }

    void OnDisable()
    {
        HideLaserLine();
    }

    bool HasLineOfSight()
    {
        Vector2 toPlayer = PlayerTarget - rb.position;
        return !Physics2D.Raycast(rb.position, toPlayer.normalized, toPlayer.magnitude, groundLayer);
    }

    bool CanSeePlayer()
    {
        if (player == null || playerHealth == null || playerHealth.IsDead) return false;
        bool closeEnough = Vector2.Distance(rb.position, player.position) <= detectionRange;
        bool withinLeash = Vector2.Distance(rb.position, startPosition) <= leashRange;
        return closeEnough && withinLeash;
    }

    void Face(int direction)
    {
        Vector3 scale = transform.localScale;
        float size = Mathf.Abs(scale.x);
        scale.x = ((direction > 0) == artFacesRight) ? size : -size;
        transform.localScale = scale;
    }

    void OnDrawGizmosSelected()
    {
        Vector3 start = Application.isPlaying ? (Vector3)startPosition : transform.position;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(start + Vector3.left * patrolDistance, start + Vector3.right * patrolDistance);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }
}
