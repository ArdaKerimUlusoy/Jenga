using UnityEngine;

namespace SecretJenga
{
    /// <summary>
    /// Central registry of generated assets. Created by Tools > Secret Jenga > Generate Project at
    /// Assets/SecretJenga/Resources/SecretJengaAssets.asset. Every consumer tolerates null entries
    /// (falls back to runtime-generated art / silence), so the game still runs if something is missing.
    /// </summary>
    [CreateAssetMenu(menuName = "Secret Jenga/Asset Registry", fileName = "SecretJengaAssets")]
    public class SecretJengaAssets : ScriptableObject
    {
        [Header("Network prefabs")]
        public GameObject blockPrefab;
        public GameObject sessionPrefab;

        [Header("Block visuals")]
        public Mesh blockMesh;
        public Material[] woodMaterials;
        public Material endGrainMaterial;
        public Material ghostMaterial;
        public Material cursorMaterial;
        public Material dustMaterial;

        [Header("UI sprites")]
        public Sprite panel;
        public Sprite button;
        public Sprite rounded;
        public Sprite pill;
        public Sprite circle;
        public Sprite glow;
        public Sprite card;
        public Sprite vignette;
        [Tooltip("Indexed by MissionType (0 = None).")]
        public Sprite[] missionIcons = new Sprite[8];
        public Sprite iconStar, iconCrown, iconClock, iconCheck, iconCross, iconCopy, iconGear, iconRefresh, iconPerson, iconSecret;

        [Header("Audio")]
        public AudioClip[] blockHits;
        public AudioClip blockPlace, blockSlideLoop, blockGrab, towerCollapse;
        public AudioClip uiHover, uiClick, missionComplete, turnStart, timerTick, win, lose, countdown, error;
        public AudioClip music;

        static SecretJengaAssets _instance;

        public static SecretJengaAssets Instance
        {
            get
            {
                if (_instance == null) _instance = Resources.Load<SecretJengaAssets>("SecretJengaAssets");
                if (_instance == null)
                {
                    Debug.LogWarning("[SecretJenga] SecretJengaAssets registry not found. Run Tools > Secret Jenga > Generate Project. Using runtime fallbacks.");
                    _instance = CreateInstance<SecretJengaAssets>();
                }
                return _instance;
            }
            set => _instance = value;
        }

        public Sprite MissionIcon(MissionType t)
        {
            int i = (int)t;
            if (missionIcons != null && i >= 0 && i < missionIcons.Length && missionIcons[i] != null) return missionIcons[i];
            return iconStar != null ? iconStar : RuntimeArt.Circle;
        }
    }
}
