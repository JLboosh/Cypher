using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Cypher
{
    /// <summary>
    /// Holographic import window: choose Google Calendar / Google Tasks / Notion, pick a calendar,
    /// list or database, tick the items you want, Import. Items already imported are marked
    /// "update" (importing them again refreshes them instead of duplicating).
    /// Opens from the IMPORT button on the desk, or "Hey Cypher, import from Notion". Esc closes.
    /// </summary>
    public class ImportPanel : MonoBehaviour
    {
        public static ImportPanel Instance { get; private set; }

        const float W = 660f, H = 600f, Pad = 24f, UnitsToMeters = 0.001f;
        const int Rows = 8;

        HoloUI ui;
        IntegrationHub hub;
        GameObject window;

        HoloSegment[] tabs;
        TextMeshProUGUI status, containerLabel, pageLabel, importLabel, connectLabel;
        Button containerButton, connectButton, importButton;
        readonly HoloToggle[] rowToggles = new HoloToggle[Rows];
        readonly TextMeshProUGUI[] rowTitles = new TextMeshProUGUI[Rows];
        readonly TextMeshProUGUI[] rowDues = new TextMeshProUGUI[Rows];

        IntegrationHub.Kind kind;
        List<ImportContainer> containers = new List<ImportContainer>();
        int containerIndex;
        List<ImportCandidate> items = new List<ImportCandidate>();
        readonly HashSet<string> selected = new HashSet<string>();
        int page;
        int loadVersion;

        public bool IsOpen => window != null && window.activeSelf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake() => Instance = this;

        void Start()
        {
            hub = IntegrationHub.Instance;
            ui = EditPanelController.Instance != null ? EditPanelController.Instance.UI : null;
            if (hub == null || ui == null) return;
            BuildDeskButton();
            BuildWindow();
            window.SetActive(false);
        }

        public void Open(IntegrationHub.Kind source)
        {
            if (window == null || IsOpen) return;
            var cam = Camera.main.transform;
            window.transform.SetPositionAndRotation(cam.position + cam.forward * 0.78f - cam.up * 0.02f, Quaternion.LookRotation(cam.forward, cam.up));
            window.SetActive(true);
            InputLock.Acquire(this);
            CypherAudio.Play(Sfx.Open, window.transform.position);
            SelectTab(source);
        }

        public void Close()
        {
            if (!IsOpen) return;
            window.SetActive(false);
            InputLock.Release(this);
            CypherAudio.Play(Sfx.Close, window.transform.position);
        }

        void Update()
        {
            if (IsOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Close();
        }

        void OnDestroy() => InputLock.Release(this);

        // ------------------------------------------------------------------ loading

        void SelectTab(IntegrationHub.Kind k)
        {
            kind = k;
            for (int i = 0; i < tabs.Length; i++) tabs[i].SetSelected(i == (int)k);
            bool google = k != IntegrationHub.Kind.Notion;
            connectButton.gameObject.SetActive(google);
            connectLabel.text = hub.Google.IsConnected ? "Disconnect Google" : "Connect Google";
            LoadContainers();
        }

        async void LoadContainers()
        {
            int version = ++loadVersion;
            containers.Clear();
            items.Clear();
            selected.Clear();
            containerLabel.text = "-";
            RefreshRows();

            string problem = hub.Problem(kind);
            if (problem != null)
            {
                status.text = problem;
                return;
            }
            status.text = "Loading...";
            var list = await hub.ListContainers(kind);
            if (version != loadVersion) return; // switched tabs meanwhile
            if (list == null) { status.text = "Couldn't reach the service. Check the Console for details."; return; }
            if (list.Count == 0)
            {
                status.text = kind == IntegrationHub.Kind.Notion
                    ? "No databases shared with your integration. In Notion: open the database > ... > Connections > add it."
                    : "Nothing found in this account.";
                return;
            }
            containers = list;
            containerIndex = 0;
            LoadItems();
        }

        async void LoadItems()
        {
            int version = ++loadVersion;
            var container = containers[containerIndex];
            containerLabel.text = $"{container.Name}   ({containerIndex + 1}/{containers.Count})";
            status.text = "Loading items...";
            items.Clear();
            selected.Clear();
            page = 0;
            RefreshRows();

            var list = await hub.ListItems(kind, container.Id);
            if (version != loadVersion) return;
            if (list == null) { status.text = "Couldn't load items. Check the Console for details."; return; }
            items = list;
            status.text = items.Count == 0 ? "Nothing to import here." : $"{items.Count} items. Tick the ones you want, then Import.";
            RefreshRows();
        }

        void RefreshRows()
        {
            int pages = Mathf.Max(1, Mathf.CeilToInt(items.Count / (float)Rows));
            page = Mathf.Clamp(page, 0, pages - 1);
            pageLabel.text = $"{page + 1} / {pages}";

            for (int r = 0; r < Rows; r++)
            {
                int i = page * Rows + r;
                bool has = i < items.Count;
                rowToggles[r].Button.gameObject.SetActive(has);
                if (!has) continue;
                var c = items[i];
                rowToggles[r].Set(selected.Contains(c.ExternalId), notify: false);
                rowTitles[r].text = (c.Existing != null ? "[update] " : "") + c.Title + (c.Done ? "  (done)" : "");
                rowDues[r].text = c.Due.HasValue ? NaturalDate.Describe(c.Due.Value, true, DateTime.Now) : "no date";
            }
            importLabel.text = selected.Count > 0 ? $"Import {selected.Count}" : "Import";
            importButton.interactable = selected.Count > 0;
        }

        void Toggle(int row, bool on)
        {
            int i = page * Rows + row;
            if (i >= items.Count) return;
            if (on) selected.Add(items[i].ExternalId); else selected.Remove(items[i].ExternalId);
            RefreshRows();
        }

        void SelectAll()
        {
            bool all = items.Count > 0 && items.All(c => selected.Contains(c.ExternalId));
            selected.Clear();
            if (!all) foreach (var c in items) selected.Add(c.ExternalId);
            RefreshRows();
        }

        void DoImport()
        {
            var chosen = items.Where(c => selected.Contains(c.ExternalId)).ToList();
            if (chosen.Count == 0) return;
            var (added, updated) = hub.Import(chosen);
            selected.Clear();
            status.text = $"Imported {added} new" + (updated > 0 ? $", updated {updated}." : ".");
            CypherAudio.Play(Sfx.Sync, window.transform.position);
            RefreshRows();
        }

        async void ConnectOrDisconnect()
        {
            if (hub.Google.IsConnected)
            {
                hub.Google.Disconnect();
                connectLabel.text = "Connect Google";
                LoadContainers();
                return;
            }
            if (!hub.Google.IsConfigured)
            {
                status.text = hub.Problem(kind);
                return;
            }
            status.text = "Approve access in your browser... (waiting up to 3 minutes)";
            string error = await hub.Google.Connect();
            connectLabel.text = hub.Google.IsConnected ? "Disconnect Google" : "Connect Google";
            if (error != null) status.text = error;
            else LoadContainers();
        }

        void NextContainer()
        {
            if (containers.Count == 0) return;
            containerIndex = (containerIndex + 1) % containers.Count;
            LoadItems();
        }

        // ------------------------------------------------------------------ building

        void BuildWindow()
        {
            window = new GameObject("Import Window");
            var rect = MakeCanvas(window.transform, W, H, 30);
            ui.Glass(rect, W, H, 28);
            ui.Frame("Frame", rect, 0, 0, W, H, new Color(0.4f, 0.9f, 1f, 1f));

            float x = Pad, w = W - Pad * 2f;
            ui.Label("Header", rect, x, 16, 300, 26, "IMPORT TASKS", 19, TextAlignmentOptions.Left, HoloUI.TextColor);
            ui.Button("Close", rect, W - Pad - 40, 14, 40, 30, "X", 15, Close);

            string[] names = { "Google Calendar", "Google Tasks", "Notion" };
            tabs = new HoloSegment[3];
            float tw = (w - 16f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var k = (IntegrationHub.Kind)i;
                tabs[i] = ui.Segment(names[i], rect, x + i * (tw + 8f), 56, tw, 36, names[i], 15, () => SelectTab(k));
            }

            status = ui.Label("Status", rect, x, 100, w, 40, "", 14, TextAlignmentOptions.Left, HoloUI.LabelColor);
            status.textWrappingMode = TextWrappingModes.Normal;

            ui.Label("From", rect, x, 146, 60, 32, "FROM", 12, TextAlignmentOptions.Left, HoloUI.LabelColor);
            containerButton = ui.Button("Container", rect, x + 60, 146, w - 60, 32, "-", 14, NextContainer);
            containerLabel = containerButton.GetComponentInChildren<TextMeshProUGUI>();

            for (int r = 0; r < Rows; r++)
            {
                int row = r;
                float y = 190 + r * 40;
                rowToggles[r] = ui.Toggle($"Row {r}", rect, x, y, w, 36, "", 14, on => Toggle(row, on));
                rowTitles[r] = rowToggles[r].Button.GetComponentInChildren<TextMeshProUGUI>();
                rowTitles[r].rectTransform.offsetMax = new Vector2(-150f, rowTitles[r].rectTransform.offsetMax.y);
                rowDues[r] = ui.Label("Due", rowToggles[r].Button.transform, w - 150, 0, 140, 36, "", 13, TextAlignmentOptions.Right, HoloUI.LabelColor);
            }

            float by = H - Pad - 40;
            ui.Button("Prev", rect, x, by, 44, 40, "<", 16, () => { page--; RefreshRows(); });
            pageLabel = ui.Label("Page", rect, x + 48, by, 70, 40, "1 / 1", 14, TextAlignmentOptions.Center, HoloUI.LabelColor);
            ui.Button("Next", rect, x + 122, by, 44, 40, ">", 16, () => { page++; RefreshRows(); });
            ui.Button("All", rect, x + 176, by, 90, 40, "All / none", 14, SelectAll);
            connectButton = ui.Button("Connect", rect, x + 276, by, 170, 40, "Connect Google", 14, ConnectOrDisconnect);
            connectLabel = connectButton.GetComponentInChildren<TextMeshProUGUI>();
            importButton = ui.Button("Import", rect, W - Pad - 120, by, 120, 40, "Import", 16, DoImport);
            importLabel = importButton.GetComponentInChildren<TextMeshProUGUI>();
        }

        /// <summary>The IMPORT console pad on the left side of the desk.</summary>
        void BuildDeskButton()
        {
            float angle = -58f * Mathf.Deg2Rad;
            var position = new Vector3(Mathf.Sin(angle) * 0.76f, 0.83f, Mathf.Cos(angle) * 0.76f);
            DeskImportButton.Create(ui, position, () => Open(kind));
        }

        static RectTransform MakeCanvas(Transform parent, float w, float h, int order)
        {
            var go = new GameObject("Canvas", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvas.sortingOrder = order;
            go.AddComponent<GraphicRaycaster>();
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(w, h);
            rect.localScale = Vector3.one * UnitsToMeters;
            return rect;
        }
    }
}
