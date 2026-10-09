using System;
using System.IO;
using UnityEngine;

namespace Cypher
{
    /// <summary>
    /// Personal settings and API keys, stored OUTSIDE the Unity project in
    /// ~/Library/Application Support/DefaultCompany/CypherWorkshop/cypher-config.json
    /// so keys never end up in Assets/ or in a shared repo. Created with defaults on first run.
    /// Edit it with any text editor (menu: Cypher > Open Config File), then restart Play.
    /// Paths may start with "~/" for your home folder.
    /// </summary>
    [Serializable]
    public class CypherConfig
    {
        [Header("AI: Gemini (free tier). Leave the key empty to skip Gemini.")]
        public string geminiApiKey = "";
        public string geminiModel = "gemini-3.5-flash-lite";

        [Header("AI: Ollama (local, private). Used when Gemini is off or fails.")]
        public bool useOllama = true;
        public string ollamaUrl = "http://localhost:11434";
        public string ollamaModel = "llama3.2:3b";

        [Header("Voice output: \"piper\" (local neural voice) or \"say\" (macOS built-in)")]
        public string ttsEngine = "piper";
        public string piperExecutable = "~/cypher/tools/piper-venv/bin/piper";
        public string piperModel = "~/cypher/tools/piper-voices/en_GB-alan-medium.onnx";
        [Tooltip("Above 1 = slower, calmer speech.")]
        public float piperLengthScale = 1.08f;
        public string sayVoice = "Daniel";
        public int sayWordsPerMinute = 175;
        [Tooltip("0 = natural, 0.3 = clearly robotic.")]
        public float syntheticAmount = 0.12f;

        [Header("Integrations (Phase 7)")]
        [Tooltip("From Google Cloud Console > Credentials > OAuth client ID (Desktop app).")]
        public string googleClientId = "";
        public string googleClientSecret = "";
        [Tooltip("Notion internal integration secret (starts with ntn_ or secret_).")]
        public string notionToken = "";
        [Tooltip("When you remove an imported task: \"ask\", \"always\" (delete at the source too) or \"never\".")]
        public string deleteSyncMode = "ask";
        [Tooltip("How many days ahead to list calendar events for import.")]
        public int importDaysAhead = 60;

        [Header("Reminders")]
        public bool macNotifications = true;
        [Tooltip("A macOS sound name: Glass, Ping, Pop, Hero, Submarine... or empty for silent.")]
        public string notificationSound = "Glass";

        [Header("Listening")]
        public string whisperModel = "Whisper/ggml-base.en.bin";
        [Tooltip("Empty = system default microphone.")]
        public string microphoneDevice = "";
        [Tooltip("Higher = picks up quieter speech (and more background noise).")]
        public float micSensitivity = 1f;
        public string[] wakeWords = { "cypher", "cipher", "sypher", "syfer", "sifer", "psypher", "cyber" };

        const string FileName = "cypher-config.json";

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static CypherConfig Load()
        {
            var config = new CypherConfig();
            try
            {
                if (File.Exists(FilePath))
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), config);
                // Re-save so settings added in newer versions show up in the file.
                File.WriteAllText(FilePath, JsonUtility.ToJson(config, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Cypher] Could not read {FilePath}: {e.Message}. Using defaults.");
            }
            return config;
        }

        public static string ExpandHome(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("~/")) return path;
            string home = Environment.GetEnvironmentVariable("HOME") ?? "";
            return Path.Combine(home, path.Substring(2));
        }
    }
}
