using System.Collections.Generic;
using UnityEngine;

// Passive upgrades: factory components Unit 0412 finds and builds into itself. Each one is found once
// and lasts until a new game. Static, so they survive loading the next section.
public static class PlayerUpgrades
{
    public enum Module
    {
        ReinforcedPlating,   // +1 health cell
        ServoOverdrive,      // press arm hits 50% harder
        ActuatorOverclock,   // +12% movement speed
        DashCapacitor,       // dash recharges 40% faster
        ShieldEmitter,       // energy shields absorb one more hit
    }

    public static readonly Dictionary<Module, (string name, string description)> Info = new Dictionary<Module, (string, string)>
    {
        { Module.ReinforcedPlating, ("REINFORCED PLATING", "MAX HP +1 CELL") },
        { Module.ServoOverdrive, ("SERVO OVERDRIVE", "PRESS ARM DAMAGE +50%") },
        { Module.ActuatorOverclock, ("ACTUATOR OVERCLOCK", "MOVE SPEED +12%") },
        { Module.DashCapacitor, ("DASH CAPACITOR", "DASH RECHARGE -40%") },
        { Module.ShieldEmitter, ("SHIELD EMITTER", "SHIELDS ABSORB +1 HIT") },
    };

    // (module) - after a new module was installed
    public static event System.Action<Module> Installed;

    private static readonly HashSet<Module> installed = new HashSet<Module>();
    // Pickup IDs already collected ("Scene/Object"), so a reloaded section doesn't give them again
    private static readonly HashSet<string> collected = new HashSet<string>();

    public static bool Has(Module module) => installed.Contains(module);
    public static IEnumerable<Module> All => installed;

    public static int ExtraHealthCells => Has(Module.ReinforcedPlating) ? 1 : 0;
    public static float DamageMultiplier => Has(Module.ServoOverdrive) ? 1.5f : 1f;
    public static float SpeedMultiplier => Has(Module.ActuatorOverclock) ? 1.12f : 1f;
    public static float DashCooldownMultiplier => Has(Module.DashCapacitor) ? 0.6f : 1f;
    public static int ExtraShieldHits => Has(Module.ShieldEmitter) ? 1 : 0;

    public static bool WasCollected(string pickupId) => collected.Contains(pickupId);
    public static void MarkCollected(string pickupId) { collected.Add(pickupId); }

    public static void Install(Module module)
    {
        if (!installed.Add(module)) return;
        Installed?.Invoke(module);
    }

    // Called when a new game starts from the main menu
    public static void ResetProgress()
    {
        installed.Clear();
        collected.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        ResetProgress();
        Installed = null;
    }
}
