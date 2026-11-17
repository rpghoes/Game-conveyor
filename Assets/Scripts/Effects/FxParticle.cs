using UnityEngine;

// One effect particle (spark, smoke puff, shockwave ring, sprite shard...).
// Moves, shrinks/grows and fades by itself, then destroys itself. Created by Fx.
public class FxParticle : MonoBehaviour
{
    public Vector2 velocity;
    public float gravity;
    public float drag;
    public float lifetime = 0.5f;
    public float spin;
    public float startScale = 1f;
    public float endScale = 0f;
    public Vector3 baseScale = Vector3.one;
    public Color startColor = Color.white;
    public Color endColor = Color.clear;
    [Tooltip("Destroy the sprite with the particle (used by shards cut from an enemy sprite)")]
    public bool ownsSprite;

    private SpriteRenderer spriteRenderer;
    private float age;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    void Update()
    {
        age += Time.deltaTime;
        float t = age / lifetime;
        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        velocity.y -= gravity * Time.deltaTime;
        velocity /= 1f + drag * Time.deltaTime;
        transform.position += (Vector3)(velocity * Time.deltaTime);
        if (spin != 0f) transform.Rotate(0f, 0f, spin * Time.deltaTime);

        transform.localScale = baseScale * Mathf.Lerp(startScale, endScale, t);
        spriteRenderer.color = Color.Lerp(startColor, endColor, t);
    }

    void OnDestroy()
    {
        if (ownsSprite && spriteRenderer != null && spriteRenderer.sprite != null) Destroy(spriteRenderer.sprite);
    }
}
