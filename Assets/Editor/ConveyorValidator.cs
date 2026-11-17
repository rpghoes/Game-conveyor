using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

// EDITOR-ONLY TOOL (not part of the game).
// CONVEYOR > Validate Level: opens every scene of the build list and checks the things that break a playthrough:
// missing scripts, missing sprites, the section chain (every door leads to a scene in the build), the three relays
// and their logs, doors that need more relays than exist so far, belts overlapping solid tiles,
// presses hanging over a platform instead of the floor, and the ending scene.
// The report goes to the Console and to Logs/ConveyorValidation.txt.
// For automation: Unity.exe -projectPath <path> -executeMethod ConveyorValidator.BuildAndValidateBatch
// (rebuilds everything, validates, then quits with exit code 0 = passed, 1 = failed).
public static class ConveyorValidator
{
    const string ReportPath = "Logs/ConveyorValidation.txt";

    [MenuItem("CONVEYOR/Validate Level")]
    public static bool Validate()
    {
        var errors = new List<string>();
        var notes = new List<string>();
        string[] buildScenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var sceneNames = new HashSet<string>(buildScenes.Select(Path.GetFileNameWithoutExtension));

        if (buildScenes.Length == 0) errors.Add("Build list is empty - run CONVEYOR > 2. Build All Scenes");
        if (!sceneNames.Contains("MainMenu")) errors.Add("MainMenu is not in the build list");
        if (!sceneNames.Contains("Ending_Surface")) errors.Add("Ending_Surface is not in the build list");

        var relayNumbers = new List<string>();
        int relaysSoFar = 0;
        foreach (string path in buildScenes)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            string name = scene.name;
            GameObject[] roots = scene.GetRootGameObjects();
            var all = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToArray();

            foreach (GameObject go in all)
            {
                int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                if (missing > 0) errors.Add(name + ": " + go.name + " has " + missing + " missing script(s)");
                var sr = go.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite == null) errors.Add(name + ": " + go.name + " has a SpriteRenderer without a sprite");
            }
            if (Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length != 1) errors.Add(name + ": expected exactly one camera");

            if (name == "MainMenu")
            {
                var ui = Object.FindFirstObjectByType<UIManager>();
                if (ui == null) errors.Add("MainMenu: no UIManager (START button)");
                else if (!sceneNames.Contains(ui.firstRoomScene)) errors.Add("MainMenu: START loads '" + ui.firstRoomScene + "' which is not in the build");
                continue;
            }
            if (name == "Ending_Surface")
            {
                var ending = Object.FindFirstObjectByType<EndingSequence>();
                if (ending == null || ending.robotSprite == null) errors.Add("Ending_Surface: EndingSequence or its robot sprite is missing");
                else if (!sceneNames.Contains(ending.mainMenuScene)) errors.Add("Ending_Surface: main menu scene is not in the build");
                continue;
            }

            // ---- A factory section ----
            var players = Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            if (players.Length != 1) errors.Add(name + ": expected 1 player, found " + players.Length);
            if (Object.FindFirstObjectByType<RespawnManager>() == null) errors.Add(name + ": no RespawnManager");
            if (Object.FindFirstObjectByType<UIManager>() == null) errors.Add(name + ": no HUD");

            var relays = Object.FindObjectsByType<PowerRelay>(FindObjectsSortMode.None);
            foreach (PowerRelay relay in relays)
            {
                relayNumbers.Add(name + "/" + relay.name);
                if (relay.logLines == null || relay.logLines.Length == 0) errors.Add(name + ": " + relay.name + " has no factory log");
            }
            relaysSoFar += relays.Length;

            var exits = Object.FindObjectsByType<LevelExit>(FindObjectsSortMode.None);
            if (exits.Length == 0) errors.Add(name + ": no exit door");
            foreach (LevelExit exit in exits)
            {
                if (!sceneNames.Contains(exit.nextSceneName)) errors.Add(name + ": door leads to '" + exit.nextSceneName + "' which is not in the build");
                if (exit.requiredRelays > relaysSoFar) errors.Add(name + ": door needs " + exit.requiredRelays + " relays but only " + relaysSoFar + " exist up to here");
            }

            Tilemap ground = FindTilemap(roots, "Ground");
            Tilemap platforms = FindTilemap(roots, "Platforms");
            if (ground == null) { errors.Add(name + ": no Ground tilemap"); continue; }

            foreach (ConveyorBelt belt in Object.FindObjectsByType<ConveyorBelt>(FindObjectsSortMode.None))
            {
                int y = Mathf.FloorToInt(belt.transform.position.y);
                int x0 = Mathf.RoundToInt(belt.transform.position.x - belt.length * 0.5f);
                for (int x = x0; x < x0 + belt.length; x++)
                    if (ground.HasTile(new Vector3Int(x, y, 0))) { errors.Add(name + ": belt overlaps a solid tile at " + x + "," + y); break; }
                if (belt.beltRenderer == null) errors.Add(name + ": belt without a renderer");
            }

            foreach (IndustrialPress press in Object.FindObjectsByType<IndustrialPress>(FindObjectsSortMode.None))
            {
                if (press.head == null || press.rod == null || press.shadow == null) errors.Add(name + ": press at x=" + press.transform.position.x + " is missing art");
                float restBottom = press.ceilingY - press.restDrop - press.headHeight;
                // Walk down from the raised head: the first tile below must be solid floor, not a platform
                for (int y = Mathf.FloorToInt(restBottom) - 1; y >= 0; y--)
                {
                    bool platform = false, solid = false;
                    for (float dx = -press.width * 0.5f + 0.5f; dx < press.width * 0.5f; dx += 1f)
                    {
                        var cell = new Vector3Int(Mathf.FloorToInt(press.transform.position.x + dx), y, 0);
                        platform |= platforms != null && platforms.HasTile(cell);
                        solid |= ground.HasTile(cell);
                    }
                    if (platform) { errors.Add(name + ": press at x=" + press.transform.position.x + " hangs over a platform"); break; }
                    if (solid) { if (restBottom - (y + 1) < 2f) errors.Add(name + ": press at x=" + press.transform.position.x + " has almost no room to fall"); break; }
                }
                // A ground tile directly inside the raised head means the press sits in a wall
                if (ground.HasTile(new Vector3Int(Mathf.FloorToInt(press.transform.position.x), Mathf.FloorToInt(restBottom + 0.5f), 0)))
                    errors.Add(name + ": press at x=" + press.transform.position.x + " is inside a solid block");
            }
            notes.Add(name + ": relays " + relays.Length + ", belts " + Object.FindObjectsByType<ConveyorBelt>(FindObjectsSortMode.None).Length +
                      ", presses " + Object.FindObjectsByType<IndustrialPress>(FindObjectsSortMode.None).Length +
                      ", enemies " + Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None).Length);
        }

        if (relayNumbers.Count != RelayManager.TotalRelays)
            errors.Add("Expected " + RelayManager.TotalRelays + " relays in the factory, found " + relayNumbers.Count + ": " + string.Join(", ", relayNumbers));

        var report = new StringBuilder();
        report.AppendLine("CONVEYOR level validation - " + System.DateTime.Now);
        foreach (string n in notes) report.AppendLine("  " + n);
        report.AppendLine(errors.Count == 0 ? "RESULT: PASSED" : "RESULT: FAILED (" + errors.Count + ")");
        foreach (string e in errors) report.AppendLine("  ERROR: " + e);
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, report.ToString());
        if (errors.Count == 0) Debug.Log(report.ToString());
        else Debug.LogError(report.ToString());
        return errors.Count == 0;
    }

    static Tilemap FindTilemap(GameObject[] roots, string name)
    {
        foreach (GameObject root in roots)
            foreach (Tilemap map in root.GetComponentsInChildren<Tilemap>(true))
                if (map.name == name) return map;
        return null;
    }

    // Full rebuild + validation, then quit (for command-line runs)
    public static void BuildAndValidateBatch()
    {
        bool ok = false;
        try
        {
            ConveyorBuilder.SetupProject();
            ConveyorBuilder.BuildAllScenes();
            ok = Validate();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
        EditorApplication.Exit(ok ? 0 : 1);
    }
}
