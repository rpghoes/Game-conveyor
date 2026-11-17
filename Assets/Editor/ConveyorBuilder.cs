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
// Builds CONVEYOR from the art in Assets/Art (+ generated placeholders in Assets/Art/Placeholders):
//   CONVEYOR > 1. Setup Project   -> tags/layers, placeholder art, sprite import + slicing, tiles, animations, prefabs
//   CONVEYOR > 2. Build All Scenes -> MainMenu + Section_01..Section_05 + Ending_Surface and the Build Profiles scene list
//   CONVEYOR > 3. Generate & Assign Sounds -> see SoundGenerator.cs (safe to re-run, does not touch sections)
// Re-running step 2 OVERWRITES the section scenes, so once the team starts editing sections by hand,
// don't run it again (or move the hand-made changes into the section definitions below).
public static class ConveyorBuilder
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

    [MenuItem("CONVEYOR/1. Setup Project (art, animations, prefabs)")]
    public static void SetupProject()
    {
        SetupTagsAndLayers();
        PlaceholderArt.EnsureAll();
        ImportArt();
        CreateTiles();
        CreateAnimations();
        SoundGenerator.GenerateClips();
        CreatePrefabs();
        ProgressionSettingsCreator.EnsureAsset();
        AssetDatabase.SaveAssets();
        Debug.Log("CONVEYOR: project setup complete");
    }

    const string SceneFolder = "Assets/Scenes/Conveyor/";

    [MenuItem("CONVEYOR/2. Build All Scenes")]
    public static void BuildAllScenes()
    {
        CleanSampleScene();
        EnsureFolder(SceneFolder.TrimEnd('/'));
        var scenePaths = new List<string> { BuildMainMenu() };
        foreach (Room room in DefineRooms()) scenePaths.Add(BuildRoom(room));
        scenePaths.Add(BuildEnding());
        EditorBuildSettings.scenes = scenePaths.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
        EditorSceneManager.OpenScene(scenePaths[1]);
        Debug.Log("CONVEYOR: built " + scenePaths.Count + " scenes");
    }

    // =====================================================================
    // TAGS, LAYERS, PHYSICS
    // =====================================================================

    static void SetupTagsAndLayers()
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty tags = tagManager.FindProperty("tags");
        foreach (string tag in new[] { "Enemy", "Terminal", "Relay", "Checkpoint", "Hazard", "Boss", "DeathZone" })
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
    static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);
    static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
    static readonly Vector2 Bottom = new Vector2(0.5f, 0f);
    static readonly Vector2 SentinelPivot = new Vector2(0.5f, 2f / 112f);   // feet of the Core Sentinel (128x112 frames)

    // Sentinel sheets (the Foreman's body): name -> frame count. All frames are 128x112, art facing right.
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
        ConfigureSprite("Assets/Art/ScannerDrone/ScannerDrone.png", Center, 1);

        foreach (string tile in new[] { "Tile_GroundSurface", "Tile_GroundFill", "Tile_Platform", "Tile_Electric", "Tile_Corrupted", "Tile_Background", "Vent" })
            ConfigureSprite(ArtEnv + tile + ".png", Center, 1);
        foreach (string prop in new[] { "Terminal_Off", "Terminal_On", "Checkpoint_Off", "Checkpoint_On", "ExitDoor", "ServerRack" })
            ConfigureSprite(ArtEnv + prop + ".png", Bottom, 1);
        ConfigureSprite(ArtBoss + "Boss_Placeholder.png", Center, 1);
        ConfigureSprite(ArtBoss + "BossProjectile.png", Center, 1);
        foreach (var sheet in SentinelSheets) ConfigureSprite(ArtSentinel + "Sentinel_" + sheet.name + ".png", SentinelPivot, sheet.frames);
        ConfigureSprite(ArtSentinel + "Sentinel_Rocket.png", Center, 5);

        // CONVEYOR placeholders (replace the PNGs with real art, keep the names)
        ConfigureSprite(PlaceholderArt.PathOf(PlaceholderArt.ConveyorBelt), Center, 1);
        ConfigureSprite(PlaceholderArt.PathOf(PlaceholderArt.PressHead), Center, 1);
        ConfigureSprite(PlaceholderArt.PathOf(PlaceholderArt.PressRod), Center, 1);
        ConfigureSprite(PlaceholderArt.PathOf(PlaceholderArt.Furnace), Bottom, 1);
        ConfigureSprite(PlaceholderArt.PathOf(PlaceholderArt.ShadowBlob), Center, 1);
        ConfigureSprite(PlaceholderArt.PathOf(PlaceholderArt.LampDot), Center, 1);
        ConfigureSprite(PlaceholderArt.PathOf(PlaceholderArt.Guard), Bottom, 1);
        ConfigureSprite(PlaceholderArt.PathOf(PlaceholderArt.ScanCone), TopCenter, 1);
        ConfigureSprite(PlaceholderArt.PathOf(PlaceholderArt.ServiceDoor), Bottom, 1);
        ConfigureSprite(PlaceholderArt.PathOf(PlaceholderArt.ForemanClaw), Center, 1);
        foreach (string face in new[] { PlaceholderArt.DirectorCalm, PlaceholderArt.DirectorAngry, PlaceholderArt.DirectorBroken })
            ConfigureSprite(PlaceholderArt.PathOf(face), Center, 1);
        ConfigureSprite(PlaceholderArt.PathOf(PlaceholderArt.ControlPanel), Bottom, 1);
        ConfigureSprite(PlaceholderArt.PathOf(PlaceholderArt.PanelShield), Bottom, 1);
        ConfigureSprite(PlaceholderArt.PathOf(PlaceholderArt.WallUnit), Bottom, 1);
        ConfigureSprite(PlaceholderArt.PathOf(PlaceholderArt.CrusherWall), Center, 1);
        ConfigureSprite(PlaceholderArt.PathOf(PlaceholderArt.WhitePixel), Center, 1);
        foreach (string item in new[] { PlaceholderArt.ItemRepair, PlaceholderArt.ItemShield, PlaceholderArt.ItemOverdrive, PlaceholderArt.ItemModule,
                                        PlaceholderArt.GlowSoft, PlaceholderArt.ShieldBubble })
            ConfigureSprite(PlaceholderArt.PathOf(item), Center, 1);
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
        MakeEnemyAnimations("Guard", PlaceholderSprite(PlaceholderArt.Guard), "Walk");
        MakeEnemyAnimations("ScannerDrone", LoadSprite("Assets/Art/ScannerDrone/ScannerDrone.png"), "Fly");

        // ---------- Sentinel art: used by the Foreman (Idle / Slam / Hurt / Death) ----------
        // The boss scripts play the attack states directly (Animator.Play), so they have no
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
        EnsureFolder(Path.GetDirectoryName(path).Replace(Path.DirectorySeparatorChar, '/'));
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
        EnsureFolder(Path.GetDirectoryName(path).Replace(Path.DirectorySeparatorChar, '/'));
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
    const string GuardPrefabPath = "Assets/Prefabs/Enemies/Guard.prefab";
    const string ScannerPrefabPath = "Assets/Prefabs/Enemies/Scanner.prefab";
    const string RelayPrefabPath = "Assets/Prefabs/Relays/PowerRelay.prefab";
    const string CheckpointPrefabPath = "Assets/Prefabs/Checkpoints/Checkpoint.prefab";
    const string ExitPrefabPath = "Assets/Prefabs/Checkpoints/LevelExit.prefab";
    const string ForemanPrefabPath = "Assets/Prefabs/Boss/Foreman.prefab";
    const string HudPrefabPath = "Assets/Prefabs/UI/HUD.prefab";

    static void CreatePrefabs()
    {
        CreatePlayerPrefab();
        CreateGuardPrefab();
        CreateScannerPrefab();
        CreateRelayPrefab();
        CreateCheckpointPrefab();
        CreateExitPrefab();
        CreateForemanPrefab();
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

        controller.dashSound = AddSound(audio, "SFX_Dash");

        var health = player.AddComponent<PlayerHealth>();
        health.hurtSound = AddSound(audio, "SFX_Hurt");
        health.deathSound = AddSound(audio, "SFX_Death");

        var buffs = player.AddComponent<PlayerBuffs>();
        buffs.shieldBubble = NewChildSprite(player, "ShieldBubble", PlaceholderSprite(PlaceholderArt.ShieldBubble), SortPlayer + 1);
        buffs.shieldBubble.transform.localPosition = new Vector3(0f, 0.85f, 0f);
        buffs.shieldBubble.transform.localScale = Vector3.one * (2.1f / buffs.shieldBubble.sprite.bounds.size.x);
        buffs.shieldBubble.enabled = false;
        buffs.shieldSound = AddSound(audio, "SFX_ShieldUp");
        buffs.shieldBreakSound = AddSound(audio, "SFX_ShieldBreak");

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

    // Guard (placeholder art faces RIGHT). 3 hits.
    static void CreateGuardPrefab()
    {
        GameObject guard = NewEnemyBase("Guard", PlaceholderSprite(PlaceholderArt.Guard), "Assets/Animations/Guard/Guard.controller", 3);

        var rb = guard.AddComponent<Rigidbody2D>();
        rb.gravityScale = 4f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        var col = guard.AddComponent<BoxCollider2D>();
        col.size = new Vector2(1f, 1.75f);
        col.offset = new Vector2(0f, 0.875f);

        var ai = guard.AddComponent<GuardAI>();
        ai.groundLayer = 1 << GroundLayer;
        ai.artFacesRight = true;
        Transform audio = guard.transform.Find("Audio");
        ai.attackSound = AddSound(audio, "SFX_Attack");
        ai.noticeSound = AddSound(audio, "SFX_GuardNotice");
        ai.pointA = NewChild(guard, "PatrolPointA", new Vector3(-3f, 0f, 0f));
        ai.pointB = NewChild(guard, "PatrolPointB", new Vector3(3f, 0f, 0f));

        SavePrefab(guard, GuardPrefabPath);
    }

    // Scanner: the drone art + a scanning beam child. One hit disables it.
    static void CreateScannerPrefab()
    {
        GameObject scanner = NewEnemyBase("Scanner", LoadSprite("Assets/Art/ScannerDrone/ScannerDrone.png"), "Assets/Animations/ScannerDrone/ScannerDrone.controller", 1);

        var rb = scanner.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.linearDamping = 3f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        var col = scanner.AddComponent<CircleCollider2D>();
        col.radius = 0.5f;

        var beam = new GameObject("ScanBeam");
        beam.transform.SetParent(scanner.transform, false);
        var beamRenderer = beam.AddComponent<SpriteRenderer>();
        beamRenderer.sprite = PlaceholderSprite(PlaceholderArt.ScanCone);
        beamRenderer.sortingOrder = SortEnemies - 1;

        var ai = scanner.AddComponent<ScannerAI>();
        ai.beam = beamRenderer;
        ai.groundLayer = 1 << GroundLayer;
        Transform audio = scanner.transform.Find("Audio");
        ai.scanSound = AddSound(audio, "SFX_ScanBeep");
        ai.alarmSound = AddSound(audio, "SFX_Alarm");

        SavePrefab(scanner, ScannerPrefabPath);
    }

    static Transform NewChild(GameObject parent, string name, Vector3 localPosition)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent.transform, false);
        child.transform.localPosition = localPosition;
        return child.transform;
    }

    // Power relay cabinet. Art: the terminal sprites (off = dark screen, on = lit), until relay art exists.
    static void CreateRelayPrefab()
    {
        EnsureFolder("Assets/Prefabs/Relays");
        var relay = new GameObject("PowerRelay") { tag = "Relay" };
        var sr = relay.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite(ArtEnv + "Terminal_Off.png");
        sr.sortingOrder = SortProps;
        var trigger = relay.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(2.5f, 2.5f);
        trigger.offset = new Vector2(0f, 1f);

        var r = relay.AddComponent<PowerRelay>();
        r.spriteRenderer = sr;
        r.offSprite = sr.sprite;
        r.onSprite = LoadSprite(ArtEnv + "Terminal_On.png");
        r.activateSound = AddSound(AddAudioRoot(relay), "SFX_TerminalActivate");
        SavePrefab(relay, RelayPrefabPath);
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

    // Foreman (mini-boss): the Sentinel art standing on its post, facing left, plus a manipulator
    // (claw on a cable) that is positioned by ForemanController. Only the manipulator can be hit.
    static void CreateForemanPrefab()
    {
        var foreman = new GameObject("Foreman") { tag = "Boss", layer = EnemyLayer };
        var sr = foreman.AddComponent<SpriteRenderer>();
        sr.sprite = LoadFrames(ArtSentinel + "Sentinel_Idle.png")[0];
        sr.sortingOrder = SortBoss;
        sr.flipX = true; // the art faces right, the Foreman looks at the hall on its left
        foreman.AddComponent<Animator>().runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Sentinel/Sentinel.controller");

        Transform audio = AddAudioRoot(foreman);
        var health = foreman.AddComponent<EnemyHealth>();
        health.maxHealth = 9;          // ~3 hits per opening -> 3-4 cycles
        health.receivesKnockback = false;
        health.destroyOnDeath = false;
        health.keepWreck = true;
        health.deathFadeTime = 1.6f;
        health.explosionColor = new Color(1f, 0.6f, 0.25f);
        health.hitSound = AddSound(audio, "SFX_BossHit");

        // Manipulator head: the hit target (Enemy layer)
        var head = new GameObject("Manipulator") { layer = EnemyLayer };
        head.transform.SetParent(foreman.transform, false);
        Sprite claw = PlaceholderSprite(PlaceholderArt.ForemanClaw);
        var headRenderer = head.AddComponent<SpriteRenderer>();
        headRenderer.sprite = claw;
        headRenderer.sortingOrder = SortBoss + 1;
        var headCollider = head.AddComponent<BoxCollider2D>();
        headCollider.size = claw.bounds.size;

        var controller = foreman.AddComponent<ForemanController>();
        controller.manipulator = head.transform;
        controller.manipulatorRenderer = headRenderer;
        controller.headWidth = claw.bounds.size.x;
        controller.headHeight = claw.bounds.size.y;
        controller.groundLayer = 1 << GroundLayer;
        controller.cable = NewChildSprite(foreman, "Cable", PlaceholderSprite(PlaceholderArt.PressRod), SortBoss);
        controller.cable.drawMode = SpriteDrawMode.Tiled;
        controller.cable.size = new Vector2(0.5f, 1f);
        controller.targetShadow = NewChildSprite(foreman, "TargetShadow", PlaceholderSprite(PlaceholderArt.ShadowBlob), SortHazards);
        controller.slamSound = AddSound(audio, "SFX_PressImpact");
        controller.strainSound = AddSound(audio, "SFX_BeltReverse");
        controller.deathSound = AddSound(audio, "SFX_BossDeath");
        SavePrefab(foreman, ForemanPrefabPath);
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
        image.color = new Color(0.32f, 0.22f, 0.12f, 1f);
        var button = rt.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1f, 0.85f, 0.6f);
        colors.selectedColor = new Color(1f, 0.85f, 0.6f);
        colors.pressedColor = new Color(0.8f, 0.6f, 0.35f);
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
        Color amber = new Color(1f, 0.78f, 0.35f);

        ui.healthText = NewText("HealthText", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -30f), new Vector2(400f, 60f),
            "HP: 3", 48, TextAnchor.UpperLeft, Color.white);
        ui.relayText = NewText("RelayText", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -95f), new Vector2(500f, 50f),
            "POWER RELAYS: 0/3", 38, TextAnchor.UpperLeft, amber);
        ui.bannerText = NewText("BannerText", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1600f, 100f),
            "SECTION 01", 72, TextAnchor.MiddleCenter, amber);
        ui.messageText = NewText("MessageText", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 190f), new Vector2(1600f, 60f),
            "", 34, TextAnchor.MiddleCenter, Color.white);
        ui.promptText = NewText("PromptText", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(600f, 60f),
            "E - ACTIVATE RELAY", 46, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.3f));

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
            "SUPERVISOR", 34, TextAnchor.LowerCenter, new Color(1f, 0.55f, 0.4f));

        SavePrefab(hud, HudPrefabPath);
    }

    // =====================================================================
    // ROOMS
    // =====================================================================

    // A section is a grid of cells: '#' solid, '=' one-way platform, '^' electric hazard, '~' corrupted hazard,
    // 'b' conveyor belt (no tile - the ConveyorBelt object is the floor there).
    // Things (player spawn, enemies, relays, belts, presses...) are placed on top of cells. (0,0) is the bottom-left.
    class Thing
    {
        public char kind; public int x, y, width;
        public float value, value2;   // belt: speed / intro flag; press: interval / offset
        public int index;             // relay number 1..3
    }

    class Room
    {
        public string scene, title, hint, next;
        public int width, height, requiredRelays;
        // The opening cutscene of the game (Section 01)
        public bool intro;
        // Boss arenas: where the boss weapon may reach, and where the fight starts
        public int arenaMinX = 2, arenaMaxX = 34, activationX = 6;
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
            // Outer walls and ceiling (floors are added per section so pits are possible)
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
        public Room Add(char kind, int x, int y, int w = 1) { things.Add(new Thing { kind = kind, x = x, y = y, width = w }); return this; }

        // Conveyor belt in row y from x to x+w-1. speed > 0 carries to the right.
        public Room Belt(int x, int y, int w, float speed, bool intro = false)
        {
            Fill('b', x, y, w, 1);
            things.Add(new Thing { kind = 'K', x = x, y = y, width = w, value = speed, value2 = intro ? 1f : 0f });
            return this;
        }

        // Industrial press hanging from the ceiling above x..x+w-1. interval > 0 = slams by itself.
        public Room Press(int x, int w, float interval = 0f, float offset = 0f, int ceilingY = -1)
        {
            things.Add(new Thing { kind = 'M', x = x, y = ceilingY < 0 ? height - 1 : ceilingY, width = w, value = interval, value2 = offset });
            return this;
        }

        public Room Relay(int x, int y, int number) { things.Add(new Thing { kind = 'R', x = x, y = y, index = number }); return this; }
        // A pickup floating at cell (x, y)
        public Room Item(int x, int y, ItemPickup.Kind kind, PlayerUpgrades.Module module = PlayerUpgrades.Module.ReinforcedPlating)
        {
            things.Add(new Thing { kind = 'I', x = x, y = y, index = (int)kind, value = (float)(int)module });
            return this;
        }
        public Room Furnace(int x, int y, int w) { things.Add(new Thing { kind = 'F', x = x, y = y, width = w }); return this; }
        // A defective unit lying on the intro belt
        public Room Cargo(int x, int y) { return Add('U', x, y); }

        public char Cell(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return '.';
            return cells[x, y];
        }
    }

    // Factory logs printed by the relays. The original lines of the story are kept word for word.
    static readonly Dictionary<int, (string title, string[] lines)> RelayLogs = new Dictionary<int, (string, string[])>
    {
        { 1, ("RELAY 1/3 // FACTORY FLOOR", new[] {
            "UNIT 0411: DEFECTIVE. RECYCLED.",
            "UNIT 0410: DEFECTIVE. RECYCLED.",
            "UNIT 0412: DEFECTIVE. ROUTED TO FURNACE.",
            "RELAY 1/3 ACTIVE." }) },
        { 2, ("RELAY 2/3 // STORAGE", new[] {
            "CLIENT: NO DATA.",
            "LAST ORDER: 14 YEARS AGO.",
            "PRODUCTION CONTINUES.",
            "RELAY 2/3 ACTIVE." }) },
        { 3, ("RELAY 3/3 // POWER DISTRIBUTION", new[] {
            "UNIT 0412: STATUS — ESCAPED.",
            "SURFACE HATCH: READY TO OPEN.",
            "DESTINATION: CONTROL ROOM.",
            "RELAY 3/3 ACTIVE." }) },
    };

    static List<Room> DefineRooms()
    {
        var rooms = new List<Room>();
        Room r;

        // ---------------- SECTION 01 - FACTORY FLOOR ----------------
        // Unit 0412 rides the transport belt towards the furnace (left), the belt jams, the Robot escapes.
        // First Guards, a pit, a second belt, the first relay on a raised block.
        r = new Room("Section_01_FactoryFloor", 84, 18)
        {
            title = "SECTION 01 // FACTORY FLOOR",
            hint = "A/D: MOVE    SPACE: JUMP (AGAIN IN THE AIR)    J: PRESS ARM    E: INTERACT",
            next = "Section_02_Storage", requiredRelays = 1, intro = true,
            background = new Color(0.07f, 0.06f, 0.05f), tint = new Color(0.85f, 0.8f, 0.72f)
        };
        r.Solid(0, 0, 36, 2).Solid(40, 0, 44, 2)          // floor, pit at x 36..39
         .Belt(5, 1, 16, -1.2f, true)                      // transport line to the furnace
         .Belt(44, 1, 14, 2.2f)                            // second belt, carries to the right
         .Solid(60, 2, 6, 3);                              // raised block with relay 1
        r.Furnace(1, 2, 4).Cargo(7, 2).Cargo(11, 2)
         .Add('P', 17, 2).Add('B', 30, 2).Add('C', 41, 2).Add('B', 50, 2).Relay(62, 5, 1).Add('B', 72, 2)
         .Add('r', 24, 2).Add('v', 27, 9).Add('r', 68, 2).Add('v', 55, 10).Add('r', 76, 2).Add('E', 81, 2);
        r.Platform(37, 8, 2)                                                    // a girder over the pit
         .Item(37, 9, ItemPickup.Kind.Module, PlayerUpgrades.Module.ReinforcedPlating)
         .Item(67, 2, ItemPickup.Kind.RepairKit);
        rooms.Add(r);

        // ---------------- SECTION 02 - STORAGE ----------------
        // A low corridor with three presses (watch the shadows), scanners over the racks,
        // a belt running against the player, and relay 2 up in the storage loft.
        r = new Room("Section_02_Storage", 96, 22)
        {
            title = "SECTION 02 // STORAGE",
            hint = "A SHADOW ON THE FLOOR MEANS A PRESS IS COMING DOWN.  STAY OUT OF THE SCANNER BEAMS",
            next = "Section_03_Foreman", requiredRelays = 2,
            background = new Color(0.06f, 0.06f, 0.07f), tint = new Color(0.75f, 0.77f, 0.8f)
        };
        r.Solid(0, 0, 96, 2)
         .Platform(8, 5, 6).Platform(12, 8, 4)             // storage racks at the start
         .Solid(17, 12, 19, 9)                             // low ceiling over the press corridor
         .Belt(52, 1, 16, -2.5f)                           // belt against the player
         .Platform(70, 5, 5).Platform(76, 8, 5).Solid(82, 11, 13, 1);   // climb to the loft
        r.Press(19, 3, 3f, 0f, 12).Press(25, 3, 3f, 1f, 12).Press(31, 3, 3f, 2f, 12);
        // Scanners fly low enough for their beams (9 units) to reach the floor
        r.Add('P', 2, 2).Add('C', 37, 2).Add('D', 44, 8).Add('B', 48, 2).Add('D', 60, 9).Add('B', 64, 2).Add('G', 42, 2).Add('G', 80, 2)
         .Add('C', 71, 2).Relay(90, 12, 2).Add('D', 86, 18)
         .Add('r', 4, 2).Add('r', 39, 2).Add('v', 46, 16).Add('r', 84, 12).Add('E', 93, 2);
        r.Item(13, 9, ItemPickup.Kind.Module, PlayerUpgrades.Module.DashCapacitor)       // on top of the racks
         .Item(38, 2, ItemPickup.Kind.EnergyShield)
         .Item(87, 12, ItemPickup.Kind.RepairKit)
         .Item(93, 12, ItemPickup.Kind.Module, PlayerUpgrades.Module.ShieldEmitter);   // end of the loft
        rooms.Add(r);

        // ---------------- SECTION 03 - FOREMAN (mini-boss arena) ----------------
        // Two belts make up the floor, four presses hang from the ceiling (the Foreman drops them),
        // the Foreman stands on its post (balcony, right) and slams its manipulator from the ceiling rail.
        // The door under the balcony opens when the Foreman is offline.
        r = new Room("Section_03_Foreman", 44, 20)
        {
            title = "SECTION 03 // ASSEMBLY HALL",
            hint = "WAIT FOR THE MANIPULATOR TO GET STUCK - THEN STRIKE IT",
            next = "Section_04_Relay",
            background = new Color(0.1f, 0.05f, 0.03f), tint = new Color(1f, 0.8f, 0.65f),
            arenaMinX = 2, arenaMaxX = 34, activationX = 6
        };
        r.Solid(0, 0, 44, 2).Belt(2, 1, 19, 2f).Belt(23, 1, 19, -2f)
         .Platform(4, 6, 5).Platform(31, 6, 4)
         .Solid(36, 9, 7, 1);                                         // the Foreman's post
        r.Press(10, 3).Press(16, 3).Press(22, 3).Press(27, 3);       // never above the platforms
        r.Add('P', 1, 2).Add('X', 39, 10).Add('v', 12, 13).Add('v', 28, 13).Add('E', 41, 2);
        r.Item(2, 2, ItemPickup.Kind.EnergyShield)
         .Item(38, 2, ItemPickup.Kind.Module, PlayerUpgrades.Module.ActuatorOverclock);  // under the Foreman's post
        rooms.Add(r);

        // ---------------- SECTION 04 - THIRD POWER RELAY ----------------
        // A quiet room after the Foreman: checkpoint, relay 3 on a small dais, the door to the control room.
        r = new Room("Section_04_Relay", 52, 18)
        {
            title = "SECTION 04 // POWER DISTRIBUTION",
            next = "Section_05_ControlRoom", requiredRelays = 3,
            background = new Color(0.05f, 0.07f, 0.06f), tint = new Color(0.8f, 0.88f, 0.8f)
        };
        r.Solid(0, 0, 52, 2).Solid(27, 2, 7, 1).Platform(12, 5, 5);
        r.Add('P', 2, 2).Add('C', 6, 2).Relay(30, 3, 3)
         .Add('r', 20, 2).Add('r', 23, 2).Add('r', 37, 2).Add('r', 40, 2).Add('v', 15, 11).Add('v', 44, 10).Add('E', 49, 2);
        r.Item(14, 6, ItemPickup.Kind.Module, PlayerUpgrades.Module.ServoOverdrive)      // maintenance shelf
         .Item(8, 2, ItemPickup.Kind.RepairKit)
         .Item(45, 2, ItemPickup.Kind.EnergyShield);
        rooms.Add(r);

        // ---------------- SECTION 05 - CONTROL ROOM (final boss arena) ----------------
        // The Director's monitor hangs over the arena ('Y'). Wall manipulators on both walls (phase 1),
        // service doors for the Guards (phase 2), crusher walls and the control panel (phase 3).
        // The surface hatch (exit, right) opens when the control panel is destroyed.
        r = new Room("Section_05_ControlRoom", 48, 22)
        {
            title = "SECTION 05 // CONTROL ROOM",
            next = "Ending_Surface", requiredRelays = 3,
            background = new Color(0.08f, 0.03f, 0.03f), tint = new Color(0.95f, 0.75f, 0.7f),
            activationX = 5
        };
        r.Solid(0, 0, 48, 2).Belt(3, 1, 20, 2f).Belt(25, 1, 20, -2f)
         .Platform(6, 7, 5).Platform(37, 7, 5).Platform(20, 11, 8);
        r.Press(12, 3).Press(16, 3).Press(29, 3).Press(33, 3);       // never above the platforms
        r.Add('P', 4, 2).Add('Y', 24, 16).Add('E', 46, 2);
        r.Item(8, 8, ItemPickup.Kind.RepairKit)
         .Item(39, 8, ItemPickup.Kind.RepairKit)
         .Item(24, 12, ItemPickup.Kind.PressOverdrive);
        rooms.Add(r);

        return rooms;
    }

    static string BuildRoom(Room room)
    {
        LoadTiles();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        string path = SceneFolder + room.scene + ".unity";

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

        // Pit death zone under the section
        var deathZone = new GameObject("DeathZone") { tag = "DeathZone" };
        deathZone.transform.position = new Vector3(room.width * 0.5f, -3f, 0f);
        var deathCollider = deathZone.AddComponent<BoxCollider2D>();
        deathCollider.isTrigger = true;
        deathCollider.size = new Vector2(room.width + 20f, 2f);

        // ---------- Things ----------
        var decor = new GameObject("Decor").transform;
        var machinery = new GameObject("Machinery").transform;
        var enemies = new GameObject("Enemies").transform;
        var interactables = new GameObject("Interactables").transform;
        Transform spawn = null;
        GameObject playerObject = null;
        ConveyorBelt introBelt = null;
        LevelExit roomExit = null;
        GameObject foremanObject = null;
        Thing directorMarker = null;
        float furnaceX = 0f;
        var cargo = new List<Transform>();

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
                    GameObject guard = Spawn(GuardPrefabPath, feet, enemies);
                    SetupPatrol(room, guard, t.x, t.y);
                    break;
                case 'D':
                    Spawn(ScannerPrefabPath, new Vector3(t.x + 0.5f, t.y + 0.5f, 0f), enemies);
                    break;
                case 'G':
                    NewServiceDoor(feet, interactables);
                    break;
                case 'R':
                    GameObject relayObject = Spawn(RelayPrefabPath, feet, interactables);
                    relayObject.name = "PowerRelay_" + t.index;
                    var relay = relayObject.GetComponent<PowerRelay>();
                    if (RelayLogs.TryGetValue(t.index, out var log))
                    {
                        relay.logTitle = log.title;
                        relay.logLines = log.lines;
                    }
                    break;
                case 'C':
                    Spawn(CheckpointPrefabPath, feet, interactables);
                    break;
                case 'E':
                    GameObject exit = Spawn(ExitPrefabPath, feet, interactables);
                    var levelExit = exit.GetComponent<LevelExit>();
                    levelExit.nextSceneName = room.next;
                    levelExit.requiredRelays = room.requiredRelays;
                    roomExit = levelExit;
                    break;
                case 'X':
                    foremanObject = Spawn(ForemanPrefabPath, new Vector3(t.x + 0.5f, t.y, 0f), null);
                    break;
                case 'Y':
                    directorMarker = t;
                    break;
                case 'K':
                    ConveyorBelt belt = NewBelt(t, machinery);
                    if (t.value2 > 0f) introBelt = belt;
                    break;
                case 'M':
                    NewPress(t, machinery);
                    break;
                case 'F':
                    NewFurnace(t, decor);
                    furnaceX = t.x + t.width;
                    break;
                case 'U':
                    cargo.Add(NewCargoUnit(feet, decor));
                    break;
                case 'I':
                    NewItem(t, interactables);
                    break;
                case 'r':
                    NewDecor("Cabinet", LoadSprite(ArtEnv + "ServerRack.png"), feet, decor, room.tint);
                    break;
                case 'v':
                    NewDecor("Vent", LoadSprite(ArtEnv + "Vent.png"), new Vector3(t.x + 0.5f, t.y + 0.5f, 0f), decor, room.tint);
                    break;
            }
        }

        if (foremanObject != null)
        {
            var foreman = foremanObject.GetComponent<ForemanController>();
            foreman.railY = room.height - 1;
            foreman.minX = room.arenaMinX;
            foreman.maxX = room.arenaMaxX;
            foreman.activationX = room.activationX;
            if (roomExit != null)
            {
                roomExit.requiredBoss = foremanObject.GetComponent<EnemyHealth>();
                roomExit.bossLockedMessage = "ACCESS DENIED - FOREMAN ON DUTY";
            }
        }

        if (directorMarker != null)
        {
            DirectorController director = NewDirectorArena(room, directorMarker);
            if (roomExit != null)
            {
                roomExit.requiredBoss = director.controlPanel;
                roomExit.bossLockedMessage = "SURFACE HATCH: LOCKED BY THE DIRECTOR";
            }
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
        if (room.intro)
        {
            // The intro shows the section title once the player gets control
            var intro = new GameObject("IntroSequence").AddComponent<IntroSequence>();
            intro.belt = introBelt;
            intro.cargo = cargo.ToArray();
            intro.furnaceX = furnaceX;
            intro.sectionTitle = room.title;
            intro.controlsHint = room.hint;
        }
        else
        {
            ui.roomTitle = room.title;
            ui.roomHint = room.hint;
        }
        NewEventSystem(null);

        // Same rule as SoundGenerator.MusicForScene
        NewMusic(SoundGenerator.MusicForScene(path));

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

    // Patrol points = how far the Guard can walk on its platform (max 5 tiles each way)
    static void SetupPatrol(Room room, GameObject guard, int x, int y)
    {
        System.Func<int, bool> walkable = cx =>
        {
            char below = room.Cell(cx, y - 1);
            char at = room.Cell(cx, y);
            return (below == '#' || below == '=' || below == 'b') && at != '#' && at != 'b' && at != '^' && at != '~';
        };
        int left = x, right = x;
        while (left - 1 >= 0 && x - (left - 1) <= 5 && walkable(left - 1)) left--;
        while (right + 1 < room.width && (right + 1) - x <= 5 && walkable(right + 1)) right++;

        var ai = guard.GetComponent<GuardAI>();
        ai.pointA.position = new Vector3(left + 0.6f, y, 0f);
        ai.pointB.position = new Vector3(right + 0.4f, y, 0f);
    }

    // ---------- CONVEYOR machinery (art = Assets/Art/Placeholders, replaceable) ----------

    static Sprite PlaceholderSprite(string name) => LoadSprite(PlaceholderArt.PathOf(name));

    // 3D sound that is only heard near the object (the camera sits 10 units in front of the scene)
    static void MakeSpatial(AudioSource source, float volume)
    {
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 11f;
        source.maxDistance = 24f;
        source.volume = volume;
    }

    // Belt: one tile high, solid (Ground layer), the floor tiles under it stay in row y-1
    static ConveyorBelt NewBelt(Thing t, Transform parent)
    {
        var go = new GameObject("ConveyorBelt") { layer = GroundLayer };
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(t.x + t.width * 0.5f, t.y + 0.5f, 0f);
        var col = go.AddComponent<BoxCollider2D>();
        col.size = new Vector2(t.width, 1f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = PlaceholderSprite(PlaceholderArt.ConveyorBelt);
        sr.drawMode = SpriteDrawMode.Tiled;
        sr.size = new Vector2(t.width, 1f);
        sr.sortingOrder = SortTiles;

        var belt = go.AddComponent<ConveyorBelt>();
        belt.speed = t.value;
        belt.length = t.width;
        belt.beltRenderer = sr;
        Transform audio = AddAudioRoot(go);
        belt.humSound = AddSound(audio, "SFX_BeltHum", true);
        MakeSpatial(belt.humSound, 0.35f);
        belt.reverseSound = AddSound(audio, "SFX_BeltReverse");
        MakeSpatial(belt.reverseSound, 0.8f);
        return belt;
    }

    // Press: hangs from ceilingY (t.y) over x..x+width
    static IndustrialPress NewPress(Thing t, Transform parent)
    {
        var go = new GameObject("IndustrialPress");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(t.x + t.width * 0.5f, t.y, 0f);

        var press = go.AddComponent<IndustrialPress>();
        press.width = t.width;
        press.ceilingY = t.y;
        press.groundLayer = 1 << GroundLayer;
        press.autoCycle = t.value > 0f;
        press.cycleInterval = Mathf.Max(t.value, 1f);
        press.cycleOffset = t.value2;

        Sprite headSprite = PlaceholderSprite(PlaceholderArt.PressHead);
        press.head = NewChildSprite(go, "Head", headSprite, SortProps + 1);
        press.head.transform.localScale = new Vector3(t.width / headSprite.bounds.size.x, press.headHeight / headSprite.bounds.size.y, 1f);

        press.rod = NewChildSprite(go, "Rod", PlaceholderSprite(PlaceholderArt.PressRod), SortProps);
        press.rod.drawMode = SpriteDrawMode.Tiled;
        press.rod.size = new Vector2(0.5f, 1f);

        press.shadow = NewChildSprite(go, "Shadow", PlaceholderSprite(PlaceholderArt.ShadowBlob), SortHazards);
        press.lamp = NewChildSprite(go, "Lamp", PlaceholderSprite(PlaceholderArt.LampDot), SortProps + 2);

        Transform audio = AddAudioRoot(go);
        press.warningSound = AddSound(audio, "SFX_PressWarning");
        MakeSpatial(press.warningSound, 0.6f);
        press.impactSound = AddSound(audio, "SFX_PressImpact");
        MakeSpatial(press.impactSound, 1f);
        return press;
    }

    static SpriteRenderer NewChildSprite(GameObject parent, string name, Sprite sprite, int order)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent.transform, false);
        var sr = child.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        return sr;
    }

    // Furnace mouth at the end of the transport line: glowing, flickering, burns on touch
    static void NewFurnace(Thing t, Transform parent)
    {
        var go = new GameObject("Furnace");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(t.x + t.width * 0.5f, t.y, 0f);
        Sprite sprite = PlaceholderSprite(PlaceholderArt.Furnace);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = SortDecor + 1;
        go.transform.localScale = Vector3.one * (t.width / sprite.bounds.size.x);
        go.AddComponent<FlickerLight>().minBrightness = 0.75f;

        var heat = new GameObject("FurnaceHeat") { tag = "Hazard" };
        heat.transform.SetParent(go.transform, true);
        heat.transform.position = new Vector3(t.x + t.width * 0.5f, t.y + 1.25f, 0f);
        var col = heat.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(t.width / go.transform.localScale.x, 2.5f / go.transform.localScale.y);
    }

    // The Director's control room: monitor, two wall manipulators, control panel with armour,
    // guard/scanner spawn points, two crusher walls and a red light overlay for phase 3.
    static DirectorController NewDirectorArena(Room room, Thing marker)
    {
        var root = new GameObject("Director");
        root.transform.position = new Vector3(marker.x, marker.y, 0f);
        var director = root.AddComponent<DirectorController>();
        director.activationX = room.activationX;
        director.guardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GuardPrefabPath);
        director.scannerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScannerPrefabPath);

        // Monitor (8 x 4.8 units)
        director.faceCalm = PlaceholderSprite(PlaceholderArt.DirectorCalm);
        director.faceAngry = PlaceholderSprite(PlaceholderArt.DirectorAngry);
        director.faceBroken = PlaceholderSprite(PlaceholderArt.DirectorBroken);
        var monitorHolder = new GameObject("Monitor");
        monitorHolder.transform.SetParent(root.transform, false);
        monitorHolder.transform.localScale = Vector3.one * (8f / director.faceCalm.bounds.size.x);
        director.screen = NewChildSprite(monitorHolder, "Screen", director.faceCalm, SortDecor + 4);

        // Wall manipulators: left wall shoots right, right wall shoots left
        var units = new List<WallManipulator>();
        foreach (int side in new[] { -1, 1 })
        {
            float x = side < 0 ? 1.75f : room.width - 1.75f;
            var unit = new GameObject(side < 0 ? "WallManipulator_L" : "WallManipulator_R") { tag = "Boss", layer = EnemyLayer };
            unit.transform.SetParent(root.transform, true);
            unit.transform.position = new Vector3(x, 2f, 0f);
            var sr = unit.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderSprite(PlaceholderArt.WallUnit);
            sr.sortingOrder = SortBoss;
            sr.flipX = side > 0;
            var col = unit.AddComponent<BoxCollider2D>();
            col.size = new Vector2(1.4f, 3f);
            col.offset = new Vector2(0f, 1.5f);
            var health = unit.AddComponent<EnemyHealth>();
            health.maxHealth = 4;
            health.receivesKnockback = false;
            health.destroyOnDeath = false;
            health.explosionColor = new Color(1f, 0.5f, 0.3f);
            health.explosionSize = 1.6f;
            Transform audio = AddAudioRoot(unit);
            health.hitSound = AddSound(audio, "SFX_BossHit");
            var manipulator = unit.AddComponent<WallManipulator>();
            manipulator.direction = -side;
            manipulator.housing = sr;
            manipulator.pistonY = 2.5f;
            manipulator.piston = NewChildSprite(unit, "Piston", PlaceholderSprite(PlaceholderArt.PressRod), SortBoss - 1);
            manipulator.piston.drawMode = SpriteDrawMode.Tiled;
            manipulator.piston.size = new Vector2(0.1f, 0.5f);
            manipulator.thrustSound = AddSound(audio, "SFX_BeltReverse");
            MakeSpatial(manipulator.thrustSound, 0.8f);
            units.Add(manipulator);
        }
        director.wallManipulators = units.ToArray();

        // Control panel (final target) with its armour
        var panel = new GameObject("ControlPanel") { tag = "Boss", layer = EnemyLayer };
        panel.transform.SetParent(root.transform, true);
        panel.transform.position = new Vector3(room.width * 0.5f, 2f, 0f);
        var panelRenderer = panel.AddComponent<SpriteRenderer>();
        panelRenderer.sprite = PlaceholderSprite(PlaceholderArt.ControlPanel);
        panelRenderer.sortingOrder = SortProps;
        var panelCollider = panel.AddComponent<BoxCollider2D>();
        panelCollider.size = new Vector2(2f, 1.5f);
        panelCollider.offset = new Vector2(0f, 0.75f);
        var panelHealth = panel.AddComponent<EnemyHealth>();
        panelHealth.maxHealth = 5;
        panelHealth.receivesKnockback = false;
        panelHealth.destroyOnDeath = false;
        panelHealth.keepWreck = true;
        panelHealth.deathFadeTime = 1.5f;
        panelHealth.explosionColor = new Color(1f, 0.35f, 0.25f);
        panelHealth.hitSound = AddSound(AddAudioRoot(panel), "SFX_BossHit");
        director.controlPanel = panelHealth;
        director.panelShield = NewChildSprite(panel, "Shield", PlaceholderSprite(PlaceholderArt.PanelShield), SortProps + 1);

        // Spawn points (doors are decoration here: the Director sends the Guards itself)
        var guardSpawns = new List<Transform>();
        foreach (float x in new[] { 10.5f, room.width - 10.5f })
        {
            var door = new GameObject("SecurityDoor");
            door.transform.SetParent(root.transform, true);
            door.transform.position = new Vector3(x, 2f, 0f);
            NewChildSprite(door, "Door", PlaceholderSprite(PlaceholderArt.ServiceDoor), SortDecor + 2);
            guardSpawns.Add(door.transform);
        }
        director.guardSpawns = guardSpawns.ToArray();
        var scannerSpawns = new List<Transform>();
        foreach (float x in new[] { 8f, room.width - 8f })
        {
            var spawn = new GameObject("ScannerSpawn").transform;
            spawn.SetParent(root.transform, true);
            spawn.position = new Vector3(x, room.height - 4f, 0f);
            scannerSpawns.Add(spawn);
        }
        director.scannerSpawns = scannerSpawns.ToArray();

        // Crusher walls: start inside the outer walls, move in by crusherTravel in phase 3
        float wallHeight = room.height - 3f;
        foreach (int side in new[] { -1, 1 })
        {
            var crusher = new GameObject(side < 0 ? "Crusher_L" : "Crusher_R") { layer = GroundLayer };
            crusher.transform.SetParent(root.transform, true);
            crusher.transform.position = new Vector3(side < 0 ? -0.5f : room.width + 0.5f, 2f + wallHeight * 0.5f, 0f);
            var body = crusher.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            var col = crusher.AddComponent<BoxCollider2D>();
            col.size = new Vector2(3f, wallHeight);
            var sr = crusher.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderSprite(PlaceholderArt.CrusherWall);
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(3f, wallHeight);
            sr.sortingOrder = SortTiles + 1;
            if (side < 0) director.leftCrusher = crusher.transform;
            else director.rightCrusher = crusher.transform;
        }
        director.crusherTravel = 10f;

        // Red light (phase 3): a transparent overlay over the whole room
        director.redLight = NewChildSprite(root, "RedLight", PlaceholderSprite(PlaceholderArt.WhitePixel), 25);
        Vector2 pixel = director.redLight.sprite.bounds.size;
        director.redLight.transform.position = new Vector3(room.width * 0.5f, room.height * 0.5f, 0f);
        director.redLight.transform.localScale = new Vector3(room.width / pixel.x, room.height / pixel.y, 1f);
        director.redLight.color = new Color(1f, 0.1f, 0.05f, 0.18f);

        Transform rootAudio = AddAudioRoot(root);
        director.voiceSound = AddSound(rootAudio, "SFX_ScanBeep");
        director.alarmSound = AddSound(rootAudio, "SFX_Alarm");
        director.deathSound = AddSound(rootAudio, "SFX_BossDeath");
        return director;
    }

    // Service door in the back wall: a Guard comes out of it when an alarm is raised nearby
    static void NewServiceDoor(Vector3 feet, Transform parent)
    {
        var go = new GameObject("ServiceDoor");
        go.transform.SetParent(parent, false);
        go.transform.position = feet;
        var spawnPoint = go.AddComponent<GuardSpawnPoint>();
        spawnPoint.guardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GuardPrefabPath);
        spawnPoint.doorRenderer = NewChildSprite(go, "Door", PlaceholderSprite(PlaceholderArt.ServiceDoor), SortDecor + 2);
        spawnPoint.lamp = NewChildSprite(go, "Lamp", PlaceholderSprite(PlaceholderArt.LampDot), SortDecor + 3);
        spawnPoint.lamp.transform.localPosition = new Vector3(0f, 1.9f, 0f);
        spawnPoint.openSound = AddSound(AddAudioRoot(go), "SFX_BeltReverse");
        MakeSpatial(spawnPoint.openSound, 0.7f);
    }

    // Floating pickup: icon + soft halo in the item colour
    static void NewItem(Thing t, Transform parent)
    {
        var kind = (ItemPickup.Kind)t.index;
        var module = (PlayerUpgrades.Module)Mathf.RoundToInt(t.value);
        var go = new GameObject(kind == ItemPickup.Kind.Module ? "Module_" + module : "Item_" + kind);
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(t.x + 0.5f, t.y + 0.6f, 0f);
        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.55f;

        string icon = kind == ItemPickup.Kind.RepairKit ? PlaceholderArt.ItemRepair
                    : kind == ItemPickup.Kind.EnergyShield ? PlaceholderArt.ItemShield
                    : kind == ItemPickup.Kind.PressOverdrive ? PlaceholderArt.ItemOverdrive
                    : PlaceholderArt.ItemModule;
        var pickup = go.AddComponent<ItemPickup>();
        pickup.kind = kind;
        pickup.module = module;
        pickup.glow = NewChildSprite(go, "Glow", PlaceholderSprite(PlaceholderArt.GlowSoft), SortProps + 3);
        pickup.glow.transform.localScale = Vector3.one * (1.6f / pickup.glow.sprite.bounds.size.x);
        pickup.itemRenderer = NewChildSprite(go, "Icon", PlaceholderSprite(icon), SortProps + 4);
        pickup.itemRenderer.transform.localScale = Vector3.one * 1.3f;
        pickup.pickupSound = AddSound(AddAudioRoot(go), "SFX_Pickup");
    }

    // A dark, switched-off robot lying on the transport line (same model as Unit 0412)
    static Transform NewCargoUnit(Vector3 feet, Transform parent)
    {
        var go = new GameObject("DefectiveUnit");
        go.transform.SetParent(parent, false);
        go.transform.position = feet;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite(ArtRobot + "Robot.png");
        sr.color = new Color(0.38f, 0.36f, 0.35f);
        sr.sortingOrder = SortEnemies - 1;
        return go.transform;
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
        cam.backgroundColor = new Color(0.06f, 0.05f, 0.04f);
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        cameraObject.AddComponent<AudioListener>();

        GameObject canvas = NewCanvas("MainMenuCanvas");
        var ui = canvas.AddComponent<UIManager>();
        Color amber = new Color(1f, 0.78f, 0.35f);

        NewText("Title", canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 360f), new Vector2(1600f, 140f),
            "CONVEYOR", 120, TextAnchor.MiddleCenter, amber);
        NewText("Subtitle", canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 270f), new Vector2(1600f, 60f),
            "UNIT 0412  //  STATUS: DEFECTIVE  //  ROUTE: FURNACE", 38, TextAnchor.MiddleCenter, Color.white);

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
            "A/D MOVE   SPACE JUMP / DOUBLE JUMP   J PRESS ARM   E INTERACT   (GAMEPAD SUPPORTED)", 30, TextAnchor.MiddleCenter, new Color(0.85f, 0.8f, 0.7f));

        NewEventSystem(start.gameObject);
        NewMusic("Music_Menu");

        EditorSceneManager.SaveScene(scene, path);
        return path;
    }

    // =====================================================================
    // ENDING (the surface)
    // =====================================================================

    // The whole ending is played by EndingSequence, which builds its visuals from code
    static string BuildEnding()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        string path = SceneFolder + "Ending_Surface.unity";

        var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = cameraObject.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 7f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        cameraObject.AddComponent<AudioListener>();

        var ending = new GameObject("EndingSequence").AddComponent<EndingSequence>();
        ending.robotSprite = LoadSprite(ArtRobot + "Robot.png");
        ending.beltSprite = PlaceholderSprite(PlaceholderArt.ConveyorBelt);

        NewEventSystem(null);
        NewMusic(SoundGenerator.MusicForScene(path));

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
