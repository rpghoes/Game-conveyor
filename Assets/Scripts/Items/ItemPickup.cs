using UnityEngine;

// An item lying in the factory. Walk into it to take it.
//   REPAIR KIT     - restores 2 health cells (stays where it is if the Robot is not damaged)
//   ENERGY SHIELD  - absorbs the next hits for a while (PlayerBuffs)
//   PRESS OVERDRIVE- double press arm damage for a while (PlayerBuffs)
//   MODULE         - a passive upgrade, kept until a new game (PlayerUpgrades)
// Items float gently and glow in their colour so they read at a glance.
[RequireComponent(typeof(Collider2D))]
public class ItemPickup : MonoBehaviour
{
    public enum Kind { RepairKit, EnergyShield, PressOverdrive, Module }

    public Kind kind = Kind.RepairKit;
    [Tooltip("Only for Kind = Module")]
    public PlayerUpgrades.Module module;
    [Tooltip("Repair kit: health cells restored")]
    public int healCells = 2;

    [Header("Art")]
    public SpriteRenderer itemRenderer;
    public SpriteRenderer glow;

    [Header("Audio")]
    public AudioSource pickupSound;

    private Vector3 basePosition;
    private bool taken;
    private float nextFullMessage;

    string PickupId => gameObject.scene.name + "/" + gameObject.name;

    public static Color ColorOf(Kind kind)
    {
        switch (kind)
        {
            case Kind.RepairKit: return new Color(0.4f, 1f, 0.5f);
            case Kind.EnergyShield: return new Color(0.45f, 0.95f, 1f);
            case Kind.PressOverdrive: return new Color(1f, 0.6f, 0.2f);
            default: return new Color(1f, 0.85f, 0.3f);
        }
    }

    public static string NameOf(Kind kind)
    {
        switch (kind)
        {
            case Kind.RepairKit: return "REPAIR KIT";
            case Kind.EnergyShield: return "ENERGY SHIELD";
            case Kind.PressOverdrive: return "PRESS OVERDRIVE";
            default: return "MODULE";
        }
    }

    void Start()
    {
        basePosition = transform.position;
        // Modules are found once per game, even if the section is loaded again
        if (kind == Kind.Module && (PlayerUpgrades.WasCollected(PickupId) || PlayerUpgrades.Has(module))) gameObject.SetActive(false);
    }

    void Update()
    {
        if (taken) return;
        transform.position = basePosition + Vector3.up * Mathf.Sin(Time.time * 2.5f + basePosition.x) * 0.12f;
        if (glow != null)
        {
            Color c = ColorOf(kind);
            glow.color = new Color(c.r, c.g, c.b, 0.25f + 0.15f * Mathf.Sin(Time.time * 4f));
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (taken || !other.CompareTag("Player")) return;
        PlayerHealth health = other.GetComponent<PlayerHealth>();
        PlayerBuffs buffs = other.GetComponent<PlayerBuffs>();
        if (health == null || health.IsDead) return;

        string message;
        switch (kind)
        {
            case Kind.RepairKit:
                if (health.CurrentHealth >= health.MaxHealth)
                {
                    if (Time.time >= nextFullMessage && UIManager.Instance != null) UIManager.Instance.ShowMessage("REPAIR KIT: NO DAMAGE TO REPAIR", 1.5f);
                    nextFullMessage = Time.time + 2f;
                    return;
                }
                health.Heal(healCells * PlayerProgression.HitValue);
                message = "REPAIR KIT  +" + healCells + " HP";
                break;
            case Kind.EnergyShield:
                if (buffs == null) return;
                buffs.AddShield();
                message = "ENERGY SHIELD ONLINE";
                break;
            case Kind.PressOverdrive:
                if (buffs == null) return;
                buffs.AddOverdrive();
                message = "PRESS OVERDRIVE  x2 DAMAGE";
                break;
            default:
                PlayerUpgrades.MarkCollected(PickupId);
                PlayerUpgrades.Install(module);
                var info = PlayerUpgrades.Info[module];
                message = "MODULE INSTALLED: " + info.name + "  //  " + info.description;
                health.RefreshMaxHealth();
                break;
        }

        taken = true;
        Color color = ColorOf(kind);
        Fx.Shockwave(transform.position, color, 2.5f, 0.35f);
        Fx.Sparks(transform.position, color, 12, 5f);
        if (pickupSound != null)
        {
            // Let the sound finish after the item disappears
            pickupSound.transform.SetParent(null, true);
            pickupSound.Play();
            Destroy(pickupSound.gameObject, 2f);
        }
        if (UIManager.Instance != null) UIManager.Instance.ShowPickup(message, color);
        gameObject.SetActive(false);
    }
}
