using System;
using System.Collections.Generic;
using System.IO;
using SecretJenga.Game;
using SecretJenga.Networking;
using SecretJenga.UI;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SecretJenga.EditorTools
{
    /// <summary>
    /// Tools > Secret Jenga > Generate Project
    /// Creates (or refreshes - it is safe to run again) every generated asset of the game:
    /// texture/audio import settings, URP materials, procedural meshes (+ OBJ export), physics materials,
    /// network prefabs, the NGO prefab list, the asset registry, the post-processing profile,
    /// the fully wired game scene and the player/build settings.
    /// </summary>
    public static class SecretJengaProjectGenerator
    {
        public const string Root = "Assets/SecretJenga";
        const string Tex = Root + "/Textures";
        const string UITex = Root + "/Textures/UI";
        const string Mats = Root + "/Materials";
        const string Models = Root + "/Models";
        const string Prefabs = Root + "/Prefabs";
        const string Audio = Root + "/Audio";
        const string Scenes = Root + "/Scenes";
        const string Res = Root + "/Resources";
        public const string ScenePath = Scenes + "/SecretJenga.unity";

        class Ctx
        {
            public readonly Dictionary<string, Material> M = new Dictionary<string, Material>();
            public readonly Dictionary<string, Mesh> Mesh = new Dictionary<string, Mesh>();
            public GameObject BlockPrefab, SessionPrefab, EnvPrefab;
            public SecretJengaAssets Registry;
            public NetworkPrefabsList PrefabList;
            public VolumeProfile Volume;
        }

        [MenuItem("Tools/Secret Jenga/Generate Project", priority = 0)]
        public static void Generate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Secret Jenga", "Exit Play Mode first.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var ctx = new Ctx();
            try
            {
                Step("Folders", 0.02f); EnsureFolders();
                Step("Texture import settings", 0.08f); ConfigureTextures();
                Step("Audio import settings", 0.15f); ConfigureAudio();
                Step("Materials", 0.22f); CreateMaterials(ctx);
                Step("Meshes + OBJ export", 0.32f); CreateMeshes(ctx);
                Step("Block prefab", 0.42f); CreateBlockPrefab(ctx);
                Step("Session prefab", 0.48f); CreateSessionPrefab(ctx);
                Step("Environment prefab", 0.55f); CreateEnvironmentPrefab(ctx);
                Step("Network prefab list", 0.62f); CreatePrefabList(ctx);
                Step("Asset registry", 0.68f); CreateRegistry(ctx);
                Step("Post processing", 0.74f); CreateVolumeProfile(ctx);
                Step("Scene", 0.82f); CreateScene(ctx);
                Step("Project settings", 0.94f); ConfigureProjectSettings();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Secret Jenga", "Generation failed:\n" + e.Message + "\n\nSee the Console for details.", "OK");
                return;
            }
            EditorUtility.ClearProgressBar();
            Debug.Log("[SecretJenga] Project generated. Open " + ScenePath + " and press Play.");
            EditorUtility.DisplayDialog("Secret Jenga",
                "Project generated!\n\nScene: " + ScenePath + " (opened and added to Build Settings).\n\n" +
                "Before playing online make sure this project is linked to Unity Cloud (Edit > Project Settings > Services) " +
                "and that Relay + Lobby are enabled in the Unity Dashboard. See Assets/SecretJenga/README.md.", "Great");
        }

        static void Step(string what, float p) => EditorUtility.DisplayProgressBar("Secret Jenga", what + "...", p);

        // ------------------------------------------------------------------ Folders
        static void EnsureFolders()
        {
            foreach (var f in new[] { Root, Root + "/Scripts", Root + "/Editor", Prefabs, Mats, Models, Models + "/OBJ", Scenes, Tex, UITex, Audio, Res })
                EnsureFolder(f);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        // ------------------------------------------------------------------ Import settings
        static void ConfigureTextures()
        {
            AssetDatabase.Refresh();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Tex }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                bool changed = false;
                void Set<T>(Func<T> get, Action<T> set, T v) { if (!EqualityComparer<T>.Default.Equals(get(), v)) { set(v); changed = true; } }

                if (path.StartsWith(UITex))
                {
                    Set(() => ti.textureType, v => ti.textureType = v, TextureImporterType.Sprite);
                    Set(() => ti.spriteImportMode, v => ti.spriteImportMode = v, SpriteImportMode.Single);
                    Set(() => ti.mipmapEnabled, v => ti.mipmapEnabled = v, false);
                    Set(() => ti.alphaIsTransparency, v => ti.alphaIsTransparency = v, true);
                    Set(() => ti.wrapMode, v => ti.wrapMode = v, TextureWrapMode.Clamp);
                    var border = SpriteBorder(Path.GetFileNameWithoutExtension(path));
                    Set(() => ti.spriteBorder, v => ti.spriteBorder = v, border);
                }
                else if (path.Contains("_Normal"))
                {
                    Set(() => ti.textureType, v => ti.textureType = v, TextureImporterType.NormalMap);
                    Set(() => ti.wrapMode, v => ti.wrapMode = v, TextureWrapMode.Repeat);
                    Set(() => ti.anisoLevel, v => ti.anisoLevel = v, 4);
                }
                else if (path.Contains("DustParticle"))
                {
                    Set(() => ti.alphaIsTransparency, v => ti.alphaIsTransparency = v, true);
                    Set(() => ti.wrapMode, v => ti.wrapMode = v, TextureWrapMode.Clamp);
                }
                else
                {
                    Set(() => ti.textureType, v => ti.textureType = v, TextureImporterType.Default);
                    Set(() => ti.sRGBTexture, v => ti.sRGBTexture = v, true);
                    Set(() => ti.wrapMode, v => ti.wrapMode = v, TextureWrapMode.Repeat);
                    Set(() => ti.anisoLevel, v => ti.anisoLevel = v, 4);
                }
                if (changed) ti.SaveAndReimport();
            }
        }

        static Vector4 SpriteBorder(string file)
        {
            switch (file)
            {
                case "UI_Panel": return new Vector4(26, 26, 26, 26);
                case "UI_Button": return new Vector4(24, 24, 24, 24);
                case "UI_Rounded": return new Vector4(18, 18, 18, 18);
                case "UI_Pill": return new Vector4(31, 31, 31, 31);
                case "UI_Card": return new Vector4(30, 30, 30, 30);
                default: return Vector4.zero;
            }
        }

        static void ConfigureAudio()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Audio }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ai = AssetImporter.GetAtPath(path) as AudioImporter;
                if (ai == null) continue;
                var s = ai.defaultSampleSettings;
                bool music = path.Contains("MUS_");
                var load = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
                if (s.loadType == load && s.compressionFormat == AudioCompressionFormat.Vorbis) continue;
                s.loadType = load;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = music ? 0.6f : 0.8f;
                ai.defaultSampleSettings = s;
                ai.SaveAndReimport();
            }
        }

        // ------------------------------------------------------------------ Materials
        static Shader Lit => Shader.Find("Universal Render Pipeline/Lit");

        static T LoadOrCreate<T>(string path, Func<T> create) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var obj = create();
            AssetDatabase.CreateAsset(obj, path);
            return obj;
        }

        static Texture2D T(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/" + file);

        static Material LitMat(Ctx ctx, string name, Color color, float smooth, Texture2D albedo = null, Texture2D normal = null, Vector2? tiling = null, float metallic = 0f, Color? emission = null, bool doubleSided = false)
        {
            var path = Mats + "/" + name + ".mat";
            var m = LoadOrCreate(path, () => new Material(Lit));
            if (m.shader != Lit) m.shader = Lit;
            m.SetColor("_BaseColor", color);
            m.color = color;
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metallic);
            m.SetTexture("_BaseMap", albedo);
            m.mainTexture = albedo;
            var t = tiling ?? Vector2.one;
            m.SetTextureScale("_BaseMap", t);
            if (normal != null)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetTextureScale("_BumpMap", t);
                m.SetFloat("_BumpScale", 0.7f);
                m.EnableKeyword("_NORMALMAP");
            }
            else m.DisableKeyword("_NORMALMAP");
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission ?? Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (doubleSided && m.HasProperty("_Cull")) { m.SetFloat("_Cull", 0f); m.doubleSidedGI = true; }
            EditorUtility.SetDirty(m);
            ctx.M[name] = m;
            return m;
        }

        static void CreateMaterials(Ctx ctx)
        {
            var blockNormal = T("T_BlockWood_Normal.png");
            for (int i = 0; i < 3; i++) LitMat(ctx, "M_BlockWood_" + i, Color.white, 0.35f, T($"T_BlockWood_{i}_Albedo.png"), blockNormal);
            LitMat(ctx, "M_BlockEndGrain", new Color(0.95f, 0.9f, 0.85f), 0.25f, T("T_BlockEndGrain_Albedo.png"));
            LitMat(ctx, "M_TableWalnut", Color.white, 0.62f, T("T_TableWalnut_Albedo.png"), T("T_TableWalnut_Normal.png"));
            LitMat(ctx, "M_TableLeg", new Color(0.85f, 0.8f, 0.75f), 0.5f, T("T_TableWalnut_Albedo.png"), null, new Vector2(1, 3));
            LitMat(ctx, "M_FloorOak", Color.white, 0.35f, T("T_FloorOak_Albedo.png"), T("T_FloorOak_Normal.png"), new Vector2(5, 5));
            LitMat(ctx, "M_Wallpaper", Color.white, 0.08f, T("T_Wallpaper_Albedo.png"));
            LitMat(ctx, "M_WallTrim", new Color(0.55f, 0.42f, 0.34f), 0.4f, T("T_TableWalnut_Albedo.png"));
            LitMat(ctx, "M_Rug", Color.white, 0.02f, T("T_Rug_Albedo.png"));
            LitMat(ctx, "M_Ceiling", new Color(0.25f, 0.2f, 0.17f), 0.05f);
            LitMat(ctx, "M_LampShade", new Color(0.12f, 0.30f, 0.22f), 0.75f, null, null, null, 0.4f, null, true);
            LitMat(ctx, "M_Brass", new Color(0.78f, 0.6f, 0.3f), 0.7f, null, null, null, 0.9f);
            LitMat(ctx, "M_Bulb", new Color(1f, 0.9f, 0.7f), 0.9f, null, null, null, 0f, new Color(1f, 0.75f, 0.45f) * 6f);
            LitMat(ctx, "M_Window", new Color(0.08f, 0.1f, 0.18f), 0.95f, null, null, null, 0f, new Color(0.25f, 0.35f, 0.65f) * 1.4f);
            LitMat(ctx, "M_Ceramic", new Color(0.92f, 0.9f, 0.85f), 0.8f);
            LitMat(ctx, "M_CardBack", new Color(0.55f, 0.12f, 0.12f), 0.45f);
            LitMat(ctx, "M_Dice", new Color(0.95f, 0.93f, 0.88f), 0.7f);
            LitMat(ctx, "M_BookA", new Color(0.45f, 0.12f, 0.10f), 0.3f);
            LitMat(ctx, "M_BookB", new Color(0.12f, 0.25f, 0.40f), 0.3f);
            LitMat(ctx, "M_BookC", new Color(0.18f, 0.35f, 0.20f), 0.3f);
            LitMat(ctx, "M_BookD", new Color(0.65f, 0.5f, 0.2f), 0.3f);
            LitMat(ctx, "M_Cursor", Color.white, 0.8f, null, null, null, 0f, Color.white);

            // ghost (transparent unlit)
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var ghost = LoadOrCreate(Mats + "/M_Ghost.mat", () => new Material(unlit));
            ghost.shader = unlit;
            RuntimeArt.MakeTransparent(ghost);
            ghost.SetColor("_BaseColor", new Color(1f, 0.9f, 0.6f, 0.3f));
            EditorUtility.SetDirty(ghost);
            ctx.M["M_Ghost"] = ghost;

            // dust particles
            var pShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var dust = LoadOrCreate(Mats + "/M_Dust.mat", () => new Material(pShader));
            dust.shader = pShader;
            RuntimeArt.MakeTransparent(dust);
            dust.SetTexture("_BaseMap", T("T_DustParticle.png"));
            dust.SetColor("_BaseColor", new Color(0.9f, 0.82f, 0.7f, 0.6f));
            EditorUtility.SetDirty(dust);
            ctx.M["M_Dust"] = dust;
        }

        // ------------------------------------------------------------------ Meshes
        static Mesh SaveMesh(Ctx ctx, Mesh mesh, string name, bool exportObj = false)
        {
            var path = Models + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                EditorUtility.CopySerialized(mesh, existing);
                existing.name = name;
                EditorUtility.SetDirty(existing);
                mesh = existing;
            }
            else
            {
                mesh.name = name;
                AssetDatabase.CreateAsset(mesh, path);
            }
            if (exportObj) ObjExporter.Export(mesh, Models + "/OBJ/" + name + ".obj");
            ctx.Mesh[name] = mesh;
            return mesh;
        }

        static void CreateMeshes(Ctx ctx)
        {
            SaveMesh(ctx, ProceduralMeshes.JengaBlock(), "SJ_JengaBlock", true);
            SaveMesh(ctx, ProceduralMeshes.TableTop(), "SJ_TableTop", true);
            SaveMesh(ctx, ProceduralMeshes.TableLeg(), "SJ_TableLeg", true);
            SaveMesh(ctx, ProceduralMeshes.Apron(12.2f), "SJ_ApronLong");
            SaveMesh(ctx, ProceduralMeshes.Apron(7.8f), "SJ_ApronShort");
            SaveMesh(ctx, ProceduralMeshes.LampShade(), "SJ_LampShade", true);
            SaveMesh(ctx, ProceduralMeshes.UVSphere(0.28f, 16, 24, "SJ_Bulb"), "SJ_Bulb");
            SaveMesh(ctx, ProceduralMeshes.Lathe(new[] { new Vector2(0.03f, 0f), new Vector2(0.03f, 10f) }, 8, false, false, "SJ_Cord"), "SJ_Cord");
            SaveMesh(ctx, ProceduralMeshes.Mug(), "SJ_Mug", true);
            SaveMesh(ctx, ProceduralMeshes.Plane(90f, 90f, 1f, 1f, "SJ_Floor"), "SJ_Floor");
            SaveMesh(ctx, ProceduralMeshes.Plane(18f, 12f, 1f, 1f, "SJ_Rug"), "SJ_Rug");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(90f, 34f, 0.6f), 0.05f, false, new Vector2(7f, 7f), "SJ_Wall"), "SJ_Wall");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(90f, 1.4f, 0.9f), 0.12f, false, new Vector2(6f, 6f), "SJ_Baseboard"), "SJ_Baseboard");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(0.9f, 0.03f, 1.3f), 0.01f, false, new Vector2(1f, 1f), "SJ_Card"), "SJ_Card");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(0.42f, 0.42f, 0.42f), 0.08f, false, new Vector2(1f, 1f), "SJ_Dice"), "SJ_Dice");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(0.35f, 2.2f, 1.6f), 0.04f, false, new Vector2(2f, 2f), "SJ_Book"), "SJ_Book");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(5.5f, 0.3f, 1.9f), 0.05f, false, new Vector2(5f, 5f), "SJ_Shelf"), "SJ_Shelf");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(0.3f, 12f, 1.9f), 0.05f, false, new Vector2(5f, 5f), "SJ_ShelfSide"), "SJ_ShelfSide");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(8f, 10f, 0.2f), 0.02f, false, new Vector2(8f, 8f), "SJ_WindowGlass"), "SJ_WindowGlass");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(8.8f, 0.5f, 0.6f), 0.08f, false, new Vector2(4f, 4f), "SJ_WindowBarH"), "SJ_WindowBarH");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(0.5f, 10.8f, 0.6f), 0.08f, false, new Vector2(4f, 4f), "SJ_WindowBarV"), "SJ_WindowBarV");
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ Prefabs
        static void SetProp(SerializedObject so, string name, Action<SerializedProperty> set)
        {
            var p = so.FindProperty(name);
            if (p != null) set(p);
            else Debug.LogWarning($"[SecretJenga] Serialized property '{name}' not found on {so.targetObject.GetType().Name} (package version difference) - left at default.");
        }

        static void CreateBlockPrefab(Ctx ctx)
        {
            var go = new GameObject("SJ_Block");
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = ctx.Mesh["SJ_JengaBlock"];
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { ctx.M["M_BlockWood_0"], ctx.M["M_BlockEndGrain"] };
                mr.shadowCastingMode = ShadowCastingMode.On;
                var col = go.AddComponent<BoxCollider>();
                col.size = JengaDims.BlockSize;
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = JengaDims.BlockMass;
                rb.interpolation = RigidbodyInterpolation.Interpolate;

                go.AddComponent<NetworkObject>();
                var nt = go.AddComponent<NetworkTransform>();
                go.AddComponent<NetworkRigidbody>();
                go.AddComponent<JengaBlock>();

                var so = new SerializedObject(nt);
                SetProp(so, "SyncScaleX", p => p.boolValue = false);
                SetProp(so, "SyncScaleY", p => p.boolValue = false);
                SetProp(so, "SyncScaleZ", p => p.boolValue = false);
                SetProp(so, "Interpolate", p => p.boolValue = true);
                SetProp(so, "UseQuaternionSynchronization", p => p.boolValue = true);
                SetProp(so, "PositionThreshold", p => p.floatValue = 0.001f);
                SetProp(so, "RotAngleThreshold", p => p.floatValue = 0.1f);
                so.ApplyModifiedPropertiesWithoutUndo();

                ctx.BlockPrefab = PrefabUtility.SaveAsPrefabAsset(go, Prefabs + "/SJ_Block.prefab");
            }
            finally { Object.DestroyImmediate(go); }
        }

        static void CreateSessionPrefab(Ctx ctx)
        {
            var go = new GameObject("SJ_Session");
            try
            {
                go.AddComponent<NetworkObject>();
                go.AddComponent<TurnManager>();
                go.AddComponent<JengaTower>();
                go.AddComponent<SecretMissionManager>();
                go.AddComponent<TowerStabilityChecker>();
                go.AddComponent<NetworkGameManager>();
                ctx.SessionPrefab = PrefabUtility.SaveAsPrefabAsset(go, Prefabs + "/SJ_Session.prefab");
            }
            finally { Object.DestroyImmediate(go); }
        }

        static GameObject Part(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Vector3 euler, bool collider = false, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (collider)
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.size = mesh.bounds.size;
                bc.center = mesh.bounds.center;
            }
            go.isStatic = true;
            return go;
        }

        static void CreateEnvironmentPrefab(Ctx ctx)
        {
            var root = new GameObject("SJ_Environment");
            try
            {
                var M = ctx.M;
                var me = ctx.Mesh;
                const float floorY = -11.5f;

                // table (top surface at y = 0)
                var table = new GameObject("Table").transform;
                table.SetParent(root.transform, false);
                Part(table, "TableTop", me["SJ_TableTop"], M["M_TableWalnut"], new Vector3(0, -0.25f, 0), Vector3.zero, true);
                foreach (var (x, z) in new[] { (6.1f, 3.9f), (-6.1f, 3.9f), (6.1f, -3.9f), (-6.1f, -3.9f) })
                    Part(table, "Leg", me["SJ_TableLeg"], M["M_TableLeg"], new Vector3(x, -0.5f, z), Vector3.zero);
                Part(table, "ApronN", me["SJ_ApronLong"], M["M_TableLeg"], new Vector3(0, -0.95f, 3.95f), Vector3.zero);
                Part(table, "ApronS", me["SJ_ApronLong"], M["M_TableLeg"], new Vector3(0, -0.95f, -3.95f), Vector3.zero);
                Part(table, "ApronE", me["SJ_ApronShort"], M["M_TableLeg"], new Vector3(6.15f, -0.95f, 0), new Vector3(0, 90, 0));
                Part(table, "ApronW", me["SJ_ApronShort"], M["M_TableLeg"], new Vector3(-6.15f, -0.95f, 0), new Vector3(0, 90, 0));

                // table props
                var props = new GameObject("Props").transform;
                props.SetParent(root.transform, false);
                Part(props, "Mug", me["SJ_Mug"], M["M_Ceramic"], new Vector3(4.6f, 0f, 2.4f), Vector3.zero, true);
                for (int i = 0; i < 6; i++)
                    Part(props, "Card" + i, me["SJ_Card"], M["M_CardBack"], new Vector3(-4.7f + i * 0.02f, 0.015f + i * 0.032f, -2.6f + i * 0.015f), new Vector3(0, 12 + i * 7, 0), i == 0);
                Part(props, "Card_Loose", me["SJ_Card"], M["M_CardBack"], new Vector3(-3.6f, 0.015f, -3.3f), new Vector3(0, -25, 0));
                Part(props, "DiceA", me["SJ_Dice"], M["M_Dice"], new Vector3(-5.2f, 0.21f, 2.7f), new Vector3(0, 20, 0), true);
                Part(props, "DiceB", me["SJ_Dice"], M["M_Dice"], new Vector3(-4.5f, 0.21f, 3.1f), new Vector3(0, -35, 0), true);

                // room
                var room = new GameObject("Room").transform;
                room.SetParent(root.transform, false);
                var floor = Part(room, "Floor", me["SJ_Floor"], M["M_FloorOak"], new Vector3(0, floorY, 0), Vector3.zero);
                var fc = floor.AddComponent<BoxCollider>();
                fc.size = new Vector3(90, 0.2f, 90);
                fc.center = new Vector3(0, -0.1f, 0);
                Part(room, "Rug", me["SJ_Rug"], M["M_Rug"], new Vector3(0, floorY + 0.02f, 0), Vector3.zero, false, false);
                Part(room, "Ceiling", me["SJ_Floor"], M["M_Ceiling"], new Vector3(0, floorY + 32f, 0), new Vector3(180, 0, 0), false, false);
                Part(room, "WallN", me["SJ_Wall"], M["M_Wallpaper"], new Vector3(0, floorY + 16f, 30f), Vector3.zero);
                Part(room, "WallS", me["SJ_Wall"], M["M_Wallpaper"], new Vector3(0, floorY + 16f, -30f), Vector3.zero);
                Part(room, "WallE", me["SJ_Wall"], M["M_Wallpaper"], new Vector3(30f, floorY + 16f, 0), new Vector3(0, 90, 0));
                Part(room, "WallW", me["SJ_Wall"], M["M_Wallpaper"], new Vector3(-30f, floorY + 16f, 0), new Vector3(0, 90, 0));
                Part(room, "BaseN", me["SJ_Baseboard"], M["M_WallTrim"], new Vector3(0, floorY + 0.7f, 29.6f), Vector3.zero);
                Part(room, "BaseS", me["SJ_Baseboard"], M["M_WallTrim"], new Vector3(0, floorY + 0.7f, -29.6f), Vector3.zero);
                Part(room, "BaseE", me["SJ_Baseboard"], M["M_WallTrim"], new Vector3(29.6f, floorY + 0.7f, 0), new Vector3(0, 90, 0));
                Part(room, "BaseW", me["SJ_Baseboard"], M["M_WallTrim"], new Vector3(-29.6f, floorY + 0.7f, 0), new Vector3(0, 90, 0));

                // window on the north wall
                var win = new GameObject("Window").transform;
                win.SetParent(room, false);
                win.localPosition = new Vector3(-8f, floorY + 17f, 29.6f);
                Part(win, "Glass", me["SJ_WindowGlass"], M["M_Window"], Vector3.zero, Vector3.zero, false, false);
                Part(win, "Top", me["SJ_WindowBarH"], M["M_WallTrim"], new Vector3(0, 5.15f, -0.2f), Vector3.zero);
                Part(win, "Bottom", me["SJ_WindowBarH"], M["M_WallTrim"], new Vector3(0, -5.15f, -0.2f), Vector3.zero);
                Part(win, "MidH", me["SJ_WindowBarH"], M["M_WallTrim"], new Vector3(0, 0, -0.2f), Vector3.zero);
                Part(win, "Left", me["SJ_WindowBarV"], M["M_WallTrim"], new Vector3(-4.15f, 0, -0.2f), Vector3.zero);
                Part(win, "Right", me["SJ_WindowBarV"], M["M_WallTrim"], new Vector3(4.15f, 0, -0.2f), Vector3.zero);
                Part(win, "MidV", me["SJ_WindowBarV"], M["M_WallTrim"], new Vector3(0, 0, -0.2f), Vector3.zero);

                // bookshelf on the east wall
                var shelf = new GameObject("Bookshelf").transform;
                shelf.SetParent(room, false);
                shelf.localPosition = new Vector3(28.6f, floorY, 6f);
                shelf.localEulerAngles = new Vector3(0, -90, 0);
                Part(shelf, "SideL", me["SJ_ShelfSide"], M["M_TableLeg"], new Vector3(-2.75f, 6f, 0), Vector3.zero);
                Part(shelf, "SideR", me["SJ_ShelfSide"], M["M_TableLeg"], new Vector3(2.75f, 6f, 0), Vector3.zero);
                var bookMats = new[] { M["M_BookA"], M["M_BookB"], M["M_BookC"], M["M_BookD"] };
                var rnd = new System.Random(5);
                for (int s = 0; s < 5; s++)
                {
                    float y = 0.15f + s * 2.9f;
                    Part(shelf, "Shelf" + s, me["SJ_Shelf"], M["M_TableLeg"], new Vector3(0, y, 0), Vector3.zero);
                    if (s == 4) break;
                    float x = -2.4f;
                    while (x < 2.2f)
                    {
                        float tilt = rnd.NextDouble() < 0.1 ? 12f : 0f;
                        Part(shelf, "Book", me["SJ_Book"], bookMats[rnd.Next(bookMats.Length)], new Vector3(x + 0.18f, y + 0.15f + 1.1f, 0.1f), new Vector3(0, 0, tilt));
                        x += 0.38f + (float)rnd.NextDouble() * 0.06f;
                    }
                }

                // pendant lamp above the tower
                var lamp = new GameObject("PendantLamp").transform;
                lamp.SetParent(root.transform, false);
                lamp.localPosition = new Vector3(0, 9.5f, 0);
                Part(lamp, "Cord", me["SJ_Cord"], M["M_Brass"], Vector3.zero, Vector3.zero, false, false);
                Part(lamp, "Shade", me["SJ_LampShade"], M["M_LampShade"], Vector3.zero, Vector3.zero, false, false);
                Part(lamp, "Bulb", me["SJ_Bulb"], M["M_Bulb"], new Vector3(0, -0.45f, 0), Vector3.zero, false, false);

                ctx.EnvPrefab = PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/SJ_Environment.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void CreatePrefabList(Ctx ctx)
        {
            var list = LoadOrCreate(Prefabs + "/SJ_NetworkPrefabs.asset", ScriptableObject.CreateInstance<NetworkPrefabsList>);
            foreach (var p in new[] { ctx.BlockPrefab, ctx.SessionPrefab })
                if (!list.Contains(p)) list.Add(new NetworkPrefab { Prefab = p });
            EditorUtility.SetDirty(list);
            ctx.PrefabList = list;
        }

        // ------------------------------------------------------------------ Registry
        static Sprite S(string file) => AssetDatabase.LoadAssetAtPath<Sprite>(UITex + "/" + file + ".png");
        static AudioClip AC(string file) => AssetDatabase.LoadAssetAtPath<AudioClip>(Audio + "/" + file + ".wav");

        static void CreateRegistry(Ctx ctx)
        {
            var r = LoadOrCreate(Res + "/SecretJengaAssets.asset", ScriptableObject.CreateInstance<SecretJengaAssets>);
            r.blockPrefab = ctx.BlockPrefab;
            r.sessionPrefab = ctx.SessionPrefab;
            r.blockMesh = ctx.Mesh["SJ_JengaBlock"];
            r.woodMaterials = new[] { ctx.M["M_BlockWood_0"], ctx.M["M_BlockWood_1"], ctx.M["M_BlockWood_2"] };
            r.endGrainMaterial = ctx.M["M_BlockEndGrain"];
            r.ghostMaterial = ctx.M["M_Ghost"];
            r.cursorMaterial = ctx.M["M_Cursor"];
            r.dustMaterial = ctx.M["M_Dust"];

            r.panel = S("UI_Panel"); r.button = S("UI_Button"); r.rounded = S("UI_Rounded"); r.pill = S("UI_Pill");
            r.circle = S("UI_Circle"); r.glow = S("UI_Glow"); r.card = S("UI_Card"); r.vignette = S("UI_Vignette");
            r.missionIcons = new[]
            {
                S("Icon_Star"), S("Icon_Architect"), S("Icon_Speed"), S("Icon_ColorHunter"), S("Icon_Guardian"),
                S("Icon_Saboteur"), S("Icon_Master"), S("Icon_RiskTaker")
            };
            r.iconStar = S("Icon_Star"); r.iconCrown = S("Icon_Crown"); r.iconClock = S("Icon_Clock"); r.iconCheck = S("Icon_Check");
            r.iconCross = S("Icon_Cross"); r.iconCopy = S("Icon_Copy"); r.iconGear = S("Icon_Gear"); r.iconRefresh = S("Icon_Refresh");
            r.iconPerson = S("Icon_Person"); r.iconSecret = S("Icon_Secret");

            r.blockHits = new[] { AC("SFX_BlockHit_0"), AC("SFX_BlockHit_1"), AC("SFX_BlockHit_2") };
            r.blockPlace = AC("SFX_BlockPlace"); r.blockSlideLoop = AC("SFX_BlockSlide_Loop"); r.blockGrab = AC("SFX_BlockGrab");
            r.towerCollapse = AC("SFX_TowerCollapse"); r.uiHover = AC("UI_Hover"); r.uiClick = AC("UI_Click");
            r.missionComplete = AC("SFX_MissionComplete"); r.turnStart = AC("SFX_TurnStart"); r.timerTick = AC("SFX_TimerTick");
            r.win = AC("SFX_Win"); r.lose = AC("SFX_Lose"); r.countdown = AC("SFX_Countdown"); r.error = AC("SFX_Error");
            r.music = AC("MUS_Ambient_Loop");
            EditorUtility.SetDirty(r);
            ctx.Registry = r;

            int missing = 0;
            foreach (var s in r.missionIcons) if (s == null) missing++;
            if (r.panel == null) missing++;
            if (r.music == null) missing++;
            if (missing > 0) Debug.LogWarning($"[SecretJenga] {missing} UI/audio assets were not found. The game uses fallbacks; regenerate textures/audio with the scripts in Assets/SecretJenga/Tools~.");
        }

        // ------------------------------------------------------------------ Post processing
        static void CreateVolumeProfile(Ctx ctx)
        {
            var path = Scenes + "/SJ_PostProcess.asset";
            var old = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (old != null) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.7f);
            bloom.threshold.Override(1.0f);
            bloom.scatter.Override(0.65f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.3f);
            vig.smoothness.Override(0.45f);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.35f);
            ca.contrast.Override(12f);
            ca.saturation.Override(8f);
            ca.colorFilter.Override(new Color(1f, 0.96f, 0.9f));

            foreach (var c in profile.components)
            {
                c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(c, profile);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            ctx.Volume = profile;
        }

        // ------------------------------------------------------------------ Scene
        static void CreateScene(Ctx ctx)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // lighting / atmosphere
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.36f, 0.30f, 0.30f);
            RenderSettings.ambientEquatorColor = new Color(0.25f, 0.18f, 0.13f);
            RenderSettings.ambientGroundColor = new Color(0.10f, 0.07f, 0.05f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.012f;
            RenderSettings.fogColor = new Color(0.10f, 0.07f, 0.05f);
            RenderSettings.skybox = null;
            var lighting = new LightingSettings { name = "SJ_Lighting", bakedGI = false, realtimeGI = false };
            var lightingPath = Scenes + "/SJ_Lighting.lighting";
            if (AssetDatabase.LoadAssetAtPath<LightingSettings>(lightingPath) != null) AssetDatabase.DeleteAsset(lightingPath);
            AssetDatabase.CreateAsset(lighting, lightingPath);
            Lightmapping.lightingSettings = lighting;

            // camera
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 250f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.05f, 0.04f);
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<CameraController>();
            camGo.transform.position = new Vector3(-6, 5, -8);
            camGo.transform.LookAt(new Vector3(0, 1.5f, 0));
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;

            // lights
            var spot = new GameObject("Lamp Spot Light").AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.transform.position = new Vector3(0, 9.0f, 0);
            spot.transform.rotation = Quaternion.Euler(90, 0, 0);
            spot.color = new Color(1f, 0.84f, 0.64f);
            spot.intensity = 90f;
            spot.range = 40f;
            spot.spotAngle = 95f;
            spot.innerSpotAngle = 40f;
            spot.shadows = LightShadows.Soft;
            spot.shadowStrength = 0.9f;
            spot.shadowBias = 0.02f;
            spot.shadowNormalBias = 0.3f;

            var glow = new GameObject("Bulb Glow").AddComponent<Light>();
            glow.type = LightType.Point;
            glow.transform.position = new Vector3(0, 9.3f, 0);
            glow.color = new Color(1f, 0.78f, 0.5f);
            glow.intensity = 4f;
            glow.range = 4f;
            glow.shadows = LightShadows.None;

            var moon = new GameObject("Window Light").AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.transform.rotation = Quaternion.Euler(35, 160, 0);
            moon.color = new Color(0.55f, 0.65f, 0.95f);
            moon.intensity = 0.35f;
            moon.shadows = LightShadows.Soft;
            moon.shadowStrength = 0.5f;

            var fill = new GameObject("Warm Fill").AddComponent<Light>();
            fill.type = LightType.Point;
            fill.transform.position = new Vector3(-9, 6, -10);
            fill.color = new Color(1f, 0.7f, 0.45f);
            fill.intensity = 60f;
            fill.range = 35f;
            fill.shadows = LightShadows.None;

            // environment
            var env = (GameObject)PrefabUtility.InstantiatePrefab(ctx.EnvPrefab, scene);
            env.name = "Environment";

            // post processing
            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = ctx.Volume;

            // networking
            var nmGo = new GameObject("NetworkManager");
            var utp = nmGo.AddComponent<UnityTransport>();
            var nm = nmGo.AddComponent<NetworkManager>();
            if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = utp;
            nm.NetworkConfig.ConnectionApproval = true;
            nm.NetworkConfig.EnableSceneManagement = false;
            nm.NetworkConfig.TickRate = 30;
            nm.NetworkConfig.PlayerPrefab = null;
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(ctx.PrefabList);
            EditorUtility.SetDirty(nm);

            // game systems
            var sys = new GameObject("SecretJenga Systems");
            var boot = sys.AddComponent<GameBootstrap>();
            boot.assets = ctx.Registry;
            sys.AddComponent<MultiplayerServiceManager>();
            sys.AddComponent<RelayConnectionManager>();
            sys.AddComponent<LobbyManager>();
            sys.AddComponent<NetworkSessionManager>();
            sys.AddComponent<AudioManager>();
            sys.AddComponent<EffectsManager>();
            var bic = sys.AddComponent<BlockInteractionController>();
            bic.cam = cam;
            sys.AddComponent<UIManager>();

            var showcase = new GameObject("Menu Showcase Tower");
            showcase.AddComponent<MenuShowcase>();

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();

            EditorSceneManager.SaveScene(scene, ScenePath);

            var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != ScenePath) list.Add(new EditorBuildSettingsScene(s.path, false));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ------------------------------------------------------------------ Player settings
        static void ConfigureProjectSettings()
        {
            PlayerSettings.productName = "Secret Jenga";
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.forceSingleInstance = false;
        }

        [MenuItem("Tools/Secret Jenga/Open Game Scene", priority = 1)]
        public static void OpenScene()
        {
            if (!File.Exists(ScenePath)) { EditorUtility.DisplayDialog("Secret Jenga", "Scene not generated yet. Run Tools > Secret Jenga > Generate Project.", "OK"); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
