using UnityEngine;

// Makes a sprite flicker like an unstable light or a fire (furnace glow, broken lamps, warning lights).
// Brightness wanders smoothly between minBrightness and 1; "dropouts" make it blink off for a moment.
[RequireComponent(typeof(SpriteRenderer))]
public class FlickerLight : MonoBehaviour
{
    public Color baseColor = Color.white;
    [Range(0f, 1f)] public float minBrightness = 0.7f;
    [Tooltip("How fast the brightness wanders")]
    public float speed = 6f;
    [Tooltip("Chance per second of a short blackout (broken lamps)")]
    public float dropoutChance = 0f;

    private SpriteRenderer sr;
    private float seed;
    private float dropoutUntil;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        seed = Random.value * 100f;
    }

    void Update()
    {
        if (dropoutChance > 0f && Random.value < dropoutChance * Time.deltaTime) dropoutUntil = Time.time + Random.Range(0.05f, 0.25f);
        float brightness = Time.time < dropoutUntil ? 0.15f : Mathf.Lerp(minBrightness, 1f, Mathf.PerlinNoise(seed, Time.time * speed));
        sr.color = new Color(baseColor.r * brightness, baseColor.g * brightness, baseColor.b * brightness, baseColor.a);
    }
}
