using UnityEngine;

// Visual effects made from code, so they need no prefabs or extra art:
// sparks, explosions, shockwave rings, shattering a sprite into pieces, and enemy projectiles.
public static class Fx
{
    public const int SortingOrder = 30;

    private static Sprite pixel, circle, ring, crescent;

    // 4x4 white square (sparks, shards)
    public static Sprite Pixel => pixel != null ? pixel : (pixel = MakeSprite(4, 32, FilterMode.Point, d => 1f));
    // Soft white dot, 1 world unit wide (flashes, smoke, projectiles)
    public static Sprite Circle => circle != null ? circle : (circle = MakeSprite(32, 32, FilterMode.Bilinear, d => Mathf.Clamp01((1f - d) * 3f)));
    // Thin white ring, 1 world unit wide (shockwaves)
    public static Sprite Ring => ring != null ? ring : (ring = MakeSprite(128, 128, FilterMode.Bilinear, d => Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) * 14f)));
    // Right half of a thick ring that thins out at the tips, 1 world unit wide (slash arcs)
    public static Sprite Crescent => crescent != null ? crescent : (crescent = MakeCrescent(64));

    static Sprite MakeCrescent(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f - half, y + 0.5f - half) / half;
                float d = p.magnitude;
                // 1 in the middle of the arc, 0 at the top and bottom tips
                float thickness = 0.16f * Mathf.Clamp01(p.x * 1.3f);
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.82f) / Mathf.Max(thickness, 0.001f));
                pixels[y * size + x] = new Color(1f, 1f, 1f, p.x > 0f ? Mathf.Round(a * 4f) / 4f : 0f);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return sprite;
    }

    static Sprite MakeSprite(int size, float pixelsPerUnit, FilterMode filter, System.Func<float, float> alphaAtDistance)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = filter, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 0 at the centre, 1 at the edge
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
                pixels[y * size + x] = new Color(1f, 1f, 1f, alphaAtDistance(d));
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), pixelsPerUnit);
        sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return sprite;
    }

    public static FxParticle Spawn(Sprite sprite, Vector2 position, Vector2 velocity, float lifetime,
        float startScale, float endScale, Color startColor, Color endColor, float gravity = 0f, float drag = 0f, int sortingOffset = 0)
    {
        var go = new GameObject("Fx");
        go.transform.position = position;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = SortingOrder + sortingOffset;
        sr.color = startColor;
        go.transform.localScale = Vector3.one * startScale;

        var p = go.AddComponent<FxParticle>();
        p.velocity = velocity;
        p.lifetime = lifetime;
        p.startScale = startScale;
        p.endScale = endScale;
        p.startColor = startColor;
        p.endColor = endColor;
        p.gravity = gravity;
        p.drag = drag;
        return p;
    }

    static Color Clear(Color c) { return new Color(c.r, c.g, c.b, 0f); }

    // Small burst of square sparks
    public static void Sparks(Vector2 position, Color color, int count, float speed)
    {
        for (int i = 0; i < count; i++)
        {
            Vector2 dir = Random.insideUnitCircle.normalized;
            Color start = Color.Lerp(Color.white, color, Random.Range(0.3f, 1f));
            FxParticle p = Spawn(Pixel, position, dir * speed * Random.Range(0.5f, 1.2f), Random.Range(0.25f, 0.5f),
                Random.Range(0.7f, 1.3f), 0.2f, start, Clear(color), 10f, 2f, 2);
            p.spin = Random.Range(-360f, 360f);
        }
    }

    // Expanding ring. diameter = final width in world units
    public static void Shockwave(Vector2 position, Color color, float diameter, float lifetime)
    {
        Spawn(Ring, position, Vector2.zero, lifetime, 0.3f, diameter, color, Clear(color), 0f, 0f, 1);
    }

    // Big flash + ring + sparks + smoke. size 1 = a normal enemy
    public static void Explosion(Vector2 position, Color color, float size)
    {
        Spawn(Circle, position, Vector2.zero, 0.18f, 0.6f * size, 3f * size, Color.white, Clear(color), 0f, 0f, 3);
        Spawn(Circle, position, Vector2.zero, 0.3f, 1f * size, 2.2f * size, color, Clear(color), 0f, 0f, 2);
        Shockwave(position, color, 4f * size, 0.35f);

        int sparkCount = Mathf.RoundToInt(22 * size);
        for (int i = 0; i < sparkCount; i++)
        {
            Vector2 dir = Random.insideUnitCircle.normalized;
            Color start = Color.Lerp(Color.white, color, Random.Range(0.2f, 1f));
            FxParticle p = Spawn(Pixel, position, dir * Random.Range(4f, 11f) * Mathf.Sqrt(size), Random.Range(0.35f, 0.8f),
                Random.Range(0.8f, 1.6f), 0.2f, start, Clear(color), 14f, 2.5f, 2);
            p.spin = Random.Range(-720f, 720f);
        }

        Color smoke = new Color(0.15f, 0.13f, 0.18f, 0.7f);
        for (int i = 0; i < Mathf.RoundToInt(6 * size); i++)
        {
            Vector2 offset = Random.insideUnitCircle * 0.4f * size;
            Spawn(Circle, position + offset, new Vector2(Random.Range(-0.6f, 0.6f), Random.Range(0.6f, 1.6f)),
                Random.Range(0.6f, 1.1f), 0.4f * size, 1.4f * size, smoke, Clear(smoke), -0.5f, 1f, -1);
        }
    }

    // Cuts the sprite into pieces x pieces shards that fly apart
    public static void Shatter(SpriteRenderer source, int pieces, Vector2 push)
    {
        if (source == null || source.sprite == null) return;
        Sprite sprite = source.sprite;
        Rect rect = sprite.rect;
        float ppu = sprite.pixelsPerUnit;
        Vector3 scale = source.transform.lossyScale;
        Vector2 center = source.bounds.center;
        float w = rect.width / pieces, h = rect.height / pieces;

        for (int ix = 0; ix < pieces; ix++)
        {
            for (int iy = 0; iy < pieces; iy++)
            {
                var chunkRect = new Rect(rect.x + ix * w, rect.y + iy * h, w, h);
                Sprite chunk = Sprite.Create(sprite.texture, chunkRect, new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);

                // Chunk centre relative to the sprite pivot, then into world space (handles flipping)
                Vector2 local = new Vector2((ix + 0.5f) * w - sprite.pivot.x, (iy + 0.5f) * h - sprite.pivot.y) / ppu;
                Vector2 world = source.transform.TransformPoint(local);

                Vector2 away = (world - center).normalized;
                Vector2 velocity = away * Random.Range(3f, 7f) + push + Vector2.up * Random.Range(3f, 6f);
                FxParticle p = Spawn(chunk, world, velocity, Random.Range(0.6f, 0.95f), 1f, 0.6f,
                    source.color, Clear(source.color), 22f, 0.5f, -SortingOrder + source.sortingOrder);
                p.baseScale = new Vector3(scale.x, scale.y, 1f);
                p.spin = Random.Range(-540f, 540f);
                p.ownsSprite = true;
                // Fade only near the end
                p.endColor = new Color(source.color.r, source.color.g, source.color.b, -1.5f);
            }
        }
    }

    // Glowing shot that hurts the Robot. size.x = length along the flight direction
    // damage in hits; damageMultiplier = the shooter's level scaling
    public static EnemyProjectile SpawnProjectile(Vector2 position, Vector2 velocity, Color color, Vector2 size, int damage, float lifetime = 4f, float damageMultiplier = 1f)
    {
        var go = new GameObject("EnemyProjectile");
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Circle;
        sr.color = Color.Lerp(color, Color.white, 0.35f);
        sr.sortingOrder = SortingOrder;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.linearVelocity = velocity;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.3f;

        var projectile = go.AddComponent<EnemyProjectile>();
        projectile.damage = damage;
        projectile.damageMultiplier = damageMultiplier;
        projectile.color = color;
        projectile.lifetime = lifetime;
        return projectile;
    }

    // Thin line (telegraph for laser attacks). Returns the renderer so the caller can blink/destroy it.
    public static SpriteRenderer Line(Vector2 from, Vector2 to, Color color, float width)
    {
        var go = new GameObject("FxLine");
        Vector2 delta = to - from;
        go.transform.position = (from + to) * 0.5f;
        go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        // The pixel sprite is 0.125 units wide
        go.transform.localScale = new Vector3(delta.magnitude / 0.125f, width / 0.125f, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Pixel;
        sr.color = color;
        sr.sortingOrder = SortingOrder - 1;
        return sr;
    }
}
