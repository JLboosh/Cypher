using System;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using Whisper;
using Whisper.Utils;

namespace Cypher
{
    /// <summary>
    /// Local speech-to-text with whisper.unity (whisper.cpp, Metal-accelerated on Apple Silicon).
    /// The model file lives in Assets/StreamingAssets/Whisper/.
    /// </summary>
    public class SpeechRecognizer : MonoBehaviour
    {
        // Whisper sometimes "hears" these in silence or noise.
        static readonly string[] Hallucinations =
        {
            "", "you", "thank you", "thanks", "thank you for watching", "thanks for watching", "bye",
            "blank audio", "silence", "music", "applause", "sound", "inaudible", "click", "beep",
        };

        WhisperManager whisper;

        public bool IsReady => whisper != null && whisper.IsLoaded;

        public async Task<bool> Init(string modelPath)
        {
            // Configure before the component wakes up, otherwise it starts loading with defaults.
            var go = new GameObject("Whisper");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            whisper = go.AddComponent<WhisperManager>();
            whisper.ModelPath = modelPath;
            whisper.logLevel = LogLevel.Error; // otherwise every sentence logs a message + stack trace
            SetPrivateField(whisper, "initOnAwake", false);
            SetPrivateField(whisper, "useGpu", true); // Metal on Apple Silicon
            whisper.language = "en";
            whisper.noContext = true;
            whisper.initialPrompt = "Hey Cypher, add a task.";
            go.SetActive(true);

            await whisper.InitModel();
            return whisper.IsLoaded;
        }

        /// <summary>Returns cleaned text, or "" if nothing meaningful was said.</summary>
        public async Task<string> Transcribe(float[] samples)
        {
            if (!IsReady) return "";
            var result = await whisper.GetTextAsync(samples, MicrophoneListener.SampleRate, 1);
            return Clean(result?.Result);
        }

        static string Clean(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            text = Regex.Replace(text, @"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*", " "); // [BLANK_AUDIO], (music), *sigh*
            text = Regex.Replace(text, @"\s+", " ").Trim();
            string bare = Regex.Replace(text.ToLowerInvariant(), @"[^a-z ]", "").Trim();
            return Array.IndexOf(Hallucinations, bare) >= 0 ? "" : text;
        }

        static void SetPrivateField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null) field.SetValue(target, value);
            else Debug.LogWarning($"[Cypher] whisper.unity has no field '{name}' (package version changed?)");
        }
    }
}
