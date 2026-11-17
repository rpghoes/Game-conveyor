using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Melee attack. Press J to swing: the AttackHitbox child object is switched on for part
// of the attack animation, and every enemy inside it is damaged and knocked back once.
[RequireComponent(typeof(PlayerController))]
public class PlayerCombat : MonoBehaviour
{
    [Header("Hitbox")]
    [Tooltip("Child object in front of the Robot with a BoxCollider2D (trigger). It flips with the Robot.")]
    public BoxCollider2D attackHitbox;
    public LayerMask enemyLayer;

    [Header("Damage")]
    [Tooltip("Damage in hits (x hitValue x the level bonus, see ProgressionSettings)")]
    public int damage = 1;
    public float knockbackForce = 8f;
    public float knockbackUpForce = 4f;

    [Header("Timing (seconds)")]
    [Tooltip("Total attack length. The Robot_Attack clip is 8 frames at 16 fps = 0.5 s")]
    public float attackDuration = 0.5f;
    [Tooltip("When the hitbox turns on (the arm starts reaching out)")]
    public float hitboxStart = 0.15f;
    [Tooltip("When the hitbox turns off")]
    public float hitboxEnd = 0.4f;
    [Tooltip("Extra wait after an attack before the next one (prevents spam)")]
    public float attackCooldown = 0.1f;

    [Header("Audio")]
    public AudioSource attackSound;

    public bool IsAttacking { get; private set; }

    private PlayerController controller;
    private PlayerBuffs buffs;
    private Animator animator;
    private float nextAttackTime;
    private readonly List<EnemyHealth> hitThisAttack = new List<EnemyHealth>();

    void Awake()
    {
        controller = GetComponent<PlayerController>();
        buffs = GetComponent<PlayerBuffs>();
        animator = GetComponent<Animator>();
        if (attackHitbox != null) attackHitbox.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!controller.canControl) return;

        if (GameInput.AttackPressed && !IsAttacking && Time.time >= nextAttackTime)
        {
            StartCoroutine(AttackRoutine());
        }
    }

    IEnumerator AttackRoutine()
    {
        IsAttacking = true;
        hitThisAttack.Clear();
        if (animator != null) animator.SetTrigger("Attack");
        if (attackSound != null) attackSound.Play();

        float timer = 0f;
        while (timer < attackDuration)
        {
            // The hitbox is only active during the middle of the swing
            bool hitboxActive = timer >= hitboxStart && timer <= hitboxEnd && controller.canControl;
            attackHitbox.gameObject.SetActive(hitboxActive);
            if (hitboxActive) DamageEnemiesInHitbox();

            yield return new WaitForFixedUpdate();
            timer += Time.fixedDeltaTime;
        }

        attackHitbox.gameObject.SetActive(false);
        IsAttacking = false;
        nextAttackTime = Time.time + attackCooldown;
    }

    void DamageEnemiesInHitbox()
    {
        // Work out the hitbox rectangle in world space
        Vector2 center = attackHitbox.transform.TransformPoint(attackHitbox.offset);
        Vector3 scale = attackHitbox.transform.lossyScale;
        Vector2 size = new Vector2(attackHitbox.size.x * Mathf.Abs(scale.x), attackHitbox.size.y * Mathf.Abs(scale.y));

        Collider2D[] hits = Physics2D.OverlapBoxAll(center, size, 0f, enemyLayer);
        foreach (Collider2D hit in hits)
        {
            EnemyHealth enemy = hit.GetComponentInParent<EnemyHealth>();
            if (enemy == null || enemy.IsDead || hitThisAttack.Contains(enemy)) continue;

            hitThisAttack.Add(enemy); // each enemy can only be hit once per swing
            Vector2 knockback = new Vector2(controller.FacingDirection * knockbackForce, knockbackUpForce);
            float multiplier = PlayerUpgrades.DamageMultiplier * (buffs != null ? buffs.DamageMultiplier : 1f);
            int dealt = Mathf.Max(1, Mathf.RoundToInt(PlayerProgression.PlayerDamage(damage) * multiplier));
            if (enemy.TakeDamage(dealt, knockback))
            {
                // The press arm lands: a tiny freeze and shake sell the weight of the hit
                CameraFollow.Shake(0.08f, 0.1f);
                HitStop.Freeze(0.045f);
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        if (attackHitbox == null) return;
        Gizmos.color = Color.red;
        Vector2 center = attackHitbox.transform.TransformPoint(attackHitbox.offset);
        Vector3 scale = attackHitbox.transform.lossyScale;
        Gizmos.DrawWireCube(center, new Vector3(attackHitbox.size.x * Mathf.Abs(scale.x), attackHitbox.size.y * Mathf.Abs(scale.y), 0f));
    }
}
