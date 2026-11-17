using System.Collections;
using UnityEngine;

// Robot health: 3 hits (x hitValue HP) plus more HP every level, a short invincibility after each hit,
// and respawning at the latest checkpoint after death. Also reacts to hazards (tag "Hazard") and
// bottomless pits (tag "DeathZone").
// Levels (PlayerProgression) add max HP, defense (less damage taken) and out-of-combat regeneration.
// Death: the Robot_Death animation plays (visor turns red, glitches, collapses),
// sparks fly, then the wreck pops in a small explosion before the Robot reboots at the checkpoint.
[RequireComponent(typeof(PlayerController))]
public class PlayerHealth : MonoBehaviour
{
    [Tooltip("Base health in hits at level 1 (real HP = hits x hitValue + level bonus, see ProgressionSettings)")]
    public int maxHealth = 3;
    [Tooltip("Seconds of invincibility after taking damage")]
    public float invincibleTime = 1.2f;
    public Vector2 hurtKnockback = new Vector2(7f, 9f);
    [Tooltip("How long the player can't steer while being knocked back")]
    public float hurtControlLock = 0.25f;
    [Tooltip("Seconds between dying and respawning")]
    public float respawnDelay = 1.5f;

    [Header("Death Effect")]
    [Tooltip("Length of the Robot_Death clip (8 frames at 10 fps)")]
    public float deathAnimationLength = 0.8f;
    public Color energyColor = new Color(0.45f, 0.95f, 1f);
    public Color damageColor = new Color(1f, 0.35f, 0.3f);

    [Header("Audio")]
    public AudioSource hurtSound;
    public AudioSource deathSound;

    public int CurrentHealth { get; private set; }
    // Real max HP for the current level
    public int MaxHealth { get; private set; }
    public bool IsDead { get; private set; }
    // Cutscenes switch this on so nothing can hurt the Robot
    [HideInInspector] public bool invulnerable;

    private PlayerController controller;
    private PlayerBuffs buffs;
    private Rigidbody2D rb;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private float invincibleUntil;
    private Coroutine blinkRoutine;
    private float lastDamageTime = -100f;
    private float regenBuffer;

    Vector2 Chest => (Vector2)transform.position + Vector2.up * 0.9f;

    void Awake()
    {
        controller = GetComponent<PlayerController>();
        buffs = GetComponent<PlayerBuffs>();
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        MaxHealth = ComputeMaxHealth();
        CurrentHealth = MaxHealth;
    }

    // Base health (+ level bonus) + Reinforced Plating modules
    int ComputeMaxHealth()
    {
        return PlayerProgression.PlayerMaxHealth(maxHealth) + PlayerUpgrades.ExtraHealthCells * PlayerProgression.HitValue;
    }

    // A module changed the maximum: the new cell comes filled
    public void RefreshMaxHealth()
    {
        int oldMax = MaxHealth;
        MaxHealth = ComputeMaxHealth();
        if (!IsDead) CurrentHealth = Mathf.Clamp(CurrentHealth + (MaxHealth - oldMax), 0, MaxHealth);
        RefreshUI();
    }

    // Repair kits
    public void Heal(int amount)
    {
        if (IsDead || amount <= 0) return;
        CurrentHealth = Mathf.Min(CurrentHealth + amount, MaxHealth);
        RefreshUI();
        Fx.Sparks(Chest, DamageNumber.HealColor, 10, 4f);
        DamageNumber.Show(Chest + Vector2.up * 1.1f, "+" + amount, DamageNumber.HealColor, 1.1f);
    }

    void OnEnable() { PlayerProgression.LeveledUp += OnLevelUp; }
    void OnDisable() { PlayerProgression.LeveledUp -= OnLevelUp; }

    void Start()
    {
        RefreshUI();
    }

    void Update()
    {
        // Out-of-combat regeneration (from level 2)
        float regen = PlayerProgression.PlayerRegen;
        if (IsDead || regen <= 0f || CurrentHealth >= MaxHealth || Time.time < lastDamageTime + PlayerProgression.Settings.regenDelay)
        {
            regenBuffer = 0f;
            return;
        }
        regenBuffer += regen * Time.deltaTime;
        if (regenBuffer < 1f) return;
        int heal = Mathf.FloorToInt(regenBuffer);
        regenBuffer -= heal;
        CurrentHealth = Mathf.Min(CurrentHealth + heal, MaxHealth);
        RefreshUI();
    }

    // The level went up: more max HP (and a full heal), with a golden burst
    void OnLevelUp(int level)
    {
        int oldMax = MaxHealth;
        MaxHealth = ComputeMaxHealth();
        if (!IsDead)
        {
            CurrentHealth = PlayerProgression.Settings.healOnLevelUp ? MaxHealth : Mathf.Min(CurrentHealth + MaxHealth - oldMax, MaxHealth);
            Color gold = DamageNumber.LevelUpColor;
            Fx.Shockwave(Chest, gold, 5f, 0.5f);
            Fx.Shockwave(Chest, Color.white, 3f, 0.3f);
            Fx.Sparks(Chest, gold, 24, 8f);
            DamageNumber.Show(Chest + Vector2.up * 1.4f, "LEVEL UP!", gold, 1.5f, 1.6f);
        }
        RefreshUI();
    }

    // amount in hits from hazards and the environment (not scaled by enemy level)
    public void TakeDamage(int amount, Vector2 sourcePosition)
    {
        TakeDamage(amount, sourcePosition, 1f);
    }

    // amount in hits; multiplier = the attacker's damage scaling (EnemyHealth.DamageMultiplier).
    // sourcePosition = where the damage came from (the Robot is pushed away from it)
    public void TakeDamage(int amount, Vector2 sourcePosition, float multiplier)
    {
        if (IsDead || invulnerable || Time.time < invincibleUntil) return;

        // An energy shield takes the hit instead (short invincibility so one attack only breaks one charge)
        if (buffs != null && buffs.TryAbsorbHit())
        {
            invincibleUntil = Time.time + 0.5f;
            return;
        }

        // Hits -> HP, then the Robot's defense takes its share
        float raw = amount * PlayerProgression.HitValue * multiplier * (1f - PlayerProgression.PlayerDefense);
        amount = Mathf.Max(1, Mathf.RoundToInt(raw));
        lastDamageTime = Time.time;
        CurrentHealth = Mathf.Max(CurrentHealth - amount, 0);
        RefreshUI();
        DamageNumber.Show(Chest + Vector2.up * 1.1f, "-" + amount, DamageNumber.PlayerHitColor, 1.2f);
        Fx.Sparks(Chest, damageColor, 8, 6f);
        CameraFollow.Shake(0.15f, 0.15f);

        if (CurrentHealth == 0)
        {
            Die();
            return;
        }

        invincibleUntil = Time.time + invincibleTime;
        float direction = transform.position.x >= sourcePosition.x ? 1f : -1f;
        controller.ApplyKnockback(new Vector2(direction * hurtKnockback.x, hurtKnockback.y), hurtControlLock);

        if (animator != null) animator.SetTrigger("Hurt");
        if (hurtSound != null) hurtSound.Play();
        StartBlink();
    }

    void Die()
    {
        IsDead = true;
        if (buffs != null) buffs.ClearAll();
        controller.canControl = false;
        controller.StopMovement();
        if (animator != null) animator.SetBool("Dead", true);
        if (deathSound != null) deathSound.Play();

        // Stop the hurt blink so the death animation is fully visible
        if (blinkRoutine != null) StopCoroutine(blinkRoutine);
        spriteRenderer.enabled = true;

        StartCoroutine(DeathSequence());
    }

    IEnumerator DeathSequence()
    {
        CameraFollow.Shake(0.3f, 0.35f);
        if (UIManager.Instance != null) UIManager.Instance.Flash(new Color(1f, 0.2f, 0.2f, 0.45f), 0.35f);

        // Sparks and short-circuit crackle while the robot collapses
        float timer = 0f, nextSpark = 0f;
        while (timer < deathAnimationLength)
        {
            timer += Time.deltaTime;
            if (timer >= nextSpark)
            {
                nextSpark = timer + 0.1f;
                Vector2 point = Chest + Random.insideUnitCircle * 0.5f - Vector2.up * (timer / deathAnimationLength) * 0.5f;
                Fx.Sparks(point, Random.value < 0.5f ? energyColor : damageColor, 5, 4f);
            }
            yield return null;
        }

        // The wreck pops
        Vector2 wreck = (Vector2)transform.position + Vector2.up * 0.4f;
        Fx.Explosion(wreck, damageColor, 0.8f);
        CameraFollow.Shake(0.25f, 0.25f);

        yield return new WaitForSeconds(Mathf.Max(respawnDelay - deathAnimationLength, 0.6f));
        Respawn(GetRespawnPoint());
    }

    // Moves the Robot to the checkpoint with full health
    public void Respawn(Vector3 position)
    {
        MoveTo(position);
        CurrentHealth = MaxHealth;
        IsDead = false;
        controller.canControl = true;
        if (animator != null) animator.SetBool("Dead", false);

        invincibleUntil = Time.time + invincibleTime;
        StartBlink();
        RefreshUI();
        RebootEffect();
    }

    // Cyan ring + sparks where the Robot appears
    void RebootEffect()
    {
        Fx.Shockwave(Chest, energyColor, 3f, 0.4f);
        Fx.Sparks(Chest, energyColor, 12, 5f);
        if (CurrentHealth == MaxHealth) DamageNumber.Show(Chest + Vector2.up * 1.1f, "REBOOT  HP " + MaxHealth, DamageNumber.HealColor, 0.8f, 1.2f);
    }

    // Falling into a pit costs 1 hit and puts the Robot back at the checkpoint right away
    void FellIntoPit()
    {
        if (IsDead) return;

        int pitDamage = PlayerProgression.HitValue;
        lastDamageTime = Time.time;
        CurrentHealth = Mathf.Max(CurrentHealth - pitDamage, 0);
        RefreshUI();
        if (CurrentHealth == 0)
        {
            Die();
            return;
        }

        MoveTo(GetRespawnPoint());
        invincibleUntil = Time.time + invincibleTime;
        StartBlink();
        RebootEffect();
        DamageNumber.Show(Chest + Vector2.up * 1.1f, "-" + pitDamage, DamageNumber.PlayerHitColor, 1.2f);
    }

    // Teleport. Set both the Rigidbody2D and the Transform so interpolation doesn't undo it.
    void MoveTo(Vector3 position)
    {
        rb.position = position;
        transform.position = position;
        rb.linearVelocity = Vector2.zero;
    }

    Vector3 GetRespawnPoint()
    {
        return RespawnManager.Instance != null ? RespawnManager.Instance.RespawnPoint : transform.position;
    }

    void OnTriggerStay2D(Collider2D other)
    {
        if (!other.CompareTag("Hazard")) return;

        // Moving hazards (boss projectiles) push the Robot away from themselves.
        // Floor hazards push it backwards.
        Rigidbody2D hazardBody = other.attachedRigidbody;
        Vector2 source = hazardBody != null && hazardBody.bodyType != RigidbodyType2D.Static
            ? hazardBody.position
            : (Vector2)transform.position + Vector2.right * controller.FacingDirection;
        // Boss shots carry their boss's damage scaling; floor hazards are not scaled
        HazardDamage scaled = other.GetComponentInParent<HazardDamage>();
        TakeDamage(1, source, scaled != null ? scaled.multiplier : 1f);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("DeathZone")) FellIntoPit();
    }

    void StartBlink()
    {
        if (blinkRoutine != null) StopCoroutine(blinkRoutine);
        blinkRoutine = StartCoroutine(Blink());
    }

    // Flicker the sprite while invincible
    IEnumerator Blink()
    {
        while (Time.time < invincibleUntil)
        {
            spriteRenderer.enabled = !spriteRenderer.enabled;
            yield return new WaitForSeconds(0.08f);
        }
        spriteRenderer.enabled = true;
    }

    void RefreshUI()
    {
        if (UIManager.Instance != null) UIManager.Instance.UpdateHealth(CurrentHealth, MaxHealth);
    }
}
