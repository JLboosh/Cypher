using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Cypher
{
    /// <summary>
    /// Cypher's smart ranking.
    ///
    /// Urgency (0..2) grows as a deadline approaches. Harder tasks get a longer "lead time", so a
    /// difficulty-5 task due in two days counts as more urgent than a difficulty-1 task due then.
    /// Overdue tasks are the most urgent of all; tasks with no deadline sit low.
    ///
    /// Each task then gets a priority class (High / Medium / Low). If you set the priority
    /// yourself, that class is used as-is: manual priority always wins. "Let Cypher decide"
    /// tasks get their class from urgency. Tasks sort by class, then by urgency, then oldest first.
    /// </summary>
    public static class TaskRanker
    {
        public const float HighThreshold = 0.5f;
        public const float MediumThreshold = 0.2f;

        public static float Urgency(TaskItem task, DateTime now)
        {
            int difficulty = Mathf.Clamp(task.difficulty, 1, 5);
            float weight = 0.75f + 0.1f * difficulty; // 0.85 .. 1.25
            var due = task.DueDate;
            if (!due.HasValue) return 0.06f * weight;

            double hoursLeft = (due.Value - now).TotalHours;
            if (hoursLeft <= 0) return 1.5f + (float)Math.Min(0.5, -hoursLeft / 48.0); // overdue
            double leadHours = 24.0 * (0.5 + 0.35 * difficulty); // 20 h (easy) .. 54 h (hard)
            return (float)(weight / (1.0 + hoursLeft / leadHours));
        }

        /// <summary>What "Let Cypher decide" resolves to right now.</summary>
        public static PriorityOverride AutoPriority(TaskItem task, DateTime now)
        {
            float u = Urgency(task, now);
            return u >= HighThreshold ? PriorityOverride.High
                 : u >= MediumThreshold ? PriorityOverride.Medium
                 : PriorityOverride.Low;
        }

        public static PriorityOverride EffectivePriority(TaskItem task, DateTime now) =>
            task.priority == PriorityOverride.Auto ? AutoPriority(task, now) : task.priority;

        /// <summary>Unfinished tasks most-important-first, then finished tasks.</summary>
        public static List<TaskItem> Rank(IEnumerable<TaskItem> tasks, DateTime now) =>
            tasks.OrderBy(t => t.done)
                 .ThenByDescending(t => (int)EffectivePriority(t, now))
                 .ThenByDescending(t => Urgency(t, now))
                 .ThenBy(t => t.createdIso, StringComparer.Ordinal)
                 .ToList();
    }
}
