# Cypher Workshop: a holographic 3D to-do list (Unity 6, macOS)

A personal desktop to-do app built like a game. You sit at a curved desk in a dark lab, tasks float
as holograms, and a voice assistant called "Cypher" manages them.

## Getting started

1. Clone the repo, then run `tools/setup.sh`. It downloads the Whisper speech model and the Piper
   voice, which are too large for git.
2. Open `CypherWorkshop/` in Unity 6000.3.25f1. The first open rebuilds `Library/`, which takes a few minutes.
3. **Cypher → Build Workshop Scene**, then **Cypher → Build macOS App** (see `docs/PHASE-8.md`).

Or download a prebuilt `Cypher.app` from the Releases page.

## Repo layout

```
cypher/
├── README.md              ← you are here: roadmap and free-tier notes
├── docs/
│   ├── PHASE-N.md         ← step-by-step setup and test guide for each phase
│   └── WORKSHOP.md        ← the workshop environment (props, lighting, how to tweak)
├── tools/                 ← Piper TTS (venv + voice), fetch_polyhaven.py
├── Builds/Cypher.app      ← the standalone app (after Cypher → Build macOS App)
└── CypherWorkshop/        ← the Unity 6.3 project
    └── Assets/Cypher/
    ├── Scripts/           ← runtime C# (Core, Data, Holograms, Interaction, Environment)
    ├── Editor/            ← editor-only tools (the one-click scene builder)
    └── Shaders/           ← hand-written URP shaders
```

## Roadmap

| Phase | What you get | Status |
|---|---|---|
| 1 | Project setup, desk scene, lighting, camera rotation, draggable holograms, save/load | ✅ Done: `docs/PHASE-1.md` |
| 2 | Hologram polish: flicker, glitches, particles, wireframe→solid→glow materialize, procedural sound | ✅ Done: `docs/PHASE-2.md` |
| 3 | Edit panel: double-click/pencil, all fields, date picker, Save/Cancel/Delete | ✅ Done: `docs/PHASE-3.md` |
| 4 | Cypher voice: Whisper wake word + speech-to-text → Gemini/Ollama/offline rules → Piper; add/edit/list/done/remove | ✅ Done: `docs/PHASE-4.md` |
| 5 | Crumple-and-throw removal: holographic hands, staged paper crumple (vertex shader), arc + fading trail, trash landing | ✅ Done: `docs/PHASE-5.md` |
| 6 | Reminders (voice + hologram pulse + macOS notification, snooze, missed-while-closed) and smart ranking | ✅ Done: `docs/PHASE-6.md` |
| 7 | Google Calendar + Google Tasks + Notion import, no duplicates, optional two-way sync for edits and deletions | ✅ Built: `docs/PHASE-7.md` |
| 8 | Standalone macOS app: icon, mic permission, App Nap off, background running, ad-hoc signed | **Ready**: `docs/PHASE-8.md` |
| + | Real workshop environment (CC0 Poly Haven props, concrete & steel room, warm lamps) | **Ready**: `docs/WORKSHOP.md` |

Each phase builds on the previous one. Test a phase before starting the next.

## Free-tier reality check

Everything in the plan can be done for $0, but a few pieces have catches you should know about now.

| Piece | Free? | Catch / plan |
|---|---|---|
| Unity 6 Personal | Yes | Free under Unity's revenue threshold. Fine for a personal app. |
| URP, Shader Graph, VFX Graph, Input System, TextMeshPro | Yes | Built-in packages. VFX Graph runs on Apple Silicon (Metal compute). |
| Shaders | Yes | Shaders are hand-written URP HLSL rather than Shader Graph. Graph files can't be hand-authored reliably as text, and HLSL renders the same way. You can still use Shader Graph for your own experiments. |
| whisper.unity | Yes (MIT) | Runs locally; the `base.en` or `small.en` model is a good fit for an M-series Air. |
| Wake word | Yes | Porcupine's Unity SDK lost maintenance on Dec 15, 2025, so Whisper detects "Hey Cypher" locally instead: no account or key needed. |
| Piper TTS | Yes | The original repo is archived and development moved to `OHF-Voice/piper1-gpl` (GPL, which is fine for personal use). Phase 4 runs it as a local process, with macOS `say` as the placeholder. |
| Gemini API | Free tier | Rate-limited. On the free tier Google may use your prompts to improve its products, so don't dictate anything sensitive. Model names change often, so the model is a setting, not hard-coded. **Ollama** is the private, offline fallback. |
| Google Calendar / Tasks API | Yes | Desktop OAuth with a loopback redirect is free. **Catch:** while your OAuth app is in "Testing" status, refresh tokens expire after **7 days** and you'd have to sign in again weekly. Fix: set the app to "In production" without verification. You'll see an "unverified app" warning once, which is fine for personal use. Note that Google *Tasks* is a separate API from Calendar *events*; Phase 7 supports both. |
| Notion API | Yes | Internal integration token (API version 2026-03-11). You must add the integration to the database under Connections. |
| macOS notifications | Yes | Sent via `osascript` (`display notification`), so no paid Apple developer account is needed. |
| Running in the background | Yes | Unity keeps running when minimized with *Run In Background*. macOS **App Nap** can throttle it; Phase 8 disables App Nap in the built app's Info.plist. |
| Distributing the .app | Yes, for yourself | Without the $99 Apple Developer Program the app is unsigned/ad-hoc signed. On your own Mac you right-click → Open once. Fine for personal use. |
| 3D props & textures | Yes | Poly Haven, CC0 (public domain). See `ThirdParty/PolyHaven/CREDITS.md`. |
| Sound effects | Yes | Generated procedurally in code, or CC0 sounds from Kenney.nl / freesound.org (filter by CC0). |
# Cypher
