using System.Collections.Generic;
using SecretJenga.Game;
using SecretJenga.Networking;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SecretJenga.UI
{
    /// <summary>
    /// In-round HUD. Nothing here is a box: every good turn is scratched as a tally mark (there is no last turn -
    /// the round lasts until the tower falls, and whoever drops it loses), the turn order is a typed
    /// slip taped to the corner, the tower's tremor is drawn by a seismograph needle, your special card is a real
    /// card lying at the bottom right and your secret task is a folded note you open by holding TAB.
    /// </summary>
    public partial class UIManager
    {
        // tally + turn line
        Text _hudTallyText, _hudTurnLine, _hudNextLine, _hudTimer, _hudCountdown, _hudBanner, _hudHint, _hudEffects, _hudTremorWord;
        RectTransform _tallyRow;
        readonly List<Image> _tallies = new List<Image>();
        int _tallyRequired = -1, _tallyDone = -1;
        CanvasGroup _bannerGroup, _countdownGroup;
        Seismograph _seismo;
        // turn order slip
        RectTransform _orderSlip, _orderList;
        readonly List<(ulong id, Text line, Image under, Image strike)> _hudRows = new List<(ulong, Text, Image, Image)>();
        string _hudRosterKey = "";
        // secret task note
        RectTransform _taskFolded, _taskOpen;
        Text _taskTitle, _taskDesc, _taskProgress, _taskStatus;
        RectTransform _taskStatusCircle;
        bool _taskPinned;
        // card
        RectTransform _cardRoot, _cardPlayedStamp;
        Image _cardIcon;
        Text _cardTitle, _cardMeta, _cardDesc, _cardState;
        Button _cardButton;
        float _cardLift;
        // cursor prompt
        RectTransform _promptRt, _crosshair;
        Text _promptText;
        int _lastTick = -1;
        int _lastCountdown = -1;

        void BuildHud()
        {
            UIFactory.OnPaper = false;
            var g = NewScreen(View.Hud, "HUD");
            g.blocksRaycasts = true;

            // ---------------- survived turns: scratched tally marks (top centre)
            _tallyRow = UIFactory.Rect("Tally", g.transform);
            UIFactory.Place(_tallyRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -26), new Vector2(900, 64));
            _hudTallyText = UIFactory.Write(g.transform, FontKind.Type, "", 19, Ink.BoneDim, TextAnchor.MiddleCenter);
            UIFactory.Place(_hudTallyText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -96), new Vector2(900, 28));

            // ---------------- whose turn (written under the tally) + timer
            _hudTurnLine = UIFactory.Write(g.transform, FontKind.Body, "", 38, Ink.Bone, TextAnchor.MiddleCenter, FontStyle.Italic);
            UIFactory.Place(_hudTurnLine.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -136), new Vector2(1200, 50));
            _hudNextLine = UIFactory.Write(g.transform, FontKind.Type, "", 18, Ink.BoneDim, TextAnchor.MiddleCenter);
            UIFactory.Place(_hudNextLine.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -172), new Vector2(1200, 26));
            _hudTimer = UIFactory.Write(g.transform, FontKind.Type, "", 44, Ink.Bone, TextAnchor.MiddleRight);
            UIFactory.Place(_hudTimer.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-44, -30), new Vector2(240, 60));
            var tl = UIFactory.Write(g.transform, FontKind.Type, L.T("time", "süre"), 16, Ink.BoneFaint, TextAnchor.MiddleRight);
            UIFactory.Place(tl.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-46, -88), new Vector2(240, 22));
            _hudEffects = UIFactory.Write(g.transform, FontKind.Type, "", 18, Ink.RedBright, TextAnchor.MiddleCenter);
            UIFactory.Place(_hudEffects.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -202), new Vector2(1200, 28));

            // ---------------- turn order: a typed slip taped top-left
            var slip = UIFactory.PaperSheet(g.transform, "OrderSlip", new Vector2(0f, 1f), new Vector2(190, -150), new Vector2(330, 250), -2.2f, 1, 30, true);
            _orderSlip = slip.Root;
            slip.PaperImage.raycastTarget = false;
            var head = UIFactory.Write(slip.Body, FontKind.Type, UIFactory.Spaced(L.T("order", "sıra")), 16, Ink.Faded, TextAnchor.MiddleLeft);
            UIFactory.LE(head, 24);
            _orderList = UIFactory.Rect("Lines", slip.Body);
            UIFactory.VLayout(_orderList, 0, 0, TextAnchor.UpperLeft);
            UIFactory.LE(_orderList, -1, -1, -1, 1);
            UIFactory.OnPaper = false;

            // ---------------- seismograph (bottom left)
            var seis = UIFactory.PaperSheet(g.transform, "Seismo", new Vector2(0f, 0f), new Vector2(230, 112), new Vector2(380, 150), 1.4f, 1, 22, true);
            seis.PaperImage.raycastTarget = false;
            var seisHead = UIFactory.Rect("Head", seis.Body);
            UIFactory.LE(seisHead, 24);
            UIFactory.HLayout(seisHead, 6, 0, TextAnchor.MiddleLeft, false);
            UIFactory.LE(UIFactory.Write(seisHead, FontKind.Type, UIFactory.Spaced(L.T("tremor", "sarsıntı")), 15, Ink.Faded, TextAnchor.MiddleLeft), -1, -1, 1);
            _hudTremorWord = UIFactory.Write(seisHead, FontKind.Type, L.T("still", "sakin"), 17, Ink.Black, TextAnchor.MiddleRight);
            UIFactory.LE(_hudTremorWord, -1, 170);
            var graphRt = UIFactory.Rect("Graph", seis.Body);
            UIFactory.LE(graphRt, -1, -1, -1, 1);
            _seismo = graphRt.gameObject.AddComponent<Seismograph>();
            _seismo.color = Ink.Black;
            _seismo.raycastTarget = false;
            UIFactory.OnPaper = false;

            // ---------------- the card (bottom right)
            BuildCard(g.transform);

            // ---------------- secret task: folded note (right), opened with TAB
            BuildTaskNote(g.transform);

            // ---------------- hint + controls (bottom, written on the air)
            _hudHint = UIFactory.Write(g.transform, FontKind.Body, "", 27, Ink.Bone, TextAnchor.MiddleCenter, FontStyle.Italic);
            UIFactory.Place(_hudHint.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 78), new Vector2(1100, 44));
            var controls = UIFactory.Write(g.transform, FontKind.Type,
                L.T("mouse  look   hold LMB  pull   ALT  free cursor   A/D  lean   W/S  closer   F  knock   C  card   TAB  task   ESC\n1  OK    2  lollipop    3  nyah nyah    4  sip of juice    5  wave    6  rock on",
                    "fare  bak   sol tık basılı  çek   ALT  imleç   A/D  eğil   W/S  yaklaş   F  masaya vur   C  kart   TAB  görev   ESC\n1  tamam    2  lolipop    3  nanik    4  meyve suyu    5  el salla    6  rock"), 15, Ink.BoneFaint, TextAnchor.MiddleCenter);
            UIFactory.Place(controls.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(1300, 44));

            // ---------------- cursor prompt (written beside the hand)
            _promptText = UIFactory.Write(g.transform, FontKind.Type, "", 18, Ink.Bone, TextAnchor.UpperLeft);
            _promptRt = _promptText.rectTransform;
            _promptRt.pivot = new Vector2(0f, 1f);
            _promptRt.anchorMin = _promptRt.anchorMax = Vector2.zero;
            _promptRt.sizeDelta = new Vector2(320, 30);
            _promptText.gameObject.SetActive(false);

            // aiming dot (the cursor is locked: the hand aims where this dot is)
            var dot = UIFactory.Image(g.transform, "Crosshair", UIFactory.CircleSprite, new Color(Ink.Bone.r, Ink.Bone.g, Ink.Bone.b, 0.75f), false);
            dot.raycastTarget = false;
            _crosshair = dot.rectTransform;
            _crosshair.anchorMin = _crosshair.anchorMax = Vector2.zero;
            _crosshair.sizeDelta = new Vector2(9, 9);

            // ---------------- banner + countdown (centre, transient)
            var banner = UIFactory.Write(g.transform, FontKind.Title, "", 86, Ink.Bone, TextAnchor.MiddleCenter);
            UIFactory.AddShadow(banner, new Color(0, 0, 0, 0.9f), new Vector2(3, -5));
            UIFactory.Place(banner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 210), new Vector2(1500, 130));
            _hudBanner = banner;
            _bannerGroup = banner.gameObject.AddComponent<CanvasGroup>();
            _bannerGroup.alpha = 0;
            _bannerGroup.blocksRaycasts = false;

            var cd = UIFactory.Write(g.transform, FontKind.Title, "", 230, Ink.RedBright, TextAnchor.MiddleCenter);
            UIFactory.AddShadow(cd, new Color(0, 0, 0, 0.9f), new Vector2(5, -7));
            UIFactory.Place(cd.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(900, 300));
            _hudCountdown = cd;
            _countdownGroup = cd.gameObject.AddComponent<CanvasGroup>();
            _countdownGroup.alpha = 0;
            _countdownGroup.blocksRaycasts = false;
        }

        void BuildCard(Transform parent)
        {
            _cardRoot = UIFactory.Rect("Card", parent);
            UIFactory.Place(_cardRoot, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-190, 190), new Vector2(240, 336));
            _cardRoot.localRotation = Quaternion.Euler(0, 0, 5f);
            var shadow = UIFactory.Image(_cardRoot, "Shadow", A.cardStock != null ? A.cardStock : UIFactory.PaperSprite, new Color(0, 0, 0, 0.6f), false);
            UIFactory.Stretch(shadow.rectTransform, -4, 4, -12, -16);
            var face = UIFactory.Image(_cardRoot, "Face", A.cardStock != null ? A.cardStock : UIFactory.PaperSprite, Color.white, false);
            UIFactory.Stretch(face.rectTransform);
            face.raycastTarget = true;
            _cardButton = face.gameObject.AddComponent<Button>();
            _cardButton.transition = Selectable.Transition.None;
            var nav = _cardButton.navigation;
            nav.mode = Navigation.Mode.None;
            _cardButton.navigation = nav;
            _cardButton.onClick.AddListener(PlayCard);

            UIFactory.OnPaper = true;
            _cardTitle = UIFactory.Write(face.transform, FontKind.Title, "", 27, Ink.Black, TextAnchor.MiddleCenter);
            UIFactory.Place(_cardTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -36), new Vector2(200, 40));
            _cardIcon = UIFactory.Image(face.transform, "Symbol", null, Ink.Red, false);
            _cardIcon.preserveAspect = true;
            UIFactory.Place(_cardIcon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -82), new Vector2(110, 110));
            _cardDesc = UIFactory.Write(face.transform, FontKind.Body, "", 16, Ink.Black, TextAnchor.UpperCenter);
            UIFactory.Place(_cardDesc.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 58), new Vector2(186, 100));
            _cardMeta = UIFactory.Write(face.transform, FontKind.Type, "", 12, Ink.Faded, TextAnchor.MiddleCenter);
            UIFactory.Place(_cardMeta.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 38), new Vector2(190, 20));
            _cardPlayedStamp = UIFactory.Stamp(face.transform, L.T("PLAYED", "OYNANDI"), new Color(Ink.Red.r, Ink.Red.g, Ink.Red.b, 0.85f), new Vector2(200, 70), 18f);
            _cardPlayedStamp.anchoredPosition = new Vector2(0, 10);
            UIFactory.OnPaper = false;

            _cardState = UIFactory.Write(_cardRoot, FontKind.Type, "", 16, Ink.BoneDim, TextAnchor.MiddleCenter);
            UIFactory.Place(_cardState.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0, -12), new Vector2(300, 26));
        }

        void BuildTaskNote(Transform parent)
        {
            // folded: a small slip with the eye symbol
            var folded = UIFactory.PaperSheet(parent, "TaskFolded", new Vector2(1f, 0.5f), new Vector2(-150, 150), new Vector2(240, 96), 3f, 1, 18, true);
            _taskFolded = folded.Root;
            var fr = UIFactory.Rect("Row", folded.Body);
            UIFactory.LE(fr, 52);
            UIFactory.HLayout(fr, 10, 0, TextAnchor.MiddleLeft, false);
            var eye = UIFactory.Image(fr, "Eye", A.iconCardPressure != null ? A.iconCardPressure : A.iconSecret, Ink.Red, false);
            eye.preserveAspect = true;
            UIFactory.LE(eye, 40, 46);
            var ft = UIFactory.Write(fr, FontKind.Type, L.T("your task\nhold TAB", "görevin\nTAB basılı tut"), 17, Ink.Black, TextAnchor.MiddleLeft);
            UIFactory.LE(ft, 52, -1, 1);
            var fb = folded.PaperImage.gameObject.AddComponent<Button>();
            fb.transition = Selectable.Transition.None;
            fb.onClick.AddListener(() => _taskPinned = !_taskPinned);

            // open: the note unfolded
            var open = UIFactory.PaperSheet(parent, "TaskOpen", new Vector2(1f, 0.5f), new Vector2(-270, 110), new Vector2(460, 400), -1.6f, 2, 40);
            _taskOpen = open.Root;
            var ob = open.PaperImage.gameObject.AddComponent<Button>();
            ob.transition = Selectable.Transition.None;
            ob.onClick.AddListener(() => _taskPinned = !_taskPinned);
            UIFactory.Caption(open.Body, L.T("for your eyes only", "sadece senin için"), 17);
            _taskTitle = UIFactory.Write(open.Body, FontKind.Title, "", 36, Ink.Black, TextAnchor.MiddleLeft);
            UIFactory.LE(_taskTitle, 46);
            UIFactory.Divider(open.Body, 7);
            _taskDesc = UIFactory.Write(open.Body, FontKind.Body, "", 23, Ink.Black, TextAnchor.UpperLeft);
            UIFactory.LE(_taskDesc, 140);
            var pr = UIFactory.Rect("Progress", open.Body);
            UIFactory.LE(pr, 50);
            UIFactory.HLayout(pr, 10, 0, TextAnchor.MiddleLeft, false);
            _taskProgress = UIFactory.Write(pr, FontKind.Type, "", 26, Ink.Black, TextAnchor.MiddleLeft);
            UIFactory.LE(_taskProgress, -1, -1, 1);
            _taskStatus = UIFactory.Write(pr, FontKind.Type, "", 20, Ink.Faded, TextAnchor.MiddleCenter);
            UIFactory.LE(_taskStatus, 50, 170);
            var circle = UIFactory.Image(_taskStatus.transform, "Circled", A.inkCircle != null ? A.inkCircle : UIFactory.CircleSprite, Ink.Red, false);
            UIFactory.Stretch(circle.rectTransform, -8, -6, -8, -6);
            _taskStatusCircle = circle.rectTransform;
            UIFactory.OnPaper = false;
            _taskOpen.gameObject.SetActive(false);
        }

        void ResetHud()
        {
            _hudRosterKey = "";
            _lastTick = -1;
            _lastCountdown = -1;
            _taskPinned = false;
            _tallyRequired = _tallyDone = -1;
            _reportNo = 0;
            _seismo?.Clear();
            if (!SecretTaskManager.HasLocalTask) OnTaskChanged(default, false);
            OnCardChanged(CardManager.LocalCard);
        }

        // ------------------------------------------------------------------ Tally marks
        const int MaxTallyMarks = 40;

        void RebuildTally(int required)
        {
            foreach (Transform c in _tallyRow) Destroy(c.gameObject);
            _tallies.Clear();
            var sprite = A.tallyStroke != null ? A.tallyStroke : UIFactory.StrokeSprite;
            var rnd = new System.Random(7919);
            int groups = Mathf.CeilToInt(required / 5f);
            float groupW = 74f, x0 = -(groups * groupW + (groups - 1) * 26f) * 0.5f;
            for (int i = 0; i < required; i++)
            {
                int gi = i / 5, k = i % 5;
                float gx = x0 + gi * (groupW + 26f);
                var img = UIFactory.Image(_tallyRow, "Mark" + i, sprite, Ink.BoneFaint, false);
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                if (k < 4)
                {
                    rt.sizeDelta = new Vector2(11, 54 + (float)rnd.NextDouble() * 8f);
                    rt.anchoredPosition = new Vector2(gx + 10 + k * 17f, (float)rnd.NextDouble() * 4f - 2f);
                    rt.localRotation = Quaternion.Euler(0, 0, (float)rnd.NextDouble() * 8f - 4f);
                }
                else
                {
                    // the fifth mark slashes across the group
                    rt.sizeDelta = new Vector2(11, 86);
                    rt.anchoredPosition = new Vector2(gx + 35f, 0f);
                    rt.localRotation = Quaternion.Euler(0, 0, -62f + (float)rnd.NextDouble() * 6f);
                }
                _tallies.Add(img);
            }
            _tallyDone = -1;
        }

        /// <summary>One scratched mark per good turn - the count only grows; the round ends when the tower falls.</summary>
        void UpdateTally(int done, int layers)
        {
            int shown = Mathf.Min(done, MaxTallyMarks);
            if (shown != _tallyRequired) { _tallyRequired = shown; RebuildTally(shown); }
            if (done == _tallyDone) return;
            bool grew = _tallyDone >= 0 && done > _tallyDone;
            _tallyDone = done;
            for (int i = 0; i < _tallies.Count; i++) _tallies[i].color = Ink.Bone;
            if (grew && _tallies.Count > 0) UITween.Pop(_tallies[_tallies.Count - 1].transform, 1.6f, 0.4f);
            int rnd = _game != null ? _game.RoundNumber.Value : 0, rounds = _game != null ? _game.MatchRounds.Value : 0;
            _hudTallyText.text = L.T($"round {rnd}/{rounds}   -   {done} good turns   -   {layers} floors   -   whoever drops it: -3",
                                     $"el {rnd}/{rounds}   -   {done} başarılı tur   -   {layers} kat   -   kuleyi yıkan: -3");
        }

        // ------------------------------------------------------------------ Task note
        void OnTaskChanged(TaskInfo t, bool justCompleted)
        {
            if (_taskTitle == null) return;
            if (t.Kind == TaskKind.None)
            {
                _taskTitle.text = L.T("Not yet dealt", "Henüz dağıtılmadı");
                _taskDesc.text = L.T("Your task arrives when the round begins. Keep it to yourself.", "Görevin el başlayınca gelir. Kendine sakla.");
                _taskProgress.text = "";
                _taskStatus.text = "";
                _taskStatusCircle.gameObject.SetActive(false);
                return;
            }
            var def = GameContent.Task(t.DefinitionIndex);
            _taskTitle.text = def != null ? def.Title : t.Kind.ToString();
            _taskDesc.text = (def != null ? def.Describe(t) : "") +
                L.T($"\n<i>reward: +{ScoreRules.TaskValue(def, t.Kind)} points (half done: +{ScoreRules.HalfTask})</i>",
                    $"\n<i>ödül: +{ScoreRules.TaskValue(def, t.Kind)} puan (yarısı: +{ScoreRules.HalfTask})</i>");
            _taskProgress.text = L.T($"{t.Progress} of {t.Target}", $"{t.Progress} / {t.Target}");
            _taskStatus.text = t.Failed ? L.T("failed", "başarısız") : (t.Completed ? L.T("done", "tamam") : "");
            _taskStatus.color = t.Failed ? Ink.Red : Ink.Black;
            _taskStatusCircle.gameObject.SetActive(t.Completed || t.Failed);
            if (justCompleted)
            {
                Toast(L.T("Your secret task is done: ", "Gizli görevin tamam: ") + _taskTitle.text, Palette.Good);
                UITween.Pop(_taskFolded, 1.12f, 0.4f);
            }
        }

        void UpdateTaskPanel()
        {
            var kb = Keyboard.current;
            bool look = _taskPinned || (kb != null && kb.tabKey.isPressed && !_modalOpen && !_settingsOpen);
            if (_taskOpen.gameObject.activeSelf != look)
            {
                _taskOpen.gameObject.SetActive(look);
                _taskFolded.gameObject.SetActive(!look);
                if (look)
                {
                    UITween.SlideIn(_taskOpen, new Vector2(60, -20), 0.22f);
                    AudioManager.Instance?.Play(AudioManager.Sfx.UIHover, 0.7f);
                }
            }
        }

        // ------------------------------------------------------------------ Card
        Sprite CardSymbol(CardKind k)
        {
            switch (k)
            {
                case CardKind.SteadyGrip: return A.iconCardSteady;
                case CardKind.Pressure: return A.iconCardPressure;
                case CardKind.SecondChance: return A.iconCardSecond;
                default: return null;
            }
        }

        void OnCardChanged(CardInfo c)
        {
            if (_cardTitle == null) return;
            var def = c.Kind == CardKind.None ? null : GameContent.Card(c.DefinitionIndex);
            _cardTitle.text = def != null ? def.Title : "";
            _cardDesc.text = def != null ? def.Description : L.T("No card yet.", "Henüz kart yok.");
            _cardIcon.sprite = CardSymbol(c.Kind);
            _cardIcon.enabled = _cardIcon.sprite != null;
            _cardMeta.text = def == null ? "" : (def.timing == CardTiming.OwnTurnBeforeExtraction ? L.T("before the block is out", "blok çıkmadan önce") : L.T("before you lay it down", "bloğu koymadan önce")) +
                                               (def.target == CardTarget.NextPlayer ? L.T("  /  on the next", "  /  sıradakine") : "");
        }

        void OnCardPlayed(ulong user, CardKind kind, ulong target, string message)
        {
            Toast(message, Palette.Lamp);
            if (_game != null && _game.NetworkManager != null && target == _game.NetworkManager.LocalClientId && kind == CardKind.Pressure)
                ShowBanner(L.T("They are watching you", "Gözler üzerinde"), Ink.RedBright);
        }

        void PlayCard()
        {
            var game = _game;
            if (game == null || !game.IsSpawned || game.Cards == null || !_cardButton.interactable) return;
            game.Cards.PlayCardServerRpc();
        }

        void UpdateCardPanel(TurnManager turns)
        {
            var c = CardManager.LocalCard;
            bool has = c.Kind != CardKind.None;
            var def = has ? GameContent.Card(c.DefinitionIndex) : null;
            bool myTurn = turns.IsLocalPlayersTurn;
            var st = turns.Stage.Value;
            bool timingOk = def != null && myTurn && (def.timing == CardTiming.OwnTurnBeforeExtraction
                ? st == TurnStage.WaitingForGrab || st == TurnStage.Dragging
                : st == TurnStage.WaitingForGrab || st == TurnStage.Dragging || st == TurnStage.Carrying);
            _cardButton.interactable = has && !c.Used && timingOk;
            _cardPlayedStamp.gameObject.SetActive(has && c.Used);
            _cardState.text = !has ? "" : c.Used ? "" : (!myTurn ? L.T("keep it for your turn", "sıran gelince oyna") : (timingOk ? L.T("C  -  play it", "C  -  oyna") : L.T("too late this turn", "bu tur için geç")));
            _cardState.color = timingOk && !c.Used ? Ink.Bone : Ink.BoneDim;

            // the card lifts a little when it can be played, and sinks once it is spent
            float target = !has ? -260f : c.Used ? -120f : timingOk ? 22f : 0f;
            _cardLift = Mathf.Lerp(_cardLift, target, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            _cardRoot.anchoredPosition = new Vector2(-190, 190 + _cardLift);
            _cardRoot.localRotation = Quaternion.Euler(0, 0, 5f - Mathf.Clamp01(_cardLift / 22f) * 3f);

            var kb = Keyboard.current;
            if (kb != null && kb.cKey.wasPressedThisFrame && _cardButton.interactable && !BlocksGameInput) PlayCard();
        }

        // ------------------------------------------------------------------ Banner
        void ShowBanner(string text, Color color)
        {
            if (_hudBanner == null) return;
            _hudBanner.text = text;
            _hudBanner.color = color;
            _bannerGroup.alpha = 1f;
            UITween.SlideIn(_hudBanner.rectTransform, new Vector2(0, -14), 0.5f);
            UITween.Delay(1.6f, () => { if (_bannerGroup != null) UITween.Fade(_bannerGroup, 0f, 0.8f); });
        }

        void OnTurnStarted(ulong clientId)
        {
            if (_game == null || _hudBanner == null) return;
            bool mine = _game.NetworkManager != null && clientId == _game.NetworkManager.LocalClientId;
            ShowBanner(mine ? L.T("Your turn", "Sıra sende") : L.T($"{_game.PlayerName(clientId)}'s turn", $"Sıra: {_game.PlayerName(clientId)}"), mine ? Palette.Lamp : Ink.Bone);
            _lastTick = -1;
        }

        // ------------------------------------------------------------------ Turn order slip
        void RebuildHudPlayers()
        {
            foreach (Transform c in _orderList) Destroy(c.gameObject);
            _hudRows.Clear();
            var order = new List<ulong>();
            foreach (var id in _game.Turns.TurnOrder) order.Add(id);
            if (order.Count == 0) foreach (var p in _game.PlayersSorted()) order.Add(p.ClientId);
            UIFactory.OnPaper = true;
            foreach (var id in order)
            {
                if (!_game.TryGetPlayer(id, out var p)) continue;
                var line = UIFactory.Write(_orderList, FontKind.Type, "", 21, Ink.Black, TextAnchor.MiddleLeft);
                UIFactory.LE(line, 34);
                var under = UIFactory.Image(line.transform, "Under", UIFactory.StrokeSprite, Ink.Red, false);
                UIFactory.Place(under.rectTransform, new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(26, 4), new Vector2(180, 8));
                var strike = UIFactory.Image(line.transform, "Strike", UIFactory.StrokeSprite, Ink.Black, false);
                UIFactory.Place(strike.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(26, 0), new Vector2(180, 6));
                _hudRows.Add((id, line, under, strike));
            }
            UIFactory.OnPaper = false;
            _orderSlip.sizeDelta = new Vector2(330, 86 + order.Count * 34);
            _orderSlip.anchoredPosition = new Vector2(190, -40 - _orderSlip.sizeDelta.y * 0.5f);
        }

        // ------------------------------------------------------------------ Per frame
        void UpdateHud()
        {
            if (_game == null || !_game.IsSpawned) return;
            var turns = _game.Turns;
            var phase = _game.Phase.Value;

            // turn order (rebuild when order or connection changes)
            var key = "";
            foreach (var id in turns.TurnOrder) key += id + ",";
            foreach (var p in _game.Players) key += p.ClientId + (p.IsConnected ? "c" : "d") + p.ColorIndex + ";";
            if (key != _hudRosterKey) { _hudRosterKey = key; RebuildHudPlayers(); }
            ulong active = turns.ActiveClientId.Value, next = turns.NextClientId.Value;
            ulong me = _game.NetworkManager.LocalClientId;
            foreach (var r in _hudRows)
            {
                bool isActive = r.id == active && phase == GamePhase.Playing;
                bool isNext = !isActive && r.id == next && phase == GamePhase.Playing;
                bool connected = _game.TryGetPlayer(r.id, out var pd) && pd.IsConnected;
                string name = _game.PlayerName(r.id) + (r.id == me ? L.T(" (you)", " (sen)") : (_game.IsBot(r.id) ? " (bot)" : ""));
                r.line.text = (isActive ? "→ " : isNext ? "  " : "  ") + name + (pd.Score != 0 || _game.RoundNumber.Value > 1 ? "   " + pd.Score : "") + (isNext ? L.T("   next", "   sıradaki") : "");
                r.line.color = !connected ? Ink.Faded : isActive ? Ink.Red : Ink.Black;
                r.under.gameObject.SetActive(isActive);
                r.under.rectTransform.sizeDelta = new Vector2(Mathf.Min(260f, r.line.preferredWidth - 20f), 8);
                r.strike.gameObject.SetActive(!connected);
                r.strike.rectTransform.sizeDelta = new Vector2(Mathf.Min(260f, r.line.preferredWidth - 20f), 6);
            }

            // good turns so far (no limit: the round lasts until the tower falls)
            UpdateTally(turns.MovesCompleted.Value, _game.Tower.LayerCount.Value);

            // turn line / countdown / timer
            if (phase == GamePhase.Countdown)
            {
                double left = _game.CountdownEndTime.Value - _game.ServerNow;
                int n = Mathf.CeilToInt((float)left);
                _countdownGroup.alpha = 1f;
                if (n != _lastCountdown && n >= 1 && n <= 3)
                {
                    _lastCountdown = n;
                    _hudCountdown.text = n.ToString();
                    UITween.Pop(_hudCountdown.transform, 1.2f, 0.35f);
                    AudioManager.Instance?.Play(AudioManager.Sfx.Countdown);
                }
                _hudTurnLine.text = L.T("Sit still.", "Kıpırdama.");
                _hudNextLine.text = "";
                _hudTimer.text = "";
                _hudHint.text = L.T("Hold TAB to read your secret task. Your card lies at the bottom right. Whoever drops the tower loses.",
                                    "Gizli görevini okumak için TAB basılı tut. Kartın sağ altta. Kuleyi yıkan kaybeder.");
            }
            else
            {
                if (_lastCountdown != 0 && _countdownGroup.alpha > 0.99f)
                {
                    _lastCountdown = 0;
                    _hudCountdown.text = "";
                    UITween.Fade(_countdownGroup, 0f, 0.3f);
                }
                bool mine = turns.IsLocalPlayersTurn;
                var stage = turns.Stage.Value;
                bool settling = stage == TurnStage.Settling;
                if (phase != GamePhase.Playing) _hudTurnLine.text = L.T("It is over.", "Bitti.");
                else if (mine) _hudTurnLine.text = settling ? L.T("Let it settle.", "Otursun bekle.") : L.T("Your turn.", "Sıra sende.");
                else _hudTurnLine.text = settling ? L.T("The tower settles...", "Kule oturuyor...") : L.T($"{_game.PlayerName(active)} reaches for the tower.", $"{_game.PlayerName(active)} kuleye uzanıyor.");
                _hudTurnLine.color = mine && phase == GamePhase.Playing ? Palette.Lamp : Ink.Bone;
                _hudNextLine.text = phase == GamePhase.Playing && next != ulong.MaxValue && next != active
                    ? (next == me ? L.T("you are next", "sıradaki sensin") : L.T("next:  ", "sıradaki:  ") + _game.PlayerName(next)) : "";

                if (!turns.TimerActive.Value) _hudTimer.text = "";
                else
                {
                    int secs = Mathf.CeilToInt((float)turns.TimeRemaining);
                    _hudTimer.text = settling ? "-:--" : $"{secs / 60}:{secs % 60:00}";
                    bool urgent = !settling && secs <= 10;
                    _hudTimer.color = urgent ? Ink.RedBright : Ink.Bone;
                    if (urgent && mine && secs != _lastTick && secs > 0)
                    {
                        _lastTick = secs;
                        AudioManager.Instance?.Play(AudioManager.Sfx.TimerTick, secs <= 5 ? 1f : 0.5f);
                        UITween.Pop(_hudTimer.transform, 1.1f, 0.2f);
                    }
                }
                var bic = BlockInteractionController.Instance;
                _hudHint.text = bic != null ? bic.Hint : "";
            }

            // active card effects (visible to everyone)
            var effects = new List<string>();
            if (turns.SteadyGripActive.Value) effects.Add(L.T("steady grip", "sağlam tutuş"));
            if (turns.TouchMoveActive.Value) effects.Add(L.T("pressure - touch it, take it", "baskı - dokunduğunu al"));
            if (turns.SecondChanceArmed.Value) effects.Add(L.T("second chance waits", "ikinci şans hazır"));
            _hudEffects.text = effects.Count > 0 ? string.Join("     /     ", effects) : "";

            // tremor
            float w = _game.Stability.Wobble.Value;
            _seismo.Push(w);
            _hudTremorWord.text = w < 0.12f ? L.T("still", "sakin") : (w < 0.4f ? L.T("swaying", "sallanıyor") : L.T("about to fall", "yıkılmak üzere"));
            _hudTremorWord.color = w < 0.4f ? Ink.Black : Ink.Red;
            _seismo.color = Color.Lerp(Ink.Black, Ink.Red, Mathf.Clamp01((w - 0.25f) * 2.5f));

            UpdateTaskPanel();
            UpdateCardPanel(turns);
            UpdateCursorPrompt();
        }

        void UpdateCursorPrompt()
        {
            var bic = BlockInteractionController.Instance;
            var p = bic != null ? bic.CursorPrompt : BlockInteractionController.Prompt.None;
            if (_crosshair != null)
            {
                bool dotOn = bic != null && !BlocksGameInput;
                if (_crosshair.gameObject.activeSelf != dotOn) _crosshair.gameObject.SetActive(dotOn);
                if (dotOn)
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, bic.PointerScreen, null, out var lp);
                    _crosshair.anchoredPosition = lp + _root.rect.size * 0.5f;
                }
            }
            bool show = p != BlockInteractionController.Prompt.None && p != BlockInteractionController.Prompt.Grab && bic != null && !BlocksGameInput;
            if (_promptText.gameObject.activeSelf != show) _promptText.gameObject.SetActive(show);
            if (!show) return;
            switch (p)
            {
                case BlockInteractionController.Prompt.Forbidden: _promptText.text = L.T("not from the top", "üstten alınmaz"); break;
                case BlockInteractionController.Prompt.Committed: _promptText.text = L.T("you touched another one", "başka birine dokundun"); break;
            }
            _promptText.color = Ink.RedBright;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, bic.PointerScreen, null, out var local);
            var size = _root.rect.size;
            _promptRt.anchoredPosition = local + size * 0.5f + new Vector2(26, -18);
        }
    }

    /// <summary>A seismograph strip: the tower's wobble scratched as a jittering ink line that scrolls left.</summary>
    public class Seismograph : MaskableGraphic
    {
        const int N = 120;
        readonly float[] _v = new float[N];
        int _head;
        float _acc, _level, _phase;

        public void Clear() { System.Array.Clear(_v, 0, N); _level = 0f; SetVerticesDirty(); }

        public void Push(float wobble)
        {
            _level = Mathf.Lerp(_level, Mathf.Clamp01(wobble), 0.2f);
            _acc += Time.unscaledDeltaTime;
            if (_acc < 1f / 40f) return;
            _acc = 0f;
            _phase += 1.7f + _level * 3f;
            // a resting needle still trembles a hair; a swaying tower swings it hard
            float amp = 0.035f + _level * 0.95f;
            float s = Mathf.Sin(_phase) * 0.6f + (Random.value - 0.5f) * 0.8f;
            _v[_head] = Mathf.Clamp(s * amp, -1f, 1f);
            _head = (_head + 1) % N;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            float mid = r.center.y, half = r.height * 0.46f, thick = 1.6f;
            var c = (Color32)color;
            // faint baseline
            AddLine(vh, new Vector2(r.xMin, mid), new Vector2(r.xMax, mid), 0.6f, new Color32(c.r, c.g, c.b, (byte)(c.a * 0.25f)));
            Vector2 prev = default;
            for (int i = 0; i < N; i++)
            {
                float v = _v[(_head + i) % N];
                var p = new Vector2(Mathf.Lerp(r.xMin, r.xMax, i / (float)(N - 1)), mid + v * half);
                if (i > 0) AddLine(vh, prev, p, thick, c);
                prev = p;
            }
        }

        static void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float w, Color32 c)
        {
            var d = (b - a);
            if (d.sqrMagnitude < 1e-4f) return;
            var n = new Vector2(-d.y, d.x).normalized * w;
            int i = vh.currentVertCount;
            vh.AddVert(a - n, c, Vector2.zero);
            vh.AddVert(a + n, c, Vector2.zero);
            vh.AddVert(b + n, c, Vector2.zero);
            vh.AddVert(b - n, c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
