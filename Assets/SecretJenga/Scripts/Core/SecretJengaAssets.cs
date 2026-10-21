using System.Collections.Generic;
using SecretJenga.Game;
using UnityEngine;

namespace SecretJenga
{
    /// <summary>
    /// Central registry of generated assets. Created by Tools > The Last Block > Generate Project at
    /// Assets/SecretJenga/Resources/SecretJengaAssets.asset. Every consumer tolerates null entries
    /// (falls back to runtime-generated art / silence), so the game still runs if something is missing.
    /// </summary>
    [CreateAssetMenu(menuName = "The Last Block/Asset Registry", fileName = "SecretJengaAssets")]
    public class SecretJengaAssets : ScriptableObject
    {
        [Header("Network prefabs")]
        public GameObject blockPrefab;
        public GameObject sessionPrefab;

        [Header("Game content (data driven)")]
        [Tooltip("Secret personal tasks that can be dealt. Order must be identical on every build (indices are networked).")]
        public TaskDefinition[] tasks;
        [Tooltip("Special cards that can be dealt. Order must be identical on every build (indices are networked).")]
        public CardDefinition[] cards;

        [Header("Block visuals")]
        public Mesh blockMesh;
        public Material[] woodMaterials;
        public Material endGrainMaterial;
        public Material ghostMaterial;
        public Material cursorMaterial;
        public Material dustMaterial;

        [Header("Characters")]
        [Tooltip("Rigged saloon patron (Quaternius UAL mannequin, CC0) with cowboy hat. Optional - falls back to the procedural bodies.")]
        public GameObject cowboyModel;
        public RuntimeAnimatorController cowboyAnimator;
        public Material skinMaterial;
        public Material clothMaterial;
        public Material hoodMaterial;

        [Header("UI sprites")]
        public Sprite panel;
        public Sprite button;
        public Sprite rounded;
        public Sprite pill;
        public Sprite circle;
        public Sprite glow;
        public Sprite card;
        public Sprite vignette;
        public Sprite iconStar, iconCrown, iconClock, iconCheck, iconCross, iconCopy, iconGear, iconRefresh, iconPerson, iconSecret;

        [Header("UI - paper, ink and type")]
        [Tooltip("IM Fell English SC - headings.")] public Font fontTitle;
        [Tooltip("IM Fell English - body text, names.")] public Font fontBody;
        [Tooltip("Special Elite - typewritten data (timer, codes, reports).")] public Font fontType;
        public Sprite paper, paperSlip, cardStock, tape, inkStroke, tallyStroke, inkCircle, inkBlot, stamp;
        public Sprite iconCardSteady, iconCardPressure, iconCardSecond;

        [Header("Audio - blocks")]
        public AudioClip[] blockHits;
        public AudioClip blockPlace, blockSlideLoop, blockGrab, towerCollapse;
        [Tooltip("Wood creaking under strain; volume follows the tower wobble while a block is being moved.")]
        public AudioClip woodStrainLoop;

        [Header("Audio - room")]
        public AudioClip music;                 // low drone bed
        public AudioClip lampHumLoop;
        public AudioClip flicker;
        public AudioClip heartbeatLoop;
        public AudioClip collapseSting;
        public AudioClip[] chairCreaks;
        public AudioClip[] distantMetal;

        [Header("Audio - UI / round")]
        public AudioClip uiHover, uiClick, missionComplete, turnStart, timerTick, win, lose, countdown, error, cardPlayed;

        static SecretJengaAssets _instance;

        public static SecretJengaAssets Instance
        {
            get
            {
                if (_instance == null) _instance = Resources.Load<SecretJengaAssets>("SecretJengaAssets");
                if (_instance == null)
                {
                    Debug.LogWarning("[TheLastBlock] Asset registry not found. Run Tools > The Last Block > Generate Project. Using runtime fallbacks.");
                    _instance = CreateInstance<SecretJengaAssets>();
                }
                return _instance;
            }
            set => _instance = value;
        }
    }

    /// <summary>Resolves the task/card catalogs (registry assets, or the built-in defaults when none are assigned).</summary>
    public static class GameContent
    {
        static List<TaskDefinition> _tasks;
        static List<CardDefinition> _cards;

        public static IReadOnlyList<TaskDefinition> Tasks
        {
            get
            {
                if (_tasks == null)
                {
                    _tasks = new List<TaskDefinition>();
                    var reg = SecretJengaAssets.Instance.tasks;
                    if (reg != null) foreach (var t in reg) if (t != null) _tasks.Add(t);
                    if (_tasks.Count == 0) _tasks = TaskDefinition.CreateDefaults();
                }
                return _tasks;
            }
        }

        public static IReadOnlyList<CardDefinition> Cards
        {
            get
            {
                if (_cards == null)
                {
                    _cards = new List<CardDefinition>();
                    var reg = SecretJengaAssets.Instance.cards;
                    if (reg != null) foreach (var c in reg) if (c != null) _cards.Add(c);
                    if (_cards.Count == 0) _cards = CardDefinition.CreateDefaults();
                }
                return _cards;
            }
        }

        public static TaskDefinition Task(int index) => index >= 0 && index < Tasks.Count ? Tasks[index] : null;
        public static CardDefinition Card(int index) => index >= 0 && index < Cards.Count ? Cards[index] : null;

        public static int FirstCardOfKind(CardKind kind)
        {
            for (int i = 0; i < Cards.Count; i++) if (Cards[i].kind == kind && Cards[i].inPool) return i;
            for (int i = 0; i < Cards.Count; i++) if (Cards[i].kind == kind) return i;
            return -1;
        }

        public static string TaskTitle(Networking.TaskInfo t) => Task(t.DefinitionIndex)?.Title ?? L.T("No task", "Görev yok");
        public static string CardTitle(Networking.CardInfo c) => c.Kind == CardKind.None ? L.T("No card", "Kart yok") : (Card(c.DefinitionIndex)?.Title ?? c.Kind.ToString());

        /// <summary>Clears the cache (the registry can change in the editor between play sessions).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() { _tasks = null; _cards = null; }
    }
}
