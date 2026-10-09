using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Cypher
{
    /// <summary>
    /// Google Calendar (events) and Google Tasks: list, import, push edits back, delete.
    /// Calendar events: title = summary, notes = description, due = start time (all-day = end of that day).
    /// Google Tasks: title, notes, due (Google stores a date only), done = completed.
    /// </summary>
    public class GoogleClient
    {
        const string CalendarApi = "https://www.googleapis.com/calendar/v3";
        const string TasksApi = "https://tasks.googleapis.com/tasks/v1";

        readonly GoogleAuth auth;
        readonly CypherConfig config;

        public GoogleClient(GoogleAuth auth, CypherConfig config)
        {
            this.auth = auth;
            this.config = config;
        }

        // ------------------------------------------------------------------ JSON shapes (JsonUtility)

        [Serializable] class CalendarEntry { public string id; public string summary; public bool primary; public string accessRole; }
        [Serializable] class CalendarList { public CalendarEntry[] items; }
        [Serializable] class EventTime { public string dateTime; public string date; }
        [Serializable] class GEvent { public string id; public string summary; public string description; public string status; public EventTime start; public EventTime end; }
        [Serializable] class EventList { public GEvent[] items; }
        [Serializable] class TaskListEntry { public string id; public string title; }
        [Serializable] class TaskLists { public TaskListEntry[] items; }
        [Serializable] class GTask { public string id; public string title; public string notes; public string due; public string status; }
        [Serializable] class GTaskList { public GTask[] items; }

        // ------------------------------------------------------------------ Calendar

        public async Task<List<ImportContainer>> ListCalendars()
        {
            var r = await Get(CalendarApi + "/users/me/calendarList?minAccessRole=reader");
            if (r == null) return null;
            var list = JsonUtility.FromJson<CalendarList>(r).items ?? new CalendarEntry[0];
            return list.OrderByDescending(c => c.primary)
                       .Select(c => new ImportContainer { Id = c.id, Name = c.primary ? c.summary + " (primary)" : c.summary, Meta = c.accessRole })
                       .ToList();
        }

        public async Task<List<ImportCandidate>> ListEvents(string calendarId)
        {
            string timeMin = Uri.EscapeDataString(ApiDates.ToRfc3339(DateTime.Now.AddDays(-1)));
            string timeMax = Uri.EscapeDataString(ApiDates.ToRfc3339(DateTime.Now.AddDays(config.importDaysAhead)));
            var r = await Get($"{CalendarApi}/calendars/{Uri.EscapeDataString(calendarId)}/events" +
                              $"?timeMin={timeMin}&timeMax={timeMax}&singleEvents=true&orderBy=startTime&maxResults=250");
            if (r == null) return null;

            var result = new List<ImportCandidate>();
            foreach (var e in JsonUtility.FromJson<EventList>(r).items ?? new GEvent[0])
            {
                if (e.status == "cancelled") continue;
                bool allDay = string.IsNullOrEmpty(e.start?.dateTime);
                var start = allDay ? ApiDates.ParseDateOnly(e.start?.date) : ApiDates.ParseDateTime(e.start.dateTime);
                var end = allDay ? null : ApiDates.ParseDateTime(e.end?.dateTime);
                int minutes = start.HasValue && end.HasValue ? (int)(end.Value - start.Value).TotalMinutes : 60;
                result.Add(new ImportCandidate
                {
                    Source = TaskSource.GoogleCalendar,
                    ExternalId = $"gcal|{calendarId}|{e.id}",
                    Meta = MetaString.Build(("cal", calendarId), ("event", e.id), ("allday", allDay ? "1" : "0"), ("minutes", minutes.ToString(CultureInfo.InvariantCulture))),
                    Title = string.IsNullOrWhiteSpace(e.summary) ? "(no title)" : e.summary,
                    Notes = e.description ?? "",
                    Due = start,
                });
            }
            return result;
        }

        // ------------------------------------------------------------------ Tasks

        public async Task<List<ImportContainer>> ListTaskLists()
        {
            var r = await Get(TasksApi + "/users/@me/lists?maxResults=100");
            if (r == null) return null;
            return (JsonUtility.FromJson<TaskLists>(r).items ?? new TaskListEntry[0])
                .Select(l => new ImportContainer { Id = l.id, Name = l.title }).ToList();
        }

        public async Task<List<ImportCandidate>> ListTasks(string listId)
        {
            var r = await Get($"{TasksApi}/lists/{Uri.EscapeDataString(listId)}/tasks?showCompleted=false&showHidden=false&maxResults=100");
            if (r == null) return null;
            return (JsonUtility.FromJson<GTaskList>(r).items ?? new GTask[0])
                .Where(t => !string.IsNullOrWhiteSpace(t.title))
                .Select(t => new ImportCandidate
                {
                    Source = TaskSource.GoogleTasks,
                    ExternalId = $"gtasks|{listId}|{t.id}",
                    Meta = MetaString.Build(("list", listId), ("task", t.id)),
                    Title = t.title,
                    Notes = t.notes ?? "",
                    Due = ApiDates.ParseDateOnly(t.due),
                    Done = t.status == "completed",
                }).ToList();
        }

        // ------------------------------------------------------------------ sync back

        public async Task<bool> Push(TaskItem task)
        {
            var meta = MetaString.Parse(task.externalMeta);
            var sb = new StringBuilder("{");
            sb.Append("\"").Append(task.source == TaskSource.GoogleCalendar ? "summary" : "title").Append("\":").Append(MiniJson.Quote(task.title));
            sb.Append(",\"").Append(task.source == TaskSource.GoogleCalendar ? "description" : "notes").Append("\":").Append(MiniJson.Quote(task.notes ?? ""));

            string url;
            if (task.source == TaskSource.GoogleCalendar)
            {
                url = $"{CalendarApi}/calendars/{Uri.EscapeDataString(meta["cal"])}/events/{Uri.EscapeDataString(meta["event"])}";
                if (task.DueDate.HasValue)
                {
                    var due = task.DueDate.Value;
                    bool allDay = meta.TryGetValue("allday", out var a) && a == "1";
                    if (allDay)
                    {
                        sb.Append(",\"start\":{\"date\":\"").Append(ApiDates.ToDateOnly(due)).Append("\"}");
                        sb.Append(",\"end\":{\"date\":\"").Append(ApiDates.ToDateOnly(due.AddDays(1))).Append("\"}");
                    }
                    else
                    {
                        int minutes = meta.TryGetValue("minutes", out var m) && int.TryParse(m, out int mm) && mm > 0 ? mm : 60;
                        sb.Append(",\"start\":{\"dateTime\":\"").Append(ApiDates.ToRfc3339(due)).Append("\"}");
                        sb.Append(",\"end\":{\"dateTime\":\"").Append(ApiDates.ToRfc3339(due.AddMinutes(minutes))).Append("\"}");
                    }
                }
            }
            else
            {
                url = $"{TasksApi}/lists/{Uri.EscapeDataString(meta["list"])}/tasks/{Uri.EscapeDataString(meta["task"])}";
                sb.Append(",\"due\":").Append(task.DueDate.HasValue ? "\"" + ApiDates.ToDateOnly(task.DueDate.Value) + "T00:00:00.000Z\"" : "null");
                sb.Append(",\"status\":\"").Append(task.done ? "completed" : "needsAction").Append("\"");
                if (!task.done) sb.Append(",\"completed\":null");
            }
            sb.Append("}");

            string token = await auth.GetAccessToken();
            if (token == null) return false;
            var r = await Http.Send("PATCH", url, token, sb.ToString());
            if (!r.Ok) Http.LogFailure($"Updating \"{task.title}\" in Google", r);
            return r.Ok;
        }

        public async Task<bool> Delete(TaskItem task)
        {
            var meta = MetaString.Parse(task.externalMeta);
            string url = task.source == TaskSource.GoogleCalendar
                ? $"{CalendarApi}/calendars/{Uri.EscapeDataString(meta["cal"])}/events/{Uri.EscapeDataString(meta["event"])}"
                : $"{TasksApi}/lists/{Uri.EscapeDataString(meta["list"])}/tasks/{Uri.EscapeDataString(meta["task"])}";
            string token = await auth.GetAccessToken();
            if (token == null) return false;
            var r = await Http.Send("DELETE", url, token);
            bool ok = r.Ok || r.Status == 404 || r.Status == 410; // already gone counts as done
            if (!ok) Http.LogFailure($"Deleting \"{task.title}\" from Google", r);
            return ok;
        }

        async Task<string> Get(string url)
        {
            string token = await auth.GetAccessToken();
            if (token == null) return null;
            var r = await Http.Send("GET", url, token);
            if (r.Ok) return r.Text;
            Http.LogFailure("Google request", r);
            return null;
        }
    }
}
