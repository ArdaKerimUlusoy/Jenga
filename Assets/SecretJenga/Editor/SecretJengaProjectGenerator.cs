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
using UnityEditor.Animations;
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
    /// the post-processing profile, the fully wired frontier-saloon scene (warm oil-lamp light, bar, piano, barrels,
    /// wanted posters, a wagon-wheel chandelier over the table), the rigged cowboy character and the player/build settings.
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
        const string CharDir = Models + "/Characters";
        public const string CowboyPath = CharDir + "/TLB_Cowboy.fbx";
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
            public GameObject CowboyModel;
            public RuntimeAnimatorController CowboyController;
            public readonly List<Renderer> ExtraBulbs = new List<Renderer>();
            public readonly List<Light> RoomLights = new List<Light>();
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
                Step("Saloon", 0.52f); CreateEnvironmentPrefab(ctx);
                Step("Cowboy character", 0.56f); ImportCharacter(ctx);
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
                "Quick test alone: Play > 'Play alone vs bots' (or Host a table > Begin after seating bots).\n" +
                "Two players: Host a table > Direct (IP); the other joins with Direct (IP) and 127.0.0.1 " +
                "(Multiplayer Play Mode or a second build). Online (Relay) mode needs Unity Cloud linking - see Assets/SecretJenga/README.md.", "OK");
        }

        static void Step(string what, float p) => EditorUtility.DisplayProgressBar("The Last Block", what + "...", p);

        // ------------------------------------------------------------------ Folders
        static void EnsureFolders()
        {
            foreach (var f in new[] { Root, Root + "/Scripts", Root + "/Editor", Prefabs, Mats, Models, CharDir, Scenes, Tex, UITex, Audio, Res, Data, TaskDir, CardDir })
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

            // saloon
            LitMat(ctx, "M_FloorPlanks", new Color(0.92f, 0.86f, 0.8f), 0.32f, T("T_Planks_Albedo.png"), T("T_Planks_Normal.png"), null, 0f, null, false, 0.8f);
            LitMat(ctx, "M_WallBoards", new Color(0.95f, 0.88f, 0.82f), 0.3f, T("T_WallBoards_Albedo.png"), T("T_WallBoards_Normal.png"), null, 0f, null, false, 0.8f);
            LitMat(ctx, "M_SaloonPaper", new Color(0.95f, 0.9f, 0.84f), 0.12f, T("T_SaloonPaper_Albedo.png"));
            LitMat(ctx, "M_CeilingPlanks", new Color(0.55f, 0.46f, 0.4f), 0.15f, T("T_Planks_Albedo.png"), T("T_Planks_Normal.png"));
            LitMat(ctx, "M_DarkWood", new Color(0.3f, 0.18f, 0.11f), 0.45f, T("T_WallBoards_Albedo.png"), T("T_WallBoards_Normal.png"), new Vector2(0.5f, 0.5f));
            LitMat(ctx, "M_BarTop", new Color(0.42f, 0.24f, 0.14f), 0.72f, T("T_Planks_Albedo.png"), null, new Vector2(0.4f, 2f));
            LitMat(ctx, "M_Brass", new Color(0.85f, 0.64f, 0.32f), 0.75f, null, null, null, 1f);
            LitMat(ctx, "M_BlackIron", new Color(0.07f, 0.065f, 0.06f), 0.4f, null, null, null, 0.85f);
            LitMat(ctx, "M_Mirror", new Color(0.22f, 0.2f, 0.18f), 0.96f, null, null, null, 1f);
            LitMat(ctx, "M_BottleGreen", new Color(0.12f, 0.3f, 0.12f), 0.92f, null, null, null, 0.1f, new Color(0.02f, 0.05f, 0.02f));
            LitMat(ctx, "M_BottleAmber", new Color(0.45f, 0.22f, 0.06f), 0.92f, null, null, null, 0.1f, new Color(0.08f, 0.035f, 0.01f));
            LitMat(ctx, "M_BottleClear", new Color(0.7f, 0.66f, 0.55f), 0.95f, null, null, null, 0.1f);
            LitMat(ctx, "M_PianoBlack", new Color(0.05f, 0.035f, 0.03f), 0.78f);
            LitMat(ctx, "M_Ivory", new Color(0.86f, 0.82f, 0.72f), 0.6f);
            LitMat(ctx, "M_Candle", new Color(0.92f, 0.88f, 0.78f), 0.3f);
            LitMat(ctx, "M_Flame", new Color(1f, 0.8f, 0.45f), 0.2f, null, null, null, 0f, new Color(1f, 0.6f, 0.22f) * 6f);
            LitMat(ctx, "M_LanternGlass", new Color(1f, 0.85f, 0.6f), 0.9f, null, null, null, 0f, new Color(1f, 0.62f, 0.3f) * 3.2f);
            LitMat(ctx, "M_WindowDusk", Color.white, 0.6f, T("T_Window_Dusk.png"), null, null, 0f, Color.white * 1.6f);
            var dusk = ctx.M["M_WindowDusk"];
            dusk.SetTexture("_EmissionMap", T("T_Window_Dusk.png"));
            EditorUtility.SetDirty(dusk);
            foreach (var poster in new[] { "T_Poster_Wanted_0", "T_Poster_Wanted_1", "T_Poster_Wanted_2", "T_Poster_Whiskey" })
                LitMat(ctx, "M_" + poster.Substring(2), Color.white, 0.1f, T(poster + ".png"));

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

        const float RoomHalf = 64f;     // the saloon is 6.4 m across
        const float CeilingY = 46f;     // 3 m from floor to ceiling
        const float WainscotH = 22f;    // boards up to 1.1 m, wallpaper above

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
            // saloon
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(RoomHalf * 2f, WainscotH, 1.2f), 0.05f, false, new Vector2(24f, 24f), "TLB_WallLow"), "TLB_WallLow");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(RoomHalf * 2f, wallH - WainscotH, 1f), 0.05f, false, new Vector2(32f, 32f), "TLB_WallHigh"), "TLB_WallHigh");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(RoomHalf * 2f, 1.2f, 2f), 0.2f, false, new Vector2(16f, 16f), "TLB_ChairRail"), "TLB_ChairRail");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(RoomHalf * 2f, 2.6f, 2.6f), 0.25f, false, new Vector2(16f, 16f), "TLB_Beam"), "TLB_Beam");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(72f, 22f, 9f), 0.3f, false, new Vector2(24f, 24f), "TLB_BarCounter"), "TLB_BarCounter");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(75f, 1.3f, 12f), 0.4f, false, new Vector2(20f, 20f), "TLB_BarTop"), "TLB_BarTop");
            SaveMesh(ctx, ProceduralMeshes.Pipe(0.45f, 70f), "TLB_FootRail");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(62f, 0.9f, 4f), 0.12f, false, new Vector2(16f, 16f), "TLB_Shelf"), "TLB_Shelf");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(66f, 30f, 1.4f), 0.2f, false, new Vector2(24f, 24f), "TLB_BackBar"), "TLB_BackBar");
            SaveMesh(ctx, SaloonMeshes.Quad(46f, 12f, "TLB_MirrorQuad"), "TLB_MirrorQuad");
            SaveMesh(ctx, SaloonMeshes.Barrel(), "TLB_Barrel");
            SaveMesh(ctx, SaloonMeshes.BarrelHoops(), "TLB_BarrelHoops");
            SaveMesh(ctx, SaloonMeshes.Bottle(1f, "TLB_Bottle"), "TLB_Bottle");
            SaveMesh(ctx, SaloonMeshes.Bottle(0.8f, "TLB_BottleSmall"), "TLB_BottleSmall");
            SaveMesh(ctx, SaloonMeshes.ShotGlass(), "TLB_ShotGlass");
            SaveMesh(ctx, SaloonMeshes.WagonWheel(6.2f), "TLB_WagonWheel");
            SaveMesh(ctx, SaloonMeshes.Candles(6.2f, 8), "TLB_Candles");
            SaveMesh(ctx, SaloonMeshes.Flames(6.2f, 8), "TLB_Flames");
            SaveMesh(ctx, ProceduralMeshes.Cylinder(0.12f, CeilingY - SeatLayout.LampHeight, 8, "TLB_Chain"), "TLB_Chain");
            SaveMesh(ctx, SaloonMeshes.LanternFrame(), "TLB_LanternFrame");
            SaveMesh(ctx, SaloonMeshes.LanternGlass(), "TLB_LanternGlass");
            SaveMesh(ctx, SaloonMeshes.BatwingDoor(7.5f, 15f), "TLB_Batwing");
            SaveMesh(ctx, SaloonMeshes.WindowFrame(15f, 21f), "TLB_WindowFrame");
            SaveMesh(ctx, SaloonMeshes.Quad(15f, 21f, "TLB_WindowPane"), "TLB_WindowPane");
            SaveMesh(ctx, SaloonMeshes.Quad(18f, 34f, "TLB_DoorOutside"), "TLB_DoorOutside");
            SaveMesh(ctx, SaloonMeshes.PianoBody(), "TLB_Piano");
            SaveMesh(ctx, SaloonMeshes.PianoKeys(), "TLB_PianoKeys");
            SaveMesh(ctx, SaloonMeshes.SideTable(6f, 15f), "TLB_SideTable");
            SaveMesh(ctx, SaloonMeshes.Quad(7.2f, 10.1f, "TLB_Poster"), "TLB_Poster");
            SaveMesh(ctx, SaloonMeshes.Quad(36f, 13.5f, "TLB_Sign"), "TLB_Sign");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(1.4f, 35f, 1.8f), 0.15f, false, new Vector2(8f, 8f), "TLB_DoorPost"), "TLB_DoorPost");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(20.6f, 1.6f, 2f), 0.15f, false, new Vector2(8f, 8f), "TLB_DoorHeader"), "TLB_DoorHeader");
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
        /// The old frontier saloon: plank floor, board-and-wallpaper walls, timber beams, a long bar with a mirror and
        /// bottles, a whiskey sign, wanted posters, an upright piano, barrels, two side tables, dusk windows and
        /// swinging batwing doors. The players' worn round table with five chairs stands in the middle.
        /// (Lights - chandelier and wall lanterns - are created in the scene so they can be wired to the AtmosphereDirector.)
        /// </summary>
        static void CreateEnvironmentPrefab(Ctx ctx)
        {
            var root = new GameObject("TLB_Room");
            try
            {
                var M = ctx.M;
                var me = ctx.Mesh;
                float F = SeatLayout.FloorY;
                float wallH = CeilingY - F;
                var rnd = new System.Random(1881);
                float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

                // table (top surface at y = 0) - the top has a collider so blocks rest and fall on it
                var table = new GameObject("Table").transform;
                table.SetParent(root.transform, false);
                Part(table, "TableTop", me["TLB_TableTop"], M["M_TableWorn"], Vector3.zero, Vector3.zero);
                var tableCol = new GameObject("TableCollider"); // round (static, non-convex) so blocks can fall off the edge
                tableCol.transform.SetParent(table, false);
                tableCol.transform.localPosition = new Vector3(0f, -0.3f, 0f); // 0.6 thick cylinder: top at y = 0
                tableCol.AddComponent<MeshCollider>().sharedMesh = me["TLB_TableCollider"];
                tableCol.isStatic = true;
                Part(table, "Pedestal", me["TLB_TableBase"], M["M_TableIron"], new Vector3(0f, F, 0f), Vector3.zero);

                // chairs
                var chairs = new GameObject("Chairs").transform;
                chairs.SetParent(root.transform, false);
                for (int s = 0; s < SeatLayout.SeatCount; s++)
                {
                    var c = Part(chairs, "Chair_" + (s + 1), me["TLB_Chair"], M["M_ChairWood"], SeatLayout.ChairPosition(s), Vector3.zero);
                    c.transform.rotation = SeatLayout.Facing(s) * Quaternion.Euler(0f, R(-3f, 3f), 0f);
                }

                // ---------------- room shell
                var room = new GameObject("Room").transform;
                room.SetParent(root.transform, false);
                var floor = Part(room, "Floor", me["TLB_Floor"], M["M_FloorPlanks"], new Vector3(0, F, 0), Vector3.zero);
                var fc = floor.AddComponent<BoxCollider>();
                fc.size = new Vector3(RoomHalf * 2f, 0.2f, RoomHalf * 2f);
                fc.center = new Vector3(0, -0.1f, 0);
                Part(room, "Ceiling", me["TLB_Floor"], M["M_CeilingPlanks"], new Vector3(0, CeilingY, 0), new Vector3(180, 0, 0), false);
                foreach (var z in new[] { -34f, 0f, 34f })
                    Part(room, "Beam", me["TLB_Beam"], M["M_DarkWood"], new Vector3(0, CeilingY - 1.3f, z), Vector3.zero, false);

                float lowY = F + WainscotH * 0.5f, highY = F + WainscotH + (wallH - WainscotH) * 0.5f;
                void Wall(string n, Vector3 at, float yRot, Vector3 inward)
                {
                    Part(room, n + "_Boards", me["TLB_WallLow"], M["M_WallBoards"], new Vector3(at.x, lowY, at.z), new Vector3(0, yRot, 0), false);
                    Part(room, n + "_Paper", me["TLB_WallHigh"], M["M_SaloonPaper"], new Vector3(at.x, highY, at.z), new Vector3(0, yRot, 0), false);
                    Part(room, n + "_Rail", me["TLB_ChairRail"], M["M_DarkWood"], new Vector3(at.x, F + WainscotH, at.z) + inward * 0.9f, new Vector3(0, yRot, 0), false);
                }
                Wall("WallN", new Vector3(0, 0, RoomHalf), 0f, Vector3.back);
                Wall("WallS", new Vector3(0, 0, -RoomHalf), 0f, Vector3.forward);
                Wall("WallE", new Vector3(RoomHalf, 0, 0), 90f, Vector3.left);
                Wall("WallW", new Vector3(-RoomHalf, 0, 0), 90f, Vector3.right);

                var props = new GameObject("Saloon").transform;
                props.SetParent(root.transform, false);

                // ---------------- the bar (north wall)
                float barX = -8f, barZ = RoomHalf - 16f;
                Part(props, "BarCounter", me["TLB_BarCounter"], M["M_DarkWood"], new Vector3(barX, F + 11f, barZ), Vector3.zero);
                Part(props, "BarTop", me["TLB_BarTop"], M["M_BarTop"], new Vector3(barX, F + 22.65f, barZ - 0.5f), Vector3.zero);
                Part(props, "FootRail", me["TLB_FootRail"], M["M_Brass"], new Vector3(barX, F + 2.6f, barZ - 5.8f), Vector3.zero);
                Part(props, "BackBar", me["TLB_BackBar"], M["M_DarkWood"], new Vector3(barX, F + 30f, RoomHalf - 1.4f), Vector3.zero);
                Part(props, "Mirror", me["TLB_MirrorQuad"], M["M_Mirror"], new Vector3(barX, F + 38f, RoomHalf - 2.15f), Vector3.zero, false);
                Part(props, "SaloonSign", me["TLB_Sign"], M["M_Poster_Whiskey"], new Vector3(barX, F + 52f, RoomHalf - 2.2f), Vector3.zero, false);
                string[] bottleMats = { "M_BottleGreen", "M_BottleAmber", "M_BottleClear", "M_BottleAmber" };
                foreach (var shelfY in new[] { F + 22.6f, F + 28.4f })
                {
                    Part(props, "Shelf", me["TLB_Shelf"], M["M_DarkWood"], new Vector3(barX, shelfY, RoomHalf - 4.1f), Vector3.zero);
                    for (float x = barX - 29f; x < barX + 29f; x += R(2.2f, 4.2f))
                        Part(props, "Bottle", me[rnd.NextDouble() < 0.6 ? "TLB_Bottle" : "TLB_BottleSmall"], M[bottleMats[rnd.Next(bottleMats.Length)]],
                            new Vector3(x, shelfY + 0.45f, RoomHalf - 4.1f + R(-0.8f, 0.8f)), new Vector3(0, R(0, 360), 0));
                }
                for (int i = 0; i < 5; i++)
                {
                    float x = barX + R(-30f, 30f);
                    Part(props, "BarBottle", me["TLB_Bottle"], M[bottleMats[rnd.Next(bottleMats.Length)]], new Vector3(x, F + 23.3f, barZ + R(-2.5f, 1.5f)), new Vector3(0, R(0, 360), 0));
                    Part(props, "ShotGlass", me["TLB_ShotGlass"], M["M_BottleClear"], new Vector3(x + R(1.4f, 2.6f), F + 23.3f, barZ + R(-3.5f, -1.5f)), Vector3.zero);
                }

                // ---------------- wanted posters
                var posters = new[]
                {
                    ("M_Poster_Wanted_0", new Vector3(-RoomHalf + 0.62f, F + 33f, 40f), -90f),
                    ("M_Poster_Wanted_1", new Vector3(RoomHalf - 0.62f, F + 34f, 28f), 90f),
                    ("M_Poster_Wanted_2", new Vector3(-6f, F + 33f, -RoomHalf + 0.62f), 180f),
                    ("M_Poster_Wanted_0", new Vector3(RoomHalf - 0.62f, F + 32f, -8f), 90f),
                };
                foreach (var (mat, pos, rot) in posters)
                    Part(props, "Poster", me["TLB_Poster"], M[mat], pos, new Vector3(0, rot, R(-4f, 4f)), false);

                // ---------------- dusk windows (west wall)
                foreach (var z in new[] { -24f, 20f })
                {
                    Part(props, "WindowFrame", me["TLB_WindowFrame"], M["M_DarkWood"], new Vector3(-RoomHalf + 1.0f, F + 33f, z), new Vector3(0, -90, 0));
                    Part(props, "WindowPane", me["TLB_WindowPane"], M["M_WindowDusk"], new Vector3(-RoomHalf + 0.66f, F + 33f, z), new Vector3(0, -90, 0), false);
                }

                // ---------------- swinging batwing doors (south wall) with the dusk street behind
                float doorX = 24f, doorZ = -RoomHalf + 1.6f;
                Part(props, "DoorOutside", me["TLB_DoorOutside"], M["M_WindowDusk"], new Vector3(doorX, F + 17f, -RoomHalf + 0.66f), new Vector3(0, 180, 0), false);
                Part(props, "DoorPostL", me["TLB_DoorPost"], M["M_DarkWood"], new Vector3(doorX - 9.6f, F + 17.5f, doorZ - 0.4f), Vector3.zero);
                Part(props, "DoorPostR", me["TLB_DoorPost"], M["M_DarkWood"], new Vector3(doorX + 9.6f, F + 17.5f, doorZ - 0.4f), Vector3.zero);
                Part(props, "DoorHeader", me["TLB_DoorHeader"], M["M_DarkWood"], new Vector3(doorX, F + 35f, doorZ - 0.4f), Vector3.zero);
                Part(props, "BatwingL", me["TLB_Batwing"], M["M_DarkWood"], new Vector3(doorX - 7.6f, F + 9f, doorZ), new Vector3(0, -10f, 0));
                Part(props, "BatwingR", me["TLB_Batwing"], M["M_DarkWood"], new Vector3(doorX + 7.6f, F + 9f, doorZ), new Vector3(0, 190f, 0));

                // ---------------- upright piano (east wall)
                var piano = new GameObject("Piano").transform;
                piano.SetParent(props, false);
                piano.localPosition = new Vector3(RoomHalf - 6.6f, F, -30f);
                piano.localEulerAngles = new Vector3(0, 90, 0);
                Part(piano, "Body", me["TLB_Piano"], M["M_PianoBlack"], Vector3.zero, Vector3.zero);
                Part(piano, "Keys", me["TLB_PianoKeys"], M["M_Ivory"], new Vector3(0, 14.85f, -8.2f), Vector3.zero);
                Part(piano, "Stool", me["TLB_Barrel"], M["M_DarkWood"], new Vector3(0, 0, -17f), Vector3.zero).transform.localScale = new Vector3(0.6f, 0.7f, 0.6f);
                Part(piano, "Bottle", me["TLB_Bottle"], M["M_BottleGreen"], new Vector3(10f, 26f, 0f), Vector3.zero);

                // ---------------- barrels
                foreach (var b in new[] { new Vector3(50f, 0, 50f), new Vector3(57f, 0, 41f), new Vector3(44.5f, 0, 57.5f), new Vector3(-52f, 0, -51f), new Vector3(-44f, 0, -56.5f), new Vector3(-56.5f, 0, -42f) })
                {
                    var at = new Vector3(b.x, F, b.z);
                    var rot = new Vector3(0, R(0, 360), 0);
                    Part(props, "Barrel", me["TLB_Barrel"], M["M_ChairWood"], at, rot);
                    Part(props, "Hoops", me["TLB_BarrelHoops"], M["M_BlackIron"], at, rot);
                }

                // ---------------- two side tables where nobody sits tonight
                foreach (var t in new[] { new Vector3(-40f, 0, -30f), new Vector3(40f, 0, 12f) })
                {
                    var at = new Vector3(t.x, F, t.z);
                    Part(props, "SideTable", me["TLB_SideTable"], M["M_TableWorn"], at, Vector3.zero);
                    for (int k = 0; k < 3; k++)
                    {
                        float a = (k * 120f + R(-20f, 20f) + t.x) * Mathf.Deg2Rad;
                        var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        var ch = Part(props, "SideChair", me["TLB_Chair"], M["M_ChairWood"], at + dir * 8.5f, Vector3.zero);
                        ch.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up) * Quaternion.Euler(0, R(-15f, 15f), 0);
                    }
                    Part(props, "Bottle", me["TLB_Bottle"], M[bottleMats[rnd.Next(bottleMats.Length)]], at + new Vector3(R(-2f, 2f), 15f, R(-2f, 2f)), Vector3.zero);
                    Part(props, "Glass", me["TLB_ShotGlass"], M["M_BottleClear"], at + new Vector3(R(-3f, 3f), 15f, R(-3f, 3f)), Vector3.zero);
                    Part(props, "Glass", me["TLB_ShotGlass"], M["M_BottleClear"], at + new Vector3(R(-3f, 3f), 15f, R(-3f, 3f)), Vector3.zero);
                }

                ctx.EnvPrefab = PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/TLB_Room.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>Wall-mounted oil lantern with its own warm point light (created in the scene, wired to the AtmosphereDirector).</summary>
        static void Lantern(Ctx ctx, Transform parent, Vector3 pos, float yRot)
        {
            var t = new GameObject("WallLantern").transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localEulerAngles = new Vector3(0, yRot, 0);
            Part(t, "Frame", ctx.Mesh["TLB_LanternFrame"], ctx.M["M_BlackIron"], Vector3.zero, Vector3.zero);
            var glass = Part(t, "Glass", ctx.Mesh["TLB_LanternGlass"], ctx.M["M_LanternGlass"], Vector3.zero, Vector3.zero, false);
            glass.isStatic = false;
            ctx.ExtraBulbs.Add(glass.GetComponent<MeshRenderer>());
            var l = new GameObject("Lantern Light").AddComponent<Light>();
            l.transform.SetParent(t, false);
            l.transform.localPosition = new Vector3(0f, 0f, 3.4f);
            l.type = LightType.Point;
            l.color = new Color(1f, 0.66f, 0.36f);
            l.intensity = 11f;
            l.range = 34f;
            l.shadows = LightShadows.None;
            ctx.RoomLights.Add(l);
        }

        // ------------------------------------------------------------------ Character (Quaternius UAL mannequin, CC0)
        /// <summary>
        /// Imports TLB_Cowboy.fbx as a Generic rig, makes the seated clips loop and builds an Animator Controller with
        /// one state per clip (state name = clip name). Skipped with a warning if the model is not in the project.
        /// </summary>
        static void ImportCharacter(Ctx ctx)
        {
            var mi = AssetImporter.GetAtPath(CowboyPath) as ModelImporter;
            if (mi == null)
            {
                Debug.LogWarning("[TheLastBlock] " + CowboyPath + " not found - players use the simple hooded bodies. (Build it with Tools~/blender_build_cowboy.py.)");
                return;
            }
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.importAnimation = true;
            mi.optimizeGameObjects = false;
            mi.importBlendShapes = false;
            mi.SaveAndReimport();
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                int bar = c.name.LastIndexOf('|');
                if (bar >= 0) c.name = c.name.Substring(bar + 1);
                c.loopTime = c.name.Contains("Loop");
                c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = clips;
            mi.SaveAndReimport();

            var ctrlPath = CharDir + "/TLB_Cowboy.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath) != null) AssetDatabase.DeleteAsset(ctrlPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            var sm = ctrl.layers[0].stateMachine;
            int n = 0;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(CowboyPath))
            {
                if (!(o is AnimationClip clip) || clip.name.StartsWith("__preview")) continue;
                var st = sm.AddState(clip.name, new Vector3(260, 60 * n++, 0));
                st.motion = clip;
                if (clip.name == "Sitting_Idle_Loop") sm.defaultState = st;
            }
            EditorUtility.SetDirty(ctrl);
            ctx.CowboyModel = AssetDatabase.LoadAssetAtPath<GameObject>(CowboyPath);
            ctx.CowboyController = ctrl;
            Debug.Log($"[TheLastBlock] Cowboy character imported with {n} animation clip(s).");
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
            r.cowboyModel = ctx.CowboyModel;
            r.cowboyAnimator = ctx.CowboyController;

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
            // warm, golden oil-lamp evening in a frontier saloon: bright enough to read every face and block
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.75f);
            bloom.threshold.Override(0.95f);
            bloom.scatter.Override(0.65f);
            bloom.tint.Override(new Color(1f, 0.85f, 0.65f));
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.2f);
            vig.smoothness.Override(0.45f);
            vig.color.Override(new Color(0.08f, 0.04f, 0.02f));
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.55f);
            ca.contrast.Override(10f);
            ca.saturation.Override(6f);
            ca.colorFilter.Override(new Color(1f, 0.94f, 0.84f));
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(12f);
            wb.tint.Override(3f);
            var lgg = profile.Add<LiftGammaGain>(true);
            lgg.lift.Override(new Vector4(1f, 0.97f, 0.92f, 0.02f));
            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.12f);
            grain.response.Override(0.8f);
            var chroma = profile.Add<ChromaticAberration>(true);
            chroma.intensity.Override(0.03f);

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

            // lighting: a warm frontier saloon at dusk - oil lamps, candles on a wagon wheel, low sun through the windows.
            // Bright and golden; only the far corners fall into amber haze.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.34f, 0.25f, 0.17f);
            RenderSettings.ambientEquatorColor = new Color(0.26f, 0.18f, 0.12f);
            RenderSettings.ambientGroundColor = new Color(0.11f, 0.075f, 0.05f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.0045f;
            RenderSettings.fogColor = new Color(0.24f, 0.15f, 0.09f);
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
            cam.farClipPlane = 240f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.07f, 0.04f);
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

            // the wagon-wheel chandelier over the table: rig swings (AtmosphereDirector), lights are children
            var lampRig = new GameObject("LampRig").transform;
            lampRig.position = new Vector3(0f, SeatLayout.LampHeight, 0f);
            var chain = Part(lampRig, "Chain", ctx.Mesh["TLB_Chain"], ctx.M["M_BlackIron"], new Vector3(0f, (CeilingY - SeatLayout.LampHeight) * 0.5f, 0f), Vector3.zero, false);
            var wheel = Part(lampRig, "WagonWheel", ctx.Mesh["TLB_WagonWheel"], ctx.M["M_DarkWood"], Vector3.zero, Vector3.zero);
            var candles = Part(lampRig, "Candles", ctx.Mesh["TLB_Candles"], ctx.M["M_Candle"], Vector3.zero, Vector3.zero, false);
            var bulb = Part(lampRig, "Flames", ctx.Mesh["TLB_Flames"], ctx.M["M_Flame"], Vector3.zero, Vector3.zero, false);
            chain.isStatic = wheel.isStatic = candles.isStatic = bulb.isStatic = false;

            var spot = new GameObject("Lamp Spot Light").AddComponent<Light>();
            spot.transform.SetParent(lampRig, false);
            spot.transform.localPosition = new Vector3(0f, -1.2f, 0f);
            spot.transform.localRotation = Quaternion.Euler(90, 0, 0);
            spot.type = LightType.Spot;
            spot.color = new Color(1f, 0.78f, 0.52f);
            spot.intensity = 420f;
            spot.range = 80f;
            spot.spotAngle = 120f;
            spot.innerSpotAngle = 60f;
            spot.shadows = LightShadows.Soft;
            spot.shadowStrength = 0.85f;
            spot.shadowBias = 0.02f;
            spot.shadowNormalBias = 0.3f;

            var glow = new GameObject("Bulb Glow").AddComponent<Light>();
            glow.transform.SetParent(lampRig, false);
            glow.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.7f, 0.4f);
            glow.intensity = 70f;
            glow.range = 70f;
            glow.shadows = LightShadows.None;

            // oil lanterns around the walls
            var lanterns = new GameObject("Wall Lanterns").transform;
            float ly = SeatLayout.FloorY + 34f;
            Lantern(ctx, lanterns, new Vector3(-RoomHalf + 0.8f, ly, -2f), 90f);
            Lantern(ctx, lanterns, new Vector3(RoomHalf - 0.8f, ly, 12f), -90f);
            Lantern(ctx, lanterns, new Vector3(RoomHalf - 0.8f, ly, -48f), -90f);
            Lantern(ctx, lanterns, new Vector3(-24f, ly, -RoomHalf + 0.8f), 0f);
            Lantern(ctx, lanterns, new Vector3(-40f, ly - 2f, RoomHalf - 2.2f), 180f);
            Lantern(ctx, lanterns, new Vector3(24f, ly - 2f, RoomHalf - 2.2f), 180f);

            // low dusk sun through the west windows (walls do not cast shadows, so it fills the room warmly)
            var sun = new GameObject("Dusk Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(26, 72, 0);
            sun.color = new Color(1f, 0.6f, 0.34f);
            sun.intensity = 0.8f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.6f;

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
            atmos.extraBulbs = ctx.ExtraBulbs.ToArray();
            atmos.roomLights = ctx.RoomLights.ToArray();
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
