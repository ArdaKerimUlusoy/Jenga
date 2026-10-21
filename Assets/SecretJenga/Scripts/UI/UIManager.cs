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
    /// Builds and drives every screen at runtime (uGUI): splash, main menu, host / join screens, room browser, lobby,
    /// HUD (turns, objective, card, secret task), pause, settings, credits, round results, dialogs, loading overlay,
    /// toasts and banners. The UI only displays replicated state and sends requests - no game rules live here.
    /// Screens follow the session state and the replicated game phase automatically.
    /// </summary>
    public partial class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        /// <summary>True when an overlay should swallow game input (mouse picking / camera).</summary>
        public static bool BlocksGameInput =>
            Instance != null && (Instance._pauseOpen || Instance._modalOpen || Instance._settingsOpen || Instance._loadingOpen || Instance._current == View.Results);

        enum View { None, Splash, Main, Host, Join, Browser, Lobby, Hud, Results, Credits }

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
        bool _rebuildPending;

        void Awake()
        {
            Instance = this;
            EnsureEventSystem();
            BuildAll();
            L.Changed += OnLanguageChanged;

            NetworkGameManager.Spawned += OnGameSpawned;
            NetworkGameManager.Despawned += OnGameDespawned;
            NetworkGameManager.Notification += OnNotification;
            NetworkGameManager.MatchEnded += OnMatchEnded;
            TurnManager.TurnStarted += OnTurnStarted;
            TurnManager.PrivateMessage += OnPrivateMessage;
            SecretTaskManager.LocalTaskChanged += OnTaskChanged;
            RoundResults.Revealed += OnResultsRevealed;
            CardManager.LocalCardChanged += OnCardChanged;
            CardManager.CardPlayed += OnCardPlayed;
        }

        void BuildAll()
        {
            BuildCanvas();
            BuildSplash();
            BuildMainMenu();
            BuildHostScreen();
            BuildJoinScreen();
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
        }

        void OnLanguageChanged() => _rebuildPending = true;

        /// <summary>Every screen is written in code, so a new language simply means writing them again.</summary>
        void RebuildForLanguage()
        {
            _rebuildPending = false;
            var view = _current;
            bool settings = _settingsOpen, pause = _pauseOpen;
            if (_root != null) Destroy(_root.gameObject);
            _screens.Clear();
            _slots.Clear();
            _swatches.Clear();
            _hudRows.Clear();
            _tallies.Clear();
            _toasts.Clear();
            _hudRosterKey = "";
            _tallyRequired = _tallyDone = -1;
            _settingsOpen = _pauseOpen = _modalOpen = _loadingOpen = false;
            _current = View.None;
            BuildAll();
            ShowScreen(view == View.None ? View.Main : view);
            if (view == View.Hud) { OnTaskChanged(SecretTaskManager.LocalTask, false); OnCardChanged(CardManager.LocalCard); }
            if (pause) TogglePause();
            if (settings) OpenSettings();
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
            SecretTaskManager.LocalTaskChanged -= OnTaskChanged;
            RoundResults.Revealed -= OnResultsRevealed;
            CardManager.LocalCardChanged -= OnCardChanged;
            CardManager.CardPlayed -= OnCardPlayed;
            if (_game != null) _game.StateChanged -= OnGameStateChanged;
            var s = NetworkSessionManager.Instance;
            if (s != null)
            {
                s.StatusChanged -= ShowLoading;
                s.SessionStarted -= OnSessionStarted;
                s.SessionEnded -= OnSessionEnded;
            }
            L.Changed -= OnLanguageChanged;
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
            var go = new GameObject("TheLastBlock UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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
            var vig = UIFactory.Image(_root, "Vignette", A.vignette, new Color(0, 0, 0, 0.85f), false);
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
            var bg = UIFactory.Image(rt, "Dim", null, new Color(0.01f, 0.01f, 0.01f, dim));
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
                // sheets are laid down on the table: a short slide, no bounce
                if (s != View.Hud)
                    foreach (Transform child in g.transform)
                        if (child.name.StartsWith("Sheet")) UITween.SlideIn((RectTransform)child, new Vector2(0, -26), 0.32f);
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
                if (g.transform.childCount > 1) UITween.SlideIn((RectTransform)g.transform.GetChild(1), new Vector2(0, -22), 0.28f);
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
            if (_rebuildPending) RebuildForLanguage();
            if (_loadingOpen && _spinner != null)
            {
                // the ink ring breathes instead of spinning like a web page
                float k = Mathf.Sin(Time.unscaledTime * 2.2f);
                _spinner.localRotation = Quaternion.Euler(0, 0, k * 3f);
                _spinner.localScale = Vector3.one * (1f + k * 0.03f);
            }
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) OnEscape();
            if (_current == View.Hud)
            {
                // W/A/S/D lean the head; don't let them also navigate between still-selected HUD buttons.
                var es = EventSystem.current;
                if (es != null && es.currentSelectedGameObject != null && !_pauseOpen && !_settingsOpen && !_modalOpen &&
                    es.currentSelectedGameObject.GetComponent<InputField>() == null)
                    es.SetSelectedGameObject(null);
                UpdateHud();
            }
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
                case View.Host:
                case View.Join: ShowScreen(View.Main); break;
                case View.Browser: ShowScreen(View.Join); break;
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
            _pendingBots = 0;
            ShowScreen(View.Main);
            if (!string.IsNullOrEmpty(reason)) ShowModal(L.T("Notice", "Bilgi"), reason);
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
                    case GamePhase.Lobby: SeatPendingBots(); ShowScreen(View.Lobby); break;
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
            Flash(won ? new Color(0.95f, 0.8f, 0.5f, 0.18f) : new Color(0.5f, 0.02f, 0.01f, 0.55f));
        }

        // ------------------------------------------------------------------ Modal
        void BuildModal()
        {
            _modalGroup = NewOverlay("Modal", 0.6f);
            var sheet = UIFactory.PaperSheet(_modalGroup.transform, "SheetModal", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(780, 440), 1.1f, 1, 60);
            _modalTitle = UIFactory.Title(sheet.Body, L.T("Notice", "Bilgi"), 46);
            UIFactory.Divider(sheet.Body);
            _modalText = UIFactory.Write(sheet.Body, FontKind.Type, "", 25, Ink.Black, TextAnchor.UpperLeft);
            UIFactory.LE(_modalText, 170);
            var row = UIFactory.ButtonRow(sheet.Body, 64, 40, TextAnchor.MiddleRight);
            UIFactory.Button(row, L.T("Understood", "Anladım"), () => { var a = _modalOk; CloseModal(); a?.Invoke(); }, ButtonStyle.Primary, 34);
            UIFactory.OnPaper = false;
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
            _loadingGroup = NewOverlay("Loading", 0.72f);
            UIFactory.OnPaper = false;
            var ring = UIFactory.Image(_loadingGroup.transform, "InkRing", A.inkCircle != null ? A.inkCircle : UIFactory.CircleSprite, Ink.RedBright, false);
            _spinner = ring.rectTransform;
            UIFactory.Place(_spinner, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(560, 150));
            _loadingText = UIFactory.Write(_loadingGroup.transform, FontKind.Type, "...", 30, Ink.Bone, TextAnchor.MiddleCenter);
            UIFactory.Place(_loadingText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(1100, 60));
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
            UIFactory.Place(_toastRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -200), new Vector2(1000, 420));
            var v = UIFactory.VLayout(_toastRoot, 6, 0, TextAnchor.UpperCenter, false);
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

        /// <summary>A typed slip of paper that slides in, stays a moment and fades.</summary>
        public void Toast(string message, Color accent)
        {
            if (string.IsNullOrEmpty(message) || _toastRoot == null) return;
            bool prev = UIFactory.OnPaper;
            UIFactory.OnPaper = true;
            var holder = UIFactory.Rect("Toast", _toastRoot);
            var slip = UIFactory.Image(holder, "Slip", UIFactory.SlipSprite, Color.white);
            UIFactory.Stretch(slip.rectTransform);
            var mark = UIFactory.Image(holder, "Mark", UIFactory.BlotSprite, Bright(accent), false);
            UIFactory.Place(mark.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(36, 0), new Vector2(20, 20));
            var t = UIFactory.Write(holder, FontKind.Type, message, 22, Ink.Black, TextAnchor.MiddleLeft);
            float w = Mathf.Clamp(t.preferredWidth + 100f, 300f, 900f);
            t.rectTransform.sizeDelta = new Vector2(w - 100f, 0);
            float h = Mathf.Max(64f, t.cachedTextGeneratorForLayout.GetPreferredHeight(message, t.GetGenerationSettings(new Vector2(w - 100f, 0))) / t.pixelsPerUnit + 34f);
            holder.sizeDelta = new Vector2(w, h);
            UIFactory.Stretch(t.rectTransform, 62, 0, 38, 2);
            holder.localRotation = Quaternion.Euler(0, 0, Random.Range(-1.4f, 1.4f));
            UIFactory.OnPaper = prev;
            var g = holder.gameObject.AddComponent<CanvasGroup>();
            g.blocksRaycasts = false;
            g.alpha = 0;
            UITween.Fade(g, 1f, 0.2f);
            _toasts.Add(new ToastItem { Group = g, Life = 4.4f });
            while (_toasts.Count > 4) { Destroy(_toasts[0].Group.gameObject); _toasts.RemoveAt(0); }
        }

        /// <summary>Accent colours are designed for the dark room; on paper the ink must be darker.</summary>
        static Color Bright(Color c) => new Color(c.r * 0.8f, c.g * 0.8f, c.b * 0.8f, 1f);

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
