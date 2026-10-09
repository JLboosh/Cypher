using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Cypher
{
    public enum RingState { Idle, Listening, Thinking, Speaking, Paused }

    /// <summary>
    /// Cypher's HUD emblem plus captions, floating low in front of the desk and following the
    /// camera as you turn. Everything is one dynamic mesh of glowing line segments (one draw
    /// call), rebuilt each frame from these layers, inside out:
    ///   core glow · waveform ring · 64 spectrum bars · tick dial · segmented arcs ·
    ///   scanner comet · corner brackets · shockwave (on wake)
    /// States blend smoothly: Idle (dim, slow) · Listening (cyan, reacts to you) ·
    /// Thinking (violet, fast spin, sweeping bars) · Speaking (white-cyan, reacts to Cypher) · Paused.
    /// </summary>
    public class VoiceRing : MonoBehaviour
    {
        [SerializeField] Vector3 anchor = new Vector3(0f, 0.95f, 0.6f);
        [Tooltip("Overall size (meters). Every layer scales with it.")]
        [SerializeField] float size = 0.05f;
        [SerializeField] float followSharpness = 3f;
        [SerializeField] float captionSeconds = 9f;

        const int Bars = 64;
        const int Ticks = 72;
        const int CirclePoints = 96;

        struct Look
        {
            public Color main, accent;
            public float intensity, spin, barGain, wobble;
        }

        static readonly Look IdleLook = new Look { main = new Color(0.25f, 0.65f, 1f), accent = new Color(0.4f, 0.8f, 1f), intensity = 0.55f, spin = 0.15f, barGain = 0.15f, wobble = 0.15f };
        static readonly Look ListenLook = new Look { main = new Color(0.3f, 0.95f, 1f), accent = new Color(0.75f, 1f, 1f), intensity = 1.35f, spin = 0.5f, barGain = 1f, wobble = 1f };
        static readonly Look ThinkLook = new Look { main = new Color(0.65f, 0.4f, 1f), accent = new Color(0.4f, 0.85f, 1f), intensity = 1.2f, spin = 3.2f, barGain = 0.6f, wobble = 0.2f };
        static readonly Look SpeakLook = new Look { main = new Color(0.7f, 0.95f, 1f), accent = new Color(1f, 0.85f, 0.55f), intensity = 1.45f, spin = 0.8f, barGain = 1.2f, wobble = 1.1f };
        static readonly Look PausedLook = new Look { main = new Color(0.2f, 0.4f, 0.7f), accent = new Color(0.2f, 0.4f, 0.7f), intensity = 0.22f, spin = 0f, barGain = 0f, wobble = 0f };

        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int TintId = Shader.PropertyToID("_Tint");

        Mesh hudMesh;
        Material hudMat, coreMat, textMat, beamMat, baseGlowMat, baseRingMat;
        Transform core, projector, baseRing;
        TMP_Text status, heard, reply;
        CanvasGroup captionGroup;

        RingState state = RingState.Idle;
        Look look = IdleLook;
        Func<float> levelSource;
        float level, flash, yaw, captionUntil, spinAngle, shockTime = -1f;
        float replyStart, replySeconds;   // for paging long replies in step with the speech
        TMP_Text pageLabel;

        // Piper/say speak roughly this many words per second (used to pace caption pages).
        const float WordsPerSecond = 2.5f;
        readonly float[] barLevels = new float[Bars];

        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> tris = new List<int>();

        public void Build(Material lineMaterial, TMP_FontAsset font)
        {
            hudMat = new Material(lineMaterial != null ? lineMaterial : new Material(Shader.Find("Cypher/GlowLine")));
            hudMat.SetColor(TintId, Color.white);
            var hud = new GameObject("HUD");
            hud.transform.SetParent(transform, false);
            hudMesh = new Mesh { name = "Voice HUD (generated)" };
            hudMesh.MarkDynamic();
            hud.AddComponent<MeshFilter>().sharedMesh = hudMesh;
            var hudRenderer = hud.AddComponent<MeshRenderer>();
            hudRenderer.sharedMaterial = hudMat;
            hudRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            core = BuildCore();

            textMat = HoloUI.ReadableTextMaterial(font);
            var ui = EditPanelController.Instance != null ? EditPanelController.Instance.UI : null;
            if (ui != null)
            {
                BuildCaptionPanel(ui);
            }
            else
            {
                status = MakeText("Status", font, -size * 1.42f, 0.2f, new Color(0.55f, 0.85f, 1f, 0.9f));
                heard = MakeText("Heard", font, -size * 1.42f - 0.024f, 0.26f, new Color(0.8f, 0.92f, 1f, 1f));
                reply = MakeText("Reply", font, -size * 1.42f - 0.05f, 0.26f, new Color(0.55f, 1f, 0.95f, 1f));
            }

            BuildProjector(lineMaterial);
        }

        /// <summary>Captions on a frosted glass strip under the emblem (readable over anything).</summary>
        void BuildCaptionPanel(HoloUI ui)
        {
            const float w = 640f, h = 116f;
            var go = new GameObject("Captions", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            // Just under the emblem's brackets, and high enough to clear the desk top.
            go.transform.localPosition = new Vector3(0f, -size * 1.1f - 0.012f - h * 0.0005f, 0f);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 6;
            captionGroup = go.AddComponent<CanvasGroup>();
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(w, h);
            rect.localScale = Vector3.one * 0.001f;

            ui.Glass(rect, w, h, 5);
            ui.Frame("Frame", rect, 0, 0, w, h, new Color(0.35f, 0.8f, 1f, 0.35f));
            status = ui.Label("Status", rect, 16, 4, w - 32, 16, "", 11, TextAlignmentOptions.Center, HoloUI.LabelColor);

            // What you said: up to two wrapped lines.
            heard = ui.Label("Heard", rect, 16, 21, w - 32, 36, "", 14, TextAlignmentOptions.Center, new Color(0.85f, 0.94f, 1f, 1f));
            heard.fontStyle = FontStyles.Italic;
            heard.textWrappingMode = TextWrappingModes.Normal;
            heard.lineSpacing = -8f;

            // Cypher's reply: up to three wrapped lines; longer replies page along with the speech.
            reply = ui.Label("Reply", rect, 16, 58, w - 32, 54, "", 15, TextAlignmentOptions.Top, new Color(0.6f, 1f, 0.96f, 1f));
            reply.textWrappingMode = TextWrappingModes.Normal;
            reply.overflowMode = TextOverflowModes.Page;
            reply.lineSpacing = -8f;
            pageLabel = ui.Label("Page", rect, w - 60, 96, 52, 16, "", 10, TextAlignmentOptions.Right, HoloUI.LabelColor);
        }

        /// <summary>A projector on the desk under the emblem: glowing pad, turning ring, rising light beam.</summary>
        void BuildProjector(Material lineMaterial)
        {
            projector = new GameObject("Voice Projector").transform;

            float height = anchor.y - DeskTop;
            var beam = new GameObject("Beam");
            beam.transform.SetParent(projector, false);
            beam.AddComponent<MeshFilter>().sharedMesh = BuildCone(0.024f, size * 0.55f, height, 28);
            var beamRenderer = beam.AddComponent<MeshRenderer>();
            beamMat = new Material(Shader.Find("Cypher/HoloBeam"));
            beamRenderer.sharedMaterial = beamMat;
            beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var pad = new GameObject("Pad");
            pad.transform.SetParent(projector, false);
            pad.transform.localPosition = new Vector3(0f, 0.002f, 0f);
            pad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            pad.transform.localScale = new Vector3(0.05f, 0.05f, 1f);
            pad.AddComponent<MeshFilter>().sharedMesh = core.GetComponent<MeshFilter>().sharedMesh;
            var padRenderer = pad.AddComponent<MeshRenderer>();
            baseGlowMat = new Material(Shader.Find("Cypher/GlowParticle"));
            baseGlowMat.SetFloat("_Softness", 0.5f);
            padRenderer.sharedMaterial = baseGlowMat;
            padRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            baseRing = new GameObject("Pad Ring").transform;
            baseRing.SetParent(projector, false);
            baseRing.localPosition = new Vector3(0f, 0.003f, 0f);
            baseRing.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var lr = baseRing.gameObject.AddComponent<LineRenderer>();
            baseRingMat = new Material(lineMaterial != null ? lineMaterial : hudMat);
            lr.sharedMaterial = baseRingMat;
            lr.useWorldSpace = false;
            lr.loop = false;
            lr.alignment = LineAlignment.TransformZ;
            lr.widthMultiplier = 0.0025f;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            const int n = 40;
            lr.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)(n - 1) * Mathf.PI * 1.6f; // an open arc, so its turning is visible
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * 0.034f, Mathf.Sin(a) * 0.034f, 0f));
            }
        }

        static Mesh BuildCone(float bottomRadius, float topRadius, float height, int segments)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts.Add(dir * bottomRadius);
                verts.Add(dir * topRadius + Vector3.up * height);
                normals.Add(dir);
                normals.Add(dir);
                uvs.Add(new Vector2(i / (float)segments, 0f));
                uvs.Add(new Vector2(i / (float)segments, 1f));
                if (i == segments) continue;
                int k = i * 2;
                tris.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 });
            }
            var mesh = new Mesh { name = "Voice Beam" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            return mesh;
        }

        void OnDestroy()
        {
            if (hudMesh != null) Destroy(hudMesh);
            if (hudMat != null) Destroy(hudMat);
            if (coreMat != null) Destroy(coreMat);
            if (textMat != null) Destroy(textMat);
            if (beamMat != null) Destroy(beamMat);
            if (baseGlowMat != null) Destroy(baseGlowMat);
            if (baseRingMat != null) Destroy(baseRingMat);
            if (projector != null) Destroy(projector.gameObject);
        }

        public void SetState(RingState newState, Func<float> level)
        {
            state = newState;
            levelSource = level;
        }

        public void SetStatus(string text) => status.text = text;

        public void ShowHeard(string text)
        {
            heard.text = "\"" + text + "\"";
            SetReply("");
            captionUntil = Time.time + captionSeconds;
        }

        public void ShowReply(string text)
        {
            SetReply("CYPHER: " + text);
            // Keep it up for as long as it takes to say, plus a few seconds to finish reading.
            captionUntil = Time.time + Mathf.Max(captionSeconds, replySeconds + 4f);
        }

        void SetReply(string text)
        {
            reply.text = text;
            reply.pageToDisplay = 1;
            replyStart = Time.time;
            int words = string.IsNullOrEmpty(text) ? 0 : text.Split(' ').Length;
            replySeconds = words / WordsPerSecond;
            if (pageLabel != null) pageLabel.text = "";
        }

        /// <summary>Flips through reply pages at the pace Cypher is speaking them.</summary>
        void UpdateReplyPages()
        {
            if (reply == null || pageLabel == null || string.IsNullOrEmpty(reply.text)) return;
            int pages = reply.textInfo != null ? reply.textInfo.pageCount : 1;
            if (pages <= 1)
            {
                pageLabel.text = "";
                return;
            }
            float perPage = Mathf.Max(2.5f, replySeconds / pages);
            int page = Mathf.Clamp(1 + (int)((Time.time - replyStart) / perPage), 1, pages);
            if (reply.pageToDisplay != page) reply.pageToDisplay = page;
            pageLabel.text = $"{page}/{pages}";
        }

        /// <summary>Wake acknowledgement: brightness flash plus an expanding shockwave.</summary>
        public void Flash()
        {
            flash = 1f;
            shockTime = Time.time;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || hudMesh == null) return;
            float dt = Time.deltaTime, t = Time.time;

            // Follow the camera's yaw so the emblem stays in view, and face the camera.
            yaw = Mathf.LerpAngle(yaw, cam.transform.eulerAngles.y, 1f - Mathf.Exp(-followSharpness * dt));
            transform.position = Quaternion.Euler(0f, yaw, 0f) * anchor;
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up);

            look = Blend(look, Target(state), 1f - Mathf.Exp(-6f * dt));
            float target = levelSource != null ? Mathf.Clamp01(levelSource()) : 0f;
            level = Mathf.Lerp(level, target, 1f - Mathf.Exp(-16f * dt));
            flash = Mathf.MoveTowards(flash, 0f, dt * 1.8f);
            spinAngle += look.spin * dt;

            float breathe = 0.5f + 0.5f * Mathf.Sin(t * 1.2f);
            hudMat.SetFloat(IntensityId, look.intensity * (state == RingState.Idle ? 0.85f + 0.3f * breathe : 1f) + flash * 2.5f);

            verts.Clear(); colors.Clear(); uvs.Clear(); tris.Clear();
            float s = size;
            Color main = look.main, accent = look.accent;

            // 1. Waveform ring: deforms with the voice.
            float waveR = s * 0.36f * (1f + level * 0.12f + flash * 0.15f);
            Vector3 prev = default, first = default;
            for (int i = 0; i <= CirclePoints; i++)
            {
                float a = i % CirclePoints / (float)CirclePoints * Mathf.PI * 2f;
                float n = Mathf.PerlinNoise(Mathf.Cos(a) * 1.7f + t * 2.6f, Mathf.Sin(a) * 1.7f + t * 1.9f) - 0.5f;
                float r = waveR * (1f + n * look.wobble * (0.12f + level * 0.9f));
                var p = Polar(a, r);
                if (i == 0) first = p; else Segment(prev, p, s * 0.035f, main, main);
                prev = i == CirclePoints ? first : p;
            }

            // 2. Spectrum bars: 64 radial bars, mirrored left/right like an equalizer.
            float barInner = s * 0.44f;
            for (int i = 0; i < Bars; i++)
            {
                int mirror = i < Bars / 2 ? i : Bars - 1 - i;
                float n = Mathf.PerlinNoise(mirror * 0.37f, t * 3.1f);
                float sweep = state == RingState.Thinking ? Mathf.Pow(0.5f + 0.5f * Mathf.Cos(i / (float)Bars * Mathf.PI * 2f - t * 5f), 6f) : 0f;
                float targetLen = 0.04f + look.barGain * (level * 0.9f * n + 0.08f * n) + sweep * 0.35f;
                barLevels[i] = Mathf.Lerp(barLevels[i], targetLen, 1f - Mathf.Exp(-18f * dt));
                float a = (i + 0.5f) / Bars * Mathf.PI * 2f + Mathf.PI * 0.5f;
                float len = s * barLevels[i] * 0.55f;
                var c = Color.Lerp(main, accent, barLevels[i] * 1.8f);
                Segment(Polar(a, barInner), Polar(a, barInner + len), s * 0.022f, c, Fade(c, 0.35f));
            }

            // 3. Tick dial: rotating, every 6th tick longer.
            float tickR = s * 0.72f;
            for (int i = 0; i < Ticks; i++)
            {
                float a = i / (float)Ticks * Mathf.PI * 2f + spinAngle * 0.25f;
                bool major = i % 6 == 0;
                float len = s * (major ? 0.07f : 0.035f);
                Segment(Polar(a, tickR), Polar(a, tickR + len), s * (major ? 0.016f : 0.01f), Fade(main, major ? 0.9f : 0.45f), Fade(main, major ? 0.9f : 0.45f));
            }

            // 4. Segmented arcs, counter-rotating.
            float arcR = s * 0.84f;
            Arc(arcR, -spinAngle * 0.6f, 70f, s * 0.03f, Fade(accent, 0.95f), Fade(accent, 0.95f), 28);
            Arc(arcR, -spinAngle * 0.6f + 115f * Mathf.Deg2Rad, 40f, s * 0.03f, Fade(main, 0.8f), Fade(main, 0.8f), 16);
            Arc(arcR, -spinAngle * 0.6f + 190f * Mathf.Deg2Rad, 110f, s * 0.03f, Fade(main, 0.9f), Fade(main, 0.9f), 40);
            Arc(s * 0.9f, spinAngle * 0.9f, 25f, s * 0.018f, Fade(accent, 0.7f), Fade(accent, 0.7f), 10);
            Arc(s * 0.9f, spinAngle * 0.9f + Mathf.PI, 25f, s * 0.018f, Fade(accent, 0.7f), Fade(accent, 0.7f), 10);

            // 5. Outer hairline with an orbiting scanner comet.
            float outerR = s * 0.97f;
            Arc(outerR, 0f, 360f, s * 0.008f, Fade(main, 0.3f), Fade(main, 0.3f), 72);
            Arc(outerR, spinAngle * 1.6f, 55f, s * 0.022f, Fade(accent, 0f), accent, 22);

            // 6. Corner brackets, like a targeting reticle; they breathe outward with the voice.
            float bracketR = s * (1.08f + level * 0.06f + flash * 0.1f);
            for (int k = 0; k < 4; k++)
            {
                float center = (45f + 90f * k) * Mathf.Deg2Rad;
                Arc(bracketR, center - 11f * Mathf.Deg2Rad, 22f, s * 0.03f, Fade(main, 0.85f), Fade(main, 0.85f), 8);
                Segment(Polar(center - 11f * Mathf.Deg2Rad, bracketR), Polar(center - 11f * Mathf.Deg2Rad, bracketR - s * 0.07f), s * 0.03f, Fade(main, 0.85f), Fade(main, 0.2f));
                Segment(Polar(center + 11f * Mathf.Deg2Rad, bracketR), Polar(center + 11f * Mathf.Deg2Rad, bracketR - s * 0.07f), s * 0.03f, Fade(main, 0.85f), Fade(main, 0.2f));
            }

            // 7. Three small lights orbiting at different radii and speeds.
            for (int k = 0; k < 3; k++)
            {
                float r = s * (0.56f + 0.14f * k);
                float a = t * (1.1f - 0.3f * k) * (k == 1 ? -1f : 1f) * (1f + look.spin * 0.5f) + k * 2.1f;
                Segment(Polar(a, r), Polar(a + 0.12f, r), s * 0.045f, Fade(accent, 0f), Fade(accent, 0.95f));
            }

            // 8. Shockwave after the wake word.
            if (shockTime >= 0f)
            {
                float k = (t - shockTime) / 0.6f;
                if (k >= 1f) shockTime = -1f;
                else Arc(Mathf.Lerp(s * 0.4f, s * 1.5f, 1f - (1f - k) * (1f - k)), 0f, 360f, s * 0.05f * (1f - k), Fade(accent, 1f - k), Fade(accent, 1f - k), 72);
            }

            hudMesh.Clear();
            hudMesh.SetVertices(verts);
            hudMesh.SetColors(colors);
            hudMesh.SetUVs(0, uvs);
            hudMesh.SetTriangles(tris, 0);
            hudMesh.bounds = new Bounds(Vector3.zero, Vector3.one * s * 4f);

            // Core: soft orb that swells with the voice.
            float coreScale = s * (0.45f + level * 0.35f + flash * 0.3f + (state == RingState.Idle ? 0.05f * breathe : 0f));
            core.localScale = new Vector3(coreScale, coreScale, 1f);
            coreMat.SetColor(TintId, main * (0.9f + look.intensity * 1.4f + flash * 2f));

            // Projector on the desk, directly under the emblem.
            if (projector != null)
            {
                projector.SetPositionAndRotation(new Vector3(transform.position.x, DeskTop, transform.position.z), Quaternion.Euler(0f, yaw, 0f));
                baseRing.localRotation = Quaternion.Euler(90f, 0f, t * 40f * (1f + look.spin));
                beamMat.SetColor(TintId, main * 1.6f);
                beamMat.SetFloat(IntensityId, (state == RingState.Idle || state == RingState.Paused ? 0.3f : 0.75f) + level * 0.6f + flash);
                baseGlowMat.SetColor(TintId, main * (1.2f + look.intensity + flash * 2f));
                baseRingMat.SetColor(TintId, new Color(main.r * 1.5f, main.g * 1.5f, main.b * 1.5f, 1f));
                baseRingMat.SetFloat(IntensityId, 0.8f + look.intensity * 0.5f);
            }

            if (captionGroup != null)
            {
                bool talking = !string.IsNullOrEmpty(heard.text) || !string.IsNullOrEmpty(reply.text);
                captionGroup.alpha = Mathf.MoveTowards(captionGroup.alpha, talking ? 1f : 0.7f, Time.deltaTime * 3f);
            }

            UpdateReplyPages();
            if (captionUntil > 0f && Time.time > captionUntil)
            {
                heard.text = "";
                SetReply("");
                captionUntil = 0f;
            }
        }

        // ------------------------------------------------------------------ geometry helpers

        static Vector3 Polar(float angle, float radius) => new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);

        static Color Fade(Color c, float alpha) => new Color(c.r, c.g, c.b, alpha);

        /// <summary>A flat glowing line segment (quad) in the emblem's plane.</summary>
        void Segment(Vector3 a, Vector3 b, float width, Color ca, Color cb)
        {
            Vector3 dir = b - a;
            if (dir.sqrMagnitude < 1e-12f) return;
            Vector3 n = new Vector3(-dir.y, dir.x, 0f).normalized * (width * 0.5f);
            int i = verts.Count;
            verts.Add(a - n); verts.Add(a + n); verts.Add(b + n); verts.Add(b - n);
            colors.Add(ca); colors.Add(ca); colors.Add(cb); colors.Add(cb);
            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        /// <summary>Arc from startRad sweeping sweepDeg, color blending from c0 to c1 along it.</summary>
        void Arc(float radius, float startRad, float sweepDeg, float width, Color c0, Color c1, int segments)
        {
            float sweep = sweepDeg * Mathf.Deg2Rad;
            for (int i = 0; i < segments; i++)
            {
                float u0 = i / (float)segments, u1 = (i + 1) / (float)segments;
                Segment(Polar(startRad + sweep * u0, radius), Polar(startRad + sweep * u1, radius), width,
                    Color.Lerp(c0, c1, u0), Color.Lerp(c0, c1, u1));
            }
        }

        Transform BuildCore()
        {
            var go = new GameObject("Core");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.001f); // just behind the lines
            var mesh = new Mesh { name = "Voice Core Quad" };
            mesh.SetVertices(new[] { new Vector3(-1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0), new Vector3(1, -1, 0) });
            mesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
            mesh.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            coreMat = new Material(Shader.Find("Cypher/GlowParticle"));
            coreMat.SetFloat("_Softness", 0.35f);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = coreMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        const float DeskTop = 0.752f;

        TextMeshPro MakeText(string name, TMP_FontAsset font, float y, float maxSize, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            var t = go.AddComponent<TextMeshPro>();
            t.font = font;
            t.fontSharedMaterial = textMat;
            t.alignment = TextAlignmentOptions.Center;
            t.enableAutoSizing = true;
            t.fontSizeMin = 0.05f;
            t.fontSizeMax = maxSize;
            t.overflowMode = TextOverflowModes.Truncate;
            t.richText = false;
            t.color = color;
            t.rectTransform.sizeDelta = new Vector2(0.62f, 0.024f);
            t.text = "";
            return t;
        }

        static Look Target(RingState s) => s switch
        {
            RingState.Listening => ListenLook,
            RingState.Thinking => ThinkLook,
            RingState.Speaking => SpeakLook,
            RingState.Paused => PausedLook,
            _ => IdleLook,
        };

        static Look Blend(Look a, Look b, float k) => new Look
        {
            main = Color.Lerp(a.main, b.main, k),
            accent = Color.Lerp(a.accent, b.accent, k),
            intensity = Mathf.Lerp(a.intensity, b.intensity, k),
            spin = Mathf.Lerp(a.spin, b.spin, k),
            barGain = Mathf.Lerp(a.barGain, b.barGain, k),
            wobble = Mathf.Lerp(a.wobble, b.wobble, k),
        };
    }
}
