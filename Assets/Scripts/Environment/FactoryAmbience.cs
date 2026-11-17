using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The atmosphere of a factory section, built from code when the section starts (no art needed,
// every piece is a tinted square or soft dot, so it is easy to swap for real sprites later):
//  - two parallax layers of machinery silhouettes behind the level (girders, tanks, chimneys, pipes, chains)
//  - ceiling lamps. Without power most of them are broken: dark, flickering, sparking.
//    Every activated power relay brings more of them back, lit in a wave starting at the Robot.
//  - drifting dust, steam from wall vents
//  - the security alarm turns the lamps red
//  - factory PA announcements
public class FactoryAmbience : MonoBehaviour
{
    [Header("Section")]
    public float width = 60f;
    public float height = 18f;
    public int seed = 1;

    [Header("Background machinery")]
    public Color silhouetteColor = new Color(0.03f, 0.035f, 0.05f, 0.55f);
    public Color hazeColor = new Color(1f, 0.6f, 0.3f, 0.03f);

    [Header("Lamps")]
    [Tooltip("Where lamps hang: x, y = ceiling point, z = distance to the floor below")]
    public Vector3[] lampPoints;
    public Color lampColor = new Color(1f, 0.85f, 0.55f);
    [Range(0f, 1f), Tooltip("Share of lamps broken while no relay is online (0 = intact hall, 1 = all broken)")]
    public float damage = 0.5f;

    [Header("PA announcements")]
    [TextArea] public string[] announcements;
    public float firstAnnouncementDelay = 14f;
    public float announcementInterval = 34f;

    [Header("Audio")]
    public AudioSource chimeSound;
    public AudioSource powerUpSound;

    // Lamps currently drawn (also read by the smoke test)
    public int LampCount => lamps.Count;
    public int PoweredLamps { get { int n = 0; foreach (Lamp l in lamps) if (l.powered) n++; return n; } }

    const int SortHaze = -19, SortFar = -18, SortNear = -16, SortLamp = -9;

    class Lamp
    {
        public float rank;            // lamps with a rank below the powered share are on
        public bool powered;
        public bool dead;             // without power: dead (dark) or flickering
        public Vector2 position;
        public SpriteRenderer bulb, cone, pool;
        public float flickerSeed, dropoutUntil, nextSpark;
    }

    class Mote
    {
        public Transform transform;
        public SpriteRenderer sr;
        public Vector2 velocity;
        public float phase;
    }

    private readonly List<Lamp> lamps = new List<Lamp>();
    private readonly List<Mote> motes = new List<Mote>();
    private readonly List<Transform> vents = new List<Transform>();
    private Transform farLayer, nearLayer, hazeLayer;
    private Camera cam;
    private System.Random rng;
    private float nextSteam;

    void Start()
    {
        rng = new System.Random(seed);
        cam = Camera.main;
        BuildParallax();
        BuildLamps();
        BuildDust();
        foreach (SpriteRenderer sr in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
            if (sr.name == "Vent") vents.Add(sr.transform);

        ApplyPower(RelayManager.ActivatedCount, false);
        RelayManager.RelayActivated += OnRelayActivated;
        if (announcements != null && announcements.Length > 0) StartCoroutine(Announcements());
    }

    void OnDestroy()
    {
        RelayManager.RelayActivated -= OnRelayActivated;
    }

    float R(float min, float max) { return min + (float)rng.NextDouble() * (max - min); }

    // ---------------- Parallax machinery ----------------

    SpriteRenderer Block(Transform parent, Vector2 center, Vector2 size, Color color, int order, float angle = 0f)
    {
        var go = new GameObject("Shape");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Fx.Pixel;   // 4 px at 32 ppu = 0.125 units: scale 8 = 1 unit
        go.transform.localScale = new Vector3(size.x * 8f, size.y * 8f, 1f);
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    SpriteRenderer Dot(Transform parent, Vector2 center, Vector2 size, Color color, int order)
    {
        var go = new GameObject("Glow");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Fx.Circle;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    void BuildParallax()
    {
        hazeLayer = new GameObject("Parallax_Haze").transform;
        farLayer = new GameObject("Parallax_Far").transform;
        nearLayer = new GameObject("Parallax_Near").transform;
        foreach (Transform t in new[] { hazeLayer, farLayer, nearLayer }) t.SetParent(transform, false);

        Color far = silhouetteColor;
        Color near = new Color(silhouetteColor.r * 0.7f, silhouetteColor.g * 0.7f, silhouetteColor.b * 0.7f, Mathf.Min(1f, silhouetteColor.a * 1.25f));
        float from = -12f, to = width + 12f;

        // Warm haze from distant furnaces, behind everything
        for (float x = from; x < to; x += R(10f, 16f))
            Dot(hazeLayer, new Vector2(x, R(2f, height * 0.5f)), new Vector2(R(10f, 16f), R(6f, 9f)), hazeColor, SortHaze);

        // Far: girders with cross bracing, tanks, chimneys
        for (float x = from; x < to; x += R(5f, 9f))
        {
            float kind = (float)rng.NextDouble();
            if (kind < 0.45f)
            {
                // Lattice girder from floor to ceiling
                float w = R(0.6f, 1f);
                Block(farLayer, new Vector2(x - w * 0.5f, height * 0.5f), new Vector2(0.18f, height + 4f), far, SortFar);
                Block(farLayer, new Vector2(x + w * 0.5f, height * 0.5f), new Vector2(0.18f, height + 4f), far, SortFar);
                for (float y = -1f; y < height + 2f; y += w * 1.6f)
                    Block(farLayer, new Vector2(x, y), new Vector2(0.09f, w * 1.9f), far, SortFar, (Mathf.RoundToInt(y) % 2 == 0) ? 32f : -32f);
            }
            else if (kind < 0.75f)
            {
                // Storage tank with a dome and a ladder
                float w = R(2.5f, 4f), h = R(4f, 7f);
                Block(farLayer, new Vector2(x, h * 0.5f - 1f), new Vector2(w, h), far, SortFar);
                Dot(farLayer, new Vector2(x, h - 1f), new Vector2(w * 1.05f, w * 0.7f), new Color(far.r, far.g, far.b, far.a * 0.9f), SortFar);
                Block(farLayer, new Vector2(x + w * 0.3f, h * 0.5f), new Vector2(0.08f, h + 1f), near, SortFar);
            }
            else
            {
                // Chimney stack reaching into the dark
                float w = R(1f, 1.6f);
                Block(farLayer, new Vector2(x, height * 0.5f), new Vector2(w, height + 4f), far, SortFar);
                Block(farLayer, new Vector2(x, R(3f, height - 3f)), new Vector2(w * 1.3f, 0.25f), near, SortFar);
            }
        }

        // Near: long horizontal pipes with drops and valves, hanging chains with hooks
        int pipes = Mathf.Max(2, Mathf.RoundToInt(height / 7f));
        for (int i = 0; i < pipes; i++)
        {
            float y = R(height * 0.35f, height - 2.5f);
            float thick = R(0.25f, 0.45f);
            Block(nearLayer, new Vector2((from + to) * 0.5f, y), new Vector2(to - from, thick), near, SortNear);
            for (float x = from + R(2f, 8f); x < to; x += R(8f, 18f))
            {
                float drop = R(2f, y - 1f);
                Block(nearLayer, new Vector2(x, y - drop * 0.5f), new Vector2(thick * 0.8f, drop), near, SortNear);
                Block(nearLayer, new Vector2(x, y - drop), new Vector2(thick * 2f, thick * 0.8f), near, SortNear);
                // Flange
                Block(nearLayer, new Vector2(x + R(-3f, 3f), y), new Vector2(0.15f, thick * 1.8f), near, SortNear);
            }
        }
        for (float x = from + R(3f, 10f); x < to; x += R(10f, 20f))
        {
            float length = R(2f, height * 0.4f);
            for (float y = height - 0.5f; y > height - length; y -= 0.32f)
                Block(nearLayer, new Vector2(x, y), new Vector2(0.1f, 0.22f), near, SortNear);
            Block(nearLayer, new Vector2(x + 0.15f, height - length - 0.1f), new Vector2(0.35f, 0.12f), near, SortNear);
        }
    }

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam != null)
        {
            Vector2 c = cam.transform.position;
            // A layer at factor f moves with the camera by f: the further, the slower it seems to pass
            hazeLayer.position = new Vector3(c.x * 0.75f, c.y * 0.5f - height * 0.15f, 0f);
            farLayer.position = new Vector3(c.x * 0.55f, c.y * 0.3f - 1f, 0f);
            nearLayer.position = new Vector3(c.x * 0.3f, c.y * 0.15f, 0f);
        }
        UpdateLamps();
        UpdateDust();
        UpdateSteam();
    }

    // ---------------- Lamps and power ----------------

    void BuildLamps()
    {
        if (lampPoints == null) return;
        var root = new GameObject("CeilingLamps").transform;
        root.SetParent(transform, false);
        foreach (Vector3 p in lampPoints)
        {
            var lamp = new Lamp
            {
                position = new Vector2(p.x, p.y),
                rank = (float)rng.NextDouble(),
                dead = rng.NextDouble() < 0.5,
                flickerSeed = R(0f, 100f),
                nextSpark = Time.time + R(2f, 8f),
            };
            float drop = Mathf.Max(2f, p.z);
            // Cable, housing, bulb
            Block(root, new Vector2(p.x, p.y - 0.2f), new Vector2(0.06f, 0.4f), new Color(0.1f, 0.1f, 0.12f), SortLamp);
            Block(root, new Vector2(p.x, p.y - 0.45f), new Vector2(0.9f, 0.18f), new Color(0.2f, 0.2f, 0.22f), SortLamp + 1);
            lamp.bulb = Block(root, new Vector2(p.x, p.y - 0.58f), new Vector2(0.55f, 0.08f), lampColor, SortLamp + 2);
            // Light: a soft cone down to the floor and a pool where it lands
            lamp.cone = Dot(root, new Vector2(p.x, p.y - 0.6f - drop * 0.5f), new Vector2(2.6f, drop * 1.05f), Color.clear, SortLamp - 1);
            lamp.pool = Dot(root, new Vector2(p.x, p.y - 0.6f - drop + 0.2f), new Vector2(4.5f, 1.2f), Color.clear, SortLamp - 1);
            lamps.Add(lamp);
        }
    }

    float PoweredShare(int relays)
    {
        // No relay: (1 - damage) of the lamps work. Three relays: all of them.
        return Mathf.Lerp(1f - damage, 1f, relays / (float)RelayManager.TotalRelays);
    }

    void ApplyPower(int relays, bool animate)
    {
        float share = PoweredShare(relays);
        var newlyPowered = new List<Lamp>();
        foreach (Lamp lamp in lamps)
        {
            bool on = lamp.rank < share;
            if (on && !lamp.powered)
            {
                if (animate) newlyPowered.Add(lamp);
                else lamp.powered = true;
            }
        }
        if (!animate) return;
        // A relay always visibly brings something back: at least the next broken lamp
        if (newlyPowered.Count == 0)
        {
            Lamp next = null;
            foreach (Lamp lamp in lamps)
                if (!lamp.powered && (next == null || lamp.rank < next.rank)) next = lamp;
            if (next != null) newlyPowered.Add(next);
        }
        StartCoroutine(PowerWave(newlyPowered));
    }

    void OnRelayActivated(int count)
    {
        ApplyPower(count, true);
        if (powerUpSound != null) powerUpSound.Play();
        Announce("POWER GRID: " + count + "/" + RelayManager.TotalRelays + " ONLINE.", 2.5f);
    }

    // Lamps come back one after another, starting at the Robot (it stands at the relay)
    IEnumerator PowerWave(List<Lamp> newlyPowered)
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Vector2 origin = player != null ? (Vector2)player.transform.position : new Vector2(width * 0.5f, 0f);
        newlyPowered.Sort((a, b) => Vector2.Distance(a.position, origin).CompareTo(Vector2.Distance(b.position, origin)));
        float startTime = Time.time;
        foreach (Lamp lamp in newlyPowered)
        {
            float due = startTime + 0.4f + Vector2.Distance(lamp.position, origin) * 0.05f;
            while (Time.time < due) yield return null;
            lamp.powered = true;
            Fx.Sparks(lamp.position + Vector2.down * 0.55f, lampColor, 6, 3f);
            Fx.Spawn(Fx.Circle, lamp.position + Vector2.down * 0.55f, Vector2.zero, 0.35f, 1.5f, 3f,
                new Color(lampColor.r, lampColor.g, lampColor.b, 0.6f), new Color(lampColor.r, lampColor.g, lampColor.b, 0f));
        }
    }

    void UpdateLamps()
    {
        bool alarm = AlarmSystem.IsActive;
        float alarmPulse = Mathf.PingPong(Time.time * 3f, 1f);
        foreach (Lamp lamp in lamps)
        {
            float brightness;
            if (lamp.powered) brightness = Mathf.Lerp(0.9f, 1f, Mathf.PerlinNoise(lamp.flickerSeed, Time.time * 2f));
            else if (lamp.dead) brightness = 0f;
            else
            {
                // Broken: wanders and blacks out
                if (Random.value < 1.5f * Time.deltaTime) lamp.dropoutUntil = Time.time + Random.Range(0.05f, 0.4f);
                brightness = Time.time < lamp.dropoutUntil ? 0.05f : Mathf.Lerp(0.2f, 0.85f, Mathf.PerlinNoise(lamp.flickerSeed, Time.time * 7f));
            }

            Color color = alarm ? Color.Lerp(new Color(1f, 0.15f, 0.1f), new Color(0.5f, 0.05f, 0.05f), alarmPulse) : lampColor;
            if (alarm && brightness < 0.5f) brightness = 0.5f;
            lamp.bulb.color = brightness > 0.01f ? new Color(color.r, color.g, color.b, 1f) * new Color(brightness, brightness, brightness, 1f)
                                                 : new Color(0.12f, 0.12f, 0.13f);
            lamp.cone.color = new Color(color.r, color.g, color.b, 0.055f * brightness);
            lamp.pool.color = new Color(color.r, color.g, color.b, 0.09f * brightness);

            // Broken lamps spit sparks now and then
            if (!lamp.powered && Time.time > lamp.nextSpark)
            {
                lamp.nextSpark = Time.time + Random.Range(3f, 9f);
                if (IsVisible(lamp.position)) Fx.Sparks(lamp.position + Vector2.down * 0.55f, new Color(1f, 0.8f, 0.4f), 5, 3f);
            }
        }
    }

    bool IsVisible(Vector2 point)
    {
        if (cam == null) return false;
        Vector3 v = cam.WorldToViewportPoint(point);
        return v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f;
    }

    // ---------------- Dust and steam ----------------

    void BuildDust()
    {
        var root = new GameObject("Dust").transform;
        root.SetParent(transform, false);
        for (int i = 0; i < 28; i++)
        {
            SpriteRenderer sr = Block(root, Vector2.zero, Vector2.one * R(0.05f, 0.1f), new Color(1f, 0.9f, 0.75f, R(0.1f, 0.3f)), Fx.SortingOrder - 31);
            motes.Add(new Mote
            {
                transform = sr.transform,
                sr = sr,
                velocity = new Vector2(R(-0.3f, 0.3f), R(-0.15f, 0.1f)),
                phase = R(0f, 6.28f),
            });
            sr.transform.position = new Vector3(R(0f, width), R(0f, height), 0f);
        }
    }

    void UpdateDust()
    {
        if (cam == null) return;
        float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
        Vector2 c = cam.transform.position;
        foreach (Mote m in motes)
        {
            Vector3 p = m.transform.position;
            p += (Vector3)(m.velocity + new Vector2(0f, Mathf.Sin(Time.time * 0.7f + m.phase) * 0.12f)) * Time.deltaTime;
            // Keep the dust around the camera: wrap at the edges of the view
            if (p.x < c.x - halfW - 1f) p.x += halfW * 2f + 2f;
            if (p.x > c.x + halfW + 1f) p.x -= halfW * 2f + 2f;
            if (p.y < c.y - halfH - 1f) p.y += halfH * 2f + 2f;
            if (p.y > c.y + halfH + 1f) p.y -= halfH * 2f + 2f;
            m.transform.position = p;
        }
    }

    void UpdateSteam()
    {
        if (vents.Count == 0 || Time.time < nextSteam) return;
        nextSteam = Time.time + 0.18f;
        foreach (Transform vent in vents)
        {
            if (vent == null || !IsVisible(vent.position)) continue;
            Vector2 at = (Vector2)vent.position + new Vector2(Random.Range(-0.3f, 0.3f), 0.2f);
            Fx.Spawn(Fx.Circle, at, new Vector2(Random.Range(-0.2f, 0.2f), Random.Range(0.6f, 1f)), 2.2f, 0.4f, 1.8f,
                new Color(0.75f, 0.78f, 0.8f, 0.12f), new Color(0.75f, 0.78f, 0.8f, 0f), 0f, 0f, -31 - 14);
        }
    }

    // ---------------- PA announcements ----------------

    void Announce(string text, float delay)
    {
        StartCoroutine(AnnounceAfter(text, delay));
    }

    IEnumerator AnnounceAfter(string text, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (UIManager.Instance == null) yield break;
        if (chimeSound != null) chimeSound.Play();
        UIManager.Instance.ShowAnnouncement(text);
    }

    IEnumerator Announcements()
    {
        yield return new WaitForSeconds(firstAnnouncementDelay);
        int index = 0;
        while (true)
        {
            UIManager ui = UIManager.Instance;
            // The factory keeps talking, but not over a log window or a boss fight
            if (ui != null && !ui.IsLogOpen && !ui.IsBossFight)
            {
                if (chimeSound != null) chimeSound.Play();
                ui.ShowAnnouncement(announcements[index % announcements.Length]);
                index++;
            }
            yield return new WaitForSeconds(announcementInterval);
        }
    }
}
