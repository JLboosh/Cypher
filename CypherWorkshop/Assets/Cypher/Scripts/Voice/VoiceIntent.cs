using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cypher
{
    /// <summary>
    /// What the user meant, as structured data. Produced by the AI (Gemini/Ollama, as JSON)
    /// or by the offline RuleBrain. Field names match the JSON the AI is asked to return.
    /// </summary>
    [Serializable]
    public class VoiceIntent
    {
        public const string AddTask = "add_task", EditTask = "edit_task", RemoveTask = "remove_task",
            MarkDone = "mark_done", ListTasks = "list_tasks", MostImportant = "most_important",
            SetTitle = "set_title", SetNotes = "set_notes", SetDue = "set_due", ClearDue = "clear_due",
            SetReminder = "set_reminder", ClearReminder = "clear_reminder", SetPriority = "set_priority",
            SetDifficulty = "set_difficulty", SetDone = "set_done", SetNotDone = "set_not_done",
            DeleteCurrent = "delete_current", Save = "save", Cancel = "cancel", Yes = "yes", No = "no",
            Arrange = "arrange", Snooze = "snooze", Import = "import",
            Unknown = "unknown";

        public string intent = Unknown;
        public string title = "";      // new task title / new name
        public string task = "";       // which existing task the user means
        public string due = "";        // "YYYY-MM-DDTHH:MM" local
        public string reminder = "";   // "YYYY-MM-DDTHH:MM" local
        public string priority = "";   // low | medium | high | auto
        public int difficulty;         // 1-5, 0 = not said
        public string notes = "";
        public string reply = "";      // optional short answer for chit-chat

        public static VoiceIntent Of(string intent) => new VoiceIntent { intent = intent };

        public static DateTime? ParseDate(string iso)
        {
            if (string.IsNullOrWhiteSpace(iso)) return null;
            string[] formats = { "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd" };
            return DateTime.TryParseExact(iso.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d)
                ? d
                : (DateTime?)null;
        }

        public static string FormatDate(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

        public static PriorityOverride? ParsePriority(string p) => (p ?? "").Trim().ToLowerInvariant() switch
        {
            "low" => PriorityOverride.Low,
            "medium" => PriorityOverride.Medium,
            "med" => PriorityOverride.Medium,
            "high" => PriorityOverride.High,
            "auto" => PriorityOverride.Auto,
            _ => (PriorityOverride?)null,
        };
    }

    /// <summary>Where the conversation is, so short answers ("Friday", "yes") make sense.</summary>
    public class BrainContext
    {
        public string State = "idle"; // idle | editing
        public string EditingTitle = "";
        public List<string> TaskTitles = new List<string>();
        public DateTime Now = DateTime.Now;
    }

    /// <summary>Finds "Hey Cypher" (and Whisper's misspellings of it) in a transcript.</summary>
    public static class WakeWord
    {
        static readonly string[] Greetings = { "hey", "hi", "hay", "hej", "a", "ok", "okay", "yo", "oh" };

        /// <summary>True if the wake word is near the start; command = everything after it.</summary>
        public static bool TryFind(string transcript, string[] wakeWords, out string command)
        {
            command = "";
            var words = Regex.Matches(transcript ?? "", @"[A-Za-z']+");
            for (int i = 0; i < words.Count && i < 4; i++)
            {
                string w = words[i].Value.ToLowerInvariant().Replace("'", "");
                bool isWake = wakeWords.Any(k => w == k || (w.Length >= 5 && TaskMatcher.Similarity(w, k) >= 0.75f));
                if (!isWake) continue;
                bool greeted = i > 0 && Greetings.Contains(words[i - 1].Value.ToLowerInvariant());
                if (!greeted && i != 0) continue; // "...my cipher homework" shouldn't wake Cypher
                command = transcript.Substring(words[i].Index + words[i].Length).Trim(' ', ',', '.', '!', '?', ':', ';', '-');
                return true;
            }
            return false;
        }
    }

    /// <summary>Fuzzy-matches what you said ("the lab report") to a task title.</summary>
    public static class TaskMatcher
    {
        static readonly HashSet<string> Stopwords = new HashSet<string>
            { "the", "a", "an", "my", "task", "to", "for", "of", "one", "that", "this", "item", "thing", "called", "named" };

        public struct Match
        {
            public TaskItem Best;
            public TaskItem RunnerUp;
            public float Score;
            public bool Ambiguous;
        }

        public static Match Find(string spoken, IEnumerable<TaskItem> tasks)
        {
            var scored = tasks.Select(t => (task: t, score: Score(spoken, t.title))).OrderByDescending(x => x.score).ToList();
            var result = new Match();
            if (scored.Count == 0) return result;
            result.Best = scored[0].task;
            result.Score = scored[0].score;
            if (scored.Count > 1)
            {
                result.RunnerUp = scored[1].task;
                result.Ambiguous = scored[1].score > 0.3f && scored[0].score - scored[1].score < 0.12f;
            }
            return result;
        }

        public static float Score(string spoken, string title)
        {
            var a = Tokens(spoken);
            var b = Tokens(title);
            if (a.Count == 0 || b.Count == 0) return 0f;

            // Token overlap with fuzzy token equality (handles plural/mishearing).
            float hits = 0f;
            foreach (var x in a)
                hits += b.Max(y => x == y ? 1f : Similarity(x, y) >= 0.8f ? 0.8f : 0f);
            // Mostly "how much of what you said is in the title" (people say a key word or two),
            // a little "how much of the title you said" to prefer closer matches.
            float overlap = 0.7f * hits / a.Count + 0.3f * Math.Min(1f, hits / b.Count);

            float whole = Similarity(string.Join(" ", a), string.Join(" ", b));
            return Math.Max(overlap, whole);
        }

        static List<string> Tokens(string s) =>
            NaturalDate.Normalize(s).Split(' ').Where(w => w.Length > 0 && !Stopwords.Contains(w)).ToList();

        /// <summary>1 - normalized Levenshtein distance.</summary>
        public static float Similarity(string a, string b)
        {
            if (a == b) return 1f;
            if (a.Length == 0 || b.Length == 0) return 0f;
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return 1f - d[a.Length, b.Length] / (float)Math.Max(a.Length, b.Length);
        }
    }

    /// <summary>
    /// Offline command understanding with plain pattern matching. Always available; used when
    /// no AI is configured or reachable, and as a backstop when the AI answers "unknown".
    /// </summary>
    public static class RuleBrain
    {
        public static VoiceIntent Interpret(string text, BrainContext ctx)
        {
            string raw = (text ?? "").Trim().TrimEnd('.', '!', '?', ' ');
            // Whisper often punctuates after the verb ("Add, call Sherlock." / "Delete, the report").
            raw = Regex.Replace(raw, @"^(\w+)[,.:;!]+\s*", "$1 ");
            string t = NaturalDate.Normalize(raw);
            Match m;

            if (ctx.State == "editing")
            {
                var edit = InterpretEdit(raw, t, ctx);
                if (edit.intent != VoiceIntent.Unknown) return edit;
            }

            if (Regex.IsMatch(t, @"^(whats|what is|what are|read|tell me|show me|list)\b.*\b(list|tasks|to dos?|todo|todos|agenda|have)\b")
                || Regex.IsMatch(t, @"^what do i (have|need to do)"))
                return Regex.IsMatch(t, @"\b(most important|first|top|priority|next)\b") ? VoiceIntent.Of(VoiceIntent.MostImportant) : VoiceIntent.Of(VoiceIntent.ListTasks);
            if (Regex.IsMatch(t, @"\b(most important|top priority|what should i (do|work on)|whats next|what is next)\b"))
                return VoiceIntent.Of(VoiceIntent.MostImportant);

            if ((m = Regex.Match(raw, @"^(?:please\s+)?(?:add|create|new task|make a task|put|remind me to|i need to|i have to)\s+(?:a\s+task\s+(?:to\s+)?|a\s+to-?do\s+(?:to\s+)?)?(.+)$", RegexOptions.IgnoreCase)).Success)
            {
                var intent = VoiceIntent.Of(VoiceIntent.AddTask);
                string title = Regex.Replace(m.Groups[1].Value, @"\s+(to|on|onto) (my |the )?(list|to-?do list|tasks)\b", "", RegexOptions.IgnoreCase).Trim();
                if (NaturalDate.TrySplitTrailingDate(title, ctx.Now, out string rest, out var due))
                {
                    title = rest;
                    intent.due = VoiceIntent.FormatDate(due.HasTime ? due.Value : due.Value.Date + new TimeSpan(23, 59, 0));
                }
                intent.title = Tidy(title);
                return intent;
            }

            if ((m = Regex.Match(t, @"^(?:please )?(?:edit|open|change|modify|update)\s+(.+)$")).Success)
                return new VoiceIntent { intent = VoiceIntent.EditTask, task = m.Groups[1].Value };

            if ((m = Regex.Match(t, @"^(?:please )?(?:remove|delete|trash|get rid of|throw away|throw out|scrap|bin)\s+(.+)$")).Success)
                return new VoiceIntent { intent = VoiceIntent.RemoveTask, task = m.Groups[1].Value };

            if ((m = Regex.Match(t, @"^(?:mark|set)\s+(.+?)\s+(?:as\s+)?(?:done|complete|completed|finished)$")).Success
                || (m = Regex.Match(t, @"^(?:i\s+)?(?:finished|completed|did|done with|complete)\s+(.+)$")).Success
                || (m = Regex.Match(t, @"^(.+?)\s+is\s+(?:done|finished|complete)$")).Success)
                return new VoiceIntent { intent = VoiceIntent.MarkDone, task = m.Groups[1].Value };

            if ((m = Regex.Match(t, @"\b(import|bring in|pull in|sync)\b.*\b(notion|google tasks|tasks|calendar|google)\b")).Success)
                return new VoiceIntent { intent = VoiceIntent.Import, task = m.Groups[2].Value };

            if (Regex.IsMatch(t, @"\b(arrange|rearrange|reorganize|organize|tidy up|sort)\b.*\b(holograms?|tasks?|list|everything)\b"))
                return VoiceIntent.Of(VoiceIntent.Arrange);

            if (Regex.IsMatch(t, @"^(yes|yeah|yep|yup|sure|correct|right|do it|confirm|affirmative|please do)\b")) return VoiceIntent.Of(VoiceIntent.Yes);
            if (Regex.IsMatch(t, @"^(no|nope|nah|dont|do not|negative)\b")) return VoiceIntent.Of(VoiceIntent.No);
            if (Regex.IsMatch(t, @"^(cancel|never mind|nevermind|stop|forget it)\b")) return VoiceIntent.Of(VoiceIntent.Cancel);

            return VoiceIntent.Of(VoiceIntent.Unknown);
        }

        static VoiceIntent InterpretEdit(string raw, string t, BrainContext ctx)
        {
            Match m;
            if ((m = Regex.Match(raw, @"^(?:rename|call)\s+(?:it|this|the task)?\s*(?:to|as)?\s+(.+)$", RegexOptions.IgnoreCase)).Success
                || (m = Regex.Match(raw, @"^(?:change|set|make)\s+the\s+(?:title|name)\s+(?:to\s+)?(.+)$", RegexOptions.IgnoreCase)).Success)
                return new VoiceIntent { intent = VoiceIntent.SetTitle, title = Tidy(m.Groups[1].Value) };

            if (Regex.IsMatch(t, @"\b(no|remove the|clear the|without a?) ?(deadline|due date)\b") || t == "undetermined")
                return VoiceIntent.Of(VoiceIntent.ClearDue);
            if (Regex.IsMatch(t, @"\b(no|remove the|clear the|without a?|dont) ?(reminder|remind me)\b"))
                return VoiceIntent.Of(VoiceIntent.ClearReminder);

            if (Regex.IsMatch(t, @"\b(remind|reminder)\b") && NaturalDate.TryParse(t, ctx.Now, out var rem))
                return new VoiceIntent { intent = VoiceIntent.SetReminder, reminder = VoiceIntent.FormatDate(rem.HasTime ? rem.Value : rem.Value.Date + new TimeSpan(9, 0, 0)) };
            if (Regex.IsMatch(t, @"\b(deadline|due|move it|push it|date)\b") && NaturalDate.TryParse(t, ctx.Now, out var due))
                return new VoiceIntent { intent = VoiceIntent.SetDue, due = VoiceIntent.FormatDate(due.HasTime ? due.Value : due.Value.Date + new TimeSpan(23, 59, 0)) };

            if ((m = Regex.Match(t, @"\b(low|medium|high)\b.*\bpriority\b|\bpriority\b.*\b(low|medium|high)\b")).Success)
                return new VoiceIntent { intent = VoiceIntent.SetPriority, priority = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value };
            if (Regex.IsMatch(t, @"\b(let cypher decide|you decide|automatic|auto priority|let you decide)\b"))
                return new VoiceIntent { intent = VoiceIntent.SetPriority, priority = "auto" };

            if ((m = Regex.Match(t, @"\bdifficulty\b.*\b(1|2|3|4|5|one|two|three|four|five)\b|\b(1|2|3|4|5|one|two|three|four|five)\b.*\bdifficulty\b")).Success)
            {
                string n = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                int level = Array.IndexOf(new[] { "one", "two", "three", "four", "five" }, n) + 1;
                return new VoiceIntent { intent = VoiceIntent.SetDifficulty, difficulty = level > 0 ? level : int.Parse(n) };
            }

            if ((m = Regex.Match(raw, @"^(?:add (?:a )?note|note|notes)\s*(?:that|saying|:)?\s+(.+)$", RegexOptions.IgnoreCase)).Success)
                return new VoiceIntent { intent = VoiceIntent.SetNotes, notes = m.Groups[1].Value.Trim() };

            if (Regex.IsMatch(t, @"\b(not done|undone|not finished|unmark)\b")) return VoiceIntent.Of(VoiceIntent.SetNotDone);
            if (Regex.IsMatch(t, @"\bmark (it |this )?(as )?(done|complete|finished)\b|^(its|it is) (done|finished)")) return VoiceIntent.Of(VoiceIntent.SetDone);
            if (Regex.IsMatch(t, @"^(delete|remove|trash) (it|this|this task|the task)$")) return VoiceIntent.Of(VoiceIntent.DeleteCurrent);
            if (Regex.IsMatch(t, @"^(save|save it|save changes|done|im done|done editing|finish|finished|thats all|thats it|close|looks good)\b")) return VoiceIntent.Of(VoiceIntent.Save);
            if (Regex.IsMatch(t, @"^(cancel|discard|never mind|nevermind|forget it|undo)\b")) return VoiceIntent.Of(VoiceIntent.Cancel);
            return VoiceIntent.Of(VoiceIntent.Unknown);
        }

        /// <summary>False for titles with no real words ("a", "it", "task", "something").</summary>
        public static bool IsMeaningfulTitle(string title)
        {
            var words = NaturalDate.Normalize(title).Split(' ');
            foreach (var w in words)
                if (w.Length > 1 && !Filler.Contains(w)) return true;
            return false;
        }

        static readonly HashSet<string> Filler = new HashSet<string>
            { "a", "an", "the", "it", "task", "new", "something", "thing", "todo", "to", "do", "item", "one", "this", "that", "please" };

        static string Tidy(string title)
        {
            title = Regex.Replace(title.Trim(), @"[\s,]*(please|thanks|thank you)$", "", RegexOptions.IgnoreCase).Trim(' ', '.', ',', '"');
            return title.Length > 0 ? char.ToUpperInvariant(title[0]) + title.Substring(1) : title;
        }
    }
}
