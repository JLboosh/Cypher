using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Cypher
{
    /// <summary>
    /// Turns a transcript into a VoiceIntent. Tries, in order:
    ///   1. Gemini (free tier) if an API key is in the config
    ///   2. Ollama on this Mac if it's running
    ///   3. RuleBrain (offline patterns), which always works
    /// The AI is only asked to *understand* the request; the app does the actual work and
    /// writes the spoken replies itself, so a confused AI can't delete things on its own.
    /// </summary>
    public class CypherBrain
    {
        readonly CypherConfig config;
        bool ollamaAvailable;
        float nextOllamaCheck;

        public string LastSource { get; private set; } = "rules";

        public CypherBrain(CypherConfig config)
        {
            this.config = config;
        }

        public bool HasGemini => !string.IsNullOrWhiteSpace(config.geminiApiKey);

        public async Task<VoiceIntent> Interpret(string text, BrainContext ctx)
        {
            string prompt = BuildSystemPrompt(ctx);

            if (HasGemini)
            {
                var intent = Validate(await AskGemini(prompt, text));
                if (intent != null) { LastSource = "Gemini"; return intent; }
            }

            if (config.useOllama && await IsOllamaUp())
            {
                var intent = Validate(await AskOllama(prompt, text));
                if (intent != null) { LastSource = "Ollama"; return intent; }
            }

            LastSource = "rules";
            return RuleBrain.Interpret(text, ctx);
        }

        // ------------------------------------------------------------------ prompt

        static string BuildSystemPrompt(BrainContext ctx)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You interpret spoken commands for Cypher, a voice-controlled to-do list. Reply with ONE JSON object only.");
            sb.AppendLine($"Now (local): {ctx.Now:dddd yyyy-MM-dd HH:mm}.");
            sb.AppendLine("The text comes from speech recognition and may contain small mistakes; infer the intended meaning.");
            sb.AppendLine();
            sb.AppendLine("JSON keys (always include all; use \"\" or 0 when not mentioned):");
            sb.AppendLine("  intent: one of add_task, edit_task, remove_task, mark_done, list_tasks, most_important,");
            sb.AppendLine("          set_title, set_notes, set_due, clear_due, set_reminder, clear_reminder, set_priority,");
            sb.AppendLine("          set_difficulty, set_done, set_not_done, delete_current, save, cancel, yes, no,");
            sb.AppendLine("          arrange (= let Cypher re-arrange the holograms by priority),");
            sb.AppendLine("          import (= open the import window; put 'calendar', 'tasks' or 'notion' in task), unknown");
            sb.AppendLine("  title: for add_task the new task's title (short, capitalized, without date words); for set_title the new name");
            sb.AppendLine("  task: for edit_task/remove_task/mark_done, the words identifying the existing task");
            sb.AppendLine("  due: deadline as \"YYYY-MM-DDTHH:MM\" (use 23:59 when no time was said)");
            sb.AppendLine("  reminder: reminder time as \"YYYY-MM-DDTHH:MM\" (use 09:00 when no time was said)");
            sb.AppendLine("  priority: low | medium | high | auto (auto = 'let Cypher decide')");
            sb.AppendLine("  difficulty: 1-5");
            sb.AppendLine("  notes: text to add as a note (set_notes)");
            sb.AppendLine("Resolve relative dates ('next Friday', 'in two weeks', 'tomorrow evening') to absolute dates.");
            sb.AppendLine("'Remind me to X' means add_task with title X.");
            sb.AppendLine();
            if (ctx.State == "editing")
            {
                sb.AppendLine($"The user is EDITING the task \"{ctx.EditingTitle}\". Commands like 'change the deadline to Monday',");
                sb.AppendLine("'rename it to ...', 'set priority to high', 'make it difficulty 4', 'mark it done', 'save', 'cancel'");
                sb.AppendLine("refer to that task: use the set_* / clear_* / save / cancel / delete_current intents.");
            }
            if (ctx.TaskTitles.Count > 0)
                sb.AppendLine("Existing tasks: " + string.Join(" | ", ctx.TaskTitles));
            return sb.ToString();
        }

        static VoiceIntent Validate(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            json = json.Trim();
            int start = json.IndexOf('{'), end = json.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            try
            {
                var intent = JsonUtility.FromJson<VoiceIntent>(json.Substring(start, end - start + 1));
                if (intent == null || string.IsNullOrEmpty(intent.intent) || intent.intent == VoiceIntent.Unknown) return null;
                return intent;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Cypher] AI returned unreadable JSON: {e.Message}\n{json}");
                return null;
            }
        }

        // ------------------------------------------------------------------ Gemini

        [Serializable] class Part { public string text; }
        [Serializable] class Content { public string role; public Part[] parts; }
        [Serializable] class GenerationConfig { public float temperature; public string responseMimeType; }
        [Serializable] class SystemContent { public Part[] parts; } // no "role": Gemini rejects an empty one
        [Serializable] class GeminiRequest { public SystemContent systemInstruction; public Content[] contents; public GenerationConfig generationConfig; }
        [Serializable] class Candidate { public Content content; }
        [Serializable] class GeminiResponse { public Candidate[] candidates; }

        async Task<string> AskGemini(string system, string userText)
        {
            var body = new GeminiRequest
            {
                systemInstruction = new SystemContent { parts = new[] { new Part { text = system } } },
                contents = new[] { new Content { role = "user", parts = new[] { new Part { text = userText } } } },
                generationConfig = new GenerationConfig { temperature = 0f, responseMimeType = "application/json" },
            };
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{config.geminiModel}:generateContent";
            string response = await Post(url, JsonUtility.ToJson(body), ("x-goog-api-key", config.geminiApiKey), 10);
            if (response == null) return null;
            var parsed = JsonUtility.FromJson<GeminiResponse>(response);
            var parts = parsed?.candidates?.Length > 0 ? parsed.candidates[0].content?.parts : null;
            return parts != null && parts.Length > 0 ? parts[0].text : null;
        }

        // ------------------------------------------------------------------ Ollama

        [Serializable] class OllamaMessage { public string role; public string content; }
        [Serializable] class OllamaOptions { public float temperature; }
        [Serializable] class OllamaRequest { public string model; public bool stream; public string format; public OllamaMessage[] messages; public OllamaOptions options; }
        [Serializable] class OllamaResponse { public OllamaMessage message; }

        async Task<string> AskOllama(string system, string userText)
        {
            var body = new OllamaRequest
            {
                model = config.ollamaModel,
                stream = false,
                format = "json",
                messages = new[]
                {
                    new OllamaMessage { role = "system", content = system },
                    new OllamaMessage { role = "user", content = userText },
                },
                options = new OllamaOptions { temperature = 0f },
            };
            string response = await Post(config.ollamaUrl.TrimEnd('/') + "/api/chat", JsonUtility.ToJson(body), default, 30);
            return response == null ? null : JsonUtility.FromJson<OllamaResponse>(response)?.message?.content;
        }

        static readonly string[] OllamaInstallPaths =
            { "/opt/homebrew/bin/ollama", "/usr/local/bin/ollama", "/Applications/Ollama.app" };

        async Task<bool> IsOllamaUp()
        {
            if (Time.realtimeSinceStartup < nextOllamaCheck) return ollamaAvailable;
            // Not installed locally: don't ping (each failed ping logs a "Curl error" in the Console).
            bool local = config.ollamaUrl.Contains("localhost") || config.ollamaUrl.Contains("127.0.0.1");
            if (local && !System.Array.Exists(OllamaInstallPaths, p => System.IO.File.Exists(p) || System.IO.Directory.Exists(p)))
            {
                nextOllamaCheck = Time.realtimeSinceStartup + 300f;
                return ollamaAvailable = false;
            }
            nextOllamaCheck = Time.realtimeSinceStartup + (ollamaAvailable ? 300f : 60f);
            using (var req = UnityWebRequest.Get(config.ollamaUrl.TrimEnd('/') + "/api/tags"))
            {
                req.timeout = 1;
                await req.SendWebRequest();
                ollamaAvailable = req.result == UnityWebRequest.Result.Success;
            }
            return ollamaAvailable;
        }

        // ------------------------------------------------------------------ HTTP

        static async Task<string> Post(string url, string json, (string name, string value) header, int timeoutSeconds)
        {
            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrEmpty(header.name)) req.SetRequestHeader(header.name, header.value);
                req.timeout = timeoutSeconds;
                await req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success) return req.downloadHandler.text;
                // Never log the request (it contains the API key header); the response is safe.
                Debug.LogWarning($"[Cypher] AI request failed ({req.responseCode} {req.error}): {Truncate(req.downloadHandler?.text, 300)}");
                return null;
            }
        }

        static string Truncate(string s, int max) => s == null ? "" : s.Length <= max ? s : s.Substring(0, max) + "...";
    }
}
