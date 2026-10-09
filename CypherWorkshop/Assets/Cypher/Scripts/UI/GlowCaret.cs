using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Cypher
{
    /// <summary>
    /// Draws a glowing, blinking text cursor for a TMP_InputField (the field's own caret is
    /// hidden because it can't glow). Finds the caret's spot from TextMeshPro's character layout.
    /// Stays solid while you type and starts blinking once you pause.
    /// </summary>
    public class GlowCaret : MonoBehaviour
    {
        [SerializeField] float blinkRate = 1.1f;

        TMP_InputField field;
        Image bar;
        int lastIndex = -1;
        float lastMoveTime;

        public void Init(TMP_InputField inputField, Image caretBar)
        {
            field = inputField;
            bar = caretBar;
            bar.enabled = false;
        }

        void LateUpdate()
        {
            if (field == null || bar == null) return;

            bool hasSelection = field.selectionAnchorPosition != field.caretPosition;
            if (!field.isFocused || hasSelection)
            {
                bar.enabled = false;
                return;
            }

            int index = field.stringPosition;
            if (index != lastIndex)
            {
                lastIndex = index;
                lastMoveTime = Time.unscaledTime;
            }
            float phase = (Time.unscaledTime - lastMoveTime) * blinkRate;
            bar.enabled = phase < 0.5f || phase % 1f < 0.6f;
            if (!bar.enabled) return;

            CaretInTextSpace(index, out Vector3 local, out float height);
            var text = field.textComponent.rectTransform;
            var viewport = field.textViewport;

            // Keep the cursor inside the visible text area, then express it in the field's space.
            Vector3 inViewport = viewport.InverseTransformPoint(text.TransformPoint(local));
            Rect r = viewport.rect;
            inViewport.x = Mathf.Clamp(inViewport.x, r.xMin, r.xMax);
            inViewport.y = Mathf.Clamp(inViewport.y, r.yMin, r.yMax);

            bar.rectTransform.localPosition = transform.InverseTransformPoint(viewport.TransformPoint(inViewport));
            bar.rectTransform.sizeDelta = new Vector2(3f, height);
        }

        void CaretInTextSpace(int index, out Vector3 local, out float height)
        {
            var text = field.textComponent;
            var info = text.textInfo;
            Rect r = text.rectTransform.rect;

            if (info == null || info.characterCount == 0)
            {
                height = text.fontSize * 1.2f;
                float y = field.lineType == TMP_InputField.LineType.SingleLine ? r.center.y : r.yMax - height * 0.5f;
                local = new Vector3(r.xMin + 1f, y, 0f);
                return;
            }

            bool atEnd = index >= info.characterCount;
            var c = info.characterInfo[atEnd ? info.characterCount - 1 : index];
            var line = info.lineInfo[c.lineNumber];
            height = line.ascender - line.descender;
            float centerY = (line.ascender + line.descender) * 0.5f;

            if (!atEnd)
                local = new Vector3(c.origin, centerY, 0f);
            else if (c.character == '\n')
                local = new Vector3(r.xMin + 1f, centerY - height, 0f); // start of the new empty line
            else
                local = new Vector3(c.xAdvance, centerY, 0f);
        }
    }
}
