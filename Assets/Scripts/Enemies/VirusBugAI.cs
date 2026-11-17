using System.Collections;
using UnityEngine;

// Ground enemy. Patrols between two points, chases the player when close, and picks
// one of three attacks. Each one has a clear warning (flash colour + pose):
//   BITE   - close range, short wind-up, then a snap in front of it
//   POUNCE - mid range, crouches (orange) and leaps at the player in an arc
//   RUSH   - mid range, shakes (red) and charges along the floor, then is dizzy for a moment
// No pathfinding.
[RequireComponent(typeof(Rigidbody2D), typeof(EnemyHealth))]
public class VirusBugAI : MonoBehaviour
{
    [Header("Patrol")]
    [Tooltip("The bug walks back and forth between these two points")]
    public Transform pointA;
    public Transform pointB;
    public float patrolSpeed = 2f;

    [Header("Chase")]
    public float detectionRange = 7f;
    [Tooltip("Ignore the player if they are this much higher or lower")]
    public float maxHeightDifference = 2.5f;
    public float chaseSpeed = 4f;

    [Header("Bite")]
    public float attackRange = 1.3f;
    [Tooltip("Pause before the bite lands, so the player can react")]
    public float attackWindup = 0.3f;
    public float attackCooldown = 1.2f;
    public int damage = 1;

    [Header("Pounce")]
    [Tooltip("Pounce when the player is between these distances")]
    public Vector2 pounceRange = new Vector2(2.5f, 5.5f);
    public float pounceWindup = 0.45f;
    public float pounceJumpSpeed = 10f;
    public float pounceCooldown = 3.5f;

    [Header("Rush")]
    public Vector2 rushRange = new Vector2(3f, 7f);
    public float rushWindup = 0.55f;
    public float rushSpeed = 11f;
    public float rushDuration = 0.7f;
    [Tooltip("Dizzy time after a rush - a good moment to hit it")]
    public float rushRecovery = 0.7f;
    public float rushCooldown = 4f;

    [Header("Telegraph Colours")]
    public Color biteColor = new Color(1f, 0.85f, 0.4f);
    public Color pounceColor = new Color(1f, 0.6f, 0.2f);
    public Color rushColor = new Color(1f, 0.3f, 0.3f);

    [Header("Ground Sensing")]
    public LayerMask groundLayer;
    [Tooltip("How far ahead to look for ledges and walls")]
    public float lookAheadDistance = 0.8f;

    [Header("Art")]
    [Tooltip("The provided Virus Bug artwork faces LEFT")]
    public bool artFacesRight = false;

    [Header("Audio")]
    public AudioSource attackSound;

    enum Pattern { None, Bite, Pounce, Rush }

    private Rigidbody2D rb;
    private EnemyHealth health;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private Transform player;
    private PlayerHealth playerHealth;
    private float leftX, rightX;
    private int moveDirection = 1;
    private bool attacking;
    private float nextAttackTime;
    private float nextPounceTime;
    private float nextRushTime;
    private Pattern lastSpecial = Pattern.None;
    private Vector3 baseScale;

    Vector2 PlayerChest => (Vector2)player.position + Vector2.up * 0.8f;
    Vector2 BodyCenter => (Vector2)transform.position + Vector2.up * 0.5f;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        health = GetComponent<EnemyHealth>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        baseScale = new Vector3(Mathf.Abs(transform.localScale.x), transform.localScale.y, transform.localScale.z);

        // Remember the patrol limits in world space (the points are children and would move with us)
        if (pointA != null && pointB != null)
        {
            leftX = Mathf.Min(pointA.position.x, pointB.position.x);
            rightX = Mathf.Max(pointA.position.x, pointB.position.x);
        }
        else
        {
            leftX = transform.position.x - 3f;
            rightX = transform.position.x + 3f;
        }

        // Stagger the first special attacks so a group doesn't attack in sync
        nextPounceTime = Time.time + Random.Range(0.5f, 2f);
        nextRushTime = Time.time + Random.Range(1f, 3f);

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            player = playerObject.transform;
            playerHealth = playerObject.GetComponent<PlayerHealth>();
        }

        ApplyLevelScaling();
    }

    // A higher-level Robot meets faster, more aggressive bugs. Wind-ups shrink only half as much
    // as cooldowns so every attack still has a readable warning.
    void ApplyLevelScaling()
    {
        ProgressionSettings.Tier s = health.Scaling;
        patrolSpeed *= s.moveSpeed;
        chaseSpeed *= s.moveSpeed;
        rushSpeed *= s.moveSpeed;
        attackCooldown /= s.attackSpeed;
        pounceCooldown /= s.attackSpeed;
        rushCooldown /= s.attackSpeed;
        float telegraph = Mathf.Lerp(1f, s.attackSpeed, 0.5f);
        attackWindup /= telegraph;
        pounceWindup /= telegraph;
        rushWindup /= telegraph;
        rushRecovery /= telegraph;
        detectionRange *= s.aggression;
    }

    void FixedUpdate()
    {
        if (health.IsDead) return;
        if (transform.position.y < -8f) { Destroy(gameObject); return; } // fell out of the room
        if (attacking) return;            // the attack coroutine moves the bug
        if (health.IsStunned) return;     // let the knockback play out

        float velocityX;
        if (CanSeePlayer())
        {
            // --- Chase ---
            float dx = player.position.x - transform.position.x;
            float distance = Mathf.Abs(dx);
            Face(dx > 0f ? 1 : -1);

            Pattern pattern = ChoosePattern(distance);
            if (pattern != Pattern.None)
            {
                StartCoroutine(RunPattern(pattern));
                return;
            }

            if (distance <= attackRange) velocityX = 0f;
            // Don't run off ledges or into walls while chasing
            else velocityX = PathIsClear() ? moveDirection * chaseSpeed : 0f;
        }
        else
        {
            // --- Patrol ---
            if (transform.position.x >= rightX) Face(-1);
            else if (transform.position.x <= leftX) Face(1);
            if (!PathIsClear()) Face(-moveDirection);
            velocityX = moveDirection * patrolSpeed;
        }

        rb.linearVelocity = new Vector2(velocityX, rb.linearVelocity.y);
        SetAnimSpeed(Mathf.Abs(velocityX));
    }

    Pattern ChoosePattern(float distance)
    {
        if (Time.time < nextAttackTime || !IsGrounded()) return Pattern.None;
        if (distance <= attackRange) return Pattern.Bite;

        bool canPounce = Time.time >= nextPounceTime && distance >= pounceRange.x && distance <= pounceRange.y;
        bool canRush = Time.time >= nextRushTime && distance >= rushRange.x && distance <= rushRange.y
                       && Mathf.Abs(player.position.y - transform.position.y) < 1f && PathIsClear();

        // Prefer the attack we didn't use last time
        if (canPounce && canRush) return lastSpecial == Pattern.Pounce ? Pattern.Rush : Pattern.Pounce;
        if (canPounce) return Pattern.Pounce;
        if (canRush) return Pattern.Rush;
        return Pattern.None;
    }

    IEnumerator RunPattern(Pattern pattern)
    {
        attacking = true;
        SetAnimSpeed(0f);
        if (pattern != Pattern.Bite) lastSpecial = pattern;

        switch (pattern)
        {
            case Pattern.Bite: yield return Bite(); break;
            case Pattern.Pounce: yield return Pounce(); break;
            case Pattern.Rush: yield return Rush(); break;
        }

        transform.localScale = new Vector3(Mathf.Sign(transform.localScale.x) * baseScale.x, baseScale.y, baseScale.z);
        if (!health.IsDead && spriteRenderer != null) spriteRenderer.color = Color.white;
        attacking = false;
    }

    // ---------------- BITE ----------------
    IEnumerator Bite()
    {
        nextAttackTime = Time.time + attackCooldown;
        if (animator != null) animator.SetTrigger("Attack");
        yield return Telegraph(attackWindup, biteColor, 0.9f, 1.1f);
        if (health.IsDead || health.IsStunned) yield break;

        // Only hurt the player if they are still in reach after the wind-up
        if (player != null && !playerHealth.IsDead)
        {
            Vector2 offset = player.position - transform.position;
            if (Mathf.Abs(offset.x) <= attackRange + 0.3f && Mathf.Abs(offset.y) <= 1.5f)
            {
                playerHealth.TakeDamage(damage, transform.position, health.DamageMultiplier);
                if (attackSound != null) attackSound.Play();
            }
        }
        // small lunge so the bite reads clearly
        rb.linearVelocity = new Vector2(moveDirection * 3f, rb.linearVelocity.y);
        yield return new WaitForSeconds(0.2f);
        StopX();
    }

    // ---------------- POUNCE ----------------
    IEnumerator Pounce()
    {
        nextPounceTime = Time.time + pounceCooldown;
        nextAttackTime = Time.time + attackCooldown;
        if (animator != null) animator.SetTrigger("Attack");

        // Crouch: squash down and flash orange
        yield return Telegraph(pounceWindup, pounceColor, 1.15f, 0.75f);
        if (health.IsDead || health.IsStunned || player == null) yield break;

        // Jump in an arc that lands roughly where the player is now
        float dx = player.position.x - transform.position.x;
        Face(dx > 0f ? 1 : -1);
        float gravity = Mathf.Abs(Physics2D.gravity.y * rb.gravityScale);
        float airTime = 2f * pounceJumpSpeed / gravity;
        float speedX = Mathf.Clamp(dx / airTime, -9f, 9f);
        rb.linearVelocity = new Vector2(speedX, pounceJumpSpeed);
        if (attackSound != null) attackSound.Play();
        Fx.Sparks(transform.position, pounceColor, 6, 3f);

        // Stretch while in the air, hurt on contact
        bool hit = false;
        float timer = 0f;
        yield return new WaitForFixedUpdate();
        while (timer < airTime + 0.5f && !health.IsDead && !health.IsStunned)
        {
            SetScale(0.85f, 1.2f);
            if (!hit && TouchingPlayer(1f))
            {
                playerHealth.TakeDamage(damage, transform.position, health.DamageMultiplier);
                hit = true;
            }
            if (timer > 0.15f && IsGrounded()) break;
            timer += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        // Landing: squash + dust
        if (!health.IsDead && !health.IsStunned)
        {
            StopX();
            Fx.Sparks(transform.position, new Color(0.6f, 0.6f, 0.7f), 8, 3f);
            SetScale(1.25f, 0.75f);
            yield return new WaitForSeconds(0.35f);
        }
    }

    // ---------------- RUSH ----------------
    IEnumerator Rush()
    {
        nextRushTime = Time.time + rushCooldown;
        nextAttackTime = Time.time + attackCooldown;
        if (animator != null) animator.SetTrigger("Attack");

        // Shake in place and flash red, stepping back a little
        float timer = 0f;
        while (timer < rushWindup && !health.IsDead && !health.IsStunned)
        {
            timer += Time.fixedDeltaTime;
            float shake = Mathf.Sin(timer * 90f) * 2f;
            rb.linearVelocity = new Vector2(-moveDirection * 0.8f + shake, rb.linearVelocity.y);
            if (spriteRenderer != null) spriteRenderer.color = Mathf.FloorToInt(timer * 14f) % 2 == 0 ? rushColor : Color.white;
            SetScale(1.1f, 0.9f);
            yield return new WaitForFixedUpdate();
        }
        if (spriteRenderer != null) spriteRenderer.color = rushColor;
        if (health.IsDead || health.IsStunned) yield break;

        // Charge straight ahead until the time runs out or a wall/ledge is in the way
        if (attackSound != null) attackSound.Play();
        bool hit = false;
        timer = 0f;
        float trailTimer = 0f;
        while (timer < rushDuration && !health.IsDead && !health.IsStunned && PathIsClear())
        {
            rb.linearVelocity = new Vector2(moveDirection * rushSpeed, rb.linearVelocity.y);
            SetScale(1.2f, 0.85f);
            if (!hit && TouchingPlayer(1.1f))
            {
                playerHealth.TakeDamage(damage, transform.position, health.DamageMultiplier);
                hit = true;
            }
            trailTimer -= Time.fixedDeltaTime;
            if (trailTimer <= 0f)
            {
                trailTimer = 0.05f;
                Fx.Spawn(Fx.Pixel, (Vector2)transform.position + Vector2.up * 0.1f, new Vector2(-moveDirection * 2f, 1.5f), 0.3f, 1.2f, 0.2f,
                    new Color(0.6f, 0.6f, 0.7f, 0.8f), Color.clear, 4f);
            }
            timer += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        // Skid to a stop and stay dizzy (open to attacks)
        if (health.IsDead || health.IsStunned) yield break;
        StopX();
        if (spriteRenderer != null) spriteRenderer.color = Color.white;
        float dizzy = 0f;
        while (dizzy < rushRecovery && !health.IsDead)
        {
            dizzy += Time.deltaTime;
            float wobble = Mathf.Sin(dizzy * 25f) * 0.08f;
            SetScale(1f + wobble, 1f - wobble);
            yield return null;
        }
    }

    // ---------------- Helpers ----------------

    // Flash a colour and squash/stretch while winding up
    IEnumerator Telegraph(float duration, Color color, float scaleX, float scaleY)
    {
        float timer = 0f;
        while (timer < duration && !health.IsDead && !health.IsStunned)
        {
            timer += Time.deltaTime;
            float t = timer / duration;
            SetScale(Mathf.Lerp(1f, scaleX, t), Mathf.Lerp(1f, scaleY, t));
            if (spriteRenderer != null) spriteRenderer.color = Mathf.FloorToInt(timer * 16f) % 2 == 0 ? color : Color.white;
            StopX();
            yield return null;
        }
    }

    bool TouchingPlayer(float radius)
    {
        return player != null && !playerHealth.IsDead && Vector2.Distance(BodyCenter, PlayerChest) < radius;
    }

    bool CanSeePlayer()
    {
        if (player == null || playerHealth == null || playerHealth.IsDead) return false;
        Vector2 offset = player.position - transform.position;
        return Mathf.Abs(offset.x) <= detectionRange && Mathf.Abs(offset.y) <= maxHeightDifference;
    }

    bool IsGrounded()
    {
        return Physics2D.Raycast((Vector2)transform.position + Vector2.up * 0.1f, Vector2.down, 0.25f, groundLayer);
    }

    void StopX()
    {
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    // True if there is floor ahead and no wall in the way
    bool PathIsClear()
    {
        Vector2 position = transform.position;
        Vector2 ledgeOrigin = position + new Vector2(moveDirection * lookAheadDistance, 0.3f);
        bool groundAhead = Physics2D.Raycast(ledgeOrigin, Vector2.down, 0.8f, groundLayer);
        bool wallAhead = Physics2D.Raycast(position + Vector2.up * 0.5f, Vector2.right * moveDirection, lookAheadDistance, groundLayer);
        return groundAhead && !wallAhead;
    }

    void Face(int direction)
    {
        moveDirection = direction;
        Vector3 scale = transform.localScale;
        float size = Mathf.Abs(scale.x);
        scale.x = ((direction > 0) == artFacesRight) ? size : -size;
        transform.localScale = scale;
    }

    // Squash/stretch relative to the normal size, keeping the facing direction
    void SetScale(float x, float y)
    {
        float sign = Mathf.Sign(transform.localScale.x);
        transform.localScale = new Vector3(sign * baseScale.x * x, baseScale.y * y, baseScale.z);
    }

    void SetAnimSpeed(float speed)
    {
        if (animator != null) animator.SetFloat("Speed", speed);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
        Gizmos.color = new Color(1f, 0.6f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, pounceRange.y);
    }
}
