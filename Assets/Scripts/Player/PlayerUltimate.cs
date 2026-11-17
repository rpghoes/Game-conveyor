using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Ultimate attack "SYSTEM PURGE". Hitting and killing enemies fills the meter; when it is full,
// press K: the Robot hangs in the air and charges up (invulnerable), then releases a huge energy
// shockwave. The wave damages every enemy it reaches (it pierces the boss armour), knocks them away
// and wipes out all enemy shots nearby.
[RequireComponent(typeof(PlayerController), typeof(PlayerHealth))]
public class PlayerUltimate : MonoBehaviour
{
    [Header("Charge")]
    [Tooltip("Meter gained per hit on an enemy (1 = full)")]
    public float chargePerHit = 0.08f;
    [Tooltip("Extra meter gained when the hit kills the enemy")]
    public float chargePerKill = 0.2f;

    [Header("Shockwave")]
    public float radius = 9f;
    [Tooltip("Damage in hits (x hitValue x the level bonus)")]
    public int damage = 3;
    public float knockbackForce = 14f;
    public float knockbackUpForce = 6f;
    [Tooltip("How long the Robot charges before the blast")]
    public float chargeTime = 0.7f;
    [Tooltip("How long the wave takes to reach its full radius")]
    public float waveTime = 0.35f;
    [Tooltip("Pause in the air after the blast before control returns")]
    public float recoveryTime = 0.25f;
    [Tooltip("Leave empty to use the Enemy layer from PlayerCombat")]
    public LayerMask enemyLayer;
    public Color energyColor = new Color(0.45f, 0.95f, 1f);

    [Header("Audio")]
    public AudioSource chargeSound;
    public AudioSource blastSound;

    public float Charge { get; private set; }
    public bool IsReady => Charge >= 1f;
    public bool IsActive { get; private set; }

    private PlayerController controller;
    private PlayerHealth health;
    private Animator animator;
    private SpriteRenderer spriteRenderer;

    Vector2 Chest => (Vector2)transform.position + Vector2.up * 0.9f;

    void Awake()
    {
        controller = GetComponent<PlayerController>();
        health = GetComponent<PlayerHealth>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (enemyLayer == 0)
        {
            PlayerCombat combat = GetComponent<PlayerCombat>();
            enemyLayer = combat != null && combat.enemyLayer != 0 ? combat.enemyLayer : LayerMask.GetMask("Enemy");
        }
    }

    void OnEnable() { EnemyHealth.Damaged += OnEnemyDamaged; }

    void OnDisable()
    {
        EnemyHealth.Damaged -= OnEnemyDamaged;
        if (IsActive) EndUltimate();
    }

    void Start()
    {
        RefreshUI();
    }

    void Update()
    {
        if (IsActive || !IsReady || health.IsDead || !controller.canControl || Keyboard.current == null) return;
        if (Keyboard.current.kKey.wasPressedThisFrame) StartCoroutine(UltimateRoutine());
    }

    void OnEnemyDamaged(EnemyHealth enemy, bool killed)
    {
        if (IsActive) return; // the ultimate doesn't refill itself
        bool wasReady = IsReady;
        Charge = Mathf.Min(Charge + (chargePerHit + (killed ? chargePerKill : 0f)) * PlayerProgression.UltimateChargeMultiplier, 1f);
        RefreshUI();
        if (IsReady && !wasReady)
        {
            Fx.Shockwave(Chest, energyColor, 2f, 0.3f);
            if (UIManager.Instance != null) UIManager.Instance.ShowMessage("ULTIMATE READY - PRESS K", 2f);
        }
    }

    IEnumerator UltimateRoutine()
    {
        IsActive = true;
        Charge = 0f;
        RefreshUI();

        controller.canControl = false;
        controller.frozen = true;
        controller.frozenVelocity = Vector2.up * 2f; // small lift into the air
        health.invulnerable = true;
        if (chargeSound != null) chargeSound.Play();
        if (UIManager.Instance != null) UIManager.Instance.ShowBanner("SYSTEM PURGE", 1.5f);

        // ---- Charge: energy is sucked in, the Robot flickers and glows brighter ----
        float timer = 0f, nextParticle = 0f;
        while (timer < chargeTime)
        {
            timer += Time.deltaTime;
            float t = timer / chargeTime;
            controller.frozenVelocity = Vector2.up * Mathf.Lerp(2f, 0f, t * 3f);

            if (timer >= nextParticle)
            {
                nextParticle = timer + 0.02f;
                Vector2 from = Chest + Random.insideUnitCircle.normalized * Random.Range(2f, 3.5f);
                Fx.Spawn(Fx.Pixel, from, (Chest - from) / 0.25f, 0.25f, 1.2f, 0.3f,
                    Color.Lerp(Color.white, energyColor, Random.value), new Color(energyColor.r, energyColor.g, energyColor.b, 0f));
            }
            if (Random.value < 0.15f) Fx.Shockwave(Chest, energyColor, 3f * (1f - t) + 0.5f, 0.15f); // inward "pulses"

            spriteRenderer.color = Mathf.FloorToInt(timer * (12f + 30f * t)) % 2 == 0 ? Color.white : energyColor;
            CameraFollow.Shake(0.05f + 0.12f * t, 0.1f);
            yield return null;
        }

        // ---- Blast ----
        spriteRenderer.color = Color.white;
        controller.frozenVelocity = Vector2.zero;
        if (animator != null) animator.SetTrigger("Attack");
        if (blastSound != null) blastSound.Play();

        Vector2 center = Chest;
        Fx.Explosion(center, energyColor, 1.6f);
        Fx.Shockwave(center, Color.white, radius * 2f, waveTime);
        Fx.Shockwave(center, energyColor, radius * 1.6f, waveTime * 1.3f);
        Fx.Shockwave(center, energyColor, radius * 1.1f, waveTime * 1.6f);
        CameraFollow.Shake(0.55f, 0.45f);
        if (UIManager.Instance != null) UIManager.Instance.Flash(new Color(0.75f, 1f, 1f, 0.7f), 0.45f);

        ClearProjectiles(center, radius * 1.5f);

        // The wave travels outward, so nearby enemies are hit first
        var targets = new List<KeyValuePair<EnemyHealth, float>>();
        foreach (Collider2D hit in Physics2D.OverlapCircleAll(center, radius, enemyLayer))
        {
            EnemyHealth enemy = hit.GetComponentInParent<EnemyHealth>();
            if (enemy == null || enemy.IsDead || targets.Exists(p => p.Key == enemy)) continue;
            targets.Add(new KeyValuePair<EnemyHealth, float>(enemy, Vector2.Distance(center, hit.ClosestPoint(center))));
        }
        targets.Sort((a, b) => a.Value.CompareTo(b.Value));

        timer = 0f;
        int next = 0;
        while (next < targets.Count || timer < recoveryTime)
        {
            timer += Time.deltaTime;
            float waveRadius = radius * Mathf.Clamp01(timer / waveTime);
            while (next < targets.Count && targets[next].Value <= waveRadius)
            {
                HitEnemy(targets[next].Key, center);
                next++;
            }
            yield return null;
        }

        EndUltimate();
    }

    void HitEnemy(EnemyHealth enemy, Vector2 center)
    {
        if (enemy == null || enemy.IsDead) return;
        Vector2 enemyPos = enemy.transform.position;
        float side = enemyPos.x >= center.x ? 1f : -1f;
        enemy.TakeDamage(PlayerProgression.PlayerDamage(damage), new Vector2(side * knockbackForce, knockbackUpForce), true);
        Fx.Sparks(enemyPos + Vector2.up * 0.5f, energyColor, 10, 7f);
    }

    // Enemy shots (drone bolts and boss projectiles) inside the blast vanish
    void ClearProjectiles(Vector2 center, float range)
    {
        foreach (EnemyProjectile projectile in new List<EnemyProjectile>(EnemyProjectile.Active))
        {
            if (projectile != null && Vector2.Distance(center, projectile.transform.position) <= range) projectile.Pop();
        }

        foreach (GameObject hazard in GameObject.FindGameObjectsWithTag("Hazard"))
        {
            // Only moving hazards are projectiles; floor tiles and arena hazards are static
            Rigidbody2D body = hazard.GetComponent<Rigidbody2D>();
            if (body == null || body.bodyType == RigidbodyType2D.Static) continue;
            if (Vector2.Distance(center, body.position) > range) continue;
            Fx.Sparks(body.position, new Color(1f, 0.3f, 0.6f), 6, 4f);
            Destroy(hazard);
        }
    }

    void EndUltimate()
    {
        IsActive = false;
        controller.frozen = false;
        health.invulnerable = false;
        if (!health.IsDead) controller.canControl = true;
        if (spriteRenderer != null) spriteRenderer.color = Color.white;
    }

    void RefreshUI()
    {
        if (UIManager.Instance != null) UIManager.Instance.UpdateUltimate(Charge, IsReady);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.45f, 0.95f, 1f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, radius);
    }
}
