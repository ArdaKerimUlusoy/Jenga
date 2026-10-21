using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SecretJenga.UI
{
    public enum ButtonStyle { Primary, Secondary, Danger, Ghost, Good }

    /// <summary>Builds styled uGUI widgets in code so every screen shares one look and needs no hand-wired references.</summary>
    public static class UIFactory
    {
        static SecretJengaAssets A => SecretJengaAssets.Instance;
        public static Font Font => RuntimeArt.Font;

        public static Sprite PanelSprite => A.panel != null ? A.panel : RuntimeArt.Rounded;
        public static Sprite ButtonSprite => A.button != null ? A.button : RuntimeArt.Rounded;
        public static Sprite RoundedSprite => A.rounded != null ? A.rounded : RuntimeArt.Rounded;
        public static Sprite PillSprite => A.pill != null ? A.pill : RuntimeArt.Rounded;
        public static Sprite CircleSprite => A.circle != null ? A.circle : RuntimeArt.Circle;

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

        // ------------------------------------------------------------------ Visual primitives
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

        /// <summary>Wooden card with a soft drop shadow.</summary>
        public static Image Card(Transform parent, string name, Color? color = null)
        {
            var shadow = Image(parent, name + "_Shadow", RoundedSprite, new Color(0, 0, 0, 0.35f));
            var card = Image(shadow.transform, name, PanelSprite, color ?? Palette.Walnut);
            card.raycastTarget = true;
            Stretch(card.rectTransform, 0, 0, 0, 6);
            shadow.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            return card;
        }

        public static Text Text(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal, string name = "Text")
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            return t;
        }

        public static Shadow AddShadow(Graphic g, Color c, Vector2 dist)
        {
            var s = g.gameObject.AddComponent<Shadow>();
            s.effectColor = c;
            s.effectDistance = dist;
            return s;
        }

        // ------------------------------------------------------------------ Buttons
        public static void StyleColors(ButtonStyle style, out Color normal, out Color text)
        {
            switch (style)
            {
                case ButtonStyle.Secondary: normal = Palette.Paper; text = Palette.Ink; break;
                case ButtonStyle.Danger: normal = Palette.Bad; text = Color.white; break;
                case ButtonStyle.Ghost: normal = new Color(1, 1, 1, 0.08f); text = Palette.Cream; break;
                case ButtonStyle.Good: normal = Palette.Good; text = Color.white; break;
                default: normal = Palette.Accent; text = Palette.WalnutDark; break;
            }
        }

        public static Button Button(Transform parent, string label, UnityAction onClick, ButtonStyle style = ButtonStyle.Primary, int fontSize = 30, Sprite icon = null)
        {
            StyleColors(style, out var normal, out var textColor);
            var img = Image(parent, "Btn_" + label, ButtonSprite, Color.white);
            img.raycastTarget = true;
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var cb = btn.colors;
            cb.normalColor = normal;
            cb.highlightedColor = Color.Lerp(normal, Color.white, 0.18f);
            cb.pressedColor = Color.Lerp(normal, Color.black, 0.15f);
            cb.selectedColor = normal;
            cb.disabledColor = new Color(normal.r * 0.6f, normal.g * 0.6f, normal.b * 0.6f, 0.5f);
            cb.fadeDuration = 0.08f;
            btn.colors = cb;
            if (onClick != null) btn.onClick.AddListener(onClick);

            var row = Rect("Content", img.transform);
            Stretch(row, 16, 4, 16, 4);
            var h = HLayout(row, 12, 0, TextAnchor.MiddleCenter, false);
            h.childForceExpandHeight = false;

            if (icon != null)
            {
                var ic = Image(row, "Icon", icon, textColor, false);
                ic.preserveAspect = true;
                LE(ic, fontSize * 1.1f, fontSize * 1.1f);
            }
            var t = Text(row, label, fontSize, textColor, TextAnchor.MiddleCenter, FontStyle.Bold, "Label");
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            LE(t, fontSize * 1.4f);
            img.gameObject.AddComponent<UIButtonFX>();
            return btn;
        }

        public static void SetLabel(Button b, string label)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = label;
        }

        public static Button IconButton(Transform parent, Sprite icon, UnityAction onClick, Color bg, Color fg, float size = 64)
        {
            var img = Image(parent, "IconBtn", CircleSprite, bg, false);
            img.raycastTarget = true;
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(onClick);
            var ic = Image(img.transform, "Icon", icon != null ? icon : CircleSprite, fg, false);
            ic.preserveAspect = true;
            Stretch(ic.rectTransform, size * 0.22f, size * 0.22f, size * 0.22f, size * 0.22f);
            img.gameObject.AddComponent<UIButtonFX>();
            LE(img, size, size);
            img.rectTransform.sizeDelta = new Vector2(size, size);
            return btn;
        }

        // ------------------------------------------------------------------ Inputs
        public static InputField Input(Transform parent, string placeholder, string value, int charLimit, int fontSize = 30)
        {
            var bg = Image(parent, "Input", RoundedSprite, new Color(0.10f, 0.06f, 0.04f, 0.65f));
            bg.raycastTarget = true;
            var field = bg.gameObject.AddComponent<InputField>();
            var text = Text(bg.transform, "", fontSize, Palette.Cream, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform, 20, 6, 20, 6);
            text.supportRichText = false;
            var ph = Text(bg.transform, placeholder, fontSize, new Color(1, 1, 1, 0.35f), TextAnchor.MiddleLeft, FontStyle.Italic);
            Stretch(ph.rectTransform, 20, 6, 20, 6);
            field.textComponent = text;
            field.placeholder = ph;
            field.characterLimit = charLimit;
            field.text = value ?? "";
            field.caretColor = Palette.Accent;
            field.selectionColor = new Color(Palette.Accent.r, Palette.Accent.g, Palette.Accent.b, 0.4f);
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

        public static StepperRef Stepper(Transform parent, string label, int min, int max, int value, int step, Func<int, string> format, Action<int> onChanged)
        {
            var row = Rect("Stepper_" + label, parent);
            LE(row, 64);
            var h = HLayout(row, 10, 0, TextAnchor.MiddleLeft, false);
            h.childForceExpandHeight = true;
            var l = Text(row, label, 28, Palette.Cream, TextAnchor.MiddleLeft);
            LE(l, -1, -1, 1);
            var s = new StepperRef { Min = min, Max = max, Step = step, Format = format, OnChanged = onChanged };
            s.Minus = Button(row, "-", () => s.Set(s.Value - s.Step, true), ButtonStyle.Secondary, 34);
            LE(s.Minus, 60, 64);
            var vb = Image(row, "Value", PillSprite, new Color(0, 0, 0, 0.3f));
            LE(vb, 60, 150);
            s.ValueText = Text(vb.transform, "", 30, Palette.Cream, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(s.ValueText.rectTransform);
            s.Plus = Button(row, "+", () => s.Set(s.Value + s.Step, true), ButtonStyle.Secondary, 34);
            LE(s.Plus, 60, 64);
            s.Set(value, false);
            return s;
        }

        public static Slider Slider(Transform parent, string label, float value, Action<float> onChanged)
        {
            var row = Rect("Slider_" + label, parent);
            LE(row, 60);
            var h = HLayout(row, 16, 0, TextAnchor.MiddleLeft, false);
            var l = Text(row, label, 28, Palette.Cream, TextAnchor.MiddleLeft);
            LE(l, -1, 260);

            var root = Rect("Slider", row);
            LE(root, 40, -1, 1);
            var slider = root.gameObject.AddComponent<Slider>();
            var bg = Image(root, "Track", PillSprite, new Color(0, 0, 0, 0.35f));
            Stretch(bg.rectTransform, 0, 14, 0, 14);
            var fillArea = Rect("FillArea", root);
            Stretch(fillArea, 8, 14, 8, 14);
            var fill = Image(fillArea, "Fill", PillSprite, Palette.Accent);
            Stretch(fill.rectTransform);
            var handleArea = Rect("HandleArea", root);
            Stretch(handleArea, 14, 0, 14, 0);
            var handle = Image(handleArea, "Handle", CircleSprite, Palette.Cream, false);
            handle.raycastTarget = true;
            handle.rectTransform.sizeDelta = new Vector2(34, 0);
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.value = value;
            if (onChanged != null) slider.onValueChanged.AddListener(v => onChanged(v));
            return slider;
        }

        public static Toggle Toggle(Transform parent, string label, bool value, Action<bool> onChanged)
        {
            var row = Rect("Toggle_" + label, parent);
            LE(row, 56);
            var h = HLayout(row, 16, 0, TextAnchor.MiddleLeft, false);
            var box = Image(row, "Box", RoundedSprite, new Color(0, 0, 0, 0.35f));
            box.raycastTarget = true;
            LE(box, 44, 44);
            var check = Image(box.transform, "Check", A.iconCheck != null ? A.iconCheck : RoundedSprite, Palette.Accent, false);
            Stretch(check.rectTransform, 7, 7, 7, 7);
            var l = Text(row, label, 28, Palette.Cream, TextAnchor.MiddleLeft);
            LE(l, -1, -1, 1);
            var t = row.gameObject.AddComponent<Toggle>();
            t.targetGraphic = box;
            t.graphic = check;
            t.isOn = value;
            if (onChanged != null) t.onValueChanged.AddListener(v => onChanged(v));
            row.gameObject.AddComponent<UIButtonFX>().scaleOnHover = 1.0f;
            return t;
        }

        /// <summary>Round avatar with initial letter in the player's color.</summary>
        public static Image Avatar(Transform parent, string name, Color color, float size)
        {
            var ring = Image(parent, "Avatar", CircleSprite, Palette.Cream, false);
            LE(ring, size, size);
            ring.rectTransform.sizeDelta = new Vector2(size, size);
            var inner = Image(ring.transform, "Fill", CircleSprite, color, false);
            Stretch(inner.rectTransform, size * 0.07f, size * 0.07f, size * 0.07f, size * 0.07f);
            var letter = Text(inner.transform, string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant(), Mathf.RoundToInt(size * 0.5f), Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(letter.rectTransform);
            AddShadow(letter, new Color(0, 0, 0, 0.35f), new Vector2(1, -2));
            return ring;
        }

        public static Text Title(Transform parent, string text, int size = 54)
        {
            var t = Text(parent, text, size, Palette.Cream, TextAnchor.MiddleCenter, FontStyle.Bold, "Title");
            AddShadow(t, new Color(0, 0, 0, 0.5f), new Vector2(2, -3));
            LE(t, size * 1.5f);
            return t;
        }

        public static Image Divider(Transform parent)
        {
            var d = Image(parent, "Divider", RoundedSprite, new Color(1, 1, 1, 0.12f));
            LE(d, 3);
            return d;
        }
    }
}
