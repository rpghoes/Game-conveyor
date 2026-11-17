using UnityEngine;

// Floating numbers that pop out of whatever got hit ("-1", "-3", "BLOCKED", "REBOOT").
// They jump up, wobble a little, then fade. Made from code (TextMesh), so no prefab is needed.
public class DamageNumber : MonoBehaviour
{
    public const int SortingOrder = 40;

    // Enemy took damage
    public static readonly Color EnemyHitColor = new Color(1f, 0.95f, 0.75f);
    // Big hits (ultimate, 3+ damage)
    public static readonly Color HeavyHitColor = new Color(1f, 0.8f, 0.2f);
    // The Robot took damage
    public static readonly Color PlayerHitColor = new Color(1f, 0.3f, 0.3f);
    public static readonly Color BlockedColor = new Color(0.6f, 0.65f, 0.75f);
    public static readonly Color HealColor = new Color(0.45f, 0.95f, 1f);
    // XP gained from a kill, LEVEL UP!
    public static readonly Color XpColor = new Color(0.6f, 1f, 0.45f);
    public static readonly Color LevelUpColor = new Color(1f, 0.85f, 0.25f);

    private static Font font;

    private TextMesh text, shadow;
    private Vector2 velocity;
    private float age, lifetime, baseScale;
    private Color color;

    // size 1 = normal hit
    public static void Show(Vector2 position, string label, Color color, float size = 1f, float lifetime = 0.8f)
    {
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var go = new GameObject("DamageNumber");
        go.transform.position = position + new Vector2(Random.Range(-0.25f, 0.25f), 0f);

        var number = go.AddComponent<DamageNumber>();
        // Black copy slightly behind = readable on any background
        number.shadow = NewText(go.transform, "Shadow", label, new Vector3(0.05f, -0.05f, 0f), SortingOrder);
        number.text = NewText(go.transform, "Text", label, Vector3.zero, SortingOrder + 1);
        number.color = color;
        number.lifetime = lifetime;
        number.baseScale = size;
        number.velocity = new Vector2(Random.Range(-0.8f, 0.8f), 4.5f);
        number.Apply(0f);
    }

    static TextMesh NewText(Transform parent, string name, string label, Vector3 offset, int sortingOrder)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = offset;
        var mesh = go.AddComponent<TextMesh>();
        mesh.font = font;
        mesh.text = label;
        mesh.fontSize = 64;
        mesh.characterSize = 0.07f;
        mesh.fontStyle = FontStyle.Bold;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = TextAlignment.Center;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = font.material;
        renderer.sortingOrder = sortingOrder;
        return mesh;
    }

    void Update()
    {
        age += Time.deltaTime;
        if (age >= lifetime)
        {
            Destroy(gameObject);
            return;
        }
        // Jump up and slow down
        velocity.y -= 12f * Time.deltaTime;
        velocity.y = Mathf.Max(velocity.y, 0.3f);
        transform.position += (Vector3)(velocity * Time.deltaTime);
        Apply(age / lifetime);
    }

    void Apply(float t)
    {
        // Pop: big for a moment, then normal size
        float pop = t < 0.15f ? Mathf.Lerp(1.6f, 1f, t / 0.15f) : 1f;
        transform.localScale = Vector3.one * baseScale * pop;
        float alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
        // Flash white on the first frames
        Color c = t < 0.08f ? Color.white : color;
        text.color = new Color(c.r, c.g, c.b, alpha);
        shadow.color = new Color(0f, 0f, 0f, alpha * 0.85f);
    }
}
