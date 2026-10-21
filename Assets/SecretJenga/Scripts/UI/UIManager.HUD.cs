using System.Collections.Generic;
using SecretJenga.Game;
using SecretJenga.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace SecretJenga.UI
{
    public partial class UIManager
    {
        // top bar
        Text _hudHeight, _hudTurn, _hudTimer, _hudCountdown, _hudBanner, _hudHint, _hudStabilityText;
        RectTransform _hudProgressFill, _hudStabilityFill;
        Image _hudStabilityFillImg, _hudTimerBg;
        CanvasGroup _bannerGroup, _countdownGroup;
        // players
        RectTransform _hudPlayers;
        readonly List<(ulong id, Image bg, Image avatar, Text name, Text state)> _hudRows = new List<(ulong, Image, Image, Text, Text)>();
        string _hudRosterKey = "";
        // mission
        Image _missionIcon, _missionHeaderBg;
        Text _missionTitle, _missionDesc, _missionProgress, _missionStatus, _missionHeader;
        RectTransform _missionBody, _missionCard;
        bool _missionHidden;
        int _lastTick = -1;
        int _lastCountdown = -1;

        void BuildHud()
        {
            var g = NewScreen(View.Hud, "HUD");
            g.blocksRaycasts = true;

            // ---------------- height / turn / timer (top center)
            var top = UIFactory.Card(g.transform, "TopBar", new Color(0.18f, 0.11f, 0.07f, 0.92f));
            UIFactory.Place((RectTransform)top.transform.parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -20), new Vector2(760, 130));
            var topCol = UIFactory.Rect("Col", top.transform);
            UIFactory.Stretch(topCol, 30, 14, 30, 14);
            UIFactory.VLayout(topCol, 8, 0);
            var row1 = UIFactory.Rect("Row1", topCol);
            UIFactory.LE(row1, 50);
            UIFactory.HLayout(row1, 10, 0, TextAnchor.MiddleLeft, false);
            UIFactory.LE(UIFactory.Text(row1, "HEIGHT", 22, Palette.Muted, TextAnchor.MiddleLeft, FontStyle.Bold), -1, 100);
            _hudHeight = UIFactory.Text(row1, "10 / 15", 40, Palette.Cream, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.LE(_hudHeight, -1, -1, 1);
            _hudTurn = UIFactory.Text(row1, "TURN 1", 26, Palette.Paper, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.LE(_hudTurn, -1, 200);
            var bar = UIFactory.Image(topCol, "Progress", UIFactory.PillSprite, new Color(0, 0, 0, 0.4f));
            UIFactory.LE(bar, 26);
            var fill = UIFactory.Image(bar.transform, "Fill", UIFactory.PillSprite, Palette.Accent);
            _hudProgressFill = fill.rectTransform;
            _hudProgressFill.anchorMin = Vector2.zero;
            _hudProgressFill.anchorMax = new Vector2(0.1f, 1);
            _hudProgressFill.offsetMin = _hudProgressFill.offsetMax = Vector2.zero;
            var star = UIFactory.Image(bar.transform, "Goal", A.iconStar, Palette.Accent, false);
            UIFactory.Place(star.rectTransform, new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(46, 46));

            _hudTimerBg = UIFactory.Image(g.transform, "Timer", UIFactory.PillSprite, new Color(0.18f, 0.11f, 0.07f, 0.92f));
            UIFactory.Place(_hudTimerBg.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -160), new Vector2(220, 70));
            var th = UIFactory.HLayout(_hudTimerBg, 10, 0, TextAnchor.MiddleCenter, false);
            th.padding = new RectOffset(20, 20, 8, 8);
            var clock = UIFactory.Image(_hudTimerBg.transform, "Clock", A.iconClock, Palette.Cream, false);
            UIFactory.LE(clock, 40, 40);
            _hudTimer = UIFactory.Text(_hudTimerBg.transform, "30", 40, Palette.Cream, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.LE(_hudTimer, -1, 90);

            // ---------------- players (top left)
            _hudPlayers = UIFactory.Rect("Players", g.transform);
            UIFactory.Place(_hudPlayers, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -24), new Vector2(380, 360));
            var pv = UIFactory.VLayout(_hudPlayers, 8, 0, TextAnchor.UpperLeft);
            pv.childForceExpandHeight = false;

            // ---------------- secret mission (right)
            var mc = UIFactory.Card(g.transform, "MissionCard", new Color(0.16f, 0.10f, 0.06f, 0.95f));
            _missionCard = (RectTransform)mc.transform.parent;
            UIFactory.Place(_missionCard, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-24, 60), new Vector2(440, 400));
            var mcol = UIFactory.Rect("Col", mc.transform);
            UIFactory.Stretch(mcol);
            UIFactory.VLayout(mcol, 8, 0);
            _missionHeaderBg = UIFactory.Image(mcol, "Header", UIFactory.RoundedSprite, Palette.AccentDark);
            UIFactory.LE(_missionHeaderBg, 60);
            var hh = UIFactory.HLayout(_missionHeaderBg, 8, 0, TextAnchor.MiddleLeft, false);
            hh.padding = new RectOffset(18, 10, 6, 6);
            var eye = UIFactory.Image(_missionHeaderBg.transform, "Eye", A.iconSecret, Palette.Cream, false);
            UIFactory.LE(eye, 34, 40);
            _missionHeader = UIFactory.Text(_missionHeaderBg.transform, "SECRET MISSION", 24, Palette.Cream, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.LE(_missionHeader, -1, -1, 1);
            var hide = UIFactory.Button(_missionHeaderBg.transform, "Hide", ToggleMissionHidden, ButtonStyle.Ghost, 20);
            UIFactory.LE(hide, 44, 90);

            _missionBody = UIFactory.Rect("Body", mcol);
            UIFactory.LE(_missionBody, 320);
            UIFactory.VLayout(_missionBody, 8, 22);
            var titleRow = UIFactory.Rect("TitleRow", _missionBody);
            UIFactory.LE(titleRow, 80);
            UIFactory.HLayout(titleRow, 14, 0, TextAnchor.MiddleLeft, false);
            var iconBg = UIFactory.Image(titleRow, "IconBg", UIFactory.CircleSprite, new Color(1, 1, 1, 0.1f), false);
            UIFactory.LE(iconBg, 76, 76);
            _missionIcon = UIFactory.Image(iconBg.transform, "Icon", A.iconStar, Palette.Accent, false);
            _missionIcon.preserveAspect = true;
            UIFactory.Stretch(_missionIcon.rectTransform, 14, 14, 14, 14);
            _missionTitle = UIFactory.Text(titleRow, "Dealing...", 32, Palette.Cream, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.LE(_missionTitle, -1, -1, 1);
            _missionDesc = UIFactory.Text(_missionBody, "", 24, Palette.Paper, TextAnchor.UpperLeft);
            UIFactory.LE(_missionDesc, 110);
            var pr = UIFactory.Rect("ProgressRow", _missionBody);
            UIFactory.LE(pr, 46);
            UIFactory.HLayout(pr, 10, 0, TextAnchor.MiddleLeft, false);
            _missionProgress = UIFactory.Text(pr, "0 / 1", 26, Palette.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.LE(_missionProgress, -1, -1, 1);
            var stBg = UIFactory.Image(pr, "Status", UIFactory.PillSprite, new Color(1, 1, 1, 0.1f));
            UIFactory.LE(stBg, 42, 180);
            _missionStatus = UIFactory.Text(stBg.transform, "IN PROGRESS", 20, Palette.Cream, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(_missionStatus.rectTransform);

            // ---------------- stability (bottom left)
            var stab = UIFactory.Image(g.transform, "Stability", UIFactory.RoundedSprite, new Color(0.16f, 0.10f, 0.06f, 0.9f));
            UIFactory.Place(stab.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 24), new Vector2(380, 100));
            var scol = UIFactory.VLayout(stab, 6, 16);
            scol.padding = new RectOffset(20, 20, 12, 12);
            _hudStabilityText = UIFactory.Text(stab.transform, "TOWER: STABLE", 22, Palette.Cream, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.LE(_hudStabilityText, 28);
            var sbar = UIFactory.Image(stab.transform, "Bar", UIFactory.PillSprite, new Color(0, 0, 0, 0.4f));
            UIFactory.LE(sbar, 22);
            _hudStabilityFillImg = UIFactory.Image(sbar.transform, "Fill", UIFactory.PillSprite, Palette.Good);
            _hudStabilityFill = _hudStabilityFillImg.rectTransform;
            _hudStabilityFill.anchorMin = Vector2.zero;
            _hudStabilityFill.anchorMax = new Vector2(0.05f, 1);
            _hudStabilityFill.offsetMin = _hudStabilityFill.offsetMax = Vector2.zero;

            // ---------------- hint (bottom center)
            var hint = UIFactory.Image(g.transform, "Hint", UIFactory.PillSprite, new Color(0.10f, 0.06f, 0.04f, 0.85f));
            UIFactory.Place(hint.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(1000, 66));
            _hudHint = UIFactory.Text(hint.transform, "", 26, Palette.Cream);
            UIFactory.Stretch(_hudHint.rectTransform, 24, 0, 24, 0);

            var controls = UIFactory.Text(g.transform, "LMB drag: move block    RMB drag / A-D: orbit    Wheel: zoom    Esc: menu", 20, new Color(1, 1, 1, 0.5f), TextAnchor.LowerRight);
            UIFactory.Place(controls.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 24), new Vector2(560, 60));

            // ---------------- pause button (top right)
            var pause = UIFactory.IconButton(g.transform, A.iconGear, TogglePause, new Color(0.18f, 0.11f, 0.07f, 0.92f), Palette.Cream, 72);
            UIFactory.Place((RectTransform)pause.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -24), new Vector2(72, 72));

            // ---------------- banner + countdown (center)
            var banner = UIFactory.Text(g.transform, "", 92, Palette.Cream, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.AddShadow(banner, new Color(0, 0, 0, 0.6f), new Vector2(4, -6));
            UIFactory.Place(banner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(1400, 140));
            _hudBanner = banner;
            _bannerGroup = banner.gameObject.AddComponent<CanvasGroup>();
            _bannerGroup.alpha = 0;
            _bannerGroup.blocksRaycasts = false;

            var cd = UIFactory.Text(g.transform, "", 220, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.AddShadow(cd, new Color(0, 0, 0, 0.6f), new Vector2(6, -8));
            UIFactory.Place(cd.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(800, 300));
            _hudCountdown = cd;
            _countdownGroup = cd.gameObject.AddComponent<CanvasGroup>();
            _countdownGroup.alpha = 0;
            _countdownGroup.blocksRaycasts = false;
        }

        void ResetHud()
        {
            _hudRosterKey = "";
            _lastTick = -1;
            _lastCountdown = -1;
            if (!SecretMissionManager.HasLocalMission) OnMissionChanged(default, false);
        }

        void ToggleMissionHidden()
        {
            _missionHidden = !_missionHidden;
            _missionBody.gameObject.SetActive(!_missionHidden);
            _missionCard.sizeDelta = new Vector2(440, _missionHidden ? 70 : 400);
            var btn = _missionHeaderBg.GetComponentInChildren<Button>();
            if (btn != null) UIFactory.SetLabel(btn, _missionHidden ? "Show" : "Hide");
        }

        void OnMissionChanged(MissionInfo m, bool justCompleted)
        {
            if (_missionTitle == null) return;
            if (m.Type == MissionType.None)
            {
                _missionTitle.text = "Dealing...";
                _missionDesc.text = "Your secret mission arrives when the match starts. Keep it to yourself!";
                _missionProgress.text = "";
                _missionStatus.text = "WAITING";
                _missionIcon.sprite = A.iconSecret != null ? A.iconSecret : UIFactory.CircleSprite;
                _missionHeaderBg.color = Palette.AccentDark;
                return;
            }
            bool sabotage = MissionCatalog.IsSabotage(m.Type);
            _missionHeaderBg.color = sabotage ? new Color(0.62f, 0.18f, 0.14f) : Palette.AccentDark;
            _missionHeader.text = sabotage ? "SECRET MISSION - risky" : "SECRET MISSION";
            _missionIcon.sprite = A.MissionIcon(m.Type);
            _missionIcon.color = m.Type == MissionType.ColorHunter ? Palette.BlockUIColor(m.TargetColor) : Palette.Accent;
            _missionTitle.text = MissionCatalog.Title(m.Type);
            _missionDesc.text = MissionCatalog.Description(m);
            _missionProgress.text = $"{m.Progress} / {m.Target}";
            _missionStatus.text = m.Completed ? "COMPLETED" : "IN PROGRESS";
            _missionStatus.transform.parent.GetComponent<Image>().color = m.Completed ? Palette.Good : new Color(1, 1, 1, 0.1f);
            if (justCompleted)
            {
                Toast("Secret mission complete: " + MissionCatalog.Title(m.Type), Palette.Good);
                UITween.Pop(_missionCard, 1.12f, 0.4f);
            }
        }

        void OnTurnStarted(ulong clientId)
        {
            if (_game == null || _hudBanner == null) return;
            bool mine = _game.NetworkManager != null && clientId == _game.NetworkManager.LocalClientId;
            _hudBanner.text = mine ? "YOUR TURN!" : $"{_game.PlayerName(clientId)}'s turn";
            _hudBanner.color = mine ? Palette.Accent : _game.PlayerColor(clientId);
            _bannerGroup.alpha = 1f;
            UITween.Pop(_hudBanner.transform, 1.35f, 0.4f);
            UITween.Delay(1.4f, () => { if (_bannerGroup != null) UITween.Fade(_bannerGroup, 0f, 0.5f); });
            _lastTick = -1;
        }

        void RebuildHudPlayers()
        {
            foreach (Transform c in _hudPlayers) Destroy(c.gameObject);
            _hudRows.Clear();
            var order = new List<ulong>();
            foreach (var id in _game.Turns.TurnOrder) order.Add(id);
            if (order.Count == 0) foreach (var p in _game.PlayersSorted()) order.Add(p.ClientId);
            foreach (var id in order)
            {
                if (!_game.TryGetPlayer(id, out var p)) continue;
                var bg = UIFactory.Image(_hudPlayers, "P", UIFactory.PillSprite, new Color(0.16f, 0.10f, 0.06f, 0.88f));
                UIFactory.LE(bg, 72, 360);
                var h = UIFactory.HLayout(bg, 12, 0, TextAnchor.MiddleLeft, false);
                h.padding = new RectOffset(8, 20, 6, 6);
                h.childForceExpandHeight = false;
                var av = UIFactory.Avatar(bg.transform, p.Name.ToString(), p.Color, 60);
                var name = UIFactory.Text(bg.transform, p.Name.ToString(), 28, Palette.Cream, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.LE(name, 40, -1, 1);
                var state = UIFactory.Text(bg.transform, "", 20, Palette.Muted, TextAnchor.MiddleRight, FontStyle.Bold);
                UIFactory.LE(state, 40, 110);
                _hudRows.Add((id, bg, av, name, state));
            }
        }

        void UpdateHud()
        {
            if (_game == null || !_game.IsSpawned) return;
            var turns = _game.Turns;
            var tower = _game.Tower;

            // roster (rebuild when order or connection changes)
            var key = "";
            foreach (var id in turns.TurnOrder) key += id + ",";
            foreach (var p in _game.Players) key += p.ClientId + (p.IsConnected ? "c" : "d") + p.ColorIndex + ";";
            if (key != _hudRosterKey) { _hudRosterKey = key; RebuildHudPlayers(); }
            ulong active = turns.ActiveClientId.Value;
            foreach (var r in _hudRows)
            {
                bool isActive = r.id == active && _game.Phase.Value == GamePhase.Playing;
                bool connected = _game.TryGetPlayer(r.id, out var pd) && pd.IsConnected;
                r.bg.color = isActive ? new Color(Palette.Accent.r, Palette.Accent.g, Palette.Accent.b, 0.92f) : new Color(0.16f, 0.10f, 0.06f, 0.88f);
                r.name.color = isActive ? Palette.WalnutDark : Palette.Cream;
                r.state.text = !connected ? "OFFLINE" : (isActive ? "PLAYING" : "");
                r.state.color = isActive ? Palette.WalnutDark : Palette.Muted;
                r.bg.transform.localScale = Vector3.Lerp(r.bg.transform.localScale, Vector3.one * (isActive ? 1.06f : 1f), 0.2f);
            }

            // height
            int layers = tower.LayerCount.Value, fill = tower.TopLayerFill.Value;
            int completed = fill >= 3 ? layers : Mathf.Max(0, layers - 1);
            int start = _game.StartLayers.Value, target = _game.TargetHeight.Value;
            float h = (layers - 1) + fill / 3f;
            float progress = Mathf.Clamp01((h - start) / Mathf.Max(1f, target - start));
            _hudHeight.text = fill >= 3 || layers == 0 ? $"{completed} / {target}" : $"{completed} / {target}  <size=24>(+{fill}/3)</size>";
            var am = _hudProgressFill.anchorMax;
            am.x = Mathf.Lerp(am.x, Mathf.Max(0.04f, progress), 0.15f);
            _hudProgressFill.anchorMax = am;
            _hudTurn.text = $"TURN {Mathf.Max(1, turns.TurnNumber.Value)}";

            // countdown / timer
            if (_game.Phase.Value == GamePhase.Countdown)
            {
                double left = _game.CountdownEndTime.Value - _game.ServerNow;
                int n = Mathf.CeilToInt((float)left);
                _countdownGroup.alpha = 1f;
                if (n != _lastCountdown && n >= 1 && n <= 3)
                {
                    _lastCountdown = n;
                    _hudCountdown.text = n.ToString();
                    UITween.Pop(_hudCountdown.transform, 1.6f, 0.35f);
                    AudioManager.Instance?.Play(AudioManager.Sfx.Countdown);
                }
                _hudTimer.text = "--";
                _hudHint.text = "Get ready! Check your secret mission on the right.";
            }
            else
            {
                if (_lastCountdown != 0 && _countdownGroup.alpha > 0.99f)
                {
                    _lastCountdown = 0;
                    _hudCountdown.text = "GO!";
                    UITween.Pop(_hudCountdown.transform, 1.6f, 0.35f);
                    UITween.Delay(0.6f, () => UITween.Fade(_countdownGroup, 0f, 0.3f));
                }
                double rem = turns.TimeRemaining;
                int secs = Mathf.CeilToInt((float)rem);
                bool settling = turns.Stage.Value == TurnStage.Settling;
                _hudTimer.text = settling ? "..." : secs.ToString();
                bool urgent = !settling && secs <= 5;
                _hudTimer.color = urgent ? Palette.Bad : Palette.Cream;
                if (urgent && turns.IsLocalPlayersTurn && secs != _lastTick && secs > 0)
                {
                    _lastTick = secs;
                    AudioManager.Instance?.Play(AudioManager.Sfx.TimerTick);
                    UITween.Pop(_hudTimerBg.transform, 1.12f, 0.2f);
                }
                var bic = BlockInteractionController.Instance;
                _hudHint.text = bic != null ? bic.Hint : "";
            }

            // stability
            float w = _game.Stability.Wobble.Value;
            var sm = _hudStabilityFill.anchorMax;
            sm.x = Mathf.Lerp(sm.x, Mathf.Max(0.04f, w), 0.25f);
            _hudStabilityFill.anchorMax = sm;
            _hudStabilityFillImg.color = Color.Lerp(Palette.Good, Palette.Bad, w * 1.4f);
            _hudStabilityText.text = w < 0.15f ? "TOWER: STABLE" : (w < 0.45f ? "TOWER: WOBBLING" : "TOWER: DANGER!");
        }
    }
}
