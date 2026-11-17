using UnityEngine;

// Homing rocket fired by the Core Sentinel. Flies up first, then slowly turns towards the Robot.
// Damage, lifetime and popping on walls are handled by the EnemyProjectile on the same object.
// The rocket art points LEFT, so the sprite is rotated by an extra 180 degrees.
[RequireComponent(typeof(Rigidbody2D))]
public class SentinelRocket : MonoBehaviour
{
    public Sprite[] frames;
    public float frameRate = 12f;
    public Transform target;
    public float speed = 7f;
    [Tooltip("Degrees per second")]
    public float turnRate = 140f;
    [Tooltip("Flies straight for this long before homing")]
    public float launchTime = 0.45f;
    [Tooltip("Stops homing after this long so it can be dodged")]
    public float homingTime = 2.2f;
    public Color color = Color.red;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private float age;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    void FixedUpdate()
    {
        age += Time.fixedDeltaTime;
        Vector2 velocity = rb.linearVelocity;

        if (target != null && age > launchTime && age < launchTime + homingTime)
        {
            Vector2 toTarget = (Vector2)target.position + Vector2.up * 0.8f - rb.position;
            float current = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
            float wanted = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
            float angle = Mathf.MoveTowardsAngle(current, wanted, turnRate * Time.fixedDeltaTime) * Mathf.Deg2Rad;
            velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
            rb.linearVelocity = velocity;
        }

        rb.MoveRotation(Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg + 180f);
    }

    void Update()
    {
        if (frames != null && frames.Length > 0)
        {
            spriteRenderer.sprite = frames[Mathf.FloorToInt(age * frameRate) % frames.Length];
        }
    }
}
