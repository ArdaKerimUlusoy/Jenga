using SecretJenga.Game;
using SecretJenga.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace SecretJenga.UI
{
    /// <summary>The end of a round as a typed incident report, stamped STANDING or COLLAPSED.</summary>
    public partial class UIManager
    {
        Text _resTitle, _resReason, _resTurns, _resWaiting, _resCards, _resHeader;
        RectTransform _resGrid, _resStampHolder;
        Button _resPlayAgain, _resLobby;
        int _reportNo;

        void BuildResults()
        {
            UIFactory.OnPaper = false;
            var g = NewScreen(View.Results, "Results");
            var dim = UIFactory.Image(g.transform, "Dim", null, new Color(0f, 0f, 0f, 0.58f));
            dim.raycastTarget = true;
            UIFactory.Stretch(dim.rectTransform);
            dim.transform.SetAsFirstSibling();

            var sheet = UIFactory.PaperSheet(g.transform, "SheetReport", new Vector2(0.5f, 0.5f), new Vector2(0, -6), new Vector2(1640, 990), -0.7f, 2, 70);
            var col = sheet.Body;
            col.GetComponent<VerticalLayoutGroup>().spacing = 6;

            _resHeader = UIFactory.Write(col, FontKind.Type, "", 19, Ink.Faded, TextAnchor.MiddleLeft);
            UIFactory.LE(_resHeader, 28);
            _resTitle = UIFactory.Write(col, FontKind.Title, "", 74, Ink.Black, TextAnchor.MiddleLeft);
            _resTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.LE(_resTitle, 90);
            _resReason = UIFactory.Write(col, FontKind.Body, "", 27, Ink.Faded, TextAnchor.MiddleLeft, FontStyle.Italic);
            UIFactory.LE(_resReason, 36);
            _resTurns = UIFactory.Write(col, FontKind.Type, "", 22, Ink.Black, TextAnchor.MiddleLeft);
            UIFactory.LE(_resTurns, 32);
            UIFactory.Divider(col);
            UIFactory.Caption(col, "the secret tasks, now that it no longer matters:");

            _resGrid = UIFactory.Rect("Grid", col);
            UIFactory.LE(_resGrid, 430);
            var h = UIFactory.HLayout(_resGrid, 34, 0, TextAnchor.UpperLeft, true);
            h.childForceExpandHeight = true;

            UIFactory.Divider(col);
            _resCards = UIFactory.Write(col, FontKind.Type, "", 18, Ink.Faded, TextAnchor.UpperLeft);
            UIFactory.LE(_resCards, 52);

            UIFactory.LE(UIFactory.Rect("Push", col), -1, -1, -1, 1);
            var row = UIFactory.ButtonRow(col, 70, 60);
            UIFactory.Button(row, "leave the table", LeaveRoom, ButtonStyle.Ghost, 30);
            _resWaiting = UIFactory.Write(row, FontKind.Body, "", 22, Ink.Faded, TextAnchor.MiddleLeft, FontStyle.Italic);
            UIFactory.LE(_resWaiting, -1, -1, 1);
            _resLobby = UIFactory.Button(row, "Back to the lobby", () => _game?.ReturnToLobbyServerRpc(), ButtonStyle.Secondary, 32);
            _resPlayAgain = UIFactory.Button(row, "Another round", () => _game?.PlayAgainServerRpc(), ButtonStyle.Primary, 42);

            // the stamp, top right of the sheet
            _resStampHolder = UIFactory.Rect("StampHolder", sheet.Root);
            UIFactory.Place(_resStampHolder, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-300, -150), new Vector2(420, 150));
            UIFactory.OnPaper = false;
        }

        void OnResultsRevealed()
        {
            if (_current == View.Results) RefreshResults();
        }

        void RefreshResults()
        {
            if (_resGrid == null) return;
            bool won = RoundResults.GroupWon;
            string reason = RoundResults.ReasonText;
            if (_game != null && _game.IsSpawned)
            {
                won = _game.TeamWon.Value;
                if (string.IsNullOrEmpty(reason)) reason = _game.EndReason.Value.ToString();
                bool host = _game.IsServer;
                _resPlayAgain.gameObject.SetActive(host);
                _resLobby.gameObject.SetActive(host);
                _resWaiting.text = host ? "" : "the host decides what happens next...";
            }
            if (_reportNo == 0) _reportNo = Random.Range(104, 987);
            _resHeader.text = $"INCIDENT REPORT  No. {_reportNo}        {System.DateTime.Now:dd.MM.yyyy  HH:mm}";
            _resTitle.text = won ? "The tower stands." : "The tower fell.";
            _resReason.text = reason;
            _resTurns.text = RoundResults.RequiredTurns > 0 ? $"turns survived:  {RoundResults.SuccessfulTurns} of {RoundResults.RequiredTurns}" : "";
            _resCards.text = string.IsNullOrEmpty(RoundResults.CardSummary) ? "" : "cards:  " + RoundResults.CardSummary.Replace("\n", "     ");

            foreach (Transform c in _resStampHolder) Destroy(c.gameObject);
            UIFactory.OnPaper = true;
            var stamp = UIFactory.Stamp(_resStampHolder, won ? "STANDING" : "COLLAPSED", won ? new Color(Ink.Green.r, Ink.Green.g, Ink.Green.b, 0.8f) : new Color(Ink.Red.r, Ink.Red.g, Ink.Red.b, 0.85f), new Vector2(400, 130), won ? 9f : -11f);
            stamp.anchorMin = stamp.anchorMax = new Vector2(0.5f, 0.5f);
            stamp.anchoredPosition = Vector2.zero;
            UITween.Scale(stamp, Vector3.one * 1.6f, Vector3.one, 0.22f, false);

            foreach (Transform c in _resGrid) Destroy(c.gameObject);
            var results = RoundResults.Players;
            if (results == null || results.Count == 0)
            {
                var t = UIFactory.Write(_resGrid, FontKind.Body, "unsealing the envelopes...", 28, Ink.Faded, TextAnchor.UpperLeft, FontStyle.Italic);
                UIFactory.LE(t, -1, -1, 1);
                UIFactory.OnPaper = false;
                return;
            }
            foreach (var r in results) AddResultCard(r);
            UIFactory.OnPaper = false;
        }

        void AddResultCard(PlayerRoundResult r)
        {
            var colRt = UIFactory.Rect("Player", _resGrid);
            UIFactory.LE(colRt, -1, 280, 1);
            var v = UIFactory.VLayout(colRt, 4, 0, TextAnchor.UpperLeft);
            v.childForceExpandHeight = false;

            var color = Palette.Player(r.ColorIndex);
            var head = UIFactory.Rect("Head", colRt);
            UIFactory.LE(head, 60);
            UIFactory.HLayout(head, 12, 0, TextAnchor.MiddleLeft, false);
            UIFactory.Avatar(head, r.Name.ToString(), new Color(color.r * 0.72f, color.g * 0.72f, color.b * 0.72f), 50);
            var name = UIFactory.Write(head, FontKind.Body, r.Name.ToString(), 32, Ink.Black, TextAnchor.MiddleLeft);
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.LE(name, 60, -1, 1);

            var turns = UIFactory.Write(colRt, FontKind.Type, $"{r.SuccessfulTurns} turn(s) survived" + (r.CausedCollapse ? "\nit fell in their hands" : ""), 16, r.CausedCollapse ? Ink.Red : Ink.Faded, TextAnchor.UpperLeft);
            UIFactory.LE(turns, 44);

            var t = r.Task;
            var def = t.Kind == TaskKind.None ? null : GameContent.Task(t.DefinitionIndex);
            var title = UIFactory.Write(colRt, FontKind.Title, def != null ? def.title : "no task", 28, Ink.Black, TextAnchor.MiddleLeft);
            UIFactory.LE(title, 38);
            var desc = UIFactory.Write(colRt, FontKind.Body, def != null ? def.Describe(t, RoundResults.RequiredTurns) : "", 19, Ink.Black, TextAnchor.UpperLeft);
            UIFactory.LE(desc, 120);
            var prog = UIFactory.Write(colRt, FontKind.Type, def != null ? $"{t.Progress} of {t.Target}" : "", 17, Ink.Faded, TextAnchor.MiddleLeft);
            UIFactory.LE(prog, 26);

            // verdict, by hand
            var verdictRow = UIFactory.Rect("Verdict", colRt);
            UIFactory.LE(verdictRow, 58);
            var word = UIFactory.Write(verdictRow, FontKind.Title, "", 30, Ink.Black, TextAnchor.MiddleLeft);
            UIFactory.Stretch(word.rectTransform, 14, 0, 0, 0);
            if (t.Completed)
            {
                word.text = "kept";
                word.color = Ink.Black;
                var c = UIFactory.Image(verdictRow, "Circle", A.inkCircle != null ? A.inkCircle : UIFactory.CircleSprite, Ink.Red, false);
                UIFactory.Place(c.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(-6, 0), new Vector2(120, 56));
            }
            else
            {
                word.text = t.Failed ? "broken" : "unfinished";
                word.color = t.Failed ? Ink.Red : Ink.Faded;
                if (t.Failed)
                {
                    var s = UIFactory.Image(verdictRow, "Strike", UIFactory.StrokeSprite, Ink.Red, false);
                    UIFactory.Place(s.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(word.preferredWidth + 18, 10));
                    s.rectTransform.localRotation = Quaternion.Euler(0, 0, 4f);
                }
            }

            var cardLine = UIFactory.Write(colRt, FontKind.Type, "card: " + GameContent.CardTitle(r.Card) + (r.Card.Kind != CardKind.None ? (r.Card.Used ? " - played" : " - kept") : ""), 15, Ink.Faded, TextAnchor.MiddleLeft);
            UIFactory.LE(cardLine, 24);
        }
    }
}
