using System;
using System.Text;
using UnityEngine;

namespace SecretJenga
{
    /// <summary>Local, per-machine settings persisted in PlayerPrefs.</summary>
    public static class GameSettings
    {
        const string KName = "sj_player_name";
        const string KMaster = "sj_vol_master";
        const string KMusic = "sj_vol_music";
        const string KSfx = "sj_vol_sfx";
        const string KSens = "sj_mouse_sens";
        const string KFull = "sj_fullscreen";
        const string KQuality = "sj_quality";
        const string KUniqueProfile = "sj_unique_profile";
        const string KSolo = "sj_allow_solo";
        const string KProfile = "sj_auth_profile";
        const string KLastLobby = "sj_last_lobby";
        const string KAvatar = "sj_avatar_color";
        const string KDirect = "tlb_direct_mode";
        const string KAddress = "tlb_direct_address";
        const string KPort = "tlb_direct_port";
        const string KOfflineId = "tlb_offline_id_";

        public const ushort DefaultPort = 7777;

        public static event Action Changed;

        static string _sessionProfile;

        public static string PlayerName
        {
            get
            {
                var n = PlayerPrefs.GetString(KName, "");
                if (string.IsNullOrWhiteSpace(n))
                {
                    n = "Player" + UnityEngine.Random.Range(100, 999);
                    PlayerPrefs.SetString(KName, n);
                }
                return n;
            }
            set { PlayerPrefs.SetString(KName, SanitizeName(value)); Save(); }
        }

        public static int PreferredColor
        {
            get => PlayerPrefs.GetInt(KAvatar, -1);
            set { PlayerPrefs.SetInt(KAvatar, value); Save(); }
        }

        public static float MasterVolume { get => PlayerPrefs.GetFloat(KMaster, 0.8f); set { PlayerPrefs.SetFloat(KMaster, value); Save(); } }
        public static float MusicVolume { get => PlayerPrefs.GetFloat(KMusic, 0.5f); set { PlayerPrefs.SetFloat(KMusic, value); Save(); } }
        public static float SfxVolume { get => PlayerPrefs.GetFloat(KSfx, 0.9f); set { PlayerPrefs.SetFloat(KSfx, value); Save(); } }
        public static float MouseSensitivity { get => PlayerPrefs.GetFloat(KSens, 1f); set { PlayerPrefs.SetFloat(KSens, value); Save(); } }
        public static bool Fullscreen { get => PlayerPrefs.GetInt(KFull, 0) == 1; set { PlayerPrefs.SetInt(KFull, value ? 1 : 0); Save(); } }
        public static int Quality { get => PlayerPrefs.GetInt(KQuality, QualitySettings.GetQualityLevel()); set { PlayerPrefs.SetInt(KQuality, value); Save(); } }

        /// <summary>When true, every launch signs in with a fresh anonymous identity (lets you run 2+ builds on one PC).</summary>
        public static bool UniqueProfilePerLaunch { get => PlayerPrefs.GetInt(KUniqueProfile, 0) == 1; set { PlayerPrefs.SetInt(KUniqueProfile, value ? 1 : 0); Save(); } }

        /// <summary>Developer option: allow the host to start a match alone (for testing physics / UI).</summary>
        public static bool AllowSoloStart { get => PlayerPrefs.GetInt(KSolo, 0) == 1; set { PlayerPrefs.SetInt(KSolo, value ? 1 : 0); Save(); } }

        public static string LastLobbyId { get => PlayerPrefs.GetString(KLastLobby, ""); set { PlayerPrefs.SetString(KLastLobby, value ?? ""); PlayerPrefs.Save(); } }

        /// <summary>True = connect by IP address (LAN / port forwarding, no Unity services). False = Unity Relay + Lobby.</summary>
        public static bool UseDirectConnection { get => PlayerPrefs.GetInt(KDirect, 1) == 1; set { PlayerPrefs.SetInt(KDirect, value ? 1 : 0); Save(); } }
        public static string LastDirectAddress { get => PlayerPrefs.GetString(KAddress, "127.0.0.1"); set { PlayerPrefs.SetString(KAddress, string.IsNullOrWhiteSpace(value) ? "127.0.0.1" : value.Trim()); PlayerPrefs.Save(); } }
        public static ushort DirectPort
        {
            get => (ushort)Mathf.Clamp(PlayerPrefs.GetInt(KPort, DefaultPort), 1024, 65535);
            set { PlayerPrefs.SetInt(KPort, value); PlayerPrefs.Save(); }
        }

        /// <summary>
        /// Stable player identity used when Unity Authentication is not available (direct-IP sessions).
        /// Stored per auth profile so several local instances (MPPM virtual players, -sjprofile) stay distinct.
        /// </summary>
        public static string OfflinePlayerId
        {
            get
            {
                var key = KOfflineId + ResolveAuthProfile();
                var id = PlayerPrefs.GetString(key, "");
                if (string.IsNullOrEmpty(id))
                {
                    id = "local-" + Guid.NewGuid().ToString("N").Substring(0, 16);
                    PlayerPrefs.SetString(key, id);
                    PlayerPrefs.Save();
                }
                return id;
            }
        }

        static void Save()
        {
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static void ApplyDisplay()
        {
            var q = Mathf.Clamp(Quality, 0, QualitySettings.names.Length - 1);
            if (QualitySettings.GetQualityLevel() != q) QualitySettings.SetQualityLevel(q, true);
#if !UNITY_EDITOR
            Screen.fullScreenMode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
#endif
        }

        public static string SanitizeName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Player";
            raw = raw.Trim();
            if (raw.Length > 16) raw = raw.Substring(0, 16);
            return raw;
        }

        /// <summary>
        /// Authentication profile name. Each profile is a separate anonymous player identity on this machine.
        /// Priority: command line "-sjprofile NAME" > unique-per-launch option > editor/build default.
        /// </summary>
        public static string ResolveAuthProfile()
        {
            if (!string.IsNullOrEmpty(_sessionProfile)) return _sessionProfile;

            string fromArgs = null;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-sjprofile") fromArgs = args[i + 1];

            string p;
            if (!string.IsNullOrEmpty(fromArgs)) p = fromArgs;
            else if (UniqueProfilePerLaunch) p = "t" + Guid.NewGuid().ToString("N").Substring(0, 12);
            else
            {
#if UNITY_EDITOR
                p = "editor";
                // Multiplayer Play Mode virtual players run in separate folders; give them distinct identities.
                var dataPath = Application.dataPath.Replace('\\', '/');
                if (dataPath.Contains("/Library/VP/")) p = "vp" + Mathf.Abs(dataPath.GetHashCode() % 100000);
#else
                p = PlayerPrefs.GetString(KProfile, "");
                if (string.IsNullOrEmpty(p))
                {
                    p = "p" + Guid.NewGuid().ToString("N").Substring(0, 12);
                    PlayerPrefs.SetString(KProfile, p);
                    PlayerPrefs.Save();
                }
#endif
            }
            _sessionProfile = SanitizeProfile(p);
            return _sessionProfile;
        }

        static string SanitizeProfile(string p)
        {
            var sb = new StringBuilder();
            foreach (var c in p)
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
            var s = sb.ToString();
            if (s.Length == 0) s = "default";
            if (s.Length > 30) s = s.Substring(0, 30);
            return s;
        }
    }
}
