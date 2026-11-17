using System.Collections.Generic;
using UnityEngine;

public enum EnemyRank { Normal, MiniBoss, Boss }

// The Robot's level and XP. Like TerminalManager it is a static class, so progress survives
// loading the next room. Every enemy kill gives XP once (kills are remembered by a unique ID).
// Also answers "how strong is the Robot" and "how strong should this enemy be" for the current level.
public static class PlayerProgression
{
    public static int Level { get; private set; } = 1;
    // XP collected towards the next level
    public static int Xp { get; private set; }
    public static int TotalKills { get; private set; }

    // (new level) - after the level went up
    public static event System.Action<int> LeveledUp;
    // (xp gained) - after any XP change
    public static event System.Action<int> XpChanged;

    private static readonly HashSet<string> killed = new HashSet<string>();
    private static ProgressionSettings settings;

    public static ProgressionSettings Settings
    {
        get
        {
            if (settings == null) settings = Resources.Load<ProgressionSettings>("ProgressionSettings");
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<ProgressionSettings>();
                settings.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            return settings;
        }
    }

    public static int HitValue => Mathf.Max(1, Settings.hitValue);
    public static bool IsMaxLevel => Level >= Settings.maxLevel;
    public static int XpToNextLevel => XpRequiredFor(Level);
    public static float XpProgress => IsMaxLevel ? 1f : (float)Xp / XpToNextLevel;

    // XP needed to go from this level to the next one
    public static int XpRequiredFor(int level)
    {
        return Settings.xpForLevel2 + Settings.xpStepPerLevel * (Mathf.Max(level, 1) - 1);
    }

    // ---------------- Robot stats ----------------

    static int LevelsGained => Level - 1;

    public static int PlayerMaxHealth(int baseHits)
    {
        return baseHits * HitValue + Settings.healthPerLevel * LevelsGained;
    }

    public static float PlayerDamageMultiplier => 1f + Settings.damagePerLevel * LevelsGained;
    public static float PlayerDefense => Mathf.Min(Settings.defensePerLevel * LevelsGained, Settings.maxDefense);
    public static float PlayerRegen => Settings.regenPerLevel * LevelsGained;
    public static float PlayerMoveSpeedMultiplier => 1f + Mathf.Min(Settings.moveSpeedPerLevel * LevelsGained, Settings.maxMoveSpeedBonus);
    public static float UltimateChargeMultiplier => 1f + Settings.ultimateChargePerLevel * LevelsGained;

    // Robot attack in hits (PlayerCombat.damage / PlayerUltimate.damage) -> HP points
    public static int PlayerDamage(int baseHits)
    {
        return Mathf.Max(1, Mathf.RoundToInt(baseHits * HitValue * PlayerDamageMultiplier));
    }

    // ---------------- Enemy scaling ----------------

    public static ProgressionSettings.Tier TierFor(EnemyRank rank)
    {
        ProgressionSettings.Tier[] tiers = rank == EnemyRank.Boss ? Settings.bossTiers
            : rank == EnemyRank.MiniBoss ? Settings.miniBossTiers : Settings.enemyTiers;
        if (tiers == null || tiers.Length == 0) return ProgressionSettings.Tier.One;

        ProgressionSettings.Tier best = tiers[0];
        int lastLevel = 0;
        foreach (ProgressionSettings.Tier tier in tiers)
        {
            if (tier.minPlayerLevel <= Level && tier.minPlayerLevel >= best.minPlayerLevel) best = tier;
            lastLevel = Mathf.Max(lastLevel, tier.minPlayerLevel);
        }
        // Past the last tier the enemies keep growing slowly
        if (best.minPlayerLevel == lastLevel && Level > lastLevel + 1)
        {
            int extra = Level - (lastLevel + 1);
            best.health += Settings.extraHealthPerLevel * extra;
            best.damage += Settings.extraDamagePerLevel * extra;
        }
        return best;
    }

    // Boss behaviour unlocks
    public static bool BossCombosUnlocked => Level >= Settings.bossComboLevel;
    public static bool BossExtraShotsUnlocked => Level >= Settings.bossExtraShotsLevel;
    public static bool BossFinalPhaseUnlocked => Level >= Settings.bossFinalPhaseLevel;

    public static float BossComboChance(float aggression)
    {
        if (!BossCombosUnlocked) return 0f;
        return Mathf.Clamp01(Settings.bossComboChance * aggression);
    }

    // ---------------- XP ----------------

    public static int XpFor(EnemyRank rank)
    {
        return rank == EnemyRank.Boss ? Settings.xpPerBoss : rank == EnemyRank.MiniBoss ? Settings.xpPerMiniBoss : Settings.xpPerEnemy;
    }

    // Called by EnemyHealth when an enemy dies. Returns the XP given (0 if this enemy was already counted).
    public static int RegisterKill(string killId, EnemyRank rank)
    {
        if (!killed.Add(killId)) return 0;
        TotalKills++;
        int xp = XpFor(rank);
        AddXp(xp);
        return xp;
    }

    public static void AddXp(int amount)
    {
        if (amount <= 0) return;
        if (IsMaxLevel)
        {
            XpChanged?.Invoke(amount);
            return;
        }

        Xp += amount;
        int levelsGained = 0;
        while (!IsMaxLevel && Xp >= XpToNextLevel)
        {
            Xp -= XpToNextLevel;
            Level++;
            levelsGained++;
        }
        if (IsMaxLevel) Xp = 0;

        XpChanged?.Invoke(amount);
        if (levelsGained > 0) LeveledUp?.Invoke(Level);
    }

    // Called when a new game starts from the main menu
    public static void ResetProgress()
    {
        Level = 1;
        Xp = 0;
        TotalKills = 0;
        killed.Clear();
    }

    // Also reset when pressing Play in the editor
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        ResetProgress();
        settings = null;
        LeveledUp = null;
        XpChanged = null;
    }
}
