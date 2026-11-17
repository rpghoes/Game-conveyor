using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

// A system terminal. Walk up to it and press E to reboot it.
// Needs a trigger Collider2D that marks the interaction area.
[RequireComponent(typeof(Collider2D))]
public class Terminal : MonoBehaviour
{
    public SpriteRenderer spriteRenderer;
    public Sprite offSprite;
    public Sprite onSprite;
    public string promptText = "E - REBOOT";

    [Header("Audio")]
    public AudioSource activateSound;

    private bool playerInRange;
    private bool activated;

    // Unique name used by TerminalManager
    string TerminalId => gameObject.scene.name + "/" + gameObject.name;

    void Start()
    {
        // Already rebooted earlier (e.g. room reloaded)
        if (TerminalManager.IsActivated(TerminalId))
        {
            activated = true;
            if (spriteRenderer != null) spriteRenderer.sprite = onSprite;
        }
    }

    void Update()
    {
        if (!playerInRange || activated || Keyboard.current == null) return;
        if (Keyboard.current.eKey.wasPressedThisFrame) Activate();
    }

    void Activate()
    {
        activated = true;
        if (spriteRenderer != null) spriteRenderer.sprite = onSprite;
        if (activateSound != null) activateSound.Play();

        if (UIManager.Instance != null)
        {
            UIManager.Instance.HidePrompt();
            UIManager.Instance.ShowMessage("TERMINAL REBOOTED", 2f);
        }

        TerminalManager.Activate(TerminalId);
        StartCoroutine(ActivationEffect());
    }

    // Simple effect: a quick green flash and a "pop" in size
    IEnumerator ActivationEffect()
    {
        Vector3 startScale = transform.localScale;
        Color flashColor = new Color(0.5f, 1f, 0.7f);
        float duration = 0.5f;
        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = timer / duration;
            transform.localScale = startScale * (1f + Mathf.Sin(t * Mathf.PI) * 0.25f);
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
