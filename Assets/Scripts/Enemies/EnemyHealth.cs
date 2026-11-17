using System.Collections;
using UnityEngine;

// Health for every enemy (Virus Bug, Scanner Drone and the Boss).
// Handles damage, knockback, a short stun, hit flash and death.
// On death the enemy blows up: its sprite shatters into pieces with sparks and a shockwave.
// The boss goes through a chain of small explosions before the final blast.
public class EnemyHealth : MonoBehaviour
{
    // Any enemy got hit (enemy, killed?). The Robot's ultimate charges from this.
    public static event System.Action<EnemyHealth, bool> Damaged;

    [Tooltip("Base health in hits. Real HP = hits x hitValue x the difficulty multiplier for the Robot's level (ProgressionSettings)")]
    public int maxHealth = 3;
    public bool receivesKnockback = true;
    [Tooltip("The AI pauses this long after a hit so the knockback can play out")]
    public float stunTime = 0.3f;
    [Tooltip("Normal enemies: flicker time before exploding. Boss: length of the explosion chain")]
    public float deathFadeTime = 0.6f;
    [Tooltip("Destroy the object after death (off = just disable it)")]
    public bool destroyOnDeath = true;
    [Tooltip("Boss only: after the explosion chain the wreck stays (its Death animation) instead of shattering")]
    public bool keepWreck = false;

    [Header("Death Explosion")]
    [Tooltip("Leave fully transparent to pick a colour that matches the enemy type")]
    public Color explosionColor = Color.clear;
    [Tooltip("0 = pick automatically (1 for enemies, 2.5 for the boss)")]
    public float explosionSize = 0f;
    [Tooltip("The sprite is cut into N x N shards")]
    public int shatterPieces = 4;

    [Header("Audio")]
    public AudioSource hitSound;
    public AudioSource deathSound;

    // The boss switches this on when its core is closed
    [HideInInspector] public bool invulnerable;

    // Real HP after level scaling (use this instead of maxHealth)
    public int MaxHealth { get; private set; }
    public int CurrentHealth { get; private set; }
    public bool IsDead { get; private set; }
    public EnemyRank Rank { get; private set; }
    // How strong this enemy is for the Robot's level. AI scripts read speed / attack rate / aggression from it.
    public ProgressionSettings.Tier Scaling { get; private set; } = ProgressionSettings.Tier.One;
    public float DamageMultiplier => Scaling.damage;
    public bool IsStunned => Time.time < stunnedUntil;

    private Rigidbody2D rb;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private float stunnedUntil;
    private bool isBoss;
    private EnemyHealthBar healthBar;
    private float nextBlockedLabel;
    private string killId;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        isBoss = GetComponent<BossController>() != null || GetComponent<SentinelBossController>() != null;
        BossController core = GetComponent<BossController>();
        Rank = !isBoss ? EnemyRank.Normal : (core != null && core.isMiniBoss ? EnemyRank.MiniBoss : EnemyRank.Boss);

        // Stronger for a stronger Robot
        Scaling = PlayerProgression.TierFor(Rank);
        MaxHealth = Mathf.Max(1, Mathf.RoundToInt(maxHealth * PlayerProgression.HitValue * Scaling.health));
        CurrentHealth = MaxHealth;

        // Same enemy = same ID even if its room is loaded again, so it never gives XP twice
        Vector3 start = transform.position;
        killId = gameObject.scene.name + "/" + name + "@" + Mathf.RoundToInt(start.x * 10f) + "," + Mathf.RoundToInt(start.y * 10f);

        if (explosionColor.a <= 0f)
        {
            if (isBoss) explosionColor = new Color(1f, 0.3f, 0.6f);
            else if (GetComponent<ScannerDroneAI>() != null) explosionColor = new Color(1f, 0.4f, 0.25f);
            else explosionColor = new Color(0.8f, 0.35f, 1f);
        }
        if (explosionSize <= 0f) explosionSize = isBoss ? 2.5f : 1f;
    }

    Vector2 Center => spriteRenderer != null ? (Vector2)spriteRenderer.bounds.center : (Vector2)transform.position;
    // Where damage numbers appear
    Vector2 TopOfSprite => spriteRenderer != null
        ? new Vector2(spriteRenderer.bounds.center.x, spriteRenderer.bounds.max.y - (isBoss ? 0.6f : 0f))
        : (Vector2)transform.position + Vector2.up;

    // Returns true if the hit did damage. ignoreInvulnerable = the ultimate pierces the boss armour.
    public bool TakeDamage(int amount, Vector2 knockback, bool ignoreInvulnerable = false)
    {
        if (IsDead) return false;
        if (invulnerable && !ignoreInvulnerable)
        {
            // Armour: show that the hit did nothing (at most every 0.3 s)
            if (Time.time >= nextBlockedLabel)
            {
                nextBlockedLabel = Time.time + 0.3f;
                DamageNumber.Show(TopOfSprite, "BLOCKED", DamageNumber.BlockedColor, 0.8f, 0.6f);
            }
            return false;
        }

        int dealt = Mathf.Min(amount, CurrentHealth);
        CurrentHealth = Mathf.Max(CurrentHealth - amount, 0);
        bool heavy = amount >= PlayerProgression.HitValue * 2.5f;
        DamageNumber.Show(TopOfSprite, "-" + dealt, heavy ? DamageNumber.HeavyHitColor : DamageNumber.EnemyHitColor, heavy ? 1.4f : 1f);
        if (!isBoss)
        {
            if (healthBar == null) healthBar = EnemyHealthBar.Create(this);
            healthBar.Refresh(CurrentHealth);
        }
        if (hitSound != null) hitSound.Play();
        if (animator != null) animator.SetTrigger("Hurt");
        StartCoroutine(HitFlash());
        Fx.Sparks(Center, explosionColor, 7, 6f);

        if (receivesKnockback && rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            rb.linearVelocity = Vector2.zero;
            rb.AddForce(knockback * rb.mass, ForceMode2D.Impulse);
            stunnedUntil = Time.time + stunTime;
        }

        if (CurrentHealth == 0) Die();
        Damaged?.Invoke(this, IsDead);
        return true;
    }

    void Die()
    {
        IsDead = true;
        if (animator != null) animator.SetBool("Dead", true);

        int xp = PlayerProgression.RegisterKill(killId, Rank);
        if (xp > 0) DamageNumber.Show(TopOfSprite + Vector2.up * 0.5f, "+" + xp + " XP", DamageNumber.XpColor, isBoss ? 1.4f : 0.9f, 1.1f);
        if (deathSound != null) deathSound.Play();

        // Stop colliding and stop moving
        foreach (Collider2D col in GetComponentsInChildren<Collider2D>()) col.enabled = false;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.simulated = false;
        }

        StartCoroutine(isBoss ? BossExplosion() : Explode());
    }

    IEnumerator HitFlash()
    {
        if (spriteRenderer == null) yield break;
        spriteRenderer.color = new Color(1f, 0.4f, 0.4f);
        yield return new WaitForSeconds(0.1f);
        if (!IsDead) spriteRenderer.color = Color.white;
    }

    // Normal enemies: swell and flicker for a moment, then burst
    IEnumerator Explode()
    {
        Vector3 startScale = transform.localScale;
        float windup = Mathf.Min(deathFadeTime, 0.2f);
        float timer = 0f;
        while (timer < windup)
        {
            timer += Time.deltaTime;
            transform.localScale = startScale * (1f + 0.2f * (timer / windup));
            if (spriteRenderer != null) spriteRenderer.color = Mathf.FloorToInt(timer * 30f) % 2 == 0 ? Color.white : explosionColor;
            yield return null;
        }
        transform.localScale = startScale;
        if (spriteRenderer != null) spriteRenderer.color = Color.white;

        Blast(explosionSize, shatterPieces);
        yield return FinishAfter(1f);
    }

    // Boss: random explosions all over it, then one huge blast
    IEnumerator BossExplosion()
    {
        float duration = Mathf.Max(deathFadeTime, 1.2f);
        float timer = 0f, nextBlast = 0f;
        Bounds bounds = spriteRenderer != null ? spriteRenderer.bounds : new Bounds(transform.position, Vector3.one * 2f);

        while (timer < duration)
        {
            timer += Time.deltaTime;
            if (timer >= nextBlast)
            {
                nextBlast = timer + Random.Range(0.08f, 0.18f);
                Vector2 point = (Vector2)bounds.center + Vector2.Scale(Random.insideUnitCircle, bounds.extents);
                Fx.Explosion(point, explosionColor, Random.Range(0.5f, 0.9f));
                CameraFollow.Shake(0.25f, 0.2f);
            }
            if (spriteRenderer != null) spriteRenderer.color = Mathf.FloorToInt(timer * 16f) % 2 == 0 ? Color.white : explosionColor;
            yield return null;
        }

        if (UIManager.Instance != null) UIManager.Instance.Flash(new Color(1f, 0.85f, 0.95f, 0.8f), 0.6f);
        if (keepWreck)
        {
            // One big blast, but the smoking wreck stays on the floor
            if (spriteRenderer != null) spriteRenderer.color = Color.white;
            Fx.Explosion(Center, explosionColor, explosionSize);
            CameraFollow.Shake(0.12f * explosionSize, 0.15f + 0.1f * explosionSize);
            yield break;
        }
        Blast(explosionSize, shatterPieces + 2);
        yield return FinishAfter(1.5f);
    }

    void Blast(float size, int pieces)
    {
        Vector2 push = rb != null ? rb.linearVelocity * 0.3f : Vector2.zero;
        Fx.Shatter(spriteRenderer, pieces, push);
        Fx.Explosion(Center, explosionColor, size);
        CameraFollow.Shake(0.12f * size, 0.15f + 0.1f * size);
        if (spriteRenderer != null) spriteRenderer.enabled = false;
    }

    // Wait a little so the death sound can finish, then remove the enemy
    IEnumerator FinishAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (destroyOnDeath) Destroy(gameObject);
        else gameObject.SetActive(false);
    }
}
