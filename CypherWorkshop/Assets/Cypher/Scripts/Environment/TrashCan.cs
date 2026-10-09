using System.Collections;
using UnityEngine;

namespace Cypher
{
    /// <summary>
    /// The holographic trash can: a soft breathing glow, and the landing reaction for the
    /// crumple-and-throw animation (flash, light pulse, ripples spreading from the rim).
    /// MouthPosition is where thrown balls aim.
    /// </summary>
    public class TrashCan : MonoBehaviour
    {
        [SerializeField] Renderer glowRenderer;
        [SerializeField] Transform mouth;
        [Tooltip("Radius of the opening (m). Balls aim at its center; ripples start at its edge.")]
        [SerializeField] float mouthRadius = 0.07f;

        [Header("Idle glow")]
        [SerializeField] float idleBrightness = 0.9f;
        [SerializeField] float pulseAmplitude = 0.25f;
        [Tooltip("Breaths per second.")]
        [SerializeField] float pulseSpeed = 0.4f;

        [Header("Landing")]
        [SerializeField] float flashBrightness = 3.5f;
        [SerializeField] float flashSeconds = 0.5f;
        [SerializeField] float lightFlashIntensity = 4f;
        [SerializeField] float rippleSeconds = 0.75f;
        [SerializeField] float rippleMaxRadius = 0.24f;
        [SerializeField] int rippleCount = 2;
        [SerializeField] Color rippleColor = new Color(0.4f, 1.5f, 2.6f, 1f);

        static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        static readonly int TintId = Shader.PropertyToID("_Tint");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        Material material;
        Light glowLight;
        float baseLightIntensity;
        float flash;

        public Vector3 MouthPosition => mouth != null ? mouth.position : transform.position + Vector3.up * 0.2f;
        public float MouthRadius => mouthRadius;

        void Awake()
        {
            if (glowRenderer != null) material = glowRenderer.material;
            glowLight = GetComponentInChildren<Light>();
            if (glowLight != null) baseLightIntensity = glowLight.intensity;
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
        }

        void Update()
        {
            flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime / Mathf.Max(0.01f, flashSeconds));
            float eased = flash * flash;
            if (material != null)
            {
                float pulse = Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f);
                material.SetFloat(BrightnessId, idleBrightness + pulseAmplitude * pulse + flashBrightness * eased);
            }
            if (glowLight != null) glowLight.intensity = baseLightIntensity + lightFlashIntensity * eased;
        }

        /// <summary>Flash + ripples. Called when a crumpled ball drops in.</summary>
        public void PlayLanding(Material lineMaterial)
        {
            flash = 1f;
            for (int i = 0; i < rippleCount; i++) StartCoroutine(Ripple(lineMaterial, i * 0.14f));
        }

        IEnumerator Ripple(Material source, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            var go = new GameObject("Ripple");
            go.transform.SetParent(transform, false);
            go.transform.position = MouthPosition;
            var lr = go.AddComponent<LineRenderer>();
            var mat = source != null ? new Material(source) : new Material(Shader.Find("Cypher/GlowLine"));
            mat.SetColor(TintId, rippleColor);
            lr.sharedMaterial = mat;
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.alignment = LineAlignment.TransformZ;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            const int points = 64;
            lr.positionCount = points;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // lie flat across the rim

            for (float t = 0f; t < rippleSeconds; t += Time.deltaTime)
            {
                float k = t / rippleSeconds;
                float ease = 1f - (1f - k) * (1f - k);
                float r = Mathf.Lerp(MouthRadius, rippleMaxRadius, ease);
                for (int i = 0; i < points; i++)
                {
                    float a = i / (float)points * Mathf.PI * 2f;
                    lr.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
                }
                lr.widthMultiplier = Mathf.Lerp(0.012f, 0.002f, k) * Mathf.Max(1f, mouthRadius / 0.07f * 0.6f);
                mat.SetFloat(IntensityId, (1f - k) * 2f);
                yield return null;
            }
            Destroy(mat);
            Destroy(go);
        }
    }
}
