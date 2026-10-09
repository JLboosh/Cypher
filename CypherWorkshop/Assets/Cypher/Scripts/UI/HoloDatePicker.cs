using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Cypher
{
    /// <summary>
    /// Holographic month calendar shown beside the edit panel. Call Show() with a starting date
    /// and a callback; clicking a day (or a quick button) reports the date.
    /// </summary>
    public class HoloDatePicker : MonoBehaviour
    {
        public const float Width = 300f;
        public const float Height = 364f;

        const float CellW = 36f, CellH = 32f, CellStepX = 38f, CellStepY = 36f, GridX = 17f, GridY = 100f;

        readonly Button[] cells = new Button[42];
        readonly TextMeshProUGUI[] cellLabels = new TextMeshProUGUI[42];
        readonly Image[] cellFills = new Image[42];
        readonly Image[] todayMarks = new Image[42];
        readonly DateTime[] cellDates = new DateTime[42];

        TextMeshProUGUI title;
        TextMeshProUGUI monthLabel;
        DateTime month;
        DateTime? selected;
        Action<DateTime> onPicked;

        public bool IsOpen => gameObject.activeSelf;

        public void Build(HoloUI ui)
        {
            var root = transform;
            ui.Frame("Background", root, 0, 0, Width, Height, new Color(0.3f, 0.75f, 1f, 0.8f));
            ui.Glass(root, Width, Height, 8);

            title = ui.Label("Title", root, 14, 10, Width - 28, 22, "DATE", 13, TextAlignmentOptions.Left, HoloUI.LabelColor);
            ui.Button("Prev", root, 12, 40, 36, 30, "<", 18, () => ChangeMonth(-1));
            monthLabel = ui.Label("Month", root, 52, 40, 196, 30, "", 18, TextAlignmentOptions.Center, HoloUI.TextColor);
            ui.Button("Next", root, 252, 40, 36, 30, ">", 18, () => ChangeMonth(1));

            var dayNames = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
            for (int i = 0; i < 7; i++)
                ui.Label("Weekday", root, GridX + i * CellStepX, 78, CellW, 18,
                    dayNames[i].Substring(0, Math.Min(2, dayNames[i].Length)).ToUpperInvariant(), 11,
                    TextAlignmentOptions.Center, HoloUI.LabelColor);

            for (int i = 0; i < 42; i++)
            {
                int index = i;
                float x = GridX + (i % 7) * CellStepX;
                float y = GridY + (i / 7) * CellStepY;
                var seg = ui.Segment($"Day {i}", root, x, y, CellW, CellH, "", 15, () => Pick(cellDates[index]));
                cells[i] = seg.Button;
                cellLabels[i] = seg.Button.GetComponentInChildren<TextMeshProUGUI>();
                cellFills[i] = seg.Button.transform.Find("Selected").GetComponent<Image>();
                todayMarks[i] = ui.Glow("Today", seg.Button.transform, CellW / 2f - 7f, CellH - 6f, 14f, 2f, new Color(1f, 0.75f, 0.35f, 1f));
            }

            ui.Button("Today", root, 12, 326, 88, 28, "Today", 13, () => Pick(DateTime.Today));
            ui.Button("Tomorrow", root, 106, 326, 88, 28, "Tomorrow", 13, () => Pick(DateTime.Today.AddDays(1)));
            ui.Button("Next week", root, 200, 326, 88, 28, "+1 week", 13, () => Pick(DateTime.Today.AddDays(7)));

            gameObject.SetActive(false);
        }

        public void Show(DateTime? initial, string heading, Action<DateTime> picked)
        {
            selected = initial?.Date;
            var start = initial ?? DateTime.Today;
            month = new DateTime(start.Year, start.Month, 1);
            onPicked = picked;
            title.text = heading;
            gameObject.SetActive(true);
            Redraw();
            CypherAudio.Play(Sfx.Open, transform.position, 0.5f);
        }

        public void Hide() => gameObject.SetActive(false);

        void ChangeMonth(int delta)
        {
            month = month.AddMonths(delta);
            Redraw();
        }

        void Pick(DateTime date)
        {
            selected = date.Date;
            onPicked?.Invoke(date.Date);
        }

        void Redraw()
        {
            monthLabel.text = month.ToString("MMMM yyyy", CultureInfo.CurrentCulture).ToUpperInvariant();
            var first = month.AddDays(-(int)month.DayOfWeek);
            for (int i = 0; i < 42; i++)
            {
                var date = first.AddDays(i);
                cellDates[i] = date;
                bool inMonth = date.Month == month.Month;
                cellLabels[i].text = date.Day.ToString(CultureInfo.InvariantCulture);
                cellLabels[i].alpha = inMonth ? 1f : 0.35f;
                cellFills[i].enabled = selected.HasValue && date == selected.Value;
                todayMarks[i].enabled = date == DateTime.Today;
            }
        }
    }
}
