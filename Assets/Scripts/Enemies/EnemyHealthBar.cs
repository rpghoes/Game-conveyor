using UnityEngine;

// Small segmented health bar above a normal enemy (one cell per hit = hitValue HP, the last
// cell can be partly filled). Created by EnemyHealth on the first hit, shows for a few
// seconds after each hit, then fades.
// Bosses don't use it - they have the big bar at the bottom of the screen.
public class EnemyHealthBar : MonoBehaviour
{
    const float CellWidth = 0.32f, CellHeight = 0.12f, Gap = 0.05f, Border = 0.04f;
    const int SortingOrder = 35;

    public float showTime = 2.5f;
    public float fadeTime = 0.4f;
    public Color fullColor = new Color(0.4f, 1f, 0.45f);
    public Color lowColor = new Color(1f, 0.3f, 0.25f);
    public Color emptyColor = new Color(0.15f, 0.12f, 0.18f);
    public Color lostColor = Color.white;

    private EnemyHealth owner;
    private SpriteRenderer ownerSprite;
    private SpriteRenderer background;
    private SpriteRenderer[] cells;     // empty cell backgrounds
    private SpriteRenderer[] fills;     // filled part of each cell
    private float[] cellX;
    private float visibleUntil = -1f;
    private float lostFlashUntil;
    private int lostCell = -1;
    private int shownHealth;
    private int hitValue = 1;

    public static EnemyHealthBar Create(EnemyHealth owner)
    {
        var go = new GameObject("HealthBar");
        var bar = go.AddComponent<EnemyHealthBar>();
        bar.owner = owner;
        bar.ownerSprite = owner.GetComponent<SpriteRenderer>();
        bar.Build(owner.MaxHealth);
        return bar;
    }

    void Build(int maxHealth)
    {
        hitValue = PlayerProgression.HitValue;
        int count = Mathf.Max(1, Mathf.CeilToInt((float)maxHealth / hitValue));
        float width = count * CellWidth + (count - 1) * Gap;
        background = NewQuad("Background", Vector2.zero, new Vector2(width + Border * 2f, CellHeight + Border * 2f),
            new Color(0f, 0f, 0f, 0.85f), SortingOrder);
        cells = new SpriteRenderer[count];
        fills = new SpriteRenderer[count];
        cellX = new float[count];
        for (int i = 0; i < count; i++)
        {
            cellX[i] = -width * 0.5f + CellWidth * 0.5f + i * (CellWidth + Gap);
            cells[i] = NewQuad("Cell" + i, new Vector2(cellX[i], 0f), new Vector2(CellWidth, CellHeight), emptyColor, SortingOrder + 1);
            fills[i] = NewQuad("Fill" + i, new Vector2(cellX[i], 0f), new Vector2(CellWidth, CellHeight), fullColor, SortingOrder + 2);
        }
        shownHealth = maxHealth;
        LayoutFills();
        SetAlpha(0f);
    }

    SpriteRenderer NewQuad(string name, Vector2 localPosition, Vector2 size, Color color, int sortingOrder)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPosition;
        // Fx.Pixel is 0.125 units wide
        go.transform.localScale = new Vector3(size.x / 0.125f, size.y / 0.125f, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Fx.Pixel;
        sr.color = color;
        sr.sortingOrder = sortingOrder;
        return sr;
    }

    // Called by EnemyHealth after every hit
    public void Refresh(int current)
    {
        if (current < shownHealth)
        {
            lostFlashUntil = Time.time + 0.15f;
            lostCell = Mathf.Clamp(Mathf.CeilToInt((float)current / hitValue), 0, cells.Length - 1);
        }
        shownHealth = current;
        visibleUntil = Time.time + showTime;
        LayoutFills();
    }

    // Each fill is as wide as the HP left in its cell, pinned to the cell's left edge
    void LayoutFills()
    {
        for (int i = 0; i < fills.Length; i++)
        {
            float part = Mathf.Clamp01((shownHealth - i * hitValue) / (float)hitValue);
            Transform t = fills[i].transform;
            t.localScale = new Vector3(CellWidth * part / 0.125f, CellHeight / 0.125f, 1f);
            t.localPosition = new Vector3(cellX[i] - CellWidth * 0.5f * (1f - part), 0f, 0f);
            fills[i].enabled = part > 0f;
        }
    }

    void LateUpdate()
    {
        if (owner == null)
        {
            Destroy(gameObject);
            return;
        }

        // Follow the top of the enemy (works for flipped and moving sprites)
        float top = ownerSprite != null ? ownerSprite.bounds.max.y : owner.transform.position.y + 1f;
        float x = ownerSprite != null ? ownerSprite.bounds.center.x : owner.transform.position.x;
        transform.position = new Vector3(x, top + 0.3f, 0f);

        if (owner.IsDead || !owner.gameObject.activeInHierarchy)
        {
            Destroy(gameObject);
            return;
        }

        float timeLeft = visibleUntil - Time.time;
        SetAlpha(Mathf.Clamp01(timeLeft / fadeTime));
    }

    void SetAlpha(float alpha)
    {
        background.color = new Color(0f, 0f, 0f, 0.85f * alpha);
        bool flash = Time.time < lostFlashUntil;
        // Last hit left: the bar turns red
        Color filled = shownHealth <= hitValue ? lowColor : fullColor;
        for (int i = 0; i < cells.Length; i++)
        {
            Color empty = flash && i == lostCell ? lostColor : emptyColor;
            cells[i].color = new Color(empty.r, empty.g, empty.b, alpha);
            fills[i].color = new Color(filled.r, filled.g, filled.b, alpha);
        }
    }
}
