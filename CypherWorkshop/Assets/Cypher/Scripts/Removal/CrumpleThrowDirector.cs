using System;
using System.Collections;
using UnityEngine;

namespace Cypher
{
    /// <summary>
    /// The signature removal animation. Every removal (voice, Delete button, debug key) plays:
    ///   1. Two holographic hands materialize at the hologram's sides.
    ///   2. They crush it in stages like a sheet of paper: creases fold in, it wrinkles and
    ///      squeezes; each stage crinkles and throws sparks.
    ///   3. It ends as a glowing crumpled ball.
    ///   4. The right hand winds up and tosses it in a spinning parabolic arc at the trash can.
    ///   5. A glowing trail follows the arc, lingers, then fades out completely.
    ///   6. On landing: flash, ripples across the can, a "swish".
    /// Motion uses no randomness (sparks use a fixed seed), so it looks the same every time.
    /// Every timing below is adjustable in the Inspector (Systems or Cypher Removal object).
    /// </summary>
    public class CrumpleThrowDirector : MonoBehaviour
    {
        public static CrumpleThrowDirector Instance { get; private set; }

        [Header("1. Hands appear")]
        [SerializeField] float handsInSeconds = 0.45f;
        [Tooltip("How far out the hands start before moving in to grip (m).")]
        [SerializeField] float handsApproachDistance = 0.06f;

        [Header("2-3. Crumple")]
        [Tooltip("Crumple speed: total seconds from flat sheet to ball.")]
        [SerializeField] float crumpleSeconds = 1.5f;
        [Tooltip("Crumple amount over time. The flat parts are pauses between squeezes.")]
        [SerializeField] AnimationCurve crumpleCurve = DefaultCrumpleCurve();
        [Tooltip("Normalized times (0-1) of each squeeze: crinkle sound + sparks.")]
        [SerializeField] float[] squeezeMoments = { 0f, 0.38f, 0.72f };
        [SerializeField] float ballRadius = 0.045f;
        [SerializeField] float ballBrightness = 1.6f;
        [SerializeField] int sparksPerSqueeze = 14;
        [SerializeField] float holdBallSeconds = 0.2f;

        [Header("4. Throw")]
        [SerializeField] float windupSeconds = 0.28f;
        [Tooltip("Minimum seconds in the air (short throws).")]
        [SerializeField] float flightSeconds = 0.85f;
        [Tooltip("Ball speed along the arc for long throws (m/s). Flight time = max(Flight Seconds, distance / speed).")]
        [SerializeField] float throwSpeed = 5.5f;
        [Tooltip("Minimum arc height above the straight line to the can (m).")]
        [SerializeField] float arcHeight = 0.32f;
        [Tooltip("Extra arc height per meter of distance, so long throws loft like a real toss.")]
        [SerializeField] float arcHeightPerMeter = 0.18f;
        [Tooltip("The top of the arc never goes above this height (stays under the ceiling beams).")]
        [SerializeField] float maxArcTopY = 3.8f;
        [SerializeField] float spinDegreesPerSecond = 540f;

        [Header("5. Trail")]
        [SerializeField] float trailWidth = 0.014f;
        [Tooltip("How long the full arc stays visible after landing.")]
        [SerializeField] float trailHoldSeconds = 0.4f;
        [Tooltip("Then it fades out completely over this long.")]
        [SerializeField] float trailFadeSeconds = 2.5f;
        [SerializeField] Color trailColor = new Color(0.4f, 1.5f, 2.6f, 1f);

        [Header("6. Landing")]
        [SerializeField] int landingSparks = 28;

        [Header("Look (blank = found at runtime)")]
        [SerializeField] Material handMaterial;
        [SerializeField] Material lineMaterial;
        [SerializeField] Shader crumpleShader;
        [SerializeField] Material sparkMaterial;

        static readonly int TintId = Shader.PropertyToID("_Tint");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        ParticleSystem sparks;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        static AnimationCurve DefaultCrumpleCurve()
        {
            // Three squeezes with brief pauses: 0 -> 0.32, hold, -> 0.64, hold, -> 1.
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.24f, 0.32f), new Keyframe(0.38f, 0.34f),
                new Keyframe(0.6f, 0.64f), new Keyframe(0.72f, 0.66f), new Keyframe(1f, 1f));
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
            return curve;
        }

        void Awake()
        {
            Instance = this;
            if (handMaterial == null) handMaterial = new Material(Shader.Find("Cypher/HoloHand"));
            if (lineMaterial == null) lineMaterial = new Material(Shader.Find("Cypher/GlowLine"));
            if (crumpleShader == null) crumpleShader = Shader.Find("Cypher/HologramCrumple");
            if (sparkMaterial == null)
            {
                sparkMaterial = new Material(Shader.Find("Cypher/GlowParticle"));
                sparkMaterial.SetColor(TintId, new Color(0.8f, 2.2f, 3.2f, 1f));
            }
            sparks = BuildSparks();
        }

        public bool CanPlay => crumpleShader != null && handMaterial != null && FindFirstObjectByType<TrashCan>() != null;

        /// <summary>
        /// Plays the sequence for a hologram. The hologram object itself is destroyed right away
        /// (a crumple-able copy takes its place). onReleased runs when the ball leaves the hand.
        /// </summary>
        public void Play(HologramPanel hologram, Action onReleased)
        {
            var sheet = CrumpleSheet.FromHologram(hologram, crumpleShader, ballRadius, 0.37f);
            Destroy(hologram.gameObject);
            StartCoroutine(Sequence(sheet, onReleased));
        }

        IEnumerator Sequence(CrumpleSheet sheet, Action onReleased)
        {
            var trash = FindFirstObjectByType<TrashCan>();
            var rng = new System.Random(4242); // fixed seed: identical sparks every time
            Transform st = sheet.transform;
            Vector3 center = st.position, right = st.right, up = st.up, toward = -st.forward; // toward = toward the viewer
            float half0 = sheet.WorldHalfWidth;

            var leftHand = HoloHand.Create("Hand L", left: true, handMaterial);
            var rightHand = HoloHand.Create("Hand R", left: false, handMaterial);

            // Hand placement around the sheet for a given half-width and grip.
            void PoseHands(Vector3 c, float half, float curl, float spread)
            {
                float lower = 0.075f - curl * 0.035f; // wrists drop below, fingers wrap over the edge
                Vector3 fingerDir = (up * (1f - curl * 0.35f) + toward * (0.25f + curl * 0.5f)).normalized;
                leftHand.SetPose(c - right * (half + 0.012f + spread) - up * lower + toward * 0.02f, right, fingerDir);
                rightHand.SetPose(c + right * (half + 0.012f + spread) - up * lower + toward * 0.02f, -right, fingerDir);
                leftHand.SetCurl(curl);
                rightHand.SetCurl(curl);
            }

            // 1. Hands materialize and move in to grip the edges.
            CypherAudio.Play(Sfx.Materialize, center, 0.45f);
            for (float t = 0f; t < handsInSeconds; t += Time.deltaTime)
            {
                float k = Smooth(t / handsInSeconds);
                PoseHands(center, half0, 0.15f * k, handsApproachDistance * (1f - k));
                leftHand.SetMaterialize(k);
                rightHand.SetMaterialize(k);
                yield return null;
            }

            // 2-3. Crumple in stages.
            int nextSqueeze = 0;
            for (float t = 0f; ; t += Time.deltaTime)
            {
                float u = Mathf.Clamp01(t / crumpleSeconds);
                while (nextSqueeze < squeezeMoments.Length && u >= squeezeMoments[nextSqueeze])
                {
                    CypherAudio.PlayVariant(Sfx.Crinkle, nextSqueeze, center);
                    float halfNow = Mathf.Lerp(half0, ballRadius, Squeeze(crumpleCurve.Evaluate(u)));
                    EmitSparks(rng, center, right, up, toward, halfNow, sparksPerSqueeze, 0.25f);
                    nextSqueeze++;
                }

                float c = crumpleCurve.Evaluate(u);
                sheet.SetCrumple(c);
                sheet.SetBrightness(Mathf.Lerp(1f, ballBrightness, Mathf.SmoothStep(0.6f, 1f, c)));
                float half = Mathf.Lerp(half0, ballRadius, Squeeze(c));
                PoseHands(center, half, Mathf.Lerp(0.15f, 0.9f, c), 0f);
                if (u >= 1f) break;
                yield return null;
            }

            // Ball glow light.
            var ballLight = new GameObject("Ball Light").AddComponent<Light>();
            ballLight.type = LightType.Point;
            ballLight.color = new Color(0.4f, 0.85f, 1f);
            ballLight.range = 0.6f;
            ballLight.intensity = 0.8f;
            ballLight.shadows = LightShadows.None;
            ballLight.transform.SetParent(st, false);

            yield return new WaitForSeconds(holdBallSeconds);

            // 4a. Left hand lets go and fades; right hand winds up with the ball.
            Vector3 target = trash != null ? trash.MouthPosition : center + right * 0.8f - up * 0.4f;
            Vector3 throwDir = Vector3.ProjectOnPlane(target - center, Vector3.up).normalized;
            Vector3 windupPos = center - throwDir * 0.05f + Vector3.up * 0.06f + toward * 0.03f;
            Quaternion ballRot = st.rotation;

            for (float t = 0f; t < windupSeconds; t += Time.deltaTime)
            {
                float k = Smooth(t / windupSeconds);
                st.position = Vector3.Lerp(center, windupPos, k);
                leftHand.SetMaterialize(1f - k);
                leftHand.transform.position -= right * 0.15f * Time.deltaTime;
                PlaceHoldingHand(rightHand, st.position, throwDir, 0.9f);
                yield return null;
            }
            Destroy(leftHand.gameObject);

            // 4b. Release: parabolic arc with spin. Trail on.
            Vector3 start = st.position;
            Vector3 end = target - Vector3.up * (trash != null ? Mathf.Clamp(trash.MouthRadius * 0.6f, 0.06f, 0.2f) : 0.06f); // into the can

            // Long throws take longer and loft higher, but stay under the ceiling.
            float distance = Vector3.Distance(start, end);
            float flight = Mathf.Max(flightSeconds, distance / Mathf.Max(0.1f, throwSpeed));
            float height = Mathf.Max(arcHeight, distance * arcHeightPerMeter);
            float apex = (start.y + end.y) * 0.5f + height;
            if (apex > maxArcTopY) height = Mathf.Max(0.1f, height - (apex - maxArcTopY));
            Vector3 spinAxis = Vector3.Cross(Vector3.up, (end - start).normalized).normalized;
            if (spinAxis.sqrMagnitude < 0.01f) spinAxis = right;
            var trail = CreateTrail(st);
            onReleased?.Invoke();
            CypherAudio.Play(Sfx.Whoosh, start, 0.8f);

            Vector3 handStart = rightHand.transform.position;
            float startScale = st.localScale.x;
            for (float t = 0f; t < flight; t += Time.deltaTime)
            {
                float k = t / flight;
                st.position = Vector3.Lerp(start, end, k) + Vector3.up * (height * 4f * k * (1f - k));
                st.rotation = Quaternion.AngleAxis(spinDegreesPerSecond * t, spinAxis) * ballRot;
                // Shrink a little in the last stretch so the ball drops cleanly into the can's mouth.
                st.localScale = Vector3.one * startScale * (k > 0.85f ? Mathf.Lerp(1f, 0.7f, (k - 0.85f) / 0.15f) : 1f);

                // Follow-through: the hand flicks toward the can, opens, and dissolves.
                float hk = Mathf.Clamp01(t / 0.35f);
                rightHand.transform.position = handStart + throwDir * 0.08f * Smooth(hk) + Vector3.up * 0.02f * hk;
                rightHand.SetCurl(Mathf.Lerp(0.9f, 0.1f, Smooth(hk)));
                rightHand.SetMaterialize(1f - hk);
                yield return null;
            }
            Destroy(rightHand.gameObject);

            // 6. Landing.
            st.position = end;
            // Play the swish part-way toward the can: clearly from that direction, but audible from the seat.
            var listener = Camera.main != null ? Camera.main.transform.position : end;
            CypherAudio.Play(Sfx.Swish, Vector3.Lerp(listener, end, 0.35f));
            if (trash != null) trash.PlayLanding(lineMaterial);
            EmitSparks(rng, target, Vector3.right, Vector3.up, Vector3.forward, 0.05f, landingSparks, 0.6f);
            trail.transform.SetParent(null, true);
            trail.emitting = false;
            Destroy(sheet.gameObject);

            // 5. The trail lingers, then fades out completely.
            StartCoroutine(FadeTrail(trail));
        }

        static void PlaceHoldingHand(HoloHand hand, Vector3 ballPos, Vector3 throwDir, float curl)
        {
            // Palm faces the ball from behind (opposite the throw), fingers up and around it.
            Vector3 palm = throwDir;
            hand.SetPose(ballPos - palm * 0.06f - Vector3.up * 0.06f, palm, (Vector3.up + palm * 0.4f).normalized);
            hand.SetCurl(curl);
        }

        TrailRenderer CreateTrail(Transform ball)
        {
            var go = new GameObject("Throw Trail");
            go.transform.SetParent(ball, false);
            var trail = go.AddComponent<TrailRenderer>();
            var mat = new Material(lineMaterial);
            mat.SetColor(TintId, trailColor);
            mat.SetFloat(IntensityId, 1.6f);
            trail.sharedMaterial = mat;
            trail.time = 60f; // points never expire on their own; FadeTrail controls the fade
            trail.minVertexDistance = 0.005f;
            trail.widthMultiplier = trailWidth;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.25f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.55f, 1f) });
            trail.colorGradient = g;
            trail.textureMode = LineTextureMode.Stretch;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.emitting = true;
            return trail;
        }

        IEnumerator FadeTrail(TrailRenderer trail)
        {
            var mat = trail.sharedMaterial;
            yield return new WaitForSeconds(trailHoldSeconds);
            for (float t = 0f; t < trailFadeSeconds; t += Time.deltaTime)
            {
                float k = t / trailFadeSeconds;
                mat.SetFloat(IntensityId, 1.6f * (1f - k) * (1f - k));
                trail.widthMultiplier = trailWidth * Mathf.Lerp(1f, 0.4f, k);
                yield return null;
            }
            Destroy(mat);
            Destroy(trail.gameObject);
        }

        void EmitSparks(System.Random rng, Vector3 c, Vector3 right, Vector3 up, Vector3 toward, float half, int count, float speed)
        {
            var p = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                double a = rng.NextDouble() * Math.PI * 2;
                Vector3 dir = (right * (float)Math.Cos(a) + up * (float)Math.Sin(a) + toward * (float)(rng.NextDouble() * 0.6)).normalized;
                p.position = c + (right * (float)Math.Cos(a) * half + up * (float)Math.Sin(a) * half * 0.6f);
                p.velocity = dir * speed * (float)(0.4 + rng.NextDouble() * 0.8);
                p.startLifetime = (float)(0.35 + rng.NextDouble() * 0.4);
                p.startSize = (float)(0.003 + rng.NextDouble() * 0.004);
                sparks.Emit(p, 1);
            }
        }

        ParticleSystem BuildSparks()
        {
            var go = new GameObject("Removal Sparks");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.35f;
            main.maxParticles = 400;
            main.startColor = new Color(0.7f, 1f, 1f, 1f);
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.5f, 0.8f, 1f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = sparkMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.sortingOrder = 4;
            ps.Play();
            return ps;
        }

        /// <summary>How far the sheet has pulled in, matching the shader's squeeze, for hand placement.</summary>
        static float Squeeze(float c)
        {
            float k = Mathf.Clamp01(c * 1.05f);
            return k * k * (3f - 2f * k);
        }

        static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
