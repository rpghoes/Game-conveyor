using UnityEngine;

// Touch it to save your respawn position in this room.
[RequireComponent(typeof(Collider2D))]
public class Checkpoint : MonoBehaviour
{
    public SpriteRenderer spriteRenderer;
    public Sprite inactiveSprite;
    public Sprite activeSprite;

    [Header("Audio")]
    public AudioSource activateSound;

    private bool activated;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (activated || !other.CompareTag("Player")) return;

        activated = true;
        if (spriteRenderer != null && activeSprite != null) spriteRenderer.sprite = activeSprite;
        if (activateSound != null) activateSound.Play();

        if (RespawnManager.Instance != null) RespawnManager.Instance.SetCheckpoint(transform.position);
        if (UIManager.Instance != null) UIManager.Instance.ShowMessage("CHECKPOINT SAVED", 1.5f);
    }
}
