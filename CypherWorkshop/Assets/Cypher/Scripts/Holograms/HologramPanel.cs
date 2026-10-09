using System;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace Cypher
{
    /// <summary>
    /// One floating task hologram. Shows the task's text, glides to its target position,
    /// always turns to face the player, brightens/grows while hovered or dragged, flickers,
    /// sheds drifting particles, and materializes / dematerializes.
    /// Lives on the root of the Hologram prefab (built by Cypher > Build Workshop Scene).
    /// </summary>
    public class HologramPanel : MonoBehaviour
    {
        [Header("References (filled in by the scene builder)")]
        [SerializeField] Transform visual;
        [SerializeField] Renderer panelRenderer;
        [SerializeField] TMP_Text titleText;
        [SerializeField] TMP_Text dueText;
        [SerializeField] TMP_Text priorityText;
        [SerializeField] TMP_Text sourceText;
        [SerializeField] ParticleSystem motes;

        [Header("Hover / drag feel")]
        [SerializeField] float hoverScale = 1.08f;
        [SerializeField] float hoverBrightness = 1.7f;
        [SerializeField] float idleBrightness = 1f;
        [SerializeField] float doneBrightness = 0.45f;
        [Tooltip("How quickly brightness and hover scale react. Higher = snappier.")]
        [SerializeField] float responsiveness = 14f;

        [Header("Movement")]
        [Tooltip("Seconds to glide to a new layout slot.")]
        [SerializeField] float moveSmoothTime = 0.45f;
        [Tooltip("How tightly the panel follows the mouse while dragged. Higher = tighter.")]
        [SerializeField] float dragFollowSharpness = 25f;
        [SerializeField] float turnSharpness = 10f;

        [Header("Materialize")]
        [SerializeField] float materializeSeconds = 1.1f;
        [SerializeField] float dematerializeSeconds = 0.6f;
        [Tooltip("Sparks thrown off the edges when the hologram builds or un-builds.")]
        [SerializeField] int burstParticles = 28;
        [Tooltip("Brightness spike at the end of the build (the 'glow' stage).")]
        [SerializeField] float buildFlash = 1.6f;

        [Header("Focus mode")]
        [Tooltip("Brightness of the other holograms while an edit panel, import window or question is open.")]
        [Range(0f, 1f)] [SerializeField] float focusDim = 0.18f;
        [SerializeField] float focusFadeSpeed = 6f;

        [Header("Text")]
        [Tooltip("Longer titles are cut and end with an ellipsis.")]
        [SerializeField] int maxTitleLength = 60;

        [Header("Flicker")]
        [Tooltip("Constant gentle shimmer. 0 = steady.")]
        [Range(0f, 0.5f)] [SerializeField] float flickerAmount = 0.08f;
        [SerializeField] float flickerSpeed = 3f;
        [Tooltip("Seconds between brief glitches (random within range).")]
        [SerializeField] Vector2 glitchInterval = new Vector2(5f, 16f);
        [SerializeField] Vector2 glitchDuration = new Vector2(0.05f, 0.14f);

        static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        static readonly int MaterializeId = Shader.PropertyToID("_Materialize");
        static readonly int GlitchId = Shader.PropertyToID("_Glitch");

        static readonly Color HighColor = new Color(1f, 0.62f, 0.25f);
        static readonly Color MediumColor = new Color(0.55f, 0.95f, 1f);
        static readonly Color LowColor = new Color(0.55f, 0.7f, 0.8f);
        static readonly Color AutoColor = new Color(0.7f, 0.85f, 1f);

        public TaskItem Task { get; private set; }
        public bool IsHovered { get; private set; }
        public bool IsDragging { get; private set; }

        /// <summary>Only fully built, visible holograms react to the mouse.</summary>
        public bool IsInteractable => materialize >= 0.95f && materializeTarget > 0.5f && !hiddenForEdit;

        /// <summary>Panel width/height in meters (before scaling).</summary>
        public Vector2 PanelSize
        {
            get
            {
                var box = GetComponent<BoxCollider>();
                return box != null ? (Vector2)box.size : new Vector2(0.42f, 0.26f);
            }
        }

        Material material;
        Transform viewer;
        TMP_Text[] texts;
        Vector3 targetPosition;
        Vector3 velocity;
        float targetScale = 1f;
        float brightness;
        float flash;

        float materialize;
        float materializeTarget;
        bool destroyWhenHidden;
        bool flashedThisBuild;
        bool hiddenForEdit;
        float pulseUntil;
        float focus = 1f; // 1 = normal, focusDim = faded back behind an open panel

        float seed;
        float nextGlitchTime;
        float glitchEndTime;
        float appliedTextAlpha = -1f;

        void Awake()
        {
            // .material makes a per-panel copy so each hologram can glow independently.
            material = panelRenderer.material;
            texts = new[] { titleText, dueText, priorityText, sourceText };
            // TMP's built-in Ellipsis mode fails its glyph lookup for bold 3D text and logs a warning,
            // so long titles are shortened in Refresh() instead.
            foreach (var t in texts) if (t != null) t.overflowMode = TextOverflowModes.Truncate;
            targetPosition = transform.position;
            brightness = idleBrightness;
            seed = UnityEngine.Random.value * 100f;
            nextGlitchTime = Time.time + UnityEngine.Random.Range(glitchInterval.x, glitchInterval.y);

            // Holograms start invisible; TaskManager calls Materialize() to bring them in.
            material.SetFloat(MaterializeId, 0f);
            SetTextAlpha(0f);
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
        }

        public void Bind(TaskItem task)
        {
            Task = task;
            Refresh();
        }

        /// <summary>Re-draws the text from the bound TaskItem. Call after editing the task.</summary>
        public void Refresh()
        {
            if (Task == null) return;
            name = $"Hologram - {Task.title}";

            titleText.text = string.IsNullOrWhiteSpace(Task.title) ? "(untitled)" : Shorten(Task.title.Trim(), maxTitleLength);
            titleText.fontStyle = Task.done ? FontStyles.Bold | FontStyles.Strikethrough : FontStyles.Bold;

            dueText.text = FormatDue(Task);
            dueText.color = IsOverdue(Task) ? HighColor : Color.white;

            if (Task.priority == PriorityOverride.Auto)
            {
                // "Let Cypher decide": show what Cypher decided.
                var auto = TaskRanker.AutoPriority(Task, DateTime.Now);
                priorityText.text = "AUTO · " + PriorityLabel(auto);
                priorityText.color = PriorityColor(auto);
            }
            else
            {
                priorityText.text = "PRI · " + PriorityLabel(Task.priority);
                priorityText.color = PriorityColor(Task.priority);
            }

            sourceText.text = Task.source switch
            {
                TaskSource.GoogleCalendar => "GCAL",
                TaskSource.Notion => "NOTION",
                TaskSource.GoogleTasks => "GTASKS",
                _ => "",
            };

            appliedTextAlpha = -1f; // setting colors reset the alpha; re-apply next frame
        }

        public void MoveTo(Vector3 position, float scale, bool instant)
        {
            targetPosition = position;
            targetScale = scale;
            if (!instant) return;

            transform.position = position;
            transform.localScale = Vector3.one * scale;
            velocity = Vector3.zero;
            FaceViewer(1f);
        }

        /// <summary>Wireframe -> solid -> glow build-up, with sparks and sound.</summary>
        public void Materialize(bool withSound = true)
        {
            materializeTarget = 1f;
            destroyWhenHidden = false;
            flashedThisBuild = false;
            EmitBurst();
            if (withSound) CypherAudio.Play(Sfx.Materialize, transform.position);
        }

        /// <summary>The build-up in reverse. Optionally destroys the hologram when it's gone.</summary>
        public void Dematerialize(bool destroyWhenDone, bool withSound = true)
        {
            materializeTarget = 0f;
            destroyWhenHidden = destroyWhenDone;
            IsHovered = false;
            EmitBurst();
            if (withSound) CypherAudio.Play(Sfx.Dematerialize, transform.position);
        }

        /// <summary>Hides the hologram while the edit panel (which "is" this hologram) is open.</summary>
        public void SetHiddenForEdit(bool hidden)
        {
            hiddenForEdit = hidden;
            visual.gameObject.SetActive(!hidden);
            IsHovered = false;
            if (hidden && motes != null) motes.Clear();
        }

        /// <summary>The quick "data sync" re-scan after saving: the fill sweeps up again and flashes.</summary>
        public void PlaySyncShimmer()
        {
            materialize = Mathf.Min(materialize, 0.55f);
            materializeTarget = 1f;
            flashedThisBuild = false;
            CypherAudio.Play(Sfx.Sync, transform.position);
        }

        public void SetHovered(bool hovered) => IsHovered = hovered;

        public void BeginDrag()
        {
            IsDragging = true;
            velocity = Vector3.zero;
        }

        public void DragTo(Vector3 position) => targetPosition = position;

        public void EndDrag()
        {
            IsDragging = false;
            targetPosition = transform.position;
            velocity = Vector3.zero;
        }

        /// <summary>Repeated attention pulses (brightness + size), e.g. when a reminder fires.</summary>
        public void Pulse(float seconds = 8f) => pulseUntil = Time.time + seconds;

        /// <summary>A quick brightness spike, e.g. to acknowledge a click.</summary>
        public void Flash(float amount = 1.5f) => flash = Mathf.Max(flash, amount);

        void LateUpdate()
        {
            float dt = Time.deltaTime;

            UpdateMaterialize(dt);
            if (destroyWhenHidden && materialize <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            if (IsDragging)
                transform.position = Vector3.Lerp(transform.position, targetPosition, 1f - Mathf.Exp(-dragFollowSharpness * dt));
            else
                transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref velocity, moveSmoothTime);

            float scale = Mathf.Lerp(transform.localScale.x, targetScale, 1f - Mathf.Exp(-6f * dt));
            transform.localScale = Vector3.one * scale;

            FaceViewer(1f - Mathf.Exp(-turnSharpness * dt));

            bool lit = IsHovered || IsDragging;
            float k = 1f - Mathf.Exp(-responsiveness * dt);
            float pulse = Time.time < pulseUntil ? Mathf.Pow(0.5f + 0.5f * Mathf.Sin(Time.time * 7f), 2f) : 0f;
            if (pulse > 0f) flash = Mathf.Max(flash, pulse * 1.4f);
            visual.localScale = Vector3.Lerp(visual.localScale, Vector3.one * ((lit ? hoverScale : 1f) + pulse * 0.06f), k);

            float baseBrightness = Task != null && Task.done ? doneBrightness : idleBrightness;
            brightness = Mathf.Lerp(brightness, lit ? hoverBrightness : baseBrightness, k);
            flash = Mathf.MoveTowards(flash, 0f, dt * 3f);

            // Focus mode: fade back while a panel/prompt owns the input, so nothing shines through it.
            focus = Mathf.MoveTowards(focus, InputLock.IsLocked ? focusDim : 1f, focusFadeSpeed * dt);

            float flicker = Flicker(out bool glitching);
            material.SetFloat(BrightnessId, (brightness * flicker + flash) * focus);
            material.SetFloat(GlitchId, glitching ? 1f : 0f);

            // Text fades in during the last stage of the build and dips during glitches.
            float textAlpha = Mathf.Clamp01((materialize - 0.6f) / 0.35f) * (glitching ? 0.55f : 1f) * focus;
            if (Mathf.Abs(textAlpha - appliedTextAlpha) > 0.01f) SetTextAlpha(textAlpha);
        }

        void UpdateMaterialize(float dt)
        {
            float seconds = materializeTarget > materialize ? materializeSeconds : dematerializeSeconds;
            materialize = Mathf.MoveTowards(materialize, materializeTarget, dt / Mathf.Max(0.01f, seconds));
            material.SetFloat(MaterializeId, materialize);

            if (!flashedThisBuild && materializeTarget > 0.5f && materialize >= 0.8f)
            {
                flashedThisBuild = true;
                Flash(buildFlash);
            }

            if (motes != null)
            {
                var emission = motes.emission;
                emission.enabled = materialize > 0.6f && materializeTarget > 0.5f && !hiddenForEdit && focus > 0.9f;
            }
        }

        /// <summary>Returns a brightness multiplier around 1, with rare short glitches.</summary>
        float Flicker(out bool glitching)
        {
            float t = Time.time;
            float f = 1f - flickerAmount * Mathf.PerlinNoise(seed, t * flickerSpeed);

            if (t >= nextGlitchTime)
            {
                glitchEndTime = t + UnityEngine.Random.Range(glitchDuration.x, glitchDuration.y);
                nextGlitchTime = glitchEndTime + UnityEngine.Random.Range(glitchInterval.x, glitchInterval.y);
            }

            glitching = t < glitchEndTime && materialize >= 1f;
            if (glitching) f *= 0.55f + 0.35f * Mathf.PerlinNoise(t * 40f, seed);
            return f;
        }

        void SetTextAlpha(float alpha)
        {
            // Rebuilt lazily: Unity clears non-serialized fields if scripts recompile during Play.
            texts ??= new[] { titleText, dueText, priorityText, sourceText };
            foreach (var t in texts) if (t != null) t.alpha = alpha;
            appliedTextAlpha = alpha;
        }

        void EmitBurst()
        {
            if (motes == null || burstParticles <= 0) return;

            Vector2 size = PanelSize;
            var p = new ParticleSystem.EmitParams();
            for (int i = 0; i < burstParticles; i++)
            {
                Vector2 edge = RandomPointOnRectEdge(size);
                p.position = edge;
                p.velocity = new Vector3(edge.x, edge.y, -0.05f).normalized * UnityEngine.Random.Range(0.02f, 0.07f);
                p.startLifetime = UnityEngine.Random.Range(0.5f, 1.1f);
                p.startSize = UnityEngine.Random.Range(0.004f, 0.009f);
                motes.Emit(p, 1);
            }
        }

        static Vector2 RandomPointOnRectEdge(Vector2 size)
        {
            float hx = size.x / 2f, hy = size.y / 2f;
            float d = UnityEngine.Random.value * 2f * (size.x + size.y);
            if (d < size.x) return new Vector2(-hx + d, -hy);
            d -= size.x;
            if (d < size.y) return new Vector2(hx, -hy + d);
            d -= size.y;
            if (d < size.x) return new Vector2(hx - d, hy);
            d -= size.x;
            return new Vector2(-hx, hy - d);
        }

        void FaceViewer(float t)
        {
            if (viewer == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                viewer = cam.transform;
            }

            // Point the panel's +Z away from the viewer: quads and TextMeshPro are read from -Z.
            Vector3 away = transform.position - viewer.position;
            if (away.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(away, Vector3.up), t);
        }

        static string Shorten(string text, int max) =>
            text.Length <= max ? text : text.Substring(0, Mathf.Max(1, max - 1)).TrimEnd() + "\u2026";

        static bool IsOverdue(TaskItem task) =>
            !task.done && task.DueDate.HasValue && task.DueDate.Value.Date < DateTime.Now.Date;

        static string FormatDue(TaskItem task)
        {
            var due = task.DueDate;
            if (!due.HasValue) return "NO DEADLINE";

            var culture = CultureInfo.CurrentCulture;
            int days = (due.Value.Date - DateTime.Now.Date).Days;
            if (days < 0) return "OVERDUE · " + due.Value.ToString("MMM d", culture).ToUpperInvariant();
            if (days == 0) return "DUE TODAY";
            if (days == 1) return "DUE TOMORROW";
            if (days < 7) return "DUE " + due.Value.ToString("dddd", culture).ToUpperInvariant();
            return "DUE " + due.Value.ToString("MMM d", culture).ToUpperInvariant();
        }

        static string PriorityLabel(PriorityOverride p) => p switch
        {
            PriorityOverride.High => "HIGH",
            PriorityOverride.Medium => "MED",
            PriorityOverride.Low => "LOW",
            _ => "AUTO",
        };

        static Color PriorityColor(PriorityOverride p) => p switch
        {
            PriorityOverride.High => HighColor,
            PriorityOverride.Medium => MediumColor,
            PriorityOverride.Low => LowColor,
            _ => AutoColor,
        };
    }
}
