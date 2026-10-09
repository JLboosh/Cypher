using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cypher
{
    /// <summary>
    /// Notion (API version 2026-03-11): finds databases ("data sources") shared with your
    /// integration, works out which columns hold the title, date, done flag and notes, imports
    /// pages, pushes edits back, and moves pages to the trash.
    /// </summary>
    public class NotionClient
    {
        const string Api = "https://api.notion.com/v1";
        const string Version = "2026-03-11";

        readonly CypherConfig config;

        public NotionClient(CypherConfig config) => this.config = config;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(config.notionToken);

        /// <summary>Databases shared with the integration. Meta holds the detected column names.</summary>
        public async Task<List<ImportContainer>> ListDatabases()
        {
            var r = await Send("POST", "/search", "{\"filter\":{\"property\":\"object\",\"value\":\"data_source\"},\"page_size\":50}");
            if (r == null) return null;
            var result = new List<ImportContainer>();
            foreach (var ds in MiniJson.Arr(r, "results"))
            {
                string id = MiniJson.Str(ds, "id");
                string name = PlainText(MiniJson.Get(ds, "title")) ?? MiniJson.Str(ds, "name") ?? "Untitled";
                result.Add(new ImportContainer { Id = id, Name = string.IsNullOrWhiteSpace(name) ? "Untitled" : name });
            }
            return result;
        }

        public async Task<List<ImportCandidate>> ListPages(string dataSourceId)
        {
            var schema = await Send("GET", $"/data_sources/{dataSourceId}", null);
            if (schema == null) return null;
            var columns = DetectColumns(MiniJson.Obj(schema, "properties"));
            if (columns.title == null) return new List<ImportCandidate>();

            var result = new List<ImportCandidate>();
            string cursor = null;
            for (int page = 0; page < 5; page++) // up to 500 rows
            {
                string body = "{\"page_size\":100" + (cursor != null ? ",\"start_cursor\":" + MiniJson.Quote(cursor) : "") + "}";
                var r = await Send("POST", $"/data_sources/{dataSourceId}/query", body);
                if (r == null) return result.Count > 0 ? result : null;

                foreach (var p in MiniJson.Arr(r, "results"))
                {
                    if (MiniJson.Str(p, "object") != "page" || MiniJson.Bool(p, "in_trash")) continue;
                    var props = MiniJson.Obj(p, "properties");
                    string title = PlainText(MiniJson.Get(props, columns.title, "title"));
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    string id = MiniJson.Str(p, "id");
                    result.Add(new ImportCandidate
                    {
                        Source = TaskSource.Notion,
                        ExternalId = "notion|" + id,
                        Meta = MetaString.Build(("page", id), ("title", columns.title), ("date", columns.date),
                                                ("done", columns.done), ("doneType", columns.doneType), ("notes", columns.notes)),
                        Title = title,
                        Notes = columns.notes != null ? PlainText(MiniJson.Get(props, columns.notes, "rich_text")) ?? "" : "",
                        Due = columns.date != null ? ParseNotionDate(MiniJson.Str(props, columns.date, "date", "start")) : null,
                        Done = columns.done != null && IsDone(MiniJson.Obj(props, columns.done), columns.doneType),
                    });
                }

                if (!MiniJson.Bool(r, "has_more")) break;
                cursor = MiniJson.Str(r, "next_cursor");
            }
            return result;
        }

        public async Task<bool> Push(TaskItem task)
        {
            var meta = MetaString.Parse(task.externalMeta);
            var props = new List<string>
            {
                MiniJson.Quote(meta["title"]) + ":{\"title\":[{\"text\":{\"content\":" + MiniJson.Quote(task.title) + "}}]}",
            };
            if (meta.TryGetValue("date", out var dateCol))
            {
                string date = task.DueDate.HasValue ? DateValue(task.DueDate.Value) : null;
                props.Add(MiniJson.Quote(dateCol) + ":{\"date\":" + (date == null ? "null" : "{\"start\":" + MiniJson.Quote(date) + "}") + "}");
            }
            if (meta.TryGetValue("notes", out var notesCol))
                props.Add(MiniJson.Quote(notesCol) + ":{\"rich_text\":[{\"text\":{\"content\":" + MiniJson.Quote(Clip(task.notes ?? "", 1900)) + "}}]}");
            if (meta.TryGetValue("done", out var doneCol) && meta.TryGetValue("doneType", out var doneType) && doneType == "checkbox")
                props.Add(MiniJson.Quote(doneCol) + ":{\"checkbox\":" + (task.done ? "true" : "false") + "}");

            var r = await Send("PATCH", "/pages/" + meta["page"], "{\"properties\":{" + string.Join(",", props) + "}}");
            return r != null;
        }

        /// <summary>Moves the page to Notion's trash (recoverable there for 30 days).</summary>
        public async Task<bool> Trash(TaskItem task)
        {
            var r = await Send("PATCH", "/pages/" + MetaString.Get(task.externalMeta, "page"), "{\"in_trash\":true}");
            return r != null;
        }

        // ------------------------------------------------------------------ helpers

        struct Columns
        {
            public string title, date, done, doneType, notes;
        }

        /// <summary>Picks the title column, a date column (prefers names like due/deadline), a done column, a notes column.</summary>
        static Columns DetectColumns(Dictionary<string, object> properties)
        {
            var c = new Columns();
            if (properties == null) return c;
            string Type(string name) => MiniJson.Str(properties[name], "type");
            var names = properties.Keys.ToList();

            c.title = names.FirstOrDefault(n => Type(n) == "title");
            var dates = names.Where(n => Type(n) == "date").ToList();
            c.date = dates.FirstOrDefault(n => Has(n, "due", "deadline", "date", "when")) ?? dates.FirstOrDefault();
            var checks = names.Where(n => Type(n) == "checkbox").ToList();
            c.done = checks.FirstOrDefault(n => Has(n, "done", "complete", "finished")) ?? checks.FirstOrDefault();
            c.doneType = c.done != null ? "checkbox" : null;
            if (c.done == null)
            {
                c.done = names.FirstOrDefault(n => Type(n) == "status");
                c.doneType = c.done != null ? "status" : null; // read-only: status option names vary per database
            }
            c.notes = names.FirstOrDefault(n => Type(n) == "rich_text" && Has(n, "note", "description", "details", "summary"));
            return c;
        }

        static bool Has(string name, params string[] words) =>
            words.Any(w => name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);

        static bool IsDone(Dictionary<string, object> prop, string type)
        {
            if (prop == null) return false;
            if (type == "checkbox") return MiniJson.Bool(prop, "checkbox");
            string status = MiniJson.Str(prop, "status", "name") ?? "";
            return Has(status, "done", "complete", "finished");
        }

        static string PlainText(object richTextArray)
        {
            if (!(richTextArray is List<object> parts)) return null;
            var sb = new StringBuilder();
            foreach (var part in parts) sb.Append(MiniJson.Str(part, "plain_text"));
            return sb.ToString();
        }

        static DateTime? ParseNotionDate(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            return s.Length <= 10 ? ApiDates.ParseDateOnly(s) : ApiDates.ParseDateTime(s);
        }

        /// <summary>End-of-day deadlines go back as plain dates; specific times as date-times.</summary>
        static string DateValue(DateTime due) =>
            due.Hour == 23 && due.Minute == 59 ? ApiDates.ToDateOnly(due) : ApiDates.ToRfc3339(due);

        static string Clip(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

        async Task<object> Send(string method, string path, string body)
        {
            var r = await Http.Send(method, Api + path, config.notionToken.Trim(), body,
                new Dictionary<string, string> { ["Notion-Version"] = Version });
            if (r.Ok) return MiniJson.Parse(r.Text);
            Http.LogFailure("Notion request", r);
            return null;
        }
    }
}
