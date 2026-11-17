using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// An industrial press hanging from the ceiling. One slam:
//   WARNING - a shadow grows on the floor under the press and a red lamp blinks (~1 s)
//   SLAM    - the head drops fast; anyone under it takes damage (Guards too - the press does not care)
//   HOLD    - the head stays down for a moment (dust, sparks, screen shake)
//   RISE    - the head goes back up slowly
// Either it slams on its own every few seconds (autoCycle, environment hazard)
// or a boss calls Slam() / SlamAt().
public class IndustrialPress : MonoBehaviour
{
    public static readonly List<IndustrialPress> All = new List<IndustrialPress>();

    [Header("Size (world units)")]
    [Tooltip("Width of the press head")]
    public float width = 3f;
    public float headHeight = 1.5f;
    [Tooltip("World Y of the ceiling the press hangs from")]
    public float ceilingY = 18f;
    [Tooltip("World Y of the floor surface it slams onto (found automatically if Ground is below)")]
    public float floorY = 2f;
    [Tooltip("How far below the ceiling the head waits while raised")]
    public float restDrop = 1.5f;

    [Header("Timing")]
    public float warningTime = 1f;
    public float slamSpeed = 30f;
    public float holdTime = 0.6f;
    public float riseSpeed = 4f;
    [Tooltip("Slam by itself every cycleInterval seconds")]
    public bool autoCycle = false;
    public float cycleInterval = 3.5f;
    public float cycleOffset = 0f;

    [Header("Damage")]
    public int damage = 1;
    public bool hurtsEnemies = true;
    public LayerMask groundLayer;

    [Header("Art")]
    public SpriteRenderer head;
    public SpriteRenderer rod;
    public SpriteRenderer shadow;
    public SpriteRenderer lamp;
    public Color shadowColor = new Color(0f, 0f, 0f, 0.55f);
    public Color lampOffColor = new Color(0.35f, 0.1f, 0.1f);
    public Color lampOnColor = new Color(1f, 0.2f, 0.15f);

    [Header("Audio")]
    public AudioSource warningSound;
    public AudioSource impactSound;

    public bool IsBusy { get; private set; }
    // True from the start of the warning until the head is back up (bosses wait for this)
    public bool IsDown { get; private set; }

    private float headY;          // current world Y of the bottom of the head
    private float restY;          // bottom of the head while raised
    private Transform player;
    private PlayerHealth playerHealth;
    private float speedMultiplier = 1f;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Start()
    {
        if (groundLayer == 0) groundLayer = LayerMask.GetMask("Ground");
        // Find the floor below
        RaycastHit2D hit = Physics2D.Raycast(new Vector2(transform.position.x, ceilingY - restDrop - headHeight - 0.1f), Vector2.down, 40f, groundLayer);
        if (hit.collider != null) floorY = hit.point.y;

        restY = ceilingY - restDrop - headHeight;
        headY = restY;
        SetShadow(0f);
        SetLamp(false);
        UpdateVisuals();

        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerHealth = p.GetComponent<PlayerHealth>();
        }
        if (autoCycle) StartCoroutine(AutoCycle());
    }

    // Phase 3 of the Director makes every press faster (shorter warning, never below 0.55 s)
    public static void SetGlobalSpeed(float multiplier)
    {
        foreach (IndustrialPress press in All) press.speedMultiplier = multiplier;
    }

    float Warning => Mathf.Max(warningTime / speedMultiplier, 0.55f);

    IEnumerator AutoCycle()
    {
        yield return new WaitForSeconds(cycleOffset);
        while (true)
        {
            yield return SlamRoutine();
            yield return new WaitForSeconds(cycleInterval / speedMultiplier);
        }
    }

    // Slam where the press stands. Returns false if it is already busy.
    public bool Slam()
    {
        if (IsBusy) return false;
        StartCoroutine(SlamRoutine());
        return true;
    }

    // Move over the given X (instantly, while raised and out of sight) and slam - used by bosses
    public bool SlamAt(float x)
    {
        if (IsBusy) return false;
        transform.position = new Vector3(x, transform.position.y, transform.position.z);
        return Slam();
    }

    public IEnumerator SlamRoutine()
    {
        IsBusy = true;
        IsDown = true;

        // WARNING: the shadow grows, the lamp blinks
        if (warningSound != null) warningSound.Play();
        float warning = Warning;
        float timer = 0f;
        while (timer < warning)
        {
            timer += Time.deltaTime;
            float t = timer / warning;
            SetShadow(Mathf.Lerp(0.25f, 1f, t));
            SetLamp(Mathf.FloorToInt(timer * (6f + 10f * t)) % 2 == 0);
            // the head twitches just before falling
            float twitch = t > 0.75f ? Mathf.Sin(timer * 80f) * 0.05f : 0f;
            headY = restY + twitch;
            UpdateVisuals();
            yield return null;
        }

        // SLAM
        SetLamp(true);
        bool hitSomething = false;
        while (headY > floorY)
        {
            headY = Mathf.Max(headY - slamSpeed * Time.deltaTime, floorY);
            UpdateVisuals();
            if (!hitSomething) hitSomething = CrushCheck();
            yield return null;
        }
        if (!hitSomething) CrushCheck();
        Impact();

        // HOLD
        yield return new WaitForSeconds(holdTime);
        SetShadow(0f);
        SetLamp(false);

        // RISE
        while (headY < restY)
        {
            headY = Mathf.Min(headY + riseSpeed * speedMultiplier * Time.deltaTime, restY);
            UpdateVisuals();
            yield return null;
        }
        IsDown = false;
        IsBusy = false;
    }

    // Damage everything inside the head's area. Returns true if something was hit.
    bool CrushCheck()
    {
        bool hit = false;
        Rect area = HeadRect;
        if (playerHealth != null && !playerHealth.IsDead)
        {
            Vector2 chest = (Vector2)player.position + Vector2.up * 0.8f;
            if (chest.x > area.xMin + 0.15f && chest.x < area.xMax - 0.15f && chest.y < area.yMax && chest.y > area.yMin - 0.9f)
            {
                playerHealth.TakeDamage(damage, new Vector2(transform.position.x, chest.y + 1f));
                hit = true;
            }
        }
        if (hurtsEnemies)
        {
            Collider2D[] cols = Physics2D.OverlapBoxAll(area.center, area.size, 0f, LayerMask.GetMask("Enemy"));
            foreach (Collider2D col in cols)
            {
                EnemyHealth enemy = col.GetComponentInParent<EnemyHealth>();
                if (enemy == null || enemy.IsDead || enemy.CompareTag("Boss")) continue;
                enemy.TakeDamage(PlayerProgression.HitValue * 3, Vector2.down * 2f);
                hit = true;
            }
        }
        return hit;
    }

    void Impact()
    {
        if (impactSound != null) impactSound.Play();
        CameraFollow.Shake(0.22f, 0.25f);
        Vector2 left = new Vector2(transform.position.x - width * 0.5f, floorY + 0.1f);
        Vector2 right = new Vector2(transform.position.x + width * 0.5f, floorY + 0.1f);
        Fx.Sparks(left, new Color(1f, 0.8f, 0.4f), 8, 5f);
        Fx.Sparks(right, new Color(1f, 0.8f, 0.4f), 8, 5f);
        for (int i = 0; i < 6; i++)
        {
            Vector2 at = new Vector2(Random.Range(left.x - 0.5f, right.x + 0.5f), floorY + 0.2f);
            Fx.Spawn(Fx.Circle, at, new Vector2(Random.Range(-2f, 2f), Random.Range(0.5f, 1.5f)), 0.7f, 0.6f, 1.6f,
                new Color(0.55f, 0.5f, 0.45f, 0.5f), new Color(0.55f, 0.5f, 0.45f, 0f), 0f, 2f, -5);
        }
    }

    Rect HeadRect => new Rect(transform.position.x - width * 0.5f, headY, width, headHeight);

    void UpdateVisuals()
    {
        float x = transform.position.x;
        if (head != null)
        {
            head.transform.position = new Vector3(x, headY + headHeight * 0.5f, 0f);
            if (head.drawMode != SpriteDrawMode.Simple) head.size = new Vector2(width, headHeight);
        }
        if (rod != null)
        {
            float top = ceilingY;
            float bottom = headY + headHeight;
            rod.transform.position = new Vector3(x, (top + bottom) * 0.5f, 0f);
            rod.size = new Vector2(rod.size.x, Mathf.Max(top - bottom, 0.01f));
        }
        if (lamp != null) lamp.transform.position = new Vector3(x, headY + headHeight + 0.15f, 0f);
    }

    void SetShadow(float strength)
    {
        if (shadow == null) return;
        shadow.enabled = strength > 0f;
        shadow.transform.position = new Vector3(transform.position.x, floorY + 0.06f, 0f);
        // Scale so the shadow is (width x 0.4) world units whatever the sprite size is
        Vector2 spriteSize = shadow.sprite != null ? (Vector2)shadow.sprite.bounds.size : Vector2.one;
        shadow.transform.localScale = new Vector3(width * Mathf.Lerp(0.5f, 1.05f, strength) / spriteSize.x, 0.4f / spriteSize.y, 1f);
        shadow.color = new Color(shadowColor.r, shadowColor.g, shadowColor.b, shadowColor.a * strength);
    }

    void SetLamp(bool on)
    {
        if (lamp != null) lamp.color = on ? lampOnColor : lampOffColor;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.6f);
        float x = transform.position.x;
        Gizmos.DrawWireCube(new Vector3(x, (ceilingY + floorY) * 0.5f, 0f), new Vector3(width, ceilingY - floorY, 0f));
    }
}
