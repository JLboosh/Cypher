using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Cypher
{
    /// <summary>
    /// The IMPORT console pad on the left of the desk: frosted glass, corner brackets, an animated
    /// "download into tray" icon, a slow light sweep, and a lift + glow on hover.
    /// </summary>
    public class DeskImportButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        const float W = 230f, H = 86f, UnitsToMeters = 0.001f;

        Action onClick;
        RectTransform arrow, sweep;
        Image[] brackets;
        Image border;
        TextMeshProUGUI title;
        bool hovered;
        float hover, flash;
        Vector3 baseScale;

        public static DeskImportButton Create(HoloUI ui, Vector3 position, Action onClick)
        {
            var go = new GameObject("Import Desk Button");
            go.transform.position = position;
            var cam = Camera.main.transform.position;
            // Face the seat, leaning back a little like a console.
            go.transform.rotation = Quaternion.LookRotation(position - cam, Vector3.up) * Quaternion.Euler(-12f, 0f, 0f);
            var button = go.AddComponent<DeskImportButton>();
            button.onClick = onClick;
            button.Build(ui);
            return button;
        }

        void Build(HoloUI ui)
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvas.sortingOrder = 7;
            canvasGo.AddComponent<GraphicRaycaster>();
            var rect = (RectTransform)canvasGo.transform;
            rect.sizeDelta = new Vector2(W, H);
            rect.localScale = Vector3.one * UnitsToMeters;
            baseScale = transform.localScale;

            ui.Glass(rect, W, H, 6);
            border = ui.Frame("Border", rect, 0, 0, W, H, new Color(0.35f, 0.8f, 1f, 0.35f));

            // Corner brackets.
            const float L = 18f, T = 3f;
            brackets = new[]
            {
                ui.Glow("TL h", rect, 0, 0, L, T, Color.white), ui.Glow("TL v", rect, 0, 0, T, L, Color.white),
                ui.Glow("TR h", rect, W - L, 0, L, T, Color.white), ui.Glow("TR v", rect, W - T, 0, T, L, Color.white),
                ui.Glow("BL h", rect, 0, H - T, L, T, Color.white), ui.Glow("BL v", rect, 0, H - L, T, L, Color.white),
                ui.Glow("BR h", rect, W - L, H - T, L, T, Color.white), ui.Glow("BR v", rect, W - T, H - L, T, L, Color.white),
            };

            // Icon: a tray (U shape) with an arrow dropping into it.
            var icon = HoloUI.Rect("Icon", rect, 18, 16, 54, 54);
            var iconColor = new Color(0.55f, 0.95f, 1f, 1f);
            ui.Glow("Tray bottom", icon, 6, 46, 42, 4, iconColor);
            ui.Glow("Tray left", icon, 6, 32, 4, 18, iconColor);
            ui.Glow("Tray right", icon, 44, 32, 4, 18, iconColor);
            arrow = HoloUI.Rect("Arrow", icon, 0, 0, 54, 54);
            ui.Glow("Shaft", arrow, 25, 4, 4, 28, iconColor);
            var left = ui.Glow("Head L", arrow, 17, 26, 14, 4, iconColor);
            left.rectTransform.pivot = new Vector2(1f, 0.5f);
            left.rectTransform.anchoredPosition = new Vector2(29f, -34f);
            left.rectTransform.localRotation = Quaternion.Euler(0, 0, 45f);
            var right = ui.Glow("Head R", arrow, 25, 26, 14, 4, iconColor);
            right.rectTransform.pivot = new Vector2(0f, 0.5f);
            right.rectTransform.anchoredPosition = new Vector2(25f, -34f);
            right.rectTransform.localRotation = Quaternion.Euler(0, 0, -45f);

            title = ui.Label("Title", rect, 84, 14, W - 96, 34, "IMPORT", 22, TextAlignmentOptions.Left, HoloUI.TextColor);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 8f;
            ui.Label("Subtitle", rect, 85, 48, W - 96, 20, "Calendar · Tasks · Notion", 12, TextAlignmentOptions.Left, HoloUI.LabelColor);

            // A soft light band sweeping across now and then.
            var band = ui.Glow("Sweep", rect, 0, 4, 10, H - 8, new Color(0.5f, 0.9f, 1f, 0.12f));
            sweep = band.rectTransform;

            // Invisible full-size hit area for the pointer.
            var hit = HoloUI.Rect("Hit", rect, 0, 0, W, H).gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            hit.gameObject.AddComponent<PointerRelay>().Target = this;
        }

        void Update()
        {
            float dt = Time.deltaTime, t = Time.time;
            hover = Mathf.MoveTowards(hover, hovered ? 1f : 0f, dt * 6f);
            flash = Mathf.MoveTowards(flash, 0f, dt * 2.5f);

            transform.localScale = baseScale * (1f + 0.05f * hover + 0.04f * flash);
            arrow.anchoredPosition = new Vector2(0f, -(2f + 3f * Mathf.Sin(t * (2f + 3f * hover))) * (0.5f + hover));

            float pulse = 0.65f + 0.2f * Mathf.Sin(t * 1.4f) + 0.35f * hover + flash;
            var c = new Color(0.45f, 0.95f, 1f, Mathf.Clamp01(pulse));
            foreach (var b in brackets) b.color = c;
            border.color = new Color(0.35f, 0.8f, 1f, 0.3f + 0.4f * hover + 0.5f * flash);

            float cycle = (t * 0.35f) % 1f;
            sweep.anchoredPosition = new Vector2(Mathf.Lerp(-10f, W, cycle), -4f);
        }

        public void OnPointerEnter(PointerEventData e)
        {
            hovered = true;
            CypherAudio.Play(Sfx.Hover, transform.position, 0.7f);
        }

        public void OnPointerExit(PointerEventData e) => hovered = false;

        public void OnPointerClick(PointerEventData e)
        {
            flash = 1f;
            CypherAudio.Play(Sfx.Click, transform.position);
            onClick?.Invoke();
        }

        /// <summary>Forwards pointer events from the hit area (a child) to the button.</summary>
        class PointerRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
        {
            public DeskImportButton Target;
            public void OnPointerEnter(PointerEventData e) => Target.OnPointerEnter(e);
            public void OnPointerExit(PointerEventData e) => Target.OnPointerExit(e);
            public void OnPointerClick(PointerEventData e) => Target.OnPointerClick(e);
        }
    }
}
