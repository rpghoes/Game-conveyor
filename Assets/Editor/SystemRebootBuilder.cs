using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

// EDITOR-ONLY TOOL (not part of the game).
// Builds the SYSTEM-REBOOT prototype from the art in Assets/Art:
//   SYSTEM-REBOOT > 1. Setup Project   -> tags/layers, sprite import + slicing, tiles, animations, prefabs
//   SYSTEM-REBOOT > 2. Build All Scenes -> MainMenu + Room_01..Room_10 and the Build Profiles scene list
//   SYSTEM-REBOOT > 3. Generate & Assign Sounds -> see SoundGenerator.cs (safe to re-run, does not touch rooms)
// Re-running step 2 OVERWRITES the room scenes, so once the team starts editing rooms by hand,
// don't run it again (or delete this file).
public static class SystemRebootBuilder
{
    const int PPU = 32;
    const int GroundLayer = 6, EnemyLayer = 7, PlayerLayer = 8, PlayerAttackLayer = 9;

    const string ArtRobot = "Assets/Art/Robot/";
    const string ArtEnv = "Assets/Art/Environment/";
    const string ArtBoss = "Assets/Art/Boss/";
    const string ArtSentinel = "Assets/Art/Sentinel/";
    const string TileFolder = "Assets/Art/Environment/Tiles/";

    // Sorting orders
    const int SortBackground = -20, SortDecor = -10, SortTiles = 0, SortHazards = 2, SortProps = 3, SortEnemies = 5, SortBoss = 6, SortProjectiles = 8, SortPlayer = 10;

    [MenuItem("SYSTEM-REBOOT/1. Setup Project (art, animations, prefabs)")]
    public static void SetupProject()
    {
        SetupTagsAndLayers();
        ImportArt();
        CreateTiles();
        CreateAnimations();
        SoundGenerator.GenerateClips();
        CreatePrefabs();
        ProgressionSettingsCreator.EnsureAsset();
        AssetDatabase.SaveAssets();
        Debug.Log("SYSTEM-REBOOT: project setup complete");
    }

    [MenuItem("SYSTEM-REBOOT/2. Build All Scenes")]
    public static void BuildAllScenes()
    {
        CleanSampleScene();
        var scenePaths = new List<string> { BuildMainMenu() };
        foreach (Room room in DefineRooms()) scenePaths.Add(BuildRoom(room));
        EditorBuildSettings.scenes = scenePaths.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
        EditorSceneManager.OpenScene(scenePaths[1]);
        Debug.Log("SYSTEM-REBOOT: built " + scenePaths.Count + " scenes");
    }

    // =====================================================================
    // TAGS, LAYERS, PHYSICS
    // =====================================================================

    static void SetupTagsAndLayers()
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty tags = tagManager.FindProperty("tags");
        foreach (string tag in new[] { "Enemy", "Terminal", "Checkpoint", "Hazard", "Boss", "DeathZone" })
        {
            bool exists = false;
            for (int i = 0; i < tags.arraySize; i++) if (tags.GetArrayElementAtIndex(i).stringValue == tag) exists = true;
            if (exists) continue;
            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
        }

        SerializedProperty layers = tagManager.FindProperty("layers");
        layers.GetArrayElementAtIndex(GroundLayer).stringValue = "Ground";
        layers.GetArrayElementAtIndex(EnemyLayer).stringValue = "Enemy";
        layers.GetArrayElementAtIndex(PlayerLayer).stringValue = "Player";
        layers.GetArrayElementAtIndex(PlayerAttackLayer).stringValue = "PlayerAttack";
        tagManager.ApplyModifiedProperties();

        // Player and enemies pass through each other (damage is handled by scripts),
        // enemies don't push each other, and the attack hitbox never collides physically.
        Physics2D.IgnoreLayerCollision(PlayerLayer, EnemyLayer, true);
        Physics2D.IgnoreLayerCollision(EnemyLayer, EnemyLayer, true);
        for (int i = 0; i < 32; i++) Physics2D.IgnoreLayerCollision(PlayerAttackLayer, i, true);
    }

    // =====================================================================
    // ART IMPORT (pixel-art settings + sprite sheet slicing)
    // =====================================================================

    static readonly Vector2 RobotPivot = new Vector2(0.5f, 4f / 64f);   // feet of the robot
    static readonly Vector2 BugPivot = new Vector2(0.5f, 7f / 64f);     // feet of the virus bug
    static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
    static readonly Vector2 Bottom = new Vector2(0.5f, 0f);
    static readonly Vector2 SentinelPivot = new Vector2(0.5f, 2f / 112f);   // feet of the Core Sentinel (128x112 frames)

    // Core Sentinel sheets (final boss): name -> frame count. All frames are 128x112, art facing right.
    static readonly (string name, int frames, float fps, bool loop)[] SentinelSheets =
    {
        ("Idle", 8, 8f, true), ("Walk", 6, 8f, true), ("Laser", 5, 8f, false), ("Slash", 8, 14f, false),
        ("Missile", 10, 12f, false), ("Slam", 9, 12f, false), ("Dash", 9, 14f, true), ("Hurt", 6, 16f, false), ("Death", 11, 8f, false),
    };

    static void ImportArt()
    {
        ConfigureSprite(ArtRobot + "Robot.png", RobotPivot, 1);
        ConfigureSprite(ArtRobot + "Robot_Idle.png", RobotPivot, 8);
        ConfigureSprite(ArtRobot + "Robot_Attack.png", RobotPivot, 8);
        ConfigureSprite(ArtRobot + "Robot_Run.png", RobotPivot, 8);
        ConfigureSprite(ArtRobot + "Robot_Death.png", RobotPivot, 8);
        ConfigureSprite("Assets/Art/VirusBug/VirusBug.png", BugPivot, 1);
        ConfigureSprite("Assets/Art/ScannerDrone/ScannerDrone.png", Center, 1);

        foreach (string tile in new[] { "Tile_GroundSurface", "Tile_GroundFill", "Tile_Platform", "Tile_Electric", "Tile_Corrupted", "Tile_Background", "Vent" })
            ConfigureSprite(ArtEnv + tile + ".png", Center, 1);
        foreach (string prop in new[] { "Terminal_Off", "Terminal_On", "Checkpoint_Off", "Checkpoint_On", "ExitDoor", "ServerRack" })
            ConfigureSprite(ArtEnv + prop + ".png", Bottom, 1);
        ConfigureSprite(ArtBoss + "Boss_Placeholder.png", Center, 1);
        ConfigureSprite(ArtBoss + "BossProjectile.png", Center, 1);
        foreach (var sheet in SentinelSheets) ConfigureSprite(ArtSentinel + "Sentinel_" + sheet.name + ".png", SentinelPivot, sheet.frames);
        ConfigureSprite(ArtSentinel + "Sentinel_Rocket.png", Center, 5);
    }

    static void ConfigureSprite(string path, Vector2 pivot, int frameCount)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null) throw new System.Exception("Missing art file: " + path);

        importer.textureType = TextureImporterType.Sprite;
        importer.spritePixelsPerUnit = PPU;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMode = (int)(frameCount > 1 ? SpriteImportMode.Multiple : SpriteImportMode.Single);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = pivot;
        settings.spriteGenerateFallbackPhysicsShape = true;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        if (frameCount > 1) SliceSheet(importer, frameCount, pivot);
    }

    // Cuts a horizontal sprite sheet into equal frames (64x64 for the robot sheets)
    static void SliceSheet(TextureImporter importer, int frameCount, Vector2 pivot)
    {
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        ISpriteEditorDataProvider dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
        dataProvider.InitSpriteEditorDataProvider();

        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(importer.assetPath);
        int frameWidth = texture.width / frameCount;
        string baseName = Path.GetFileNameWithoutExtension(importer.assetPath);

        // Keep existing slices (and their IDs) if the sheet is already sliced correctly
        SpriteRect[] existing = dataProvider.GetSpriteRects();
        if (existing.Length == frameCount && existing.All(r => (int)r.rect.width == frameWidth)) return;

        var rects = new List<SpriteRect>();
        for (int i = 0; i < frameCount; i++)
        {
            rects.Add(new SpriteRect
            {
                name = baseName + "_" + i,
                rect = new Rect(i * frameWidth, 0, frameWidth, texture.height),
                alignment = SpriteAlignment.Custom,
                pivot = pivot,
                spriteID = GUID.Generate()
            });
        }
        dataProvider.SetSpriteRects(rects.ToArray());
        var nameIds = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        nameIds.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)).ToList());
        dataProvider.Apply();
        importer.SaveAndReimport();
    }

    static Sprite LoadSprite(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().First();
    }

    static Sprite[] LoadFrames(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
            .OrderBy(s => int.Parse(s.name.Substring(s.name.LastIndexOf('_') + 1))).ToArray();
    }

    // =====================================================================
    // TILES
    // =====================================================================

    static Tile tileSurface, tileFill, tilePlatform, tileElectric, tileCorrupted, tileBackground;

    static void CreateTiles()
    {
        EnsureFolder(TileFolder.TrimEnd('/'));
        tileSurface = MakeTile("Ground_Surface", "Tile_GroundSurface", Tile.ColliderType.Grid);
        tileFill = MakeTile("Ground_Fill", "Tile_GroundFill", Tile.ColliderType.Grid);
        tilePlatform = MakeTile("Platform_OneWay", "Tile_Platform", Tile.ColliderType.Sprite);
        tileElectric = MakeTile("Hazard_Electric", "Tile_Electric", Tile.ColliderType.Sprite);
        tileCorrupted = MakeTile("Hazard_Corrupted", "Tile_Corrupted", Tile.ColliderType.Grid);
        tileBackground = MakeTile("Background", "Tile_Background", Tile.ColliderType.None);
    }

    static Tile MakeTile(string tileName, string spriteName, Tile.ColliderType colliderType)
    {
        string path = TileFolder + tileName + ".asset";
        var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
        if (tile == null)
        {
            tile = ScriptableObject.CreateInstance<Tile>();
            AssetDatabase.CreateAsset(tile, path);
        }
        tile.sprite = LoadSprite(ArtEnv + spriteName + ".png");
        tile.colliderType = colliderType;
        EditorUtility.SetDirty(tile);
        return tile;
    }

    static void LoadTiles()
    {
        if (tileSurface != null) return;
        tileSurface = AssetDatabase.LoadAssetAtPath<Tile>(TileFolder + "Ground_Surface.asset");
        tileFill = AssetDatabase.LoadAssetAtPath<Tile>(TileFolder + "Ground_Fill.asset");
        tilePlatform = AssetDatabase.LoadAssetAtPath<Tile>(TileFolder + "Platform_OneWay.asset");
        tileElectric = AssetDatabase.LoadAssetAtPath<Tile>(TileFolder + "Hazard_Electric.asset");
        tileCorrupted = AssetDatabase.LoadAssetAtPath<Tile>(TileFolder + "Hazard_Corrupted.asset");
        tileBackground = AssetDatabase.LoadAssetAtPath<Tile>(TileFolder + "Background.asset");
    }

    // =====================================================================
    // ANIMATIONS
    // =====================================================================

    static void CreateAnimations()
    {
        // ---------- Robot ----------
        Sprite robot = LoadSprite(ArtRobot + "Robot.png");
        Sprite[] idleFrames = LoadFrames(ArtRobot + "Robot_Idle.png");
        Sprite[] attackFrames = LoadFrames(ArtRobot + "Robot_Attack.png");
        Sprite[] runFrames = LoadFrames(ArtRobot + "Robot_Run.png");
        Sprite[] deathFrames = LoadFrames(ArtRobot + "Robot_Death.png");
        string robotDir = "Assets/Animations/Robot/";

        AnimationClip idle = MakeClip(robotDir + "Robot_Idle.anim", idleFrames, 8f, true);
        AnimationClip attack = MakeClip(robotDir + "Robot_Attack.anim", attackFrames, 16f, false);
        // Walk cycle (8 frames at 14 fps) and death: visor turns red, glitches, the robot collapses (8 frames at 10 fps)
        AnimationClip run = MakeClip(robotDir + "Robot_Run.anim", runFrames, 14f, true);
        AnimationClip death = MakeClip(robotDir + "Robot_Death.anim", deathFrames, 10f, false);
        // PLACEHOLDERS: these show the base Robot image until real frames are added to the clips
        AnimationClip jump = MakeClip(robotDir + "Robot_Jump.anim", new[] { robot }, 4f, true);
        AnimationClip doubleJump = MakeClip(robotDir + "Robot_DoubleJump.anim", new[] { robot }, 4f, false);
        AnimationClip hurt = MakeClip(robotDir + "Robot_Hurt.anim", new[] { robot }, 4f, false);

        AnimatorController rc = NewController(robotDir + "Robot.controller");
        rc.AddParameter("Speed", AnimatorControllerParameterType.Float);
        rc.AddParameter(new AnimatorControllerParameter { name = "Grounded", type = AnimatorControllerParameterType.Bool, defaultBool = true });
        rc.AddParameter("VerticalVelocity", AnimatorControllerParameterType.Float);
        rc.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        rc.AddParameter("DoubleJump", AnimatorControllerParameterType.Trigger);
        rc.AddParameter("Hurt", AnimatorControllerParameterType.Trigger);
        rc.AddParameter("Dead", AnimatorControllerParameterType.Bool);

        AnimatorStateMachine sm = rc.layers[0].stateMachine;
        AnimatorState sIdle = AddState(sm, "Idle", idle, 300, 0);
        AnimatorState sRun = AddState(sm, "Run", run, 550, 0);
        AnimatorState sJump = AddState(sm, "Jump", jump, 420, 120);
        AnimatorState sDouble = AddState(sm, "DoubleJump", doubleJump, 680, 120);
        AnimatorState sAttack = AddState(sm, "Attack", attack, 420, -120);
        AnimatorState sHurt = AddState(sm, "Hurt", hurt, 680, -120);
        AnimatorState sDeath = AddState(sm, "Death", death, 150, -120);
        sm.defaultState = sIdle;

        Quick(sIdle.AddTransition(sRun)).AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
        Quick(sRun.AddTransition(sIdle)).AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
        Quick(sIdle.AddTransition(sJump)).AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
        Quick(sRun.AddTransition(sJump)).AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
        Quick(sJump.AddTransition(sIdle)).AddCondition(AnimatorConditionMode.If, 0, "Grounded");
        Quick(sDouble.AddTransition(sIdle)).AddCondition(AnimatorConditionMode.If, 0, "Grounded");
        AfterClip(sDouble.AddTransition(sJump));
        AfterClip(sAttack.AddTransition(sIdle));
        AfterClip(sHurt.AddTransition(sIdle));
        Quick(sDeath.AddTransition(sIdle)).AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
        // Any State transitions (first = highest priority)
        AnyState(sm, sDeath).AddCondition(AnimatorConditionMode.If, 0, "Dead");
        AnyState(sm, sHurt).AddCondition(AnimatorConditionMode.If, 0, "Hurt");
        AnyState(sm, sAttack).AddCondition(AnimatorConditionMode.If, 0, "Attack");
        AnyState(sm, sDouble).AddCondition(AnimatorConditionMode.If, 0, "DoubleJump");

        // ---------- Enemies (single provided image each; other states ready for future frames) ----------
        MakeEnemyAnimations("VirusBug", LoadSprite("Assets/Art/VirusBug/VirusBug.png"), "Walk");
        MakeEnemyAnimations("ScannerDrone", LoadSprite("Assets/Art/ScannerDrone/ScannerDrone.png"), "Fly");

        // ---------- Boss (placeholder art) ----------
        Sprite bossSprite = LoadSprite(ArtBoss + "Boss_Placeholder.png");
        string bossDir = "Assets/Animations/Boss/";
        AnimatorController bc = NewController(bossDir + "Boss.controller");
        bc.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        bc.AddParameter("Hurt", AnimatorControllerParameterType.Trigger);
        bc.AddParameter("Dead", AnimatorControllerParameterType.Bool);
        bc.AddParameter("Vulnerable", AnimatorControllerParameterType.Bool);
        AnimatorStateMachine bsm = bc.layers[0].stateMachine;
        AnimatorState bIdle = AddState(bsm, "Idle", MakeClip(bossDir + "Boss_Idle.anim", new[] { bossSprite }, 4f, true), 300, 0);
        AnimatorState bAttack = AddState(bsm, "Attack", MakeClip(bossDir + "Boss_Attack.anim", new[] { bossSprite }, 4f, false), 550, -120);
        AnimatorState bVuln = AddState(bsm, "Vulnerable", MakeClip(bossDir + "Boss_Vulnerable.anim", new[] { bossSprite }, 4f, true), 550, 0);
        AnimatorState bHurt = AddState(bsm, "Hurt", MakeClip(bossDir + "Boss_Hurt.anim", new[] { bossSprite }, 4f, false), 550, 120);
        AnimatorState bDeath = AddState(bsm, "Death", MakeClip(bossDir + "Boss_Death.anim", new[] { bossSprite }, 4f, false), 150, -120);
        bsm.defaultState = bIdle;
        Quick(bIdle.AddTransition(bVuln)).AddCondition(AnimatorConditionMode.If, 0, "Vulnerable");
        Quick(bVuln.AddTransition(bIdle)).AddCondition(AnimatorConditionMode.IfNot, 0, "Vulnerable");
        AfterClip(bAttack.AddTransition(bIdle));
        AfterClip(bHurt.AddTransition(bIdle));
        AnyState(bsm, bDeath).AddCondition(AnimatorConditionMode.If, 0, "Dead");
        AnyState(bsm, bHurt).AddCondition(AnimatorConditionMode.If, 0, "Hurt");
        AnyState(bsm, bAttack).AddCondition(AnimatorConditionMode.If, 0, "Attack");

        // ---------- Core Sentinel (final boss) ----------
        // SentinelBossController plays the attack states directly (Animator.Play), so they have no
        // entry transitions. Hurt only plays from Idle/Walk so hits never cancel an attack.
        string sentinelDir = "Assets/Animations/Sentinel/";
        EnsureFolder(sentinelDir.TrimEnd('/'));
        AnimatorController sc = NewController(sentinelDir + "Sentinel.controller");
        sc.AddParameter("Hurt", AnimatorControllerParameterType.Trigger);
        sc.AddParameter("Dead", AnimatorControllerParameterType.Bool);
        AnimatorStateMachine ssm = sc.layers[0].stateMachine;
        var states = new Dictionary<string, AnimatorState>();
        for (int i = 0; i < SentinelSheets.Length; i++)
        {
            var sheet = SentinelSheets[i];
            AnimationClip clip = MakeClip(sentinelDir + "Sentinel_" + sheet.name + ".anim",
                LoadFrames(ArtSentinel + "Sentinel_" + sheet.name + ".png"), sheet.fps, sheet.loop);
            states[sheet.name] = AddState(ssm, sheet.name, clip, 300 + (i % 3) * 230, -120 + (i / 3) * 110);
        }
        ssm.defaultState = states["Idle"];
        Quick(states["Idle"].AddTransition(states["Hurt"])).AddCondition(AnimatorConditionMode.If, 0, "Hurt");
        Quick(states["Walk"].AddTransition(states["Hurt"])).AddCondition(AnimatorConditionMode.If, 0, "Hurt");
        AfterClip(states["Hurt"].AddTransition(states["Idle"]));
        AnyState(ssm, states["Death"]).AddCondition(AnimatorConditionMode.If, 0, "Dead");
    }

    static void MakeEnemyAnimations(string enemyName, Sprite sprite, string moveStateName)
    {
        string dir = "Assets/Animations/" + enemyName + "/";
        AnimatorController c = NewController(dir + enemyName + ".controller");
        c.AddParameter("Speed", AnimatorControllerParameterType.Float);
        c.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        c.AddParameter("Hurt", AnimatorControllerParameterType.Trigger);
        c.AddParameter("Dead", AnimatorControllerParameterType.Bool);
        AnimatorStateMachine sm = c.layers[0].stateMachine;

        AnimatorState idle = AddState(sm, "Idle", MakeClip(dir + enemyName + "_Idle.anim", new[] { sprite }, 4f, true), 300, 0);
        AnimatorState move = AddState(sm, moveStateName, MakeClip(dir + enemyName + "_" + moveStateName + ".anim", new[] { sprite }, 4f, true), 550, 0);
        AnimatorState attack = AddState(sm, "Attack", MakeClip(dir + enemyName + "_Attack.anim", new[] { sprite }, 4f, false), 420, -120);
        AnimatorState hurt = AddState(sm, "Hurt", MakeClip(dir + enemyName + "_Hurt.anim", new[] { sprite }, 4f, false), 680, -120);
        AnimatorState death = AddState(sm, "Death", MakeClip(dir + enemyName + "_Death.anim", new[] { sprite }, 4f, false), 150, -120);
        sm.defaultState = idle;

        Quick(idle.AddTransition(move)).AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
        Quick(move.AddTransition(idle)).AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
        AfterClip(attack.AddTransition(idle));
        AfterClip(hurt.AddTransition(idle));
        AnyState(sm, death).AddCondition(AnimatorConditionMode.If, 0, "Dead");
        AnyState(sm, hurt).AddCondition(AnimatorConditionMode.If, 0, "Hurt");
        AnyState(sm, attack).AddCondition(AnimatorConditionMode.If, 0, "Attack");
    }

    // Sprite animation clip: one key per frame (Unity holds the last key for one frame,
    // so the clip length is frames / fps)
    static AnimationClip MakeClip(string path, Sprite[] frames, float fps, bool loop)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        bool isNew = clip == null;
        if (isNew) clip = new AnimationClip();

        clip.frameRate = fps;
        var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        var keys = new ObjectReferenceKeyframe[frames.Length];
        for (int i = 0; i < frames.Length; i++) keys[i] = new ObjectReferenceKeyframe { time = i / fps, value = frames[i] };
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        if (isNew) AssetDatabase.CreateAsset(clip, path);
        else EditorUtility.SetDirty(clip);
        return clip;
    }

    static AnimatorController NewController(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);
        return AnimatorController.CreateAnimatorControllerAtPath(path);
    }

    static AnimatorState AddState(AnimatorStateMachine sm, string stateName, Motion motion, float x, float y)
    {
        AnimatorState state = sm.AddState(stateName, new Vector3(x, y, 0));
        state.motion = motion;
        return state;
    }

    static AnimatorStateTransition Quick(AnimatorStateTransition t)
    {
        t.hasExitTime = false;
        t.duration = 0f;
        return t;
    }

    static AnimatorStateTransition AfterClip(AnimatorStateTransition t)
    {
        t.hasExitTime = true;
        t.exitTime = 1f;
        t.duration = 0f;
        return t;
    }

    static AnimatorStateTransition AnyState(AnimatorStateMachine sm, AnimatorState target)
    {
        AnimatorStateTransition t = sm.AddAnyStateTransition(target);
        t.hasExitTime = false;
        t.duration = 0f;
        t.canTransitionToSelf = false;
        return t;
    }

    // =====================================================================
    // PREFABS
    // =====================================================================

    const string PlayerPrefabPath = "Assets/Prefabs/Player/Player.prefab";
    const string BugPrefabPath = "Assets/Prefabs/Enemies/VirusBug.prefab";
    const string DronePrefabPath = "Assets/Prefabs/Enemies/ScannerDrone.prefab";
    const string TerminalPrefabPath = "Assets/Prefabs/Terminals/Terminal.prefab";
    const string CheckpointPrefabPath = "Assets/Prefabs/Checkpoints/Checkpoint.prefab";
    const string ExitPrefabPath = "Assets/Prefabs/Checkpoints/LevelExit.prefab";
    const string BossPrefabPath = "Assets/Prefabs/Boss/Boss.prefab";
    const string ProjectilePrefabPath = "Assets/Prefabs/Boss/BossProjectile.prefab";
    const string SentinelPrefabPath = "Assets/Prefabs/Boss/Boss_Sentinel.prefab";
    const string HudPrefabPath = "Assets/Prefabs/UI/HUD.prefab";

    static void CreatePrefabs()
    {
        CreatePlayerPrefab();
        CreateVirusBugPrefab();
        CreateDronePrefab();
        CreateTerminalPrefab();
        CreateCheckpointPrefab();
        CreateExitPrefab();
        CreateProjectilePrefab();
        CreateBossPrefab();
        CreateSentinelPrefab();
        CreateHudPrefab();
    }

    static GameObject SavePrefab(GameObject root, string path)
    {
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static AudioSource AddSound(Transform audioRoot, string soundName, bool loop = false)
    {
        var go = new GameObject(soundName);
        go.transform.SetParent(audioRoot, false);
        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.clip = SoundGenerator.ClipFor(audioRoot.parent.name, soundName);
        source.loop = loop;
        return source;
    }

    static Transform AddAudioRoot(GameObject owner)
    {
        var audio = new GameObject("Audio");
        audio.transform.SetParent(owner.transform, false);
        return audio.transform;
    }

    static void CreatePlayerPrefab()
    {
        var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>("Assets/Prefabs/Player/PlayerNoFriction.physicsMaterial2D");
        if (material == null)
        {
            material = new PhysicsMaterial2D("PlayerNoFriction") { friction = 0f, bounciness = 0f };
            AssetDatabase.CreateAsset(material, "Assets/Prefabs/Player/PlayerNoFriction.physicsMaterial2D");
        }

        var player = new GameObject("Player") { tag = "Player", layer = PlayerLayer };
        var sr = player.AddComponent<SpriteRenderer>();
        sr.sprite = LoadFrames(ArtRobot + "Robot_Idle.png")[0];
        sr.sortingOrder = SortPlayer;

        var rb = player.AddComponent<Rigidbody2D>();
        rb.gravityScale = 4f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.sleepMode = RigidbodySleepMode2D.NeverSleep;

        var col = player.AddComponent<CapsuleCollider2D>();
        col.size = new Vector2(0.7f, 1.6f);
        col.offset = new Vector2(0f, 0.8f);
        col.sharedMaterial = material;

        player.AddComponent<Animator>().runtimeAnimatorController =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Robot/Robot.controller");

        var groundCheck = new GameObject("GroundCheck");
        groundCheck.transform.SetParent(player.transform, false);

        // Hitbox child. The Robot art faces LEFT, so "in front" is negative local X.
        var hitboxObject = new GameObject("AttackHitbox") { layer = PlayerAttackLayer };
        hitboxObject.transform.SetParent(player.transform, false);
        hitboxObject.transform.localPosition = new Vector3(-0.8f, 0.85f, 0f);
        var hitbox = hitboxObject.AddComponent<BoxCollider2D>();
        hitbox.isTrigger = true;
        hitbox.size = new Vector2(1.2f, 1.1f);
        hitboxObject.SetActive(false);

        Transform audio = AddAudioRoot(player);

        var controller = player.AddComponent<PlayerController>();
        controller.groundCheck = groundCheck.transform;
        controller.groundLayer = 1 << GroundLayer;
        controller.jumpSound = AddSound(audio, "SFX_Jump");

        var combat = player.AddComponent<PlayerCombat>();
        combat.attackHitbox = hitbox;
        combat.enemyLayer = 1 << EnemyLayer;
        combat.attackSound = AddSound(audio, "SFX_Attack");

        var health = player.AddComponent<PlayerHealth>();
        health.hurtSound = AddSound(audio, "SFX_Hurt");
        health.deathSound = AddSound(audio, "SFX_Death");

        var ultimate = player.AddComponent<PlayerUltimate>();
        ultimate.enemyLayer = 1 << EnemyLayer;
        ultimate.chargeSound = AddSound(audio, "SFX_UltimateCharge");
        ultimate.blastSound = AddSound(audio, "SFX_UltimateBlast");

        SavePrefab(player, PlayerPrefabPath);
    }

    static GameObject NewEnemyBase(string name, Sprite sprite, string controllerPath, int maxHealth)
    {
        var enemy = new GameObject(name) { tag = "Enemy", layer = EnemyLayer };
        var sr = enemy.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = SortEnemies;
        enemy.AddComponent<Animator>().runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);

        Transform audio = AddAudioRoot(enemy);
        var health = enemy.AddComponent<EnemyHealth>();
        health.maxHealth = maxHealth;
        health.hitSound = AddSound(audio, "SFX_EnemyHit");
        health.deathSound = AddSound(audio, "SFX_EnemyDeath");
        return enemy;
    }

    static void CreateVirusBugPrefab()
    {
        GameObject bug = NewEnemyBase("VirusBug", LoadSprite("Assets/Art/VirusBug/VirusBug.png"), "Assets/Animations/VirusBug/VirusBug.controller", 3);

        var rb = bug.AddComponent<Rigidbody2D>();
        rb.gravityScale = 4f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        var col = bug.AddComponent<BoxCollider2D>();
        col.size = new Vector2(1.1f, 1.0f);
        col.offset = new Vector2(0f, 0.5f);

        var ai = bug.AddComponent<VirusBugAI>();
        ai.groundLayer = 1 << GroundLayer;
        ai.attackSound = AddSound(bug.transform.Find("Audio"), "SFX_Attack");
        ai.pointA = NewChild(bug, "PatrolPointA", new Vector3(-3f, 0f, 0f));
        ai.pointB = NewChild(bug, "PatrolPointB", new Vector3(3f, 0f, 0f));

        SavePrefab(bug, BugPrefabPath);
    }

    static void CreateDronePrefab()
    {
        GameObject drone = NewEnemyBase("ScannerDrone", LoadSprite("Assets/Art/ScannerDrone/ScannerDrone.png"), "Assets/Animations/ScannerDrone/ScannerDrone.controller", 2);

        var rb = drone.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.linearDamping = 3f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        var col = drone.AddComponent<CircleCollider2D>();
        col.radius = 0.5f;

        var ai = drone.AddComponent<ScannerDroneAI>();
        ai.attackSound = AddSound(drone.transform.Find("Audio"), "SFX_Attack");

        SavePrefab(drone, DronePrefabPath);
    }

    static Transform NewChild(GameObject parent, string name, Vector3 localPosition)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent.transform, false);
        child.transform.localPosition = localPosition;
        return child.transform;
    }

    static void CreateTerminalPrefab()
    {
        var terminal = new GameObject("Terminal") { tag = "Terminal" };
        var sr = terminal.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite(ArtEnv + "Terminal_Off.png");
        sr.sortingOrder = SortProps;
        var trigger = terminal.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(2.5f, 2.5f);
        trigger.offset = new Vector2(0f, 1f);

        var t = terminal.AddComponent<Terminal>();
        t.spriteRenderer = sr;
        t.offSprite = sr.sprite;
        t.onSprite = LoadSprite(ArtEnv + "Terminal_On.png");
        t.activateSound = AddSound(AddAudioRoot(terminal), "SFX_TerminalActivate");
        SavePrefab(terminal, TerminalPrefabPath);
    }

    static void CreateCheckpointPrefab()
    {
        var checkpoint = new GameObject("Checkpoint") { tag = "Checkpoint" };
        var sr = checkpoint.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite(ArtEnv + "Checkpoint_Off.png");
        sr.sortingOrder = SortProps;
        var trigger = checkpoint.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(1.2f, 3f);
        trigger.offset = new Vector2(0f, 1.5f);

        var c = checkpoint.AddComponent<Checkpoint>();
        c.spriteRenderer = sr;
        c.inactiveSprite = sr.sprite;
        c.activeSprite = LoadSprite(ArtEnv + "Checkpoint_On.png");
        c.activateSound = AddSound(AddAudioRoot(checkpoint), "SFX_Checkpoint");
        SavePrefab(checkpoint, CheckpointPrefabPath);
    }

    static void CreateExitPrefab()
    {
        var exit = new GameObject("LevelExit");
        var sr = exit.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite(ArtEnv + "ExitDoor.png");
        sr.sortingOrder = SortProps;
        var trigger = exit.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(1f, 2f);
        trigger.offset = new Vector2(0f, 1f);
        exit.AddComponent<LevelExit>().doorRenderer = sr;
        SavePrefab(exit, ExitPrefabPath);
    }

    static void CreateProjectilePrefab()
    {
        var projectile = new GameObject("BossProjectile") { tag = "Hazard" };
        var sr = projectile.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite(ArtBoss + "BossProjectile.png");
        sr.sortingOrder = SortProjectiles;
        projectile.transform.localScale = Vector3.one * 1.5f;
        var rb = projectile.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        var col = projectile.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.15f;
        SavePrefab(projectile, ProjectilePrefabPath);
    }

    static void CreateBossPrefab()
    {
        var boss = new GameObject("Boss_CorruptedCore") { tag = "Boss", layer = EnemyLayer };
        var sr = boss.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite(ArtBoss + "Boss_Placeholder.png");
        sr.sortingOrder = SortBoss;
        var rb = boss.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        var col = boss.AddComponent<CircleCollider2D>();
        col.radius = 1.4f;
        boss.AddComponent<Animator>().runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Boss/Boss.controller");

        Transform audio = AddAudioRoot(boss);
        var health = boss.AddComponent<EnemyHealth>();
        health.maxHealth = 24;
        health.receivesKnockback = false;
        health.destroyOnDeath = false;
        health.deathFadeTime = 2f;
        health.hitSound = AddSound(audio, "SFX_BossHit");

        var controller = boss.AddComponent<BossController>();
        controller.projectilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePrefabPath);
        controller.attackSound = AddSound(audio, "SFX_BossAttack");
        controller.deathSound = AddSound(audio, "SFX_BossDeath");
        SavePrefab(boss, BossPrefabPath);
    }

    // Final boss: a heavy walker that stays on the floor
    static void CreateSentinelPrefab()
    {
        var boss = new GameObject("Boss_Sentinel") { tag = "Boss", layer = EnemyLayer };
        var sr = boss.AddComponent<SpriteRenderer>();
        sr.sprite = LoadFrames(ArtSentinel + "Sentinel_Idle.png")[0];
        sr.sortingOrder = SortBoss;
        var rb = boss.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        var col = boss.AddComponent<BoxCollider2D>();
        col.size = new Vector2(2.6f, 2.6f);
        col.offset = new Vector2(0f, 1.3f);
        boss.AddComponent<Animator>().runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Sentinel/Sentinel.controller");

        Transform audio = AddAudioRoot(boss);
        var health = boss.AddComponent<EnemyHealth>();
        health.maxHealth = 40;
        health.receivesKnockback = false;
        health.destroyOnDeath = false;
        health.keepWreck = true;
        health.deathFadeTime = 1.6f;
        health.explosionColor = new Color(1f, 0.2f, 0.25f);
        health.hitSound = AddSound(audio, "SFX_BossHit");

        var controller = boss.AddComponent<SentinelBossController>();
        controller.rocketFrames = LoadFrames(ArtSentinel + "Sentinel_Rocket.png");
        controller.attackSound = AddSound(audio, "SFX_BossAttack");
        controller.laserSound = AddSound(audio, "SFX_BossLaser");
        controller.deathSound = AddSound(audio, "SFX_BossDeath");
        SavePrefab(boss, SentinelPrefabPath);
    }

    // ---------- HUD ----------

    static Font UIFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    static RectTransform NewUI(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        return rt;
    }

    static Text NewText(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size,
        string text, int fontSize, TextAnchor alignment, Color color)
    {
        RectTransform rt = NewUI(name, parent, anchor, pivot, position, size);
        var label = rt.gameObject.AddComponent<Text>();
        label.font = UIFont;
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = FontStyle.Bold;
        label.alignment = alignment;
        label.color = color;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        var outline = rt.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        outline.effectDistance = new Vector2(2f, -2f);
        return label;
    }

    static Button NewButton(string name, Transform parent, Vector2 position, string label)
    {
        RectTransform rt = NewUI(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, new Vector2(420f, 80f));
        var image = rt.gameObject.AddComponent<Image>();
        image.color = new Color(0.1f, 0.35f, 0.45f, 1f);
        var button = rt.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.6f, 1f, 1f);
        colors.selectedColor = new Color(0.6f, 1f, 1f);
        colors.pressedColor = new Color(0.3f, 0.7f, 0.8f);
        button.colors = colors;
        Text text = NewText("Label", rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, 80f),
            label, 40, TextAnchor.MiddleCenter, Color.white);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.sizeDelta = Vector2.zero;
        return button;
    }

    static GameObject NewCanvas(string name)
    {
        var canvasObject = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return canvasObject;
    }

    static void CreateHudPrefab()
    {
        GameObject hud = NewCanvas("HUD");
        Transform root = hud.transform;
        var ui = hud.AddComponent<UIManager>();
        Color cyan = new Color(0.45f, 0.95f, 1f);

        ui.healthText = NewText("HealthText", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -30f), new Vector2(400f, 60f),
            "HP: 3", 48, TextAnchor.UpperLeft, Color.white);
        ui.terminalText = NewText("TerminalText", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -95f), new Vector2(500f, 50f),
            "TERMINALS: 0/3", 38, TextAnchor.UpperLeft, cyan);
        ui.bannerText = NewText("BannerText", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1600f, 100f),
            "SYSTEM REBOOT COMPLETE", 72, TextAnchor.MiddleCenter, cyan);
        ui.messageText = NewText("MessageText", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 190f), new Vector2(1600f, 60f),
            "", 34, TextAnchor.MiddleCenter, Color.white);
        ui.promptText = NewText("PromptText", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(600f, 60f),
            "E - REBOOT", 46, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.3f));

        // Boss health bar
        RectTransform bar = NewUI("BossBar", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(1000f, 34f));
        bar.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.05f, 0.1f, 0.9f);
        RectTransform fill = NewUI("Fill", bar, Vector2.zero, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = new Vector2(4f, 4f);
        fill.offsetMax = new Vector2(-4f, -4f);
        fill.gameObject.AddComponent<Image>().color = new Color(0.9f, 0.15f, 0.35f);
        ui.bossBarRoot = bar.gameObject;
        ui.bossBarFill = fill;
        ui.bossNameText = NewText("BossName", bar, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(1000f, 44f),
            "CORRUPTED CORE", 34, TextAnchor.LowerCenter, new Color(1f, 0.5f, 0.6f));

        // Victory screen
        RectTransform panel = NewUI("VictoryPanel", root, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        panel.anchorMin = Vector2.zero;
        panel.anchorMax = Vector2.one;
        panel.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.03f, 0.07f, 0.92f);
        NewText("Title", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 260f), new Vector2(1600f, 120f),
            "SYSTEM CLEANED", 96, TextAnchor.MiddleCenter, cyan);
        NewText("Subtitle", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(1600f, 60f),
            "The infection is gone. The server is back online.", 38, TextAnchor.MiddleCenter, Color.white);
        NewText("Thanks", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 80f), new Vector2(1600f, 60f),
            "Thanks for playing SYSTEM-REBOOT", 32, TextAnchor.MiddleCenter, new Color(0.7f, 0.8f, 0.9f));
        Button menuButton = NewButton("MainMenuButton", panel, new Vector2(0f, -60f), "MAIN MENU");
        Button quitButton = NewButton("QuitButton", panel, new Vector2(0f, -160f), "QUIT");
        UnityEventTools.AddPersistentListener(menuButton.onClick, ui.LoadMainMenu);
        UnityEventTools.AddPersistentListener(quitButton.onClick, ui.QuitGame);
        ui.victoryPanel = panel.gameObject;
        ui.victoryFirstButton = menuButton.gameObject;

        SavePrefab(hud, HudPrefabPath);
    }

    // =====================================================================
    // ROOMS
    // =====================================================================

    // A room is a grid of cells: '#' solid, '=' one-way platform, '^' electric hazard, '~' corrupted hazard.
    // Things (player spawn, enemies, terminals...) are placed on top of cells. (0,0) is the bottom-left.
    class Thing
    {
        public char kind; public int x, y, width;
    }

    class Room
    {
        public string scene, title, hint, next;
        public int width, height, requiredTerminals;
        // Boss arena: left/right limits of the boss movement (-1 = the whole room)
        public int arenaMinX = 3, arenaMaxX = -1;
        // 'X' boss is a mini-boss: it only unlocks the exit instead of ending the game
        public bool miniBoss;
        public Color background, tint;
        public char[,] cells;
        public List<Thing> things = new List<Thing>();

        public Room(string scene, int width, int height)
        {
            this.scene = scene;
            this.width = width;
            this.height = height;
            cells = new char[width, height];
            Fill('.', 0, 0, width, height);
            // Outer walls and ceiling (floors are added per room so pits are possible)
            Fill('#', 0, 0, 1, height);
            Fill('#', width - 1, 0, 1, height);
            Fill('#', 0, height - 1, width, 1);
        }

        public Room Fill(char c, int x, int y, int w, int h)
        {
            for (int i = x; i < x + w; i++)
                for (int j = y; j < y + h; j++)
                    if (i >= 0 && j >= 0 && i < width && j < height) cells[i, j] = c;
            return this;
        }

        public Room Solid(int x, int y, int w, int h) { return Fill('#', x, y, w, h); }
        public Room Platform(int x, int y, int w) { return Fill('=', x, y, w, 1); }
        public Room Electric(int x, int y, int w) { return Fill('^', x, y, w, 1); }
        public Room Corrupt(int x, int y, int w, int h) { return Fill('~', x, y, w, h); }
        public Room Add(char kind, int x, int y, int w = 1) { things.Add(new Thing { kind = kind, x = x, y = y, width = w }); return this; }

        public char Cell(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return '.';
            return cells[x, y];
        }
    }

    static List<Room> DefineRooms()
    {
        var rooms = new List<Room>();
        Room r;

        // ---------------- 01 ENTRANCE: learn to move, jump, double jump, fight ----------------
        r = new Room("Room_01_Entrance", 72, 18)
        {
            title = "SECTOR 01 - ENTRANCE",
            hint = "A/D: MOVE    SPACE: JUMP (PRESS AGAIN IN THE AIR TO DOUBLE JUMP)    J: ATTACK    E: INTERACT",
            next = "Room_02_ServerHall",
            background = new Color(0.05f, 0.07f, 0.12f), tint = new Color(0.8f, 0.9f, 1f)
        };
        r.Solid(0, 0, 24, 2).Solid(12, 2, 4, 2)          // floor + first step
         .Solid(28, 0, 20, 2)                            // after the first pit
         .Solid(48, 0, 6, 7)                             // tall wall: needs a double jump
         .Solid(54, 0, 18, 2).Electric(59, 2, 3);        // first electric hazard
        r.Add('P', 2, 2).Add('r', 5, 2).Add('r', 7, 2).Add('v', 20, 8)
         .Add('C', 29, 2).Add('B', 40, 2).Add('r', 44, 2).Add('v', 36, 9).Add('v', 62, 9).Add('E', 69, 2);
        rooms.Add(r);

        // ---------------- 02 SERVER HALL: pits, raised floors, first drones ----------------
        r = new Room("Room_02_ServerHall", 92, 22)
        {
            title = "SECTOR 02 - SERVER HALL",
            hint = "SCANNER DRONES DIVE AT YOU - JUMP AND STRIKE",
            next = "Room_03_Terminal",
            background = new Color(0.04f, 0.08f, 0.1f), tint = new Color(0.75f, 1f, 1f)
        };
        r.Solid(0, 0, 20, 2).Platform(21, 4, 3)
         .Solid(25, 0, 15, 2)
         .Solid(40, 0, 12, 5)
         .Solid(56, 0, 14, 2).Electric(60, 2, 4).Platform(58, 4, 8)
         .Solid(70, 0, 22, 2).Platform(74, 5, 5).Platform(81, 8, 5);
        r.Add('P', 2, 2).Add('B', 14, 2).Add('C', 26, 2).Add('r', 28, 2).Add('r', 30, 2).Add('B', 34, 2)
         .Add('B', 46, 5).Add('D', 54, 9).Add('C', 71, 2).Add('B', 76, 2).Add('D', 80, 12).Add('B', 85, 2)
         .Add('r', 64, 5).Add('v', 10, 10).Add('v', 48, 12).Add('E', 89, 2);
        rooms.Add(r);

        // ---------------- 03 TERMINAL #1: vertical climb to the terminal ----------------
        r = new Room("Room_03_Terminal", 60, 26)
        {
            title = "SECTOR 03 - TERMINAL ALPHA",
            hint = "CLIMB TO THE TERMINAL AND PRESS E TO REBOOT IT",
            next = "Room_04_Corrupted", requiredTerminals = 1,
            background = new Color(0.05f, 0.06f, 0.14f), tint = new Color(0.85f, 0.85f, 1f)
        };
        r.Solid(0, 0, 60, 2)
         .Platform(9, 4, 5).Platform(16, 7, 5).Platform(9, 10, 5).Platform(16, 13, 5).Platform(23, 16, 5)
         .Solid(29, 15, 14, 2)
         .Electric(49, 2, 3);
        r.Add('P', 2, 2).Add('B', 32, 2).Add('B', 45, 2).Add('C', 30, 17).Add('B', 34, 17).Add('T', 39, 17)
         .Add('D', 36, 21).Add('D', 12, 16).Add('r', 4, 2).Add('r', 6, 2).Add('v', 50, 12).Add('E', 57, 2);
        rooms.Add(r);

        // ---------------- 04 CORRUPTED: corrupted pools and enemy groups ----------------
        r = new Room("Room_04_Corrupted", 100, 20)
        {
            title = "SECTOR 04 - CORRUPTED SECTOR",
            hint = "CORRUPTED DATA HURTS - DON'T FALL IN",
            next = "Room_05_Ventilation",
            background = new Color(0.1f, 0.04f, 0.1f), tint = new Color(1f, 0.75f, 1f)
        };
        r.Solid(1, 0, 98, 1)
         .Solid(0, 1, 14, 1).Corrupt(14, 1, 6, 1).Solid(20, 1, 20, 1).Corrupt(40, 1, 6, 1)
         .Solid(46, 1, 24, 1).Corrupt(70, 1, 6, 1).Solid(76, 1, 24, 1)
         .Platform(15, 4, 4).Platform(41, 4, 4).Platform(71, 4, 4)
         .Solid(28, 2, 2, 3)
         .Platform(50, 5, 6).Platform(58, 8, 6);
        r.Add('P', 2, 2).Add('B', 24, 2).Add('B', 35, 2).Add('D', 36, 9).Add('C', 47, 2).Add('B', 52, 2)
         .Add('B', 62, 2).Add('D', 64, 12).Add('D', 82, 9).Add('B', 84, 2).Add('B', 92, 2)
         .Add('r', 5, 2).Add('r', 22, 2).Add('v', 55, 14).Add('r', 78, 2).Add('E', 97, 2);
        rooms.Add(r);

        // ---------------- 05 VENTILATION: mini-boss arena, then a tall vertical shaft ----------------
        // x 0..39 = arena of the Corrupted Core (mini-boss), x 40..79 = the shaft. The exit at the top
        // stays locked until the mini-boss is destroyed.
        r = new Room("Room_05_Ventilation", 80, 44)
        {
            title = "SECTOR 05 - VENTILATION SHAFT",
            hint = "DESTROY THE GUARDIAN CORE, THEN CLIMB THE SHAFT TO THE EXIT",
            next = "Room_06_DeepServer",
            background = new Color(0.04f, 0.09f, 0.08f), tint = new Color(0.75f, 1f, 0.85f),
            arenaMinX = 3, arenaMaxX = 37, miniBoss = true
        };
        // Arena + wall to the shaft (open at floor level)
        r.Solid(0, 0, 40, 2).Platform(4, 6, 5).Platform(31, 6, 5).Solid(40, 6, 1, 38);
        // Shaft (the old room, moved 40 tiles right)
        r.Solid(40, 0, 40, 2)
         .Platform(48, 4, 6).Platform(57, 7, 7).Solid(68, 9, 8, 1)
         .Platform(59, 12, 6).Platform(50, 15, 6).Solid(41, 18, 6, 1)
         .Platform(50, 21, 6).Platform(59, 24, 7).Solid(69, 27, 10, 1).Electric(72, 28, 2)
         .Platform(60, 30, 6).Platform(50, 33, 6).Solid(41, 36, 8, 1);
        r.Add('P', 3, 2).Add('X', 20, 12).Add('H', 5, 2, 6).Add('H', 29, 2, 6).Add('v', 3, 12).Add('v', 36, 12)
         .Add('C', 43, 2).Add('B', 71, 10).Add('C', 43, 19).Add('D', 52, 13).Add('D', 58, 28).Add('C', 77, 28)
         .Add('D', 65, 36).Add('v', 45, 8).Add('v', 74, 16).Add('v', 45, 26).Add('v', 74, 35).Add('E', 44, 37);
        rooms.Add(r);

        // ---------------- 06 DEEP SERVER: long gauntlet ----------------
        r = new Room("Room_06_DeepServer", 110, 24)
        {
            title = "SECTOR 06 - DEEP SERVER",
            next = "Room_07_Terminal",
            background = new Color(0.03f, 0.04f, 0.09f), tint = new Color(0.7f, 0.75f, 1f)
        };
        r.Solid(0, 0, 18, 2).Solid(22, 0, 18, 2).Solid(45, 0, 20, 2).Solid(71, 0, 39, 2)
         .Solid(28, 2, 4, 3).Platform(41, 4, 3)
         .Electric(50, 2, 3).Electric(56, 2, 3).Platform(49, 4, 12)
         .Platform(66, 4, 4)
         .Solid(75, 2, 3, 2).Solid(78, 2, 10, 4).Solid(92, 2, 8, 2);
        r.Add('P', 2, 2).Add('r', 6, 2).Add('r', 8, 2).Add('B', 12, 2).Add('B', 25, 2).Add('B', 35, 2).Add('D', 30, 9)
         .Add('C', 46, 2).Add('D', 55, 10).Add('B', 62, 2).Add('C', 73, 2).Add('B', 83, 6).Add('B', 96, 4)
         .Add('D', 95, 10).Add('B', 104, 2).Add('r', 100, 2).Add('v', 20, 12).Add('v', 68, 14).Add('E', 107, 2);
        rooms.Add(r);

        // ---------------- 07 TERMINAL #2: upper chamber ----------------
        r = new Room("Room_07_Terminal", 70, 30)
        {
            title = "SECTOR 07 - TERMINAL BETA",
            hint = "THE TERMINAL IS IN THE UPPER CHAMBER",
            next = "Room_08_Core", requiredTerminals = 2,
            background = new Color(0.05f, 0.06f, 0.14f), tint = new Color(0.85f, 0.85f, 1f)
        };
        r.Solid(0, 0, 70, 2).Electric(12, 2, 3)
         .Platform(34, 4, 5).Platform(42, 7, 5).Platform(50, 10, 5).Platform(58, 13, 5).Platform(56, 16, 4)
         .Solid(20, 18, 36, 2);
        r.Add('P', 2, 2).Add('B', 18, 2).Add('B', 27, 2).Add('C', 31, 2)
         .Add('T', 24, 20).Add('B', 32, 20).Add('B', 42, 20).Add('D', 38, 25).Add('C', 53, 20)
         .Add('D', 48, 8).Add('r', 5, 2).Add('r', 7, 2).Add('v', 62, 22).Add('E', 67, 2);
        rooms.Add(r);

        // ---------------- 08 CORE: hardest platforming before the last terminal ----------------
        r = new Room("Room_08_Core", 100, 26)
        {
            title = "SECTOR 08 - SERVER CORE",
            next = "Room_09_Terminal",
            background = new Color(0.12f, 0.04f, 0.05f), tint = new Color(1f, 0.7f, 0.7f)
        };
        r.Solid(0, 0, 16, 2)
         .Solid(16, 0, 20, 1).Corrupt(16, 1, 20, 1)
         .Platform(18, 4, 4).Platform(24, 6, 4).Platform(30, 4, 4)
         .Solid(36, 0, 20, 2)
         .Platform(51, 4, 3).Solid(56, 0, 4, 9)
         .Solid(60, 0, 40, 2).Electric(64, 2, 2).Electric(70, 2, 2).Electric(76, 2, 2);
        r.Add('P', 2, 2).Add('D', 25, 11).Add('C', 37, 2).Add('B', 42, 2).Add('B', 50, 2).Add('D', 46, 9)
         .Add('C', 57, 9).Add('D', 68, 9).Add('D', 78, 10).Add('B', 84, 2).Add('B', 91, 2)
         .Add('r', 5, 2).Add('r', 40, 2).Add('v', 88, 12).Add('E', 97, 2);
        rooms.Add(r);

        // ---------------- 09 TERMINAL #3: tower climb, exit to the boss ----------------
        r = new Room("Room_09_Terminal", 60, 34)
        {
            title = "SECTOR 09 - TERMINAL GAMMA",
            hint = "REBOOT THE LAST TERMINAL TO OPEN THE CORE CHAMBER",
            next = "Room_10_Boss", requiredTerminals = 3,
            background = new Color(0.05f, 0.06f, 0.14f), tint = new Color(0.85f, 0.85f, 1f)
        };
        r.Solid(0, 0, 60, 2)
         .Platform(8, 4, 5).Platform(16, 7, 5).Platform(24, 10, 5).Platform(16, 13, 5)
         .Platform(8, 16, 5).Platform(16, 19, 5).Platform(24, 22, 5)
         .Solid(30, 24, 14, 1)
         .Electric(46, 2, 3);
        r.Add('P', 2, 2).Add('B', 32, 2).Add('B', 42, 2).Add('D', 20, 12).Add('D', 12, 23)
         .Add('C', 31, 25).Add('B', 35, 25).Add('T', 40, 25).Add('r', 52, 2).Add('v', 50, 16).Add('E', 57, 2);
        rooms.Add(r);

        // ---------------- 10 BOSS ARENA: the Core Sentinel (final boss) ----------------
        r = new Room("Room_10_Boss", 48, 22)
        {
            title = "CORE CHAMBER",
            hint = "JUMP OVER BEAMS, DASHES AND SHOCKWAVES - STRIKE BETWEEN ATTACKS",
            background = new Color(0.1f, 0.02f, 0.04f), tint = new Color(1f, 0.6f, 0.65f),
            arenaMinX = 3, arenaMaxX = 45
        };
        r.Solid(0, 0, 48, 2).Platform(5, 6, 6).Platform(37, 6, 6).Platform(20, 10, 8);
        r.Add('P', 3, 2).Add('S', 32, 2).Add('r', 13, 2).Add('r', 34, 2).Add('v', 3, 12).Add('v', 44, 12).Add('v', 24, 16);
        rooms.Add(r);

        return rooms;
    }

    static string BuildRoom(Room room)
    {
        LoadTiles();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        string path = "Assets/Scenes/Level01/" + room.scene + ".unity";

        // ---------- Tilemaps ----------
        var grid = new GameObject("Grid", typeof(Grid));
        Tilemap background = NewTilemap(grid, "Background", SortBackground, 0);
        Tilemap ground = NewTilemap(grid, "Ground", SortTiles, GroundLayer);
        Tilemap platforms = NewTilemap(grid, "Platforms", SortTiles, GroundLayer);
        Tilemap hazards = NewTilemap(grid, "Hazards", SortHazards, 0);
        background.color = room.tint * 0.9f;
        ground.color = room.tint;
        platforms.color = room.tint;

        for (int x = 0; x < room.width; x++)
        {
            for (int y = 0; y < room.height; y++)
            {
                var cell = new Vector3Int(x, y, 0);
                background.SetTile(cell, tileBackground);
                switch (room.Cell(x, y))
                {
                    case '#': ground.SetTile(cell, room.Cell(x, y + 1) == '#' ? tileFill : tileSurface); break;
                    case '=': platforms.SetTile(cell, tilePlatform); break;
                    case '^': hazards.SetTile(cell, tileElectric); break;
                    case '~': hazards.SetTile(cell, tileCorrupted); break;
                }
            }
        }

        // Solid ground: merged colliders
        AddCompositeCollider(ground.gameObject, false);
        // One-way platforms: jump up through them, land on top
        AddCompositeCollider(platforms.gameObject, true);
        var effector = platforms.gameObject.AddComponent<PlatformEffector2D>();
        effector.useOneWay = true;
        effector.surfaceArc = 160f;
        // Hazards: triggers tagged "Hazard"
        hazards.gameObject.tag = "Hazard";
        hazards.gameObject.AddComponent<TilemapCollider2D>().isTrigger = true;

        // Pit death zone under the room
        var deathZone = new GameObject("DeathZone") { tag = "DeathZone" };
        deathZone.transform.position = new Vector3(room.width * 0.5f, -3f, 0f);
        var deathCollider = deathZone.AddComponent<BoxCollider2D>();
        deathCollider.isTrigger = true;
        deathCollider.size = new Vector2(room.width + 20f, 2f);

        // ---------- Things ----------
        var decor = new GameObject("Decor").transform;
        var enemies = new GameObject("Enemies").transform;
        var interactables = new GameObject("Interactables").transform;
        Transform spawn = null;
        GameObject playerObject = null;
        GameObject bossObject = null;
        GameObject sentinelObject = null;
        LevelExit roomExit = null;
        var arenaHazards = new List<GameObject>();

        foreach (Thing t in room.things)
        {
            Vector3 feet = new Vector3(t.x + 0.5f, t.y, 0f);
            switch (t.kind)
            {
                case 'P':
                    spawn = new GameObject("PlayerSpawn").transform;
                    spawn.position = feet;
                    playerObject = Spawn(PlayerPrefabPath, feet, null);
                    break;
                case 'B':
                    GameObject bug = Spawn(BugPrefabPath, feet, enemies);
                    SetupPatrol(room, bug, t.x, t.y);
                    break;
                case 'D':
                    Spawn(DronePrefabPath, new Vector3(t.x + 0.5f, t.y + 0.5f, 0f), enemies);
                    break;
                case 'T':
                    Spawn(TerminalPrefabPath, feet, interactables);
                    break;
                case 'C':
                    Spawn(CheckpointPrefabPath, feet, interactables);
                    break;
                case 'E':
                    GameObject exit = Spawn(ExitPrefabPath, feet, interactables);
                    var levelExit = exit.GetComponent<LevelExit>();
                    levelExit.nextSceneName = room.next;
                    levelExit.requiredTerminals = room.requiredTerminals;
                    roomExit = levelExit;
                    break;
                case 'X':
                    bossObject = Spawn(BossPrefabPath, new Vector3(t.x, t.y, 0f), null);
                    break;
                case 'S':
                    sentinelObject = Spawn(SentinelPrefabPath, new Vector3(t.x + 0.5f, t.y, 0f), null);
                    break;
                case 'H':
                    arenaHazards.Add(NewArenaHazard(t, interactables));
                    break;
                case 'r':
                    NewDecor("ServerRack", LoadSprite(ArtEnv + "ServerRack.png"), feet, decor, room.tint);
                    break;
                case 'v':
                    NewDecor("Vent", LoadSprite(ArtEnv + "Vent.png"), new Vector3(t.x + 0.5f, t.y + 0.5f, 0f), decor, room.tint);
                    break;
            }
        }

        if (bossObject != null)
        {
            var boss = bossObject.GetComponent<BossController>();
            boss.hoverY = 12f;
            boss.floorY = 2f;
            boss.slamY = 2f + 1.4f;
            boss.minX = room.arenaMinX;
            boss.maxX = room.arenaMaxX > 0 ? room.arenaMaxX : room.width - 3f;
            boss.arenaHazards = arenaHazards.ToArray();
            if (room.miniBoss)
            {
                // Weaker version of the old final boss; the room exit waits for it
                boss.isMiniBoss = true;
                bossObject.GetComponent<EnemyHealth>().maxHealth = 16;
                if (roomExit != null) roomExit.requiredBoss = bossObject.GetComponent<EnemyHealth>();
            }
        }

        if (sentinelObject != null)
        {
            var sentinel = sentinelObject.GetComponent<SentinelBossController>();
            sentinel.floorY = 2f;
            sentinel.minX = room.arenaMinX;
            sentinel.maxX = room.arenaMaxX > 0 ? room.arenaMaxX : room.width - 3f;
            if (roomExit != null) roomExit.requiredBoss = sentinelObject.GetComponent<EnemyHealth>();
        }

        // ---------- Managers, camera, UI, audio ----------
        var respawnManager = new GameObject("RespawnManager").AddComponent<RespawnManager>();
        respawnManager.playerSpawn = spawn;

        var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = cameraObject.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 7f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = room.background;
        cameraObject.AddComponent<AudioListener>();
        var follow = cameraObject.AddComponent<CameraFollow>();
        follow.target = playerObject != null ? playerObject.transform : null;
        follow.minBounds = Vector2.zero;
        follow.maxBounds = new Vector2(room.width, room.height);
        cameraObject.transform.position = new Vector3(spawn != null ? spawn.position.x : 0f, 7f, -10f);

        GameObject hud = Spawn(HudPrefabPath, Vector3.zero, null);
        var ui = hud.GetComponent<UIManager>();
        ui.roomTitle = room.title;
        ui.roomHint = room.hint;
        NewEventSystem(null);

        // Same rule as SoundGenerator.MusicForScene (only the final boss room gets the boss track)
        NewMusic(room.scene.Contains("Boss") ? "Music_Boss" : "Music_Level");

        EditorSceneManager.SaveScene(scene, path);
        return path;
    }

    static Tilemap NewTilemap(GameObject grid, string name, int sortingOrder, int layer)
    {
        var go = new GameObject(name, typeof(Tilemap), typeof(TilemapRenderer)) { layer = layer };
        go.transform.SetParent(grid.transform, false);
        go.GetComponent<TilemapRenderer>().sortingOrder = sortingOrder;
        return go.GetComponent<Tilemap>();
    }

    static void AddCompositeCollider(GameObject tilemapObject, bool usedByEffector)
    {
        var tilemapCollider = tilemapObject.AddComponent<TilemapCollider2D>();
        tilemapCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
        var rb = tilemapObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Static;
        var composite = tilemapObject.AddComponent<CompositeCollider2D>();
        composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
        composite.usedByEffector = usedByEffector;
    }

    static GameObject Spawn(string prefabPath, Vector3 position, Transform parent)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.position = position;
        if (parent != null) instance.transform.SetParent(parent, true);
        return instance;
    }

    // Patrol points = how far the bug can walk on its platform (max 5 tiles each way)
    static void SetupPatrol(Room room, GameObject bug, int x, int y)
    {
        System.Func<int, bool> walkable = cx =>
        {
            char below = room.Cell(cx, y - 1);
            char at = room.Cell(cx, y);
            return (below == '#' || below == '=') && at != '#' && at != '^' && at != '~';
        };
        int left = x, right = x;
        while (left - 1 >= 0 && x - (left - 1) <= 5 && walkable(left - 1)) left--;
        while (right + 1 < room.width && (right + 1) - x <= 5 && walkable(right + 1)) right++;

        var ai = bug.GetComponent<VirusBugAI>();
        ai.pointA.position = new Vector3(left + 0.6f, y, 0f);
        ai.pointB.position = new Vector3(right + 0.4f, y, 0f);
    }

    static GameObject NewArenaHazard(Thing t, Transform parent)
    {
        var hazard = new GameObject("ArenaHazard") { tag = "Hazard" };
        hazard.transform.SetParent(parent, false);
        hazard.transform.position = new Vector3(t.x + t.width * 0.5f, t.y + 0.5f, 0f);
        var sr = hazard.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite(ArtEnv + "Tile_Electric.png");
        sr.drawMode = SpriteDrawMode.Tiled;
        sr.size = new Vector2(t.width, 1f);
        sr.sortingOrder = SortHazards;
        var col = hazard.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(t.width, 0.45f);
        col.offset = new Vector2(0f, -0.27f);
        hazard.SetActive(false);
        return hazard;
    }

    static void NewDecor(string name, Sprite sprite, Vector3 position, Transform parent, Color tint)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = SortDecor;
        sr.color = tint * 0.85f;
    }

    static void NewEventSystem(GameObject firstSelected)
    {
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        es.GetComponent<EventSystem>().firstSelectedGameObject = firstSelected;
    }

    static void NewMusic(string clipName)
    {
        SoundGenerator.SetupMusic(new GameObject("Music_Background"), clipName);
    }

    // =====================================================================
    // MAIN MENU
    // =====================================================================

    static string BuildMainMenu()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        string path = "Assets/Scenes/MainMenu/MainMenu.unity";

        var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = cameraObject.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.03f, 0.04f, 0.08f);
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        cameraObject.AddComponent<AudioListener>();

        GameObject canvas = NewCanvas("MainMenuCanvas");
        var ui = canvas.AddComponent<UIManager>();
        Color cyan = new Color(0.45f, 0.95f, 1f);

        NewText("Title", canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 360f), new Vector2(1600f, 140f),
            "SYSTEM-REBOOT", 120, TextAnchor.MiddleCenter, cyan);
        NewText("Subtitle", canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 270f), new Vector2(1600f, 60f),
            "Reboot 3 terminals. Purge the infected core.", 38, TextAnchor.MiddleCenter, Color.white);

        // The provided Robot artwork, shown large
        RectTransform robotImage = NewUI("RobotArt", canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(320f, 320f));
        var image = robotImage.gameObject.AddComponent<Image>();
        image.sprite = LoadSprite(ArtRobot + "Robot.png");
        image.preserveAspect = true;
        image.raycastTarget = false;

        Button start = NewButton("StartButton", canvas.transform, new Vector2(0f, -170f), "START");
        Button quit = NewButton("QuitButton", canvas.transform, new Vector2(0f, -270f), "QUIT");
        UnityEventTools.AddPersistentListener(start.onClick, ui.StartGame);
        UnityEventTools.AddPersistentListener(quit.onClick, ui.QuitGame);

        NewText("Controls", canvas.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(1800f, 50f),
            "A/D MOVE   SPACE JUMP / DOUBLE JUMP   J ATTACK   E INTERACT", 30, TextAnchor.MiddleCenter, new Color(0.7f, 0.8f, 0.9f));

        NewEventSystem(start.gameObject);
        NewMusic("Music_Menu");

        EditorSceneManager.SaveScene(scene, path);
        return path;
    }

    // Removes the earlier test objects from SampleScene (not used by the game)
    static void CleanSampleScene()
    {
        const string samplePath = "Assets/Scenes/SampleScene.unity";
        if (!File.Exists(samplePath)) return;
        var scene = EditorSceneManager.OpenScene(samplePath, OpenSceneMode.Single);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "Player" || root.name == "Level" || root.name == "Enemies") Object.DestroyImmediate(root);
        }
        EditorSceneManager.SaveScene(scene);
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
