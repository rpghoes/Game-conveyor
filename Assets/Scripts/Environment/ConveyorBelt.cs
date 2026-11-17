using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// A conveyor belt: a solid strip of floor (Ground layer) that carries whatever stands on it.
// The Robot and the Guards ask ConveyorBelt.SpeedUnder(...) and add the belt speed to their own movement.
// Bosses can reverse or stop belts (Reverse / SetSpeed); a reversal is announced by flashing arrows first.
// Visuals: the base sprite (tiled) plus small arrow marks that slide along the belt at its speed.
[RequireComponent(typeof(BoxCollider2D))]
public class ConveyorBelt : MonoBehaviour
{
    public static readonly List<ConveyorBelt> All = new List<ConveyorBelt>();

    [Tooltip("World units per second. Positive = carries to the right")]
    public float speed = 2f;
    [Tooltip("Length of the belt in tiles (set by the level builder)")]
    public float length = 6f;
    [Tooltip("Seconds the arrows flash before a reversal takes effect")]
    public float reverseWarning = 0.7f;

    [Header("Art")]
    public SpriteRenderer beltRenderer;
    public Color arrowColor = new Color(1f, 0.75f, 0.25f, 0.85f);
    public Color warningColor = new Color(1f, 0.25f, 0.2f, 1f);
    [Tooltip("Arrow marks per tile")]
    public float arrowSpacing = 1.5f;

    [Header("Audio")]
    public AudioSource humSound;
    public AudioSource reverseSound;

    public float CurrentSpeed { get; private set; }
    public bool IsChanging { get; private set; }

    private readonly List<SpriteRenderer> arrows = new List<SpriteRenderer>();
    private float arrowOffset;
    private Color currentArrowColor;
    private static Sprite arrowSprite;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Awake()
    {
        CurrentSpeed = speed;
        currentArrowColor = arrowColor;
        BuildArrows();
    }

    // ---------------- Queries ----------------

    // Belt speed under a point (the feet of a character). 0 if no belt is there.
    public static float SpeedUnder(Collider2D groundCollider)
    {
        if (groundCollider == null) return 0f;
        ConveyorBelt belt = groundCollider.GetComponent<ConveyorBelt>();
        return belt != null ? belt.CurrentSpeed : 0f;
    }

    public static float SpeedUnder(Vector2 feet, LayerMask groundLayer)
    {
        Collider2D hit = Physics2D.OverlapBox(feet + Vector2.down * 0.05f, new Vector2(0.4f, 0.12f), 0f, groundLayer);
        return SpeedUnder(hit);
    }

    // ---------------- Control (bosses, events) ----------------

    // Flip the direction after a short warning
    public void Reverse()
    {
        SetSpeed(-CurrentSpeed);
    }

    public void SetSpeed(float newSpeed, bool warn = true)
    {
        StopAllCoroutines();
        StartCoroutine(ChangeRoutine(newSpeed, warn ? reverseWarning : 0f));
    }

    // Reverse every belt in the room (used by the Foreman and the Director)
    public static void ReverseAll()
    {
        foreach (ConveyorBelt belt in All.ToArray()) belt.Reverse();
    }

    IEnumerator ChangeRoutine(float newSpeed, float warning)
    {
        IsChanging = true;
        if (reverseSound != null) reverseSound.Play();
        float timer = 0f;
        while (timer < warning)
        {
            timer += Time.deltaTime;
            currentArrowColor = Mathf.FloorToInt(timer * 12f) % 2 == 0 ? warningColor : arrowColor;
            yield return null;
        }
        currentArrowColor = arrowColor;
        CurrentSpeed = newSpeed;
        speed = newSpeed;
        IsChanging = false;
        if (Mathf.Abs(newSpeed) > 0.01f) Fx.Sparks(transform.position + Vector3.up * 0.5f, arrowColor, 6, 3f);
    }

    // ---------------- Visuals ----------------

    void Update()
    {
        // Arrows slide along the belt and wrap around
        arrowOffset = Mathf.Repeat(arrowOffset + CurrentSpeed * Time.deltaTime, arrowSpacing);
        float left = transform.position.x - length * 0.5f;
        float dir = CurrentSpeed >= 0f ? 1f : -1f;
        for (int i = 0; i < arrows.Count; i++)
        {
            SpriteRenderer arrow = arrows[i];
            float x = Mathf.Repeat(i * arrowSpacing + arrowOffset, arrows.Count * arrowSpacing);
            bool inside = x > 0.2f && x < length - 0.2f;
            arrow.enabled = inside && (Mathf.Abs(CurrentSpeed) > 0.01f || IsChanging);
            arrow.transform.position = new Vector3(left + x, transform.position.y + 0.38f, 0f);
            arrow.transform.localScale = new Vector3(dir * 0.5f, 0.35f, 1f);
            arrow.color = currentArrowColor;
        }

        if (humSound != null)
        {
            bool shouldHum = Mathf.Abs(CurrentSpeed) > 0.01f;
            if (shouldHum && !humSound.isPlaying) humSound.Play();
            else if (!shouldHum && humSound.isPlaying) humSound.Stop();
        }
    }

    void BuildArrows()
    {
        if (arrowSprite == null) arrowSprite = MakeArrowSprite();
        int count = Mathf.Max(1, Mathf.CeilToInt(length / arrowSpacing) + 1);
        int order = beltRenderer != null ? beltRenderer.sortingOrder + 1 : 1;
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Arrow");
            go.transform.SetParent(transform, true);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = arrowSprite;
            sr.sortingOrder = order;
            arrows.Add(sr);
        }
    }

    // Small ">" chevron, 16x16 pixels, 1 world unit wide before scaling
    static Sprite MakeArrowSprite()
    {
        const int size = 16;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int dy = Mathf.Abs(y - size / 2);
                bool on = x >= 3 + dy && x <= 7 + dy && dy <= 6;
                pixels[y * size + x] = on ? Color.white : Color.clear;
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return sprite;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = speed >= 0f ? Color.yellow : new Color(1f, 0.5f, 0f);
        Vector3 c = transform.position + Vector3.up * 0.5f;
        Gizmos.DrawLine(c - Vector3.right * length * 0.5f, c + Vector3.right * length * 0.5f);
        Gizmos.DrawLine(c + Vector3.right * Mathf.Sign(speed) * length * 0.5f, c + Vector3.right * Mathf.Sign(speed) * (length * 0.5f - 0.5f) + Vector3.up * 0.3f);
    }
}
