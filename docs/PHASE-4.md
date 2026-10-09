# Phase 4: Cypher, the voice assistant

**Result:** say "Hey Cypher" and a glowing ring lights up and listens. Add tasks by voice in a
short conversation, edit them hands-free, ask what's on your list, mark things done, remove them.
Cypher answers in a calm, slightly synthetic voice, and captions show what it heard and said.

```
mic ─► voice activity detection ─► Whisper (speech-to-text, local, Metal GPU)
     ─► "Hey Cypher"? ─► understand (Gemini ▸ Ollama ▸ offline rules) ─► do it ─► speak (Piper ▸ macOS say)
```

## Changes from the original plan (and why)

| Plan | What I did | Why |
|---|---|---|
| Wake word with **Picovoice Porcupine** (Vosk fallback) | Whisper itself listens for "Hey Cypher" | Picovoice stopped maintaining the Porcupine Unity SDK on Dec 15, 2025 and archived its Unity repo. Whisper is already loaded for speech-to-text, so this needs **no account, no key, no extra native libraries**, and also catches "Hey Cypher, add milk" in one breath. Cost: a little CPU whenever someone talks nearby. |
| AI always decides what you meant | Gemini ▸ Ollama ▸ **offline rules** | Everything works with zero accounts. The AI only interprets; the app itself performs actions and writes Cypher's replies, so a confused AI can't delete things on its own. |

## Already done for you

- **whisper.unity 1.4.0** added to `Packages/manifest.json` (MIT; pinned to a specific commit).
- **Whisper model** `ggml-base.en.bin` (142 MB, checksum verified) in `Assets/StreamingAssets/Whisper/`.
- **Piper TTS** installed outside the project in `~/cypher/tools/` (230 MB): a Python virtual
  environment with `piper-tts` 1.8.0 and the voice **en_GB-alan-medium** (a calm British male).
  Tested: about 0.9 s to synthesize a sentence.

## New files

| File | Job |
|---|---|
| `Voice/VoiceAssistant.cs` | The conductor: wake word, conversations, add/edit/remove/done/list flows |
| `Voice/MicrophoneListener.cs` | Microphone → 16 kHz; cuts speech into utterances (voice activity detection) |
| `Voice/SpeechRecognizer.cs` | Whisper speech-to-text; filters Whisper's "phantom" words in silence |
| `Voice/TextToSpeech.cs` | Piper or `say` → WAV → plays in Unity with a subtle robotic filter; WAV loader |
| `Voice/CypherBrain.cs` | Asks Gemini / Ollama for structured JSON; falls back to rules |
| `Voice/VoiceIntent.cs` | Intent format, offline RuleBrain, wake-word finder, fuzzy task matching |
| `Voice/NaturalDate.cs` | "next Friday", "in two weeks", "tomorrow at 5pm", "the 15th"... without AI |
| `Voice/VoiceRing.cs` | The audio-reactive ring and captions |
| `Voice/CypherConfig.cs` | Settings + API keys in a file **outside** the project |
| `Shaders/GlowLine.shader` | Glow for the ring |

## Setup

### 1. Let Unity install whisper.unity
Switch to Unity. You'll see "Resolving packages" and then an import (it downloads from GitHub, about a
minute). Check the Console for red errors. **Window → Package Manager → In Project** should now
list **Whisper**.

### 2. Allow the microphone
Press **Play**. macOS asks *"Unity would like to access the microphone"*: click **Allow**.
If you missed it: **System Settings → Privacy & Security → Microphone → Unity: on**, then
restart Unity.

The ring below the holograms says "Loading speech model..." for a few seconds, then
**"Say 'Hey Cypher'"**. The Console shows a line like
`[Cypher] Voice ready. Mic: MacBook Air Microphone. Understanding: ... Voice: Piper.`

That's enough to use everything. Steps 3 and 4 are optional upgrades.

### 3. (Optional, recommended) Gemini, for understanding free-form speech
Without it, Cypher understands the phrasings in the table below. With Gemini it handles
anything ("I really need to get my taxes sorted before the 15th").

1. Go to **https://aistudio.google.com/apikey** and sign in with a Google account.
2. **Create API key** (accept the terms; it may create a Google Cloud project for you; that's free).
3. Copy the key.
4. In Unity: **Cypher → Open Config File (API keys, voice)**. It opens `cypher-config.json` in a text editor.
5. Paste the key between the quotes: `"geminiApiKey": "AIza..."`. Save. Restart Play.
6. The Console's "Voice ready" line should say `Understanding: Gemini (gemini-3.5-flash-lite)`.

**Free-tier notes:** rate limits are generous for a personal to-do app. On the free tier Google may
use your prompts to improve its products, so don't dictate anything sensitive. If Google renames
models, change `geminiModel` in the config (see https://ai.google.dev/gemini-api/docs/models).
The key lives in `~/Library/Application Support/DefaultCompany/CypherWorkshop/`, never in the
Unity project.

### 4. (Optional) Ollama, a fully private local AI
Used when there's no Gemini key or Gemini fails. Needs ~2 GB of disk and some RAM while running.
```bash
brew install ollama
ollama serve            # leave running (or: brew services start ollama)
ollama pull llama3.2:3b # in a second Terminal tab
```
Cypher detects it automatically (`useOllama: true`, `ollamaModel` in the config).

## Things you can say

| Say | What happens |
|---|---|
| "Hey Cypher" | Chime and flash; it listens for ~8 s without the wake word |
| "Hey Cypher, add buy milk" | "When do you want this done by?" → "next Friday" → "When should I remind you?" → "Thursday at 6pm" → confirms; hologram materializes |
| …"undetermined" / "no deadline" / "not sure" | Saved with no deadline (no reminder question) |
| …"no reminder" / "don't remind me" | Saved without a reminder |
| "Hey Cypher, add finish the lab report by Friday" | Skips the deadline question |
| "Hey Cypher, remind me to call mom tomorrow at 6pm" | Adds "Call mom" due then |
| "Hey Cypher, edit the lab report" | Opens the edit panel by voice; then **without the wake word**: "change the deadline to Monday", "rename it to Finish lab report", "set priority to high", "difficulty 4", "remind me Thursday at 6", "no reminder", "add a note bring the charger", "mark it done", "save" / "cancel" / "delete it" |
| "Hey Cypher, what's on my list?" | Reads up to 5 tasks with due dates |
| "Hey Cypher, what's most important?" | Names the top task; its hologram flashes |
| "Hey Cypher, mark call the dentist done" / "I finished reading chapter 7" | Marks it done |
| "Hey Cypher, remove buy groceries" | Removes it (the crumple animation arrives in Phase 5). If two tasks match ("finish lab"), it asks which one |
| "cancel" / "never mind" during a question | Stops the conversation |

Dates it understands offline: today, tonight, tomorrow (morning/evening), the day after tomorrow,
Friday, this/next Friday, in 3 days, in two weeks, in an hour, next week/month, this weekend,
end of the week/month, October 12, 12th of October, the 15th, 10/12, plus times like 5pm,
5:30 pm, 17:00, at 5, noon, midnight.

## Test

| # | Do this | Expect |
|---|---|---|
| 1 | Watch the ring at idle | Dim, breathing; "Say 'Hey Cypher'". It follows when you turn the camera |
| 2 | Talk normally without the wake word | Ring brightens with your voice briefly, then nothing happens |
| 3 | "Hey Cypher" | Chime, flash, "Listening..."; ring ripples with your voice |
| 4 | Full add flow from the table | Captions show what it heard; violet "thinking" comet; Cypher speaks; hologram builds in |
| 5 | Edit by voice: "edit buy milk" → "set priority to high" → "change the deadline to Monday" → "save" | The panel opens and updates **live** after each command; Cypher confirms each; save shimmer |
| 6 | Open the edit panel with the mouse, click into the title, and talk | Ring says "Listening paused while you type"; nothing is transcribed |
| 7 | "what's on my list" / "what's most important" | Spoken summary |
| 8 | "remove finish lab" (with "Finish lab report" and another "finish lab…" task) | Asks which one; answer "the first one" |
| 9 | Stop Play, Play | Voice-added tasks persisted |

## Tuning

| What | Where |
|---|---|
| Voice (Piper/say), speed, robot amount, mic, sensitivity, AI keys/models | `cypher-config.json` (**Cypher → Open Config File**) |
| Another Piper voice | `~/cypher/tools/piper-venv/bin/python -m piper.download_voices --data-dir ~/cypher/tools/piper-voices en_US-ryan-high`, then set `piperModel` (browse voices at https://rhasspy.github.io/piper-samples/) |
| macOS voice instead | `"ttsEngine": "say"`, `"sayVoice": "Daniel"` (run `say -v '?'` for the list) |
| Follow-up window, edit-mode timeout | **Cypher Voice** object → VoiceAssistant (during Play, or after a scene rebuild) |
| Speech detection thresholds | **Cypher Voice** → MicrophoneListener |
| Ring position/size | **Voice Ring** → VoiceRing (*Anchor*, *Radius*) |

## Troubleshooting

| Symptom | Fix |
|---|---|
| Ring says "No microphone found" | Check System Settings → Sound → Input; set `microphoneDevice` in the config to the exact device name shown in the Console |
| Ring never reacts to your voice | Microphone permission (setup step 2). Or raise `micSensitivity` to 1.5–2 |
| It reacts to every noise | Lower `micSensitivity` to 0.6 |
| "Speech model failed to load" | Check `Assets/StreamingAssets/Whisper/ggml-base.en.bin` exists (148 MB) |
| "Hey Cypher" not recognized | Look at the caption to see what Whisper heard. If it's consistently something else (e.g. "Hey Sifa"), add that word to `wakeWords` in the config |
| Robotic macOS voice instead of Piper | The Console warns "Piper not found"; check the two `piper...` paths in the config |
| Gemini errors in the Console (403/404) | 403 = bad key; 404 = model renamed (update `geminiModel`); 429 = rate limit (it falls back to rules automatically) |

---

When everything works, ask for **Phase 5**: the signature crumple-and-throw removal animation
with holographic hands, sparks, the arcing trail and the trash-can landing.
