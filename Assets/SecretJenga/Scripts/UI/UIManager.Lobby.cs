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
            public GameObject Filled, Empty;
            public Image AvatarFill;
            public Text AvatarLetter, Name, Tags, Status;
            public Image StatusBg, Background;
        }

        readonly List<SlotRefs> _slots = new List<SlotRefs>();
        readonly List<Image> _swatches = new List<Image>();
        Text _lobbyRoomName, _lobbyCode, _lobbyCount, _lobbyStatus, _lobbyRules;
        UIFactory.StepperRef _lTarget, _lStart, _lTurn;
        GameObject _hostSettings;
        Button _readyButton, _startButton;
        bool _settingStepper;

        void BuildLobby()
        {
            var g = NewScreen(View.Lobby, "Lobby");

            // ---------------- left: players
            var left = CenterCard(g, "PlayersCard", new Vector2(780, 860), new Vector2(-410, 0));
            var head = UIFactory.Rect("Head", left);
            UIFactory.LE(head, 70);
            UIFactory.HLayout(head, 10, 0, TextAnchor.MiddleLeft, false);
            UIFactory.LE(UIFactory.Text(head, "Players", 46, Palette.Cream, TextAnchor.MiddleLeft, FontStyle.Bold), -1, -1, 1);
            _lobbyCount = UIFactory.Text(head, "1 / 4", 34, Palette.Accent, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.LE(_lobbyCount, -1, 160);

            for (int i = 0; i < GameRules.MaxPlayers; i++) _slots.Add(BuildSlot(left));

            UIFactory.Divider(left);
            var cl = UIFactory.Text(left, "Your color", 26, Palette.Paper, TextAnchor.MiddleLeft);
            UIFactory.LE(cl, 34);
            var sw = UIFactory.Rect("Swatches", left);
            UIFactory.LE(sw, 70);
            UIFactory.HLayout(sw, 12, 0, TextAnchor.MiddleCenter, false);
            for (int i = 0; i < Palette.PlayerColors.Length; i++)
            {
                int idx = i;
                var b = UIFactory.IconButton(sw, UIFactory.CircleSprite, () => PickColor(idx), Palette.Cream, Palette.PlayerColors[i], 62);
                _swatches.Add(b.transform.GetChild(0).GetComponent<Image>());
            }

            // ---------------- right: room
            var right = CenterCard(g, "RoomCard", new Vector2(780, 860), new Vector2(410, 0));
            _lobbyRoomName = UIFactory.Text(right, "Room", 42, Palette.Cream, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.LE(_lobbyRoomName, 60);

            var codeBox = UIFactory.Image(right, "CodeBox", UIFactory.RoundedSprite, new Color(0, 0, 0, 0.3f));
            UIFactory.LE(codeBox, 120);
            var ch = UIFactory.HLayout(codeBox, 16, 0, TextAnchor.MiddleCenter, false);
            ch.padding = new RectOffset(30, 20, 10, 10);
            var codeCol = UIFactory.Rect("CodeCol", codeBox.transform);
            UIFactory.LE(codeCol, -1, -1, 1);
            UIFactory.VLayout(codeCol, 0, 0, TextAnchor.MiddleLeft);
            UIFactory.LE(UIFactory.Text(codeCol, "ROOM CODE", 22, Palette.Muted, TextAnchor.MiddleLeft, FontStyle.Bold), 28);
            _lobbyCode = UIFactory.Text(codeCol, "------", 56, Palette.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.LE(_lobbyCode, 66);
            UIFactory.IconButton(codeBox.transform, A.iconCopy, CopyCode, Palette.Accent, Palette.WalnutDark, 78);

            var hs = UIFactory.Rect("HostSettings", right);
            UIFactory.VLayout(hs, 8, 0);
            UIFactory.LE(hs, 220);
            _hostSettings = hs.gameObject;
            _lStart = UIFactory.Stepper(hs, "Starting layers", GameRules.MinStartLayers, GameRules.MaxStartLayers, GameRules.DefaultStartLayers, 1, v => v.ToString(), _ => PushSettings());
            _lTarget = UIFactory.Stepper(hs, "Target height", GameRules.MinTargetHeight, GameRules.MaxTargetHeight, GameRules.DefaultTargetHeight, 1, v => v.ToString(), _ => PushSettings());
            _lTurn = UIFactory.Stepper(hs, "Turn time", 10, 90, GameRules.DefaultTurnSeconds, 5, v => v + " s", _ => PushSettings());

            _lobbyRules = UIFactory.Text(right, "", 26, Palette.Paper);
            UIFactory.LE(_lobbyRules, 150);

            _lobbyStatus = UIFactory.Text(right, "", 26, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Italic);
            UIFactory.LE(_lobbyStatus, 40);

            var row = UIFactory.Rect("Buttons", right);
            UIFactory.LE(row, 90);
            UIFactory.HLayout(row, 18, 0);
            UIFactory.Button(row, "Leave", LeaveRoom, ButtonStyle.Ghost, 30);
            _readyButton = UIFactory.Button(row, "Ready", ToggleReady, ButtonStyle.Good, 34, A.iconCheck);
            _startButton = UIFactory.Button(row, "Start Game", StartGame, ButtonStyle.Primary, 34, A.iconStar);
        }

        SlotRefs BuildSlot(Transform parent)
        {
            var s = new SlotRefs();
            s.Background = UIFactory.Image(parent, "Slot", UIFactory.RoundedSprite, new Color(1, 1, 1, 0.06f));
            UIFactory.LE(s.Background, 112);

            var filled = UIFactory.Rect("Filled", s.Background.transform);
            UIFactory.Stretch(filled);
            var h = UIFactory.HLayout(filled, 20, 0, TextAnchor.MiddleLeft, false);
            h.padding = new RectOffset(18, 18, 12, 12);
            h.childForceExpandHeight = false;
            var avatar = UIFactory.Avatar(filled, "?", Color.gray, 84);
            s.AvatarFill = avatar.transform.GetChild(0).GetComponent<Image>();
            s.AvatarLetter = s.AvatarFill.GetComponentInChildren<Text>();
            var nameCol = UIFactory.Rect("NameCol", filled);
            UIFactory.LE(nameCol, 84, -1, 1);
            UIFactory.VLayout(nameCol, 2, 0, TextAnchor.MiddleLeft);
            s.Name = UIFactory.Text(nameCol, "", 34, Palette.Cream, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.LE(s.Name, 44);
            s.Tags = UIFactory.Text(nameCol, "", 22, Palette.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.LE(s.Tags, 28);
            s.StatusBg = UIFactory.Image(filled, "Status", UIFactory.PillSprite, Palette.Good);
            UIFactory.LE(s.StatusBg, 50, 190);
            s.Status = UIFactory.Text(s.StatusBg.transform, "READY", 24, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(s.Status.rectTransform);
            s.Filled = filled.gameObject;

            var empty = UIFactory.Text(s.Background.transform, "Waiting for a player...", 28, new Color(1, 1, 1, 0.3f), TextAnchor.MiddleCenter, FontStyle.Italic);
            UIFactory.Stretch(empty.rectTransform);
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
            _lobbyCode.text = string.IsNullOrEmpty(code) ? "......" : code;

            ulong me = game.NetworkManager.LocalClientId;
            var used = new HashSet<int>();
            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                s.Background.gameObject.SetActive(i < max);
                bool has = i < players.Count;
                s.Filled.SetActive(has);
                s.Empty.SetActive(!has);
                if (!has) continue;
                var p = players[i];
                used.Add(p.ColorIndex);
                s.AvatarFill.color = p.Color;
                var n = p.Name.ToString();
                s.AvatarLetter.text = n.Length > 0 ? n.Substring(0, 1).ToUpperInvariant() : "?";
                s.Name.text = n;
                var tags = new List<string>();
                if (p.IsHost) tags.Add("HOST");
                if (p.ClientId == me) tags.Add("YOU");
                s.Tags.text = string.Join("  -  ", tags);
                s.Background.color = p.ClientId == me ? new Color(Palette.Accent.r, Palette.Accent.g, Palette.Accent.b, 0.14f) : new Color(1, 1, 1, 0.06f);
                if (!p.IsConnected) { s.Status.text = "OFFLINE"; s.StatusBg.color = Palette.Muted; }
                else if (p.IsHost) { s.Status.text = "HOST"; s.StatusBg.color = Palette.AccentDark; }
                else if (p.IsReady) { s.Status.text = "READY"; s.StatusBg.color = Palette.Good; }
                else { s.Status.text = "NOT READY"; s.StatusBg.color = Palette.Bad; }
            }

            var mine = game.LocalPlayer;
            for (int i = 0; i < _swatches.Count; i++)
            {
                bool takenByOther = used.Contains(i) && (!mine.HasValue || mine.Value.ColorIndex != i);
                bool isMine = mine.HasValue && mine.Value.ColorIndex == i;
                var c = Palette.PlayerColors[i];
                _swatches[i].color = takenByOther ? new Color(c.r, c.g, c.b, 0.25f) : c;
                var ring = _swatches[i].transform.parent.GetComponent<Image>();
                ring.color = isMine ? Color.white : new Color(1, 1, 1, 0.25f);
                ring.transform.localScale = isMine ? Vector3.one * 1.12f : Vector3.one;
            }

            bool host = game.IsServer;
            _settingStepper = true;
            _lStart.Set(game.StartLayers.Value, false);
            _lTarget.Set(game.TargetHeight.Value, false);
            _lTurn.Set(game.TurnSeconds.Value, false);
            _lStart.Interactable = host;
            _lTarget.Interactable = host;
            _lTurn.Interactable = host;
            _settingStepper = false;

            int moves = (game.TargetHeight.Value - game.StartLayers.Value) * 3;
            _lobbyRules.text = $"Build the tower from {game.StartLayers.Value} to <b>{game.TargetHeight.Value}</b> layers (~{moves} moves) without letting it fall.\n" +
                               $"Each player gets a <color=#F2A13A>secret mission</color>. {game.TurnSeconds.Value}s per turn.";

            _startButton.gameObject.SetActive(host);
            _readyButton.gameObject.SetActive(!host);
            if (host)
            {
                bool ok = game.CanStart(out var reason);
                _startButton.interactable = ok;
                _lobbyStatus.text = ok ? (game.ConnectedCount < 2 ? "Solo test mode is ON." : "Everyone is ready!") : reason;
            }
            else
            {
                bool ready = mine.HasValue && mine.Value.IsReady;
                UIFactory.SetLabel(_readyButton, ready ? "Not Ready" : "Ready");
                _readyButton.image.color = ready ? Palette.Muted : Color.white;
                _lobbyStatus.text = ready ? "Waiting for the host to start..." : "Press Ready when you are set.";
            }
        }

        void UpdateLobbyLive() { }

        void PushSettings()
        {
            if (_settingStepper || _game == null || !_game.IsServer) return;
            // keep the target above the starting height
            if (_lTarget.Value < _lStart.Value + 2) { _settingStepper = true; _lTarget.Set(_lStart.Value + 2, false); _settingStepper = false; }
            _game.UpdateSettingsServerRpc(_lTarget.Value, _lStart.Value, _lTurn.Value);
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
            Toast("Room code copied to clipboard!", Palette.Good);
            UITween.Pop(_lobbyCode.transform, 1.2f);
        }
    }
}
