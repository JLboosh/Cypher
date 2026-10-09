using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Cypher
{
    /// <summary>
    /// Small factory for holographic UI on world-space canvases. Every element is placed with
    /// top-left coordinates in canvas units (1 unit = 1 mm on the edit panel), so layouts read
    /// like a sketch: Button("Save", parent, x: 420, y: 530, w: 116, h: 44, ...).
    /// </summary>
    public class HoloUI
    {
        public static readonly Color TextColor = new Color(0.94f, 0.99f, 1f, 1f);
        public static readonly Color LabelColor = new Color(0.62f, 0.92f, 1f, 1f);
        public static readonly Color FrameColor = new Color(0.35f, 0.8f, 1f, 0.9f);
        public static readonly Color WarnColor = new Color(1f, 0.55f, 0.3f, 1f);

        public readonly TMP_FontAsset Font;
        public readonly Material TextMaterial;
        public readonly Material RectMaterial;
        public readonly Material FlatMaterial;
        public readonly Material GlassMaterial;

        public HoloUI(TMP_FontAsset font, Material textMaterial, Material rectMaterial, Material flatMaterial, Material glassMaterial)
        {
            Font = font;
            TextMaterial = textMaterial;
            RectMaterial = rectMaterial;
            FlatMaterial = flatMaterial;
            GlassMaterial = glassMaterial;
        }

        public static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Place(rt, x, y, w, h);
            return rt;
        }

        /// <summary>Positions a rect by its top-left corner inside its parent's rect.</summary>
        public static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        public static void Stretch(RectTransform rt, float padding = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
        }

        /// <summary>
        /// Frosted-glass backing (Cypher/UIGlass) so text stays readable. Glowing (additive) layers
        /// can only add light, so this is a normal alpha-blended layer on its own sub-canvas.
        /// sortingOrder must be ABOVE the task holograms (0-4) so they can't shine through,
        /// and below the panel's own content: use the panel canvas's order minus 2.
        /// </summary>
        public Image Glass(Transform parent, float w, float h, int sortingOrder)
        {
            var rt = Rect("Glass", parent, 0, 0, w, h);
            rt.SetAsFirstSibling();
            var canvas = rt.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
            var img = rt.gameObject.AddComponent<Image>();
            img.material = GlassMaterial;
            img.color = Color.white;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// Text material tuned for reading: near-white (not HDR, so Bloom doesn't smear it)
        /// with a soft dark underlay that separates letters from glowing backgrounds.
        /// </summary>
        public static Material ReadableTextMaterial(TMP_FontAsset font)
        {
            var m = new Material(font.material) { name = "Readable Text (runtime)" };
            m.SetColor("_FaceColor", Color.white);
            m.EnableKeyword("UNDERLAY_ON");
            m.SetColor("_UnderlayColor", new Color(0f, 0.015f, 0.04f, 0.9f));
            m.SetFloat("_UnderlayDilate", 0.3f);
            m.SetFloat("_UnderlaySoftness", 0.35f);
            return m;
        }

        /// <summary>Glowing outlined rectangle.</summary>
        public Image Frame(string name, Transform parent, float x, float y, float w, float h, Color color)
        {
            var img = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Image>();
            img.material = RectMaterial;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Solid glowing rectangle (selected states, indicators, the cursor).</summary>
        public Image Glow(string name, Transform parent, float x, float y, float w, float h, Color color)
        {
            var img = Frame(name, parent, x, y, w, h, color);
            img.material = FlatMaterial;
            return img;
        }

        public TextMeshProUGUI Label(string name, Transform parent, float x, float y, float w, float h,
            string text, float size, TextAlignmentOptions align, Color color)
        {
            var t = Rect(name, parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            Style(t, size, color);
            t.text = text;
            t.alignment = align;
            return t;
        }

        public void Style(TMP_Text t, float size, Color color)
        {
            t.font = Font;
            t.fontSharedMaterial = TextMaterial;
            t.fontSize = size;
            t.color = color;
            t.raycastTarget = false;
            t.richText = false;
            t.overflowMode = TextOverflowModes.Truncate;
        }

        public Button Button(string name, Transform parent, float x, float y, float w, float h,
            string text, float fontSize, Action onClick)
        {
            var img = Frame(name, parent, x, y, w, h, FrameColor);
            img.raycastTarget = true;

            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            // Additive blending: alpha acts as brightness. Hover brightens, press flares.
            button.colors = new ColorBlock
            {
                normalColor = new Color(1f, 1f, 1f, 0.6f),
                highlightedColor = new Color(1f, 1f, 1f, 1f),
                pressedColor = new Color(1f, 1f, 1f, 1f),
                selectedColor = new Color(1f, 1f, 1f, 0.6f),
                disabledColor = new Color(1f, 1f, 1f, 0.15f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };

            var label = Label("Label", img.transform, 0, 0, w, h, text, fontSize, TextAlignmentOptions.Center, TextColor);
            Stretch(label.rectTransform, 4f);

            img.gameObject.AddComponent<UIHoverSound>();
            button.onClick.AddListener(() =>
            {
                CypherAudio.Play(Sfx.Click, button.transform.position);
                onClick?.Invoke();
            });
            return button;
        }

        /// <summary>A button with a glowing fill that shows when it's the selected option.</summary>
        public HoloSegment Segment(string name, Transform parent, float x, float y, float w, float h,
            string text, float fontSize, Action onClick)
        {
            var button = Button(name, parent, x, y, w, h, text, fontSize, onClick);
            var fill = Glow("Selected", button.transform, 0, 0, w, h, new Color(0.25f, 0.7f, 1f, 0.28f));
            Stretch(fill.rectTransform, 2f);
            fill.transform.SetAsFirstSibling(); // behind the label
            return new HoloSegment(button, fill);
        }

        public HoloToggle Toggle(string name, Transform parent, float x, float y, float w, float h,
            string text, float fontSize, Action<bool> onChanged)
        {
            HoloToggle toggle = null;
            var button = Button(name, parent, x, y, w, h, "", fontSize, () => toggle.Set(!toggle.Value, notify: true));

            float box = Mathf.Min(18f, h - 12f);
            var frame = Frame("Box", button.transform, 10, (h - box) / 2f, box, box, FrameColor);
            var check = Glow("Check", frame.transform, 4, 4, box - 8, box - 8, new Color(0.6f, 1f, 1f, 1f));

            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            label.text = text;
            label.alignment = TextAlignmentOptions.Left;
            label.rectTransform.offsetMin = new Vector2(box + 18f, label.rectTransform.offsetMin.y);

            toggle = new HoloToggle(button, check, onChanged);
            return toggle;
        }

        /// <summary>Text box with holographic frame, placeholder and a glowing cursor.</summary>
        public TMP_InputField InputField(string name, Transform parent, float x, float y, float w, float h,
            string placeholder, float fontSize, bool multiLine)
        {
            var go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
            go.name = name;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Place(rt, x, y, w, h);

            var img = go.GetComponent<Image>();
            img.sprite = null;
            img.material = RectMaterial;
            img.color = FrameColor;

            var field = go.GetComponent<TMP_InputField>();
            field.transition = Selectable.Transition.None;
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            field.lineType = multiLine ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            field.richText = false; // keeps caret indexes identical to string indexes (GlowCaret relies on it)
            field.onFocusSelectAll = false;
            field.restoreOriginalTextOnEscape = false;
            field.customCaretColor = true;
            field.caretColor = new Color(0f, 0f, 0f, 0f); // hidden: GlowCaret draws the cursor
            field.selectionColor = new Color(0.3f, 0.8f, 1f, 0.35f);
            field.pointSize = fontSize;

            var area = field.textViewport;
            area.offsetMin = new Vector2(12f, 6f);
            area.offsetMax = new Vector2(-12f, -6f);

            var text = (TextMeshProUGUI)field.textComponent;
            Style(text, fontSize, TextColor);
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = multiLine ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Left;
            text.textWrappingMode = multiLine ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;

            var ph = (TextMeshProUGUI)field.placeholder;
            Style(ph, fontSize, new Color(0.55f, 0.78f, 0.92f, 0.55f));
            ph.text = placeholder;
            ph.fontStyle = FontStyles.Italic;
            ph.alignment = text.alignment;

            var bar = Glow("Glow Caret", rt, 0, 0, 3f, fontSize * 1.2f, new Color(0.7f, 1f, 1f, 1f));
            bar.rectTransform.anchorMin = bar.rectTransform.anchorMax = bar.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            go.AddComponent<GlowCaret>().Init(field, bar);
            return field;
        }
    }

    /// <summary>On/off control: a framed box that lights up when on.</summary>
    public class HoloToggle
    {
        public readonly Button Button;
        readonly Image check;
        readonly Action<bool> onChanged;

        public bool Value { get; private set; }

        public HoloToggle(Button button, Image check, Action<bool> onChanged)
        {
            Button = button;
            this.check = check;
            this.onChanged = onChanged;
            Set(false, notify: false);
        }

        public void Set(bool value, bool notify)
        {
            Value = value;
            check.enabled = value;
            if (notify) onChanged?.Invoke(value);
        }
    }

    /// <summary>One option of a segmented choice (priority, difficulty).</summary>
    public class HoloSegment
    {
        public readonly Button Button;
        readonly Image fill;

        public HoloSegment(Button button, Image fill)
        {
            Button = button;
            this.fill = fill;
            SetSelected(false);
        }

        public void SetSelected(bool selected) => fill.enabled = selected;
    }

    /// <summary>Soft tick when the mouse moves onto a UI button.</summary>
    public class UIHoverSound : MonoBehaviour, IPointerEnterHandler
    {
        public void OnPointerEnter(PointerEventData eventData)
        {
            var selectable = GetComponent<Selectable>();
            if (selectable == null || selectable.interactable)
                CypherAudio.Play(Sfx.Hover, transform.position, 0.6f);
        }
    }
}
