using UnityEngine;

// Door to the next factory section. Walk into it to load the next scene (through the LoadingScreen).
// It can stay locked until enough power relays are active, or until a boss is destroyed.
[RequireComponent(typeof(Collider2D))]
public class LevelExit : MonoBehaviour
{
    [Tooltip("Exact name of the scene to load (it must be in File > Build Profiles scene list)")]
    public string nextSceneName;
    [Tooltip("How many power relays must be active before this door opens")]
    public int requiredRelays = 0;
    [Tooltip("Optional: the door stays locked until this enemy is dead")]
    public EnemyHealth requiredBoss;
    [Tooltip("Shown when the boss is still alive")]
    public string bossLockedMessage = "ACCESS DENIED - SUPERVISOR ON DUTY";

    [Header("Visuals")]
    public SpriteRenderer doorRenderer;
    public Color lockedColor = new Color(1f, 0.35f, 0.35f);
    public Color openColor = Color.white;

    private bool loading;
    private bool shownOpen;

    bool RelaysDone => RelayManager.ActivatedCount >= requiredRelays;
    bool BossDone => requiredBoss == null || requiredBoss.IsDead;
    public bool IsOpen => RelaysDone && BossDone;

    void Start()
    {
        shownOpen = IsOpen;
        if (doorRenderer != null) doorRenderer.color = shownOpen ? openColor : lockedColor;
    }

    void Update()
    {
        bool open = IsOpen;
        if (open == shownOpen) return;
        shownOpen = open;
        if (doorRenderer != null) doorRenderer.color = open ? openColor : lockedColor;
        // The door unlocks: a short green pulse so the player notices
        if (open && doorRenderer != null) Fx.Shockwave(doorRenderer.bounds.center, new Color(0.5f, 1f, 0.6f), 3f, 0.5f);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (loading || !other.CompareTag("Player")) return;

        if (!IsOpen)
        {
            if (UIManager.Instance != null)
            {
                if (!BossDone) UIManager.Instance.ShowMessage(bossLockedMessage, 2.5f);
                else UIManager.Instance.ShowMessage("ACCESS DENIED - NO POWER (RELAYS " +
                    RelayManager.ActivatedCount + "/" + requiredRelays + ")", 2.5f);
            }
            return;
        }

        loading = true;
        LoadingScreen.Load(nextSceneName);
    }
}
