using System.Collections.Generic;
using UnityEngine;

// Keeps count of rebooted terminals. It is a static class (not attached to any object),
// so the count survives loading the next room.
public static class TerminalManager
{
    public const int TotalTerminals = 3;

    // Terminal IDs ("SceneName/ObjectName") that have been rebooted
    private static readonly HashSet<string> activated = new HashSet<string>();

    public static int ActivatedCount => activated.Count;
    public static bool AllActivated => ActivatedCount >= TotalTerminals;

    public static bool IsActivated(string terminalId)
    {
        return activated.Contains(terminalId);
    }

    public static void Activate(string terminalId)
    {
        if (!activated.Add(terminalId)) return; // already counted

        if (UIManager.Instance != null)
        {
            UIManager.Instance.UpdateTerminals(ActivatedCount, TotalTerminals);
            if (AllActivated) UIManager.Instance.ShowRebootComplete();
        }
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
    }
}
