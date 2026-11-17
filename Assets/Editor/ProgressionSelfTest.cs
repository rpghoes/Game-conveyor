using System.Text;
using UnityEditor;
using UnityEngine;

// EDITOR-ONLY TOOL (not part of the game).
// CONVEYOR > Test Progression: checks the level-up rules and prints a balance table
// (XP per level, Robot stats, and how many hits each side needs) to the Console.
// Run it after changing ProgressionSettings to see the effect of the new numbers.
public static class ProgressionSelfTest
{
    // Base values from the prefabs (in hits)
    const int RobotHits = 3, RobotAttack = 1;
    const int GuardHits = 3, MiniBossHits = 9, BossHits = 5;   // Guard, Foreman, Director control panel

    [MenuItem("CONVEYOR/Test Progression")]
    public static void Run()
    {
        int failures = 0;
        PlayerProgression.ResetProgress();

        // 1. The same enemy never gives XP twice
        int first = PlayerProgression.RegisterKill("Test/Bug@0,0", EnemyRank.Normal);
        int again = PlayerProgression.RegisterKill("Test/Bug@0,0", EnemyRank.Normal);
        failures += Check(first > 0 && again == 0, "a kill is only counted once");
        PlayerProgression.ResetProgress();

        // 2. Kills needed per level follow the 5-6, 7-8, 9-10 ... pattern
        ProgressionSettings s = PlayerProgression.Settings;
        var table = new StringBuilder();
        table.AppendLine("LVL | XP to next | kills | Robot HP | DMG/hit | DEF | regen/s | bug HP (hits) | mini HP (hits) | boss HP (hits) | hits to die: bug / mini / boss");
        int kill = 0;
        for (int level = 1; level <= 12; level++)
        {
            int needed = PlayerProgression.XpToNextLevel;
            float kills = (float)needed / s.xpPerEnemy;
            if (level <= 9) failures += Check(kills >= 3.5f + 2f * level && kills <= 4.5f + 2f * level + 0.01f, "level " + level + " needs " + kills + " kills");

            int robotHp = PlayerProgression.PlayerMaxHealth(RobotHits);
            int robotHit = PlayerProgression.PlayerDamage(RobotAttack);
            table.AppendFormat("{0,3} | {1,10} | {2,5:0.#} | {3,8} | {4,7} | {5,3:0%} | {6,7:0.0} | {7} | {8} | {9} | {10} / {11} / {12}\n",
                level, needed, kills, robotHp, robotHit, PlayerProgression.PlayerDefense, PlayerProgression.PlayerRegen,
                Enemy(EnemyRank.Normal, GuardHits, robotHit), Enemy(EnemyRank.MiniBoss, MiniBossHits, robotHit), Enemy(EnemyRank.Boss, BossHits, robotHit),
                HitsToDie(robotHp, EnemyRank.Normal), HitsToDie(robotHp, EnemyRank.MiniBoss), HitsToDie(robotHp, EnemyRank.Boss));

            // Balance guards: a normal enemy never takes more than 5 hits and never kills the Robot in under 3
            ProgressionSettings.Tier t = PlayerProgression.TierFor(EnemyRank.Normal);
            int bugHp = Mathf.RoundToInt(GuardHits * PlayerProgression.HitValue * t.health);
            failures += Check(Mathf.CeilToInt((float)bugHp / robotHit) <= 5, "level " + level + ": guards stay killable");
            failures += Check(HitsToDie(robotHp, EnemyRank.Normal) >= 3, "level " + level + ": guards don't one-shot the Robot");
            // The Foreman has as much HP as three Guards; its real difficulty is that it can only be hit while stuck
            failures += Check(PlayerProgression.TierFor(EnemyRank.MiniBoss).health * MiniBossHits >= t.health * GuardHits * 3f, "level " + level + ": mini-boss much tougher");

            // Gain exactly one level, one kill at a time
            int startLevel = PlayerProgression.Level;
            while (PlayerProgression.Level == startLevel) PlayerProgression.RegisterKill("Test/Enemy" + (kill++), EnemyRank.Normal);
            failures += Check(PlayerProgression.Level == startLevel + 1, "one level at a time");
        }

        Debug.Log("CONVEYOR progression balance:\n" + table);
        PlayerProgression.ResetProgress();
        if (failures == 0) Debug.Log("CONVEYOR: progression test PASSED");
        else Debug.LogError("CONVEYOR: progression test FAILED (" + failures + " checks)");
    }

    static string Enemy(EnemyRank rank, int baseHits, int robotHit)
    {
        int hp = Mathf.RoundToInt(baseHits * PlayerProgression.HitValue * PlayerProgression.TierFor(rank).health);
        return hp + " (" + Mathf.CeilToInt((float)hp / robotHit) + ")";
    }

    // Same formula as PlayerHealth.TakeDamage for a 1-hit attack
    static int HitsToDie(int robotHp, EnemyRank rank)
    {
        float raw = PlayerProgression.HitValue * PlayerProgression.TierFor(rank).damage * (1f - PlayerProgression.PlayerDefense);
        return Mathf.CeilToInt(robotHp / (float)Mathf.Max(1, Mathf.RoundToInt(raw)));
    }

    static int Check(bool ok, string what)
    {
        if (!ok) Debug.LogError("Progression check failed: " + what);
        return ok ? 0 : 1;
    }

    // Batch mode entry: Unity.exe -batchmode -executeMethod ProgressionSelfTest.RunBatch
    public static void RunBatch()
    {
        ProgressionSettingsCreator.EnsureAsset();
        AssetDatabase.Refresh();
        Run();
    }
}
