using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Cypher
{
    /// <summary>
    /// Cypher's voice. Renders speech to a WAV file with Piper (local neural TTS) or macOS `say`,
    /// then plays it inside Unity, so the voice ring can react to it and a subtle ring-modulation
    /// filter can give it a slightly synthetic edge. Falls back to `say` if Piper isn't set up.
    /// </summary>
    public class TextToSpeech : MonoBehaviour
    {
        AudioSource source;
        SyntheticVoiceFilter filter;
        CypherConfig config;
        string workDir;
        bool generating;
        bool piperAvailable;

        public bool IsSpeaking => generating || (source != null && source.isPlaying);
        public float Level => filter != null ? filter.Level : 0f;
        public string EngineName => piperAvailable ? "Piper" : "macOS say";

        public void Init(CypherConfig cfg)
        {
            config = cfg;
            workDir = Path.Combine(Application.temporaryCachePath, "cypher-tts");
            Directory.CreateDirectory(workDir);

            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            filter = gameObject.AddComponent<SyntheticVoiceFilter>();
            filter.Mix = cfg.syntheticAmount;

            piperAvailable = cfg.ttsEngine == "piper"
                && File.Exists(CypherConfig.ExpandHome(cfg.piperExecutable))
                && File.Exists(CypherConfig.ExpandHome(cfg.piperModel));
            if (cfg.ttsEngine == "piper" && !piperAvailable)
                Debug.LogWarning("[Cypher] Piper not found at the configured paths; using macOS 'say' instead.");
        }

        /// <summary>Speaks and completes when the sentence has finished playing.</summary>
        public async Task Speak(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            Stop();
            generating = true;
            string id = Guid.NewGuid().ToString("N");
            string txt = Path.Combine(workDir, id + ".txt");
            string wav = Path.Combine(workDir, id + ".wav");
            AudioClip clip = null;
            try
            {
                File.WriteAllText(txt, text.Replace('\n', ' '));
                bool ok = piperAvailable && await RunPiper(txt, wav);
                if (!ok) ok = await RunSay(txt, wav);
                if (ok) clip = WavUtility.Load(wav, "Cypher speech");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Cypher] Speech failed: {e.Message}");
            }
            finally
            {
                generating = false;
                TryDelete(txt);
                TryDelete(wav);
            }

            if (clip == null) return;
            source.clip = clip;
            source.Play();
            while (source != null && source.isPlaying) await Awaitable.NextFrameAsync();
            Destroy(clip);
        }

        public void Stop()
        {
            if (source != null && source.isPlaying) source.Stop();
        }

        Task<bool> RunPiper(string textFile, string wavFile)
        {
            string scale = config.piperLengthScale.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Run(CypherConfig.ExpandHome(config.piperExecutable),
                $"-m {Quote(CypherConfig.ExpandHome(config.piperModel))} -i {Quote(textFile)} -f {Quote(wavFile)} --length-scale {scale}");
        }

        Task<bool> RunSay(string textFile, string wavFile) =>
            Run("/usr/bin/say",
                $"-v {Quote(config.sayVoice)} -r {config.sayWordsPerMinute} -f {Quote(textFile)} " +
                $"--file-format=WAVE --data-format=LEI16@22050 -o {Quote(wavFile)}");

        static Task<bool> Run(string exe, string args)
        {
            return Task.Run(() =>
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    var err = p.StandardError.ReadToEndAsync();
                    p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(20000))
                    {
                        p.Kill();
                        return false;
                    }
                    if (p.ExitCode != 0) Debug.LogWarning($"[Cypher] {Path.GetFileName(exe)} failed: {err.Result}");
                    return p.ExitCode == 0;
                }
            });
        }

        static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* temp file; ignore */ }
        }
    }

    /// <summary>
    /// Mixes in a little ring modulation (the classic "robot voice" trick) and measures the
    /// output level for the voice ring. Runs on Unity's audio thread.
    /// </summary>
    public class SyntheticVoiceFilter : MonoBehaviour
    {
        public float Mix = 0.12f;
        public float CarrierHz = 38f;

        public float Level { get; private set; }

        double phase;
        int sampleRate;

        void Awake() => sampleRate = AudioSettings.outputSampleRate;

        void OnAudioFilterRead(float[] data, int channels)
        {
            double step = 2.0 * Math.PI * CarrierHz / sampleRate;
            float mix = Mathf.Clamp01(Mix);
            float sum = 0f;
            for (int i = 0; i < data.Length; i += channels)
            {
                float mod = (float)Math.Sin(phase);
                phase += step;
                for (int c = 0; c < channels; c++)
                {
                    float d = data[i + c];
                    data[i + c] = d * (1f - mix) + d * mod * mix * 1.6f;
                    sum += d * d;
                }
            }
            if (phase > 1e6) phase -= 2.0 * Math.PI * Math.Floor(phase / (2.0 * Math.PI));
            float rms = Mathf.Sqrt(sum / Mathf.Max(1, data.Length));
            Level = Level * 0.6f + Mathf.Clamp01(rms * 6f) * 0.4f;
        }
    }

    /// <summary>Loads 16-bit PCM WAV files (what Piper and `say` produce) into AudioClips.</summary>
    public static class WavUtility
    {
        public static AudioClip Load(string path, string name)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 44 || bytes[0] != 'R' || bytes[8] != 'W') throw new InvalidDataException("Not a WAV file");

            int channels = 1, rate = 22050, bits = 16, dataStart = -1, dataLength = 0;
            int pos = 12;
            while (pos + 8 <= bytes.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
                int size = BitConverter.ToInt32(bytes, pos + 4);
                if (id == "fmt ")
                {
                    channels = BitConverter.ToInt16(bytes, pos + 10);
                    rate = BitConverter.ToInt32(bytes, pos + 12);
                    bits = BitConverter.ToInt16(bytes, pos + 22);
                }
                else if (id == "data")
                {
                    dataStart = pos + 8;
                    dataLength = Math.Min(size, bytes.Length - dataStart);
                    break;
                }
                pos += 8 + size + (size & 1);
            }
            if (dataStart < 0 || bits != 16) throw new InvalidDataException($"Unsupported WAV ({bits}-bit)");

            int count = dataLength / 2;
            var samples = new float[count];
            for (int i = 0; i < count; i++) samples[i] = BitConverter.ToInt16(bytes, dataStart + i * 2) / 32768f;

            var clip = AudioClip.Create(name, count / channels, channels, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
