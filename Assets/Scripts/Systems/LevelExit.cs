using UnityEngine;

// Door to the next room. Walk into it to load the next scene (through the LoadingScreen).
// It can stay locked until enough terminals are rebooted, or until a (mini-)boss is destroyed.
[RequireComponent(typeof(Collider2D))]
public class LevelExit : MonoBehaviour
{
    [Tooltip("Exact name of the scene to load (it must be in File > Build Profiles scene list)")]
    public string nextSceneName;
    [Tooltip("How many terminals must be rebooted before this door opens")]
    public int requiredTerminals = 0;
    [Tooltip("Optional: the door stays locked until this enemy is dead")]
    public EnemyHealth requiredBoss;

    [Header("Visuals")]
    public SpriteRenderer doorRenderer;
    public Color lockedColor = new Color(1f, 0.35f, 0.35f);
    public Color openColor = Color.white;

    private bool loading;

    bool TerminalsDone => TerminalManager.ActivatedCount >= requiredTerminals;
    bool BossDone => requiredBoss == null || requiredBoss.IsDead;
    bool IsOpen => TerminalsDone && BossDone;

    void Update()
    {
        if (doorRenderer != null) doorRenderer.color = IsOpen ? openColor : lockedColor;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (loading || !other.CompareTag("Player")) return;

        if (!IsOpen)
        {
            if (UIManager.Instance != null)
            {
                if (!BossDone) UIManager.Instance.ShowMessage("ACCESS DENIED - DESTROY THE GUARDIAN FIRST", 2.5f);
                else UIManager.Instance.ShowMessage("ACCESS DENIED - REBOOT TERMINALS (" +
                    TerminalManager.ActivatedCount + "/" + requiredTerminals + ")", 2.5f);
            }
            return;
        }

        loading = true;
        LoadingScreen.Load(nextSceneName);
    }
}
