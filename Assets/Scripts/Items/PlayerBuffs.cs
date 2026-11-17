using UnityEngine;

// Temporary effects from consumable items, on the Robot:
//   ENERGY SHIELD   - absorbs the next few hits (or runs out after shieldDuration)
//   PRESS OVERDRIVE - the press arm hits twice as hard for a while
// PlayerHealth asks TryAbsorbHit() before taking damage; PlayerCombat multiplies its damage by DamageMultiplier.
[RequireComponent(typeof(PlayerHealth))]
public class PlayerBuffs : MonoBehaviour
{
    public float shieldDuration = 20f;
    public int shieldHits = 2;
    public float overdriveDuration = 15f;
    public float overdriveMultiplier = 2f;
    public Color shieldColor = new Color(0.45f, 0.95f, 1f, 0.35f);

    [Header("Art")]
    [Tooltip("Bubble drawn around the Robot while the shield is up")]
    public SpriteRenderer shieldBubble;

    [Header("Audio")]
    public AudioSource shieldSound;
    public AudioSource shieldBreakSound;

    public int ShieldHitsLeft { get; private set; }
    public float ShieldTimeLeft => ShieldHitsLeft > 0 ? Mathf.Max(0f, shieldUntil - Time.time) : 0f;
    public float OverdriveTimeLeft => Mathf.Max(0f, overdriveUntil - Time.time);
    public float DamageMultiplier => OverdriveTimeLeft > 0f ? overdriveMultiplier : 1f;

    private float shieldUntil;
    private float overdriveUntil;
    private Vector2 Chest => (Vector2)transform.position + Vector2.up * 0.9f;

    public void AddShield()
    {
        ShieldHitsLeft = shieldHits + PlayerUpgrades.ExtraShieldHits;
        shieldUntil = Time.time + shieldDuration;
        if (shieldSound != null) shieldSound.Play();
        Fx.Shockwave(Chest, shieldColor, 3f, 0.4f);
    }

    public void AddOverdrive()
    {
        overdriveUntil = Time.time + overdriveDuration;
        Fx.Shockwave(Chest, new Color(1f, 0.6f, 0.2f), 3f, 0.4f);
    }

    // Returns true if the shield took the hit
    public bool TryAbsorbHit()
    {
        if (ShieldTimeLeft <= 0f) return false;
        ShieldHitsLeft--;
        Fx.Shockwave(Chest, shieldColor, 2.2f, 0.25f);
        Fx.Sparks(Chest, shieldColor, 8, 5f);
        DamageNumber.Show(Chest + Vector2.up * 1.1f, ShieldHitsLeft > 0 ? "SHIELD" : "SHIELD DOWN", new Color(0.6f, 0.95f, 1f), 0.9f, 0.7f);
        if (ShieldHitsLeft == 0 && shieldBreakSound != null) shieldBreakSound.Play();
        return true;
    }

    public void ClearAll()
    {
        ShieldHitsLeft = 0;
        overdriveUntil = 0f;
    }

    void Update()
    {
        if (shieldBubble != null)
        {
            float left = ShieldTimeLeft;
            shieldBubble.enabled = left > 0f;
            if (left > 0f)
            {
                // Blinks during the last 3 seconds
                bool blink = left < 3f && Mathf.FloorToInt(Time.time * 8f) % 2 == 0;
                float pulse = 0.8f + 0.2f * Mathf.Sin(Time.time * 5f);
                shieldBubble.color = new Color(shieldColor.r, shieldColor.g, shieldColor.b, blink ? 0.08f : shieldColor.a * pulse);
            }
        }
        // Overdrive: orange sparks from the press arm
        if (OverdriveTimeLeft > 0f && Random.value < 8f * Time.deltaTime)
            Fx.Sparks(Chest + new Vector2(Random.Range(-0.4f, 0.4f), -0.2f), new Color(1f, 0.6f, 0.2f), 1, 2f);
    }
}
