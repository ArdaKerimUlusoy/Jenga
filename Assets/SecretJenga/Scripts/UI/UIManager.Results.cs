using SecretJenga.Game;
using SecretJenga.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace SecretJenga.UI
{
    public partial class UIManager
    {
        Text _resTitle, _resReason, _resWaiting;
        RectTransform _resGrid;
        Button _resPlayAgain, _resLobby;
        Image _resBanner;

        void BuildResults()
        {
            var g = NewScreen(View.Results, "Results");
            var dim = UIFactory.Image(g.transform, "Dim", null, new Color(0.04f, 0.02f, 0.01f, 0.55f));
            dim.raycastTarget = true;
            UIFactory.Stretch(dim.rectTransform);
            dim.transform.SetAsFirstSibling();

            var card = UIFactory.Card(g.transform, "ResultsCard");
            UIFactory.Place((RectTransform)card.transform.parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1640, 940));
            card.transform.parent.SetAsLastSibling();
            var col = UIFactory.Rect("Col", card.transform);
            UIFactory.Stretch(col);
            UIFactory.VLayout(col, 14, 40);

            _resBanner = UIFactory.Image(col, "Banner", UIFactory.RoundedSprite, Palette.Good);
            UIFactory.LE(_resBanner, 120);
            _resTitle = UIFactory.Text(_resBanner.transform, "TEAM VICTORY!", 72, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.AddShadow(_resTitle, new Color(0, 0, 0, 0.4f), new Vector2(3, -4));
            UIFactory.Stretch(_resTitle.rectTransform);
            _resReason = UIFactory.Text(col, "", 30, Palette.Paper, TextAnchor.MiddleCenter, FontStyle.Italic);
            UIFactory.LE(_resReason, 44);
            var sub = UIFactory.Text(col, "SECRET MISSIONS REVEALED", 24, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.LE(sub, 32);

            _resGrid = UIFactory.Rect("Grid", col);
            UIFactory.LE(_resGrid, 470);
            var h = UIFactory.HLayout(_resGrid, 18, 0, TextAnchor.MiddleCenter, true);
            h.childForceExpandHeight = true;

            _resWaiting = UIFactory.Text(col, "", 26, Palette.Muted, TextAnchor.MiddleCenter, FontStyle.Italic);
            UIFactory.LE(_resWaiting, 36);

            var row = UIFactory.Rect("Buttons", col);
            UIFactory.LE(row, 90);
            UIFactory.HLayout(row, 20, 0);
            UIFactory.Button(row, "Leave", LeaveRoom, ButtonStyle.Ghost, 30);
            _resLobby = UIFactory.Button(row, "Return to Lobby", () => _game?.ReturnToLobbyServerRpc(), ButtonStyle.Secondary, 30);
            _resPlayAgain = UIFactory.Button(row, "Play Again", () => _game?.PlayAgainServerRpc(), ButtonStyle.Primary, 34, A.iconRefresh);
        }

        void OnResultsRevealed()
        {
            if (_current == View.Results) RefreshResults();
        }

        void RefreshResults()
        {
            if (_resGrid == null) return;
            bool won = SecretMissionManager.LastTeamWon;
            if (_game != null && _game.IsSpawned)
            {
                won = _game.TeamWon.Value;
                _resReason.text = _game.EndReason.Value.ToString();
                bool host = _game.IsServer;
                _resPlayAgain.gameObject.SetActive(host);
                _resLobby.gameObject.SetActive(host);
                _resWaiting.text = host ? "You are the host: play again with the same group or go back to the lobby." : "Waiting for the host to play again or return to the lobby...";
            }
            _resBanner.color = won ? Palette.Good : Palette.Bad;
            _resTitle.text = won ? "TEAM VICTORY!" : "THE TOWER FELL";

            foreach (Transform c in _resGrid) Destroy(c.gameObject);
            var results = SecretMissionManager.LastResults;
            if (results == null || results.Count == 0)
            {
                var t = UIFactory.Text(_resGrid, "Revealing missions...", 30, Palette.Paper);
                UIFactory.LE(t, -1, -1, 1);
                return;
            }
            foreach (var r in results) AddResultCard(r, won);
        }

        void AddResultCard(MissionResult r, bool teamWon)
        {
            var bg = UIFactory.Image(_resGrid, "Player", UIFactory.RoundedSprite, new Color(1, 1, 1, 0.07f));
            UIFactory.LE(bg, -1, 360, 1);
            var v = UIFactory.VLayout(bg, 10, 22, TextAnchor.UpperCenter);
            v.childForceExpandHeight = false;

            var color = Palette.Player(r.ColorIndex);
            var av = UIFactory.Avatar(bg.transform, r.Name.ToString(), color, 96);
            var holder = UIFactory.Rect("AvatarRow", bg.transform);
            UIFactory.LE(holder, 100);
            av.transform.SetParent(holder, false);
            UIFactory.Place(av.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96, 96));

            var name = UIFactory.Text(bg.transform, r.Name.ToString(), 32, Palette.Cream, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.LE(name, 42);

            var m = r.Mission;
            var mrow = UIFactory.Rect("Mission", bg.transform);
            UIFactory.LE(mrow, 52);
            UIFactory.HLayout(mrow, 10, 0, TextAnchor.MiddleCenter, false);
            var ic = UIFactory.Image(mrow, "Icon", A.MissionIcon(m.Type), m.Type == MissionType.ColorHunter ? Palette.BlockUIColor(m.TargetColor) : Palette.Accent, false);
            ic.preserveAspect = true;
            UIFactory.LE(ic, 46, 46);
            var mt = UIFactory.Text(mrow, MissionCatalog.Title(m.Type), 28, Palette.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            mt.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.LE(mt, 46);

            var desc = UIFactory.Text(bg.transform, MissionCatalog.Description(m), 22, Palette.Paper);
            UIFactory.LE(desc, 110);
            var prog = UIFactory.Text(bg.transform, m.Type == MissionType.None ? "" : $"Progress {m.Progress} / {m.Target}   -   {r.SuccessfulMoves} successful move(s)", 22, Palette.Muted);
            UIFactory.LE(prog, 30);

            string status;
            Color sc;
            if (m.Completed && teamWon) { status = "MISSION ACCOMPLISHED"; sc = Palette.Good; }
            else if (m.Completed) { status = "DONE - BUT THE TEAM LOST"; sc = Palette.AccentDark; }
            else { status = "MISSION FAILED"; sc = Palette.Bad; }
            var pill = UIFactory.Image(bg.transform, "Status", UIFactory.PillSprite, sc);
            UIFactory.LE(pill, 54);
            var st = UIFactory.Text(pill.transform, status, 22, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(st.rectTransform);
            if (m.Completed && teamWon)
            {
                var crown = UIFactory.Image(bg.transform, "Crown", A.iconCrown, Palette.Accent, false);
                crown.preserveAspect = true;
                UIFactory.LE(crown, 40);
            }
            UITween.Scale(bg.transform, Vector3.one * 0.85f, Vector3.one, 0.45f);
        }
    }
}
