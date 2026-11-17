using System.Collections;
using UnityEngine;

// Factory Guard. It does not hate the Robot - it handles defective equipment.
// Patrols back and forth (also along conveyor belts, which carry it), stops at platform edges and
// looks around before turning. When it notices the Robot it freezes for a moment ("!") and then:
//   STRIKE - close range: raises the baton (yellow flash), then a short hit in front of it
//   RUSH   - mid range on the same floor: shakes (red), charges, then is dizzy for a moment (hit it then)
// The Robot's press arm knocks it back. A Scanner alarm (Alert) sends it towards the Robot even from afar.
// No pathfinding.
[RequireComponent(typeof(Rigidbody2D), typeof(EnemyHealth))]
public class GuardAI : MonoBehaviour
{
    [Header("Patrol")]
    [Tooltip("The Guard walks back and forth between these two points")]
    public Transform pointA;
    public Transform pointB;
    public float patrolSpeed = 2f;
    [Tooltip("Pause at the end of the route / at an edge before turning around")]
    public float turnPause = 0.6f;
    [Tooltip("Slowest ground speed while walking against a conveyor belt")]
    public float minHeadway = 1.2f;

    [Header("Chase")]
    public float detectionRange = 7f;
    [Tooltip("Ignore the player if they are this much higher or lower")]
    public float maxHeightDifference = 2.5f;
    public float chaseSpeed = 3.8f;
    [Tooltip("Freeze this long when the Robot is first noticed")]
    public float noticeTime = 0.35f;
    [Tooltip("After an alarm the Guard hunts the Robot this long, wherever it is")]
    public float alertDuration = 8f;

    [Header("Strike")]
    public float attackRange = 1.3f;
    [Tooltip("Pause before the strike lands, so the player can react")]
    public float attackWindup = 0.35f;
    public float attackCooldown = 1.2f;
    public int damage = 1;

    [Header("Rush")]
    public Vector2 rushRange = new Vector2(3f, 7f);
    public float rushWindup = 0.55f;
    public float rushSpeed = 10f;
    public float rushDuration = 0.7f;
    [Tooltip("Dizzy time after a rush - a good moment to hit it")]
    public float rushRecovery = 0.8f;
    public float rushCooldown = 4f;

    [Header("Telegraph Colours")]
    public Color strikeColor = new Color(1f, 0.85f, 0.4f);
    public Color rushColor = new Color(1f, 0.3f, 0.3f);

    [Header("Ground Sensing")]
    public LayerMask groundLayer;
    [Tooltip("How far ahead to look for ledges and walls")]
    public float lookAheadDistance = 0.8f;

    [Header("Art")]
    [Tooltip("Does the Guard artwork face right?")]
    public bool artFacesRight = true;

    [Header("Audio")]
    public AudioSource attackSound;
    public AudioSource noticeSound;

    enum Pattern { None, Strike, Rush }

    private Rigidbody2D rb;
    private EnemyHealth health;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private Transform player;
    private PlayerHealth playerHealth;
    private float leftX, rightX;
    private int moveDirection = 1;
    private bool attacking;
    private bool noticedPlayer;
    private float nextAttackTime;
    private float nextRushTime;
    private float alertedUntil;
    private float pausedUntil;
    private Vector3 baseScale;

    Vector2 PlayerChest => (Vector2)player.position + Vector2.up * 0.8f;
    Vector2 BodyCenter => (Vector2)transform.position + Vector2.up * 0.8f;
    public bool IsAlerted => Time.time < alertedUntil;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        health = GetComponent<EnemyHealth>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        baseScale = new Vector3(Mathf.Abs(transform.localScale.x), transform.localScale.y, transform.localScale.z);
        if (groundLayer == 0) groundLayer = LayerMask.GetMask("Ground");

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

        // Stagger the first rush so a group doesn't attack in sync
        nextRushTime = Time.time + Random.Range(1f, 3f);

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            player = playerObject.transform;
            playerHealth = playerObject.GetComponent<PlayerHealth>();
        }

        ApplyLevelScaling();
    }

    // Stronger Guards for a stronger Robot (ProgressionSettings tiers). Wind-ups shrink only half as much
    // as cooldowns so every attack still has a readable warning.
    void ApplyLevelScaling()
    {
        ProgressionSettings.Tier s = health.Scaling;
        patrolSpeed *= s.moveSpeed;
        chaseSpeed *= s.moveSpeed;
        rushSpeed *= s.moveSpeed;
        attackCooldown /= s.attackSpeed;
        rushCooldown /= s.attackSpeed;
        float telegraph = Mathf.Lerp(1f, s.attackSpeed, 0.5f);
        attackWindup /= telegraph;
        rushWindup /= telegraph;
        rushRecovery /= telegraph;
        detectionRange *= s.aggression;
    }

    // Called by the alarm system: hunt the Robot for a while
    public void Alert()
    {
        if (health == null || health.IsDead) return;
        alertedUntil = Time.time + alertDuration;
        if (!noticedPlayer) StartCoroutine(Notice());
    }

    void FixedUpdate()
    {
        if (health.IsDead) return;
        if (transform.position.y < -8f) { Destroy(gameObject); return; } // fell out of the section
        if (attacking) return;            // the attack coroutine moves the Guard
        if (health.IsStunned) return;     // let the knockback play out

        float belt = IsGrounded() ? ConveyorBelt.SpeedUnder((Vector2)transform.position, groundLayer) : 0f;
        float velocityX;

        if (CanSeePlayer())
        {
            if (!noticedPlayer)
            {
                StartCoroutine(Notice());
                return;
            }
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
            noticedPlayer = false;
            // --- Patrol ---
            if (Time.time < pausedUntil)
            {
                velocityX = 0f;
            }
            else
            {
                bool atEnd = (moveDirection > 0 && transform.position.x >= rightX) || (moveDirection < 0 && transform.position.x <= leftX);
                if (atEnd || !PathIsClear())
                {
                    StartCoroutine(LookAroundAndTurn());
                    velocityX = 0f;
                }
                else velocityX = moveDirection * patrolSpeed;
            }
        }

        // Standing still, the Guard rides the belt. Walking against a fast belt it still makes headway
        // (otherwise a belt faster than its walk would push it off its route for good)
        float net = velocityX + belt;
        if (Mathf.Abs(velocityX) > 0.01f && (Mathf.Sign(net) != Mathf.Sign(velocityX) || Mathf.Abs(net) < minHeadway))
            net = Mathf.Sign(velocityX) * minHeadway;
        rb.linearVelocity = new Vector2(net, rb.linearVelocity.y);
        SetAnimSpeed(Mathf.Abs(velocityX));
        // A little bob while walking: heavy mechanical steps
        if (Mathf.Abs(velocityX) > 0.1f && !attacking)
        {
            float step = Mathf.Abs(Mathf.Sin(Time.time * 9f));
            SetScale(1f + step * 0.03f, 1f - step * 0.04f);
        }
    }

    // At an edge or the end of the route: stop, glance back, then turn around
    IEnumerator LookAroundAndTurn()
    {
        pausedUntil = Time.time + turnPause + 0.05f;
        SetScale(1f, 1f);
        yield return new WaitForSeconds(turnPause * 0.5f);
        if (health.IsDead || noticedPlayer) yield break;
        Face(-moveDirection);
    }

    // The Robot is spotted: freeze, flash, "!"
    IEnumerator Notice()
    {
        noticedPlayer = true;
        attacking = true;
        StopX();
        SetAnimSpeed(0f);
        if (player != null) Face(player.position.x > transform.position.x ? 1 : -1);
        if (noticeSound != null) noticeSound.Play();
        DamageNumber.Show(BodyCenter + Vector2.up * 1.3f, "!", new Color(1f, 0.85f, 0.3f), 1.4f, 0.6f);
        float timer = 0f;
        while (timer < noticeTime && !health.IsDead)
        {
            timer += Time.deltaTime;
            SetScale(1f - 0.06f * Mathf.Sin(timer / noticeTime * Mathf.PI), 1f + 0.08f * Mathf.Sin(timer / noticeTime * Mathf.PI));
            yield return null;
        }
        SetScale(1f, 1f);
        nextAttackTime = Mathf.Max(nextAttackTime, Time.time + 0.2f);
        attacking = false;
    }

    Pattern ChoosePattern(float distance)
    {
        if (Time.time < nextAttackTime || !IsGrounded()) return Pattern.None;
        if (distance <= attackRange) return Pattern.Strike;

        bool canRush = Time.time >= nextRushTime && distance >= rushRange.x && distance <= rushRange.y
                       && Mathf.Abs(player.position.y - transform.position.y) < 1f && PathIsClear();
        return canRush ? Pattern.Rush : Pattern.None;
    }

    IEnumerator RunPattern(Pattern pattern)
    {
        attacking = true;
        SetAnimSpeed(0f);

        switch (pattern)
        {
            case Pattern.Strike: yield return Strike(); break;
            case Pattern.Rush: yield return Rush(); break;
        }

        SetScale(1f, 1f);
        if (!health.IsDead && spriteRenderer != null) spriteRenderer.color = Color.white;
        attacking = false;
    }

    // ---------------- STRIKE ----------------
    IEnumerator Strike()
    {
        nextAttackTime = Time.time + attackCooldown;
        if (animator != null) animator.SetTrigger("Attack");
        // Lean back, baton raised
        yield return Telegraph(attackWindup, strikeColor, 0.92f, 1.08f);
        if (health.IsDead || health.IsStunned) yield break;

        // Only hurt the player if they are still in reach after the wind-up
        if (player != null && !playerHealth.IsDead)
        {
            Vector2 offset = player.position - transform.position;
            if (Mathf.Abs(offset.x) <= attackRange + 0.3f && Mathf.Abs(offset.y) <= 1.5f && Mathf.Sign(offset.x) == moveDirection)
            {
                playerHealth.TakeDamage(damage, transform.position, health.DamageMultiplier);
            }
        }
        if (attackSound != null) attackSound.Play();
        Fx.Sparks(BodyCenter + new Vector2(moveDirection * 0.9f, 0f), strikeColor, 5, 3f);
        // Lean into the strike so it reads clearly
        SetScale(1.12f, 0.92f);
        rb.linearVelocity = new Vector2(moveDirection * 2.5f, rb.linearVelocity.y);
        yield return new WaitForSeconds(0.2f);
        StopX();
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
            SetScale(1.15f, 0.88f);
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
                    new Color(0.6f, 0.6f, 0.65f, 0.8f), Color.clear, 4f);
            }
            timer += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        // Skid to a stop and stay dizzy (open to attacks)
        if (health.IsDead || health.IsStunned) yield break;
        StopX();
        if (spriteRenderer != null) spriteRenderer.color = Color.white;
        float dizzy = 0f;
        float nextSpark = 0f;
        while (dizzy < rushRecovery && !health.IsDead)
        {
            dizzy += Time.deltaTime;
            float wobble = Mathf.Sin(dizzy * 25f) * 0.08f;
            SetScale(1f + wobble, 1f - wobble);
            if (dizzy >= nextSpark)
            {
                nextSpark = dizzy + 0.2f;
                Fx.Sparks(BodyCenter + Vector2.up * 0.7f, new Color(0.5f, 0.8f, 1f), 2, 2f);
            }
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
        // Alerted Guards track the Robot along their floor from much further away
        float range = IsAlerted ? detectionRange * 3f : detectionRange;
        return Mathf.Abs(offset.x) <= range && Mathf.Abs(offset.y) <= maxHeightDifference;
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
    }
}
