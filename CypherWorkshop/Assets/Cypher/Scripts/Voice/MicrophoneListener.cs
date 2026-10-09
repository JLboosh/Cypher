using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cypher
{
    /// <summary>
    /// Keeps the microphone open, resamples to 16 kHz mono, and cuts the stream into
    /// utterances with a simple energy-based voice activity detector (VAD):
    /// speech starts when the level rises well above the background noise, and ends after
    /// a short silence. Each finished utterance is raised through SegmentReady.
    /// </summary>
    public class MicrophoneListener : MonoBehaviour
    {
        public const int SampleRate = 16000;
        const int FrameSamples = 320; // 20 ms
        const float FrameSeconds = FrameSamples / (float)SampleRate;

        [Tooltip("Speech starts when the level is this many times the background noise.")]
        [SerializeField] float startRatio = 3f;
        [SerializeField] float endRatio = 1.8f;
        [Tooltip("Absolute minimum level that can count as speech.")]
        [SerializeField] float minLevel = 0.008f;
        [SerializeField] float endSilenceSeconds = 0.75f;
        [SerializeField] float preRollSeconds = 0.35f;
        [SerializeField] float maxSegmentSeconds = 15f;
        [SerializeField] float minSegmentSeconds = 0.35f;

        /// <summary>A complete utterance: 16 kHz mono samples.</summary>
        public event Action<float[]> SegmentReady;
        public event Action SpeechStarted;

        /// <summary>Smoothed input level 0..1, for the voice ring.</summary>
        public float Level { get; private set; }
        public bool IsRecording => clip != null;
        public bool InSpeech => speaking;
        public string DeviceName => device;

        /// <summary>While muted, nothing is detected (Cypher talking, you typing).</summary>
        public bool Muted
        {
            get => muted;
            set
            {
                if (value && !muted) ResetSpeech();
                muted = value;
            }
        }

        string device;
        AudioClip clip;
        int deviceRate;
        int channels;
        int readPos;
        float[] chunk = new float[0];
        double resamplePos;
        float lastSample;
        readonly List<float> pending = new List<float>();
        readonly float[] frame = new float[FrameSamples];

        readonly Queue<float[]> preRoll = new Queue<float[]>();
        readonly List<float> segment = new List<float>();
        bool speaking;
        bool muted;
        int loudFrames;
        float silence;
        float noiseFloor = 0.004f;
        float sensitivity = 1f;

        /// <summary>Returns false if there is no microphone.</summary>
        public bool Begin(string preferredDevice, float micSensitivity)
        {
            var devices = Microphone.devices;
            if (devices.Length == 0) return false;

            device = Array.Exists(devices, d => d == preferredDevice) ? preferredDevice : devices[0];
            sensitivity = Mathf.Max(0.1f, micSensitivity);
            Microphone.GetDeviceCaps(device, out int min, out int max);
            deviceRate = (min == 0 && max == 0) ? SampleRate : Mathf.Clamp(SampleRate, min, max);
            clip = Microphone.Start(device, true, 4, deviceRate);
            channels = clip.channels;
            readPos = 0;
            Debug.Log($"[Cypher] Microphone \"{device}\" at {deviceRate} Hz");
            return true;
        }

        void OnDisable()
        {
            if (clip != null) Microphone.End(device);
            clip = null;
        }

        void Update()
        {
            if (clip == null) return;
            int pos = Microphone.GetPosition(device);
            if (pos < 0 || pos == readPos) return;

            int available = (pos - readPos + clip.samples) % clip.samples;
            Read(readPos, Mathf.Min(available, clip.samples - readPos));
            if (readPos + available > clip.samples) Read(0, pos);
            readPos = pos;

            while (pending.Count >= FrameSamples)
            {
                pending.CopyTo(0, frame, 0, FrameSamples);
                pending.RemoveRange(0, FrameSamples);
                ProcessFrame(frame);
            }
        }

        void Read(int offset, int count)
        {
            if (count <= 0) return;
            int needed = count * channels;
            if (chunk.Length < needed) chunk = new float[needed];
            var data = needed == chunk.Length ? chunk : new float[needed];
            clip.GetData(data, offset);

            // Take the first channel and resample linearly to 16 kHz.
            double step = deviceRate / (double)SampleRate;
            for (int i = 0; i < count; i++)
            {
                float s = data[i * channels];
                while (resamplePos <= 1.0)
                {
                    pending.Add(Mathf.Lerp(lastSample, s, (float)resamplePos));
                    resamplePos += step;
                }
                resamplePos -= 1.0;
                lastSample = s;
            }
        }

        void ProcessFrame(float[] f)
        {
            float sum = 0f;
            for (int i = 0; i < f.Length; i++) sum += f[i] * f[i];
            float rms = Mathf.Sqrt(sum / f.Length);
            Level = Mathf.Lerp(Level, Mathf.Clamp01(rms * 25f), 0.35f);

            var copy = (float[])f.Clone();
            preRoll.Enqueue(copy);
            while (preRoll.Count > preRollSeconds / FrameSeconds) preRoll.Dequeue();

            float floor = minLevel / sensitivity;
            float startThreshold = Mathf.Max(noiseFloor * startRatio, floor);
            float endThreshold = Mathf.Max(noiseFloor * endRatio, floor * 0.7f);

            if (muted)
            {
                noiseFloor = Mathf.Lerp(noiseFloor, rms, 0.01f);
                return;
            }

            if (!speaking)
            {
                if (rms > startThreshold)
                {
                    loudFrames++;
                }
                else
                {
                    loudFrames = 0;
                    noiseFloor = Mathf.Lerp(noiseFloor, rms, 0.02f);
                }

                if (loudFrames >= 3)
                {
                    speaking = true;
                    silence = 0f;
                    segment.Clear();
                    foreach (var pre in preRoll) segment.AddRange(pre);
                    SpeechStarted?.Invoke();
                }
                return;
            }

            segment.AddRange(copy);
            silence = rms < endThreshold ? silence + FrameSeconds : 0f;
            if (silence >= endSilenceSeconds || segment.Count >= maxSegmentSeconds * SampleRate)
            {
                var samples = segment.ToArray();
                ResetSpeech();
                if (samples.Length >= minSegmentSeconds * SampleRate) SegmentReady?.Invoke(samples);
            }
        }

        void ResetSpeech()
        {
            speaking = false;
            loudFrames = 0;
            silence = 0f;
            segment.Clear();
        }
    }
}
