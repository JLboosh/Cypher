using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Cypher
{
    /// <summary>
    /// The hub of the app: owns the task list, spawns one hologram per task,
    /// lays them out, and saves whenever something changes.
    /// Other systems (edit panel, voice, reminders) talk to tasks through this class.
    /// </summary>
    public class TaskManager : MonoBehaviour
    {
        public static TaskManager Instance { get; private set; }

        [SerializeField] HologramPanel hologramPrefab;
        [SerializeField] Transform hologramRoot;
        [Tooltip("Create a few example tasks on first launch so there is something to play with.")]
        [SerializeField] bool seedSampleTasksIfEmpty = true;

        [Header("Startup")]
        [Tooltip("Pause before the holograms start building in.")]
        [SerializeField] float bootDelay = 0.5f;
        [Tooltip("Seconds between each hologram materializing on startup.")]
        [SerializeField] float bootStagger = 0.14f;

        [Header("Ranking")]
        [Tooltip("How often to re-check ranking and due-date labels as time passes (seconds).")]
        [SerializeField] float rerankIntervalSeconds = 30f;

        public TaskRepository Repository { get; } = new TaskRepository();

        /// <summary>Raised on double-click or pencil click. The edit panel (Phase 3) listens to this.</summary>
        public event Action<HologramPanel> EditRequested;

        readonly Dictionary<string, HologramPanel> panels = new Dictionary<string, HologramPanel>();
        string lastOrder;
        float nextRerankTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Application.runInBackground = true; // keep running (reminders, voice) while minimized

            // Sound should work even in a scene built before CypherAudio existed.
            if (FindFirstObjectByType<CypherAudio>() == null) gameObject.AddComponent<CypherAudio>();
            // Same for the edit panel: it builds its own UI, so it can be added at runtime.
            if (FindFirstObjectByType<EditPanelController>() == null) new GameObject("Edit Panel").AddComponent<EditPanelController>();
            if (FindFirstObjectByType<VoiceAssistant>() == null) new GameObject("Cypher Voice").AddComponent<VoiceAssistant>();
            if (FindFirstObjectByType<CrumpleThrowDirector>() == null) new GameObject("Cypher Removal").AddComponent<CrumpleThrowDirector>();
            if (FindFirstObjectByType<ReminderService>() == null) gameObject.AddComponent<ReminderService>();
            if (FindFirstObjectByType<IntegrationHub>() == null) gameObject.AddComponent<IntegrationHub>();
            if (FindFirstObjectByType<ImportPanel>() == null) new GameObject("Import Panel").AddComponent<ImportPanel>();

            Repository.Load();
            if (seedSampleTasksIfEmpty && Repository.Tasks.Count == 0)
            {
                SeedSampleTasks();
                Repository.Save();
            }
        }

        void Start()
        {
            foreach (var task in Repository.Tasks) Spawn(task);
            RefreshLayout(instant: true);
            StartCoroutine(BootSequence());
        }

        /// <summary>Holograms build in one after another, most important first.</summary>
        IEnumerator BootSequence()
        {
            yield return new WaitForSeconds(bootDelay);

            var order = new List<TaskItem>(OrderedTasks()); // snapshot: the list may change mid-boot
            for (int i = 0; i < order.Count; i++)
            {
                if (!panels.TryGetValue(order[i].id, out var panel) || panel == null) continue;
                // One full materialize sound, then soft blips, so six holograms don't make a wall of noise.
                panel.Materialize(withSound: i == 0);
                if (i > 0) CypherAudio.Play(Sfx.Hover, panel.transform.position, 0.6f);
                yield return new WaitForSeconds(bootStagger);
            }
        }

        /// <summary>Deadlines approach while the app runs: re-rank and refresh "due today" labels.</summary>
        void Update()
        {
            if (Time.unscaledTime < nextRerankTime) return;
            nextRerankTime = Time.unscaledTime + rerankIntervalSeconds;
            foreach (var panel in panels.Values) panel.Refresh();
            RefreshLayout(instant: false);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) Save();
        }

        void OnApplicationQuit() => Save();

        public void Save() => Repository.Save();

        public IEnumerable<HologramPanel> Panels => panels.Values;

        public HologramPanel GetPanel(string taskId) =>
            panels.TryGetValue(taskId, out var panel) ? panel : null;

        public TaskItem AddTask(string title)
        {
            var task = TaskItem.Create(title);
            Repository.Add(task);
            Save();
            var panel = Spawn(task);
            var slot = HologramLayout.GetSlot(AutoPlacedRankOf(task));
            panel.MoveTo(slot.position, slot.scale, instant: true);
            panel.Materialize();
            RefreshLayout(instant: false);
            return task;
        }

        public void RemoveTask(string taskId)
        {
            // The data goes immediately (so quitting mid-animation can't resurrect it);
            // the other holograms close the gap once the ball is thrown.
            var removed = Repository.Get(taskId);
            Repository.Remove(taskId);
            Save();
            if (removed != null && IntegrationHub.Instance != null) IntegrationHub.Instance.OnTaskRemoved(removed);

            if (!panels.TryGetValue(taskId, out var panel))
            {
                RefreshLayout(instant: false);
                return;
            }
            panels.Remove(taskId);

            var director = CrumpleThrowDirector.Instance;
            if (director != null && director.CanPlay && panel != null)
            {
                director.Play(panel, () => RefreshLayout(instant: false));
            }
            else
            {
                panel.Dematerialize(destroyWhenDone: true);
                RefreshLayout(instant: false);
            }
        }

        /// <summary>
        /// Call after changing fields on a TaskItem so the hologram and save file update.
        /// relayout = the change can affect ranking (deadline, priority, difficulty, done).
        /// </summary>
        public void NotifyTaskChanged(TaskItem task, bool relayout = true)
        {
            task.Touch();
            GetPanel(task.id)?.Refresh();
            Repository.NotifyChanged();
            Save();
            if (relayout) RefreshLayout(instant: false);
        }

        public void OnHologramDragEnded(HologramPanel panel)
        {
            panel.Task.userPlaced = true;
            panel.Task.position = panel.transform.position;
            Save();
        }

        public void RequestEdit(HologramPanel panel)
        {
            panel.Flash();
            if (EditRequested != null)
                EditRequested.Invoke(panel);
            else
                Debug.Log($"[Cypher] Edit requested for \"{panel.Task.title}\" (the edit panel arrives in Phase 3).");
        }

        /// <summary>Forget all manual drags and let the layout place every hologram again.</summary>
        public void ResetManualPlacement()
        {
            foreach (var task in Repository.Tasks) task.userPlaced = false;
            Save();
            RefreshLayout(instant: false);
        }

        /// <summary>
        /// Ranks the tasks and moves every hologram to its slot: the top 5 in front of the desk,
        /// the rest further back. Holograms you dragged stay put, unless you chose to let Cypher
        /// arrange them (asked once, the first time a re-rank happens while one is hand-placed).
        /// </summary>
        public void RefreshLayout(bool instant)
        {
            var order = RankedTasks();
            string signature = string.Join(",", order.Select(t => t.id));
            bool reranked = lastOrder != null && signature != lastOrder;
            lastOrder = signature;

            if (reranked && order.Any(t => t.userPlaced))
            {
                string mode = Repository.Data.placementMode;
                if (mode == "auto")
                {
                    foreach (var t in order) t.userPlaced = false;
                    Save();
                }
                else if (mode == "ask")
                {
                    AskAboutPlacement();
                }
            }

            int rank = 0;
            foreach (var task in order)
            {
                if (!panels.TryGetValue(task.id, out var panel)) continue;
                if (task.userPlaced)
                {
                    panel.MoveTo(task.position, 1f, instant);
                    continue;
                }
                var slot = HologramLayout.GetSlot(rank++);
                panel.MoveTo(slot.position, slot.scale, instant);
            }
        }

        /// <summary>Tasks from most to least important (see TaskRanker).</summary>
        public List<TaskItem> RankedTasks() => TaskRanker.Rank(Repository.Tasks, DateTime.Now);

        IEnumerable<TaskItem> OrderedTasks() => RankedTasks();

        /// <summary>"keep" = your dragged holograms stay; "auto" = Cypher re-positions them.</summary>
        public void SetPlacementMode(bool keepMyPlacement)
        {
            Repository.Data.placementMode = keepMyPlacement ? "keep" : "auto";
            if (!keepMyPlacement)
                foreach (var t in Repository.Tasks) t.userPlaced = false;
            Save();
            RefreshLayout(instant: false);
        }

        bool askingPlacement;

        void AskAboutPlacement()
        {
            if (askingPlacement) return;
            askingPlacement = true;
            HoloPrompt.Show(
                "You've moved some holograms yourself.\nKeep your placement, or let Cypher arrange them by priority?",
                "Keep my placement", "Let Cypher arrange",
                keep => { askingPlacement = false; SetPlacementMode(keep); },
                voiceA: @"\b(keep|leave|stay|mine|my placement|dont move)\b",
                voiceB: @"\b(arrange|organize|priority|you decide|go ahead|move them|sort)\b",
                spoken: "You've moved some holograms yourself. Should I keep your placement, or arrange them by priority?",
                replyA: "Okay, I'll leave the ones you placed where they are.",
                replyB: "Done. I'll arrange them by priority.");
        }

        int AutoPlacedRankOf(TaskItem target)
        {
            int rank = 0;
            foreach (var task in OrderedTasks())
            {
                if (task == target) return rank;
                if (!task.userPlaced) rank++;
            }
            return rank;
        }

        HologramPanel Spawn(TaskItem task)
        {
            var panel = Instantiate(hologramPrefab, hologramRoot);
            panel.Bind(task);
            panels[task.id] = panel;
            return panel;
        }

        void SeedSampleTasks()
        {
            var today = DateTime.Today;
            Add("Finish lab report", today.AddDays(2).AddHours(17), PriorityOverride.High, 4);
            Add("Call the dentist", today.AddDays(1).AddHours(12), PriorityOverride.Auto, 1);
            Add("Prepare presentation slides", today.AddDays(5).AddHours(9), PriorityOverride.Auto, 3);
            Add("Read chapter 7", today.AddDays(9).AddHours(20), PriorityOverride.Low, 2);
            Add("Buy groceries", null, PriorityOverride.Medium, 1);
            Add("Plan weekend hike", null, PriorityOverride.Auto, 2);

            void Add(string title, DateTime? due, PriorityOverride priority, int difficulty)
            {
                var task = TaskItem.Create(title);
                task.DueDate = due;
                task.priority = priority;
                task.difficulty = difficulty;
                Repository.Add(task);
            }
        }
    }
}
