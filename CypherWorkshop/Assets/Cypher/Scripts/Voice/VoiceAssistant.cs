using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace Cypher
{
    /// <summary>
    /// Cypher, the voice assistant. Pipeline:
    ///   microphone -> voice activity detection -> Whisper speech-to-text -> wake word check
    ///   -> intent (Gemini / Ollama / offline rules) -> action -> spoken reply (Piper / say).
    ///
    /// Say "Hey Cypher" alone (it chimes, then listens), or in one breath:
    /// "Hey Cypher, add buy milk". After Cypher asks a question you can just answer; no wake
    /// word needed. While you type in the edit panel, listening pauses.
    /// </summary>
    public class VoiceAssistant : MonoBehaviour
    {
        public static VoiceAssistant Instance { get; private set; }

        [SerializeField] bool voiceEnabled = true;
        [SerializeField] Material ringMaterial;

        [Header("Conversation")]
        [Tooltip("After Cypher asks something (or you say just 'Hey Cypher'), how long it listens without the wake word.")]
        [SerializeField] float followUpSeconds = 8f;
        [Tooltip("In voice edit mode, stop listening without the wake word after this much quiet.")]
        [SerializeField] float editModeIdleSeconds = 25f;
        [SerializeField] int maxReprompts = 2;

        enum Pending { None, AddTitle, AddDue, AddReminder, Choose, ReminderFollowUp }

        CypherConfig config;
        MicrophoneListener mic;
        SpeechRecognizer stt;
        TextToSpeech tts;
        CypherBrain brain;
        VoiceRing ring;

        bool ready;
        bool busy;
        bool engaged; // this utterance is for Cypher (wake word heard or a conversation is open)
        float listenUntil;
        float unmuteAt;
        bool voiceEditing;
        float lastEditCommandTime;

        Pending pending;
        int reprompts;
        string draftTitle;
        DateTime? draftDue, draftReminder;
        bool dueAnswered, reminderAnswered;
        PriorityOverride draftPriority;
        int draftDifficulty;
        TaskItem[] choices;
        string chooseAction;
        TaskItem reminderTask;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        public bool IsReady => ready;

        void Awake() => Instance = this;

        async void Start()
        {
            config = CypherConfig.Load();
            ring = new GameObject("Voice Ring").AddComponent<VoiceRing>();
            ring.Build(ringMaterial, TMP_Settings.defaultFontAsset);

            if (!voiceEnabled)
            {
                ring.SetState(RingState.Paused, null);
                ring.SetStatus("Voice is turned off");
                return;
            }

            ring.SetState(RingState.Thinking, null);
            ring.SetStatus("Loading speech model...");

            mic = gameObject.AddComponent<MicrophoneListener>();
            tts = gameObject.AddComponent<TextToSpeech>();
            tts.Init(config);
            stt = gameObject.AddComponent<SpeechRecognizer>();
            brain = new CypherBrain(config);

            if (!mic.Begin(config.microphoneDevice, config.micSensitivity))
            {
                ring.SetState(RingState.Paused, null);
                ring.SetStatus("No microphone found");
                return;
            }

            bool loaded = false;
            try { loaded = await stt.Init(config.whisperModel); }
            catch (Exception e) { Debug.LogException(e); }
            if (!loaded)
            {
                ring.SetState(RingState.Paused, null);
                ring.SetStatus("Speech model failed to load (see Console)");
                return;
            }

            mic.SegmentReady += OnSegment;
            ready = true;
            string ai = brain.HasGemini ? $"Gemini ({config.geminiModel})" : config.useOllama ? "Ollama if running, else offline rules" : "offline rules";
            Debug.Log($"[Cypher] Voice ready. Mic: {mic.DeviceName}. Understanding: {ai}. Voice: {tts.EngineName}.");
        }

        void Update()
        {
            if (!ready) return;

            bool typing = EditPanelController.IsTyping;
            if (tts.IsSpeaking) unmuteAt = Time.time + 0.35f; // don't hear our own voice tail
            mic.Muted = typing || busy || Time.time < unmuteAt;

            var panel = EditPanelController.Instance;
            if (voiceEditing && (panel == null || !panel.IsOpen || Time.time - lastEditCommandTime > editModeIdleSeconds))
                voiceEditing = false;

            if (!busy && !tts.IsSpeaking && listenUntil > 0f && Time.time > listenUntil && !mic.InSpeech)
            {
                listenUntil = 0f;
                RunSafely(OnFollowUpTimeout);
            }

            UpdateRing(typing);
        }

        void UpdateRing(bool typing)
        {
            if (typing)
            {
                ring.SetState(RingState.Paused, null);
                ring.SetStatus("Listening paused while you type");
            }
            else if (tts.IsSpeaking)
            {
                ring.SetState(RingState.Speaking, () => tts.Level);
                ring.SetStatus("");
            }
            else if (busy && engaged)
            {
                ring.SetState(RingState.Thinking, null);
                ring.SetStatus("Thinking...");
            }
            else if (Expecting)
            {
                ring.SetState(RingState.Listening, () => mic.Level);
                ring.SetStatus(voiceEditing ? "Editing by voice. Say 'save' when done" : "Listening...");
            }
            else
            {
                // Cypher transcribes everything to spot the wake word, but stays visibly idle
                // until it is actually addressed.
                ring.SetState(RingState.Idle, null);
                ring.SetStatus("Say 'Hey Cypher'");
            }
        }

        bool Expecting => listenUntil > Time.time || pending != Pending.None || voiceEditing;

        // ------------------------------------------------------------------ input

        void OnSegment(float[] samples)
        {
            if (busy) return;
            engaged = Expecting;
            RunSafely(async () =>
            {
                string text = await stt.Transcribe(samples);
                if (string.IsNullOrEmpty(text)) return;
                await Handle(text);
            });
        }

        async void RunSafely(Func<Task> work)
        {
            busy = true;
            try { await work(); }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                busy = false;
                engaged = false;
            }
        }

        async Task Handle(string text)
        {
            string command = text;
            if (WakeWord.TryFind(text, config.wakeWords, out string afterWake))
            {
                engaged = true;
                command = afterWake;
                // "Hey Cypher... hey Cypher, add milk": drop repeated wake words.
                while (WakeWord.TryFind(command, config.wakeWords, out string again)) command = again;
                if (command.Length == 0)
                {
                    ring.ShowHeard(text);
                    ring.Flash();
                    CypherAudio.Play(Sfx.Open, ring.transform.position, 0.7f);
                    listenUntil = Time.time + followUpSeconds;
                    return;
                }
            }
            else if (!Expecting)
            {
                return; // ordinary talk in the room; not for Cypher
            }

            Debug.Log($"[Cypher] Heard: \"{command}\"");
            ring.ShowHeard(command);
            listenUntil = 0f;
            if (voiceEditing) lastEditCommandTime = Time.time;

            if (HoloPrompt.IsOpen)
            {
                if (HoloPrompt.TryVoiceAnswer(command, out string promptReply)) await Say(promptReply);
                else await Ask(HoloPrompt.CurrentOptions);
                return;
            }

            var editPanel = EditPanelController.Instance;
            if (editPanel != null && editPanel.IsAskingSync)
            {
                await AnswerSyncQuestion(editPanel, command);
                return;
            }

            if (pending != Pending.None) await ContinuePending(command);
            else await Dispatch(await brain.Interpret(command, BuildContext()));
        }

        BrainContext BuildContext()
        {
            var ctx = new BrainContext { Now = DateTime.Now };
            var panel = EditPanelController.Instance;
            if (panel != null && panel.IsOpen && panel.EditingTask != null)
            {
                ctx.State = "editing";
                ctx.EditingTitle = panel.EditingTask.title;
            }
            ctx.TaskTitles = TaskManager.Instance.Repository.Tasks.Select(t => t.title).ToList();
            return ctx;
        }

        // ------------------------------------------------------------------ top-level commands

        async Task Dispatch(VoiceIntent intent)
        {
            Debug.Log($"[Cypher] Intent ({brain.LastSource}): {intent.intent} title='{intent.title}' task='{intent.task}' due='{intent.due}'");
            var panel = EditPanelController.Instance;
            bool editing = panel != null && panel.IsOpen;

            switch (intent.intent)
            {
                case VoiceIntent.AddTask: await StartAdd(intent); return;
                case VoiceIntent.EditTask: await ResolveTask(intent.task, "edit"); return;
                case VoiceIntent.RemoveTask: await ResolveTask(intent.task, "remove"); return;
                case VoiceIntent.MarkDone: await ResolveTask(intent.task, "done"); return;
                case VoiceIntent.ListTasks: await ListTasks(); return;
                case VoiceIntent.MostImportant: await MostImportant(); return;
                case VoiceIntent.Import:
                    var panelKind = (intent.task ?? "").Contains("notion") ? IntegrationHub.Kind.Notion
                        : (intent.task ?? "").Contains("task") ? IntegrationHub.Kind.GoogleTasks
                        : IntegrationHub.Kind.GoogleCalendar;
                    if (ImportPanel.Instance == null) { await Say("Importing isn't available."); return; }
                    ImportPanel.Instance.Open(panelKind);
                    await Say("Here's what I found. Tick the ones you want and press import.");
                    return;
                case VoiceIntent.Arrange:
                    TaskManager.Instance.SetPlacementMode(keepMyPlacement: false);
                    await Say("Arranged by priority. I'll keep them organized from now on.");
                    return;
                case VoiceIntent.Yes:
                case VoiceIntent.No:
                case VoiceIntent.Cancel when !editing:
                    await Say("Okay.");
                    return;
            }

            if (editing)
            {
                await ApplyEdit(panel, intent);
                return;
            }
            if (intent.intent.StartsWith("set_") || intent.intent.StartsWith("clear_") || intent.intent == VoiceIntent.Save)
            {
                await Say("No task is open. Say edit, then the task's name.");
                return;
            }
            await Say("Sorry, I didn't catch that. You can say add, edit, remove, or what's on my list.");
        }

        async Task ListTasks()
        {
            var open = TaskManager.Instance.RankedTasks().Where(t => !t.done).ToList();
            if (open.Count == 0)
            {
                await Say("Your list is clear. Nice.");
                return;
            }
            var lines = open.Take(5).Select(t => t.title + (t.DueDate.HasValue ? ", due " + Describe(t.DueDate.Value) : ""));
            string more = open.Count > 5 ? $" And {open.Count - 5} more." : "";
            await Say($"You have {open.Count} task{(open.Count == 1 ? "" : "s")}. {string.Join(". ", lines)}.{more}");
        }

        async Task MostImportant()
        {
            var top = TaskManager.Instance.RankedTasks().FirstOrDefault(t => !t.done);
            if (top == null)
            {
                await Say("There's nothing on your list right now.");
                return;
            }
            TaskManager.Instance.GetPanel(top.id)?.Flash(2f);
            await Say($"Your most important task is {top.title}{(top.DueDate.HasValue ? ", due " + Describe(top.DueDate.Value) : "")}.");
        }

        // ------------------------------------------------------------------ add flow

        async Task StartAdd(VoiceIntent intent)
        {
            draftTitle = (intent.title ?? "").Trim();
            if (!RuleBrain.IsMeaningfulTitle(draftTitle)) draftTitle = ""; // "add a..." -> ask for the title
            draftDue = VoiceIntent.ParseDate(intent.due);
            draftReminder = VoiceIntent.ParseDate(intent.reminder);
            dueAnswered = draftDue.HasValue;
            reminderAnswered = draftReminder.HasValue;
            draftPriority = VoiceIntent.ParsePriority(intent.priority) ?? PriorityOverride.Auto;
            draftDifficulty = intent.difficulty >= 1 && intent.difficulty <= 5 ? intent.difficulty : 3;
            reprompts = 0;

            if (draftTitle.Length == 0)
            {
                pending = Pending.AddTitle;
                await Ask("What should I add?");
                return;
            }
            await NextAddStep();
        }

        async Task NextAddStep()
        {
            reprompts = 0;
            if (!dueAnswered)
            {
                pending = Pending.AddDue;
                await Ask("When do you want this done by?");
            }
            else if (draftDue.HasValue && !reminderAnswered)
            {
                pending = Pending.AddReminder;
                await Ask("When should I remind you?");
            }
            else
            {
                await FinishAdd("");
            }
        }

        async Task FinishAdd(string prefix)
        {
            pending = Pending.None;
            var tm = TaskManager.Instance;
            var task = tm.AddTask(draftTitle);
            task.DueDate = draftDue;
            task.Reminder = draftDue.HasValue ? draftReminder : null;
            task.priority = draftPriority;
            task.difficulty = draftDifficulty;
            tm.NotifyTaskChanged(task);

            string due = draftDue.HasValue ? ", due " + Describe(draftDue.Value) : " with no deadline";
            string remind = task.Reminder.HasValue ? $". I'll remind you {Describe(task.Reminder.Value, includeTime: true)}" : "";
            await Say($"{prefix}Added {draftTitle}{due}{remind}.");
        }

        async Task ContinuePending(string answer)
        {
            if (Regex.IsMatch(NaturalDate.Normalize(answer), @"^(cancel|never mind|nevermind|stop|forget it)\b"))
            {
                pending = Pending.None;
                await Say("Okay, cancelled.");
                return;
            }

            switch (pending)
            {
                case Pending.AddTitle:
                    var parsed = RuleBrain.Interpret("add " + answer, BuildContext());
                    if (!RuleBrain.IsMeaningfulTitle(parsed.title))
                    {
                        await Reprompt("What should the task be called?");
                        break;
                    }
                    draftTitle = parsed.title;
                    if (!dueAnswered && VoiceIntent.ParseDate(parsed.due) is DateTime d)
                    {
                        draftDue = d;
                        dueAnswered = true;
                    }
                    await NextAddStep();
                    break;

                case Pending.AddDue:
                    if (NaturalDate.MeansNoDate(answer))
                    {
                        draftDue = null;
                        dueAnswered = true;
                        await FinishAdd("");
                    }
                    else if (await TryGetDate(answer, "When do you want this done by?", new TimeSpan(23, 59, 0)) is DateTime due)
                    {
                        draftDue = due;
                        dueAnswered = true;
                        await NextAddStep();
                    }
                    else await Reprompt("Sorry, I didn't get a date. Say a day like next Friday, or say undetermined.");
                    break;

                case Pending.AddReminder:
                    if (NaturalDate.MeansNoDate(answer))
                    {
                        draftReminder = null;
                        reminderAnswered = true;
                        await FinishAdd("");
                    }
                    else if (await TryGetDate(answer, "When should I remind you?", new TimeSpan(9, 0, 0)) is DateTime reminder)
                    {
                        var usable = NaturalDate.FutureReminder(reminder, DateTime.Now);
                        if (usable == null)
                        {
                            await Reprompt("That time has already passed. When should I remind you?");
                            break;
                        }
                        draftReminder = usable;
                        reminderAnswered = true;
                        await FinishAdd("");
                    }
                    else await Reprompt("Sorry, when should I remind you? For example, Thursday at 6 PM. Or say no reminder.");
                    break;

                case Pending.ReminderFollowUp:
                    pending = Pending.None;
                    await HandleReminderReply(answer);
                    break;

                case Pending.Choose:
                    var pick = PickChoice(answer);
                    pending = Pending.None;
                    if (pick != null) await Perform(chooseAction, pick);
                    else await Say("Okay, I'll leave it.");
                    break;
            }
        }

        /// <summary>Local date parser first (instant); the AI only if that fails.</summary>
        async Task<DateTime?> TryGetDate(string answer, string question, TimeSpan defaultTime)
        {
            var now = DateTime.Now;
            if (NaturalDate.TryParse(answer, now, out var local))
                return local.HasTime ? local.Value : local.Value.Date + defaultTime;

            bool reminder = defaultTime.Hours == 9;
            var intent = await brain.Interpret(
                $"(Answer to \"{question}\") {answer}. Respond with intent {(reminder ? "set_reminder and the reminder field" : "set_due and the due field")}.",
                BuildContext());
            return VoiceIntent.ParseDate(reminder ? intent.reminder : intent.due) ?? VoiceIntent.ParseDate(intent.due);
        }

        async Task Reprompt(string question)
        {
            if (++reprompts > maxReprompts)
            {
                if (pending == Pending.AddTitle)
                {
                    pending = Pending.None;
                    await Say("Okay, never mind.");
                    return;
                }
                if (pending == Pending.AddDue) { draftDue = null; dueAnswered = true; }
                if (pending == Pending.AddReminder) { draftReminder = null; reminderAnswered = true; }
                await FinishAdd("I'll leave that open for now. ");
                return;
            }
            await Ask(question);
        }

        async Task OnFollowUpTimeout()
        {
            switch (pending)
            {
                case Pending.AddTitle:
                    pending = Pending.None;
                    break;
                case Pending.AddDue:
                    draftDue = null;
                    dueAnswered = true;
                    await FinishAdd("I didn't hear a deadline, so ");
                    break;
                case Pending.AddReminder:
                    draftReminder = null;
                    reminderAnswered = true;
                    await FinishAdd("");
                    break;
                case Pending.Choose:
                case Pending.ReminderFollowUp:
                    pending = Pending.None;
                    break;
            }
        }

        // ------------------------------------------------------------------ reminders & placement

        /// <summary>Called by ReminderService. Waits until Cypher isn't mid-conversation.</summary>
        public void AnnounceReminders(List<TaskItem> tasks, bool missed) => RunWhenIdle(async () =>
        {
            var now = DateTime.Now;
            string Line(TaskItem t) => t.title + (t.DueDate.HasValue ? ", due " + NaturalDate.Describe(t.DueDate.Value, true, now) : "");
            string text = missed
                ? (tasks.Count == 1 ? $"While you were away, a reminder came due: {Line(tasks[0])}." : $"While you were away, {tasks.Count} reminders came due: {string.Join(". ", tasks.Select(Line))}.")
                : (tasks.Count == 1 ? $"Reminder: {Line(tasks[0])}." : $"Reminders: {string.Join(". ", tasks.Select(Line))}.");

            ring.Flash();
            if (tasks.Count == 1 && ready)
            {
                reminderTask = tasks[0];
                pending = Pending.ReminderFollowUp;
                await Ask(text);
            }
            else await Say(text);
        });

        async Task HandleReminderReply(string answer)
        {
            var task = reminderTask;
            reminderTask = null;
            if (task == null || TaskManager.Instance.Repository.Get(task.id) == null) return;
            string t = NaturalDate.Normalize(answer);

            if (Regex.IsMatch(t, @"\b(done|finished|complete|did it)\b"))
            {
                task.done = true;
                TaskManager.Instance.NotifyTaskChanged(task);
                await Say($"Nice. {task.title} is done.");
                return;
            }
            if (Regex.IsMatch(t, @"\b(snooze|later|again|remind me)\b") || NaturalDate.TryParse(t, DateTime.Now, out _))
            {
                var now = DateTime.Now;
                DateTime next = NaturalDate.TryParse(t, now, out var when) && when.Value > now
                    ? (when.HasTime ? when.Value : when.Value.Date + new TimeSpan(9, 0, 0))
                    : now.AddMinutes(10);
                task.Reminder = next;
                task.reminderFired = false;
                TaskManager.Instance.NotifyTaskChanged(task, relayout: false);
                await Say($"Okay, I'll remind you again {NaturalDate.Describe(next, true, now)}.".Replace("today at", "at"));
                return;
            }
            if (Regex.IsMatch(t, @"^(ok|okay|thanks|thank you|got it|alright|sure|cool)\b"))
            {
                await Say("You're welcome.");
                return;
            }
            await Dispatch(await brain.Interpret(answer, BuildContext())); // something else entirely
        }

        /// <summary>Cypher reads out the open HoloPrompt question and listens for the answer.</summary>
        public void AskOpenPrompt() => RunWhenIdle(async () =>
        {
            string question = HoloPrompt.CurrentSpokenQuestion;
            if (HoloPrompt.IsOpen && !string.IsNullOrEmpty(question)) await Ask(question);
        });

        /// <summary>Runs work once Cypher is free (not transcribing, thinking or speaking).</summary>
        async void RunWhenIdle(Func<Task> work)
        {
            if (!voiceEnabled) return;
            while (tts == null || busy || tts.IsSpeaking)
            {
                if (this == null) return; // destroyed (Play stopped) while waiting
                await Awaitable.NextFrameAsync();
            }
            RunSafely(work);
        }

        // ------------------------------------------------------------------ edit / remove / done

        async Task ResolveTask(string spoken, string action)
        {
            var candidates = TaskManager.Instance.Repository.Tasks.Where(t => action != "done" || !t.done).ToList();
            var match = TaskMatcher.Find(spoken ?? "", candidates);
            if (match.Best == null || match.Score < 0.35f)
            {
                await Say($"I couldn't find a task called {spoken}.");
                return;
            }
            if (match.Ambiguous)
            {
                choices = new[] { match.Best, match.RunnerUp };
                chooseAction = action;
                pending = Pending.Choose;
                await Ask($"Did you mean {match.Best.title}, or {match.RunnerUp.title}?");
                return;
            }
            await Perform(action, match.Best);
        }

        TaskItem PickChoice(string answer)
        {
            string t = NaturalDate.Normalize(answer);
            if (Regex.IsMatch(t, @"\b(first|former|1st|one)\b")) return choices[0];
            if (Regex.IsMatch(t, @"\b(second|latter|2nd|two|other)\b")) return choices[1];
            if (Regex.IsMatch(t, @"^(no|neither|none)\b")) return null;
            var m = TaskMatcher.Find(answer, choices);
            return m.Score >= 0.3f ? m.Best : null;
        }

        async Task Perform(string action, TaskItem task)
        {
            var tm = TaskManager.Instance;
            switch (action)
            {
                case "edit":
                    var hologram = tm.GetPanel(task.id);
                    if (hologram == null) return;
                    tm.RequestEdit(hologram);
                    voiceEditing = true;
                    lastEditCommandTime = Time.time;
                    await Say($"Editing {task.title}. What would you like to change?");
                    break;

                case "remove":
                    tm.RemoveTask(task.id);
                    await Say($"Removing {task.title}.");
                    break;

                case "done":
                    task.done = true;
                    tm.NotifyTaskChanged(task);
                    await Say($"Nice work. {task.title} is done.");
                    break;
            }
        }

        async Task ApplyEdit(EditPanelController panel, VoiceIntent intent)
        {
            var now = DateTime.Now;
            switch (intent.intent)
            {
                case VoiceIntent.SetTitle when !string.IsNullOrWhiteSpace(intent.title):
                    panel.SetTitle(intent.title.Trim());
                    await Say($"Renamed to {intent.title.Trim()}.");
                    break;
                case VoiceIntent.SetNotes when !string.IsNullOrWhiteSpace(intent.notes):
                    string notes = panel.EditingTask.notes ?? "";
                    panel.SetNotes(notes.Length > 0 ? notes + "\n" + intent.notes.Trim() : intent.notes.Trim());
                    await Say("Note added.");
                    break;
                case VoiceIntent.SetDue when VoiceIntent.ParseDate(intent.due) is DateTime due:
                    panel.SetDueDate(due);
                    await Say($"Deadline changed to {Describe(due)}.");
                    break;
                case VoiceIntent.ClearDue:
                    panel.SetDueDate(null);
                    await Say("Deadline removed.");
                    break;
                case VoiceIntent.SetReminder when VoiceIntent.ParseDate(intent.reminder) is DateTime reminder:
                    if (!panel.EditingTask.DueDate.HasValue)
                    {
                        await Say("Set a deadline first, then I can add a reminder.");
                        break;
                    }
                    if (!(NaturalDate.FutureReminder(reminder, now) is DateTime upcoming))
                    {
                        await Say("That time has already passed. Try another time.");
                        break;
                    }
                    panel.SetReminder(upcoming);
                    await Say($"I'll remind you {Describe(upcoming, includeTime: true)}.");
                    break;
                case VoiceIntent.ClearReminder:
                    panel.SetReminder(null);
                    await Say("Reminder removed.");
                    break;
                case VoiceIntent.SetPriority when VoiceIntent.ParsePriority(intent.priority) is PriorityOverride p:
                    panel.SetPriority(p);
                    await Say(p == PriorityOverride.Auto ? "Okay, I'll decide the priority." : $"Priority set to {p.ToString().ToLowerInvariant()}.");
                    break;
                case VoiceIntent.SetDifficulty when intent.difficulty >= 1 && intent.difficulty <= 5:
                    panel.SetDifficulty(intent.difficulty);
                    await Say($"Difficulty set to {intent.difficulty}.");
                    break;
                case VoiceIntent.SetDone:
                    panel.SetDone(true);
                    await Say("Marked as done.");
                    break;
                case VoiceIntent.SetNotDone:
                    panel.SetDone(false);
                    await Say("Marked as not done.");
                    break;
                case VoiceIntent.Save:
                    panel.Save();
                    if (panel.IsAskingSync)
                    {
                        await Ask("This task was imported. Should I update the original too, or keep the change local?");
                        break;
                    }
                    voiceEditing = false;
                    await Say("Saved.");
                    break;
                case VoiceIntent.Cancel:
                    voiceEditing = false;
                    panel.Cancel();
                    await Say("Changes discarded.");
                    break;
                case VoiceIntent.DeleteCurrent:
                    voiceEditing = false;
                    panel.DeleteImmediately();
                    await Say("Deleted.");
                    break;
                default:
                    await Say("You can rename it, change the deadline, reminder, priority or difficulty, or say save.");
                    break;
            }
        }

        async Task AnswerSyncQuestion(EditPanelController panel, string answer)
        {
            string t = NaturalDate.Normalize(answer);
            if (Regex.IsMatch(t, @"\b(local|only here|just here|dont|do not|no)\b"))
            {
                voiceEditing = false;
                panel.AnswerSync(updateOriginal: false);
                await Say("Saved locally.");
            }
            else if (Regex.IsMatch(t, @"\b(update|original|both|yes|sync|everywhere)\b"))
            {
                voiceEditing = false;
                panel.AnswerSync(updateOriginal: true);
                await Say("Saved. I'll update the original too.");
            }
            else
            {
                await Ask("Say update the original, or keep it local.");
            }
        }

        // ------------------------------------------------------------------ speaking

        async Task Say(string text)
        {
            ring.ShowReply(text);
            Debug.Log($"[Cypher] Says: {text}");
            await tts.Speak(text);
        }

        async Task Ask(string question)
        {
            await Say(question);
            listenUntil = Time.time + followUpSeconds;
        }

        static string Describe(DateTime d, bool includeTime = false) => NaturalDate.Describe(d, includeTime, DateTime.Now);
    }
}
