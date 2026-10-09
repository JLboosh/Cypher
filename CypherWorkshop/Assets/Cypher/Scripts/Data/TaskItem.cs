using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Cypher
{
    public enum PriorityOverride { Auto = 0, Low = 1, Medium = 2, High = 3 }

    public enum TaskSource { Local = 0, GoogleCalendar = 1, Notion = 2, GoogleTasks = 3 }

    /// <summary>
    /// One to-do item. Plain data only, so JsonUtility can save and load it.
    /// JsonUtility cannot store DateTime, so dates are kept as ISO-8601 strings
    /// ("" means "not set") and exposed through the DueDate/Reminder properties.
    /// </summary>
    [Serializable]
    public class TaskItem
    {
        public string id;
        public string title;
        public string notes;

        public string dueDateIso;
        public string reminderIso;
        public bool reminderFired;

        public PriorityOverride priority = PriorityOverride.Auto;
        [Range(1, 5)] public int difficulty = 3;
        public bool done;

        public string createdIso;
        public string updatedIso;

        // Filled in by the Google Calendar / Notion importers (Phase 7).
        public TaskSource source = TaskSource.Local;
        public string externalId;
        // Details needed to sync back (calendar/list ids, Notion column names...). See IntegrationHub.
        public string externalMeta;
        // Set when you chose "Update original" for an imported task; Phase 7 pushes it and clears it.
        public bool pendingSync;

        // True once the user has dragged this hologram by hand.
        public bool userPlaced;
        public Vector3 position;

        public static TaskItem Create(string title)
        {
            string now = FormatDate(DateTime.Now);
            return new TaskItem
            {
                id = Guid.NewGuid().ToString("N"),
                title = title,
                notes = "",
                dueDateIso = "",
                reminderIso = "",
                createdIso = now,
                updatedIso = now,
            };
        }

        public DateTime? DueDate
        {
            get => ParseDate(dueDateIso);
            set => dueDateIso = FormatDate(value);
        }

        public DateTime? Reminder
        {
            get => ParseDate(reminderIso);
            set => reminderIso = FormatDate(value);
        }

        public void Touch() => updatedIso = FormatDate(DateTime.Now);

        public TaskItem Clone() => JsonUtility.FromJson<TaskItem>(JsonUtility.ToJson(this));

        /// <summary>Copies the fields the edit panel can change (not id, source, position...).</summary>
        public void CopyEditableFieldsFrom(TaskItem other)
        {
            title = other.title;
            notes = other.notes;
            dueDateIso = other.dueDateIso;
            reminderIso = other.reminderIso;
            priority = other.priority;
            difficulty = other.difficulty;
            done = other.done;
        }

        // Dates are always local wall-clock time. A DateTime built from parts (new DateTime(y, m, d),
        // e.g. by the calendar) has no time zone ("Unspecified"); it must be treated as local,
        // not as UTC, or times shift by the UTC offset (a 9 AM reminder firing at 5 AM in New York).
        static DateTime? ParseDate(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return null;
            if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)) return null;
            return d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Local) : d.ToLocalTime();
        }

        static string FormatDate(DateTime? date)
        {
            if (!date.HasValue) return "";
            var d = date.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(date.Value, DateTimeKind.Local) : date.Value;
            return d.ToString("o", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The whole save file: a version number plus the list of tasks.</summary>
    [Serializable]
    public class TaskDatabase
    {
        public int version = 1;
        public List<TaskItem> tasks = new List<TaskItem>();
        // What to do with holograms you dragged when Cypher re-ranks: "ask" (once), "keep", or "auto".
        public string placementMode = "ask";
    }
}
