using System.Collections;
using UnityEngine;

// A service door in the factory wall. When an alarm is raised nearby, a Guard steps out of it
// (the door light turns red and blinks first, so the player sees where it comes from).
// Each door keeps at most maxAlive Guards in the section.
public class GuardSpawnPoint : MonoBehaviour
{
    public GameObject guardPrefab;
    public int maxAlive = 1;
    [Tooltip("Door warning before the Guard comes out")]
    public float openDelay = 0.8f;

    [Header("Art")]
    public SpriteRenderer doorRenderer;
    public SpriteRenderer lamp;
    public Color lampIdle = new Color(0.25f, 0.6f, 0.3f);
    public Color lampAlarm = new Color(1f, 0.2f, 0.15f);

    [Header("Audio")]
    public AudioSource openSound;

    private readonly System.Collections.Generic.List<GameObject> spawned = new System.Collections.Generic.List<GameObject>();
    private bool busy;

    void OnEnable() { AlarmSystem.SpawnPoints.Add(this); }
    void OnDisable() { AlarmSystem.SpawnPoints.Remove(this); }

    void Start()
    {
        if (lamp != null) lamp.color = lampIdle;
    }

    public void Dispatch()
    {
        spawned.RemoveAll(g => g == null);
        if (busy || guardPrefab == null || spawned.Count >= maxAlive) return;
        StartCoroutine(DispatchRoutine());
    }

    IEnumerator DispatchRoutine()
    {
        busy = true;
        if (openSound != null) openSound.Play();
        float timer = 0f;
        while (timer < openDelay)
        {
            timer += Time.deltaTime;
            if (lamp != null) lamp.color = Mathf.FloorToInt(timer * 10f) % 2 == 0 ? lampAlarm : lampIdle;
            yield return null;
        }
        if (lamp != null) lamp.color = lampAlarm;
        if (doorRenderer != null) doorRenderer.color = new Color(0.35f, 0.35f, 0.35f); // the door is open (dark)

        GameObject guard = Instantiate(guardPrefab, transform.position, Quaternion.identity);
        spawned.Add(guard);
        Fx.Sparks((Vector2)transform.position + Vector2.up, new Color(1f, 0.8f, 0.4f), 8, 3f);
        GuardAI ai = guard.GetComponent<GuardAI>();
        if (ai != null) ai.Alert();

        yield return new WaitForSeconds(0.6f);
        if (doorRenderer != null) doorRenderer.color = Color.white;
        yield return new WaitForSeconds(2f);
        if (lamp != null) lamp.color = lampIdle;
        busy = false;
    }
}
