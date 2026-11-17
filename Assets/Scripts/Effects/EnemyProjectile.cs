using System.Collections.Generic;
using UnityEngine;

// Shot fired by enemies (drone laser bolts and energy orbs). Hurts the Robot on touch,
// pops when it hits a wall/floor or runs out of time. Created by Fx.SpawnProjectile.
public class EnemyProjectile : MonoBehaviour
{
    // All projectiles in flight (the ultimate wipes them out)
    public static readonly List<EnemyProjectile> Active = new List<EnemyProjectile>();

    [Tooltip("Damage in hits")]
    public int damage = 1;
    [Tooltip("The shooter's damage scaling (EnemyHealth.DamageMultiplier)")]
    public float damageMultiplier = 1f;
    public float lifetime = 4f;
    public Color color = Color.red;
    public float trailInterval = 0.03f;
    [Tooltip("Size of the explosion when it pops (0 = just sparks)")]
    public float popExplosion = 0f;

    private Rigidbody2D rb;
    private LayerMask groundLayer;
    private float age;
    private float trailTimer;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        groundLayer = LayerMask.GetMask("Ground");
    }

    void OnEnable() { Active.Add(this); }
    void OnDisable() { Active.Remove(this); }

    void FixedUpdate()
    {
        age += Time.fixedDeltaTime;
        if (age >= lifetime || Physics2D.OverlapCircle(rb.position, 0.08f, groundLayer)) Pop();
    }

    void Update()
    {
        trailTimer -= Time.deltaTime;
        if (trailTimer > 0f) return;
        trailTimer = trailInterval;
        Fx.Spawn(Fx.Circle, transform.position, Vector2.zero, 0.18f, transform.localScale.y * 0.9f, 0f,
            new Color(color.r, color.g, color.b, 0.6f), new Color(color.r, color.g, color.b, 0f));
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        PlayerHealth player = other.GetComponent<PlayerHealth>();
        if (player == null || player.IsDead) return;
        player.TakeDamage(damage, rb.position, damageMultiplier);
        Pop();
    }

    public void Pop()
    {
        Fx.Sparks(transform.position, color, 6, 4f);
        if (popExplosion > 0f) Fx.Explosion(transform.position, color, popExplosion);
        Destroy(gameObject);
    }
}
