using System;
using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Cypher
{
    /// <summary>
    /// The holographic edit panel. Opens when a hologram is double-clicked or its pencil is clicked
    /// (TaskManager.EditRequested): the hologram expands and flies toward you, becoming this panel.
    /// Edits go into a copy of the task; Save applies them, Cancel/Esc throws them away.
    ///
    /// The whole UI is built in code on Awake, so nothing needs wiring in the Inspector.
    /// Public methods (SetTitle, SetDueDate, SetPriority, Save, ...) are what the Phase 4 voice
    /// commands call to change the open panel live.
    ///
    /// Keys: Esc = close the date picker / dialog, or cancel. Cmd+Enter = save.
    /// </summary>
    public class EditPanelController : MonoBehaviour
    {
        public static EditPanelController Instance { get; private set; }

        /// <summary>True while a text field has keyboard focus. Voice listening pauses on this.</summary>
        public static bool IsTyping => Instance != null && Instance.IsOpen && Instance.AnyFieldFocused;

        [Header("Look (assigned by the scene builder; blank = created at runtime)")]
        [SerializeField] TMP_FontAsset font;
        [SerializeField] Material uiRectMaterial;
        [SerializeField] Material uiFlatMaterial;
        [SerializeField] Material hologramMaterial;
        [SerializeField] Material uiGlassMaterial;

        [Header("Placement")]
        [Tooltip("Meters in front of the camera.")]
        [SerializeField] float distance = 0.66f;
        [SerializeField] float verticalOffset = -0.02f;

        [Header("Animation")]
        [SerializeField] float openSeconds = 0.45f;
        [SerializeField] float closeSeconds = 0.35f;
        [Tooltip("The edit panel is a little brighter than a resting hologram.")]
        [SerializeField] float panelBrightness = 1.35f;
        [SerializeField] float deleteConfirmSeconds = 3f;

        const float W = 560f, H = 600f, Pad = 24f;
        const float UnitsToMeters = 0.001f;

        static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        static readonly int MaterializeId = Shader.PropertyToID("_Materialize");

        public bool IsOpen { get; private set; }
        /// <summary>The holographic UI kit (fonts, materials), shared with other prompts.</summary>
        public HoloUI UI => ui;
        public TaskItem EditingTask => work;

        HoloUI ui;
        Material textMaterial;
        Transform panel;
        CanvasGroup canvasGroup;
        CanvasGroup contentGroup;
        Material backgroundMaterial;

        TMP_InputField titleField, notesField, reminderTimeField;
        TextMeshProUGUI sourceBadge, dueLabel, reminderDateLabel, reminderHint, deleteLabel, dialogText;
        Button dueButton, reminderDateButton, clearReminderButton, deleteButton;
        HoloToggle undeterminedToggle, doneToggle;
        HoloSegment[] prioritySegments;
        HoloSegment[] difficultySegments;
        HoloDatePicker picker;
        GameObject reminderRow, dialog;

        HologramPanel source;
        TaskItem original;
        TaskItem work;
        bool animating;
        float deleteArmedUntil = -1f;

        enum CloseMode { Cancel, Save, Delete }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        bool AnyFieldFocused =>
            titleField.isFocused || notesField.isFocused || reminderTimeField.isFocused;

        void Awake()
        {
            Instance = this;
            EnsureEventSystem();
            ResolveLook();
            Build();
            panel.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            StartCoroutine(SubscribeWhenReady());
        }

        IEnumerator SubscribeWhenReady()
        {
            while (TaskManager.Instance == null) yield return null;
            TaskManager.Instance.EditRequested -= Open;
            TaskManager.Instance.EditRequested += Open;
        }

        void OnDisable()
        {
            if (TaskManager.Instance != null) TaskManager.Instance.EditRequested -= Open;
        }

        void OnDestroy()
        {
            if (backgroundMaterial != null) Destroy(backgroundMaterial);
            if (textMaterial != null) Destroy(textMaterial);
            InputLock.Release(this);
        }

        // ------------------------------------------------------------------ public API (UI + voice)

        public void Open(HologramPanel hologram)
        {
            if (IsOpen || hologram == null || hologram.Task == null) return;

            source = hologram;
            original = hologram.Task;
            work = original.Clone();
            IsOpen = true;
            InputLock.Acquire(this);
            deleteArmedUntil = -1f;

            source.SetHiddenForEdit(true);
            LoadFields();
            picker.Hide();
            dialog.SetActive(false);
            contentGroup.alpha = 1f;
            contentGroup.interactable = true;

            panel.gameObject.SetActive(true);
            CypherAudio.Play(Sfx.Open, source.transform.position);
            StartCoroutine(Animate(opening: true, () => StartCoroutine(FocusTitle())));
        }

        public void SetTitle(string title)
        {
            if (!IsOpen) return;
            work.title = title;
            titleField.SetTextWithoutNotify(title);
        }

        public void SetNotes(string notes)
        {
            if (!IsOpen) return;
            work.notes = notes;
            notesField.SetTextWithoutNotify(notes);
        }

        /// <summary>null = "Undetermined" (also clears the reminder).</summary>
        public void SetDueDate(DateTime? due)
        {
            if (!IsOpen) return;
            if (due.HasValue)
            {
                // Keep the old time of day if there was one; new deadlines mean "by end of day".
                var time = work.DueDate?.TimeOfDay ?? new TimeSpan(23, 59, 0);
                work.DueDate = due.Value.Date + (due.Value.TimeOfDay == TimeSpan.Zero ? time : due.Value.TimeOfDay);
            }
            else
            {
                work.DueDate = null;
                work.Reminder = null;
            }
            RefreshDynamic();
        }

        public void SetReminder(DateTime? reminder)
        {
            if (!IsOpen || !work.DueDate.HasValue) return;
            work.Reminder = reminder;
            RefreshDynamic();
        }

        public void SetPriority(PriorityOverride priority)
        {
            if (!IsOpen) return;
            work.priority = priority;
            RefreshDynamic();
        }

        public void SetDifficulty(int difficulty)
        {
            if (!IsOpen) return;
            work.difficulty = Mathf.Clamp(difficulty, 1, 5);
            RefreshDynamic();
        }

        public void SetDone(bool done)
        {
            if (!IsOpen) return;
            work.done = done;
            doneToggle.Set(done, notify: false);
        }

        public void Save()
        {
            if (!IsOpen || animating) return;
            CommitReminderTime();

            if (string.IsNullOrWhiteSpace(work.title))
            {
                FlashError(titleField);
                return;
            }
            // A changed reminder must be in the future, or it would fire the moment you save.
            if (work.Reminder.HasValue && work.reminderIso != original.reminderIso && work.Reminder.Value <= DateTime.Now)
            {
                FlashError(reminderTimeField);
                return;
            }
            work.title = work.title.Trim();
            work.notes = (work.notes ?? "").TrimEnd(); // Cmd+Enter in Notes can leave a stray newline

            if (!HasChanges())
            {
                Close(CloseMode.Cancel);
                return;
            }

            if (original.source != TaskSource.Local)
            {
                ShowSyncDialog();
                return;
            }
            Apply(syncOriginal: false);
        }

        public void Cancel()
        {
            if (!IsOpen || animating) return;
            Close(CloseMode.Cancel);
        }

        public void Delete()
        {
            if (!IsOpen || animating) return;
            if (Time.unscaledTime > deleteArmedUntil)
            {
                // First press arms it; a second press within a few seconds deletes.
                deleteArmedUntil = Time.unscaledTime + deleteConfirmSeconds;
                deleteLabel.text = "Confirm?";
                deleteLabel.color = HoloUI.WarnColor;
                return;
            }
            Close(CloseMode.Delete);
        }

        /// <summary>True while the "apply this edit to the original too?" question is showing.</summary>
        public bool IsAskingSync => IsOpen && dialog != null && dialog.activeSelf;

        /// <summary>Answers the imported-task question (used by voice).</summary>
        public void AnswerSync(bool updateOriginal)
        {
            if (!IsAskingSync || animating) return;
            HideSyncDialog();
            Apply(syncOriginal: updateOriginal);
        }

        /// <summary>Delete without the "Confirm?" step (voice commands confirm by speaking).</summary>
        public void DeleteImmediately()
        {
            if (!IsOpen || animating) return;
            Close(CloseMode.Delete);
        }

        // ------------------------------------------------------------------ frame loop

        void Update()
        {
            if (!IsOpen || animating) return;

            if (deleteArmedUntil > 0f && Time.unscaledTime > deleteArmedUntil)
            {
                deleteArmedUntil = -1f;
                deleteLabel.text = "Delete";
                deleteLabel.color = HoloUI.TextColor;
            }

            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (dialog.activeSelf) HideSyncDialog();
                else if (picker.IsOpen) picker.Hide();
                else Cancel();
                return;
            }

            bool command = kb.leftMetaKey.isPressed || kb.rightMetaKey.isPressed || kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            if (command && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                Save();
        }

        // ------------------------------------------------------------------ open/close animation

        IEnumerator Animate(bool opening, Action done)
        {
            animating = true;
            canvasGroup.interactable = false;
            float seconds = opening ? openSeconds : closeSeconds;
            float holoWidth = source.PanelSize.x;

            for (float t = 0f; ; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.Clamp01(t / seconds);
                k = 1f - Mathf.Pow(1f - k, 3f);          // ease out
                float a = opening ? k : 1f - k;          // 0 = at the hologram, 1 = in front of you

                ComputeEditPose(out var editPos, out var editRot);
                var holo = source.transform;
                float startScale = holo.lossyScale.x * holoWidth / (W * UnitsToMeters);

                transform.SetPositionAndRotation(
                    Vector3.Lerp(holo.position, editPos, a),
                    Quaternion.Slerp(holo.rotation, editRot, a));
                transform.localScale = Vector3.one * Mathf.Lerp(startScale, 1f, a);

                canvasGroup.alpha = Mathf.Clamp01((a - 0.55f) / 0.45f);
                backgroundMaterial.SetFloat(MaterializeId, Mathf.Lerp(0.55f, 1f, a));
                backgroundMaterial.SetFloat(BrightnessId, Mathf.Lerp(1f, panelBrightness, a));

                if (t >= seconds) break;
                yield return null;
            }

            canvasGroup.interactable = true;
            animating = false;
            done?.Invoke();
        }

        void ComputeEditPose(out Vector3 position, out Quaternion rotation)
        {
            var cam = Camera.main != null ? Camera.main.transform : transform;
            position = cam.position + cam.forward * distance + cam.up * verticalOffset;
            rotation = Quaternion.LookRotation(cam.forward, cam.up);
        }

        IEnumerator FocusTitle()
        {
            titleField.ActivateInputField();
            yield return null; // the field needs a frame to activate before the caret can move
            titleField.MoveTextEnd(false);
        }

        void Close(CloseMode mode)
        {
            picker.Hide();
            dialog.SetActive(false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            CypherAudio.Play(Sfx.Close, transform.position);

            StartCoroutine(Animate(opening: false, () =>
            {
                panel.gameObject.SetActive(false);
                source.SetHiddenForEdit(false);
                if (mode == CloseMode.Save) source.PlaySyncShimmer();

                string id = original.id;
                IsOpen = false;
                source = null;
                original = null;
                work = null;
                InputLock.Release(this);

                if (mode == CloseMode.Delete) TaskManager.Instance.RemoveTask(id);
            }));
        }

        // ------------------------------------------------------------------ saving

        bool HasChanges() =>
            original.title != work.title
            || (original.notes ?? "") != (work.notes ?? "")
            || original.dueDateIso != work.dueDateIso
            || original.reminderIso != work.reminderIso
            || original.priority != work.priority
            || original.difficulty != work.difficulty
            || original.done != work.done;

        void Apply(bool syncOriginal)
        {
            bool rerank = original.dueDateIso != work.dueDateIso
                || original.priority != work.priority
                || original.difficulty != work.difficulty
                || original.done != work.done;
            bool reminderChanged = original.reminderIso != work.reminderIso;

            original.CopyEditableFieldsFrom(work);
            if (reminderChanged) original.reminderFired = false;
            if (syncOriginal) original.pendingSync = true; // Phase 7 pushes the edit to the source

            TaskManager.Instance.NotifyTaskChanged(original, rerank);
            if (syncOriginal && IntegrationHub.Instance != null) IntegrationHub.Instance.Kick();
            Close(CloseMode.Save);
        }

        void ShowSyncDialog()
        {
            string where = IntegrationHub.SourceName(original.source);
            dialogText.text = $"This task came from {where}.\nApply this edit to the original too?";
            contentGroup.alpha = 0.12f;
            contentGroup.interactable = false;
            picker.Hide();
            dialog.SetActive(true);
        }

        void HideSyncDialog()
        {
            dialog.SetActive(false);
            contentGroup.alpha = 1f;
            contentGroup.interactable = true;
        }

        // ------------------------------------------------------------------ fields <-> task

        void LoadFields()
        {
            titleField.SetTextWithoutNotify(work.title ?? "");
            notesField.SetTextWithoutNotify(work.notes ?? "");
            sourceBadge.text = work.source switch
            {
                TaskSource.GoogleCalendar => "FROM GOOGLE CALENDAR",
                TaskSource.Notion => "FROM NOTION",
                TaskSource.GoogleTasks => "FROM GOOGLE TASKS",
                _ => "LOCAL TASK",
            };
            doneToggle.Set(work.done, notify: false);
            deleteLabel.text = "Delete";
            deleteLabel.color = HoloUI.TextColor;
            RefreshDynamic();
        }

        /// <summary>Updates everything that depends on the due date, reminder, priority and difficulty.</summary>
        void RefreshDynamic()
        {
            var culture = CultureInfo.CurrentCulture;
            var due = work.DueDate;
            undeterminedToggle.Set(!due.HasValue, notify: false);
            dueLabel.text = due.HasValue ? due.Value.ToString("ddd, MMM d, yyyy", culture) : "No deadline";

            reminderRow.SetActive(due.HasValue);
            reminderHint.gameObject.SetActive(!due.HasValue);
            var reminder = work.Reminder;
            reminderDateLabel.text = reminder.HasValue ? reminder.Value.ToString("ddd, MMM d", culture) : "Set reminder";
            reminderTimeField.SetTextWithoutNotify(reminder.HasValue ? reminder.Value.ToString("h:mm tt", culture) : "");
            reminderTimeField.interactable = due.HasValue; // type a time straight away; the day is picked for you
            clearReminderButton.interactable = reminder.HasValue;

            for (int i = 0; i < prioritySegments.Length; i++)
                prioritySegments[i].SetSelected(PriorityForSegment(i) == work.priority);
            for (int i = 0; i < difficultySegments.Length; i++)
                difficultySegments[i].SetSelected(work.difficulty == i + 1);
        }

        void CommitReminderTime()
        {
            if (!work.DueDate.HasValue) return;
            string typed = reminderTimeField.text;
            if (string.IsNullOrWhiteSpace(typed))
            {
                RefreshDynamic();
                return;
            }
            if (!TryParseTime(typed, out var time))
            {
                FlashError(reminderTimeField);
                RefreshDynamic();
                return;
            }

            if (work.Reminder.HasValue)
            {
                work.Reminder = work.Reminder.Value.Date + time;
            }
            else
            {
                // No day picked yet: use the default reminder day, or the next day if that time already passed.
                var candidate = DefaultReminderDay() + time;
                if (candidate <= DateTime.Now) candidate = candidate.AddDays(1);
                work.Reminder = candidate;
            }
            RefreshDynamic();
        }

        /// <summary>The day before the deadline, but never earlier than today.</summary>
        DateTime DefaultReminderDay()
        {
            var day = (work.DueDate ?? DateTime.Today.AddDays(1)).Date.AddDays(-1);
            return day < DateTime.Today ? DateTime.Today : day;
        }

        void PickDueDate()
        {
            picker.Show(work.DueDate, "DUE DATE", date =>
            {
                SetDueDate(date);
                picker.Hide();
            });
        }

        void PickReminderDate()
        {
            DateTime? start = work.Reminder ?? DefaultReminderDay();
            picker.Show(start, "REMIND ME ON", date =>
            {
                if (date.Date < DateTime.Today)
                {
                    FlashError(reminderTimeField);
                    return; // keep the calendar open: that day has passed
                }
                var time = work.Reminder?.TimeOfDay ?? new TimeSpan(9, 0, 0);
                // Picking today when 9 AM has already passed: use the next full hour instead.
                SetReminder(NaturalDate.FutureReminder(date.Date + time, DateTime.Now));
                picker.Hide();
            });
        }

        static PriorityOverride PriorityForSegment(int i) => i switch
        {
            0 => PriorityOverride.Low,
            1 => PriorityOverride.Medium,
            2 => PriorityOverride.High,
            _ => PriorityOverride.Auto,
        };

        void FlashError(TMP_InputField field)
        {
            CypherAudio.Play(Sfx.Error, field.transform.position);
            StartCoroutine(FlashFrame(field.GetComponent<Image>()));
        }

        static IEnumerator FlashFrame(Image frame)
        {
            for (int i = 0; i < 3; i++)
            {
                frame.color = HoloUI.WarnColor;
                yield return new WaitForSecondsRealtime(0.1f);
                frame.color = HoloUI.FrameColor;
                yield return new WaitForSecondsRealtime(0.08f);
            }
        }

        /// <summary>Accepts "9", "9:30", "9:30pm", "9 pm", "21:30", "0930".</summary>
        public static bool TryParseTime(string text, out TimeSpan time)
        {
            time = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text.Trim().ToLowerInvariant().Replace(" ", "").Replace(".", "");

            bool pm = s.EndsWith("pm") || s.EndsWith("p");
            bool am = s.EndsWith("am") || s.EndsWith("a");
            s = s.TrimEnd('a', 'p', 'm');

            int hour, minute = 0;
            if (s.Contains(":"))
            {
                var parts = s.Split(':');
                if (parts.Length != 2 || !int.TryParse(parts[0], out hour) || !int.TryParse(parts[1], out minute)) return false;
            }
            else if (s.Length >= 3 && int.TryParse(s, out int compact))
            {
                hour = compact / 100;
                minute = compact % 100;
            }
            else if (!int.TryParse(s, out hour))
            {
                return false;
            }

            if (pm || am)
            {
                if (hour < 1 || hour > 12) return false;
                if (hour == 12) hour = 0;
                if (pm) hour += 12;
            }
            if (hour < 0 || hour > 23 || minute < 0 || minute > 59) return false;
            time = new TimeSpan(hour, minute, 0);
            return true;
        }

        // ------------------------------------------------------------------ building the UI

        void ResolveLook()
        {
            if (font == null) font = TMP_Settings.defaultFontAsset;
            textMaterial = HoloUI.ReadableTextMaterial(font);
            if (uiRectMaterial == null) uiRectMaterial = new Material(Shader.Find("Cypher/UIGlow"));
            if (uiFlatMaterial == null)
            {
                uiFlatMaterial = new Material(Shader.Find("Cypher/UIGlow"));
                uiFlatMaterial.SetFloat("_BorderPx", 0f);
                uiFlatMaterial.SetFloat("_Intensity", 2.4f);
            }
            if (hologramMaterial == null) hologramMaterial = new Material(Shader.Find("Cypher/Hologram"));
            if (uiGlassMaterial == null) uiGlassMaterial = new Material(Shader.Find("Cypher/UIGlass"));
            ui = new HoloUI(font, textMaterial, uiRectMaterial, uiFlatMaterial, uiGlassMaterial);
        }

        static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        void Build()
        {
            panel = new GameObject("Panel").transform;
            panel.SetParent(transform, false);

            // Hologram backdrop: same shader as the task holograms, with a faint fill so the
            // frosted glass behind the text (see HoloUI.Glass) keeps it readable.
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Backdrop";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(panel, false);
            quad.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            quad.transform.localScale = new Vector3(W * UnitsToMeters, H * UnitsToMeters, 1f);
            var quadRenderer = quad.GetComponent<MeshRenderer>();
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            backgroundMaterial = new Material(hologramMaterial);
            backgroundMaterial.SetVector("_PanelSize", new Vector4(W * UnitsToMeters, H * UnitsToMeters));
            backgroundMaterial.SetFloat("_FillAlpha", 0.06f);
            backgroundMaterial.SetFloat("_ScanStrength", 0.15f);
            quadRenderer.sharedMaterial = backgroundMaterial;
            quadRenderer.sortingOrder = 9; // above the glass (8), below the panel's content (10)

            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(panel, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvas.sortingOrder = 10;
            canvasGo.AddComponent<GraphicRaycaster>();
            canvasGroup = canvasGo.AddComponent<CanvasGroup>();
            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.sizeDelta = new Vector2(W, H);
            canvasRect.localScale = Vector3.one * UnitsToMeters;

            var content = HoloUI.Rect("Content", canvasRect, 0, 0, W, H);
            contentGroup = content.gameObject.AddComponent<CanvasGroup>();
            ui.Glass(canvasRect, W, H, 8).transform.SetAsFirstSibling();
            BuildContent(content);

            var pickerRect = HoloUI.Rect("Date Picker", canvasRect, W + 16f, 0, HoloDatePicker.Width, HoloDatePicker.Height);
            picker = pickerRect.gameObject.AddComponent<HoloDatePicker>();
            picker.Build(ui);

            BuildSyncDialog(canvasRect);
        }

        void BuildContent(Transform root)
        {
            float x = Pad, w = W - Pad * 2f;
            var label = HoloUI.LabelColor;

            ui.Label("Header", root, x, 18, 200, 24, "EDIT TASK", 18, TextAlignmentOptions.Left, HoloUI.TextColor);
            sourceBadge = ui.Label("Source", root, x + 200, 20, w - 200, 20, "", 12, TextAlignmentOptions.Right, label);

            ui.Label("Title Label", root, x, 52, w, 16, "TITLE", 12, TextAlignmentOptions.Left, label);
            titleField = ui.InputField("Title", root, x, 70, w, 44, "What needs doing?", 22, multiLine: false);
            titleField.onValueChanged.AddListener(v => { if (work != null) work.title = v; });

            ui.Label("Notes Label", root, x, 124, w, 16, "NOTES", 12, TextAlignmentOptions.Left, label);
            notesField = ui.InputField("Notes", root, x, 142, w, 84, "Details, links, sub-steps...", 17, multiLine: true);
            notesField.onValueChanged.AddListener(v => { if (work != null) work.notes = v; });

            ui.Label("Due Label", root, x, 240, w, 16, "DUE DATE", 12, TextAlignmentOptions.Left, label);
            dueButton = ui.Button("Due", root, x, 258, 300, 38, "", 18, PickDueDate);
            dueLabel = dueButton.GetComponentInChildren<TextMeshProUGUI>();
            undeterminedToggle = ui.Toggle("Undetermined", root, x + 316, 258, w - 316, 38, "Undetermined", 16,
                on => SetDueDate(on ? (DateTime?)null : DateTime.Today.AddDays(1)));

            ui.Label("Reminder Label", root, x, 310, w, 16, "REMINDER", 12, TextAlignmentOptions.Left, label);
            reminderHint = ui.Label("Reminder Hint", root, x, 334, w, 24, "Set a due date to add a reminder.", 15,
                TextAlignmentOptions.Left, new Color(0.5f, 0.75f, 0.9f, 0.45f));
            reminderRow = HoloUI.Rect("Reminder Row", root, x, 328, w, 38).gameObject;
            reminderDateButton = ui.Button("Reminder Date", reminderRow.transform, 0, 0, 190, 38, "", 16, PickReminderDate);
            reminderDateLabel = reminderDateButton.GetComponentInChildren<TextMeshProUGUI>();
            reminderTimeField = ui.InputField("Reminder Time", reminderRow.transform, 200, 0, 130, 38, "9:00 AM", 16, multiLine: false);
            reminderTimeField.onEndEdit.AddListener(_ => CommitReminderTime());
            clearReminderButton = ui.Button("Clear Reminder", reminderRow.transform, 340, 0, w - 340, 38, "No reminder", 15,
                () => SetReminder(null));

            ui.Label("Priority Label", root, x, 380, w, 16, "PRIORITY", 12, TextAlignmentOptions.Left, label);
            string[] priorityNames = { "Low", "Medium", "High", "Let Cypher decide" };
            float[] priorityWidths = { 96f, 112f, 96f, w - 96f - 112f - 96f - 24f };
            prioritySegments = new HoloSegment[4];
            float px = x;
            for (int i = 0; i < 4; i++)
            {
                var p = PriorityForSegment(i);
                prioritySegments[i] = ui.Segment(priorityNames[i], root, px, 398, priorityWidths[i], 36, priorityNames[i], 15, () => SetPriority(p));
                px += priorityWidths[i] + 8f;
            }

            ui.Label("Difficulty Label", root, x, 448, 300, 16, "DIFFICULTY", 12, TextAlignmentOptions.Left, label);
            difficultySegments = new HoloSegment[5];
            for (int i = 0; i < 5; i++)
            {
                int level = i + 1;
                difficultySegments[i] = ui.Segment($"Difficulty {level}", root, x + i * 54f, 466, 48, 36,
                    level.ToString(CultureInfo.InvariantCulture), 16, () => SetDifficulty(level));
            }
            doneToggle = ui.Toggle("Done", root, x + 290, 466, w - 290, 36, "Mark as done", 16, on => { if (work != null) work.done = on; });

            deleteButton = ui.Button("Delete", root, x, 532, 120, 44, "Delete", 17, Delete);
            deleteLabel = deleteButton.GetComponentInChildren<TextMeshProUGUI>();
            ui.Button("Cancel", root, W - Pad - 240, 532, 112, 44, "Cancel", 17, Cancel);
            ui.Button("Save", root, W - Pad - 120, 532, 120, 44, "Save", 17, Save);
        }

        void BuildSyncDialog(Transform canvasRoot)
        {
            const float dw = 460f, dh = 210f;
            var rect = HoloUI.Rect("Sync Dialog", canvasRoot, (W - dw) / 2f, (H - dh) / 2f, dw, dh);
            dialog = rect.gameObject;
            ui.Frame("Frame", rect, 0, 0, dw, dh, new Color(0.4f, 0.9f, 1f, 1f));
            ui.Glass(rect, dw, dh, 8);
            dialogText = ui.Label("Text", rect, 24, 22, dw - 48, 70, "", 18, TextAlignmentOptions.Center, HoloUI.TextColor);
            dialogText.textWrappingMode = TextWrappingModes.Normal;
            ui.Button("Update Original", rect, 24, 110, 200, 44, "Update original", 16, () => { HideSyncDialog(); Apply(syncOriginal: true); });
            ui.Button("Keep Local", rect, dw - 224, 110, 200, 44, "Keep local only", 16, () => { HideSyncDialog(); Apply(syncOriginal: false); });
            ui.Button("Back", rect, (dw - 100) / 2f, 166, 100, 30, "Back", 14, HideSyncDialog);
            dialog.SetActive(false);
        }
    }
}
