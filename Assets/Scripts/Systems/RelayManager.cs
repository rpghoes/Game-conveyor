using System.Collections.Generic;
using UnityEngine;

// Keeps count of activated power relays (3 in the factory; the surface hatch needs all of them).
// It is a static class (not attached to any object), so the count survives loading the next section.
public static class RelayManager
{
    public const int TotalRelays = 3;

    // Relay IDs ("SceneName/ObjectName") that have been activated
    private static readonly HashSet<string> activated = new HashSet<string>();

    // (activated count) - after a new relay comes online
    public static event System.Action<int> RelayActivated;

    public static int ActivatedCount => activated.Count;
    public static bool AllActivated => ActivatedCount >= TotalRelays;

    public static bool IsActivated(string relayId)
    {
        return activated.Contains(relayId);
    }

    public static void Activate(string relayId)
    {
        if (!activated.Add(relayId)) return; // already counted
        if (UIManager.Instance != null) UIManager.Instance.UpdateRelays(ActivatedCount, TotalRelays);
        RelayActivated?.Invoke(ActivatedCount);
    }

    // Called when a new game starts from the main menu
    public static void ResetProgress()
    {
        activated.Clear();
    }

    // Also reset when pressing Play in the editor
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        activated.Clear();
        RelayActivated = null;
    }
}
