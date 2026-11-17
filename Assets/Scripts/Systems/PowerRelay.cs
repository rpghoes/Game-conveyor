using System.Collections;
using UnityEngine;

// A power relay. Walk up to it and press E (gamepad: Y) to switch it on.
// Switching it on prints the relay's factory log on screen (terminal style) and counts towards
// the three relays the surface hatch needs. Lamps and machinery that listen to RelayManager can react.
// Needs a trigger Collider2D that marks the interaction area.
[RequireComponent(typeof(Collider2D))]
public class PowerRelay : MonoBehaviour
{
    public SpriteRenderer spriteRenderer;
    public Sprite offSprite;
    public Sprite onSprite;
    public string promptText = "E - ACTIVATE RELAY";

    [Header("Factory Log")]
    [Tooltip("Header line of the log window, e.g. RELAY 1 // FACTORY FLOOR")]
    public string logTitle = "POWER RELAY";
    [Tooltip("Lines printed one by one after activation")]
    [TextArea] public string[] logLines;

    [Header("Audio")]
    public AudioSource activateSound;

    private bool playerInRange;
    private bool activated;

    // Unique name used by RelayManager
    string RelayId => gameObject.scene.name + "/" + gameObject.name;

    void Start()
    {
        // Already switched on earlier (e.g. section reloaded)
        if (RelayManager.IsActivated(RelayId))
        {
            activated = true;
            if (spriteRenderer != null) spriteRenderer.sprite = onSprite;
        }
    }

    void Update()
    {
        if (!playerInRange || activated) return;
        if (GameInput.InteractPressed) Activate();
    }

    void Activate()
    {
        activated = true;
        if (spriteRenderer != null) spriteRenderer.sprite = onSprite;
        if (activateSound != null) activateSound.Play();

        RelayManager.Activate(RelayId);
        if (UIManager.Instance != null)
        {
            UIManager.Instance.HidePrompt();
            UIManager.Instance.ShowTerminalLog(logTitle, logLines);
        }
        StartCoroutine(ActivationEffect());
    }

    // Power surge: a flash, a "pop" in size and sparks running up the cable
    IEnumerator ActivationEffect()
    {
        Vector3 startScale = transform.localScale;
        Color flashColor = new Color(1f, 0.85f, 0.4f);
        Vector2 top = spriteRenderer != null ? new Vector2(spriteRenderer.bounds.center.x, spriteRenderer.bounds.max.y) : (Vector2)transform.position;
        Fx.Shockwave(top, flashColor, 3f, 0.4f);
        Fx.Sparks(top, flashColor, 14, 6f);
        CameraFollow.Shake(0.08f, 0.2f);

        float duration = 0.5f;
        float timer = 0f;
        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = timer / duration;
            transform.localScale = startScale * (1f + Mathf.Sin(t * Mathf.PI) * 0.2f);
            if (spriteRenderer != null) spriteRenderer.color = Color.Lerp(flashColor, Color.white, t);
            yield return null;
        }

        transform.localScale = startScale;
        if (spriteRenderer != null) spriteRenderer.color = Color.white;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = true;
        if (!activated && UIManager.Instance != null) UIManager.Instance.ShowPrompt(promptText);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = false;
        if (UIManager.Instance != null) UIManager.Instance.HidePrompt();
    }
}
