using UnityEngine;

// Background music that survives room changes.
// If the next room uses the same track, the music keeps playing without restarting;
// if it uses a different track (e.g. the boss room), the old music is replaced.
[RequireComponent(typeof(AudioSource))]
public class MusicPlayer : MonoBehaviour
{
    private static MusicPlayer current;

    private AudioSource source;

    void Awake()
    {
        source = GetComponent<AudioSource>();

        if (current != null && current != this)
        {
            if (current.source.clip == source.clip)
            {
                // Same track is already playing - keep it and remove this copy
                source.Stop();
                Destroy(gameObject);
                return;
            }
            Destroy(current.gameObject);
        }

        current = this;
        DontDestroyOnLoad(gameObject);
        if (source.clip != null && !source.isPlaying) source.Play();
    }
}
