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
    /// Tools > The Last Block > Generate Project
    /// Creates (or refreshes - it is safe to run again) every generated asset of THE LAST BLOCK:
    /// texture/audio import settings, URP materials, procedural meshes, network prefabs, the NGO prefab list,
    /// task/card definition assets (existing ones are kept so designer edits survive), the asset registry,
    /// the post-processing profile, the fully wired underground-room scene and the player/build settings.
    /// Obsolete assets from the old "Secret Jenga" living-room version are removed.
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
        const string Data = Root + "/Data";
        const string TaskDir = Data + "/Tasks";
        const string CardDir = Data + "/Cards";
        public const string ScenePath = Scenes + "/TheLastBlock.unity";
        const string OldScenePath = Scenes + "/SecretJenga.unity";

        class Ctx
        {
            public readonly Dictionary<string, Material> M = new Dictionary<string, Material>();
            public readonly Dictionary<string, Mesh> Mesh = new Dictionary<string, Mesh>();
            public GameObject BlockPrefab, SessionPrefab, EnvPrefab;
            public SecretJengaAssets Registry;
            public NetworkPrefabsList PrefabList;
            public VolumeProfile Volume;
            public TaskDefinition[] Tasks;
            public CardDefinition[] Cards;
        }

        [MenuItem("Tools/The Last Block/Generate Project", priority = 0)]
        public static void Generate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("The Last Block", "Exit Play Mode first.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var ctx = new Ctx();
            try
            {
                Step("Folders", 0.02f); EnsureFolders();
                Step("Removing obsolete assets", 0.05f); RemoveObsolete();
                Step("Texture import settings", 0.08f); ConfigureTextures();
                Step("Audio import settings", 0.14f); ConfigureAudio();
                Step("Materials", 0.22f); CreateMaterials(ctx);
                Step("Meshes", 0.30f); CreateMeshes(ctx);
                Step("Block prefab", 0.38f); CreateBlockPrefab(ctx);
                Step("Session prefab", 0.44f); CreateSessionPrefab(ctx);
                Step("Underground room", 0.52f); CreateEnvironmentPrefab(ctx);
                Step("Network prefab list", 0.60f); CreatePrefabList(ctx);
                Step("Tasks and cards", 0.64f); CreateContent(ctx);
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
                EditorUtility.DisplayDialog("The Last Block", "Generation failed:\n" + e.Message + "\n\nSee the Console for details.", "OK");
                return;
            }
            EditorUtility.ClearProgressBar();
            Debug.Log("[TheLastBlock] Project generated. Open " + ScenePath + " and press Play.");
            EditorUtility.DisplayDialog("The Last Block",
                "Project generated!\n\nScene: " + ScenePath + " (opened and added to Build Settings).\n\n" +
                "Quick test: Play > Host Game > Direct (IP) > Open the Table. A second player joins with Direct (IP) and 127.0.0.1 " +
                "(Multiplayer Play Mode or a second build). Online (Relay) mode needs Unity Cloud linking - see Assets/SecretJenga/README.md.", "OK");
        }

        static void Step(string what, float p) => EditorUtility.DisplayProgressBar("The Last Block", what + "...", p);

        // ------------------------------------------------------------------ Folders
        static void EnsureFolders()
        {
            foreach (var f in new[] { Root, Root + "/Scripts", Root + "/Editor", Prefabs, Mats, Models, Scenes, Tex, UITex, Audio, Res, Data, TaskDir, CardDir })
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

        /// <summary>Living-room assets of the previous version that the dark room no longer uses.</summary>
        static void RemoveObsolete()
        {
            var obsolete = new List<string>
            {
                OldScenePath, Prefabs + "/SJ_Environment.prefab",
                Models + "/OBJ",
                Tex + "/T_Rug_Albedo.png", Tex + "/T_Wallpaper_Albedo.png", Tex + "/T_FloorOak_Albedo.png", Tex + "/T_FloorOak_Normal.png",
            };
            foreach (var m in new[] { "M_BookA", "M_BookB", "M_BookC", "M_BookD", "M_Brass", "M_CardBack", "M_Ceramic", "M_Dice", "M_FloorOak", "M_LampShade", "M_Rug", "M_TableLeg", "M_TableWalnut", "M_Wallpaper", "M_WallTrim", "M_Window", "M_Ceiling" })
                obsolete.Add(Mats + "/" + m + ".mat");
            foreach (var m in new[] { "SJ_ApronLong", "SJ_ApronShort", "SJ_Baseboard", "SJ_Book", "SJ_Bulb", "SJ_Card", "SJ_Cord", "SJ_Dice", "SJ_Floor", "SJ_LampShade", "SJ_Mug", "SJ_Rug", "SJ_Shelf", "SJ_ShelfSide", "SJ_TableLeg", "SJ_TableTop", "SJ_Wall", "SJ_WindowBarH", "SJ_WindowBarV", "SJ_WindowGlass" })
                obsolete.Add(Models + "/" + m + ".asset");
            foreach (var i in new[] { "Icon_Architect", "Icon_ColorHunter", "Icon_Guardian", "Icon_Master", "Icon_RiskTaker", "Icon_Saboteur", "Icon_Speed" })
                obsolete.Add(UITex + "/" + i + ".png");
            int removed = 0;
            foreach (var p in obsolete)
            {
                if (AssetDatabase.LoadMainAssetAtPath(p) == null && !AssetDatabase.IsValidFolder(p)) continue;
                if (AssetDatabase.DeleteAsset(p)) removed++;
            }
            if (removed > 0) Debug.Log($"[TheLastBlock] Removed {removed} obsolete asset(s) from the old living-room version.");
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
                case "UI_Paper": return new Vector4(56, 56, 56, 56);
                case "UI_PaperSlip": return new Vector4(40, 34, 40, 34);
                case "UI_Stamp": return new Vector4(30, 30, 30, 30);
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

        static Material LitMat(Ctx ctx, string name, Color color, float smooth, Texture2D albedo = null, Texture2D normal = null, Vector2? tiling = null, float metallic = 0f, Color? emission = null, bool doubleSided = false, float bump = 0.7f)
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
                m.SetFloat("_BumpScale", bump);
                m.EnableKeyword("_NORMALMAP");
            }
            else
            {
                m.SetTexture("_BumpMap", null);
                m.DisableKeyword("_NORMALMAP");
            }
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission ?? Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (m.HasProperty("_Cull")) { m.SetFloat("_Cull", doubleSided ? 0f : 2f); m.doubleSidedGI = doubleSided; }
            EditorUtility.SetDirty(m);
            ctx.M[name] = m;
            return m;
        }

        static void CreateMaterials(Ctx ctx)
        {
            // blocks: aged, slightly dirty beech
            var blockNormal = T("T_BlockWood_Normal.png");
            for (int i = 0; i < 3; i++) LitMat(ctx, "M_BlockWood_" + i, new Color(0.86f, 0.82f, 0.76f), 0.28f, T($"T_BlockWood_{i}_Albedo.png"), blockNormal);
            LitMat(ctx, "M_BlockEndGrain", new Color(0.8f, 0.75f, 0.7f), 0.2f, T("T_BlockEndGrain_Albedo.png"));

            // room
            LitMat(ctx, "M_TableWorn", Color.white, 0.38f, T("T_TableWorn_Albedo.png"), T("T_TableWorn_Normal.png"), null, 0f, null, false, 0.6f);
            LitMat(ctx, "M_TableIron", new Color(0.09f, 0.085f, 0.08f), 0.35f, null, null, null, 0.8f);
            LitMat(ctx, "M_Concrete", Color.white, 0.12f, T("T_Concrete_Albedo.png"), T("T_Concrete_Normal.png"), null, 0f, null, false, 0.9f);
            LitMat(ctx, "M_ConcreteFloor", new Color(0.85f, 0.85f, 0.85f), 0.18f, T("T_ConcreteFloor_Albedo.png"), T("T_ConcreteFloor_Normal.png"), null, 0f, null, false, 0.9f);
            LitMat(ctx, "M_ConcreteCeiling", new Color(0.45f, 0.45f, 0.45f), 0.05f, T("T_Concrete_Albedo.png"), T("T_Concrete_Normal.png"));
            LitMat(ctx, "M_ChairWood", new Color(0.32f, 0.26f, 0.22f), 0.3f, T("T_TableWorn_Albedo.png"), T("T_TableWorn_Normal.png"), new Vector2(0.5f, 0.5f));
            LitMat(ctx, "M_LampEnamel", new Color(0.12f, 0.15f, 0.13f), 0.55f, null, null, null, 0.3f, null, true);
            LitMat(ctx, "M_RustedSteel", new Color(0.16f, 0.12f, 0.1f), 0.3f, null, null, null, 0.7f);
            LitMat(ctx, "M_Bulb", new Color(1f, 0.9f, 0.7f), 0.9f, null, null, null, 0f, new Color(1f, 0.72f, 0.4f) * 8f);
            LitMat(ctx, "M_Door", new Color(0.13f, 0.12f, 0.11f), 0.3f, null, null, null, 0.75f);

            // characters (placeholders)
            LitMat(ctx, "M_Skin", new Color(0.70f, 0.58f, 0.52f), 0.38f);
            LitMat(ctx, "M_Cloth", new Color(0.19f, 0.17f, 0.155f), 0.12f);
            LitMat(ctx, "M_Hood", new Color(0.15f, 0.14f, 0.135f), 0.08f);
            LitMat(ctx, "M_Cursor", Palette.Lamp, 0.6f, null, null, null, 0f, Palette.Lamp * 0.6f);

            // ghost (transparent unlit)
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var ghost = LoadOrCreate(Mats + "/M_Ghost.mat", () => new Material(unlit));
            ghost.shader = unlit;
            RuntimeArt.MakeTransparent(ghost);
            ghost.SetColor("_BaseColor", new Color(0.9f, 0.85f, 0.7f, 0.25f));
            EditorUtility.SetDirty(ghost);
            ctx.M["M_Ghost"] = ghost;

            // dust particles
            var pShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var dust = LoadOrCreate(Mats + "/M_Dust.mat", () => new Material(pShader));
            dust.shader = pShader;
            RuntimeArt.MakeTransparent(dust);
            dust.SetTexture("_BaseMap", T("T_DustParticle.png"));
            dust.SetColor("_BaseColor", new Color(0.6f, 0.58f, 0.55f, 0.5f));
            EditorUtility.SetDirty(dust);
            ctx.M["M_Dust"] = dust;
        }

        // ------------------------------------------------------------------ Meshes
        static Mesh SaveMesh(Ctx ctx, Mesh mesh, string name)
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
            ctx.Mesh[name] = mesh;
            return mesh;
        }

        const float RoomHalf = 36f;
        const float CeilingY = 34f;

        static void CreateMeshes(Ctx ctx)
        {
            float wallH = CeilingY - SeatLayout.FloorY;
            SaveMesh(ctx, ProceduralMeshes.JengaBlock(), "SJ_JengaBlock");
            SaveMesh(ctx, ProceduralMeshes.RoundTableTop(SeatLayout.TableRadius, 0.6f), "TLB_TableTop");
            SaveMesh(ctx, ProceduralMeshes.Cylinder(SeatLayout.TableRadius, 0.6f, 40, "TLB_TableCollider"), "TLB_TableCollider");
            SaveMesh(ctx, ProceduralMeshes.Lathe(new[]
            {
                new Vector2(4.2f, 0f), new Vector2(4.2f, 0.5f), new Vector2(3.0f, 0.9f), new Vector2(1.0f, 1.6f), new Vector2(0.75f, 2.4f),
                new Vector2(0.7f, 12.4f), new Vector2(1.6f, 12.9f), new Vector2(2.6f, 13.4f)
            }, 32, true, true, "TLB_TableBase", 3f), "TLB_TableBase");
            SaveMesh(ctx, ProceduralMeshes.Chair(), "TLB_Chair");
            SaveMesh(ctx, ProceduralMeshes.IndustrialShade(), "TLB_Shade");
            SaveMesh(ctx, ProceduralMeshes.UVSphere(0.36f, 14, 20, "TLB_Bulb"), "TLB_Bulb");
            SaveMesh(ctx, ProceduralMeshes.Cylinder(0.06f, CeilingY - SeatLayout.LampHeight, 8, "TLB_Cord"), "TLB_Cord");
            SaveMesh(ctx, ProceduralMeshes.Plane(RoomHalf * 2f, RoomHalf * 2f, RoomHalf * 2f / 22f, RoomHalf * 2f / 22f, "TLB_Floor"), "TLB_Floor");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(RoomHalf * 2f, wallH, 1f), 0.05f, false, new Vector2(24f, 24f), "TLB_Wall"), "TLB_Wall");
            SaveMesh(ctx, ProceduralMeshes.Pipe(0.7f, RoomHalf * 2f), "TLB_PipeLong");
            SaveMesh(ctx, ProceduralMeshes.Pipe(0.45f, RoomHalf * 2f), "TLB_PipeThin");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(14f, 26f, 0.8f), 0.15f, false, new Vector2(14f, 26f), "TLB_Door"), "TLB_Door");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(17f, 28.5f, 0.6f), 0.1f, false, new Vector2(17f, 28f), "TLB_DoorFrame"), "TLB_DoorFrame");
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ Prefabs
        static void SetProp(SerializedObject so, string name, Action<SerializedProperty> set)
        {
            var p = so.FindProperty(name);
            if (p != null) set(p);
            else Debug.LogWarning($"[TheLastBlock] Serialized property '{name}' not found on {so.targetObject.GetType().Name} (package version difference) - left at default.");
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
                go.AddComponent<SecretTaskManager>();
                go.AddComponent<CardManager>();
                go.AddComponent<TowerStabilityChecker>();
                go.AddComponent<AtmosphereSync>();
                go.AddComponent<HandSync>();
                go.AddComponent<NetworkGameManager>();
                ctx.SessionPrefab = PrefabUtility.SaveAsPrefabAsset(go, Prefabs + "/SJ_Session.prefab");
            }
            finally { Object.DestroyImmediate(go); }
        }

        static GameObject Part(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Vector3 euler, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            go.isStatic = true;
            return go;
        }

        /// <summary>
        /// The isolated underground room: worn round table on an iron pedestal, five chairs, one hanging industrial
        /// lamp, bare concrete, a few pipes and a shut steel door. Deliberately sparse - the tower is the focus.
        /// (The lamp itself is created in the scene so its lights can be wired to the AtmosphereDirector.)
        /// </summary>
        static void CreateEnvironmentPrefab(Ctx ctx)
        {
            var root = new GameObject("TLB_Room");
            try
            {
                var M = ctx.M;
                var me = ctx.Mesh;
                float floorY = SeatLayout.FloorY;
                float wallMid = (CeilingY + floorY) * 0.5f;

                // table (top surface at y = 0) - the top has a collider so blocks rest and fall on it
                var table = new GameObject("Table").transform;
                table.SetParent(root.transform, false);
                Part(table, "TableTop", me["TLB_TableTop"], M["M_TableWorn"], Vector3.zero, Vector3.zero);
                var tableCol = new GameObject("TableCollider"); // round (static, non-convex) so blocks can fall off the edge
                tableCol.transform.SetParent(table, false);
                tableCol.transform.localPosition = new Vector3(0f, -0.3f, 0f); // 0.6 thick cylinder: top at y = 0
                tableCol.AddComponent<MeshCollider>().sharedMesh = me["TLB_TableCollider"];
                tableCol.isStatic = true;
                Part(table, "Pedestal", me["TLB_TableBase"], M["M_TableIron"], new Vector3(0f, floorY, 0f), Vector3.zero);

                // chairs
                var chairs = new GameObject("Chairs").transform;
                chairs.SetParent(root.transform, false);
                var rnd = new System.Random(7);
                for (int s = 0; s < SeatLayout.SeatCount; s++)
                {
                    var c = Part(chairs, "Chair_" + (s + 1), me["TLB_Chair"], M["M_ChairWood"], SeatLayout.ChairPosition(s), Vector3.zero);
                    c.transform.rotation = SeatLayout.Facing(s) * Quaternion.Euler(0f, (float)(rnd.NextDouble() - 0.5) * 6f, 0f);
                }

                // room shell
                var room = new GameObject("Room").transform;
                room.SetParent(root.transform, false);
                var floor = Part(room, "Floor", me["TLB_Floor"], M["M_ConcreteFloor"], new Vector3(0, floorY, 0), Vector3.zero);
                var fc = floor.AddComponent<BoxCollider>();
                fc.size = new Vector3(RoomHalf * 2f, 0.2f, RoomHalf * 2f);
                fc.center = new Vector3(0, -0.1f, 0);
                Part(room, "Ceiling", me["TLB_Floor"], M["M_ConcreteCeiling"], new Vector3(0, CeilingY, 0), new Vector3(180, 0, 0), false);
                Part(room, "WallN", me["TLB_Wall"], M["M_Concrete"], new Vector3(0, wallMid, RoomHalf), Vector3.zero);
                Part(room, "WallS", me["TLB_Wall"], M["M_Concrete"], new Vector3(0, wallMid, -RoomHalf), Vector3.zero);
                Part(room, "WallE", me["TLB_Wall"], M["M_Concrete"], new Vector3(RoomHalf, wallMid, 0), new Vector3(0, 90, 0));
                Part(room, "WallW", me["TLB_Wall"], M["M_Concrete"], new Vector3(-RoomHalf, wallMid, 0), new Vector3(0, 90, 0));

                // a few pipes and a shut steel door - just enough to say "basement"
                var details = new GameObject("Details").transform;
                details.SetParent(room, false);
                Part(details, "PipeCeilingA", me["TLB_PipeLong"], M["M_RustedSteel"], new Vector3(0, CeilingY - 2.2f, 14f), Vector3.zero);
                Part(details, "PipeCeilingB", me["TLB_PipeThin"], M["M_RustedSteel"], new Vector3(0, CeilingY - 3.6f, 16f), Vector3.zero);
                Part(details, "PipeWallE", me["TLB_PipeThin"], M["M_RustedSteel"], new Vector3(RoomHalf - 1.4f, floorY + 30f, 0f), new Vector3(0, 90, 0));
                Part(details, "PipeWallE2", me["TLB_PipeThin"], M["M_RustedSteel"], new Vector3(RoomHalf - 1.4f, floorY + 27.8f, 0f), new Vector3(0, 90, 0));
                Part(details, "DoorFrame", me["TLB_DoorFrame"], M["M_RustedSteel"], new Vector3(-12f, floorY + 14.25f, RoomHalf - 0.6f), Vector3.zero);
                Part(details, "Door", me["TLB_Door"], M["M_Door"], new Vector3(-12f, floorY + 13f, RoomHalf - 1.0f), Vector3.zero);

                ctx.EnvPrefab = PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/TLB_Room.prefab");
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

        // ------------------------------------------------------------------ Tasks + cards (data driven)
        /// <summary>
        /// Creates the default task/card assets once (existing assets are never overwritten, so tuning done in the
        /// Inspector survives regeneration). Every TaskDefinition / CardDefinition in the Data folders is registered,
        /// sorted by file name - the order must be the same on every build because indices are sent over the network.
        /// </summary>
        static void CreateContent(Ctx ctx)
        {
            int i = 1;
            foreach (var d in TaskDefinition.CreateDefaults())
            {
                var path = $"{TaskDir}/{i++:00}_{d.title.Replace(" ", "")}.asset";
                if (AssetDatabase.LoadAssetAtPath<TaskDefinition>(path) == null) AssetDatabase.CreateAsset(d, path);
                else Object.DestroyImmediate(d);
            }
            i = 1;
            foreach (var d in CardDefinition.CreateDefaults())
            {
                var path = $"{CardDir}/{i++:00}_{d.title.Replace(" ", "")}.asset";
                if (AssetDatabase.LoadAssetAtPath<CardDefinition>(path) == null) AssetDatabase.CreateAsset(d, path);
                else Object.DestroyImmediate(d);
            }
            AssetDatabase.SaveAssets();
            ctx.Tasks = LoadAllSorted<TaskDefinition>(TaskDir);
            ctx.Cards = LoadAllSorted<CardDefinition>(CardDir);
        }

        static T[] LoadAllSorted<T>(string folder) where T : Object
        {
            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { folder })) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort(StringComparer.Ordinal);
            var list = new List<T>();
            foreach (var p in paths) { var a = AssetDatabase.LoadAssetAtPath<T>(p); if (a != null) list.Add(a); }
            return list.ToArray();
        }

        // ------------------------------------------------------------------ Registry
        static Sprite S(string file) => AssetDatabase.LoadAssetAtPath<Sprite>(UITex + "/" + file + ".png");
        static AudioClip AC(string file) => AssetDatabase.LoadAssetAtPath<AudioClip>(Audio + "/" + file + ".wav");

        static void CreateRegistry(Ctx ctx)
        {
            var r = LoadOrCreate(Res + "/SecretJengaAssets.asset", ScriptableObject.CreateInstance<SecretJengaAssets>);
            r.blockPrefab = ctx.BlockPrefab;
            r.sessionPrefab = ctx.SessionPrefab;
            r.tasks = ctx.Tasks;
            r.cards = ctx.Cards;
            r.blockMesh = ctx.Mesh["SJ_JengaBlock"];
            r.woodMaterials = new[] { ctx.M["M_BlockWood_0"], ctx.M["M_BlockWood_1"], ctx.M["M_BlockWood_2"] };
            r.endGrainMaterial = ctx.M["M_BlockEndGrain"];
            r.ghostMaterial = ctx.M["M_Ghost"];
            r.cursorMaterial = ctx.M["M_Cursor"];
            r.dustMaterial = ctx.M["M_Dust"];
            r.skinMaterial = ctx.M["M_Skin"];
            r.clothMaterial = ctx.M["M_Cloth"];
            r.hoodMaterial = ctx.M["M_Hood"];

            r.panel = S("UI_Panel"); r.button = S("UI_Button"); r.rounded = S("UI_Rounded"); r.pill = S("UI_Pill");
            r.circle = S("UI_Circle"); r.glow = S("UI_Glow"); r.card = S("UI_Card"); r.vignette = S("UI_Vignette");
            r.iconStar = S("Icon_Star"); r.iconCrown = S("Icon_Crown"); r.iconClock = S("Icon_Clock"); r.iconCheck = S("Icon_Check");
            r.iconCross = S("Icon_Cross"); r.iconCopy = S("Icon_Copy"); r.iconGear = S("Icon_Gear"); r.iconRefresh = S("Icon_Refresh");
            r.iconPerson = S("Icon_Person"); r.iconSecret = S("Icon_Secret");

            // paper, ink and type
            r.paper = S("UI_Paper"); r.paperSlip = S("UI_PaperSlip"); r.cardStock = S("UI_CardStock"); r.tape = S("UI_Tape");
            r.inkStroke = S("UI_InkStroke"); r.tallyStroke = S("UI_TallyStroke"); r.inkCircle = S("UI_InkCircle"); r.inkBlot = S("UI_InkBlot");
            r.stamp = S("UI_Stamp");
            r.iconCardSteady = S("Icon_Card_SteadyGrip"); r.iconCardPressure = S("Icon_Card_Pressure"); r.iconCardSecond = S("Icon_Card_SecondChance");
            r.fontTitle = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/IMFeENsc28P.ttf");
            r.fontBody = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/IMFeENrm28P.ttf");
            r.fontType = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/SpecialElite-Regular.ttf");
            if (r.fontTitle == null || r.fontBody == null || r.fontType == null)
                Debug.LogWarning("[TheLastBlock] UI fonts missing in Assets/SecretJenga/Fonts - the UI falls back to the default font.");

            r.blockHits = new[] { AC("SFX_BlockHit_0"), AC("SFX_BlockHit_1"), AC("SFX_BlockHit_2") };
            r.blockPlace = AC("SFX_BlockPlace"); r.blockSlideLoop = AC("SFX_BlockSlide_Loop"); r.blockGrab = AC("SFX_BlockGrab");
            r.towerCollapse = AC("SFX_TowerCollapse"); r.woodStrainLoop = AC("SFX_WoodStrain_Loop");
            r.music = AC("MUS_Ambient_Loop"); r.lampHumLoop = AC("SFX_LampHum_Loop"); r.flicker = AC("SFX_Flicker");
            r.heartbeatLoop = AC("SFX_Heartbeat_Loop"); r.collapseSting = AC("SFX_CollapseSting");
            r.chairCreaks = new[] { AC("SFX_ChairCreak_0"), AC("SFX_ChairCreak_1"), AC("SFX_ChairCreak_2") };
            r.distantMetal = new[] { AC("SFX_MetalDistant_0"), AC("SFX_MetalDistant_1"), AC("SFX_MetalDistant_2") };
            r.uiHover = AC("UI_Hover"); r.uiClick = AC("UI_Click");
            r.missionComplete = AC("SFX_MissionComplete"); r.turnStart = AC("SFX_TurnStart"); r.timerTick = AC("SFX_TimerTick");
            r.win = AC("SFX_Win"); r.lose = AC("SFX_Lose"); r.countdown = AC("SFX_Countdown"); r.error = AC("SFX_Error");
            r.cardPlayed = AC("SFX_CardPlayed");
            EditorUtility.SetDirty(r);
            ctx.Registry = r;

            int missing = 0;
            if (r.paper == null) missing++;
            if (r.music == null) missing++;
            if (r.lampHumLoop == null || r.collapseSting == null) missing++;
            if (missing > 0) Debug.LogWarning($"[TheLastBlock] {missing} UI/audio asset group(s) were not found. The game uses fallbacks/silence; regenerate them with the scripts in Assets/SecretJenga/Tools~.");
        }

        // ------------------------------------------------------------------ Post processing
        static void CreateVolumeProfile(Ctx ctx)
        {
            var path = Scenes + "/TLB_PostProcess.asset";
            var old = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (old != null) AssetDatabase.DeleteAsset(path);
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(Scenes + "/SJ_PostProcess.asset") != null) AssetDatabase.DeleteAsset(Scenes + "/SJ_PostProcess.asset");
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.55f);
            bloom.threshold.Override(1.1f);
            bloom.scatter.Override(0.6f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.32f);
            vig.smoothness.Override(0.5f);
            vig.color.Override(Color.black);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.65f);
            ca.contrast.Override(18f);
            ca.saturation.Override(-16f);
            ca.colorFilter.Override(new Color(1f, 0.95f, 0.88f));
            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium2);
            grain.intensity.Override(0.28f);
            grain.response.Override(0.75f);
            var chroma = profile.Add<ChromaticAberration>(true);
            chroma.intensity.Override(0.08f);

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

            // lighting / atmosphere: dim but readable - you must see the faces and hands around the table; the far walls fade
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.12f, 0.11f, 0.10f);
            RenderSettings.ambientEquatorColor = new Color(0.085f, 0.075f, 0.068f);
            RenderSettings.ambientGroundColor = new Color(0.03f, 0.027f, 0.024f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.011f;
            RenderSettings.fogColor = new Color(0.03f, 0.027f, 0.024f);
            RenderSettings.skybox = null;
            var lighting = new LightingSettings { name = "TLB_Lighting", bakedGI = false, realtimeGI = false };
            var lightingPath = Scenes + "/TLB_Lighting.lighting";
            if (AssetDatabase.LoadAssetAtPath<LightingSettings>(lightingPath) != null) AssetDatabase.DeleteAsset(lightingPath);
            if (AssetDatabase.LoadAssetAtPath<LightingSettings>(Scenes + "/SJ_Lighting.lighting") != null) AssetDatabase.DeleteAsset(Scenes + "/SJ_Lighting.lighting");
            AssetDatabase.CreateAsset(lighting, lightingPath);
            Lightmapping.lightingSettings = lighting;

            // camera (seated first person; CameraController places it at the local player's chair)
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 58f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 160f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<CameraController>();
            camGo.transform.position = SeatLayout.EyePosition(0);
            camGo.transform.LookAt(new Vector3(0, 2f, 0));
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;

            // room
            var env = (GameObject)PrefabUtility.InstantiatePrefab(ctx.EnvPrefab, scene);
            env.name = "Room";

            // the hanging lamp: rig swings (AtmosphereDirector), lights are children so the light swings with it
            var lampRig = new GameObject("LampRig").transform;
            lampRig.position = new Vector3(0f, SeatLayout.LampHeight, 0f);
            var cordGo = Part(lampRig, "Cord", ctx.Mesh["TLB_Cord"], ctx.M["M_RustedSteel"], new Vector3(0f, (CeilingY - SeatLayout.LampHeight) * 0.5f, 0f), Vector3.zero, false);
            var shade = Part(lampRig, "Shade", ctx.Mesh["TLB_Shade"], ctx.M["M_LampEnamel"], Vector3.zero, Vector3.zero);
            var bulb = Part(lampRig, "Bulb", ctx.Mesh["TLB_Bulb"], ctx.M["M_Bulb"], new Vector3(0f, -0.75f, 0f), Vector3.zero, false);
            cordGo.isStatic = shade.isStatic = bulb.isStatic = false;

            var spot = new GameObject("Lamp Spot Light").AddComponent<Light>();
            spot.transform.SetParent(lampRig, false);
            spot.transform.localPosition = new Vector3(0f, -0.9f, 0f);
            spot.transform.localRotation = Quaternion.Euler(90, 0, 0);
            spot.type = LightType.Spot;
            spot.color = new Color(1f, 0.8f, 0.58f);
            spot.intensity = 150f;
            spot.range = 48f;
            spot.spotAngle = 128f;
            spot.innerSpotAngle = 70f;
            spot.shadows = LightShadows.Soft;
            spot.shadowStrength = 0.95f;
            spot.shadowBias = 0.02f;
            spot.shadowNormalBias = 0.3f;

            var glow = new GameObject("Bulb Glow").AddComponent<Light>();
            glow.transform.SetParent(lampRig, false);
            glow.transform.localPosition = new Vector3(0f, -0.4f, 0f);
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.74f, 0.45f);
            glow.intensity = 42f;
            glow.range = 36f;
            glow.shadows = LightShadows.None;

            var rim = new GameObject("Cold Rim (far door)").AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.transform.rotation = Quaternion.Euler(18, 200, 0);
            rim.color = new Color(0.45f, 0.55f, 0.75f);
            rim.intensity = 0.16f;
            rim.shadows = LightShadows.None;

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
            utp.SetConnectionData("127.0.0.1", GameSettings.DefaultPort, "0.0.0.0");
            EditorUtility.SetDirty(nm);
            EditorUtility.SetDirty(utp);

            // game systems (local, non-networked)
            var sys = new GameObject("TheLastBlock Systems");
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
            sys.AddComponent<PlayerAvatars>();
            var atmos = sys.AddComponent<AtmosphereDirector>();
            atmos.lampLight = spot;
            atmos.bulbGlow = glow;
            atmos.lampRig = lampRig;
            atmos.bulbRenderer = bulb.GetComponent<MeshRenderer>();
            atmos.volume = vol;
            sys.AddComponent<UIManager>();

            var showcase = new GameObject("Menu Showcase Tower");
            showcase.AddComponent<MenuShowcase>();

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();

            EditorSceneManager.SaveScene(scene, ScenePath);

            var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != ScenePath && s.path != OldScenePath) list.Add(new EditorBuildSettingsScene(s.path, false));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ------------------------------------------------------------------ Player settings
        static void ConfigureProjectSettings()
        {
            PlayerSettings.productName = "The Last Block";
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.forceSingleInstance = false;
        }

        [MenuItem("Tools/The Last Block/Open Game Scene", priority = 1)]
        public static void OpenScene()
        {
            if (!File.Exists(ScenePath)) { EditorUtility.DisplayDialog("The Last Block", "Scene not generated yet. Run Tools > The Last Block > Generate Project.", "OK"); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
