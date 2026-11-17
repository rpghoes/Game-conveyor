using System.IO;
using UnityEditor;
using UnityEngine;

// EDITOR-ONLY TOOL (not part of the game).
// Makes sure Assets/Resources/ProgressionSettings.asset exists - the game loads all XP / level /
// difficulty numbers from it. Runs once when the project opens, and from CONVEYOR > 1. Setup Project.
// Select the asset to tune the values; deleting it brings back the defaults.
[InitializeOnLoad]
public static class ProgressionSettingsCreator
{
    public const string AssetPath = "Assets/Resources/ProgressionSettings.asset";

    static ProgressionSettingsCreator()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EnsureAsset();
        };
    }

    [MenuItem("CONVEYOR/Select Progression Settings")]
    public static void SelectAsset()
    {
        Selection.activeObject = EnsureAsset();
        EditorGUIUtility.PingObject(Selection.activeObject);
    }

    public static ProgressionSettings EnsureAsset()
    {
        var settings = AssetDatabase.LoadAssetAtPath<ProgressionSettings>(AssetPath);
        if (settings != null) return settings;
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (File.Exists(AssetPath)) return null; // exists but not imported yet
        settings = ScriptableObject.CreateInstance<ProgressionSettings>();
        AssetDatabase.CreateAsset(settings, AssetPath);
        AssetDatabase.SaveAssets();
        Debug.Log("CONVEYOR: created " + AssetPath);
        return settings;
    }
}
