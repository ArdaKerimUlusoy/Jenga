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
        UIFactory.StepperRef _lRequired, _lStart, _lTurn;
        Button _readyButton, _startButton;
        bool _settingStepper;

        static readonly string[] Numerals = { "I.", "II.", "III.", "IV.", "V.", "VI." };

        void BuildLobby()
        {
            var g = NewScreen(View.Lobby, "Lobby");

            // ---------------- left sheet: the seating list
            var left = CenterCard(g, "Seating", new Vector2(780, 900), new Vector2(-420, 0));
            UIFactory.Caption(left, "seating, tonight");
            var head = UIFactory.Rect("Head", left);
            UIFactory.LE(head, 70);
            UIFactory.HLayout(head, 10, 0, TextAnchor.MiddleLeft, false);
            UIFactory.LE(UIFactory.Write(head, FontKind.Title, "At the Table", 56, Ink.Black, TextAnchor.MiddleLeft), -1, -1, 1);
            _lobbyCount = UIFactory.Write(head, FontKind.Type, "1 / 5", 30, Ink.Red, TextAnchor.MiddleRight);
            UIFactory.LE(_lobbyCount, -1, 120);
            UIFactory.Divider(left);

            for (int i = 0; i < GameRules.MaxPlayers; i++) _slots.Add(BuildSlot(left, i));

            UIFactory.LE(UIFactory.Rect("Push", left), -1, -1, -1, 1);
            UIFactory.Caption(left, "your ink");
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
            UIFactory.Caption(right, "the room");
            _lobbyRoomName = UIFactory.Write(right, FontKind.Title, "Table", 48, Ink.Black, TextAnchor.MiddleLeft);
            _lobbyRoomName.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.LE(_lobbyRoomName, 62);

            var codeRow = UIFactory.Rect("CodeRow", right);
            UIFactory.LE(codeRow, 104);
            UIFactory.HLayout(codeRow, 16, 0, TextAnchor.MiddleLeft, false);
            var codeCol = UIFactory.Rect("CodeCol", codeRow);
            UIFactory.LE(codeCol, -1, -1, 1);
            UIFactory.VLayout(codeCol, 0, 0, TextAnchor.MiddleLeft);
            _lobbyCodeLabel = UIFactory.Caption(codeCol, "room code");
            _lobbyCode = UIFactory.Write(codeCol, FontKind.Type, "------", 48, Ink.Red, TextAnchor.MiddleLeft);
            _lobbyCode.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.LE(_lobbyCode, 62);
            UIFactory.Button(codeRow, "copy", CopyCode, ButtonStyle.Secondary, 28);
            _lobbyAddresses = UIFactory.Write(right, FontKind.Body, "", 20, Ink.Faded, TextAnchor.UpperLeft, FontStyle.Italic);
            UIFactory.LE(_lobbyAddresses, 52);
            UIFactory.Divider(right);

            var hs = UIFactory.Rect("HostSettings", right);
            UIFactory.VLayout(hs, 2, 0);
            UIFactory.LE(hs, 156);
            _lRequired = UIFactory.Stepper(hs, "Turns the tower must survive", GameRules.MinRequiredTurns, GameRules.MaxRequiredTurns, GameRules.DefaultRequiredTurns, 1, v => v.ToString(), _ => PushSettings());
            _lStart = UIFactory.Stepper(hs, "Floors at the start", GameRules.MinStartLayers, GameRules.MaxStartLayers, GameRules.DefaultStartLayers, 1, v => v.ToString(), _ => PushSettings());
            _lTurn = UIFactory.Stepper(hs, "Time for each turn", 0, GameRules.MaxTurnSeconds, GameRules.DefaultTurnSeconds, 20, FormatTimer, _ => PushSettings());
            UIFactory.Divider(right);

            _lobbyRules = UIFactory.Write(right, FontKind.Body, "", 23, Ink.Black, TextAnchor.UpperLeft);
            UIFactory.LE(_lobbyRules, 150);

            UIFactory.LE(UIFactory.Rect("Push", right), -1, -1, -1, 1);
            _lobbyStatus = UIFactory.Write(right, FontKind.Type, "", 21, Ink.Red, TextAnchor.MiddleLeft);
            UIFactory.LE(_lobbyStatus, 34);

            var row = UIFactory.ButtonRow(right, 72, 40);
            UIFactory.Button(row, "leave", LeaveRoom, ButtonStyle.Ghost, 30);
            UIFactory.LE(UIFactory.Rect("Push", row), -1, -1, 1);
            _readyButton = UIFactory.Button(row, "I am ready", ToggleReady, ButtonStyle.Primary, 40);
            _startButton = UIFactory.Button(row, "Begin", StartGame, ButtonStyle.Primary, 44);
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

            var empty = UIFactory.Write(root, FontKind.Body, "an empty chair", 26, Ink.Pencil, TextAnchor.MiddleLeft, FontStyle.Italic);
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
            _lobbyCodeLabel.text = direct ? "address of this table - give it to the others" : "room code";
            _lobbyCode.text = string.IsNullOrEmpty(code) ? "......" : code;
            if (direct && game.IsServer)
            {
                var all = NetworkSessionManager.LocalAddresses();
                _lobbyAddresses.text = (all.Count > 1 ? "also: " + string.Join("   ", all.GetRange(1, all.Count - 1)) + "\n" : "") +
                                       "same PC: 127.0.0.1  -  over the internet, forward UDP " + GameSettings.DirectPort + " to this PC.";
            }
            else _lobbyAddresses.text = direct ? "You sat down at the host's table directly." : "Friends sit down with this code: Join a table > Online.";

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
                s.MarkLetter.text = n.Length > 0 ? n.Substring(0, 1).ToUpperInvariant() : "?";
                s.Name.text = n;
                var tags = new List<string> { "chair " + (p.SeatIndex + 1) };
                if (p.IsHost) tags.Add("host");
                if (p.ClientId == me) tags.Add("you");
                s.Tags.text = string.Join("  /  ", tags);
                s.Strike.gameObject.SetActive(!p.IsConnected);
                if (!p.IsConnected) { s.Status.text = "left"; s.Status.color = Ink.Faded; }
                else if (p.IsHost) { s.Status.text = "host"; s.Status.color = Ink.Faded; }
                else if (p.IsReady) { s.Status.text = "ready"; s.Status.color = Ink.Green; }
                else { s.Status.text = "not yet"; s.Status.color = Ink.Red; }
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
            _lRequired.Set(game.RequiredTurns.Value, false);
            _lStart.Set(game.StartLayers.Value, false);
            _lTurn.Set(game.TurnSeconds.Value, false);
            _lRequired.Interactable = host;
            _lStart.Interactable = host;
            _lTurn.Interactable = host;
            _settingStepper = false;

            string timer = game.TurnSeconds.Value > 0 ? $"You have {game.TurnSeconds.Value} seconds a turn." : "Nobody is timed.";
            _lobbyRules.text = $"Together, keep the tower standing for <color=#99261A>{game.RequiredTurns.Value}</color> turns. " +
                               $"Each turn, pull one block and lay it on top. Each of you is dealt a <i>secret task</i> and one <i>card</i>. {timer}";

            _startButton.gameObject.SetActive(host);
            _readyButton.gameObject.SetActive(!host);
            if (host)
            {
                bool ok = game.CanStart(out var reason);
                _startButton.interactable = ok;
                _lobbyStatus.text = ok ? (game.ConnectedCount < 2 ? "solo test - you will play alone." : "everyone is ready.") : reason;
            }
            else
            {
                bool ready = mine.HasValue && mine.Value.IsReady;
                UIFactory.SetLabel(_readyButton, ready ? "wait, not yet" : "I am ready");
                _lobbyStatus.text = ready ? "waiting for the host to begin..." : "say when you are ready.";
            }
        }

        void UpdateLobbyLive() { }

        void PushSettings()
        {
            if (_settingStepper || _game == null || !_game.IsServer) return;
            _game.UpdateSettingsServerRpc(_lRequired.Value, _lStart.Value, _lTurn.Value);
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
            Toast(_game.IsDirectSession.Value ? "Address copied." : "Code copied.", Palette.Good);
            UITween.Pop(_lobbyCode.transform, 1.15f);
        }
    }
}
