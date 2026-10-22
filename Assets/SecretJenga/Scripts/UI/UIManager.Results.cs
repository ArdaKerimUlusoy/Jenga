using SecretJenga.Game;
using SecretJenga.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace SecretJenga.UI
{
    /// <summary>
    /// The end of a round as a typed incident report: who dropped the tower (LOST, stamped over their name) and who
    /// walks away as winners. A local player sees YOU WIN / YOU LOSE in the stamp.
    /// </summary>
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
            UIFactory.LE(_resTitle, 80);
            _resReason = UIFactory.Write(col, FontKind.Body, "", 27, Ink.Faded, TextAnchor.MiddleLeft, FontStyle.Italic);
            UIFactory.LE(_resReason, 36);
            _resTurns = UIFactory.Write(col, FontKind.Type, "", 22, Ink.Black, TextAnchor.MiddleLeft);
            UIFactory.LE(_resTurns, 32);
            UIFactory.Divider(col);
            UIFactory.Caption(col, L.T("points this round (survived +3 / dropped it -3 / secret task +1..+4), best total first:",
                                       "bu elin puanları (hayatta kaldı +3 / yıktı -3 / gizli görev +1..+4), en yüksek toplam önce:"));

            _resGrid = UIFactory.Rect("Grid", col);
            UIFactory.LE(_resGrid, 452);
            var h = UIFactory.HLayout(_resGrid, 34, 0, TextAnchor.UpperLeft, true);
            h.childForceExpandHeight = true;

            UIFactory.Divider(col);
            _resCards = UIFactory.Write(col, FontKind.Type, "", 18, Ink.Faded, TextAnchor.UpperLeft);
            UIFactory.LE(_resCards, 40);

            UIFactory.LE(UIFactory.Rect("Push", col), -1, -1, -1, 1);
            var row = UIFactory.ButtonRow(col, 70, 60);
            UIFactory.Button(row, L.T("leave the table", "masadan kalk"), LeaveRoom, ButtonStyle.Ghost, 30);
            _resWaiting = UIFactory.Write(row, FontKind.Body, "", 22, Ink.Faded, TextAnchor.MiddleLeft, FontStyle.Italic);
            UIFactory.LE(_resWaiting, -1, -1, 1);
            _resLobby = UIFactory.Button(row, L.T("Back to the lobby", "Lobiye dön"), () => _game?.ReturnToLobbyServerRpc(), ButtonStyle.Secondary, 32);
            _resPlayAgain = UIFactory.Button(row, L.T("Another round", "Bir el daha"), () => _game?.PlayAgainServerRpc(), ButtonStyle.Primary, 42);

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
            ulong loser = RoundResults.LoserId;
            string reason = RoundResults.ReasonText;
            ulong me = ulong.MaxValue;
            if (_game != null && _game.IsSpawned)
            {
                me = _game.NetworkManager.LocalClientId;
                if (!RoundResults.HasResults) loser = _game.LoserClientId.Value;
                if (string.IsNullOrEmpty(reason)) reason = _game.EndReason.Value.ToString();
                bool host = _game.IsServer;
                _resPlayAgain.gameObject.SetActive(host);
                _resLobby.gameObject.SetActive(host);
                _resWaiting.text = host ? "" : L.T("the host decides what happens next...", "sırada ne olacağına kurucu karar verir...");
            }
            bool hasLoser = loser != ulong.MaxValue;
            bool iLost = hasLoser && loser == me;
            string loserName = "";
            var winners = new System.Collections.Generic.List<string>();
            foreach (var r in RoundResults.Players)
            {
                if (r.ClientId == loser) loserName = r.Name.ToString();
                else winners.Add(r.Name.ToString());
            }
            if (hasLoser && string.IsNullOrEmpty(loserName) && _game != null) loserName = _game.PlayerName(loser);

            if (_reportNo == 0) _reportNo = Random.Range(104, 987);
            _resHeader.text = L.T($"INCIDENT REPORT  No. {_reportNo}", $"OLAY TUTANAĞI  No. {_reportNo}") + $"        {System.DateTime.Now:dd.MM.yyyy  HH:mm}";
            // match standings (highest total first)
            var standings = new System.Collections.Generic.List<PlayerRoundResult>(RoundResults.Players);
            standings.Sort((a, b) => b.TotalScore.CompareTo(a.TotalScore));
            int best = standings.Count > 0 ? standings[0].TotalScore : 0;
            var champions = new System.Collections.Generic.List<string>();
            bool iChampion = false;
            foreach (var r in standings)
                if (r.TotalScore == best) { champions.Add(r.Name.ToString()); if (r.ClientId == me) iChampion = true; }
            bool matchOver = RoundResults.MatchOver;
            string roundTag = RoundResults.Rounds > 0 ? L.T($"round {RoundResults.Round} of {RoundResults.Rounds}", $"el {RoundResults.Round} / {RoundResults.Rounds}") : "";

            string fell = !hasLoser ? L.T("The round was abandoned.", "El yarıda kaldı.")
                : iLost ? L.T("You dropped the tower.", "Kuleyi sen yıktın.")
                : L.T($"{loserName} dropped the tower.", $"Kuleyi {loserName} yıktı.");
            _resTitle.text = matchOver && champions.Count > 0
                ? (iChampion && champions.Count == 1 ? L.T("You won the match!", "Maçı sen kazandın!")
                   : L.T(string.Join(" & ", champions) + " won the match.", "Maçı " + string.Join(" & ", champions) + " kazandı."))
                : fell;
            if (_resPlayAgain != null) UIFactory.SetLabel(_resPlayAgain, matchOver ? L.T("New match", "Yeni maç") : L.T("Next round", "Sonraki el"));
            _resReason.text = (matchOver ? fell + "   " : "") + L.Resolve(reason) + (roundTag.Length > 0 ? "   (" + roundTag + ")" : "");
            _resTurns.text = (hasLoser && winners.Count > 0 ? L.T("winners:  ", "kazananlar:  ") + string.Join(",  ", winners) + "        " : "") +
                             L.T($"good turns before the fall:  {RoundResults.SuccessfulTurns}", $"yıkılmadan önceki başarılı tur:  {RoundResults.SuccessfulTurns}");
            _resCards.text = string.IsNullOrEmpty(RoundResults.CardSummary) ? "" : L.T("cards:  ", "kartlar:  ") + L.Resolve(RoundResults.CardSummary).Replace("\n", "     ");

            foreach (Transform c in _resStampHolder) Destroy(c.gameObject);
            UIFactory.OnPaper = true;
            bool good = matchOver ? iChampion : !iLost;
            string stampText = matchOver ? (iChampion ? L.T("CHAMPION", "ŞAMPİYON") : L.T("MATCH OVER", "MAÇ BİTTİ"))
                : !hasLoser ? L.T("ABANDONED", "YARIDA KALDI") : iLost ? L.T("YOU LOSE", "KAYBETTİN") : L.T("YOU WIN", "KAZANDIN");
            var stamp = UIFactory.Stamp(_resStampHolder, stampText, good ? new Color(Ink.Green.r, Ink.Green.g, Ink.Green.b, 0.8f) : new Color(Ink.Red.r, Ink.Red.g, Ink.Red.b, 0.85f), new Vector2(400, 130), good ? 9f : -11f);
            stamp.anchorMin = stamp.anchorMax = new Vector2(0.5f, 0.5f);
            stamp.anchoredPosition = Vector2.zero;
            UITween.Scale(stamp, Vector3.one * 1.6f, Vector3.one, 0.22f, false);

            foreach (Transform c in _resGrid) Destroy(c.gameObject);
            var results = RoundResults.Players;
            if (results == null || results.Count == 0)
            {
                var t = UIFactory.Write(_resGrid, FontKind.Body, L.T("unsealing the envelopes...", "zarflar açılıyor..."), 28, Ink.Faded, TextAnchor.UpperLeft, FontStyle.Italic);
                UIFactory.LE(t, -1, -1, 1);
                UIFactory.OnPaper = false;
                return;
            }
            for (int i = 0; i < standings.Count; i++) AddResultCard(standings[i], standings[i].ClientId == loser, i + 1, matchOver && standings[i].TotalScore == best);
            UIFactory.OnPaper = false;
        }

        void AddResultCard(PlayerRoundResult r, bool lost, int place, bool champion)
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

            // points: this round's breakdown and the match total
            var pts = UIFactory.Rect("Points", colRt);
            UIFactory.LE(pts, 44);
            UIFactory.HLayout(pts, 8, 0, TextAnchor.MiddleLeft, false);
            int round = r.SurvivalPoints + r.TaskPoints + r.PenaltyPoints;
            string pen = r.PenaltyPoints != 0 ? L.T($"  {r.PenaltyPoints} time", $"  {r.PenaltyPoints} süre") : "";
            var breakdown = UIFactory.Write(pts, FontKind.Type,
                L.T($"{ScoreRules.Signed(r.SurvivalPoints)} table  {ScoreRules.Signed(r.TaskPoints)} task{pen}\n= {ScoreRules.Signed(round)} this round",
                    $"{ScoreRules.Signed(r.SurvivalPoints)} masa  {ScoreRules.Signed(r.TaskPoints)} görev{pen}\n= {ScoreRules.Signed(round)} bu el"),
                15, round >= 0 ? Ink.Black : Ink.Red, TextAnchor.MiddleLeft);
            UIFactory.LE(breakdown, 44, -1, 1);
            var total = UIFactory.Write(pts, FontKind.Title, $"{place}.  {r.TotalScore}", 34, champion ? Ink.Red : Ink.Black, TextAnchor.MiddleRight);
            UIFactory.LE(total, 44, 110);

            var turns = UIFactory.Write(colRt, FontKind.Type,
                L.T($"{r.SuccessfulTurns} good turn(s)", $"{r.SuccessfulTurns} başarılı tur") + (lost ? L.T("\nLOST - it fell in their hands", "\nKAYBETTİ - kule onun elinde yıkıldı") : L.T("\nwon", "\nkazandı")),
                16, lost ? Ink.Red : Ink.Green, TextAnchor.UpperLeft);
            UIFactory.LE(turns, 44);

            var t = r.Task;
            var def = t.Kind == TaskKind.None ? null : GameContent.Task(t.DefinitionIndex);
            var title = UIFactory.Write(colRt, FontKind.Title, def != null ? def.Title : L.T("no task", "görev yok"), 28, Ink.Black, TextAnchor.MiddleLeft);
            UIFactory.LE(title, 38);
            var desc = UIFactory.Write(colRt, FontKind.Body, def != null ? def.Describe(t) : "", 19, Ink.Black, TextAnchor.UpperLeft);
            UIFactory.LE(desc, 84);
            var prog = UIFactory.Write(colRt, FontKind.Type, def != null ? L.T($"{t.Progress} of {t.Target}", $"{t.Progress} / {t.Target}") : "", 17, Ink.Faded, TextAnchor.MiddleLeft);
            UIFactory.LE(prog, 26);

            // verdict, by hand
            var verdictRow = UIFactory.Rect("Verdict", colRt);
            UIFactory.LE(verdictRow, 58);
            var word = UIFactory.Write(verdictRow, FontKind.Title, "", 30, Ink.Black, TextAnchor.MiddleLeft);
            UIFactory.Stretch(word.rectTransform, 14, 0, 0, 0);
            if (t.Completed)
            {
                word.text = L.T("kept", "tuttu");
                word.color = Ink.Black;
                var c = UIFactory.Image(verdictRow, "Circle", A.inkCircle != null ? A.inkCircle : UIFactory.CircleSprite, Ink.Red, false);
                UIFactory.Place(c.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(-6, 0), new Vector2(120, 56));
            }
            else
            {
                word.text = t.Failed ? L.T("broken", "bozuldu") : L.T("unfinished", "bitmedi");
                word.color = t.Failed ? Ink.Red : Ink.Faded;
                if (t.Failed)
                {
                    var s = UIFactory.Image(verdictRow, "Strike", UIFactory.StrokeSprite, Ink.Red, false);
                    UIFactory.Place(s.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(word.preferredWidth + 18, 10));
                    s.rectTransform.localRotation = Quaternion.Euler(0, 0, 4f);
                }
            }

            var cardLine = UIFactory.Write(colRt, FontKind.Type, L.T("card: ", "kart: ") + GameContent.CardTitle(r.Card) + (r.Card.Kind != CardKind.None ? (r.Card.Used ? L.T(" - played", " - oynandı") : L.T(" - kept", " - saklandı")) : ""), 15, Ink.Faded, TextAnchor.MiddleLeft);
            UIFactory.LE(cardLine, 24);
        }
    }
}
