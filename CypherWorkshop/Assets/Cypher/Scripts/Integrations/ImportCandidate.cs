using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cypher
{
    /// <summary>One item offered for import (an event, a Google task, or a Notion page).</summary>
    public class ImportCandidate
    {
        public TaskSource Source;
        public string ExternalId;  // stable unique id, e.g. "gcal|primary|abc123" (used to avoid duplicates)
        public string Meta;        // "key=value;..." details needed to sync back (column names, all-day...)
        public string Title;
        public string Notes;
        public DateTime? Due;
        public bool Done;
        public TaskItem Existing;  // already imported -> importing again updates it instead of duplicating
    }

    /// <summary>A calendar, task list or Notion database you can import from.</summary>
    public struct ImportContainer
    {
        public string Id;
        public string Name;
        public string Meta;
    }

    /// <summary>"k=v;k=v" metadata helpers (values are URL-escaped).</summary>
    public static class MetaString
    {
        public static string Build(params (string key, string value)[] pairs) =>
            string.Join(";", pairs.Where(p => p.value != null).Select(p => p.key + "=" + Uri.EscapeDataString(p.value)));

        public static Dictionary<string, string> Parse(string meta)
        {
            var d = new Dictionary<string, string>();
            foreach (var part in (meta ?? "").Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0) d[part.Substring(0, eq)] = Uri.UnescapeDataString(part.Substring(eq + 1));
            }
            return d;
        }

        public static string Get(string meta, string key) => Parse(meta).TryGetValue(key, out var v) ? v : null;
    }

    static class ApiDates
    {
        /// <summary>RFC 3339 date-time ("2026-10-09T17:00:00-04:00") -> local time.</summary>
        public static DateTime? ParseDateTime(string s) =>
            DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.LocalDateTime : (DateTime?)null;

        /// <summary>Date only ("2026-10-09") -> that day at 23:59 local ("due by end of day").</summary>
        public static DateTime? ParseDateOnly(string s) =>
            DateTime.TryParseExact((s ?? "").Length >= 10 ? s.Substring(0, 10) : s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? DateTime.SpecifyKind(d.Date + new TimeSpan(23, 59, 0), DateTimeKind.Local)
                : (DateTime?)null;

        public static string ToRfc3339(DateTime local) =>
            DateTime.SpecifyKind(local, DateTimeKind.Local).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

        public static string ToDateOnly(DateTime local) => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
