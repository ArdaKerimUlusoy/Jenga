using System.Collections.Generic;
using SecretJenga.Game;
using SecretJenga.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace SecretJenga.UI
{
    /// <summary>
    /// Builds and drives every screen at runtime (uGUI): splash, main menu, online menus, room browser, lobby,
    /// HUD, pause, settings, credits, results, modal dialogs, loading overlay, toasts and banners.
    /// Screens follow the session state and the replicated game phase automatically.
    /// </summary>
    public partial class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        /// <summary>True when an overlay should swallow game input (mouse picking / camera).</summary>
        public static bool BlocksGameInput =>
            Instance != null && (Instance._pauseOpen || Instance._modalOpen || Instance._settingsOpen || Instance._loadingOpen || Instance._current == View.Results);

        enum View { None, Splash, Main, Online, Create, Join, Browser, Lobby, Hud, Results, Credits }

        RectTransform _root;
        readonly Dictionary<View, CanvasGroup> _screens = new Dictionary<View, CanvasGroup>();
        View _current = View.None;
        GamePhase _lastPhase = (GamePhase)255;
        NetworkGameManager _game;

        CanvasGroup _pauseGroup, _settingsGroup, _modalGroup, _loadingGroup, _flashGroup;
        bool _pauseOpen, _settingsOpen, _modalOpen, _loadingOpen;
        Text _modalTitle, _modalText, _loadingText;
        RectTransform _spinner, _toastRoot;
        Image _flashImage;
        System.Action _modalOk;

        static SecretJengaAssets A => SecretJengaAssets.Instance;

        // ------------------------------------------------------------------ Lifecycle
        void Awake()
        {
            Instance = this;
            EnsureEventSystem();
            BuildCanvas();
            BuildSplash();
            BuildMainMenu();
            BuildOnlineMenu();
            BuildCreateRoom();
            BuildJoinCode();
            BuildBrowser();
            BuildCredits();
            BuildLobby();
            BuildHud();
            BuildResults();
            BuildPause();
            BuildSettings();
            BuildModal();
            BuildLoading();
            BuildToasts();
            foreach (var g in _screens.Values) { g.alpha = 0; g.gameObject.SetActive(false); }

            NetworkGameManager.Spawned += OnGameSpawned;
            NetworkGameManager.Despawned += OnGameDespawned;
            NetworkGameManager.Notification += OnNotification;
            NetworkGameManager.MatchEnded += OnMatchEnded;
            TurnManager.TurnStarted += OnTurnStarted;
            TurnManager.PrivateMessage += OnPrivateMessage;
            SecretMissionManager.LocalMissionChanged += OnMissionChanged;
            SecretMissionManager.ResultsRevealed += OnResultsRevealed;
        }

        void Start()
        {
            var s = NetworkSessionManager.Instance;
            if (s != null)
            {
                s.StatusChanged += ShowLoading;
                s.SessionStarted += OnSessionStarted;
                s.SessionEnded += OnSessionEnded;
            }
            ShowScreen(View.Splash);
            UITween.Delay(2.4f, () => { if (_current == View.Splash) ShowScreen(View.Main); });
        }

        void OnDestroy()
        {
            NetworkGameManager.Spawned -= OnGameSpawned;
            NetworkGameManager.Despawned -= OnGameDespawned;
            NetworkGameManager.Notification -= OnNotification;
            NetworkGameManager.MatchEnded -= OnMatchEnded;
            TurnManager.TurnStarted -= OnTurnStarted;
            TurnManager.PrivateMessage -= OnPrivateMessage;
            SecretMissionManager.LocalMissionChanged -= OnMissionChanged;
            SecretMissionManager.ResultsRevealed -= OnResultsRevealed;
            if (_game != null) _game.StateChanged -= OnGameStateChanged;
            var s = NetworkSessionManager.Instance;
            if (s != null)
            {
                s.StatusChanged -= ShowLoading;
                s.SessionStarted -= OnSessionStarted;
                s.SessionEnded -= OnSessionEnded;
            }
            if (Instance == this) Instance = null;
        }

        static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(go);
        }

        void BuildCanvas()
        {
            var go = new GameObject("SecretJenga UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _root = (RectTransform)go.transform;

            // cinematic vignette over the 3D scene
            var vig = UIFactory.Image(_root, "Vignette", A.vignette, new Color(0, 0, 0, 0.75f), false);
            UIFactory.Stretch(vig.rectTransform);
            if (A.vignette == null) vig.enabled = false;
        }

        CanvasGroup NewScreen(View id, string name)
        {
            var rt = UIFactory.Rect(name, _root);
            UIFactory.Stretch(rt);
            var g = rt.gameObject.AddComponent<CanvasGroup>();
            _screens[id] = g;
            return g;
        }

        CanvasGroup NewOverlay(string name, float dim = 0.6f)
        {
            var rt = UIFactory.Rect(name, _root);
            UIFactory.Stretch(rt);
            var g = rt.gameObject.AddComponent<CanvasGroup>();
            var bg = UIFactory.Image(rt, "Dim", null, new Color(0.05f, 0.03f, 0.02f, dim));
            bg.raycastTarget = true;
            UIFactory.Stretch(bg.rectTransform);
            g.alpha = 0;
            rt.gameObject.SetActive(false);
            return g;
        }

        void ShowScreen(View s)
        {
            if (_current == s) return;
            if (_screens.TryGetValue(_current, out var old))
            {
                var o = old;
                o.interactable = false;
                UITween.Fade(o, 0f, 0.18f, () => { if (o != null && o.alpha <= 0.01f) o.gameObject.SetActive(false); });
            }
            _current = s;
            if (_screens.TryGetValue(s, out var g))
            {
                g.gameObject.SetActive(true);
                g.transform.SetAsLastSibling();
                g.interactable = true;
                UITween.Fade(g, 1f, 0.25f);
                var card = g.transform.childCount > 0 ? g.transform.GetChild(0) : null;
                if (card != null && s != View.Hud) UITween.Scale(card, Vector3.one * 0.96f, Vector3.one, 0.3f);
                OnScreenShown(s);
            }
            KeepOverlaysOnTop();
        }

        void KeepOverlaysOnTop()
        {
            _pauseGroup?.transform.SetAsLastSibling();
            _settingsGroup?.transform.SetAsLastSibling();
            _modalGroup?.transform.SetAsLastSibling();
            _loadingGroup?.transform.SetAsLastSibling();
            _toastRoot?.SetAsLastSibling();
            _flashGroup?.transform.SetAsLastSibling();
        }

        void OnScreenShown(View s)
        {
            switch (s)
            {
                case View.Main: RefreshMainMenu(); break;
                case View.Browser: RefreshBrowser(); break;
                case View.Lobby: RefreshLobby(); break;
                case View.Results: RefreshResults(); break;
            }
        }

        static void SetOverlay(CanvasGroup g, bool open)
        {
            if (g == null) return;
            if (open)
            {
                g.gameObject.SetActive(true);
                g.interactable = true;
                g.blocksRaycasts = true;
                UITween.Fade(g, 1f, 0.18f);
                if (g.transform.childCount > 1) UITween.Scale(g.transform.GetChild(1), Vector3.one * 0.94f, Vector3.one, 0.25f);
            }
            else
            {
                g.interactable = false;
                g.blocksRaycasts = false;
                UITween.Fade(g, 0f, 0.15f, () => { if (g != null && g.alpha <= 0.01f) g.gameObject.SetActive(false); });
            }
        }

        // ------------------------------------------------------------------ Update / input
        void Update()
        {
            if (_loadingOpen && _spinner != null) _spinner.Rotate(0, 0, -240f * Time.unscaledDeltaTime);
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) OnEscape();
            if (_current == View.Hud) UpdateHud();
            if (_current == View.Lobby) UpdateLobbyLive();
            UpdateToasts();
        }

        void OnEscape()
        {
            if (_loadingOpen) return;
            if (_modalOpen) { CloseModal(); return; }
            if (_settingsOpen) { CloseSettings(); return; }
            switch (_current)
            {
                case View.Hud: TogglePause(); break;
                case View.Online: ShowScreen(View.Main); break;
                case View.Create:
                case View.Join:
                case View.Browser: ShowScreen(View.Online); break;
                case View.Credits: ShowScreen(View.Main); break;
                case View.Splash: ShowScreen(View.Main); break;
            }
        }

        // ------------------------------------------------------------------ Session + game events
        void OnSessionStarted() => HideLoading();

        void OnSessionEnded(string reason)
        {
            HideLoading();
            _pauseOpen = false;
            SetOverlay(_pauseGroup, false);
            _lastPhase = (GamePhase)255;
            ShowScreen(View.Main);
            if (!string.IsNullOrEmpty(reason)) ShowModal("Notice", reason);
        }

        void OnGameSpawned(NetworkGameManager g)
        {
            if (_game != null) _game.StateChanged -= OnGameStateChanged;
            _game = g;
            _game.StateChanged += OnGameStateChanged;
            _lastPhase = (GamePhase)255;
            OnGameStateChanged();
        }

        void OnGameDespawned()
        {
            if (_game != null) _game.StateChanged -= OnGameStateChanged;
            _game = null;
        }

        void OnGameStateChanged()
        {
            if (_game == null) return;
            var phase = _game.Phase.Value;
            if (phase != _lastPhase)
            {
                var prev = _lastPhase;
                _lastPhase = phase;
                switch (phase)
                {
                    case GamePhase.Lobby: ShowScreen(View.Lobby); break;
                    case GamePhase.Countdown:
                    case GamePhase.Playing:
                        if (prev != GamePhase.Countdown && prev != GamePhase.Playing) ResetHud();
                        ShowScreen(View.Hud);
                        break;
                    case GamePhase.GameOver:
                        UITween.Delay(1.8f, () => { if (_game != null && _game.Phase.Value == GamePhase.GameOver) { ClosePauseIfOpen(); ShowScreen(View.Results); } });
                        break;
                }
            }
            if (_current == View.Lobby) RefreshLobby();
            if (_current == View.Results) RefreshResults();
        }

        void OnNotification(string msg) => Toast(msg, Palette.Cream);

        void OnPrivateMessage(string msg) => Toast(msg, Palette.Bad);

        void OnMatchEnded(bool won)
        {
            AudioManager.Instance?.Play(won ? AudioManager.Sfx.Win : AudioManager.Sfx.Lose);
            Flash(won ? new Color(1f, 0.85f, 0.4f, 0.35f) : new Color(0.8f, 0.1f, 0.05f, 0.45f));
        }

        // ------------------------------------------------------------------ Modal
        void BuildModal()
        {
            _modalGroup = NewOverlay("Modal", 0.65f);
            var card = UIFactory.Card(_modalGroup.transform, "ModalCard");
            UIFactory.Place((RectTransform)card.transform.parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 420));
            var col = UIFactory.Rect("Col", card.transform);
            UIFactory.Stretch(col);
            UIFactory.VLayout(col, 18, 40);
            _modalTitle = UIFactory.Title(col, "Notice", 44);
            _modalText = UIFactory.Text(col, "", 30, Palette.Paper);
            UIFactory.LE(_modalText, 170);
            var ok = UIFactory.Button(col, "OK", () => { var a = _modalOk; CloseModal(); a?.Invoke(); });
            UIFactory.LE(ok, 80);
        }

        public void ShowModal(string title, string message, System.Action onOk = null)
        {
            _modalTitle.text = title;
            _modalText.text = message;
            _modalOk = onOk;
            _modalOpen = true;
            SetOverlay(_modalGroup, true);
            KeepOverlaysOnTop();
            AudioManager.Instance?.Play(AudioManager.Sfx.Error, 0.6f);
        }

        void CloseModal()
        {
            _modalOpen = false;
            SetOverlay(_modalGroup, false);
        }

        // ------------------------------------------------------------------ Loading
        void BuildLoading()
        {
            _loadingGroup = NewOverlay("Loading", 0.7f);
            var spin = UIFactory.Image(_loadingGroup.transform, "Spinner", A.iconRefresh != null ? A.iconRefresh : UIFactory.CircleSprite, Palette.Accent, false);
            _spinner = spin.rectTransform;
            UIFactory.Place(_spinner, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 50), new Vector2(110, 110));
            _loadingText = UIFactory.Text(_loadingGroup.transform, "Loading...", 34, Palette.Cream);
            UIFactory.Place(_loadingText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(1000, 60));
        }

        public void ShowLoading(string text)
        {
            _loadingText.text = text;
            if (_loadingOpen) return;
            _loadingOpen = true;
            SetOverlay(_loadingGroup, true);
            KeepOverlaysOnTop();
        }

        public void HideLoading()
        {
            if (!_loadingOpen) return;
            _loadingOpen = false;
            SetOverlay(_loadingGroup, false);
        }

        // ------------------------------------------------------------------ Toasts + flash
        class ToastItem { public CanvasGroup Group; public float Life; }
        readonly List<ToastItem> _toasts = new List<ToastItem>();

        void BuildToasts()
        {
            _toastRoot = UIFactory.Rect("Toasts", _root);
            UIFactory.Place(_toastRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -150), new Vector2(900, 400));
            var v = UIFactory.VLayout(_toastRoot, 10, 0, TextAnchor.UpperCenter, false);
            v.childControlWidth = false;
            v.childControlHeight = false;

            var flash = UIFactory.Rect("Flash", _root);
            UIFactory.Stretch(flash);
            _flashGroup = flash.gameObject.AddComponent<CanvasGroup>();
            _flashGroup.blocksRaycasts = false;
            _flashGroup.interactable = false;
            _flashGroup.alpha = 0;
            _flashImage = UIFactory.Image(flash, "FlashImg", null, Color.white);
            UIFactory.Stretch(_flashImage.rectTransform);
        }

        public void Toast(string message, Color accent)
        {
            if (string.IsNullOrEmpty(message)) return;
            var pill = UIFactory.Image(_toastRoot, "Toast", UIFactory.PillSprite, new Color(0.12f, 0.07f, 0.04f, 0.92f));
            var h = UIFactory.HLayout(pill, 14, 14, TextAnchor.MiddleCenter, false);
            h.padding = new RectOffset(30, 30, 12, 12);
            var dot = UIFactory.Image(pill.transform, "Dot", UIFactory.CircleSprite, accent, false);
            UIFactory.LE(dot, 18, 18);
            var t = UIFactory.Text(pill.transform, message, 28, Palette.Cream);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            var fitter = pill.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UIFactory.LE(t, -1, Mathf.Min(760, 14 * message.Length + 40));
            var g = pill.gameObject.AddComponent<CanvasGroup>();
            g.blocksRaycasts = false;
            g.alpha = 0;
            UITween.Fade(g, 1f, 0.2f);
            _toasts.Add(new ToastItem { Group = g, Life = 3.6f });
            while (_toasts.Count > 4) { Destroy(_toasts[0].Group.gameObject); _toasts.RemoveAt(0); }
        }

        void UpdateToasts()
        {
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var t = _toasts[i];
                t.Life -= Time.unscaledDeltaTime;
                if (t.Group == null) { _toasts.RemoveAt(i); continue; }
                if (t.Life < 0.4f) t.Group.alpha = Mathf.Clamp01(t.Life / 0.4f);
                if (t.Life <= 0f) { Destroy(t.Group.gameObject); _toasts.RemoveAt(i); }
            }
        }

        public void Flash(Color c)
        {
            _flashImage.color = c;
            _flashGroup.alpha = 1f;
            UITween.Fade(_flashGroup, 0f, 0.9f);
        }
    }
}
