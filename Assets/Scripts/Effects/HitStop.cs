using System.Collections;
using UnityEngine;

// "Hit stop": the game nearly freezes for a few hundredths of a second when a heavy hit lands.
// It never touches a paused game (loading screen, timeScale 0) and only restores time it changed itself.
public class HitStop : MonoBehaviour
{
    const float FrozenScale = 0.05f;

    private static HitStop runner;
    private float frozenUntil;
    private bool running;

    public static void Freeze(float duration)
    {
        if (LoadingScreen.IsLoading || Time.timeScale == 0f) return;
        if (runner == null)
        {
            var go = new GameObject("HitStop") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            runner = go.AddComponent<HitStop>();
        }
        runner.frozenUntil = Mathf.Max(runner.frozenUntil, Time.unscaledTime + duration);
        if (!runner.running) runner.StartCoroutine(runner.Run());
    }

    IEnumerator Run()
    {
        running = true;
        if (Time.timeScale == 1f) Time.timeScale = FrozenScale;
        while (Time.unscaledTime < frozenUntil) yield return null;
        if (Mathf.Approximately(Time.timeScale, FrozenScale)) Time.timeScale = 1f;
        running = false;
    }
}
