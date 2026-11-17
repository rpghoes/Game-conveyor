using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// EDITOR-ONLY TOOL (not part of the game).
// CONVEYOR > Run Smoke Test: enters Play Mode and lets ConveyorSmokeTest play through every section,
// checking the real runtime behaviour. Report: Logs/ConveyorSmokeTest.txt.
// Command line: Unity.exe -batchmode -projectPath <path> -executeMethod ConveyorSmokeTestLauncher.RunBatch
// (Unity quits by itself when the test is done: exit code 0 = passed, 1 = failed).
[InitializeOnLoad]
public static class ConveyorSmokeTestLauncher
{
    const string RunKey = "ConveyorSmokeTest.Run";
    const string ExitKey = "ConveyorSmokeTest.Exit";

    // Survives the domain reload that happens when Play Mode starts
    static ConveyorSmokeTestLauncher()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("CONVEYOR/Run Smoke Test")]
    public static void Run() { Begin(false); }

    public static void RunBatch() { Begin(true); }

    static void Begin(bool exitWhenDone)
    {
        SessionState.SetBool(RunKey, true);
        SessionState.SetBool(ExitKey, exitWhenDone);
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu/MainMenu.unity");
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(RunKey, false)) return;
        SessionState.SetBool(RunKey, false);
        ConveyorSmokeTest.ExitWhenDone = SessionState.GetBool(ExitKey, false);
        new GameObject("ConveyorSmokeTest").AddComponent<ConveyorSmokeTest>();
    }
}
