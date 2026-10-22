using System.Collections.Generic;
using SecretJenga.Networking;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

namespace SecretJenga.UI
{
    public partial class UIManager
    {
        InputField _nameInput, _settingsNameInput, _roomNameInput, _codeInput, _addressInput, _hostPortInput, _joinPortInput;
        Button _rejoinButton;
        Text _footerText, _browserStatus, _hostModeInfo, _joinModeInfo;
        RectTransform _browserList;
        bool _browserBusy;
        HostRefs _host;
        GameObject _hostOnlineOnly, _hostDirectOnly, _joinOnlineOnly, _joinDirectOnly;
        Button _hostModeDirect, _hostModeOnline, _joinModeDirect, _joinModeOnline;

        class HostRefs
        {
            public UIFactory.StepperRef MaxPlayers, StartLayers, TurnTime, Rounds;
            public Toggle Private;
        }

        /// <summary>A taped sheet in the middle of the screen; returns its content column.</summary>
        RectTransform CenterCard(CanvasGroup screen, string name, Vector2 size, Vector2 offset = default)
        {
            float tilt = ((name.GetHashCode() & 0xff) / 255f - 0.5f) * 2.4f;
            var sheet = UIFactory.PaperSheet(screen.transform, "Sheet" + name, new Vector2(0.5f, 0.5f), offset, size, tilt, 2, 64);
            return sheet.Body;
        }

        void Logo(Transform parent, float scale = 1f)
        {
            var the = UIFactory.Write(parent, FontKind.Type, UIFactory.Spaced("the"), Mathf.RoundToInt(26 * scale), Ink.RedBright, TextAnchor.MiddleLeft);
            UIFactory.LE(the, 34 * scale);
            var title = UIFactory.Write(parent, FontKind.Title, "Last Block", Mathf.RoundToInt(120 * scale), Ink.Bone, TextAnchor.MiddleLeft);
            UIFactory.AddShadow(title, new Color(0, 0, 0, 0.95f), new Vector2(3, -5));
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.LE(title, 118 * scale);
            var rule = UIFactory.Image(parent, "Rule", UIFactory.StrokeSprite, Ink.RedBright, false);
            UIFactory.LE(rule, 12 * scale, 430 * scale);
            var tag = UIFactory.Write(parent, FontKind.Body, L.T("One tower. One table. Whoever drops it gets bonked.", "Tek kule. Tek masa. Yıkana çekiç var."), Mathf.RoundToInt(28 * scale), Ink.BoneDim, TextAnchor.MiddleLeft, FontStyle.Italic);
            UIFactory.LE(tag, 44 * scale);
        }

        // ------------------------------------------------------------------ Mode switch helper
        (Button direct, Button online) ModeSwitch(Transform parent, System.Action<bool> onPick)
        {
            var row = UIFactory.ButtonRow(parent, 58, 50);
            var d = UIFactory.Button(row, L.T("Direct  (IP / LAN)", "Doğrudan  (IP / LAN)"), () => onPick(true), ButtonStyle.Secondary, 29);
            var o = UIFactory.Button(row, L.T("Online  (Relay)", "Çevrimiçi  (Relay)"), () => onPick(false), ButtonStyle.Secondary, 29);
            AddCircle(d);
            AddCircle(o);
            return (d, o);
        }

        /// <summary>Hand-drawn ink circle around the chosen option.</summary>
        static void AddCircle(Button b)
        {
            var c = UIFactory.Image(b.transform, "Circled", A.inkCircle != null ? A.inkCircle : UIFactory.CircleSprite, Ink.Red, false);
            c.rectTransform.anchorMin = Vector2.zero;
            c.rectTransform.anchorMax = Vector2.one;
            c.rectTransform.offsetMin = new Vector2(-26, -12);
            c.rectTransform.offsetMax = new Vector2(22, 10);
            c.rectTransform.localRotation = Quaternion.Euler(0, 0, -2f);
            c.gameObject.SetActive(false);
        }

        static void MarkMode(Button direct, Button online, bool isDirect)
        {
            direct.transform.Find("Circled").gameObject.SetActive(isDirect);
            online.transform.Find("Circled").gameObject.SetActive(!isDirect);
        }

        // ------------------------------------------------------------------ Splash
        void BuildSplash()
        {
            UIFactory.OnPaper = false;
            var g = NewScreen(View.Splash, "Splash");
            var bg = UIFactory.Image(g.transform, "Bg", null, new Color(0.0f, 0.0f, 0.0f, 0.97f));
            bg.raycastTarget = true;
            UIFactory.Stretch(bg.rectTransform);
            var col = UIFactory.Rect("Logo", g.transform);
            UIFactory.Place(col, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(60, 20), new Vector2(900, 300));
            UIFactory.VLayout(col, 4, 0, TextAnchor.MiddleLeft);
            Logo(col, 1.15f);
            var hint = UIFactory.Write(g.transform, FontKind.Type, L.T("click to sit down", "oturmak için tıkla"), 20, Ink.BoneFaint, TextAnchor.MiddleCenter);
            UIFactory.Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 70), new Vector2(600, 40));
            var btn = bg.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => ShowScreen(View.Main));
        }

        // ------------------------------------------------------------------ Main menu: written straight over the room
        void BuildMainMenu()
        {
            UIFactory.OnPaper = false;
            var g = NewScreen(View.Main, "MainMenu");
            var shade = UIFactory.Image(g.transform, "Shade", UIFactory.FadeSprite, new Color(0, 0, 0, 0.92f), false);
            shade.rectTransform.anchorMin = Vector2.zero;
            shade.rectTransform.anchorMax = new Vector2(0.62f, 1f);
            shade.rectTransform.offsetMin = shade.rectTransform.offsetMax = Vector2.zero;

            var col = UIFactory.Rect("Menu", g.transform);
            UIFactory.Place(col, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(150, 10), new Vector2(720, 880));
            UIFactory.VLayout(col, 2, 0, TextAnchor.UpperLeft);
            Logo(col, 0.9f);
            UIFactory.LE(UIFactory.Rect("Gap", col), 40);

            var nameRow = UIFactory.Rect("NameRow", col);
            UIFactory.LE(nameRow, 62);
            UIFactory.HLayout(nameRow, 18, 0, TextAnchor.LowerLeft, false);
            UIFactory.LE(UIFactory.Write(nameRow, FontKind.Type, L.T("name:", "isim:"), 24, Ink.BoneDim, TextAnchor.LowerLeft), 50, 92);
            _nameInput = UIFactory.Input(nameRow, L.T("write your name", "adını yaz"), GameSettings.PlayerName, 16, 30);
            UIFactory.LE(_nameInput, 54, 380);
            _nameInput.onEndEdit.AddListener(v => GameSettings.PlayerName = v);
            UIFactory.LE(UIFactory.Rect("Gap", col), 26);

            MenuItem(col, L.T("Play alone vs bots", "Botlara karşı tek oyna"), () => { SaveName(); StartSoloWithBots(); });
            MenuItem(col, L.T("Host a table", "Masa kur"), () => { SaveName(); PrepareHost(); ShowScreen(View.Host); });
            MenuItem(col, L.T("Join a table", "Masaya katıl"), () => { SaveName(); PrepareJoin(); ShowScreen(View.Join); });
            _rejoinButton = MenuItem(col, L.T("Return to your seat", "Yerine dön"), OnRejoin);
            MenuItem(col, L.T("Settings", "Ayarlar"), OpenSettings);
            MenuItem(col, L.T("Credits", "Emeği geçenler"), () => ShowScreen(View.Credits));
            MenuItem(col, L.T("Leave", "Çık"), Quit, ButtonStyle.Ghost);

            // language switch, top right
            var lang = UIFactory.Rect("Language", g.transform);
            UIFactory.Place(lang, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -30), new Vector2(520, 60));
            UIFactory.HLayout(lang, 26, 0, TextAnchor.MiddleRight, false);
            var en = UIFactory.Button(lang, "English", () => L.Current = Language.English, ButtonStyle.Ghost, 28);
            var tr = UIFactory.Button(lang, "Türkçe", () => L.Current = Language.Turkish, ButtonStyle.Ghost, 28);
            AddCircle(en);
            AddCircle(tr);
            MarkMode(tr, en, L.TR);

            _footerText = UIFactory.Write(g.transform, FontKind.Type, "", 17, Ink.BoneFaint, TextAnchor.LowerRight);
            UIFactory.Place(_footerText.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 28), new Vector2(1200, 40));
        }

        Button MenuItem(Transform col, string label, UnityEngine.Events.UnityAction a, ButtonStyle style = ButtonStyle.Primary)
        {
            var b = UIFactory.Button(col, label, a, style, style == ButtonStyle.Ghost ? 34 : 46);
            UIFactory.LE(b, 72);
            return b;
        }

        void SaveName() => GameSettings.PlayerName = _nameInput.text;

        void RefreshMainMenu()
        {
            _nameInput.text = GameSettings.PlayerName;
            var s = NetworkSessionManager.Instance;
            _rejoinButton.gameObject.SetActive(s != null && s.CanRejoin);
            var svc = MultiplayerServiceManager.Instance;
            string online = string.IsNullOrEmpty(Application.cloudProjectId) ? L.T("direct (IP) ready  -  online needs Unity Cloud linking", "doğrudan (IP) hazır  -  çevrimiçi için Unity Cloud bağlantısı gerekir")
                : svc != null && svc.IsSignedIn ? L.T("online ready", "çevrimiçi hazır")
                : (svc != null && !string.IsNullOrEmpty(svc.LastError) ? L.T("online unavailable - direct IP still works", "çevrimiçi yok - doğrudan IP çalışır") : L.T("reaching online services...", "çevrimiçi servislere bağlanılıyor..."));
            _footerText.text = $"{GameRules.BuildVersion}   /   {online}";
        }

        /// <summary>One click: open a private direct table on this PC and seat bots in every other chair.</summary>
        async void StartSoloWithBots()
        {
            var session = NetworkSessionManager.Instance;
            if (session == null) return;
            _pendingBots = 2;
            var s = new RoomSettings
            {
                roomName = L.T(GameSettings.PlayerName + "'s table", GameSettings.PlayerName + " masası"),
                maxPlayers = GameRules.MaxPlayers,
                startLayers = GameRules.DefaultStartLayers,
                turnSeconds = GameRules.DefaultTurnSeconds,
                isPrivate = true,
                direct = true,
                port = GameSettings.DirectPort,
            };
            await session.CreateRoomAsync(s);
        }

        /// <summary>Bots to seat as soon as our own table is up (Play alone vs bots).</summary>
        int _pendingBots;

        void SeatPendingBots()
        {
            if (_pendingBots <= 0 || _game == null || !_game.IsServer || _game.Phase.Value != GamePhase.Lobby) return;
            while (_pendingBots > 0 && _game.ServerAddBot()) _pendingBots--;
            _pendingBots = 0;
        }

        async void OnRejoin()
        {
            SaveName();
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

        // ------------------------------------------------------------------ Host
        void BuildHostScreen()
        {
            var g = NewScreen(View.Host, "HostGame");
            var col = CenterCard(g, "Host", new Vector2(900, 1000));
            UIFactory.Caption(col, L.T("form 5  -  a table for the night", "form 5  -  bu gecelik bir masa"));
            UIFactory.Title(col, L.T("Open a Table", "Masa Kur"), 58);
            UIFactory.Divider(col);
            var modes = ModeSwitch(col, SetHostMode);
            _hostModeDirect = modes.direct;
            _hostModeOnline = modes.online;
            _hostModeInfo = UIFactory.Write(col, FontKind.Body, "", 22, Ink.Faded, TextAnchor.UpperLeft, FontStyle.Italic);
            UIFactory.LE(_hostModeInfo, 60);

            UIFactory.Caption(col, L.T("name of the table", "masanın adı"));
            _roomNameInput = UIFactory.Input(col, L.T("a table with no name", "isimsiz masa"), "", 40, 30);
            UIFactory.LE(_roomNameInput, 52);
            UIFactory.LE(UIFactory.Rect("Gap", col), 8);

            _host = new HostRefs();
            _host.MaxPlayers = UIFactory.Stepper(col, L.T("Chairs", "Sandalye"), GameRules.MinPlayers, GameRules.MaxPlayers, GameRules.MaxPlayers, 1, v => v.ToString(), null);
            _host.StartLayers = UIFactory.Stepper(col, L.T("Floors at the start", "Başlangıç kat sayısı"), GameRules.MinStartLayers, GameRules.MaxStartLayers, GameRules.DefaultStartLayers, 1, v => v.ToString(), null);
            _host.TurnTime = UIFactory.Stepper(col, L.T("Time for each turn", "Tur başına süre"), 0, GameRules.MaxTurnSeconds, GameRules.DefaultTurnSeconds, 20, FormatTimer, null);
            _host.Rounds = UIFactory.Stepper(col, L.T("Rounds in a match", "Maçtaki el sayısı"), GameRules.MinMatchRounds, GameRules.MaxMatchRounds, GameRules.DefaultMatchRounds, 1, v => v.ToString(), null);

            var directRow = UIFactory.Rect("DirectOnly", col);
            UIFactory.LE(directRow, 52);
            UIFactory.HLayout(directRow, 14, 0, TextAnchor.LowerLeft, false);
            UIFactory.LE(UIFactory.Write(directRow, FontKind.Body, L.T("UDP port", "UDP portu"), 27, Ink.Black, TextAnchor.MiddleLeft), -1, -1, 1);
            _hostPortInput = UIFactory.Input(directRow, "7777", GameSettings.DirectPort.ToString(), 5, 28);
            _hostPortInput.contentType = InputField.ContentType.IntegerNumber;
            UIFactory.LE(_hostPortInput, 48, 170);
            _hostDirectOnly = directRow.gameObject;

            _host.Private = UIFactory.Toggle(col, L.T("Private - only those with the code", "Özel - sadece kodu bilenler"), false, null);
            _hostOnlineOnly = _host.Private.gameObject;

            UIFactory.LE(UIFactory.Rect("Push", col), -1, -1, -1, 1);
            var row = UIFactory.ButtonRow(col, 70, 60);
            UIFactory.Button(row, L.T("back", "geri"), () => ShowScreen(View.Main), ButtonStyle.Ghost, 30);
            UIFactory.LE(UIFactory.Rect("Push", row), -1, -1, 1);
            UIFactory.Button(row, L.T("Open the table", "Masayı aç"), OnHost, ButtonStyle.Primary, 40);
            UIFactory.OnPaper = false;
        }

        static string FormatTimer(int v) => v <= 0 ? L.T("none", "yok") : v + L.T(" s", " sn");

        void PrepareHost()
        {
            if (string.IsNullOrWhiteSpace(_roomNameInput.text)) _roomNameInput.text = L.T(GameSettings.PlayerName + "'s table", GameSettings.PlayerName + " masası");
            _hostPortInput.text = GameSettings.DirectPort.ToString();
            SetHostMode(GameSettings.UseDirectConnection);
        }

        void SetHostMode(bool direct)
        {
            GameSettings.UseDirectConnection = direct;
            MarkMode(_hostModeDirect, _hostModeOnline, direct);
            _hostDirectOnly.SetActive(direct);
            _hostOnlineOnly.SetActive(!direct);
            _hostModeInfo.text = direct
                ? L.T("Others sit down using this computer's address. Same network, same PC, or over the internet with a forwarded port / VPN.",
                      "Diğerleri bu bilgisayarın adresiyle oturur. Aynı ağ, aynı PC ya da port yönlendirme / VPN ile internetten.")
                : L.T("Friends sit down with a room code through Unity Relay. The project must be linked to Unity Cloud.",
                      "Arkadaşların Unity Relay üzerinden oda koduyla oturur. Proje Unity Cloud'a bağlı olmalı.");
        }

        async void OnHost()
        {
            ushort port = ParsePort(_hostPortInput.text);
            var s = new RoomSettings
            {
                roomName = _roomNameInput.text,
                maxPlayers = _host.MaxPlayers.Value,
                startLayers = _host.StartLayers.Value,
                turnSeconds = _host.TurnTime.Value,
                matchRounds = _host.Rounds.Value,
                isPrivate = _host.Private.isOn,
                direct = GameSettings.UseDirectConnection,
                port = port,
            };
            GameSettings.DirectPort = port;
            if (NetworkSessionManager.Instance != null) await NetworkSessionManager.Instance.CreateRoomAsync(s);
        }

        static ushort ParsePort(string text) =>
            int.TryParse(text, out var p) && p >= 1024 && p <= 65535 ? (ushort)p : GameSettings.DefaultPort;

        // ------------------------------------------------------------------ Join
        void BuildJoinScreen()
        {
            var g = NewScreen(View.Join, "JoinGame");
            var col = CenterCard(g, "Join", new Vector2(860, 760));
            UIFactory.Caption(col, L.T("form 6  -  where is the table?", "form 6  -  masa nerede?"));
            UIFactory.Title(col, L.T("Take a Seat", "Otur"), 58);
            UIFactory.Divider(col);
            var modes = ModeSwitch(col, SetJoinMode);
            _joinModeDirect = modes.direct;
            _joinModeOnline = modes.online;
            _joinModeInfo = UIFactory.Write(col, FontKind.Body, "", 22, Ink.Faded, TextAnchor.UpperLeft, FontStyle.Italic);
            UIFactory.LE(_joinModeInfo, 40);

            // direct
            var direct = UIFactory.Rect("Direct", col);
            UIFactory.VLayout(direct, 6, 0, TextAnchor.UpperLeft);
            UIFactory.LE(direct, 130);
            UIFactory.Caption(direct, L.T("address of the host  (shown in their lobby)", "kurucunun adresi  (onun lobisinde yazar)"));
            var arow = UIFactory.Rect("AddrRow", direct);
            UIFactory.LE(arow, 70);
            UIFactory.HLayout(arow, 12, 0, TextAnchor.LowerLeft, false);
            _addressInput = UIFactory.Input(arow, "192.168.1.20", GameSettings.LastDirectAddress, 64, 38);
            UIFactory.LE(_addressInput, 64, -1, 1);
            UIFactory.LE(UIFactory.Write(arow, FontKind.Type, ":", 38, Ink.Faded, TextAnchor.LowerCenter), 64, 24);
            _joinPortInput = UIFactory.Input(arow, "7777", GameSettings.DirectPort.ToString(), 5, 38);
            _joinPortInput.contentType = InputField.ContentType.IntegerNumber;
            UIFactory.LE(_joinPortInput, 64, 150);
            _joinDirectOnly = direct.gameObject;

            // online
            var online = UIFactory.Rect("Online", col);
            UIFactory.VLayout(online, 6, 0, TextAnchor.UpperLeft);
            UIFactory.LE(online, 130);
            UIFactory.Caption(online, L.T("room code", "oda kodu"));
            _codeInput = UIFactory.Input(online, L.T("six letters", "altı harf"), "", 10, 46);
            _codeInput.onValidateInput += (text, index, c) => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '\0';
            UIFactory.LE(_codeInput, 66);
            var br = UIFactory.ButtonRow(online, 44);
            UIFactory.Button(br, L.T("or look for open tables", "ya da açık masalara bak"), () => ShowScreen(View.Browser), ButtonStyle.Ghost, 24);
            _joinOnlineOnly = online.gameObject;

            UIFactory.LE(UIFactory.Rect("Push", col), -1, -1, -1, 1);
            var row = UIFactory.ButtonRow(col, 70, 60);
            UIFactory.Button(row, L.T("back", "geri"), () => ShowScreen(View.Main), ButtonStyle.Ghost, 30);
            UIFactory.LE(UIFactory.Rect("Push", row), -1, -1, 1);
            UIFactory.Button(row, L.T("Sit down", "Otur"), OnJoin, ButtonStyle.Primary, 40);
            UIFactory.OnPaper = false;
        }

        void PrepareJoin()
        {
            _addressInput.text = GameSettings.LastDirectAddress;
            _joinPortInput.text = GameSettings.DirectPort.ToString();
            SetJoinMode(GameSettings.UseDirectConnection);
        }

        void SetJoinMode(bool direct)
        {
            GameSettings.UseDirectConnection = direct;
            MarkMode(_joinModeDirect, _joinModeOnline, direct);
            _joinDirectOnly.SetActive(direct);
            _joinOnlineOnly.SetActive(!direct);
            _joinModeInfo.text = direct ? L.T("Same computer? Write 127.0.0.1", "Aynı bilgisayar mı? 127.0.0.1 yaz") : L.T("The code is written in the host's lobby.", "Kod, kurucunun lobisinde yazar.");
        }

        async void OnJoin()
        {
            var s = NetworkSessionManager.Instance;
            if (s == null) return;
            if (GameSettings.UseDirectConnection) await s.JoinDirectAsync(_addressInput.text, ParsePort(_joinPortInput.text));
            else await s.JoinByCodeAsync(_codeInput.text);
        }

        // ------------------------------------------------------------------ Room browser (Relay + Lobby)
        void BuildBrowser()
        {
            var g = NewScreen(View.Browser, "Browser");
            var col = CenterCard(g, "Browser", new Vector2(1180, 900));
            var header = UIFactory.Rect("Header", col);
            UIFactory.LE(header, 80);
            UIFactory.HLayout(header, 10, 0, TextAnchor.MiddleLeft, false);
            var t = UIFactory.Write(header, FontKind.Title, L.T("Open Tables", "Açık Masalar"), 58, Ink.Black, TextAnchor.MiddleLeft);
            UIFactory.LE(t, -1, -1, 1);
            UIFactory.Button(header, L.T("look again", "tekrar bak"), RefreshBrowser, ButtonStyle.Secondary, 28);

            var colHead = UIFactory.Rect("Columns", col);
            UIFactory.LE(colHead, 34);
            UIFactory.HLayout(colHead, 10, 0, TextAnchor.MiddleLeft, false);
            foreach (var (label, w) in new[] { (L.T("table", "masa"), -1f), (L.T("chairs", "sandalye"), 160f), ("", 160f) })
            {
                var h = UIFactory.Write(colHead, FontKind.Type, label, 20, Ink.Faded, TextAnchor.MiddleLeft);
                if (w < 0) UIFactory.LE(h, -1, -1, 1); else UIFactory.LE(h, -1, w);
            }
            UIFactory.Divider(col);

            var scrollBg = UIFactory.Image(col, "Scroll", null, new Color(0, 0, 0, 0));
            scrollBg.raycastTarget = true;
            UIFactory.LE(scrollBg, 520, -1, -1, 1);
            var scroll = scrollBg.gameObject.AddComponent<ScrollRect>();
            var viewport = UIFactory.Rect("Viewport", scrollBg.transform);
            UIFactory.Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            _browserList = UIFactory.Rect("Content", viewport);
            _browserList.anchorMin = new Vector2(0, 1);
            _browserList.anchorMax = new Vector2(1, 1);
            _browserList.pivot = new Vector2(0.5f, 1);
            _browserList.sizeDelta = Vector2.zero;
            var v = UIFactory.VLayout(_browserList, 2, 0);
            v.childForceExpandHeight = false;
            var fit = _browserList.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = _browserList;
            scroll.horizontal = false;
            scroll.scrollSensitivity = 30;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            _browserStatus = UIFactory.Write(col, FontKind.Body, "", 24, Ink.Faded, TextAnchor.MiddleLeft, FontStyle.Italic);
            UIFactory.LE(_browserStatus, 40);
            var row = UIFactory.ButtonRow(col, 64);
            UIFactory.Button(row, L.T("back", "geri"), () => ShowScreen(View.Join), ButtonStyle.Ghost, 30);
            UIFactory.OnPaper = false;
        }

        async void RefreshBrowser()
        {
            if (_browserBusy) return;
            _browserBusy = true;
            foreach (Transform c in _browserList) Destroy(c.gameObject);
            _browserStatus.text = L.T("Looking for lit rooms...", "Işığı yanan odalar aranıyor...");
            try
            {
                await MultiplayerServiceManager.Instance.EnsureSignedInAsync();
                List<Lobby> lobbies = await LobbyManager.Instance.QueryOpenLobbiesAsync();
                if (this == null) return;
                foreach (Transform c in _browserList) Destroy(c.gameObject);
                foreach (var l in lobbies) AddBrowserRow(l);
                _browserStatus.text = lobbies.Count == 0 ? L.T("Every room is dark tonight. Open a table yourself.", "Bu gece bütün odalar karanlık. Kendin bir masa kur.") : L.T(lobbies.Count == 1 ? "One table is waiting." : $"{lobbies.Count} tables are waiting.", $"{lobbies.Count} masa bekliyor.");
            }
            catch (System.Exception e)
            {
                _browserStatus.text = LobbyManager.Describe(e);
            }
            finally { _browserBusy = false; }
        }

        void AddBrowserRow(Lobby l)
        {
            UIFactory.OnPaper = true;
            var row = UIFactory.Rect("Room", _browserList);
            UIFactory.LE(row, 64);
            UIFactory.HLayout(row, 10, 0, TextAnchor.MiddleLeft, false);
            var name = UIFactory.Write(row, FontKind.Body, l.Name, 30, Ink.Black, TextAnchor.MiddleLeft);
            UIFactory.LE(name, -1, -1, 1);
            var players = UIFactory.Write(row, FontKind.Type, $"{l.Players.Count} / {l.MaxPlayers}", 24, Ink.Black, TextAnchor.MiddleLeft);
            UIFactory.LE(players, -1, 160);
            string id = l.Id;
            var join = UIFactory.Button(row, L.T("sit here", "buraya otur"), async () =>
            {
                if (NetworkSessionManager.Instance != null) await NetworkSessionManager.Instance.JoinByIdAsync(id);
            }, ButtonStyle.Primary, 30);
            UIFactory.LE(join, 60, 160);
            UIFactory.OnPaper = false;
        }

        // ------------------------------------------------------------------ Credits
        void BuildCredits()
        {
            var g = NewScreen(View.Credits, "Credits");
            var col = CenterCard(g, "Credits", new Vector2(900, 780));
            UIFactory.Title(col, L.T("Credits", "Emeği Geçenler"), 58);
            UIFactory.Divider(col);
            var t = UIFactory.Write(col, FontKind.Type, L.T(
                "THE LAST BLOCK - a prototype.\n\n" +
                "An online, first-person tabletop game in a kids'\nbedroom: one wooden tower, five little chairs.\n\n" +
                "Unity 6 / Netcode for GameObjects / Unity Transport\n(Unity Relay + Lobby optional).\n\n" +
                "Characters: Universal Animation Library by Quaternius (CC0),\ndressed as kids by the project's Blender script.\n" +
                "Room, textures and sounds are made by the project's own tools.\n" +
                "Type: Baloo 2, Patrick Hand, Grandstander (all SIL Open Font License).\n\n" +
                "Jenga is a trademark of its owner. Non-commercial prototype.",
                "THE LAST BLOCK - bir prototip.\n\n" +
                "Bir çocuk odasında geçen, çevrimiçi, birinci şahıs\nbir masa oyunu: tek ahşap kule, beş küçük sandalye.\n\n" +
                "Unity 6 / Netcode for GameObjects / Unity Transport\n(Unity Relay + Lobby isteğe bağlı).\n\n" +
                "Karakterler: Quaternius - Universal Animation Library (CC0),\nprojenin Blender betiğiyle çocuk olarak giydirildi.\n" +
                "Oda, dokular ve sesler projenin kendi araçlarıyla üretildi.\n" +
                "Yazı tipleri: Baloo 2, Patrick Hand, Grandstander (SIL Open Font License).\n\n" +
                "Jenga, sahibinin ticari markasıdır. Ticari olmayan prototip."), 21, Ink.Black, TextAnchor.UpperLeft);
            UIFactory.LE(t, 440);
            UIFactory.LE(UIFactory.Rect("Push", col), -1, -1, -1, 1);
            var row = UIFactory.ButtonRow(col, 64);
            UIFactory.Button(row, L.T("back", "geri"), () => ShowScreen(View.Main), ButtonStyle.Ghost, 30);
            UIFactory.OnPaper = false;
        }

        // ------------------------------------------------------------------ Pause
        void BuildPause()
        {
            _pauseGroup = NewOverlay("Pause", 0.55f);
            var sheet = UIFactory.PaperSheet(_pauseGroup.transform, "SheetPause", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 560), -1.4f, 1, 60);
            var col = sheet.Body;
            UIFactory.Title(col, L.T("A moment", "Bir dakika"), 54);
            var note = UIFactory.Write(col, FontKind.Body, L.T("The others are still at the table. The round does not wait.", "Diğerleri hâlâ masada. El seni beklemez."), 22, Ink.Faded, TextAnchor.UpperLeft, FontStyle.Italic);
            UIFactory.LE(note, 60);
            UIFactory.Divider(col);
            UIFactory.LE(UIFactory.Button(col, L.T("Back to the table", "Masaya dön"), TogglePause, ButtonStyle.Primary, 40), 66);
            UIFactory.LE(UIFactory.Button(col, L.T("Settings", "Ayarlar"), OpenSettings, ButtonStyle.Secondary, 32), 56);
            UIFactory.LE(UIFactory.Button(col, L.T("Get up and leave", "Kalk ve git"), () => { TogglePause(); LeaveRoom(); }, ButtonStyle.Danger, 34), 60);
            UIFactory.OnPaper = false;
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
            var sheet = UIFactory.PaperSheet(_settingsGroup.transform, "SheetSettings", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(940, 1010), 0.8f, 2, 64);
            var col = sheet.Body;
            col.GetComponent<VerticalLayoutGroup>().spacing = 8;
            UIFactory.Title(col, L.T("Settings", "Ayarlar"), 56);
            UIFactory.Divider(col);

            var nameRow = UIFactory.Rect("NameRow", col);
            UIFactory.LE(nameRow, 54);
            UIFactory.HLayout(nameRow, 16, 0, TextAnchor.LowerLeft, false);
            UIFactory.LE(UIFactory.Write(nameRow, FontKind.Body, L.T("Your name", "Adın"), 27, Ink.Black, TextAnchor.MiddleLeft), -1, 300);
            _settingsNameInput = UIFactory.Input(nameRow, L.T("write your name", "adını yaz"), GameSettings.PlayerName, 16, 28);
            UIFactory.LE(_settingsNameInput, 50, -1, 1);
            _settingsNameInput.onEndEdit.AddListener(v => { GameSettings.PlayerName = v; if (_nameInput != null) _nameInput.text = GameSettings.PlayerName; });

            UIFactory.Stepper(col, L.T("Language", "Dil"), 0, 1, (int)L.Current, 1, v => v == 1 ? "Türkçe" : "English", v => L.Current = (Language)v);
            UIFactory.Caption(col, L.T("sound", "ses"));
            UIFactory.Slider(col, L.T("Everything", "Genel"), GameSettings.MasterVolume, v => GameSettings.MasterVolume = v);
            UIFactory.Slider(col, L.T("The room", "Ortam"), GameSettings.MusicVolume, v => GameSettings.MusicVolume = v);
            UIFactory.Slider(col, L.T("Wood and hands", "Tahta ve eller"), GameSettings.SfxVolume, v => GameSettings.SfxVolume = v);
            UIFactory.Slider(col, L.T("Look sensitivity", "Bakış hassasiyeti"), Mathf.InverseLerp(0.3f, 2f, GameSettings.MouseSensitivity), v => GameSettings.MouseSensitivity = Mathf.Lerp(0.3f, 2f, v));

            var names = QualitySettings.names;
            UIFactory.Stepper(col, L.T("Picture quality", "Görüntü kalitesi"), 0, Mathf.Max(0, names.Length - 1), Mathf.Clamp(GameSettings.Quality, 0, Mathf.Max(0, names.Length - 1)), 1,
                v => names.Length > 0 ? names[v].ToLowerInvariant() : v.ToString(), v => { GameSettings.Quality = v; GameSettings.ApplyDisplay(); });
            UIFactory.Toggle(col, L.T("Fullscreen", "Tam ekran"), GameSettings.Fullscreen, v => { GameSettings.Fullscreen = v; GameSettings.ApplyDisplay(); });
            UIFactory.Caption(col, L.T("for testing", "test için"));
            UIFactory.Toggle(col, L.T("New identity every launch (two copies on one PC)", "Her açılışta yeni kimlik (tek PC'de iki kopya)"), GameSettings.UniqueProfilePerLaunch, v => GameSettings.UniqueProfilePerLaunch = v);
            UIFactory.Toggle(col, L.T("Host may start alone (solo test)", "Kurucu yalnız başlatabilir (tek kişilik test)"), GameSettings.AllowSoloStart, v => { GameSettings.AllowSoloStart = v; RefreshLobby(); });
            UIFactory.LE(UIFactory.Rect("Push", col), -1, -1, -1, 1);
            var row = UIFactory.ButtonRow(col, 64);
            UIFactory.LE(UIFactory.Rect("Push", row), -1, -1, 1);
            UIFactory.Button(row, L.T("Done", "Tamam"), CloseSettings, ButtonStyle.Primary, 40);
            UIFactory.OnPaper = false;
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
