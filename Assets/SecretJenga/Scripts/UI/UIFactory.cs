using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SecretJenga.UI
{
    public enum ButtonStyle { Primary, Secondary, Danger, Ghost, Good }

    /// <summary>
    /// Crayon and pencil colours of the kids' room. On paper (notebook pages, sticky notes) things are written in
    /// navy pencil and red crayon; straight over the room the text is chalk white. (The names stay from the old
    /// ink-on-paper look so the screens did not have to change.)
    /// </summary>
    public static class Ink
    {
        public static readonly Color Black = new Color(0.15f, 0.2f, 0.38f);          // navy pencil
        public static readonly Color Faded = new Color(0.4f, 0.45f, 0.6f);
        public static readonly Color Pencil = new Color(0.4f, 0.45f, 0.6f, 0.45f);
        public static readonly Color Red = new Color(0.88f, 0.26f, 0.24f);           // red crayon
        public static readonly Color RedBright = new Color(1f, 0.5f, 0.36f);         // on the room: a warm coral
        public static readonly Color Green = new Color(0.2f, 0.6f, 0.32f);
        public static readonly Color Bone = new Color(1f, 0.98f, 0.94f);             // chalk white
        public static readonly Color BoneDim = new Color(0.84f, 0.86f, 0.94f);
        public static readonly Color BoneFaint = new Color(1f, 0.98f, 0.94f, 0.4f);
        public static readonly Color Shadow = new Color(0.05f, 0.05f, 0.15f, 0.75f);
    }

    public enum FontKind { Title, Body, Type }

    /// <summary>
    /// Builds the kids'-room uGUI in code: notebook pages and sticky notes held by washi tape, fields on ruled lines,
    /// words you click (a crayon line draws itself under them), sticker dots for players. Everything is generated
    /// from a few sprites (Tools~/generate_ui_textures_kids.py) and three fonts (Baloo 2, Patrick Hand, Grandstander -
    /// SIL OFL, see Fonts/). Missing assets fall back gracefully.
    /// </summary>
    public static class UIFactory
    {
        static SecretJengaAssets A => SecretJengaAssets.Instance;

        /// <summary>True while building widgets that sit on paper (dark ink); false over the dark room (bone ink).</summary>
        public static bool OnPaper;

        public static Font Font => A.fontBody != null ? A.fontBody : RuntimeArt.Font;
        public static Font TitleFont => A.fontTitle != null ? A.fontTitle : Font;
        public static Font TypeFont => A.fontType != null ? A.fontType : Font;
        static Font FontOf(FontKind k) => k == FontKind.Title ? TitleFont : k == FontKind.Type ? TypeFont : Font;

        public static Color InkC => OnPaper ? Ink.Black : Ink.Bone;
        public static Color FadedC => OnPaper ? Ink.Faded : Ink.BoneDim;
        public static Color AccentC => OnPaper ? Ink.Red : Ink.RedBright;

        public static Sprite RoundedSprite => A.rounded != null ? A.rounded : RuntimeArt.Rounded;
        public static Sprite PillSprite => A.pill != null ? A.pill : RuntimeArt.Rounded;
        public static Sprite CircleSprite => A.circle != null ? A.circle : RuntimeArt.Circle;
        public static Sprite PaperSprite => A.paper != null ? A.paper : RuntimeArt.Rounded;
        public static Sprite SlipSprite => A.paperSlip != null ? A.paperSlip : PaperSprite;
        public static Sprite StrokeSprite => A.inkStroke != null ? A.inkStroke : RuntimeArt.Rounded;
        public static Sprite BlotSprite => A.inkBlot != null ? A.inkBlot : RuntimeArt.Circle;

        static Sprite _fade;
        /// <summary>Horizontal black-to-clear gradient (keeps menu text readable over the 3D room).</summary>
        public static Sprite FadeSprite
        {
            get
            {
                if (_fade != null) return _fade;
                var t = new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "TLB_UIFade" };
                for (int x = 0; x < 256; x++)
                {
                    float k = x / 255f;
                    t.SetPixel(x, 0, new Color(0, 0, 0, Mathf.Pow(1f - k, 1.6f)));
                }
                t.Apply();
                _fade = Sprite.Create(t, new Rect(0, 0, 256, 1), new Vector2(0.5f, 0.5f));
                return _fade;
            }
        }

        // ------------------------------------------------------------------ Rect helpers
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static LayoutElement LE(Component c, float prefHeight = -1, float prefWidth = -1, float flexW = -1, float flexH = -1)
        {
            var le = c.gameObject.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            if (prefHeight >= 0) { le.preferredHeight = prefHeight; le.minHeight = prefHeight; }
            if (prefWidth >= 0) { le.preferredWidth = prefWidth; le.minWidth = prefWidth; }
            if (flexW >= 0) le.flexibleWidth = flexW;
            if (flexH >= 0) le.flexibleHeight = flexH;
            return le;
        }

        public static VerticalLayoutGroup VLayout(Component c, float spacing, int pad, TextAnchor align = TextAnchor.UpperCenter, bool expandWidth = true)
        {
            var v = c.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(pad, pad, pad, pad);
            v.childAlignment = align;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = expandWidth;
            v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HLayout(Component c, float spacing, int pad, TextAnchor align = TextAnchor.MiddleCenter, bool expandWidth = true)
        {
            var h = c.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(pad, pad, pad, pad);
            h.childAlignment = align;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = expandWidth;
            h.childForceExpandHeight = true;
            return h;
        }

        // ------------------------------------------------------------------ Primitives
        public static Image Image(Transform parent, string name, Sprite sprite, Color color, bool sliced = true)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = sliced && sprite != null && sprite.border != Vector4.zero ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            img.raycastTarget = false;
            return img;
        }

        public static Text Text(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal, string name = "Text")
            => Write(parent, FontKind.Body, text, size, color, align, style, name);

        /// <summary>Text in one of the three faces (Title = engraved small caps, Body = old book face, Type = typewriter).</summary>
        public static Text Write(Transform parent, FontKind kind, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal, string name = "Text")
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = FontOf(kind);
            // the faces have no real bold - fake weight looks digital, so keep them as drawn
            t.fontStyle = kind == FontKind.Body ? (style == FontStyle.Bold ? FontStyle.Normal : style == FontStyle.BoldAndItalic ? FontStyle.Italic : style) : FontStyle.Normal;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = kind == FontKind.Type ? 1.05f : 0.95f;
            t.raycastTarget = false;
            t.supportRichText = true;
            if (!OnPaper && kind != FontKind.Title) AddShadow(t, new Color(0, 0, 0, 0.8f), new Vector2(1.5f, -2f));
            return t;
        }

        public static Shadow AddShadow(Graphic g, Color c, Vector2 dist)
        {
            var s = g.gameObject.AddComponent<Shadow>();
            s.effectColor = c;
            s.effectDistance = dist;
            return s;
        }

        /// <summary>Spaced capitals for small labels: "T U R N  O R D E R".</summary>
        public static string Spaced(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var ch in L.Upper(s)) { sb.Append(ch); sb.Append(ch == ' ' ? "  " : " "); }
            return sb.ToString().TrimEnd();
        }

        public static Text Title(Transform parent, string text, int size = 54)
        {
            var t = Write(parent, FontKind.Title, text, size, InkC, TextAnchor.MiddleLeft, FontStyle.Normal, "Title");
            if (!OnPaper) AddShadow(t, new Color(0, 0, 0, 0.9f), new Vector2(2, -3));
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            LE(t, size * 1.25f);
            return t;
        }

        /// <summary>Small typed caption (a form field label, a section mark).</summary>
        public static Text Caption(Transform parent, string text, int size = 20)
        {
            var t = Write(parent, FontKind.Type, text, size, FadedC, TextAnchor.MiddleLeft);
            LE(t, size * 1.4f);
            return t;
        }

        /// <summary>A hand-drawn ink rule.</summary>
        public static Image Divider(Transform parent, float height = 9f)
        {
            var d = Image(parent, "Rule", StrokeSprite, OnPaper ? Ink.Pencil : new Color(Ink.Bone.r, Ink.Bone.g, Ink.Bone.b, 0.22f), false);
            LE(d, height);
            return d;
        }

        // ------------------------------------------------------------------ Paper
        public class Sheet
        {
            public RectTransform Root, Paper, Body;
            public Image PaperImage;
        }

        /// <summary>
        /// A torn sheet of paper taped over the scene. Root carries the position + slight tilt, Body is a padded
        /// vertical column for content. Sets OnPaper = true (dark ink) for whatever is built next.
        /// </summary>
        public static Sheet PaperSheet(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, float tilt, int tapes = 2, int pad = 56, bool slip = false)
        {
            var s = new Sheet();
            s.Root = Rect(name, parent);
            Place(s.Root, anchor, new Vector2(0.5f, 0.5f), pos, size);
            s.Root.localRotation = Quaternion.Euler(0, 0, tilt);

            var sprite = slip ? SlipSprite : PaperSprite;
            var shadow = Image(s.Root, "Shadow", sprite, new Color(0, 0, 0, 0.6f));
            Stretch(shadow.rectTransform, -6, 2, -14, -18);
            var paper = Image(s.Root, "Paper", sprite, Color.white);
            paper.raycastTarget = true;
            Stretch(paper.rectTransform);
            s.Paper = paper.rectTransform;
            s.PaperImage = paper;

            if (A.tape != null)
            {
                var rnd = new System.Random(name.GetHashCode());
                for (int i = 0; i < tapes; i++)
                {
                    float side = tapes == 1 ? 0f : (i == 0 ? -1f : 1f);
                    var tape = Image(s.Root, "Tape", A.tape, new Color(1f, 1f, 1f, 0.92f), false);
                    var x = side * (size.x * 0.5f - 46f);
                    Place(tape.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(x, -4f), new Vector2(slip ? 120 : 160, slip ? 36 : 46));
                    tape.rectTransform.localRotation = Quaternion.Euler(0, 0, side * -(22f + (float)rnd.NextDouble() * 14f) + (side == 0 ? (float)rnd.NextDouble() * 8f - 4f : 0f));
                }
            }

            s.Body = Rect("Body", s.Root);
            Stretch(s.Body, pad, pad - 6, pad, pad - 10);
            VLayout(s.Body, 12, 0, TextAnchor.UpperLeft);
            OnPaper = true;
            return s;
        }

        // ------------------------------------------------------------------ Buttons: words you click
        public static Button Button(Transform parent, string label, UnityAction onClick, ButtonStyle style = ButtonStyle.Primary, int fontSize = 30, Sprite icon = null)
        {
            var root = Rect("Btn_" + label, parent);
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0); // invisible hit area
            var btn = root.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = hit;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;
            if (onClick != null) btn.onClick.AddListener(onClick);

            Color ink = style == ButtonStyle.Danger ? AccentC : style == ButtonStyle.Ghost ? FadedC : InkC;
            var kind = style == ButtonStyle.Primary || style == ButtonStyle.Danger || style == ButtonStyle.Good ? FontKind.Title : FontKind.Body;

            var underline = Image(root, "Underline", StrokeSprite, AccentC, false);
            underline.type = UnityEngine.UI.Image.Type.Filled;
            underline.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            underline.fillAmount = 0f;

            var t = Write(root, kind, label, fontSize, ink, TextAnchor.MiddleLeft, style == ButtonStyle.Ghost ? FontStyle.Italic : FontStyle.Normal, "Label");
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(t.rectTransform, 0, 0, 0, 4);

            var fx = root.gameObject.AddComponent<InkButtonFX>();
            fx.Init(btn, t, underline, ink, AccentC);
            LE(root, fontSize * 1.55f, t.preferredWidth + 24f);
            return btn;
        }

        public static void SetLabel(Button b, string label)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = label;
        }

        /// <summary>Makes a plain row of buttons line up from the left (menus are written, not boxed).</summary>
        public static RectTransform ButtonRow(Transform parent, float height = 70, float spacing = 46, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var row = Rect("Buttons", parent);
            LE(row, height);
            var h = HLayout(row, spacing, 0, align, false);
            h.childControlWidth = true;
            h.childForceExpandHeight = true;
            return row;
        }

        /// <summary>A small clickable symbol (refresh, copy...) drawn in ink.</summary>
        public static Button IconButton(Transform parent, Sprite icon, UnityAction onClick, Color bg, Color fg, float size = 64)
        {
            var root = Rect("IconBtn", parent);
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            var btn = root.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = hit;
            if (onClick != null) btn.onClick.AddListener(onClick);
            var ic = Image(root, "Icon", icon != null ? icon : BlotSprite, fg, false);
            ic.preserveAspect = true;
            Stretch(ic.rectTransform, size * 0.16f, size * 0.16f, size * 0.16f, size * 0.16f);
            root.gameObject.AddComponent<InkButtonFX>().Init(btn, null, null, fg, AccentC, ic);
            LE(root, size, size);
            root.sizeDelta = new Vector2(size, size);
            return btn;
        }

        // ------------------------------------------------------------------ Inputs
        /// <summary>A typewritten field on a ruled line.</summary>
        public static InputField Input(Transform parent, string placeholder, string value, int charLimit, int fontSize = 30)
        {
            var root = Rect("Field", parent);
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            var line = Image(root, "Line", StrokeSprite, OnPaper ? Ink.Pencil : Ink.BoneFaint, false);
            Place(line.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 2), Vector2.zero);
            line.rectTransform.anchorMin = new Vector2(0f, 0f);
            line.rectTransform.anchorMax = new Vector2(1f, 0f);
            line.rectTransform.sizeDelta = new Vector2(0, 8);
            var field = root.gameObject.AddComponent<InputField>();
            var nav = field.navigation;
            nav.mode = Navigation.Mode.None;
            field.navigation = nav;
            field.transition = Selectable.Transition.None;
            var text = Write(root, FontKind.Type, "", fontSize, InkC, TextAnchor.LowerLeft);
            Stretch(text.rectTransform, 4, 0, 4, 10);
            text.supportRichText = false;
            var ph = Write(root, FontKind.Body, placeholder, fontSize, OnPaper ? Ink.Pencil : Ink.BoneFaint, TextAnchor.LowerLeft, FontStyle.Italic);
            Stretch(ph.rectTransform, 4, 0, 4, 10);
            field.textComponent = text;
            field.placeholder = ph;
            field.characterLimit = charLimit;
            field.text = value ?? "";
            field.caretColor = AccentC;
            field.caretWidth = 2;
            field.selectionColor = new Color(Ink.Red.r, Ink.Red.g, Ink.Red.b, 0.25f);
            var fx = root.gameObject.AddComponent<FieldFX>();
            fx.Line = line;
            fx.Idle = line.color;
            fx.Active = AccentC;
            fx.Field = field;
            return field;
        }

        public class StepperRef
        {
            public int Value;
            public int Min, Max, Step;
            public Text ValueText;
            public Button Minus, Plus;
            public Func<int, string> Format;
            public Action<int> OnChanged;

            public void Set(int v, bool notify)
            {
                Value = Mathf.Clamp(v, Min, Max);
                ValueText.text = Format != null ? Format(Value) : Value.ToString();
                Minus.interactable = Interactable && Value > Min;
                Plus.interactable = Interactable && Value < Max;
                if (notify) OnChanged?.Invoke(Value);
            }

            bool _interactable = true;
            public bool Interactable
            {
                get => _interactable;
                set { _interactable = value; Set(Value, false); }
            }
        }

        /// <summary>"Turn timer ............ ‹ 60 s ›"</summary>
        public static StepperRef Stepper(Transform parent, string label, int min, int max, int value, int step, Func<int, string> format, Action<int> onChanged)
        {
            var row = Rect("Stepper_" + label, parent);
            LE(row, 50);
            var h = HLayout(row, 6, 0, TextAnchor.MiddleLeft, false);
            h.childForceExpandHeight = true;
            var l = Write(row, FontKind.Body, label, 27, InkC, TextAnchor.MiddleLeft);
            LE(l, -1, -1, 1);
            var s = new StepperRef { Min = min, Max = max, Step = step, Format = format, OnChanged = onChanged };
            s.Minus = Button(row, "‹", () => s.Set(s.Value - s.Step, true), ButtonStyle.Secondary, 38);
            LE(s.Minus, 50, 34);
            s.ValueText = Write(row, FontKind.Type, "", 28, InkC, TextAnchor.MiddleCenter);
            LE(s.ValueText, 50, 120);
            s.Plus = Button(row, "›", () => s.Set(s.Value + s.Step, true), ButtonStyle.Secondary, 38);
            LE(s.Plus, 50, 34);
            s.Set(value, false);
            return s;
        }

        /// <summary>A pencil line with an ink blot you drag.</summary>
        public static Slider Slider(Transform parent, string label, float value, Action<float> onChanged)
        {
            var row = Rect("Slider_" + label, parent);
            LE(row, 50);
            HLayout(row, 18, 0, TextAnchor.MiddleLeft, false);
            var l = Write(row, FontKind.Body, label, 27, InkC, TextAnchor.MiddleLeft);
            LE(l, -1, 300);

            var root = Rect("Slider", row);
            LE(root, 40, -1, 1);
            var slider = root.gameObject.AddComponent<Slider>();
            var nav = slider.navigation;
            nav.mode = Navigation.Mode.None;
            slider.navigation = nav;
            slider.transition = Selectable.Transition.None;
            var bg = Image(root, "Track", StrokeSprite, OnPaper ? Ink.Pencil : Ink.BoneFaint, false);
            Stretch(bg.rectTransform, 0, 16, 0, 16);
            var fillArea = Rect("FillArea", root);
            Stretch(fillArea, 0, 15, 12, 15);
            var fill = Image(fillArea, "Fill", StrokeSprite, InkC, false);
            Stretch(fill.rectTransform);
            var handleArea = Rect("HandleArea", root);
            Stretch(handleArea, 12, 0, 12, 0);
            var handle = Image(handleArea, "Blot", BlotSprite, AccentC, false);
            handle.raycastTarget = true;
            handle.rectTransform.sizeDelta = new Vector2(30, 0);
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.value = value;
            if (onChanged != null) slider.onValueChanged.AddListener(v => onChanged(v));
            return slider;
        }

        /// <summary>"[x]  label" on a typewriter.</summary>
        public static Toggle Toggle(Transform parent, string label, bool value, Action<bool> onChanged)
        {
            var row = Rect("Toggle_" + label, parent);
            LE(row, 46);
            HLayout(row, 14, 0, TextAnchor.MiddleLeft, false);
            var hit = row.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            var box = Write(row, FontKind.Type, "[   ]", 26, InkC, TextAnchor.MiddleLeft, FontStyle.Normal, "Box");
            LE(box, 46, 66);
            var check = Write(box.transform, FontKind.Title, "x", 34, AccentC, TextAnchor.MiddleCenter, FontStyle.Normal, "Check");
            Stretch(check.rectTransform, 0, -6, 6, 0);
            check.rectTransform.localRotation = Quaternion.Euler(0, 0, -8);
            var l = Write(row, FontKind.Body, label, 25, InkC, TextAnchor.MiddleLeft);
            LE(l, -1, -1, 1);
            var t = row.gameObject.AddComponent<Toggle>();
            var nav = t.navigation;
            nav.mode = Navigation.Mode.None;
            t.navigation = nav;
            t.transition = Selectable.Transition.None;
            t.targetGraphic = hit;
            t.graphic = check;
            t.isOn = value;
            if (onChanged != null) t.onValueChanged.AddListener(v => onChanged(v));
            row.gameObject.AddComponent<InkButtonFX>().Init(t, l, null, InkC, AccentC);
            return t;
        }

        // ------------------------------------------------------------------ Marks
        /// <summary>A player's mark: a blot of their ink with their initial scratched out of it.</summary>
        public static Image Avatar(Transform parent, string name, Color color, float size)
        {
            var blot = Image(parent, "Mark", BlotSprite, color, false);
            LE(blot, size, size);
            blot.rectTransform.sizeDelta = new Vector2(size, size);
            blot.rectTransform.localRotation = Quaternion.Euler(0, 0, (name != null ? name.GetHashCode() % 40 : 0));
            var letter = Write(blot.transform, FontKind.Title, string.IsNullOrEmpty(name) ? "?" : L.Upper(name.Substring(0, 1)),
                Mathf.RoundToInt(size * 0.55f), new Color(0.95f, 0.92f, 0.85f, 0.92f), TextAnchor.MiddleCenter);
            Stretch(letter.rectTransform, 0, 0, 0, size * 0.04f);
            letter.rectTransform.localRotation = Quaternion.Inverse(blot.rectTransform.localRotation);
            return blot;
        }

        /// <summary>A rubber stamp: boxed letters in ink, slightly rotated.</summary>
        public static RectTransform Stamp(Transform parent, string text, Color color, Vector2 size, float angle)
        {
            var root = Rect("Stamp", parent);
            root.sizeDelta = size;
            root.localRotation = Quaternion.Euler(0, 0, angle);
            if (A.stamp != null)
            {
                var frame = Image(root, "Frame", A.stamp, color, false);
                Stretch(frame.rectTransform);
            }
            var t = Write(root, FontKind.Title, text, Mathf.RoundToInt(size.y * 0.5f), color, TextAnchor.MiddleCenter, FontStyle.Normal, "StampText");
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(t.rectTransform, 10, 4, 10, 0);
            return root;
        }
    }

    /// <summary>
    /// Hover feedback for written buttons: an ink underline is drawn under the word, the word moves a little and
    /// turns red; disabled words fade like pencil. Also plays the UI sounds.
    /// </summary>
    public class InkButtonFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
    {
        Selectable _sel;
        Text _label;
        Image _underline, _icon;
        Color _ink, _hover;
        bool _over;
        float _k;
        Vector2 _labelBase;
        bool _hasBase;

        public void Init(Selectable sel, Text label, Image underline, Color ink, Color hover, Image icon = null)
        {
            _sel = sel;
            _label = label;
            _underline = underline;
            _ink = ink;
            _hover = hover;
            _icon = icon;
        }

        bool Usable => _sel == null || _sel.IsInteractable();

        public void OnPointerEnter(PointerEventData e)
        {
            _over = true;
            if (Usable) AudioManager.Instance?.Play(AudioManager.Sfx.UIHover, 0.4f);
        }

        public void OnPointerExit(PointerEventData e) => _over = false;

        public void OnPointerDown(PointerEventData e)
        {
            if (!Usable) return;
            AudioManager.Instance?.Play(AudioManager.Sfx.UIClick, 0.8f);
            _k = Mathf.Min(_k, 0.5f);
        }

        void OnDisable() { _over = false; _k = 0f; Apply(); }

        void Update()
        {
            float target = _over && Usable ? 1f : 0f;
            _k = Mathf.MoveTowards(_k, target, Time.unscaledDeltaTime * (target > _k ? 5f : 7f));
            Apply();
        }

        LayoutElement _le;
        string _sized;

        void Apply()
        {
            bool usable = Usable;
            if (_label != null && _underline != null && _sized != _label.text)
            {
                // written buttons are as wide as their word
                _sized = _label.text;
                if (_le == null) _le = GetComponent<LayoutElement>();
                if (_le != null) { _le.preferredWidth = _label.preferredWidth + 24f; _le.minWidth = _le.preferredWidth; }
            }
            float e = 1f - (1f - _k) * (1f - _k);
            var c = Color.Lerp(_ink, _hover, e);
            if (!usable) c.a *= 0.32f;
            if (_label != null)
            {
                if (!_hasBase) { _labelBase = _label.rectTransform.anchoredPosition; _hasBase = true; }
                _label.color = c;
                _label.rectTransform.anchoredPosition = _labelBase + new Vector2(e * 8f, 0f);
            }
            if (_icon != null) _icon.color = c;
            if (_underline != null && _label != null)
            {
                _underline.fillAmount = e;
                var rt = _underline.rectTransform;
                float w = Mathf.Min(_label.preferredWidth + 20f, ((RectTransform)transform).rect.width + 40f);
                rt.anchorMin = rt.anchorMax = new Vector2(_label.alignment == TextAnchor.MiddleCenter ? 0.5f : 0f, 0f);
                rt.pivot = new Vector2(_label.alignment == TextAnchor.MiddleCenter ? 0.5f : 0f, 0.5f);
                rt.sizeDelta = new Vector2(w, Mathf.Max(8f, _label.fontSize * 0.28f));
                rt.anchoredPosition = new Vector2(_label.alignment == TextAnchor.MiddleCenter ? 0f : -4f, 6f);
            }
        }
    }

    /// <summary>The ruled line under a typed field turns to red ink while you type in it.</summary>
    public class FieldFX : MonoBehaviour
    {
        public Image Line;
        public Color Idle, Active;
        public InputField Field;

        void Update()
        {
            if (Line == null || Field == null) return;
            Line.color = Color.Lerp(Line.color, Field.isFocused ? Active : Idle, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
        }
    }
}
