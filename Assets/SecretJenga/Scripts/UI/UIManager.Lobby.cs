using System.Collections.Generic;
using SecretJenga.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace SecretJenga.UI
{
    public partial class UIManager
    {
        class SlotRefs
        {
            public GameObject Filled, Empty, Root;
            public Image Mark;
            public Text Numeral, MarkLetter, Name, Tags, Status;
            public Image Strike;
        }

        readonly List<SlotRefs> _slots = new List<SlotRefs>();
        readonly List<Image> _swatches = new List<Image>();
        Text _lobbyRoomName, _lobbyCode, _lobbyCodeLabel, _lobbyCount, _lobbyStatus, _lobbyRules, _lobbyAddresses;
        UIFactory.StepperRef _lStart, _lTurn, _lRounds;
        Button _readyButton, _startButton, _addBotButton, _removeBotButton;
        RectTransform _botRow;
        bool _settingStepper;

        static readonly string[] Numerals = { "I.", "II.", "III.", "IV.", "V.", "VI." };

        void BuildLobby()
        {
            var g = NewScreen(View.Lobby, "Lobby");

            // ---------------- left sheet: the seating list
            var left = CenterCard(g, "Seating", new Vector2(780, 900), new Vector2(-420, 0));
            UIFactory.Caption(left, L.T("seating, tonight", "bu geceki oturma düzeni"));
            var head = UIFactory.Rect("Head", left);
            UIFactory.LE(head, 70);
            UIFactory.HLayout(head, 10, 0, TextAnchor.MiddleLeft, false);
            UIFactory.LE(UIFactory.Write(head, FontKind.Title, L.T("At the Table", "Masada"), 56, Ink.Black, TextAnchor.MiddleLeft), -1, -1, 1);
            _lobbyCount = UIFactory.Write(head, FontKind.Type, "1 / 5", 30, Ink.Red, TextAnchor.MiddleRight);
            UIFactory.LE(_lobbyCount, -1, 120);
            UIFactory.Divider(left);

            for (int i = 0; i < GameRules.MaxPlayers; i++) _slots.Add(BuildSlot(left, i));

            UIFactory.LE(UIFactory.Rect("Push", left), -1, -1, -1, 1);
            // host: seat bots so the game can be tried alone (or a table filled up)
            _botRow = UIFactory.ButtonRow(left, 62, 24);
            _addBotButton = UIFactory.Button(_botRow, L.T("+ seat a bot", "+ bot oturt"), () => { if (_game != null) _game.AddBotServerRpc(); }, ButtonStyle.Secondary, 26);
            _removeBotButton = UIFactory.Button(_botRow, L.T("- send a bot away", "- botu kaldır"), () => { if (_game != null) _game.RemoveBotServerRpc(); }, ButtonStyle.Ghost, 26);
            UIFactory.Caption(left, L.T("your ink", "mürekkebin"));
            var sw = UIFactory.Rect("Swatches", left);
            UIFactory.LE(sw, 70);
            UIFactory.HLayout(sw, 22, 0, TextAnchor.MiddleLeft, false);
            for (int i = 0; i < Palette.PlayerColors.Length; i++)
            {
                int idx = i;
                var b = UIFactory.IconButton(sw, UIFactory.BlotSprite, () => PickColor(idx), Color.clear, InkOf(Palette.PlayerColors[i]), 66);
                var ic = b.transform.GetChild(0).GetComponent<Image>();
                ic.transform.localRotation = Quaternion.Euler(0, 0, i * 47f);
                Object.Destroy(b.GetComponent<InkButtonFX>()); // the blot keeps its own colour
                _swatches.Add(ic);
            }

            // ---------------- right sheet: the room
            var right = CenterCard(g, "Room", new Vector2(780, 900), new Vector2(420, 0));
            UIFactory.Caption(right, L.T("the room", "oda"));
            _lobbyRoomName = UIFactory.Write(right, FontKind.Title, "Table", 48, Ink.Black, TextAnchor.MiddleLeft);
            _lobbyRoomName.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.LE(_lobbyRoomName, 62);

            var codeRow = UIFactory.Rect("CodeRow", right);
            UIFactory.LE(codeRow, 104);
            UIFactory.HLayout(codeRow, 16, 0, TextAnchor.MiddleLeft, false);
            var codeCol = UIFactory.Rect("CodeCol", codeRow);
            UIFactory.LE(codeCol, -1, -1, 1);
            UIFactory.VLayout(codeCol, 0, 0, TextAnchor.MiddleLeft);
            _lobbyCodeLabel = UIFactory.Caption(codeCol, L.T("room code", "oda kodu"));
            _lobbyCode = UIFactory.Write(codeCol, FontKind.Type, "------", 48, Ink.Red, TextAnchor.MiddleLeft);
            _lobbyCode.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.LE(_lobbyCode, 62);
            UIFactory.Button(codeRow, L.T("copy", "kopyala"), CopyCode, ButtonStyle.Secondary, 28);
            _lobbyAddresses = UIFactory.Write(right, FontKind.Body, "", 20, Ink.Faded, TextAnchor.UpperLeft, FontStyle.Italic);
            UIFactory.LE(_lobbyAddresses, 52);
            UIFactory.Divider(right);

            var hs = UIFactory.Rect("HostSettings", right);
            UIFactory.VLayout(hs, 2, 0);
            UIFactory.LE(hs, 156);
            _lRounds = UIFactory.Stepper(hs, L.T("Rounds in a match", "Maçtaki el sayısı"), GameRules.MinMatchRounds, GameRules.MaxMatchRounds, GameRules.DefaultMatchRounds, 1, v => v.ToString(), _ => PushSettings());
            _lStart = UIFactory.Stepper(hs, L.T("Floors at the start", "Başlangıç kat sayısı"), GameRules.MinStartLayers, GameRules.MaxStartLayers, GameRules.DefaultStartLayers, 1, v => v.ToString(), _ => PushSettings());
            _lTurn = UIFactory.Stepper(hs, L.T("Time for each turn", "Tur başına süre"), 0, GameRules.MaxTurnSeconds, GameRules.DefaultTurnSeconds, 20, FormatTimer, _ => PushSettings());
            UIFactory.Divider(right);

            _lobbyRules = UIFactory.Write(right, FontKind.Body, "", 23, Ink.Black, TextAnchor.UpperLeft);
            UIFactory.LE(_lobbyRules, 200);

            UIFactory.LE(UIFactory.Rect("Push", right), -1, -1, -1, 1);
            _lobbyStatus = UIFactory.Write(right, FontKind.Type, "", 21, Ink.Red, TextAnchor.MiddleLeft);
            UIFactory.LE(_lobbyStatus, 34);

            var row = UIFactory.ButtonRow(right, 72, 40);
            UIFactory.Button(row, L.T("leave", "ayrıl"), LeaveRoom, ButtonStyle.Ghost, 30);
            UIFactory.LE(UIFactory.Rect("Push", row), -1, -1, 1);
            _readyButton = UIFactory.Button(row, L.T("I am ready", "Hazırım"), ToggleReady, ButtonStyle.Primary, 40);
            _startButton = UIFactory.Button(row, L.T("Begin", "Başla"), StartGame, ButtonStyle.Primary, 44);
            UIFactory.OnPaper = false;
        }

        /// <summary>Player colours are tuned for the dark room; written on paper they need to be inkier.</summary>
        static Color InkOf(Color c) => new Color(c.r * 0.72f, c.g * 0.72f, c.b * 0.72f, 1f);

        SlotRefs BuildSlot(Transform parent, int index)
        {
            var s = new SlotRefs();
            var root = UIFactory.Rect("Seat" + index, parent);
            UIFactory.LE(root, 84);
            s.Root = root.gameObject;

            s.Numeral = UIFactory.Write(root, FontKind.Title, Numerals[index], 30, Ink.Faded, TextAnchor.MiddleLeft);
            UIFactory.Place(s.Numeral.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(64, 60));

            var filled = UIFactory.Rect("Filled", root);
            UIFactory.Stretch(filled, 70, 0, 0, 0);
            var h = UIFactory.HLayout(filled, 18, 0, TextAnchor.MiddleLeft, false);
            h.childForceExpandHeight = false;
            s.Mark = UIFactory.Avatar(filled, "?", Color.gray, 58);
            s.MarkLetter = s.Mark.GetComponentInChildren<Text>();
            var nameCol = UIFactory.Rect("NameCol", filled);
            UIFactory.LE(nameCol, 76, -1, 1);
            UIFactory.VLayout(nameCol, 0, 0, TextAnchor.MiddleLeft);
            s.Name = UIFactory.Write(nameCol, FontKind.Body, "", 34, Ink.Black, TextAnchor.MiddleLeft);
            UIFactory.LE(s.Name, 42);
            s.Tags = UIFactory.Write(nameCol, FontKind.Type, "", 18, Ink.Faded, TextAnchor.MiddleLeft);
            UIFactory.LE(s.Tags, 24);
            s.Status = UIFactory.Write(filled, FontKind.Type, "", 22, Ink.Black, TextAnchor.MiddleRight);
            UIFactory.LE(s.Status, 40, 150);
            s.Strike = UIFactory.Image(filled, "Strike", UIFactory.StrokeSprite, Ink.Red, false);
            s.Strike.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UIFactory.Place(s.Strike.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(70, 2), new Vector2(330, 10));
            s.Filled = filled.gameObject;

            var empty = UIFactory.Write(root, FontKind.Body, L.T("an empty chair", "boş sandalye"), 26, Ink.Pencil, TextAnchor.MiddleLeft, FontStyle.Italic);
            UIFactory.Stretch(empty.rectTransform, 76, 0, 0, 0);
            s.Empty = empty.gameObject;
            return s;
        }

        void RefreshLobby()
        {
            var game = _game;
            if (game == null || !game.IsSpawned || _lobbyCode == null) return;

            var players = game.PlayersSorted();
            int max = game.MaxPlayers.Value;
            _lobbyCount.text = $"{game.ConnectedCount} / {max}";
            _lobbyRoomName.text = game.RoomName.Value.ToString();
            var code = game.RoomCode.Value.ToString();
            bool direct = game.IsDirectSession.Value;
            _lobbyCodeLabel.text = direct ? L.T("address of this table - give it to the others", "bu masanın adresi - diğerlerine ver") : L.T("room code", "oda kodu");
            _lobbyCode.text = string.IsNullOrEmpty(code) ? "......" : code;
            if (direct && game.IsServer)
            {
                var all = NetworkSessionManager.LocalAddresses();
                _lobbyAddresses.text = (all.Count > 1 ? L.T("also: ", "ayrıca: ") + string.Join("   ", all.GetRange(1, all.Count - 1)) + "\n" : "") +
                                       L.T("same PC: 127.0.0.1  -  over the internet, forward UDP " + GameSettings.DirectPort + " to this PC.",
                                           "aynı PC: 127.0.0.1  -  internetten: UDP " + GameSettings.DirectPort + " portunu bu PC'ye yönlendir.");
            }
            else _lobbyAddresses.text = direct ? L.T("You sat down at the host's table directly.", "Kurucunun masasına doğrudan oturdun.") : L.T("Friends sit down with this code: Join a table > Online.", "Arkadaşların bu kodla oturur: Masaya katıl > Çevrimiçi.");

            ulong me = game.NetworkManager.LocalClientId;
            var used = new HashSet<int>();
            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                s.Root.SetActive(i < max);
                bool has = i < players.Count;
                s.Filled.SetActive(has);
                s.Empty.SetActive(!has);
                s.Numeral.color = has ? Ink.Black : Ink.Pencil;
                if (!has) continue;
                var p = players[i];
                used.Add(p.ColorIndex);
                s.Mark.color = InkOf(p.Color);
                var n = p.Name.ToString();
                s.MarkLetter.text = n.Length > 0 ? L.Upper(n.Substring(0, 1)) : "?";
                s.Name.text = n;
                var tags = new List<string> { L.T("chair ", "sandalye ") + (p.SeatIndex + 1) };
                if (p.IsHost) tags.Add(L.T("host", "kurucu"));
                if (p.IsBot) tags.Add("bot");
                if (p.ClientId == me) tags.Add(L.T("you", "sen"));
                if (game.RoundNumber.Value > 0) tags.Add(L.T($"{p.Score} pts", $"{p.Score} puan"));
                s.Tags.text = string.Join("  /  ", tags);
                s.Strike.gameObject.SetActive(!p.IsConnected);
                if (!p.IsConnected) { s.Status.text = L.T("left", "ayrıldı"); s.Status.color = Ink.Faded; }
                else if (p.IsHost) { s.Status.text = L.T("host", "kurucu"); s.Status.color = Ink.Faded; }
                else if (p.IsBot) { s.Status.text = L.T("bot", "bot"); s.Status.color = Ink.Green; }
                else if (p.IsReady) { s.Status.text = L.T("ready", "hazır"); s.Status.color = Ink.Green; }
                else { s.Status.text = L.T("not yet", "henüz değil"); s.Status.color = Ink.Red; }
            }

            var mine = game.LocalPlayer;
            for (int i = 0; i < _swatches.Count; i++)
            {
                bool takenByOther = used.Contains(i) && (!mine.HasValue || mine.Value.ColorIndex != i);
                bool isMine = mine.HasValue && mine.Value.ColorIndex == i;
                var c = InkOf(Palette.PlayerColors[i]);
                _swatches[i].color = takenByOther ? new Color(c.r, c.g, c.b, 0.18f) : c;
                _swatches[i].transform.parent.localScale = isMine ? Vector3.one * 1.25f : Vector3.one * 0.85f;
            }

            bool host = game.IsServer;
            _settingStepper = true;
            _lStart.Set(game.StartLayers.Value, false);
            _lTurn.Set(game.TurnSeconds.Value, false);
            _lRounds.Set(game.MatchRounds.Value, false);
            _lRounds.Interactable = host;
            _lStart.Interactable = host;
            _lTurn.Interactable = host;
            _settingStepper = false;

            string timer = game.TurnSeconds.Value > 0
                ? L.T($"You have {game.TurnSeconds.Value} seconds a turn.", $"Her tur için {game.TurnSeconds.Value} saniyen var.")
                : L.T("Nobody is timed.", "Süre sınırı yok.");
            _lobbyRules.text = L.T(
                $"Pull one block, lay it on top; a round lasts until the tower falls. <color=#99261A>Whoever drops it: -3</color>, everyone else +3. Your <i>secret task</i> adds +1..+4. {game.MatchRounds.Value} round(s) - the highest total wins the match. {timer}",
                $"Bir blok çek, en üste koy; el, kule yıkılana kadar sürer. <color=#99261A>Kuleyi yıkan -3</color>, diğer herkes +3. <i>Gizli görevin</i> +1..+4 ekler. {game.MatchRounds.Value} el - en yüksek toplam maçı kazanır. {timer}");

            _botRow.gameObject.SetActive(host);
            if (host)
            {
                _addBotButton.interactable = game.ConnectedCount < max && players.Count < SeatLayout.SeatCount;
                _removeBotButton.interactable = game.BotCount > 0;
            }

            _startButton.gameObject.SetActive(host);
            _readyButton.gameObject.SetActive(!host);
            if (host)
            {
                bool ok = game.CanStart(out var reason);
                _startButton.interactable = ok;
                _lobbyStatus.text = ok ? (game.ConnectedCount < 2 ? L.T("solo test - you will play alone.", "tek kişilik test - yalnız oynayacaksın.") : L.T("everyone is ready.", "herkes hazır.")) : L.Resolve(reason);
            }
            else
            {
                bool ready = mine.HasValue && mine.Value.IsReady;
                UIFactory.SetLabel(_readyButton, ready ? L.T("wait, not yet", "dur, henüz değil") : L.T("I am ready", "Hazırım"));
                _lobbyStatus.text = ready ? L.T("waiting for the host to begin...", "kurucunun başlatması bekleniyor...") : L.T("say when you are ready.", "hazır olunca söyle.");
            }
        }

        void UpdateLobbyLive() { }

        void PushSettings()
        {
            if (_settingStepper || _game == null || !_game.IsServer) return;
            _game.UpdateSettingsServerRpc(_lStart.Value, _lTurn.Value, _lRounds.Value);
        }

        void PickColor(int idx)
        {
            if (_game == null) return;
            GameSettings.PreferredColor = idx;
            _game.SetColorServerRpc((byte)idx);
        }

        void ToggleReady()
        {
            if (_game == null) return;
            var me = _game.LocalPlayer;
            _game.SetReadyServerRpc(!(me.HasValue && me.Value.IsReady));
        }

        void StartGame()
        {
            if (_game == null) return;
            _game.StartGameServerRpc();
        }

        void CopyCode()
        {
            if (_game == null) return;
            GUIUtility.systemCopyBuffer = _game.RoomCode.Value.ToString();
            Toast(_game.IsDirectSession.Value ? L.T("Address copied.", "Adres kopyalandı.") : L.T("Code copied.", "Kod kopyalandı."), Palette.Good);
            UITween.Pop(_lobbyCode.transform, 1.15f);
        }
    }
}
