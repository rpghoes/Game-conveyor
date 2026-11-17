using UnityEngine;

// Put on a "Hazard" object (e.g. a boss projectile) so it hurts with its owner's damage scaling.
// Hazards without it (electric floors, corrupted pools) always do 1 hit.
public class HazardDamage : MonoBehaviour
{
    [Tooltip("Damage multiplier (the boss's EnemyHealth.DamageMultiplier)")]
    public float multiplier = 1f;
}
