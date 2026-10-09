using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Cypher
{
    /// <summary>
    /// Connects tasks to Google Calendar, Google Tasks and Notion.
    ///   Import: candidates you select become tasks. Each remembers its source id (externalId),
    ///           so importing the same item again updates that task instead of duplicating it.
    ///   Edits:  when you choose "Update original" after editing an imported task, the task is
    ///           marked pendingSync and pushed here (retried every minute if offline; survives restarts).
    ///   Deletes: removing an imported task asks once (or follows deleteSyncMode in the config)
    ///           whether to delete it at the source too.
    /// </summary>
    public class IntegrationHub : MonoBehaviour
    {
        public static IntegrationHub Instance { get; private set; }

        public enum Kind { GoogleCalendar, GoogleTasks, Notion }

        public CypherConfig Config { get; private set; }
        public GoogleAuth Google { get; private set; }
        GoogleClient google;
        NotionClient notion;

        bool pushing;
        float nextPushTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake()
        {
            Instance = this;
            Config = CypherConfig.Load();
            Google = new GoogleAuth(Config);
            google = new GoogleClient(Google, Config);
            notion = new NotionClient(Config);
            nextPushTime = Time.unscaledTime + 5f;
        }

        public static string SourceName(TaskSource source) => source switch
        {
            TaskSource.GoogleCalendar => "Google Calendar",
            TaskSource.GoogleTasks => "Google Tasks",
            TaskSource.Notion => "Notion",
            _ => "this Mac",
        };

        // ------------------------------------------------------------------ browsing

        /// <summary>Why a source can't be used yet (null = ready).</summary>
        public string Problem(Kind kind)
        {
            if (kind == Kind.Notion)
                return notion.IsConfigured ? null : "Add your Notion integration token to the config file (notionToken), then reopen this panel.";
            if (!Google.IsConfigured) return "Add googleClientId and googleClientSecret to the config file (see docs/PHASE-7.md).";
            if (!Google.IsConnected) return "Not connected. Click 'Connect Google' and approve in your browser.";
            return null;
        }

        public Task<List<ImportContainer>> ListContainers(Kind kind) => kind switch
        {
            Kind.GoogleCalendar => google.ListCalendars(),
            Kind.GoogleTasks => google.ListTaskLists(),
            _ => notion.ListDatabases(),
        };

        public async Task<List<ImportCandidate>> ListItems(Kind kind, string containerId)
        {
            var items = kind switch
            {
                Kind.GoogleCalendar => await google.ListEvents(containerId),
                Kind.GoogleTasks => await google.ListTasks(containerId),
                _ => await notion.ListPages(containerId),
            };
            if (items == null) return null;
            var byExternalId = TaskManager.Instance.Repository.Tasks
                .Where(t => !string.IsNullOrEmpty(t.externalId))
                .GroupBy(t => t.externalId).ToDictionary(g => g.Key, g => g.First());
            foreach (var c in items) c.Existing = byExternalId.TryGetValue(c.ExternalId, out var t) ? t : null;
            return items;
        }

        // ------------------------------------------------------------------ import

        /// <summary>Creates new tasks or refreshes already-imported ones. Returns (added, updated).</summary>
        public (int added, int updated) Import(IEnumerable<ImportCandidate> selected)
        {
            var tm = TaskManager.Instance;
            int added = 0, updated = 0;
            foreach (var c in selected)
            {
                var existing = c.Existing ?? tm.Repository.Tasks.FirstOrDefault(t => t.externalId == c.ExternalId);
                var task = existing ?? tm.AddTask(c.Title);
                task.title = c.Title;
                task.notes = c.Notes ?? "";
                if (task.DueDate != c.Due)
                {
                    task.DueDate = c.Due;
                    task.reminderFired = false;
                }
                if (!task.DueDate.HasValue) task.Reminder = null;
                task.done = c.Done;
                task.source = c.Source;
                task.externalId = c.ExternalId;
                task.externalMeta = c.Meta;
                task.pendingSync = false;
                tm.NotifyTaskChanged(task);
                c.Existing = task;
                if (existing == null) added++; else updated++;
            }
            return (added, updated);
        }

        // ------------------------------------------------------------------ edits -> source

        void Update()
        {
            if (pushing || Time.unscaledTime < nextPushTime || TaskManager.Instance == null) return;
            nextPushTime = Time.unscaledTime + 10f;
            // Only real imports carry sync details (tasks faked with the Shift+I debug key don't).
            var next = TaskManager.Instance.Repository.Tasks.FirstOrDefault(t =>
                t.pendingSync && t.source != TaskSource.Local && !string.IsNullOrEmpty(t.externalMeta));
            if (next != null) PushPending(next);
        }

        /// <summary>Push soon (called right after you save an edit with "Update original").</summary>
        public void Kick() => nextPushTime = 0f;

        async void PushPending(TaskItem task)
        {
            pushing = true;
            try
            {
                bool ok = task.source == TaskSource.Notion ? await notion.Push(task) : await google.Push(task);
                if (ok)
                {
                    task.pendingSync = false;
                    TaskManager.Instance.Save();
                    Debug.Log($"[Cypher] Synced \"{task.title}\" to {SourceName(task.source)}.");
                }
                else
                {
                    nextPushTime = Time.unscaledTime + 60f; // offline or rejected: retry in a minute
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                nextPushTime = Time.unscaledTime + 60f;
            }
            finally
            {
                pushing = false;
            }
        }

        // ------------------------------------------------------------------ deletes -> source

        /// <summary>Called by TaskManager after an imported task was removed locally.</summary>
        public void OnTaskRemoved(TaskItem removed)
        {
            if (removed.source == TaskSource.Local || string.IsNullOrEmpty(removed.externalMeta)) return;
            string where = SourceName(removed.source);
            switch (Config.deleteSyncMode)
            {
                case "always":
                    DeleteRemote(removed);
                    break;
                case "never":
                    break;
                default:
                    HoloPrompt.Show(
                        $"\"{removed.title}\" came from {where}.\nDelete it there too?",
                        $"Delete in {(removed.source == TaskSource.Notion ? "Notion" : "Google")}", "Only here",
                        yes => { if (yes) DeleteRemote(removed); },
                        voiceA: @"\b(yes|yeah|delete|both|there too|everywhere|remove it)\b",
                        voiceB: @"\b(no|only here|just here|keep|local|dont)\b",
                        spoken: $"That task came from {where}. Should I delete it there too?",
                        replyA: $"Deleted from {where} too.",
                        replyB: $"Okay, it stays in {where}.");
                    break;
            }
        }

        async void DeleteRemote(TaskItem task)
        {
            try
            {
                bool ok = task.source == TaskSource.Notion ? await notion.Trash(task) : await google.Delete(task);
                if (ok) Debug.Log($"[Cypher] Deleted \"{task.title}\" from {SourceName(task.source)}.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
