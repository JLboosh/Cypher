using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Cypher
{
    /// <summary>
    /// Understands everyday date/time phrases without any AI:
    ///   today, tonight, tomorrow (morning), the day after tomorrow, friday, this/next friday,
    ///   in 3 days / in two weeks / in an hour, next week, next month, this weekend,
    ///   end of the week/month, october 12, 12th of october, the 15th, 10/12,
    ///   ...plus times: at 5, at 5pm, 5:30 pm, 17:00, noon, midnight, morning/afternoon/evening.
    /// </summary>
    public static class NaturalDate
    {
        public struct Result
        {
            public DateTime Value;
            public bool HasDate;
            public bool HasTime;
        }

        static readonly Dictionary<string, int> Numbers = new Dictionary<string, int>
        {
            ["a"] = 1, ["an"] = 1, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5,
            ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11,
            ["twelve"] = 12, ["couple"] = 2, ["a couple"] = 2, ["a couple of"] = 2, ["few"] = 3, ["a few"] = 3,
        };

        static readonly string[] Months =
            { "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december" };

        const string MonthPattern = @"(jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|june?|july?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)";
        const string WeekdayPattern = @"(monday|tuesday|wednesday|thursday|friday|saturday|sunday)";
        const string NumberPattern = @"(\d+|a couple of|a couple|couple|a few|few|an|a|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)";

        static readonly string[] NoDatePhrases =
        {
            "undetermined", "no deadline", "no due date", "not sure", "dont know", "do not know", "no idea",
            "whenever", "no rush", "none", "never", "skip", "no reminder", "dont remind", "do not remind",
            "nothing", "no thanks", "no thank you", "not needed", "no need", "nope", "no",
        };

        /// <summary>"undetermined", "no deadline", "don't remind me", "no"...</summary>
        public static bool MeansNoDate(string text)
        {
            string t = Normalize(text);
            foreach (var p in NoDatePhrases)
                if (t == p || t.StartsWith(p + " ") || (p.Length > 4 && t.Contains(p))) return true;
            return false;
        }

        public static bool TryParse(string text, DateTime now, out Result result)
        {
            result = default;
            string t = Normalize(text);
            if (t.Length == 0) return false;

            DateTime date = now.Date;
            bool hasDate = false, hasTime = false;
            TimeSpan time = default;
            Match m;

            // Relative offsets: "in 3 days", "in two weeks", "in an hour", "in 20 minutes".
            if ((m = Regex.Match(t, $@"\bin {NumberPattern} (minute|hour|day|week|month)s?\b")).Success)
            {
                int n = ToNumber(m.Groups[1].Value);
                switch (m.Groups[2].Value)
                {
                    case "minute": result = Exact(now.AddMinutes(n)); return true;
                    case "hour": result = Exact(now.AddHours(n)); return true;
                    case "day": date = now.Date.AddDays(n); break;
                    case "week": date = now.Date.AddDays(7 * n); break;
                    case "month": date = now.Date.AddMonths(n); break;
                }
                hasDate = true;
            }
            else if (Regex.IsMatch(t, @"\bday after tomorrow\b")) { date = now.Date.AddDays(2); hasDate = true; }
            else if (Regex.IsMatch(t, @"\b(tomorrow|tmrw|tmr)\b")) { date = now.Date.AddDays(1); hasDate = true; }
            else if (Regex.IsMatch(t, @"\b(today|tonight|this (morning|afternoon|evening))\b")) { hasDate = true; }
            else if (Regex.IsMatch(t, @"\bnext week\b")) { date = NextWeekday(now.Date, DayOfWeek.Monday, true); hasDate = true; }
            else if (Regex.IsMatch(t, @"\bnext month\b")) { date = new DateTime(now.Year, now.Month, 1).AddMonths(1); hasDate = true; }
            else if (Regex.IsMatch(t, @"\b(this )?weekend\b")) { date = NextWeekday(now.Date, DayOfWeek.Saturday, false); hasDate = true; }
            else if (Regex.IsMatch(t, @"\bend of (the |this )?week\b")) { date = NextWeekday(now.Date, DayOfWeek.Friday, false); hasDate = true; }
            else if (Regex.IsMatch(t, @"\bend of (the |this )?month\b"))
            {
                date = new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month));
                hasDate = true;
            }
            else if ((m = Regex.Match(t, $@"\b(?:(this|next|coming) )?{WeekdayPattern}\b")).Success)
            {
                var day = (DayOfWeek)Enum.Parse(typeof(DayOfWeek), m.Groups[2].Value, true);
                date = NextWeekday(now.Date, day, includeToday: m.Groups[1].Value == "this");
                hasDate = true;
            }
            else if ((m = Regex.Match(t, $@"\b{MonthPattern} (\d{{1,2}})(st|nd|rd|th)?\b")).Success
                     || (m = Regex.Match(t, $@"\b(\d{{1,2}})(st|nd|rd|th)? (?:of )?{MonthPattern}\b")).Success)
            {
                bool monthFirst = !char.IsDigit(m.Groups[1].Value[0]);
                string monthText = monthFirst ? m.Groups[1].Value : m.Groups[3].Value;
                int day = int.Parse(monthFirst ? m.Groups[2].Value : m.Groups[1].Value, CultureInfo.InvariantCulture);
                if (TryMakeDate(now, MonthIndex(monthText), day, out date)) hasDate = true;
            }
            else if ((m = Regex.Match(t, @"\b(\d{1,2})/(\d{1,2})\b")).Success)
            {
                if (TryMakeDate(now, int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), out date)) hasDate = true;
            }
            else if ((m = Regex.Match(t, @"\bthe (\d{1,2})(st|nd|rd|th)\b")).Success)
            {
                int day = int.Parse(m.Groups[1].Value);
                if (TryMakeDate(now, now.Month, day, out date)) hasDate = true;
            }

            // Times.
            if ((m = Regex.Match(t, @"\b(\d{1,2})(?::(\d{2}))? ?(am|pm|a m|p m)\b")).Success)
            {
                int h = int.Parse(m.Groups[1].Value) % 12;
                int min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
                if (m.Groups[3].Value.StartsWith("p")) h += 12;
                if (min < 60) { time = new TimeSpan(h, min, 0); hasTime = true; }
            }
            else if ((m = Regex.Match(t, @"\b(?:at |by )?(\d{1,2}):(\d{2})\b")).Success)
            {
                int h = int.Parse(m.Groups[1].Value), min = int.Parse(m.Groups[2].Value);
                if (h < 8 && !t.Contains("morning")) h += 12; // "at 5:30" usually means pm
                if (h < 24 && min < 60) { time = new TimeSpan(h, min, 0); hasTime = true; }
            }
            else if ((m = Regex.Match(t, $@"\bat {NumberPattern}(?: o ?clock)?\b")).Success)
            {
                int h = ToNumber(m.Groups[1].Value);
                if (h >= 1 && h <= 12)
                {
                    if (h < 8 && !t.Contains("morning")) h += 12;
                    time = new TimeSpan(h % 24, 0, 0);
                    hasTime = true;
                }
            }
            else if (Regex.IsMatch(t, @"\bnoon\b")) { time = new TimeSpan(12, 0, 0); hasTime = true; }
            else if (Regex.IsMatch(t, @"\bmidnight\b")) { time = new TimeSpan(23, 59, 0); hasTime = true; }
            else if (Regex.IsMatch(t, @"\bmorning\b")) { time = new TimeSpan(9, 0, 0); hasTime = true; }
            else if (Regex.IsMatch(t, @"\bafternoon\b")) { time = new TimeSpan(15, 0, 0); hasTime = true; }
            else if (Regex.IsMatch(t, @"\b(evening|tonight)\b")) { time = new TimeSpan(19, 0, 0); hasTime = true; }

            if (!hasDate && !hasTime) return false;
            if (!hasDate)
            {
                // Only a time: today if still ahead, otherwise tomorrow.
                hasDate = true;
                if (now.Date + time <= now) date = now.Date.AddDays(1);
            }

            result = new Result { Value = date + (hasTime ? time : TimeSpan.Zero), HasDate = true, HasTime = hasTime };
            return true;
        }

        /// <summary>
        /// Splits "finish report by friday" into ("finish report", friday).
        /// Looks for a date phrase introduced by by/due/on/for/before/until/in/at/next/this/tomorrow...
        /// </summary>
        public static bool TrySplitTrailingDate(string text, DateTime now, out string rest, out Result date)
        {
            rest = text;
            date = default;
            // Try the earliest marker first so "by next friday at 5" is taken whole.
            foreach (Match marker in Regex.Matches(text,
                         @"\s*,?\s*\b(by|due|on|for|before|until|in|at|next|this|tomorrow|today|tonight|the day after)\b",
                         RegexOptions.IgnoreCase))
            {
                if (marker.Index == 0 || !TryParse(text.Substring(marker.Index), now, out date)) continue;
                rest = text.Substring(0, marker.Index).Trim(' ', ',', '.');
                return rest.Length > 0;
            }
            return false;
        }

        /// <summary>
        /// Makes a reminder time usable: a time that has already passed TODAY moves to the next
        /// full hour (e.g. "today" at 2:10 PM -> 3 PM instead of an already-past 9 AM).
        /// Returns null if it's on a day that has already gone by.
        /// </summary>
        public static DateTime? FutureReminder(DateTime reminder, DateTime now)
        {
            if (reminder > now) return reminder;
            if (reminder.Date < now.Date) return null;
            var nextHour = now.Date.AddHours(now.Hour + 1);
            return nextHour - now < TimeSpan.FromMinutes(15) ? nextHour.AddHours(1) : nextHour;
        }

        /// <summary>"today", "tomorrow at 5 PM", "Friday", "Monday, October 12".</summary>
        public static string Describe(DateTime value, bool includeTime, DateTime now)
        {
            var culture = CultureInfo.CurrentCulture;
            int days = (value.Date - now.Date).Days;
            string day = days == 0 ? "today"
                : days == 1 ? "tomorrow"
                : days > 1 && days < 7 ? value.ToString("dddd", culture)
                : value.ToString("dddd, MMMM d", culture);
            bool showTime = includeTime && !(value.Hour == 23 && value.Minute == 59);
            return showTime ? $"{day} at {value.ToString("h:mm tt", culture).Replace(":00", "")}" : day;
        }

        static Result Exact(DateTime value) => new Result { Value = value, HasDate = true, HasTime = true };

        static DateTime NextWeekday(DateTime from, DayOfWeek day, bool includeToday)
        {
            int delta = ((int)day - (int)from.DayOfWeek + 7) % 7;
            if (delta == 0 && !includeToday) delta = 7;
            return from.AddDays(delta);
        }

        static bool TryMakeDate(DateTime now, int month, int day, out DateTime date)
        {
            date = default;
            if (month < 1 || month > 12 || day < 1) return false;
            int year = now.Year;
            if (day > DateTime.DaysInMonth(year, month)) return false;
            date = new DateTime(year, month, day);
            if (date < now.Date) // already passed this year -> next occurrence
            {
                if (month == now.Month) date = date.AddMonths(1);
                else date = date.AddYears(1);
            }
            return true;
        }

        static int MonthIndex(string text)
        {
            for (int i = 0; i < Months.Length; i++)
                if (Months[i].StartsWith(text.Substring(0, 3))) return i + 1;
            return 0;
        }

        static int ToNumber(string s) =>
            int.TryParse(s, out int n) ? n : Numbers.TryGetValue(s, out n) ? n : 1;

        public static string Normalize(string text)
        {
            if (text == null) return "";
            string t = text.ToLowerInvariant().Replace("'", "").Replace("’", "");
            t = Regex.Replace(t, @"[^a-z0-9:/ ]", " ");
            return Regex.Replace(t, @"\s+", " ").Trim();
        }
    }
}
