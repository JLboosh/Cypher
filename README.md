# Cypher Workshop: a holographic 3D to-do list (Unity 6, macOS)

A personal desktop to-do app built like a game. You sit at a curved desk in a dark lab, tasks float
as holograms, and a voice assistant called "Cypher" manages them.

## Getting started

1. Clone the repo, then run `tools/setup.sh`. It downloads the Whisper speech model and the Piper
   voice, which are too large for git.
2. Open `CypherWorkshop/` in Unity 6000.3.25f1. The first open rebuilds `Library/`, which takes a few minutes.
3. **Cypher → Build Workshop Scene**, then **Cypher → Build macOS App** (see `docs/PHASE-8.md`).

## Features

- Desk scene, lighting, camera rotation, draggable holograms, save/load
- Hologram polish: flicker, glitches, particles, wireframe→solid→glow materialize, procedural sound 
- Edit panel: double-click/pencil, all fields, date picker, Save/Cancel/Delete 
- Cypher voice: Whisper wake word + speech-to-text → Gemini/Ollama/offline rules → Piper; add/edit/list/done/remove
- Crumple-and-throw removal: holographic hands, staged paper crumple (vertex shader), arc + fading trail, trash landing 
- Reminders (voice + hologram pulse + macOS notification, snooze, missed-while-closed) and smart ranking 
- Google Calendar + Google Tasks + Notion import, no duplicates, optional two-way sync for edits and deletions 
- Standalone macOS app: icon, mic permission, App Nap off, background running, ad-hoc signed
- Real workshop environment (CC0 Poly Haven props, concrete & steel room, warm lamps)
