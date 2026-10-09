using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Cypher
{
    /// <summary>
    /// Fires reminders. Checks every second; when a task's reminder time arrives it:
    ///   - pulses the task's hologram,
    ///   - sends a macOS notification (seen even when the app is minimized or behind other windows),
    ///   - has Cypher say it out loud (then you can answer "snooze", "in 20 minutes", "mark it done").
    /// Reminders that came due while the app was closed are announced together at startup.
    /// Keeps running while minimized (Application.runInBackground, set by TaskManager).
    /// </summary>
    public class ReminderService : MonoBehaviour
    {
        [Tooltip("Wait this long after launch before announcing missed reminders (lets the scene load).")]
        [SerializeField] float startupDelaySeconds = 4f;
        [SerializeField] float hologramPulseSeconds = 10f;

        CypherConfig config;
        float nextCheck;
        bool startupDone;

        void Start()
        {
            config = CypherConfig.Load();
            nextCheck = Time.unscaledTime + startupDelaySeconds;
        }

        void Update()
        {
            if (Time.unscaledTime < nextCheck || TaskManager.Instance == null) return;
            nextCheck = Time.unscaledTime + 1f;

            var now = DateTime.Now;
            var due = TaskManager.Instance.Repository.Tasks
                .Where(t => !t.reminderFired && t.Reminder.HasValue && t.Reminder.Value <= now)
                .ToList();
            if (due.Count == 0)
            {
                startupDone = true;
                return;
            }

            foreach (var t in due) t.reminderFired = true;
            var active = due.Where(t => !t.done).ToList(); // done tasks: mark fired silently
            TaskManager.Instance.Save();
            if (active.Count == 0) return;

            Fire(active, missed: !startupDone);
            startupDone = true;
        }

        void Fire(List<TaskItem> tasks, bool missed)
        {
            foreach (var t in tasks) TaskManager.Instance.GetPanel(t.id)?.Pulse(hologramPulseSeconds);

            if (config.macNotifications)
            {
                string title = missed ? "Cypher: missed reminders" : "Cypher reminder";
                string body = tasks.Count == 1 ? Describe(tasks[0]) : string.Join(" · ", tasks.Select(t => t.title));
                MacNotifier.Show(title, body, config.notificationSound);
            }

            var voice = VoiceAssistant.Instance;
            if (voice != null) voice.AnnounceReminders(tasks, missed);
            else CypherAudio.Play(Sfx.Sync, Vector3.zero);

            Debug.Log($"[Cypher] Reminder{(missed ? " (missed)" : "")}: {string.Join(", ", tasks.Select(t => t.title))}");
        }

        static string Describe(TaskItem t) =>
            t.title + (t.DueDate.HasValue ? " (due " + NaturalDate.Describe(t.DueDate.Value, true, DateTime.Now) + ")" : "");
    }
}
