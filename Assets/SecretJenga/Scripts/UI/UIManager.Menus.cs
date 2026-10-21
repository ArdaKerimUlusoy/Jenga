using System.Collections.Generic;
using SecretJenga.Networking;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

namespace SecretJenga.UI
{
    public partial class UIManager
    {
        InputField _nameInput, _settingsNameInput, _roomNameInput, _codeInput;
        Button _rejoinButton;
        Text _footerText, _browserStatus;
        RectTransform _browserList;
        bool _browserBusy;
        FactoryRefs _create;

        class FactoryRefs
        {
            public UIFactory.StepperRef MaxPlayers, Target, StartLayers, TurnTime;
            public Toggle Private;
        }

        RectTransform CenterCard(CanvasGroup screen, string name, Vector2 size, Vector2 offset = default)
        {
            var card = UIFactory.Card(screen.transform, name);
            UIFactory.Place((RectTransform)card.transform.parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), offset, size);
            var col = UIFactory.Rect("Col", card.transform);
            UIFactory.Stretch(col);
            UIFactory.VLayout(col, 16, 44);
            return col;
        }

        RectTransform LeftCard(CanvasGroup screen, string name, Vector2 size)
        {
            var card = UIFactory.Card(screen.transform, name);
            UIFactory.Place((RectTransform)card.transform.parent, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(130, 0), size);
            var col = UIFactory.Rect("Col", card.transform);
            UIFactory.Stretch(col);
            UIFactory.VLayout(col, 18, 48);
            return col;
        }

        void Logo(Transform parent, float scale = 1f)
        {
            var secret = UIFactory.Text(parent, "S E C R E T", Mathf.RoundToInt(34 * scale), Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.LE(secret, 44 * scale);
            var jenga = UIFactory.Text(parent, "JENGA", Mathf.RoundToInt(120 * scale), Palette.Cream, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.AddShadow(jenga, new Color(0.4f, 0.2f, 0.05f, 0.9f), new Vector2(0, -6));
            UIFactory.AddShadow(jenga, new Color(0, 0, 0, 0.5f), new Vector2(4, -10));
            UIFactory.LE(jenga, 130 * scale);
            var tag = UIFactory.Text(parent, "One tower. Shared goal. Secret agendas.", Mathf.RoundToInt(26 * scale), Palette.Paper, TextAnchor.MiddleCenter, FontStyle.Italic);
            UIFactory.LE(tag, 40 * scale);
        }

        // ------------------------------------------------------------------ Splash
        void BuildSplash()
        {
            var g = NewScreen(View.Splash, "Splash");
            var bg = UIFactory.Image(g.transform, "Bg", null, new Color(0.07f, 0.04f, 0.02f, 0.92f));
            bg.raycastTarget = true;
            UIFactory.Stretch(bg.rectTransform);
            var col = UIFactory.Rect("Logo", g.transform);
            UIFactory.Place(col, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(1200, 360));
            UIFactory.VLayout(col, 6, 0);
            Logo(col, 1.3f);
            var hint = UIFactory.Text(g.transform, "click to continue", 24, new Color(1, 1, 1, 0.4f));
            UIFactory.Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 60), new Vector2(600, 40));
            var btn = bg.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => ShowScreen(View.Main));
        }

        // ------------------------------------------------------------------ Main menu
        void BuildMainMenu()
        {
            var g = NewScreen(View.Main, "MainMenu");
            var col = LeftCard(g, "MainCard", new Vector2(620, 900));
            Logo(col, 0.85f);
            UIFactory.Divider(col);

            var nameRow = UIFactory.Rect("NameRow", col);
            UIFactory.LE(nameRow, 70);
            UIFactory.HLayout(nameRow, 14, 0, TextAnchor.MiddleLeft, false);
            var nl = UIFactory.Text(nameRow, "Name", 28, Palette.Paper, TextAnchor.MiddleLeft);
            UIFactory.LE(nl, -1, 100);
            _nameInput = UIFactory.Input(nameRow, "Your name", GameSettings.PlayerName, 16);
            UIFactory.LE(_nameInput, 64, -1, 1);
            _nameInput.onEndEdit.AddListener(v => GameSettings.PlayerName = v);

            UIFactory.LE(UIFactory.Button(col, "Play Online", OnPlayOnline, ButtonStyle.Primary, 36), 92);
            _rejoinButton = UIFactory.Button(col, "Rejoin Last Game", OnRejoin, ButtonStyle.Good, 30);
            UIFactory.LE(_rejoinButton, 72);
            UIFactory.LE(UIFactory.Button(col, "Settings", OpenSettings, ButtonStyle.Secondary, 30, A.iconGear), 76);
            UIFactory.LE(UIFactory.Button(col, "Credits", () => ShowScreen(View.Credits), ButtonStyle.Secondary, 30), 76);
            UIFactory.LE(UIFactory.Button(col, "Quit", Quit, ButtonStyle.Ghost, 30), 72);

            _footerText = UIFactory.Text(g.transform, "", 22, new Color(1, 1, 1, 0.55f), TextAnchor.LowerRight);
            UIFactory.Place(_footerText.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-30, 20), new Vector2(900, 60));
        }

        void RefreshMainMenu()
        {
            _nameInput.text = GameSettings.PlayerName;
            var s = NetworkSessionManager.Instance;
            _rejoinButton.gameObject.SetActive(s != null && s.CanRejoin);
            var svc = MultiplayerServiceManager.Instance;
            string who = svc != null && svc.IsSignedIn ? $"signed in (profile '{svc.Profile}')" : (svc != null && !string.IsNullOrEmpty(svc.LastError) ? "offline - " + svc.LastError.Split('\n')[0] : "connecting to Unity services...");
            _footerText.text = $"Secret Jenga {GameRules.BuildVersion}  |  {who}";
        }

        void OnPlayOnline()
        {
            GameSettings.PlayerName = _nameInput.text;
            ShowScreen(View.Online);
        }

        async void OnRejoin()
        {
            GameSettings.PlayerName = _nameInput.text;
            var s = NetworkSessionManager.Instance;
            if (s != null) await s.RejoinAsync();
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ Online menu
        void BuildOnlineMenu()
        {
            var g = NewScreen(View.Online, "OnlineMenu");
            var col = LeftCard(g, "OnlineCard", new Vector2(620, 700));
            UIFactory.Title(col, "Play Online");
            var info = UIFactory.Text(col, "Play with 2-4 friends over the internet.\nHost a table or join one.", 26, Palette.Paper);
            UIFactory.LE(info, 80);
            UIFactory.LE(UIFactory.Button(col, "Create Room", () => { PrepareCreate(); ShowScreen(View.Create); }, ButtonStyle.Primary, 34), 90);
            UIFactory.LE(UIFactory.Button(col, "Join With Code", () => ShowScreen(View.Join), ButtonStyle.Secondary, 32), 80);
            UIFactory.LE(UIFactory.Button(col, "Browse Rooms", () => ShowScreen(View.Browser), ButtonStyle.Secondary, 32, A.iconRefresh), 80);
            UIFactory.LE(UIFactory.Button(col, "Back", () => ShowScreen(View.Main), ButtonStyle.Ghost, 28), 66);
        }

        // ------------------------------------------------------------------ Create room
        void BuildCreateRoom()
        {
            var g = NewScreen(View.Create, "CreateRoom");
            var col = CenterCard(g, "CreateCard", new Vector2(820, 900));
            UIFactory.Title(col, "Create Room");
            var rl = UIFactory.Text(col, "Room name", 26, Palette.Paper, TextAnchor.MiddleLeft);
            UIFactory.LE(rl, 34);
            _roomNameInput = UIFactory.Input(col, "Room name", "", 40);
            UIFactory.LE(_roomNameInput, 66);

            _create = new FactoryRefs();
            _create.MaxPlayers = UIFactory.Stepper(col, "Max players", GameRules.MinPlayers, GameRules.MaxPlayers, GameRules.MaxPlayers, 1, v => v.ToString(), null);
            _create.StartLayers = UIFactory.Stepper(col, "Starting layers", GameRules.MinStartLayers, GameRules.MaxStartLayers, GameRules.DefaultStartLayers, 1, v => v.ToString(), v =>
            {
                if (_create.Target.Value < v + 2) _create.Target.Set(v + 2, false);
            });
            _create.Target = UIFactory.Stepper(col, "Target height (layers)", GameRules.MinTargetHeight, GameRules.MaxTargetHeight, GameRules.DefaultTargetHeight, 1, v => v.ToString(), v =>
            {
                if (v < _create.StartLayers.Value + 2) _create.StartLayers.Set(v - 2, false);
            });
            _create.TurnTime = UIFactory.Stepper(col, "Turn time", 10, 90, GameRules.DefaultTurnSeconds, 5, v => v + " s", null);
            _create.Private = UIFactory.Toggle(col, "Private room (join with code only)", false, null);

            var row = UIFactory.Rect("Buttons", col);
            UIFactory.LE(row, 86);
            UIFactory.HLayout(row, 20, 0);
            UIFactory.Button(row, "Back", () => ShowScreen(View.Online), ButtonStyle.Ghost, 30);
            UIFactory.Button(row, "Create", OnCreateRoom, ButtonStyle.Primary, 34);
        }

        void PrepareCreate()
        {
            if (string.IsNullOrWhiteSpace(_roomNameInput.text)) _roomNameInput.text = GameSettings.PlayerName + "'s table";
        }

        async void OnCreateRoom()
        {
            var s = new RoomSettings
            {
                roomName = _roomNameInput.text,
                maxPlayers = _create.MaxPlayers.Value,
                targetHeight = _create.Target.Value,
                startLayers = _create.StartLayers.Value,
                turnSeconds = _create.TurnTime.Value,
                isPrivate = _create.Private.isOn
            };
            if (NetworkSessionManager.Instance != null) await NetworkSessionManager.Instance.CreateRoomAsync(s);
        }

        // ------------------------------------------------------------------ Join with code
        void BuildJoinCode()
        {
            var g = NewScreen(View.Join, "JoinCode");
            var col = CenterCard(g, "JoinCard", new Vector2(760, 560));
            UIFactory.Title(col, "Join With Code");
            var info = UIFactory.Text(col, "Ask the host for the room code shown in their lobby.", 26, Palette.Paper);
            UIFactory.LE(info, 50);
            _codeInput = UIFactory.Input(col, "ROOM CODE", "", 10, 44);
            _codeInput.textComponent.alignment = TextAnchor.MiddleCenter;
            ((Text)_codeInput.placeholder).alignment = TextAnchor.MiddleCenter;
            _codeInput.onValidateInput += (text, index, c) => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '\0';
            UIFactory.LE(_codeInput, 90);
            var row = UIFactory.Rect("Buttons", col);
            UIFactory.LE(row, 86);
            UIFactory.HLayout(row, 20, 0);
            UIFactory.Button(row, "Back", () => ShowScreen(View.Online), ButtonStyle.Ghost, 30);
            UIFactory.Button(row, "Join", OnJoinCode, ButtonStyle.Primary, 34);
        }

        async void OnJoinCode()
        {
            if (NetworkSessionManager.Instance != null) await NetworkSessionManager.Instance.JoinByCodeAsync(_codeInput.text);
        }

        // ------------------------------------------------------------------ Room browser
        void BuildBrowser()
        {
            var g = NewScreen(View.Browser, "Browser");
            var col = CenterCard(g, "BrowserCard", new Vector2(1180, 900));
            var header = UIFactory.Rect("Header", col);
            UIFactory.LE(header, 80);
            UIFactory.HLayout(header, 10, 0, TextAnchor.MiddleLeft, false);
            var t = UIFactory.Text(header, "Browse Rooms", 50, Palette.Cream, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.LE(t, -1, -1, 1);
            UIFactory.IconButton(header, A.iconRefresh, RefreshBrowser, Palette.Accent, Palette.WalnutDark, 70);

            var colHead = UIFactory.Rect("Columns", col);
            UIFactory.LE(colHead, 36);
            UIFactory.HLayout(colHead, 10, 0, TextAnchor.MiddleLeft, false);
            foreach (var (label, w) in new[] { ("ROOM", -1f), ("PLAYERS", 160f), ("TARGET", 160f), ("", 180f) })
            {
                var h = UIFactory.Text(colHead, label, 22, Palette.Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
                if (w < 0) UIFactory.LE(h, -1, -1, 1); else UIFactory.LE(h, -1, w);
            }

            // scroll list
            var scrollBg = UIFactory.Image(col, "ScrollBg", UIFactory.RoundedSprite, new Color(0, 0, 0, 0.25f));
            UIFactory.LE(scrollBg, 520, -1, -1, 1);
            var scroll = scrollBg.gameObject.AddComponent<ScrollRect>();
            var viewport = UIFactory.Rect("Viewport", scrollBg.transform);
            UIFactory.Stretch(viewport, 8, 8, 8, 8);
            viewport.gameObject.AddComponent<RectMask2D>();
            _browserList = UIFactory.Rect("Content", viewport);
            _browserList.anchorMin = new Vector2(0, 1);
            _browserList.anchorMax = new Vector2(1, 1);
            _browserList.pivot = new Vector2(0.5f, 1);
            _browserList.sizeDelta = Vector2.zero;
            var v = UIFactory.VLayout(_browserList, 8, 4);
            v.childForceExpandHeight = false;
            var fit = _browserList.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = _browserList;
            scroll.horizontal = false;
            scroll.scrollSensitivity = 30;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            _browserStatus = UIFactory.Text(col, "", 26, Palette.Paper);
            UIFactory.LE(_browserStatus, 40);
            UIFactory.LE(UIFactory.Button(col, "Back", () => ShowScreen(View.Online), ButtonStyle.Ghost, 30), 70);
        }

        async void RefreshBrowser()
        {
            if (_browserBusy) return;
            _browserBusy = true;
            foreach (Transform c in _browserList) Destroy(c.gameObject);
            _browserStatus.text = "Searching for open rooms...";
            try
            {
                await MultiplayerServiceManager.Instance.EnsureSignedInAsync();
                List<Lobby> lobbies = await LobbyManager.Instance.QueryOpenLobbiesAsync();
                if (this == null) return;
                foreach (Transform c in _browserList) Destroy(c.gameObject);
                foreach (var l in lobbies) AddBrowserRow(l);
                _browserStatus.text = lobbies.Count == 0 ? "No open rooms right now. Create one and invite friends!" : $"{lobbies.Count} open room(s)";
            }
            catch (System.Exception e)
            {
                _browserStatus.text = LobbyManager.Describe(e);
            }
            finally { _browserBusy = false; }
        }

        void AddBrowserRow(Lobby l)
        {
            var row = UIFactory.Image(_browserList, "Room", UIFactory.RoundedSprite, new Color(1, 1, 1, 0.06f));
            UIFactory.LE(row, 84);
            var h = UIFactory.HLayout(row, 10, 0, TextAnchor.MiddleLeft, false);
            h.padding = new RectOffset(24, 12, 10, 10);
            var name = UIFactory.Text(row.transform, l.Name, 30, Palette.Cream, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.LE(name, -1, -1, 1);
            var players = UIFactory.Text(row.transform, $"{l.Players.Count} / {l.MaxPlayers}", 28, Palette.Paper, TextAnchor.MiddleLeft);
            UIFactory.LE(players, -1, 160);
            string target = l.Data != null && l.Data.TryGetValue(LobbyManager.KeyTarget, out var d) ? d.Value + " layers" : "-";
            var tt = UIFactory.Text(row.transform, target, 28, Palette.Paper, TextAnchor.MiddleLeft);
            UIFactory.LE(tt, -1, 160);
            string id = l.Id;
            var join = UIFactory.Button(row.transform, "Join", async () =>
            {
                if (NetworkSessionManager.Instance != null) await NetworkSessionManager.Instance.JoinByIdAsync(id);
            }, ButtonStyle.Primary, 28);
            UIFactory.LE(join, 64, 170);
        }

        // ------------------------------------------------------------------ Credits
        void BuildCredits()
        {
            var g = NewScreen(View.Credits, "Credits");
            var col = CenterCard(g, "CreditsCard", new Vector2(860, 760));
            UIFactory.Title(col, "Credits");
            var t = UIFactory.Text(col,
                "<b>SECRET JENGA</b>\n\n" +
                "Game design, code & procedural art\n<color=#F2A13A>Arda</color> with Claude\n\n" +
                "Built with Unity 6, Netcode for GameObjects,\nUnity Relay & Lobby (Multiplayer Services)\n\n" +
                "All models, textures and sounds are generated procedurally\nby the project's own tools.\n\n" +
                "Jenga is a trademark of its respective owner.\nThis is a non-commercial fan prototype.", 27, Palette.Paper);
            UIFactory.LE(t, 470);
            UIFactory.LE(UIFactory.Button(col, "Back", () => ShowScreen(View.Main), ButtonStyle.Ghost, 30), 70);
        }

        // ------------------------------------------------------------------ Pause
        void BuildPause()
        {
            _pauseGroup = NewOverlay("Pause", 0.55f);
            var card = UIFactory.Card(_pauseGroup.transform, "PauseCard");
            UIFactory.Place((RectTransform)card.transform.parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640, 600));
            var col = UIFactory.Rect("Col", card.transform);
            UIFactory.Stretch(col);
            UIFactory.VLayout(col, 18, 44);
            UIFactory.Title(col, "Paused");
            var note = UIFactory.Text(col, "This is an online match - the game keeps running for everyone else.", 24, Palette.Paper);
            UIFactory.LE(note, 66);
            UIFactory.LE(UIFactory.Button(col, "Resume", TogglePause, ButtonStyle.Primary, 32), 84);
            UIFactory.LE(UIFactory.Button(col, "Settings", OpenSettings, ButtonStyle.Secondary, 30, A.iconGear), 76);
            UIFactory.LE(UIFactory.Button(col, "Leave Game", () => { TogglePause(); LeaveRoom(); }, ButtonStyle.Danger, 30), 76);
        }

        void TogglePause()
        {
            _pauseOpen = !_pauseOpen;
            SetOverlay(_pauseGroup, _pauseOpen);
            KeepOverlaysOnTop();
        }

        void ClosePauseIfOpen()
        {
            if (_pauseOpen) TogglePause();
            if (_settingsOpen) CloseSettings();
        }

        async void LeaveRoom()
        {
            var s = NetworkSessionManager.Instance;
            if (s != null) await s.LeaveAsync();
        }

        // ------------------------------------------------------------------ Settings
        void BuildSettings()
        {
            _settingsGroup = NewOverlay("Settings", 0.6f);
            var card = UIFactory.Card(_settingsGroup.transform, "SettingsCard");
            UIFactory.Place((RectTransform)card.transform.parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 980));
            var col = UIFactory.Rect("Col", card.transform);
            UIFactory.Stretch(col);
            UIFactory.VLayout(col, 12, 44);
            UIFactory.Title(col, "Settings", 48);

            var nameRow = UIFactory.Rect("NameRow", col);
            UIFactory.LE(nameRow, 64);
            UIFactory.HLayout(nameRow, 16, 0, TextAnchor.MiddleLeft, false);
            UIFactory.LE(UIFactory.Text(nameRow, "Player name", 28, Palette.Cream, TextAnchor.MiddleLeft), -1, 260);
            _settingsNameInput = UIFactory.Input(nameRow, "Your name", GameSettings.PlayerName, 16);
            UIFactory.LE(_settingsNameInput, 60, -1, 1);
            _settingsNameInput.onEndEdit.AddListener(v => { GameSettings.PlayerName = v; if (_nameInput != null) _nameInput.text = GameSettings.PlayerName; });

            UIFactory.Slider(col, "Master volume", GameSettings.MasterVolume, v => GameSettings.MasterVolume = v);
            UIFactory.Slider(col, "Music", GameSettings.MusicVolume, v => GameSettings.MusicVolume = v);
            UIFactory.Slider(col, "Sound effects", GameSettings.SfxVolume, v => GameSettings.SfxVolume = v);
            UIFactory.Slider(col, "Camera sensitivity", Mathf.InverseLerp(0.3f, 2f, GameSettings.MouseSensitivity), v => GameSettings.MouseSensitivity = Mathf.Lerp(0.3f, 2f, v));

            var names = QualitySettings.names;
            UIFactory.Stepper(col, "Graphics quality", 0, Mathf.Max(0, names.Length - 1), Mathf.Clamp(GameSettings.Quality, 0, Mathf.Max(0, names.Length - 1)), 1,
                v => names.Length > 0 ? names[v] : v.ToString(), v => { GameSettings.Quality = v; GameSettings.ApplyDisplay(); });
            UIFactory.Toggle(col, "Fullscreen", GameSettings.Fullscreen, v => { GameSettings.Fullscreen = v; GameSettings.ApplyDisplay(); });
            UIFactory.Divider(col);
            var dev = UIFactory.Text(col, "Testing", 24, Palette.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.LE(dev, 30);
            UIFactory.Toggle(col, "New anonymous identity on every launch (run 2 copies on 1 PC)", GameSettings.UniqueProfilePerLaunch, v => GameSettings.UniqueProfilePerLaunch = v);
            UIFactory.Toggle(col, "Allow host to start alone (solo physics test)", GameSettings.AllowSoloStart, v => { GameSettings.AllowSoloStart = v; RefreshLobby(); });
            UIFactory.LE(UIFactory.Button(col, "Back", CloseSettings, ButtonStyle.Primary, 30), 76);
        }

        void OpenSettings()
        {
            _settingsNameInput.text = GameSettings.PlayerName;
            _settingsOpen = true;
            SetOverlay(_settingsGroup, true);
            KeepOverlaysOnTop();
        }

        void CloseSettings()
        {
            _settingsOpen = false;
            SetOverlay(_settingsGroup, false);
            if (_current == View.Main) RefreshMainMenu();
        }
    }
}
