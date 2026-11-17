using System.Collections.Generic;
using UnityEngine;

// The factory's security alarm. A Scanner that finds the Robot calls AlarmSystem.Raise(position):
//   - every Guard within alertRadius starts hunting the Robot (GuardAI.Alert)
//   - every GuardSpawnPoint within spawnRadius sends a new Guard out of its service door
//   - the HUD shows the alarm (red pulse + message)
// A short cooldown stops several Scanners from flooding the section with Guards.
public static class AlarmSystem
{
    public const float AlertRadius = 16f;
    public const float SpawnRadius = 20f;
    public const float Cooldown = 6f;

    // (position) - after an alarm was raised
    public static event System.Action<Vector2> Raised;

    public static readonly List<GuardSpawnPoint> SpawnPoints = new List<GuardSpawnPoint>();
    private static float lastAlarmTime = -100f;

    public static bool IsActive => Time.time < lastAlarmTime + Cooldown;

    // Returns false while on cooldown
    public static bool Raise(Vector2 position)
    {
        if (IsActive) return false;
        lastAlarmTime = Time.time;

        foreach (GuardAI guard in Object.FindObjectsByType<GuardAI>(FindObjectsSortMode.None))
        {
            if (Vector2.Distance(guard.transform.position, position) <= AlertRadius) guard.Alert();
        }
        foreach (GuardSpawnPoint point in SpawnPoints.ToArray())
        {
            if (point != null && Vector2.Distance(point.transform.position, position) <= SpawnRadius) point.Dispatch();
        }
        if (UIManager.Instance != null) UIManager.Instance.ShowAlarm("ALARM: DEFECTIVE UNIT DETECTED");
        Raised?.Invoke(position);
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        SpawnPoints.Clear();
        lastAlarmTime = -100f;
        Raised = null;
    }
}
