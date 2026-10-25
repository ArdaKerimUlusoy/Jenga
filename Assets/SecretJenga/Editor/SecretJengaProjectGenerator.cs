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
    /// the post-processing profile, the fully wired kids'-bedroom scene (a paper lantern over a round kids' table, a bunk
    /// bed, toy shelves, a desk under the moonlit window, fairy lights, glow-in-the-dark stars, a door that opens), the
    /// rigged kid characters and the player/build settings.
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
        public const string CowboyPath = CharDir + "/TLB_Kid.fbx";       // the kids (built by Tools~/blender_build_kid.py)
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
                Step("Kids' bedroom", 0.52f); CreateEnvironmentPrefab(ctx);
                Step("Kid characters", 0.56f); ImportCharacter(ctx);
                Step("Network prefab list", 0.60f); CreatePrefabList(ctx);
                Step("Tasks and cards", 0.64f); CreateContent(ctx);
                Step("Asset registry", 0.68f); CreateRegistry(ctx);
                Step("Post processing", 0.74f); CreateVolumeProfile(ctx);
                Step("Adaptive Probe Volumes", 0.78f); EnableProbeVolumes();
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
            Debug.Log("[TheLastBlock] Project generated (kids' bedroom). Open " + ScenePath + " and press Play.");
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
                case "UI_Stamp": return new Vector4(48, 48, 48, 48);
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
            LitMat(ctx, "M_HatFelt", new Color(0.36f, 0.25f, 0.16f), 0.15f, null, null, null, 0f, null, true);
            LitMat(ctx, "M_HatFeltBlack", new Color(0.09f, 0.08f, 0.075f), 0.2f, null, null, null, 0f, null, true);
            LitMat(ctx, "M_HatBandLeather", new Color(0.16f, 0.09f, 0.05f), 0.45f);
            LitMat(ctx, "M_Felt", new Color(0.12f, 0.3f, 0.17f), 0.05f);
            LitMat(ctx, "M_ChipRed", new Color(0.6f, 0.1f, 0.08f), 0.5f);
            LitMat(ctx, "M_ChipBlue", new Color(0.12f, 0.2f, 0.5f), 0.5f);
            LitMat(ctx, "M_ChipWhite", new Color(0.88f, 0.85f, 0.78f), 0.5f);
            LitMat(ctx, "M_CardFace", new Color(0.93f, 0.9f, 0.82f), 0.3f);
            LitMat(ctx, "M_BrassDouble", new Color(0.8f, 0.6f, 0.3f), 0.72f, null, null, null, 1f, null, true);
            LitMat(ctx, "M_GunMetal", new Color(0.12f, 0.12f, 0.13f), 0.55f, null, null, null, 0.9f);
            LitMat(ctx, "M_PendantShade", new Color(0.18f, 0.24f, 0.18f), 0.55f, null, null, null, 0.4f, null, true);
            {
                var unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                var shaft = LoadOrCreate(Mats + "/M_LightShaft.mat", () => new Material(unlitShader));
                shaft.shader = unlitShader;
                RuntimeArt.MakeTransparent(shaft);
                shaft.SetTexture("_BaseMap", T("T_LightShaft.png"));
                shaft.SetColor("_BaseColor", new Color(1f, 0.72f, 0.42f, 0.16f));
                if (shaft.HasProperty("_Cull")) shaft.SetFloat("_Cull", 0f);
                EditorUtility.SetDirty(shaft);
                ctx.M["M_LightShaft"] = shaft;
            }
            foreach (var poster in new[] { "T_Poster_Wanted_0", "T_Poster_Wanted_1", "T_Poster_Wanted_2", "T_Poster_Whiskey" })
                LitMat(ctx, "M_" + poster.Substring(2), Color.white, 0.1f, T(poster + ".png"));

            // kids' bedroom
            {
                var kt = LitMat(ctx, "M_KidTable", Color.white, 0.42f, T("T_KidTable_Albedo.png"));
                kt.SetTextureScale("_BaseMap", new Vector2(0.28f, 0.28f));     // the round top's UVs span 7 units per tile: show the whole disc once
                kt.SetTextureOffset("_BaseMap", new Vector2(0.36f, 0.36f));
                EditorUtility.SetDirty(kt);
            }
            LitMat(ctx, "M_KidWhitePaint", new Color(0.95f, 0.94f, 0.91f), 0.45f);
            Color[] chairCols = { new Color(0.93f, 0.38f, 0.36f), new Color(0.38f, 0.62f, 0.92f), new Color(0.98f, 0.82f, 0.32f), new Color(0.46f, 0.78f, 0.46f), new Color(0.72f, 0.52f, 0.88f) };
            for (int i = 0; i < chairCols.Length; i++) LitMat(ctx, "M_KidChair_" + i, chairCols[i], 0.55f);
            LitMat(ctx, "M_KidFloor", Color.white, 0.4f, T("T_KidFloor_Albedo.png"));
            LitMat(ctx, "M_KidCeiling", new Color(0.93f, 0.92f, 0.96f), 0.1f);
            LitMat(ctx, "M_KidWallPaint", Color.white, 0.2f, T("T_KidWallPaint_Albedo.png"));
            LitMat(ctx, "M_KidWallpaper", Color.white, 0.12f, T("T_KidWallpaper_Albedo.png"));
            LitMat(ctx, "M_KidShelfPaint", new Color(0.96f, 0.88f, 0.74f), 0.4f, T("T_KidFloor_Albedo.png"), null, new Vector2(0.4f, 0.4f));
            LitMat(ctx, "M_KidDoor", new Color(0.62f, 0.78f, 0.92f), 0.45f);
            LitMat(ctx, "M_KidSheet", new Color(0.97f, 0.96f, 0.93f), 0.15f);
            LitMat(ctx, "M_Blanket", Color.white, 0.1f, T("T_Blanket_Albedo.png"), null, null, 0f, null, true);
            LitMat(ctx, "M_BlanketBlue", new Color(0.7f, 0.82f, 1f), 0.1f, T("T_Blanket_Albedo.png"), null, null, 0f, null, true);
            LitMat(ctx, "M_RugRound", Color.white, 0.05f, T("T_RugRound_Albedo.png"));
            LitMat(ctx, "M_PlayMat", Color.white, 0.12f, T("T_PlayMat_Albedo.png"));
            LitMat(ctx, "M_TeddyFur", new Color(0.62f, 0.42f, 0.26f), 0.05f);
            LitMat(ctx, "M_TeddyFurPink", new Color(0.93f, 0.66f, 0.72f), 0.05f);
            LitMat(ctx, "M_Beanbag", new Color(0.56f, 0.42f, 0.78f), 0.12f);
            LitMat(ctx, "M_Curtain", new Color(1f, 0.86f, 0.5f), 0.1f, null, null, null, 0f, null, true);
            LitMat(ctx, "M_ToyRed", new Color(0.88f, 0.22f, 0.2f), 0.7f);
            LitMat(ctx, "M_ToyBlue", new Color(0.22f, 0.48f, 0.9f), 0.7f);
            LitMat(ctx, "M_ToyYellow", new Color(0.98f, 0.82f, 0.18f), 0.7f);
            LitMat(ctx, "M_ToyGreen", new Color(0.3f, 0.72f, 0.36f), 0.7f);
            LitMat(ctx, "M_ToyPurple", new Color(0.6f, 0.4f, 0.85f), 0.7f);
            LitMat(ctx, "M_ToyOrange", new Color(0.98f, 0.55f, 0.15f), 0.7f);
            LitMat(ctx, "M_ToyBlack", new Color(0.06f, 0.06f, 0.07f), 0.6f);
            for (int i = 0; i < 4; i++) LitMat(ctx, "M_Drawing_" + i, Color.white, 0.05f, T($"T_Drawing_{i}.png"));
            LitMat(ctx, "M_PosterSpace", Color.white, 0.2f, T("T_PosterSpace.png"));
            {
                var night = LitMat(ctx, "M_WindowNight", Color.white, 0.6f, T("T_Window_Night.png"), null, null, 0f, Color.white * 0.55f);
                night.SetTexture("_EmissionMap", T("T_Window_Night.png"));
                var hall = LitMat(ctx, "M_Hallway", Color.white, 0.2f, T("T_Hallway.png"), null, null, 0f, Color.white * 0.7f);
                hall.SetTexture("_EmissionMap", T("T_Hallway.png"));
                EditorUtility.SetDirty(night);
                EditorUtility.SetDirty(hall);
            }
            LitMat(ctx, "M_FairyBulb", new Color(1f, 0.9f, 0.7f), 0.8f, null, null, null, 0f, new Color(1f, 0.78f, 0.45f) * 2.2f);
            LitMat(ctx, "M_PaperLantern", new Color(1f, 0.96f, 0.88f), 0.2f, null, null, null, 0f, new Color(1f, 0.85f, 0.62f) * 0.9f, true);
            {
                var unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                var star = LoadOrCreate(Mats + "/M_GlowStar.mat", () => new Material(unlitShader));
                star.shader = unlitShader;
                RuntimeArt.MakeTransparent(star);
                star.SetTexture("_BaseMap", T("T_GlowStar.png"));
                star.SetColor("_BaseColor", new Color(0.78f, 1f, 0.7f, 0.85f));
                EditorUtility.SetDirty(star);
                ctx.M["M_GlowStar"] = star;
                var moon = LoadOrCreate(Mats + "/M_MoonShaft.mat", () => new Material(unlitShader));
                moon.shader = unlitShader;
                RuntimeArt.MakeTransparent(moon);
                moon.SetTexture("_BaseMap", T("T_LightShaft.png"));
                moon.SetColor("_BaseColor", new Color(0.62f, 0.72f, 1f, 0.12f));
                if (moon.HasProperty("_Cull")) moon.SetFloat("_Cull", 0f);
                EditorUtility.SetDirty(moon);
                ctx.M["M_MoonShaft"] = moon;
            }

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
            AddLightmapUVs(mesh);
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

        [Serializable]
        class HandJson
        {
            public string[] bones;
            public float[] positions, normals, uvs, boneWeights;
            public int[] boneIndices, triangles, clothTriangles;
        }

        /// <summary>
        /// The skinned hand exported by Tools~/blender_build_hands.py, written straight into Models/TLB_Hand.asset
        /// (CopySerialized dropped the vertex buffer of this skinned mesh). Bone indices follow HandRig's bone order;
        /// the identity bind poses are placeholders that HandRig replaces from its own joints at runtime.
        /// </summary>
        static Mesh SaveHandMesh(Ctx ctx)
        {
            var path = CharDir + "/TLB_Hand.json";
            if (!File.Exists(path))
            {
                Debug.LogWarning("[TheLastBlock] " + path + " not found (run Tools~/blender_build_hands.py in Blender). The hands stay procedural.");
                return null;
            }
            var j = JsonUtility.FromJson<HandJson>(File.ReadAllText(path));
            int n = j.positions.Length / 3;
            var pos = new Vector3[n];
            var nrm = new Vector3[n];
            var uv = new Vector2[n];
            var bw = new BoneWeight[n];
            for (int i = 0; i < n; i++)
            {
                pos[i] = new Vector3(j.positions[i * 3], j.positions[i * 3 + 1], j.positions[i * 3 + 2]);
                nrm[i] = new Vector3(j.normals[i * 3], j.normals[i * 3 + 1], j.normals[i * 3 + 2]);
                uv[i] = new Vector2(j.uvs[i * 2], j.uvs[i * 2 + 1]);
                int k = i * 4;
                bw[i] = new BoneWeight
                {
                    boneIndex0 = j.boneIndices[k], weight0 = j.boneWeights[k],
                    boneIndex1 = j.boneIndices[k + 1], weight1 = j.boneWeights[k + 1],
                    boneIndex2 = j.boneIndices[k + 2], weight2 = j.boneWeights[k + 2],
                    boneIndex3 = j.boneIndices[k + 3], weight3 = j.boneWeights[k + 3],
                };
            }
            const string assetPath = Models + "/TLB_Hand.asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            bool fresh = m == null;
            if (fresh) m = new Mesh();
            m.Clear();
            m.name = "TLB_Hand";
            m.vertices = pos;
            m.normals = nrm;
            m.uv = uv;
            m.boneWeights = bw;
            var bind = new Matrix4x4[j.bones.Length];
            for (int i = 0; i < bind.Length; i++) bind[i] = Matrix4x4.identity;
            m.bindposes = bind;
            bool sleeve = j.clothTriangles != null && j.clothTriangles.Length > 0;
            m.subMeshCount = sleeve ? 2 : 1;
            m.SetTriangles(j.triangles, 0);
            if (sleeve) m.SetTriangles(j.clothTriangles, 1);
            m.RecalculateBounds();
            m.RecalculateTangents();
            if (fresh) AssetDatabase.CreateAsset(m, assetPath);
            else EditorUtility.SetDirty(m);
            ctx.Mesh["TLB_Hand"] = m;
            Debug.Log($"[TheLastBlock] Hand mesh saved: {m.vertexCount} vertices, {m.subMeshCount} submesh(es).");
            return m;
        }

        /// <summary>
        /// Every static mesh needs lightmap UVs even though it is lit by probes: the baker rasterises albedo and
        /// emission through them, and falling back to the tiled UV0 (far outside 0..1) crashes its atlas packing.
        /// </summary>
        static bool AddLightmapUVs(Mesh mesh)
        {
            if (mesh == null || mesh.vertexCount == 0 || mesh.boneWeights.Length > 0 || mesh.uv2.Length == mesh.vertexCount) return false;
            Unwrapping.GenerateSecondaryUVSet(mesh);
            return true;
        }

        static void AddLightmapUVsToModels()
        {
            int added = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Mesh", new[] { Models }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".asset")) continue;
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (!AddLightmapUVs(mesh)) continue;
                EditorUtility.SetDirty(mesh);
                added++;
            }
            if (added > 0) Debug.Log($"[TheLastBlock] Added lightmap UVs to {added} room mesh(es).");
        }

        const float RoomHalf = 50f;     // the bedroom is 5 m across
        const float CeilingY = 40f;     // 2.7 m from floor to ceiling
        const float WainscotH = 18f;    // painted up to 90 cm, wallpaper above

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
            SaveMesh(ctx, SaloonMeshes.WagonWheel(8.6f), "TLB_WagonWheel");
            SaveMesh(ctx, SaloonMeshes.Candles(8.6f, 10), "TLB_Candles");
            SaveMesh(ctx, SaloonMeshes.Flames(8.6f, 10), "TLB_Flames");
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
            SaveMesh(ctx, SaloonMeshes.Hat(), "TLB_Hat");
            SaveMesh(ctx, SaloonMeshes.HatBand(), "TLB_HatBand");
            SaveMesh(ctx, SaloonMeshes.ChipStack(6), "TLB_Chips6");
            SaveMesh(ctx, SaloonMeshes.ChipStack(11), "TLB_Chips11");
            SaveMesh(ctx, SaloonMeshes.Card(), "TLB_Card");
            SaveMesh(ctx, SaloonMeshes.Spittoon(), "TLB_Spittoon");
            SaveMesh(ctx, SaloonMeshes.Rifle(), "TLB_Rifle");
            SaveMesh(ctx, SaloonMeshes.PendantLamp(), "TLB_PendantLamp");
            SaveMesh(ctx, SaloonMeshes.PendantGlass(), "TLB_PendantGlass");
            SaveMesh(ctx, SaloonMeshes.ShaftQuad(), "TLB_ShaftQuad");
            SaveMesh(ctx, ProceduralMeshes.Cylinder(5.6f, 0.12f, 32, "TLB_FeltDisc"), "TLB_FeltDisc");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(2.6f, CeilingY - SeatLayout.FloorY - 2.6f, 2.6f), 0.25f, false, new Vector2(8f, 8f), "TLB_Post"), "TLB_Post");
            SaveMesh(ctx, ProceduralMeshes.Cylinder(0.25f, 1.6f, 8, "TLB_Peg"), "TLB_Peg");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(20.6f, 1.6f, 2f), 0.15f, false, new Vector2(8f, 8f), "TLB_DoorHeader"), "TLB_DoorHeader");
            SaveMesh(ctx, ProceduralMeshes.Pipe(0.7f, RoomHalf * 2f), "TLB_PipeLong");
            SaveMesh(ctx, ProceduralMeshes.Pipe(0.45f, RoomHalf * 2f), "TLB_PipeThin");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(14f, 26f, 0.8f), 0.15f, false, new Vector2(14f, 26f), "TLB_Door"), "TLB_Door");
            SaveMesh(ctx, ProceduralMeshes.BeveledBox(new Vector3(17f, 28.5f, 0.6f), 0.1f, false, new Vector2(17f, 28f), "TLB_DoorFrame"), "TLB_DoorFrame");

            // kids' bedroom
            SaveMesh(ctx, KidsRoomMeshes.Disc(24f, 0.25f, 64, "KR_RugRound"), "KR_RugRound");
            SaveMesh(ctx, ProceduralMeshes.Plane(24f, 24f, 1f, 1f, "KR_PlayMat"), "KR_PlayMat");
            SaveMesh(ctx, KidsRoomMeshes.BunkFrame(), "KR_BunkFrame");
            SaveMesh(ctx, KidsRoomMeshes.Mattress(), "KR_Mattress");
            SaveMesh(ctx, KidsRoomMeshes.Blanket(), "KR_Blanket");
            SaveMesh(ctx, KidsRoomMeshes.Pillow(), "KR_Pillow");
            SaveMesh(ctx, KidsRoomMeshes.ToyShelf(), "KR_ToyShelf");
            SaveMesh(ctx, KidsRoomMeshes.Cube(3f), "KR_Cube3");
            SaveMesh(ctx, KidsRoomMeshes.Book(), "KR_Book");
            SaveMesh(ctx, ProceduralMeshes.UVSphere(3f, 14, 22, "KR_Ball"), "KR_Ball");
            SaveMesh(ctx, ProceduralMeshes.UVSphere(1.7f, 12, 18, "KR_BallSmall"), "KR_BallSmall");
            SaveMesh(ctx, KidsRoomMeshes.Teddy(), "KR_Teddy");
            SaveMesh(ctx, KidsRoomMeshes.TeddyFace(), "KR_TeddyFace");
            SaveMesh(ctx, KidsRoomMeshes.Beanbag(), "KR_Beanbag");
            SaveMesh(ctx, KidsRoomMeshes.ChestBody(), "KR_ChestBody");
            SaveMesh(ctx, KidsRoomMeshes.ChestLid(), "KR_ChestLid");
            SaveMesh(ctx, KidsRoomMeshes.Desk(), "KR_Desk");
            SaveMesh(ctx, KidsRoomMeshes.DeskLamp(), "KR_DeskLamp");
            SaveHandMesh(ctx);
            SaveMesh(ctx, ProceduralMeshes.Cylinder(0.28f, 3.4f, 6, "KR_Crayon"), "KR_Crayon");
            SaveMesh(ctx, SaloonMeshes.Quad(8f, 6f, "KR_DeskPaper"), "KR_DeskPaper");
            SaveMesh(ctx, KidsRoomMeshes.PaperLantern(5.2f), "KR_PaperLantern");
            SaveMesh(ctx, KidsRoomMeshes.CarBody(), "KR_CarBody");
            SaveMesh(ctx, KidsRoomMeshes.CarWheels(), "KR_CarWheels");
            SaveMesh(ctx, KidsRoomMeshes.Duck(), "KR_Duck");
            SaveMesh(ctx, KidsRoomMeshes.DuckBeak(), "KR_DuckBeak");
            SaveMesh(ctx, KidsRoomMeshes.RingPole(), "KR_RingPole");
            for (int k = 0; k < 5; k++) SaveMesh(ctx, SaloonMeshes.Ring(2.5f - k * 0.32f, 0.62f, 24, "KR_ToyRing_" + k), "KR_ToyRing_" + k);
            SaveMesh(ctx, KidsRoomMeshes.Curtain(10f, 26f), "KR_Curtain");
            SaveMesh(ctx, KidsRoomMeshes.DoorFrame(14.4f, 26.6f), "KR_DoorFrame");
            SaveMesh(ctx, SaloonMeshes.Quad(13.4f, 26.4f, "KR_HallwayQuad"), "KR_HallwayQuad");
            SaveMesh(ctx, SaloonMeshes.Quad(5f, 3.75f, "KR_DoorSign"), "KR_DoorSign");
            SaveMesh(ctx, SaloonMeshes.Quad(10f, 7.5f, "KR_DrawingQuad"), "KR_DrawingQuad");
            SaveMesh(ctx, SaloonMeshes.Quad(9f, 12f, "KR_PosterQuad"), "KR_PosterQuad");
            SaveMesh(ctx, SaloonMeshes.Quad(2.4f, 2.4f, "KR_StarQuad"), "KR_StarQuad");
            {
                var na = new Vector3(-RoomHalf + 4f, CeilingY - 2.5f, RoomHalf - 1.2f);
                var nb = new Vector3(RoomHalf - 4f, CeilingY - 2.5f, RoomHalf - 1.2f);
                var wa = new Vector3(-RoomHalf + 1.2f, CeilingY - 2.5f, -RoomHalf + 4f);
                var wb = new Vector3(-RoomHalf + 1.2f, CeilingY - 2.5f, RoomHalf - 4f);
                SaveMesh(ctx, KidsRoomMeshes.FairyBulbs(na, nb, 26, 3.5f, "KR_Fairy_N"), "KR_Fairy_N");
                SaveMesh(ctx, KidsRoomMeshes.FairyWire(na, nb, 3.5f, "KR_FairyWire_N"), "KR_FairyWire_N");
                SaveMesh(ctx, KidsRoomMeshes.FairyBulbs(wa, wb, 24, 3.5f, "KR_Fairy_W"), "KR_Fairy_W");
                SaveMesh(ctx, KidsRoomMeshes.FairyWire(wa, wb, 3.5f, "KR_FairyWire_W"), "KR_FairyWire_W");
            }
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
            // lit by the Adaptive Probe Volume; the procedural meshes have no lightmap UVs
            mr.receiveGI = ReceiveGI.LightProbes;
            go.isStatic = true;
            return go;
        }

        /// <summary>
        /// The kids' bedroom in the evening: honey floorboards with a round rainbow rug under the table, pastel
        /// walls with a cloud-and-star wallpaper, a bunk bed, toy shelves full of blocks and books, a desk under the
        /// window (a moon over the rooftops), a toy chest, a beanbag, a play mat with a toy car, crayon drawings,
        /// glow-in-the-dark stars on the ceiling and the bedroom door (it opens - see KidsRoomLife).
        /// The players' round kids' table with five coloured chairs stands in the middle.
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
                var rnd = new System.Random(2011);
                float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

                // table (top surface at y = 0) - the top has a collider so blocks rest and fall on it
                var table = new GameObject("Table").transform;
                table.SetParent(root.transform, false);
                Part(table, "TableTop", me["TLB_TableTop"], M["M_KidTable"], Vector3.zero, Vector3.zero);
                var tableCol = new GameObject("TableCollider"); // round (static, non-convex) so blocks can fall off the edge
                tableCol.transform.SetParent(table, false);
                tableCol.transform.localPosition = new Vector3(0f, -0.3f, 0f); // 0.6 thick cylinder: top at y = 0
                tableCol.AddComponent<MeshCollider>().sharedMesh = me["TLB_TableCollider"];
                tableCol.isStatic = true;
                Part(table, "Pedestal", me["TLB_TableBase"], M["M_KidWhitePaint"], new Vector3(0f, F, 0f), Vector3.zero);

                // chairs: one colour each
                var chairs = new GameObject("Chairs").transform;
                chairs.SetParent(root.transform, false);
                for (int s = 0; s < SeatLayout.SeatCount; s++)
                {
                    var c = Part(chairs, "Chair_" + (s + 1), me["TLB_Chair"], M["M_KidChair_" + s], SeatLayout.ChairPosition(s), Vector3.zero);
                    c.transform.rotation = SeatLayout.Facing(s) * Quaternion.Euler(0f, R(-3f, 3f), 0f);
                }

                // ---------------- room shell
                var room = new GameObject("Room").transform;
                room.SetParent(root.transform, false);
                var floor = Part(room, "Floor", me["TLB_Floor"], M["M_KidFloor"], new Vector3(0, F, 0), Vector3.zero);
                var fc = floor.AddComponent<BoxCollider>();
                fc.size = new Vector3(RoomHalf * 2f, 0.2f, RoomHalf * 2f);
                fc.center = new Vector3(0, -0.1f, 0);
                Part(room, "Ceiling", me["TLB_Floor"], M["M_KidCeiling"], new Vector3(0, CeilingY, 0), new Vector3(180, 0, 0), false);

                float lowY = F + WainscotH * 0.5f, highY = F + WainscotH + (wallH - WainscotH) * 0.5f;
                void Wall(string n, Vector3 at, float yRot, Vector3 inward)
                {
                    Part(room, n + "_Paint", me["TLB_WallLow"], M["M_KidWallPaint"], new Vector3(at.x, lowY, at.z), new Vector3(0, yRot, 0), false);
                    Part(room, n + "_Paper", me["TLB_WallHigh"], M["M_KidWallpaper"], new Vector3(at.x, highY, at.z), new Vector3(0, yRot, 0), false);
                    Part(room, n + "_Rail", me["TLB_ChairRail"], M["M_KidWhitePaint"], new Vector3(at.x, F + WainscotH, at.z) + inward * 0.9f, new Vector3(0, yRot, 0), false);
                    Part(room, n + "_Skirting", me["TLB_ChairRail"], M["M_KidWhitePaint"], new Vector3(at.x, F + 0.6f, at.z) + inward * 0.9f, new Vector3(0, yRot, 0), false);
                }
                Wall("WallN", new Vector3(0, 0, RoomHalf), 0f, Vector3.back);
                Wall("WallS", new Vector3(0, 0, -RoomHalf), 0f, Vector3.forward);
                Wall("WallE", new Vector3(RoomHalf, 0, 0), 90f, Vector3.left);
                Wall("WallW", new Vector3(-RoomHalf, 0, 0), 90f, Vector3.right);

                var props = new GameObject("KidsRoom").transform;
                props.SetParent(root.transform, false);
                string[] toyMats = { "M_ToyRed", "M_ToyBlue", "M_ToyYellow", "M_ToyGreen", "M_ToyPurple", "M_ToyOrange" };
                string Toy() => toyMats[rnd.Next(toyMats.Length)];

                // ---------------- rugs
                Part(props, "RoundRug", me["KR_RugRound"], M["M_RugRound"], new Vector3(0f, F + 0.02f, 0f), new Vector3(0f, 18f, 0f));
                var mat = Part(props, "PlayMat", me["KR_PlayMat"], M["M_PlayMat"], new Vector3(27f, F + 0.04f, 27f), new Vector3(0f, -14f, 0f));
                mat.GetComponent<MeshRenderer>().receiveShadows = true;

                // ---------------- bunk bed against the west wall
                var bed = new GameObject("BunkBed").transform;
                bed.SetParent(props, false);
                bed.localPosition = new Vector3(-RoomHalf + 11.2f, F, 6f);
                Part(bed, "Frame", me["KR_BunkFrame"], M["M_KidWhitePaint"], Vector3.zero, Vector3.zero);
                foreach (var (y, blanket) in new[] { (7.1f, "M_Blanket"), (25.1f, "M_BlanketBlue") })
                {
                    Part(bed, "Mattress", me["KR_Mattress"], M["M_KidSheet"], new Vector3(0f, y, 0f), Vector3.zero);
                    Part(bed, "Blanket", me["KR_Blanket"], M[blanket], new Vector3(0f, y + 1.7f, 4.6f), Vector3.zero);
                    Part(bed, "Pillow", me["KR_Pillow"], M["M_KidSheet"], new Vector3(0f, y + 2.6f, -15.5f), new Vector3(0f, 0f, R(-4f, 4f)));
                }
                var teddyTop = new GameObject("TeddyTop").transform;
                teddyTop.SetParent(bed, false);
                teddyTop.localPosition = new Vector3(3f, 27.4f, -10f);
                teddyTop.localEulerAngles = new Vector3(0f, 70f, 0f);
                Part(teddyTop, "Teddy", me["KR_Teddy"], M["M_TeddyFur"], Vector3.zero, Vector3.zero);
                Part(teddyTop, "Face", me["KR_TeddyFace"], M["M_ToyBlack"], Vector3.zero, Vector3.zero, false);

                // ---------------- toy shelves on the north wall, full of blocks, balls, books and a teddy
                foreach (var sx in new[] { -22f, 14f })
                {
                    var shelf = new GameObject("ToyShelf").transform;
                    shelf.SetParent(props, false);
                    shelf.localPosition = new Vector3(sx, F, RoomHalf - 5.2f);
                    Part(shelf, "Shelf", me["KR_ToyShelf"], M["M_KidShelfPaint"], Vector3.zero, Vector3.zero);
                    foreach (var y in new[] { 1.1f, 12.0f, 23.0f })
                    {
                        float x = -13f;
                        while (x < 12f)
                        {
                            int kind = rnd.Next(4);
                            if (kind == 0)
                            {
                                for (int k = 0; k < 4 && x < 12f; k++, x += 1.5f)
                                    Part(shelf, "Book", me["KR_Book"], M[Toy()], new Vector3(x, y + 3f, -0.5f), new Vector3(0f, 0f, R(-3f, 3f)));
                            }
                            else if (kind == 1)
                            {
                                Part(shelf, "Block", me["KR_Cube3"], M[Toy()], new Vector3(x + 1.5f, y + 1.5f, R(-1.5f, 0.5f)), new Vector3(0f, R(-20f, 20f), 0f));
                                Part(shelf, "Block", me["KR_Cube3"], M[Toy()], new Vector3(x + 1.6f, y + 4.5f, R(-1.5f, 0.5f)), new Vector3(0f, R(-30f, 30f), 0f));
                                x += 3.8f;
                            }
                            else if (kind == 2)
                            {
                                Part(shelf, "Ball", me["KR_BallSmall"], M[Toy()], new Vector3(x + 1.7f, y + 1.7f, -0.5f), Vector3.zero);
                                x += 3.8f;
                            }
                            else
                            {
                                var duck = Part(shelf, "Duck", me["KR_Duck"], M["M_ToyYellow"], new Vector3(x + 2f, y, -0.5f), new Vector3(0f, 180f + R(-30f, 30f), 0f));
                                Part(duck.transform, "Beak", me["KR_DuckBeak"], M["M_ToyOrange"], Vector3.zero, Vector3.zero, false);
                                x += 5f;
                            }
                            x += R(0.4f, 1.4f);
                        }
                    }
                }

                // ---------------- desk under the east window, with a desk lamp, crayons and a drawing
                var desk = new GameObject("Desk").transform;
                desk.SetParent(props, false);
                desk.localPosition = new Vector3(RoomHalf - 8f, F, -14f);
                desk.localEulerAngles = new Vector3(0f, 90f, 0f);
                Part(desk, "Desk", me["KR_Desk"], M["M_KidShelfPaint"], Vector3.zero, Vector3.zero);
                var lampBase = Part(desk, "DeskLamp", me["KR_DeskLamp"], M["M_ToyRed"], new Vector3(-9f, 15.2f, 2.5f), new Vector3(0f, -30f, 0f));
                for (int k = 0; k < 6; k++)
                    Part(desk, "Crayon", me["KR_Crayon"], M[toyMats[k % toyMats.Length]], new Vector3(2f + k * 0.9f, 15.5f, -2f + R(-0.6f, 0.6f)), new Vector3(0f, R(-30f, 30f), 90f));
                Part(desk, "Paper", me["KR_DeskPaper"], M["M_Drawing_3"], new Vector3(3f, 15.25f, 1.5f), new Vector3(90f, R(-15f, 15f), 0f), false);
                var kidChair = Part(props, "DeskChair", me["TLB_Chair"], M["M_KidChair_2"], new Vector3(RoomHalf - 17f, F, -14f), new Vector3(0f, 90f, 0f));
                kidChair.transform.localScale = new Vector3(0.85f, 0.85f, 0.85f);

                // ---------------- window (east wall) with the night sky, and curtains
                float winZ = 12f, winY = F + 31f;
                Part(props, "WindowFrame", me["TLB_WindowFrame"], M["M_KidWhitePaint"], new Vector3(RoomHalf - 1.0f, winY, winZ), new Vector3(0, 90, 0));
                Part(props, "WindowPane", me["TLB_WindowPane"], M["M_WindowNight"], new Vector3(RoomHalf - 0.66f, winY, winZ), new Vector3(0, 90, 0), false);
                foreach (var side in new[] { -1f, 1f })
                    Part(props, "Curtain", me["KR_Curtain"], M["M_Curtain"], new Vector3(RoomHalf - 2.2f, winY + 13f, winZ + side * 11.5f), new Vector3(0, 90, 0));
                Part(props, "CurtainRod", me["TLB_PipeThin"], M["M_KidWhitePaint"], new Vector3(RoomHalf - 2.2f, winY + 13.4f, winZ), new Vector3(0, 90, 0), false)
                    .transform.localScale = new Vector3(0.28f, 1f, 1f);

                // ---------------- the bedroom door (south wall): a hinged door in front of the lit hallway
                float doorX = -22f;
                Part(props, "Hallway", me["KR_HallwayQuad"], M["M_Hallway"], new Vector3(doorX, F + 13.4f, -RoomHalf + 0.62f), new Vector3(0, 180, 0), false);
                Part(props, "DoorFrame", me["KR_DoorFrame"], M["M_KidWhitePaint"], new Vector3(doorX, F, -RoomHalf + 0.9f), Vector3.zero);
                var hinge = new GameObject("BedroomDoor").transform;
                hinge.SetParent(props, false);
                hinge.localPosition = new Vector3(doorX - 7f, F, -RoomHalf + 1.25f);
                var doorLeaf = Part(hinge, "Leaf", me["TLB_Door"], M["M_KidDoor"], new Vector3(7f, 13.2f, 0f), Vector3.zero);
                doorLeaf.isStatic = false;
                hinge.gameObject.isStatic = false;
                var knob = Part(hinge, "Knob", me["KR_BallSmall"], M["M_Brass"], new Vector3(12.2f, 12.5f, 0.9f), Vector3.zero);
                knob.transform.localScale = Vector3.one * 0.32f;
                var sign = Part(hinge, "DoorSign", me["KR_DoorSign"], M["M_Drawing_2"], new Vector3(7f, 20f, 0.48f), new Vector3(0f, 180f, R(-4f, 4f)), false);
                knob.isStatic = sign.isStatic = false;   // they swing with the door

                // ---------------- toy chest (south wall), beanbag (north-east corner), toys on the floor
                var chest = new GameObject("ToyChest").transform;
                chest.SetParent(props, false);
                chest.localPosition = new Vector3(20f, F, -RoomHalf + 7.5f);
                Part(chest, "Body", me["KR_ChestBody"], M["M_ToyBlue"], Vector3.zero, Vector3.zero);
                Part(chest, "Lid", me["KR_ChestLid"], M["M_ToyYellow"], new Vector3(0f, 11f, 6.3f), new Vector3(-28f, 0f, 0f));
                Part(chest, "Ball", me["KR_Ball"], M["M_ToyRed"], new Vector3(-4f, 11.5f, 0f), Vector3.zero);
                Part(chest, "Block", me["KR_Cube3"], M["M_ToyGreen"], new Vector3(4f, 11.2f, -1f), new Vector3(20f, 30f, 10f));

                Part(props, "Beanbag", me["KR_Beanbag"], M["M_Beanbag"], new Vector3(38f, F, 38f), new Vector3(0f, 0f, 0f));
                var teddy2 = new GameObject("TeddyBeanbag").transform;
                teddy2.SetParent(props, false);
                teddy2.localPosition = new Vector3(37f, F + 7.4f, 37f);
                teddy2.localEulerAngles = new Vector3(-12f, 225f, 0f);
                Part(teddy2, "Teddy", me["KR_Teddy"], M["M_TeddyFurPink"], Vector3.zero, Vector3.zero);
                Part(teddy2, "Face", me["KR_TeddyFace"], M["M_ToyBlack"], Vector3.zero, Vector3.zero, false);

                // floor toys - kept out of the walking path from the door around the table
                var car = new GameObject("ToyCar").transform;
                car.SetParent(props, false);
                car.localPosition = new Vector3(22f, F + 0.05f, 30f);
                car.localEulerAngles = new Vector3(0f, -40f, 0f);
                Part(car, "Body", me["KR_CarBody"], M["M_ToyRed"], Vector3.zero, Vector3.zero);
                Part(car, "Wheels", me["KR_CarWheels"], M["M_ToyBlack"], Vector3.zero, Vector3.zero);
                foreach (var (x, z) in new[] { (-14f, 36f), (-10f, 38.5f), (-12f, 34f), (34f, 20f), (-30f, -24f) })
                    Part(props, "FloorBlock", me["KR_Cube3"], M[Toy()], new Vector3(x, F + 1.5f, z), new Vector3(0f, R(0f, 90f), 0f));
                Part(props, "FloorBlock", me["KR_Cube3"], M[Toy()], new Vector3(-11.5f, F + 4.5f, 36.6f), new Vector3(0f, R(0f, 90f), 0f));
                Part(props, "Ball", me["KR_Ball"], M["M_ToyBlue"], new Vector3(40f, F + 3f, -36f), Vector3.zero);
                Part(props, "Ball", me["KR_BallSmall"], M["M_ToyYellow"], new Vector3(-26f, F + 1.5f, 30f), Vector3.zero);
                var duckF = Part(props, "Duck", me["KR_Duck"], M["M_ToyYellow"], new Vector3(30f, F, -42f), new Vector3(0f, 150f, 0f));
                Part(duckF.transform, "Beak", me["KR_DuckBeak"], M["M_ToyOrange"], Vector3.zero, Vector3.zero, false);
                var rings = new GameObject("RingToy").transform;
                rings.SetParent(props, false);
                rings.localPosition = new Vector3(-24f, F, 26f);
                Part(rings, "Pole", me["KR_RingPole"], M["M_KidShelfPaint"], Vector3.zero, Vector3.zero);
                string[] ringMats = { "M_ToyRed", "M_ToyOrange", "M_ToyYellow", "M_ToyGreen", "M_ToyBlue" };
                for (int k = 0; k < 5; k++)
                    Part(rings, "Ring", me["KR_ToyRing_" + k], M[ringMats[k]], new Vector3(0f, 1.6f + k * 1.35f, 0f), Vector3.zero);

                // ---------------- drawings and a poster on the walls
                var art = new[]
                {
                    ("M_Drawing_0", new Vector3(-26f, F + 42f, RoomHalf - 0.6f), 0f),
                    ("M_Drawing_1", new Vector3(10f, F + 41f, RoomHalf - 0.6f), 0f),
                    ("M_Drawing_2", new Vector3(-RoomHalf + 0.6f, F + 43f, 30f), -90f),
                    ("M_Drawing_3", new Vector3(-RoomHalf + 0.6f, F + 41f, -14f), -90f),
                    ("M_Drawing_0", new Vector3(4f, F + 34f, -RoomHalf + 0.6f), 180f),
                };
                foreach (var (m, pos, rot) in art)
                    Part(props, "Drawing", me["KR_DrawingQuad"], M[m], pos, new Vector3(0, rot, R(-5f, 5f)), false);
                Part(props, "SpacePoster", me["KR_PosterQuad"], M["M_PosterSpace"], new Vector3(RoomHalf - 0.6f, F + 34f, -16f), new Vector3(0, 90, R(-2f, 2f)), false);

                // ---------------- glow-in-the-dark stars on the ceiling
                for (int k = 0; k < 34; k++)
                {
                    var p = new Vector3(R(-RoomHalf + 4f, RoomHalf - 4f), CeilingY - 0.08f, R(-RoomHalf + 4f, RoomHalf - 4f));
                    if (new Vector2(p.x, p.z).magnitude < 8f) continue;   // the lamp hangs there
                    var st = Part(props, "GlowStar", me["KR_StarQuad"], M["M_GlowStar"], p, new Vector3(-90f, R(0f, 360f), 0f), false);
                    st.transform.localScale = Vector3.one * R(0.7f, 1.5f);
                }

                ctx.EnvPrefab = PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/TLB_Room.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>A string of fairy lights near the ceiling: glowing bulbs + wire, one soft point light per string.</summary>
        static void FairyLights(Ctx ctx, Transform parent, Vector3 a, Vector3 b, string name)
        {
            var t = new GameObject("FairyLights_" + name).transform;
            t.SetParent(parent, false);
            var bulbs = Part(t, "Bulbs", ctx.Mesh["KR_Fairy_" + name], ctx.M["M_FairyBulb"], Vector3.zero, Vector3.zero, false);
            bulbs.isStatic = false;
            ctx.ExtraBulbs.Add(bulbs.GetComponent<MeshRenderer>());
            Part(t, "Wire", ctx.Mesh["KR_FairyWire_" + name], ctx.M["M_ToyBlack"], Vector3.zero, Vector3.zero, false);
            var l = new GameObject("Fairy Light").AddComponent<Light>();
            l.transform.SetParent(t, false);
            l.transform.position = (a + b) * 0.5f - Vector3.up * 3.5f;
            l.type = LightType.Point;
            l.color = new Color(1f, 0.82f, 0.55f);
            l.intensity = 9f;
            l.range = 40f;
            l.shadows = LightShadows.None;
            ctx.RoomLights.Add(l);
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
                Debug.LogWarning("[TheLastBlock] " + CowboyPath + " not found - players use the simple hooded bodies. (Build it with Tools~/blender_build_kid.py.)");
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
                c.loopTime = c.name.Contains("Loop") || c.name == "Fixing_Kneeling";
                c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = clips;
            mi.SaveAndReimport();

            var ctrlPath = CharDir + "/TLB_Kid.controller";
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
            Debug.Log($"[TheLastBlock] Kid character imported with {n} animation clip(s).");
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
            r.handMesh = ctx.Mesh.TryGetValue("TLB_Hand", out var handMesh) ? handMesh : null;
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
            // kids' fonts (SIL OFL, with Turkish letters): rounded Baloo 2 titles, Patrick Hand handwriting, Grandstander
            r.fontTitle = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/Baloo2-ExtraBold.ttf");
            r.fontBody = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/PatrickHand-Regular.ttf");
            r.fontType = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/Grandstander-SemiBold.ttf");
            if (r.fontTitle == null) r.fontTitle = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/IMFeENsc28P.ttf");
            if (r.fontBody == null) r.fontBody = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/IMFeENrm28P.ttf");
            if (r.fontType == null) r.fontType = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/SpecialElite-Regular.ttf");
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
            // kids' room: a music box instead of the piano, a ticking clock instead of the crowd
            r.saloonPiano = AC("MUS_Playroom_Loop"); if (r.saloonPiano == null) r.saloonPiano = AC("MUS_MusicBox_Loop"); r.saloonMurmur = AC("SFX_Room_Tone_Loop");
            r.lighter = null; r.exhale = AC("SFX_Slurp"); r.gulp = AC("SFX_Gulp");
            r.distantMetal = new[] { AC("SFX_Toy_0"), AC("SFX_Toy_1"), AC("SFX_Toy_2") };
            r.squeak = AC("SFX_Squeak"); r.bonk = AC("SFX_Bonk"); r.birdiesLoop = AC("SFX_Birdies_Loop"); r.whoosh = AC("SFX_Whoosh");
            r.steps = new[] { AC("SFX_Step_0"), AC("SFX_Step_1"), AC("SFX_Step_2") };
            r.doorCreak = AC("SFX_DoorCreak"); r.sadTrombone = AC("SFX_SadTrombone");
            r.collapseSting = r.sadTrombone != null ? r.sadTrombone : r.collapseSting;   // wah-wah-waaah instead of a horror sting
            r.music = null;          // no horror drone in a kids' room: the music box plays, the heartbeat still comes at high tension
            r.lampHumLoop = null;    // a paper lantern does not hum
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
            // a cosy kids' bedroom in the evening: warm lantern and fairy lights, cool moonlight, clean saturated colours
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.3f);
            bloom.threshold.Override(1.2f);
            bloom.scatter.Override(0.55f);
            bloom.tint.Override(new Color(1f, 0.92f, 0.8f));
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.16f);
            vig.smoothness.Override(0.45f);
            vig.color.Override(new Color(0.05f, 0.04f, 0.09f));
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.3f);
            ca.contrast.Override(6f);
            ca.saturation.Override(14f);
            ca.colorFilter.Override(new Color(1f, 0.98f, 0.95f));
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(4f);
            wb.tint.Override(2f);
            var lgg = profile.Add<LiftGammaGain>(true);
            lgg.lift.Override(new Vector4(0.97f, 0.97f, 1f, 0.02f));
            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.04f);
            grain.response.Override(0.8f);

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

            // lighting: a kids' bedroom in the evening - a warm paper lantern over the table, a desk lamp, fairy lights,
            // cool moonlight through the window. Bright and friendly; nothing hides in the corners.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.36f, 0.37f, 0.46f);
            RenderSettings.ambientEquatorColor = new Color(0.3f, 0.27f, 0.27f);
            RenderSettings.ambientGroundColor = new Color(0.14f, 0.11f, 0.09f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.0025f;
            RenderSettings.fogColor = new Color(0.16f, 0.15f, 0.22f);
            RenderSettings.skybox = null;
            // every light is baked into an Adaptive Probe Volume; nothing is lit in real time
            var lighting = new LightingSettings
            {
                name = "TLB_Lighting", bakedGI = true, realtimeGI = false, lightmapper = LightingSettings.Lightmapper.ProgressiveGPU,
                directionalityMode = LightmapsMode.NonDirectional, lightmapResolution = 2f, maxBounces = 3,
            };
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
            cam.backgroundColor = new Color(0.08f, 0.08f, 0.14f);
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

            // the paper lantern over the table: the rig swings (AtmosphereDirector), the lights are children
            var lampRig = new GameObject("LampRig").transform;
            lampRig.position = new Vector3(0f, SeatLayout.LampHeight, 0f);
            var cord = Part(lampRig, "Cord", ctx.Mesh["TLB_Cord"], ctx.M["M_KidWhitePaint"], new Vector3(0f, (CeilingY - SeatLayout.LampHeight) * 0.5f, 0f), Vector3.zero, false);
            var bulb = Part(lampRig, "PaperLantern", ctx.Mesh["KR_PaperLantern"], ctx.M["M_PaperLantern"], Vector3.zero, Vector3.zero, false);
            cord.isStatic = bulb.isStatic = false;

            var spot = new GameObject("Lamp Spot Light").AddComponent<Light>();
            spot.transform.SetParent(lampRig, false);
            spot.transform.localPosition = new Vector3(0f, -10.8f, 0f);
            spot.transform.localRotation = Quaternion.Euler(90, 0, 0);
            spot.type = LightType.Spot;
            spot.color = new Color(1f, 0.9f, 0.76f);
            spot.intensity = 300f;
            spot.range = 80f;
            spot.spotAngle = 126f;
            spot.innerSpotAngle = 64f;
            spot.shadows = LightShadows.Soft;
            spot.shadowStrength = 0.8f;
            spot.shadowBias = 0.02f;
            spot.shadowNormalBias = 0.3f;

            var glow = new GameObject("Bulb Glow").AddComponent<Light>();
            glow.transform.SetParent(lampRig, false);
            glow.transform.localPosition = new Vector3(0f, -5.2f, 0f);
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.86f, 0.66f);
            glow.intensity = 40f;
            glow.range = 70f;
            glow.shadows = LightShadows.None;

            // desk lamp + fairy lights (wired to the AtmosphereDirector as the room lights)
            var roomLightsRoot = new GameObject("Room Lights").transform;
            var desk = new GameObject("Desk Lamp Light").AddComponent<Light>();
            desk.transform.SetParent(roomLightsRoot, false);
            desk.transform.position = new Vector3(RoomHalf - 11f, SeatLayout.FloorY + 22f, -9f);
            desk.type = LightType.Point;
            desk.color = new Color(1f, 0.82f, 0.56f);
            desk.intensity = 22f;
            desk.range = 34f;
            desk.shadows = LightShadows.None;
            ctx.RoomLights.Add(desk);
            FairyLights(ctx, roomLightsRoot, new Vector3(-RoomHalf + 4f, CeilingY - 2.5f, RoomHalf - 1.2f), new Vector3(RoomHalf - 4f, CeilingY - 2.5f, RoomHalf - 1.2f), "N");
            FairyLights(ctx, roomLightsRoot, new Vector3(-RoomHalf + 1.2f, CeilingY - 2.5f, -RoomHalf + 4f), new Vector3(-RoomHalf + 1.2f, CeilingY - 2.5f, RoomHalf - 4f), "W");
            var night = new GameObject("Night Light").AddComponent<Light>();
            night.transform.SetParent(roomLightsRoot, false);
            night.transform.position = new Vector3(-RoomHalf + 24f, SeatLayout.FloorY + 4f, -12f);
            night.type = LightType.Point;
            night.color = new Color(0.6f, 0.75f, 1f);
            night.intensity = 6f;
            night.range = 22f;
            night.shadows = LightShadows.None;
            ctx.RoomLights.Add(night);

            // (no light shafts or dust here: they washed the view out when you looked towards the window)

            // moonlight (directional, cool)
            var sun = new GameObject("Moonlight").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(30, -90, 0);
            sun.color = new Color(0.62f, 0.72f, 1f);
            sun.intensity = 0.3f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.5f;

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
            sys.AddComponent<KidsRoomLife>();
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

            UseProbeLighting();

            EditorSceneManager.SaveScene(scene, ScenePath);
            CreateProbeBakingSet();

            var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != ScenePath && s.path != OldScenePath) list.Add(new EditorBuildSettingsScene(s.path, false));
            EditorBuildSettings.scenes = list.ToArray();
        }

        /// <summary>Open scene: every light baked, static renderers lit by probes, one global probe volume.</summary>
        static void UseProbeLighting()
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                l.lightmapBakeType = LightmapBakeType.Baked;
                l.shadows = LightShadows.Soft;
                EditorUtility.SetDirty(l);
            }
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!GameObjectUtility.AreStaticEditorFlagsSet(mr.gameObject, StaticEditorFlags.ContributeGI)) continue;
                mr.receiveGI = ReceiveGI.LightProbes;
                EditorUtility.SetDirty(mr);
            }
            if (Object.FindAnyObjectByType<ProbeVolume>() == null)
                new GameObject("Adaptive Probe Volume").AddComponent<ProbeVolume>().mode = ProbeVolume.Mode.Global;

            if (!Lightmapping.TryGetLightingSettings(out var lighting))
            {
                lighting = new LightingSettings { name = "TLB_Lighting" };
                AssetDatabase.CreateAsset(lighting, Scenes + "/TLB_Lighting.lighting");
                Lightmapping.lightingSettings = lighting;
            }
            lighting.bakedGI = true;
            lighting.realtimeGI = false;
            lighting.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            lighting.directionalityMode = LightmapsMode.NonDirectional;
            lighting.lightmapResolution = 2f;
            lighting.maxBounces = 3;
            EditorUtility.SetDirty(lighting);
        }

        /// <summary>
        /// Applies the desk lamp, the skinned hands and the baked APV lighting to the existing project without
        /// regenerating everything else. Opens the game scene, saves it and starts the bake.
        /// </summary>
        [MenuItem("Tools/The Last Block/Apply Hands, Lamp and Probe Lighting", priority = 2)]
        public static void ApplyHandsLampLighting()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[TheLastBlock] Leave Play Mode, then run Tools > The Last Block > Apply Hands, Lamp and Probe Lighting.");
                return;
            }
            if (!File.Exists(ScenePath)) { Generate(); return; }

            var ctx = new Ctx();
            SaveMesh(ctx, KidsRoomMeshes.DeskLamp(), "KR_DeskLamp");
            var hand = SaveHandMesh(ctx);
            var registry = AssetDatabase.LoadAssetAtPath<SecretJengaAssets>(Res + "/SecretJengaAssets.asset");
            if (hand != null && registry != null)
            {
                registry.handMesh = hand;
                EditorUtility.SetDirty(registry);
            }
            AddLightmapUVsToModels();
            EnableProbeVolumes();
            AssetDatabase.SaveAssets();

            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(ScenePath);
            }
            UseProbeLighting();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            CreateProbeBakingSet();
            AssetDatabase.SaveAssets();
            Debug.Log("[TheLastBlock] Applied the desk lamp, skinned hands and probe lighting. Bake with Tools > The Last Block > Bake Probe Lighting.");
        }

        /// <summary>
        /// Bakes the game scene's Adaptive Probe Volume. Never started automatically: a bake that still finds
        /// lightmapped renderers without lightmap UVs crashes the editor inside Unity's lightmap atlas packing.
        /// </summary>
        [MenuItem("Tools/The Last Block/Bake Probe Lighting", priority = 3)]
        public static void BakeProbeLighting()
        {
            if (!OpenGameSceneForBake()) return;
            Lightmapping.bakeCompleted -= SaveAfterBake;
            Lightmapping.bakeCompleted += SaveAfterBake;
            Lightmapping.BakeAsync();
        }

        /// <summary>The bake adds the probe volume's per-scene data object; without saving it the game loads no probes.</summary>
        static void SaveAfterBake()
        {
            Lightmapping.bakeCompleted -= SaveAfterBake;
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }

        /// <summary>Batch mode entry (-executeMethod), used to test a bake on a copy of the project.</summary>
        public static void BakeProbeLightingBatch()
        {
            bool ok = OpenGameSceneForBake() && Lightmapping.Bake();
            if (ok) SaveAfterBake();
            Debug.Log("[TheLastBlock] Batch probe bake " + (ok ? "succeeded" : "failed"));
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>Batch mode: rebuilds only Models/TLB_Hand.asset from TLB_Hand.json.</summary>
        public static void SaveHandMeshBatch()
        {
            var hand = SaveHandMesh(new Ctx());
            var registry = AssetDatabase.LoadAssetAtPath<SecretJengaAssets>(Res + "/SecretJengaAssets.asset");
            if (hand != null && registry != null) { registry.handMesh = hand; EditorUtility.SetDirty(registry); }
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(hand != null && hand.vertexCount > 0 ? 0 : 1);
        }

        /// <summary>Batch mode: the whole apply step followed by a bake (tested on a copy of the project).</summary>
        public static void ApplyAndBakeBatch()
        {
            ApplyHandsLampLighting();
            BakeProbeLightingBatch();
        }

        static bool OpenGameSceneForBake()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return false;
            if (SceneManager.GetActiveScene().path != ScenePath)
            {
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
                EditorSceneManager.OpenScene(ScenePath);
            }
            int lightmapped = 0;
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (mr.receiveGI == ReceiveGI.Lightmaps && GameObjectUtility.AreStaticEditorFlagsSet(mr.gameObject, StaticEditorFlags.ContributeGI)) lightmapped++;
            if (lightmapped > 0)
            {
                Debug.LogError($"[TheLastBlock] {lightmapped} static renderer(s) still use lightmaps; run Apply Hands, Lamp and Probe Lighting first. Bake skipped.");
                return false;
            }
            return true;
        }

        /// <summary>A single-scene APV baking set; probes every 10 cm near geometry (2 units), coarser in open air.</summary>
        static void CreateProbeBakingSet()
        {
            var path = Scenes + "/TLB_ProbeBakingSet.asset";
            if (AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(path) != null) AssetDatabase.DeleteAsset(path);
            var set = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>();
            // SetDefaults is internal, but a set without its default lighting scenario refuses to bake
            typeof(ProbeVolumeBakingSet).GetMethod("SetDefaults", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(set, null);
            set.minDistanceBetweenProbes = 2f;
            set.simplificationLevels = 3;
            if (!set.TryAddScene(AssetDatabase.AssetPathToGUID(ScenePath)))
                Debug.LogWarning("[TheLastBlock] The scene already belongs to another APV baking set; that set is used instead.");
            AssetDatabase.CreateAsset(set, path);
        }

        /// <summary>Switches every URP asset in use to Adaptive Probe Volumes.</summary>
        static void EnableProbeVolumes()
        {
            var assets = new HashSet<RenderPipelineAsset> { GraphicsSettings.defaultRenderPipeline };
            for (int i = 0; i < QualitySettings.count; i++) assets.Add(QualitySettings.GetRenderPipelineAssetAt(i));
            foreach (var a in assets)
            {
                if (!(a is UniversalRenderPipelineAsset)) continue;
                var so = new SerializedObject(a);
                SetProp(so, "m_LightProbeSystem", p => p.intValue = (int)UnityEngine.Rendering.Universal.LightProbeSystem.ProbeVolumes);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
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

    /// <summary>
    /// Runs Apply Hands, Lamp and Probe Lighting once after the next script reload when Library/TLB_ApplyPending
    /// exists (dropped there by tooling outside the editor). Claiming the marker with a move makes it run only once.
    /// </summary>
    [InitializeOnLoad]
    static class PendingApply
    {
        const string Marker = "Library/TLB_ApplyPending";

        static PendingApply() => EditorApplication.delayCall += Run;

        static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            var claimed = Marker + ".running";
            try { File.Move(Marker, claimed); }
            catch (IOException) { return; }
            try { SecretJengaProjectGenerator.ApplyHandsLampLighting(); }
            finally { File.Delete(claimed); }
        }
    }
}
