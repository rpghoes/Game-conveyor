using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// EDITOR-ONLY TOOL (not part of the game).
// Synthesizes all CONVEYOR sound effects and music as .wav files (retro chiptune style)
// and plugs them into the existing prefabs and scenes:
//   CONVEYOR > 3. Generate & Assign Sounds
// This step does NOT rebuild rooms - it only fills the AudioSources, so hand-made section edits are kept.
[InitializeOnLoad]
public static class SoundGenerator
{
    // Runs automatically once when the project opens: if the sounds don't exist yet, generate and assign them.
    static SoundGenerator()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists(SfxFolder + "SFX_Jump.wav") && File.Exists(MusicFolder + "Music_Level.wav")) return;
            GenerateAndAssign();
        };
    }

    const string SfxFolder = "Assets/Audio/SFX/";
    const string MusicFolder = "Assets/Audio/Music/";
    const int Rate = 44100;

    // Owner/AudioSource name -> clip name, for sources whose names are shared between objects.
    // Every other AudioSource uses the clip with the same name as its GameObject (e.g. SFX_Jump).
    static readonly Dictionary<string, string> ClipOverrides = new Dictionary<string, string>
    {
        { "Player/SFX_Attack", "SFX_PlayerSlash" },
        { "Player/SFX_Death", "SFX_PlayerDeath" },
        { "Guard/SFX_Attack", "SFX_GuardStrike" },
    };

    static readonly string[] Prefabs =
    {
        "Assets/Prefabs/Player/Player.prefab",
        "Assets/Prefabs/Enemies/Guard.prefab",
        "Assets/Prefabs/Enemies/Scanner.prefab",
        "Assets/Prefabs/Boss/Boss.prefab",
        "Assets/Prefabs/Boss/Boss_Sentinel.prefab",
        "Assets/Prefabs/Relays/PowerRelay.prefab",
        "Assets/Prefabs/Checkpoints/Checkpoint.prefab",
    };

    [MenuItem("CONVEYOR/3. Generate & Assign Sounds")]
    public static void GenerateAndAssign()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        GenerateClips();
        AssignToPrefabs();
        AssignToScenes();
        AssetDatabase.SaveAssets();
        Debug.Log("CONVEYOR: sounds generated and assigned");
    }

    // =====================================================================
    // PUBLIC HELPERS (also used by ConveyorBuilder)
    // =====================================================================

    public static AudioClip LoadClip(string clipName)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SfxFolder + clipName + ".wav");
        if (clip == null) clip = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicFolder + clipName + ".wav");
        return clip;
    }

    public static AudioClip ClipFor(string ownerName, string sourceName)
    {
        string clipName;
        if (!ClipOverrides.TryGetValue(ownerName + "/" + sourceName, out clipName)) clipName = sourceName;
        return LoadClip(clipName);
    }

    public static string MusicForScene(string scenePath)
    {
        if (scenePath.Contains("MainMenu") || scenePath.Contains("Ending")) return "Music_Menu";
        if (scenePath.Contains("Foreman") || scenePath.Contains("ControlRoom")) return "Music_Boss";
        return "Music_Level";
    }

    public static void SetupMusic(GameObject music, string clipName)
    {
        var source = music.GetComponent<AudioSource>();
        if (source == null) source = music.AddComponent<AudioSource>();
        source.clip = LoadClip(clipName);
        source.loop = true;
        source.playOnAwake = false; // MusicPlayer starts it, so a room change doesn't restart the same track
        source.volume = 0.45f;
        if (music.GetComponent<MusicPlayer>() == null) music.AddComponent<MusicPlayer>();
    }

    // =====================================================================
    // ASSIGNING
    // =====================================================================

    static void AssignToPrefabs()
    {
        foreach (string path in Prefabs)
        {
            if (!File.Exists(path)) continue;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            foreach (AudioSource source in root.GetComponentsInChildren<AudioSource>(true))
            {
                AudioClip clip = ClipFor(root.name, source.gameObject.name);
                if (clip != null) source.clip = clip;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void AssignToScenes()
    {
        string activeScene = EditorSceneManager.GetActiveScene().path;
        string[] scenes = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes/MainMenu", "Assets/Scenes/Conveyor" })
            .Select(AssetDatabase.GUIDToAssetPath).ToArray();

        foreach (string path in scenes)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            GameObject music = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Music_Background");
            if (music == null) continue;
            SetupMusic(music, MusicForScene(path));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        if (!string.IsNullOrEmpty(activeScene)) EditorSceneManager.OpenScene(activeScene);
    }

    // =====================================================================
    // GENERATING
    // =====================================================================

    public static void GenerateClips()
    {
        Directory.CreateDirectory(SfxFolder);
        Directory.CreateDirectory(MusicFolder);

        var sfx = new Dictionary<string, System.Func<float[]>>
        {
            { "SFX_Jump", Jump },
            { "SFX_PlayerSlash", PlayerSlash },
            { "SFX_Hurt", Hurt },
            { "SFX_PlayerDeath", PlayerDeath },
            { "SFX_UltimateCharge", UltimateCharge },
            { "SFX_UltimateBlast", UltimateBlast },
            { "SFX_EnemyHit", EnemyHit },
            { "SFX_EnemyDeath", EnemyDeath },
            { "SFX_BugAttack", BugAttack },
            { "SFX_DroneShot", DroneShot },
            { "SFX_TerminalActivate", TerminalActivate },
            { "SFX_Checkpoint", CheckpointChime },
            { "SFX_BossHit", BossHit },
            { "SFX_BossAttack", BossAttack },
            { "SFX_BossDeath", BossDeath },
            { "SFX_BossLaser", BossLaser },
            { "SFX_PressWarning", PressWarning },
            { "SFX_PressImpact", PressImpact },
            { "SFX_BeltHum", BeltHum },
            { "SFX_BeltReverse", BeltReverse },
            { "SFX_Alarm", Alarm },
            { "SFX_ScanBeep", ScanBeep },
            { "SFX_GuardStrike", GuardStrike },
            { "SFX_GuardNotice", GuardNotice },
        };
        var music = new Dictionary<string, System.Func<float[]>>
        {
            { "Music_Menu", MenuMusic },
            { "Music_Level", LevelMusic },
            { "Music_Boss", BossMusic },
        };

        foreach (var pair in sfx) Save(SfxFolder + pair.Key + ".wav", pair.Value(), 0.85f);
        foreach (var pair in music) Save(MusicFolder + pair.Key + ".wav", pair.Value(), 0.8f);

        AssetDatabase.Refresh();

        foreach (string name in sfx.Keys) ConfigureImporter(SfxFolder + name + ".wav", false);
        foreach (string name in music.Keys) ConfigureImporter(MusicFolder + name + ".wav", true);
    }

    static void ConfigureImporter(string path, bool isMusic)
    {
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null) return;
        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.loadType = isMusic ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = isMusic ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;
        settings.quality = 0.7f;
        importer.defaultSampleSettings = settings;
        importer.SaveAndReimport();
    }

    static void Save(string path, float[] data, float peak)
    {
        float max = data.Max(x => Mathf.Abs(x));
        float gain = max > 0f ? peak / max : 1f;

        using (var writer = new BinaryWriter(File.Create(path)))
        {
            int bytes = data.Length * 2;
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + bytes);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);  // PCM
            writer.Write((short)1);  // mono
            writer.Write(Rate);
            writer.Write(Rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(bytes);
            foreach (float sample in data)
            {
                float v = (float)System.Math.Tanh(sample * gain * 1.1f); // soft clip
                writer.Write((short)Mathf.Clamp(v * 32767f, -32768f, 32767f));
            }
        }
    }

    // =====================================================================
    // SYNTH
    // =====================================================================

    enum Wave { Sine, Square, Saw, Triangle }

    static System.Random rng = new System.Random(1337);

    static float[] Buffer(float seconds) => new float[Mathf.CeilToInt(seconds * Rate)];

    static float Midi(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);

    // One note. Pitch glides exponentially from f0 to f1; volume rises over `attack`, then fades out with a power curve.
    // `wrap` makes notes that run past the end continue at the start (for seamless music loops).
    static void Tone(float[] buf, float start, float dur, float f0, float f1, Wave wave, float volume,
        float attack = 0.004f, float decay = 1.5f, float duty = 0.5f, float vibratoHz = 0f, float vibratoDepth = 0f, bool wrap = false)
    {
        int s0 = Mathf.RoundToInt(start * Rate);
        int n = Mathf.RoundToInt(dur * Rate);
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            int index = s0 + i;
            if (wrap) index %= buf.Length;
            else if (index >= buf.Length) break;

            float t = i / (float)n;
            float time = i / (float)Rate;
            float f = f0 * Mathf.Pow(f1 / f0, t);
            if (vibratoDepth > 0f) f *= 1f + vibratoDepth * Mathf.Sin(2f * Mathf.PI * vibratoHz * time);
            phase += f / Rate;
            phase -= System.Math.Floor(phase);
            float p = (float)phase;

            float v;
            switch (wave)
            {
                case Wave.Sine: v = Mathf.Sin(2f * Mathf.PI * p); break;
                case Wave.Square: v = p < duty ? 0.7f : -0.7f; break;
                case Wave.Saw: v = 2f * p - 1f; break;
                default: v = 1f - 4f * Mathf.Abs(p - 0.5f); break;
            }
            float envelope = Mathf.Min(1f, time / attack) * Mathf.Pow(1f - t, decay);
            buf[index] += v * envelope * volume;
        }
    }

    // Filtered white noise. cutoff 0..1 (1 = bright, 0.02 = deep rumble) sweeps from c0 to c1.
    static void Noise(float[] buf, float start, float dur, float volume, float c0, float c1,
        float decay = 1.5f, float attack = 0.002f, bool highpass = false, bool wrap = false)
    {
        int s0 = Mathf.RoundToInt(start * Rate);
        int n = Mathf.RoundToInt(dur * Rate);
        float low = 0f;
        for (int i = 0; i < n; i++)
        {
            int index = s0 + i;
            if (wrap) index %= buf.Length;
            else if (index >= buf.Length) break;

            float t = i / (float)n;
            float time = i / (float)Rate;
            float cutoff = Mathf.Clamp(Mathf.Lerp(c0, c1, t), 0.005f, 1f);
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            low += cutoff * (white - low);
            float v = highpass ? white - low : low * Mathf.Sqrt((2f - cutoff) / cutoff) * 0.5f;
            float envelope = Mathf.Min(1f, time / attack) * Mathf.Pow(1f - t, decay);
            buf[index] += v * envelope * volume;
        }
    }

    // Reduces the sample resolution for a "corrupted / glitchy" texture
    static void Crush(float[] buf, int levels)
    {
        for (int i = 0; i < buf.Length; i++) buf[i] = Mathf.Round(buf[i] * levels) / levels;
    }

    static void LowPass(float[] buf, float cutoff)
    {
        float low = buf[buf.Length - 1];
        for (int pass = 0; pass < 2; pass++) // second pass so the loop start matches the loop end
        {
            for (int i = 0; i < buf.Length; i++)
            {
                low += cutoff * (buf[i] - low);
                if (pass == 1) buf[i] = low;
            }
        }
    }

    // =====================================================================
    // SOUND EFFECTS
    // =====================================================================

    static float[] Jump()
    {
        var b = Buffer(0.18f);
        Tone(b, 0f, 0.16f, 260f, 720f, Wave.Square, 0.5f, decay: 1f, duty: 0.25f);
        Tone(b, 0f, 0.16f, 520f, 1440f, Wave.Square, 0.15f, decay: 1.5f, duty: 0.5f);
        return b;
    }

    static float[] PlayerSlash()
    {
        var b = Buffer(0.22f);
        Noise(b, 0f, 0.2f, 0.8f, 0.9f, 0.12f, decay: 2f, attack: 0.01f);
        Tone(b, 0f, 0.12f, 1800f, 450f, Wave.Saw, 0.25f, decay: 2f);
        return b;
    }

    static float[] Hurt()
    {
        var b = Buffer(0.3f);
        Tone(b, 0f, 0.28f, 520f, 140f, Wave.Square, 0.6f, decay: 1.2f, vibratoHz: 35f, vibratoDepth: 0.15f);
        Noise(b, 0f, 0.1f, 0.5f, 0.6f, 0.3f);
        return b;
    }

    static float[] PlayerDeath()
    {
        var b = Buffer(1.5f);
        for (int k = 0; k < 6; k++)
            Tone(b, k * 0.1f, 0.2f, Midi(76 - k * 3), Midi(75 - k * 3), Wave.Square, 0.35f, decay: 1f, duty: 0.25f);
        Noise(b, 0.6f, 0.85f, 0.7f, 0.5f, 0.03f, decay: 1.5f);
        Tone(b, 0.6f, 0.85f, 220f, 35f, Wave.Saw, 0.4f, decay: 1.2f);
        Crush(b, 12);
        return b;
    }

    static float[] UltimateCharge()
    {
        var b = Buffer(0.8f);
        Tone(b, 0f, 0.75f, 110f, 880f, Wave.Saw, 0.35f, attack: 0.35f, decay: 0.3f, vibratoHz: 18f, vibratoDepth: 0.06f);
        Tone(b, 0f, 0.75f, 220f, 1760f, Wave.Square, 0.12f, attack: 0.35f, decay: 0.3f, duty: 0.125f);
        Noise(b, 0f, 0.75f, 0.2f, 0.05f, 0.6f, decay: 0.3f, attack: 0.5f);
        return b;
    }

    static float[] UltimateBlast()
    {
        var b = Buffer(1.4f);
        Noise(b, 0f, 1.35f, 1f, 0.8f, 0.03f, decay: 2.5f);
        Tone(b, 0f, 1f, 140f, 30f, Wave.Sine, 0.9f, decay: 1.5f);
        Tone(b, 0f, 0.4f, 880f, 110f, Wave.Square, 0.25f, decay: 2f);
        return b;
    }

    static float[] EnemyHit()
    {
        var b = Buffer(0.12f);
        Noise(b, 0f, 0.07f, 0.8f, 0.9f, 0.4f, decay: 2f);
        Tone(b, 0f, 0.1f, 380f, 160f, Wave.Square, 0.4f);
        return b;
    }

    static float[] EnemyDeath()
    {
        var b = Buffer(0.5f);
        Noise(b, 0f, 0.45f, 0.8f, 0.6f, 0.05f, decay: 1.8f);
        Tone(b, 0f, 0.35f, 600f, 60f, Wave.Square, 0.35f, duty: 0.25f);
        Tone(b, 0.05f, 0.25f, 1200f, 200f, Wave.Square, 0.15f);
        Crush(b, 10);
        return b;
    }

    static float[] BugAttack()
    {
        var b = Buffer(0.25f);
        Tone(b, 0f, 0.22f, 180f, 320f, Wave.Square, 0.35f, decay: 1.2f, duty: 0.3f, vibratoHz: 40f, vibratoDepth: 0.3f);
        Noise(b, 0f, 0.2f, 0.4f, 0.5f, 0.2f, highpass: true);
        return b;
    }

    static float[] DroneShot()
    {
        var b = Buffer(0.25f);
        Tone(b, 0f, 0.22f, 1600f, 280f, Wave.Square, 0.45f, decay: 1.2f);
        Tone(b, 0f, 0.22f, 1610f, 285f, Wave.Saw, 0.2f, decay: 1.2f);
        return b;
    }

    // Industrial press: two short alarm beeps before the drop
    static float[] PressWarning()
    {
        var b = Buffer(0.5f);
        Tone(b, 0f, 0.12f, 880f, 880f, Wave.Square, 0.25f, decay: 0.6f, duty: 0.5f);
        Tone(b, 0.22f, 0.12f, 880f, 880f, Wave.Square, 0.25f, decay: 0.6f, duty: 0.5f);
        return b;
    }

    // Industrial press: heavy metal slam with a low thud and a ringing tail
    static float[] PressImpact()
    {
        var b = Buffer(0.9f);
        Tone(b, 0f, 0.5f, 90f, 38f, Wave.Sine, 0.9f, attack: 0.002f, decay: 2.2f);
        Noise(b, 0f, 0.35f, 0.7f, 0.5f, 0.04f, decay: 2.5f);
        Tone(b, 0f, 0.85f, 410f, 400f, Wave.Triangle, 0.12f, decay: 3f);
        Tone(b, 0f, 0.85f, 617f, 611f, Wave.Triangle, 0.07f, decay: 3f);
        return b;
    }

    // Conveyor belt: a quiet, seamless mechanical rumble with a slow roller rhythm (looped)
    static float[] BeltHum()
    {
        var b = Buffer(2f);
        Noise(b, 0f, 2f, 0.25f, 0.03f, 0.03f, decay: 0f, attack: 0.001f, wrap: true);
        for (int k = 0; k < 8; k++) Noise(b, k * 0.25f, 0.06f, 0.18f, 0.25f, 0.1f, decay: 2f, wrap: true);
        Tone(b, 0f, 2f, 55f, 55f, Wave.Sine, 0.15f, attack: 0.001f, decay: 0f, wrap: true);
        LowPass(b, 0.35f);
        return b;
    }

    // Conveyor belt changes direction: clunk + motor whine going down and up
    static float[] BeltReverse()
    {
        var b = Buffer(0.8f);
        Noise(b, 0f, 0.12f, 0.6f, 0.4f, 0.1f, decay: 2f);
        Tone(b, 0f, 0.35f, 300f, 90f, Wave.Saw, 0.25f, decay: 1f);
        Tone(b, 0.35f, 0.4f, 90f, 300f, Wave.Saw, 0.25f, attack: 0.05f, decay: 1.2f);
        Crush(b, 12);
        return b;
    }

    // Security alarm: a two-tone factory siren, three cycles
    static float[] Alarm()
    {
        var b = Buffer(1.8f);
        for (int k = 0; k < 3; k++)
        {
            Tone(b, k * 0.6f, 0.3f, 620f, 620f, Wave.Square, 0.3f, decay: 0.3f, duty: 0.4f);
            Tone(b, k * 0.6f + 0.3f, 0.3f, 465f, 465f, Wave.Square, 0.3f, decay: 0.3f, duty: 0.4f);
        }
        LowPass(b, 0.5f);
        return b;
    }

    // Scanner locks on: rising chirps
    static float[] ScanBeep()
    {
        var b = Buffer(0.45f);
        for (int k = 0; k < 3; k++) Tone(b, k * 0.12f, 0.08f, 1300f + k * 250f, 1500f + k * 250f, Wave.Sine, 0.35f, decay: 1f);
        return b;
    }

    // Guard baton: metallic clang
    static float[] GuardStrike()
    {
        var b = Buffer(0.35f);
        Noise(b, 0f, 0.08f, 0.7f, 0.8f, 0.3f, decay: 2f);
        Tone(b, 0f, 0.3f, 720f, 700f, Wave.Triangle, 0.3f, decay: 2.5f);
        Tone(b, 0f, 0.3f, 1110f, 1090f, Wave.Triangle, 0.15f, decay: 3f);
        Tone(b, 0f, 0.12f, 180f, 90f, Wave.Square, 0.3f, decay: 2f);
        return b;
    }

    // Guard notices the Robot: short servo whirr + beep
    static float[] GuardNotice()
    {
        var b = Buffer(0.35f);
        Tone(b, 0f, 0.15f, 200f, 420f, Wave.Saw, 0.2f, decay: 0.8f);
        Tone(b, 0.15f, 0.15f, 980f, 980f, Wave.Square, 0.2f, decay: 1.2f, duty: 0.25f);
        return b;
    }

    static float[] TerminalActivate()
    {
        var b = Buffer(0.7f);
        Tone(b, 0f, 0.15f, 200f, 800f, Wave.Saw, 0.15f, decay: 1f);
        int[] notes = { 72, 76, 79, 84 };
        for (int k = 0; k < notes.Length; k++)
            Tone(b, 0.08f + k * 0.07f, 0.18f, Midi(notes[k]), Midi(notes[k]), Wave.Square, 0.3f, decay: 1.2f, duty: 0.25f);
        Tone(b, 0.36f, 0.32f, Midi(84), Midi(84), Wave.Sine, 0.4f, decay: 2f);
        return b;
    }

    static float[] CheckpointChime()
    {
        var b = Buffer(0.8f);
        Tone(b, 0f, 0.5f, Midi(79), Midi(79), Wave.Sine, 0.5f, decay: 2f);
        Tone(b, 0f, 0.3f, Midi(91), Midi(91), Wave.Triangle, 0.12f, decay: 2.5f);
        Tone(b, 0.12f, 0.65f, Midi(84), Midi(84), Wave.Sine, 0.5f, decay: 2f);
        Tone(b, 0.12f, 0.4f, Midi(96), Midi(96), Wave.Triangle, 0.12f, decay: 2.5f);
        return b;
    }

    static float[] BossHit()
    {
        var b = Buffer(0.25f);
        Noise(b, 0f, 0.12f, 0.8f, 0.7f, 0.1f);
        Tone(b, 0f, 0.22f, 160f, 70f, Wave.Square, 0.5f);
        Tone(b, 0f, 0.15f, 900f, 850f, Wave.Triangle, 0.2f, decay: 2f); // metallic ring
        return b;
    }

    static float[] BossAttack()
    {
        var b = Buffer(0.75f);
        Tone(b, 0f, 0.7f, 95f, 45f, Wave.Saw, 0.6f, attack: 0.05f, decay: 1f, vibratoHz: 9f, vibratoDepth: 0.08f);
        Tone(b, 0f, 0.7f, 97f, 46f, Wave.Square, 0.3f, attack: 0.05f, decay: 1f);
        Noise(b, 0f, 0.6f, 0.5f, 0.15f, 0.03f, attack: 0.05f);
        Crush(b, 8);
        return b;
    }

    // Core Sentinel beam: bright buzzing zap that fades out
    static float[] BossLaser()
    {
        var b = Buffer(0.7f);
        Tone(b, 0f, 0.65f, 1400f, 600f, Wave.Saw, 0.35f, attack: 0.01f, decay: 1.2f, vibratoHz: 40f, vibratoDepth: 0.05f);
        Tone(b, 0f, 0.65f, 220f, 180f, Wave.Square, 0.4f, attack: 0.01f, decay: 1f, duty: 0.25f);
        Noise(b, 0f, 0.5f, 0.35f, 0.4f, 0.5f, decay: 1.5f, highpass: true);
        Crush(b, 8);
        return b;
    }

    static float[] BossDeath()
    {
        var b = Buffer(3.4f);
        for (int k = 0; k < 6; k++)
        {
            Noise(b, k * 0.3f, 0.6f, 0.7f, 0.6f, 0.04f, decay: 1.8f);
            Tone(b, k * 0.3f, 0.5f, 120f - k * 10f, 35f, Wave.Sine, 0.6f);
        }
        Tone(b, 0f, 2f, 1000f, 50f, Wave.Square, 0.12f, decay: 0.8f, duty: 0.25f, vibratoHz: 12f, vibratoDepth: 0.1f);
        Noise(b, 1.8f, 1.5f, 1f, 0.7f, 0.02f, decay: 2.5f);
        Tone(b, 1.8f, 1.3f, 90f, 25f, Wave.Sine, 0.9f, decay: 1.5f);
        Crush(b, 16);
        return b;
    }

    // =====================================================================
    // MUSIC (seamless loops)
    // =====================================================================

    static int[] Triad(int root, bool minor) => new[] { root, root + (minor ? 3 : 4), root + 7, root + 12 };

    static void Kick(float[] b, float t, float volume)
    {
        Tone(b, t, 0.18f, 150f, 40f, Wave.Sine, volume, decay: 1.2f, wrap: true);
        Noise(b, t, 0.012f, volume * 0.3f, 1f, 1f, wrap: true);
    }

    static void Snare(float[] b, float t, float volume)
    {
        Noise(b, t, 0.16f, volume, 0.7f, 0.4f, decay: 1.8f, wrap: true);
        Tone(b, t, 0.08f, 220f, 180f, Wave.Triangle, volume * 0.5f, wrap: true);
    }

    static void Hat(float[] b, float t, float volume)
    {
        Noise(b, t, 0.04f, volume, 0.3f, 0.3f, decay: 3f, highpass: true, wrap: true);
    }

    // Calm, spacey loop for the main menu: Am - F - C - G - Am - F - C - E
    static float[] MenuMusic()
    {
        const float bpm = 84f;
        float step = 60f / bpm / 4f, bar = step * 16f;
        int[] roots = { 57, 53, 48, 55, 57, 53, 48, 52 };
        bool[] minor = { true, false, false, false, true, false, false, false };
        var b = Buffer(bar * roots.Length);
        var pad = Buffer(bar * roots.Length);

        for (int c = 0; c < roots.Length; c++)
        {
            float t0 = c * bar;
            int[] chord = Triad(roots[c] < 52 ? roots[c] + 12 : roots[c], minor[c]);

            for (int k = 0; k < 3; k++)
                foreach (float detune in new[] { 0.997f, 1f, 1.003f })
                {
                    float f = Midi(chord[k]) * detune;
                    Tone(pad, t0, bar * 1.1f, f, f, Wave.Saw, 0.07f, attack: 0.6f, decay: 0.8f, wrap: true);
                }

            Tone(b, t0, bar * 0.5f, Midi(roots[c] - 24), Midi(roots[c] - 24), Wave.Sine, 0.5f, attack: 0.02f, decay: 1f, wrap: true);
            Tone(b, t0 + bar * 0.5f, bar * 0.5f, Midi(roots[c] - 12), Midi(roots[c] - 12), Wave.Sine, 0.3f, attack: 0.02f, decay: 1f, wrap: true);

            int[] arp = { 0, 1, 2, 1, 3, 2, 1, 2 };
            for (int s = 0; s < 8; s++)
            {
                float f = Midi(chord[arp[s]] + 12);
                Tone(b, t0 + s * step * 2f, step * 3f, f, f, Wave.Triangle, 0.16f, decay: 2f, wrap: true);
            }

            Kick(b, t0, 0.35f);
            for (int s = 2; s < 16; s += 4) Hat(b, t0 + s * step, 0.06f);
        }

        LowPass(pad, 0.08f);
        for (int i = 0; i < b.Length; i++) b[i] += pad[i] * 1.6f;
        return b;
    }

    // Driving loop for the server rooms: Am - Am - F - G - Am - Am - F - E
    static float[] LevelMusic()
    {
        const float bpm = 120f;
        float step = 60f / bpm / 4f, bar = step * 16f;
        int[] roots = { 57, 57, 53, 55, 57, 57, 53, 52 };
        bool[] minor = { true, true, false, false, true, true, false, false };
        var b = Buffer(bar * roots.Length);

        for (int c = 0; c < roots.Length; c++)
        {
            float t0 = c * bar;
            int[] chord = Triad(roots[c], minor[c]);

            // Bass: octave-jumping 8th notes
            for (int s = 0; s < 8; s++)
            {
                float f = Midi(roots[c] - (s % 2 == 0 ? 24 : 12));
                Tone(b, t0 + s * step * 2f, step * 1.8f, f, f, Wave.Triangle, 0.5f, decay: 1f, wrap: true);
                Tone(b, t0 + s * step * 2f, step * 1.5f, f, f, Wave.Square, 0.1f, decay: 1.5f, duty: 0.25f, wrap: true);
            }

            // Arpeggio: 16th notes
            for (int s = 0; s < 16; s++)
            {
                float f = Midi(chord[s % 4] + 12);
                Tone(b, t0 + s * step, step * 0.9f, f, f, Wave.Square, 0.1f, decay: 1f, duty: 0.125f, wrap: true);
            }

            // Lead melody in the second half
            if (c >= 4)
            {
                int[] melody = { chord[2] + 12, chord[1] + 12, chord[0] + 12, chord[1] + 12 };
                if (c == 7) melody = new[] { chord[0] + 12, chord[1] + 12, chord[2] + 12, chord[3] + 12 };
                for (int q = 0; q < 4; q++)
                {
                    float f = Midi(melody[q]);
                    Tone(b, t0 + q * step * 4f, step * 3.6f, f, f, Wave.Square, 0.14f, attack: 0.01f, decay: 0.8f, vibratoHz: 5f, vibratoDepth: 0.01f, wrap: true);
                }
            }

            for (int s = 0; s < 16; s += 4) Kick(b, t0 + s * step, 0.8f);
            Snare(b, t0 + 4 * step, 0.45f);
            Snare(b, t0 + 12 * step, 0.45f);
            for (int s = 0; s < 16; s++) Hat(b, t0 + s * step, s % 4 == 2 ? 0.16f : 0.06f);
        }
        return b;
    }

    // Fast, aggressive loop for the Corrupted Core: Dm - Dm - Bb - A - Dm - Dm - Bb - A
    static float[] BossMusic()
    {
        const float bpm = 150f;
        float step = 60f / bpm / 4f, bar = step * 16f;
        int[] roots = { 50, 50, 46, 45, 50, 50, 46, 45 };
        bool[] minor = { true, true, false, false, true, true, false, false };
        var b = Buffer(bar * roots.Length);

        for (int c = 0; c < roots.Length; c++)
        {
            float t0 = c * bar;
            int[] chord = Triad(roots[c], minor[c]);

            // Pounding 16th-note saw bass
            for (int s = 0; s < 16; s++)
            {
                float f = Midi(roots[c] - (s % 4 == 3 ? 12 : 24));
                Tone(b, t0 + s * step, step * 0.85f, f, f, Wave.Saw, 0.28f, decay: 0.8f, wrap: true);
                Tone(b, t0 + s * step, step * 0.85f, f * 1.005f, f * 1.005f, Wave.Square, 0.08f, decay: 1f, wrap: true);
            }

            // High arpeggio
            int[] arp = { 3, 2, 1, 2, 3, 2, 0, 2 };
            for (int s = 0; s < 16; s++)
            {
                float f = Midi(chord[arp[s % 8]] + 12);
                Tone(b, t0 + s * step, step * 0.8f, f, f, Wave.Square, 0.07f, decay: 1.5f, duty: 0.25f, wrap: true);
            }

            // Ominous lead in the second half
            if (c >= 4)
            {
                int[] melody = { chord[0] + 12, chord[0] + 12, chord[1] + 12, chord[0] + 12, chord[2] + 12, chord[1] + 12, chord[0] + 12, chord[0] + 11 };
                for (int e = 0; e < 8; e++)
                {
                    float f = Midi(melody[e]);
                    Tone(b, t0 + e * step * 2f, step * 1.8f, f, f, Wave.Saw, 0.12f, attack: 0.01f, decay: 0.7f, vibratoHz: 6f, vibratoDepth: 0.012f, wrap: true);
                }
            }

            foreach (int s in new[] { 0, 4, 8, 10, 12 }) Kick(b, t0 + s * step, 0.9f);
            Snare(b, t0 + 4 * step, 0.55f);
            Snare(b, t0 + 12 * step, 0.55f);
            if (c % 4 == 3) { Snare(b, t0 + 14 * step, 0.35f); Snare(b, t0 + 15 * step, 0.4f); }
            for (int s = 0; s < 16; s++) Hat(b, t0 + s * step, s % 2 == 0 ? 0.12f : 0.06f);
        }
        return b;
    }
}
