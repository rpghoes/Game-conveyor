using UnityEngine;

// All numbers of the level-up system in one asset: XP, how the Robot grows, and how enemies,
// the mini-boss and the final boss scale with the Robot's level.
// The game loads Assets/Resources/ProgressionSettings.asset (CONVEYOR > 1. Setup Project creates it).
// If the asset is missing, the defaults below are used.
[CreateAssetMenu(fileName = "ProgressionSettings", menuName = "CONVEYOR/Progression Settings")]
public class ProgressionSettings : ScriptableObject
{
    // Enemy strength for a range of Robot levels. The tier with the highest minPlayerLevel
    // that is <= the Robot's level is used.
    [System.Serializable]
    public struct Tier
    {
        public int minPlayerLevel;
        [Tooltip("Max HP multiplier")]
        public float health;
        [Tooltip("Damage multiplier")]
        public float damage;
        [Tooltip("Movement speed multiplier (keep it small)")]
        public float moveSpeed;
        [Tooltip("Attack rate multiplier: cooldowns and wind-ups get shorter")]
        public float attackSpeed;
        [Tooltip("Detection range / extra shots / special attack frequency")]
        public float aggression;

        public Tier(int level, float health, float damage, float moveSpeed, float attackSpeed, float aggression)
        {
            minPlayerLevel = level;
            this.health = health;
            this.damage = damage;
            this.moveSpeed = moveSpeed;
            this.attackSpeed = attackSpeed;
            this.aggression = aggression;
        }

        public static readonly Tier One = new Tier(1, 1f, 1f, 1f, 1f, 1f);
    }

    [Header("Units")]
    [Tooltip("HP points in one 'hit'. Enemy/player damage and health in the prefabs are counted in hits; " +
             "the game multiplies them by this value so the scaling can be smooth (1 hit = 10 HP)")]
    public int hitValue = 10;

    [Header("Experience")]
    public int xpPerEnemy = 10;
    public int xpPerMiniBoss = 60;
    public int xpPerBoss = 150;
    [Tooltip("XP needed to go from level 1 to 2 (55 = 5-6 enemies)")]
    public int xpForLevel2 = 55;
    [Tooltip("Each next level needs this much more XP (20 = 2 more enemies per level)")]
    public int xpStepPerLevel = 20;
    public int maxLevel = 20;

    [Header("Robot Growth (per level above 1)")]
    [Tooltip("Max HP added per level (HP points, base = PlayerHealth.maxHealth x hitValue)")]
    public int healthPerLevel = 4;
    [Tooltip("Attack damage +x per level (0.12 = +12%)")]
    public float damagePerLevel = 0.12f;
    [Tooltip("Damage taken -x per level (0.025 = -2.5%)")]
    public float defensePerLevel = 0.025f;
    public float maxDefense = 0.35f;
    [Tooltip("HP regenerated per second, per level above 1, while out of combat")]
    public float regenPerLevel = 0.25f;
    [Tooltip("Seconds without taking damage before regeneration starts")]
    public float regenDelay = 4f;
    [Tooltip("Run speed +x per level")]
    public float moveSpeedPerLevel = 0.015f;
    public float maxMoveSpeedBonus = 0.15f;
    [Tooltip("Ultimate meter gain +x per level")]
    public float ultimateChargePerLevel = 0.04f;
    [Tooltip("Fully heal the Robot when it levels up")]
    public bool healOnLevelUp = true;

    [Header("Normal Enemies (Guard, Scanner)")]
    public Tier[] enemyTiers =
    {
        new Tier(1, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f),
        new Tier(3, 1.25f, 1.10f, 1.05f, 1.10f, 1.10f),
        new Tier(5, 1.60f, 1.40f, 1.10f, 1.20f, 1.25f),
        new Tier(7, 2.00f, 1.70f, 1.15f, 1.30f, 1.40f),
        new Tier(9, 2.50f, 2.00f, 1.20f, 1.40f, 1.50f),
    };

    [Header("Mini-Boss (Foreman, Section 03)")]
    public Tier[] miniBossTiers =
    {
        new Tier(1, 1.00f, 1.20f, 1.00f, 1.00f, 1.00f),
        new Tier(3, 1.30f, 1.40f, 1.05f, 1.10f, 1.15f),
        new Tier(5, 1.75f, 1.65f, 1.10f, 1.20f, 1.30f),
        new Tier(7, 2.25f, 1.90f, 1.15f, 1.30f, 1.45f),
        new Tier(9, 2.80f, 2.20f, 1.20f, 1.40f, 1.60f),
    };

    [Header("Final Boss (Director, Section 05)")]
    public Tier[] bossTiers =
    {
        new Tier(1, 1.00f, 1.30f, 1.00f, 1.00f, 1.00f),
        new Tier(3, 1.15f, 1.50f, 1.05f, 1.05f, 1.15f),
        new Tier(5, 1.35f, 1.75f, 1.10f, 1.12f, 1.30f),
        new Tier(7, 1.60f, 2.00f, 1.15f, 1.20f, 1.45f),
        new Tier(9, 1.90f, 2.30f, 1.20f, 1.28f, 1.60f),
    };

    [Header("Beyond The Last Tier")]
    [Tooltip("Each level past the last tier adds this much to the health multiplier")]
    public float extraHealthPerLevel = 0.12f;
    [Tooltip("Each level past the last tier adds this much to the damage multiplier")]
    public float extraDamagePerLevel = 0.06f;

    [Header("Boss Behaviour Unlocks (Robot level)")]
    [Tooltip("Bosses start chaining a follow-up attack without resting")]
    public int bossComboLevel = 4;
    [Tooltip("Chance of a combo at the combo level; grows with aggression")]
    [Range(0f, 1f)] public float bossComboChance = 0.3f;
    [Tooltip("Bosses get extra projectiles / rockets / pillars")]
    public int bossExtraShotsLevel = 6;
    [Tooltip("The final boss gets a 4th phase (CORE MELTDOWN) below 15% HP")]
    public int bossFinalPhaseLevel = 7;
}
