using System.Collections.Generic;
using UnityEngine;

namespace Cypher
{
    public enum Sfx { Hover, Click, PickUp, Drop, Materialize, Dematerialize, Open, Close, Sync, Error, Crinkle, Whoosh, Swish }

    /// <summary>
    /// Plays all sound effects. Lives on "Systems".
    /// Anything can call CypherAudio.Play(Sfx.Click, position).
    /// Sounds are generated in code (ProceduralSfx) unless you drop your own clips into the
    /// override slots in the Inspector.
    /// </summary>
    public class CypherAudio : MonoBehaviour
    {
        public static CypherAudio Instance { get; private set; }

        [Header("Volume")]
        [Range(0f, 1f)] [SerializeField] float masterVolume = 0.8f;
        [Range(0f, 1f)] [SerializeField] float uiVolume = 0.45f;
        [Range(0f, 1f)] [SerializeField] float effectsVolume = 0.7f;

        [Header("Optional: your own clips (empty = generated sound)")]
        [SerializeField] AudioClip hoverOverride;
        [SerializeField] AudioClip clickOverride;
        [SerializeField] AudioClip pickUpOverride;
        [SerializeField] AudioClip dropOverride;
        [SerializeField] AudioClip materializeOverride;
        [SerializeField] AudioClip dematerializeOverride;

        [Header("Playback")]
        [Tooltip("How many effects can overlap at once.")]
        [SerializeField] int voices = 10;
        [Tooltip("0 = plain stereo, 1 = fully positioned in 3D.")]
        [Range(0f, 1f)] [SerializeField] float spatialBlend = 0.5f;
        [SerializeField] float pitchVariation = 0.03f;
        [SerializeField] float minHoverInterval = 0.06f;

        readonly Dictionary<Sfx, AudioClip> clips = new Dictionary<Sfx, AudioClip>();
        AudioClip[] crinkles;
        AudioSource[] pool;
        int nextVoice;
        float lastHoverTime = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        /// <summary>Safe to call even if there is no CypherAudio in the scene.</summary>
        public static void Play(Sfx sfx, Vector3 position, float volumeScale = 1f)
        {
            if (Instance != null) Instance.PlayAt(sfx, position, volumeScale, 0);
        }

        /// <summary>For sounds with several takes (Crinkle): pick a specific one, so sequences repeat exactly.</summary>
        public static void PlayVariant(Sfx sfx, int variant, Vector3 position, float volumeScale = 1f)
        {
            if (Instance != null) Instance.PlayAt(sfx, position, volumeScale, variant);
        }

        void Awake()
        {
            Instance = this;

            clips[Sfx.Hover] = hoverOverride ? hoverOverride : ProceduralSfx.Hover();
            clips[Sfx.Click] = clickOverride ? clickOverride : ProceduralSfx.Click();
            clips[Sfx.PickUp] = pickUpOverride ? pickUpOverride : ProceduralSfx.PickUp();
            clips[Sfx.Drop] = dropOverride ? dropOverride : ProceduralSfx.Drop();
            clips[Sfx.Materialize] = materializeOverride ? materializeOverride : ProceduralSfx.Materialize();
            clips[Sfx.Dematerialize] = dematerializeOverride ? dematerializeOverride : ProceduralSfx.Dematerialize();
            clips[Sfx.Open] = ProceduralSfx.Open();
            clips[Sfx.Close] = ProceduralSfx.Close();
            clips[Sfx.Sync] = ProceduralSfx.Sync();
            clips[Sfx.Error] = ProceduralSfx.Error();
            crinkles = new[] { ProceduralSfx.Crinkle(0), ProceduralSfx.Crinkle(1), ProceduralSfx.Crinkle(2) };
            clips[Sfx.Crinkle] = crinkles[0];
            clips[Sfx.Whoosh] = ProceduralSfx.Whoosh();
            clips[Sfx.Swish] = ProceduralSfx.Swish();

            pool = new AudioSource[voices];
            for (int i = 0; i < voices; i++) pool[i] = CreateSource($"Voice {i}");
        }

        void PlayAt(Sfx sfx, Vector3 position, float volumeScale, int variant)
        {
            if (sfx == Sfx.Hover)
            {
                if (Time.unscaledTime - lastHoverTime < minHoverInterval) return;
                lastHoverTime = Time.unscaledTime;
            }

            bool isUi = sfx != Sfx.Materialize && sfx != Sfx.Dematerialize && sfx != Sfx.Crinkle && sfx != Sfx.Whoosh && sfx != Sfx.Swish;
            var source = pool[nextVoice];
            nextVoice = (nextVoice + 1) % pool.Length;

            source.transform.position = position;
            source.clip = sfx == Sfx.Crinkle ? crinkles[Mathf.Abs(variant) % crinkles.Length] : clips[sfx];
            source.volume = masterVolume * (isUi ? uiVolume : effectsVolume) * volumeScale;
            source.pitch = isUi ? 1f + Random.Range(-pitchVariation, pitchVariation) : 1f; // effects sound identical every time
            source.Play();
        }

        AudioSource CreateSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = spatialBlend;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 12f;
            source.dopplerLevel = 0f;
            return source;
        }
    }
}
