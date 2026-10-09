# Phase 2: Hologram shaders and visual polish

**Time:** about 5 minutes to set up, then play-testing.
**Result:** holograms build themselves in (wireframe → solid → glow) with sparks and a rising
shimmer sound, flicker subtly with occasional glitches, shed drifting particles, and the lab has
floating dust. Hover, click, pick-up and drop all make soft UI sounds.

## What changed

| File | Change |
|---|---|
| `Shaders/Hologram.shader` | Adds the 3-stage materialize (`_Materialize` 0→1), glitch bands (`_Glitch`) and film grain |
| `Shaders/GlowParticle.shader` | **New.** Soft additive glowing dot for all particles |
| `Scripts/Audio/ProceduralSfx.cs` | **New.** Synthesizes every sound effect in code (no audio files) |
| `Scripts/Audio/CypherAudio.cs` | **New.** Plays the effects through a pool of voices; optional override slots |
| `Scripts/Holograms/HologramPanel.cs` | Materialize/dematerialize, flicker and glitches synced with the text, edge spark bursts |
| `Scripts/Core/TaskManager.cs` | Staggered boot sequence; new tasks materialize; deleted tasks dematerialize |
| `Scripts/Interaction/InteractionController.cs` | UI sounds; holograms ignore the mouse while still building in or fading out |
| `Editor/WorkshopSceneBuilder.cs` | Builds the particle systems and adds CypherAudio |

### Two deliberate deviations from the original spec

- **Particles use Unity's built-in Particle System, not VFX Graph.** A VFX Graph asset is a large
  node-graph file that can't be written reliably as text, so you'd have to rebuild it by hand
  from screenshots. The built-in Particle System can be created entirely from code, looks the same
  at this scale (a few dozen specks per hologram), and runs on the CPU, which leaves the laptop GPU
  free for bloom. If you later want VFX Graph for something big (the Phase 5 sparks, for example),
  it can be added alongside.
- **Sounds are generated in code.** Every sound effect is synthesized at startup, so there are no
  files to download and nothing to license. If you find CC0 sounds you like better (Kenney.nl or
  freesound.org filtered to CC0), drag them into the override slots on **Systems → CypherAudio**.

## Setup

1. Switch to Unity. It recompiles for a few seconds. Check the Console for red errors.
2. **Cypher → Build Workshop Scene → Build.** This regenerates the scene and the hologram prefab so
   they include the new particles and audio. Your tasks and your existing material tweaks are kept.
   If Unity asks to save the current scene, choose **Don't Save**. The builder recreates it.
3. Press **Play**. Turn your volume up a bit.

## Test

| # | Do this | Expect |
|---|---|---|
| 1 | Press Play and watch | After half a second the holograms build in one by one, most important first. Each one shows its outline tracing from the bottom up with a faint grid, then the fill dissolving upward behind a bright edge, then a glow flash and the text fading in. Sparks fly off the edges |
| 2 | Listen | A rising shimmer on the first hologram, and soft blips on the rest |
| 3 | Watch an idle hologram for ~20 s | Gentle shimmer and occasional brief glitches: bright horizontal bands while the text dips |
| 4 | Look closely | Tiny glowing specks drift upward around each panel; faint dust floats in the room |
| 5 | Hover across holograms | A soft tick on each new hologram |
| 6 | Drag one and drop it | A rising "pick-up" tone, then a falling "drop" tone. Motes travel with the panel |
| 7 | Hover and click quickly while it is still building in (press **N**, then click immediately) | Nothing happens until it's fully built |
| 8 | **N** | The new hologram materializes in its slot with sound |
| 9 | Hover one, **Shift+Backspace** | It un-builds (reverse effect + reverse sound), then the others glide to fill the gap |
| 10 | Turn the camera fully left and right | Smooth; check **Stats** in the Game view. It should still be well above 60 FPS |

## Tuning (all live in Play mode, but changes made during Play are lost when you stop)

Tip: tweak during Play to find values you like, write them down, stop, then enter them for real.
Changes to **materials** are the exception: they persist even if made during Play.

| What | Where |
|---|---|
| Materialize/dematerialize speed, spark count, build flash | `Generated/Hologram.prefab` → HologramPanel → *Materialize* |
| Flicker strength/speed, glitch frequency | Hologram prefab → HologramPanel → *Flicker* (set *Flicker Amount* to 0 for steady panels) |
| Wireframe grid size/color, grain, scanlines | `Generated/Materials/Hologram.mat` |
| Particle color/brightness | `Generated/Materials/HologramMote.mat` and `LabDust.mat` (Tint) |
| Particle amount/speed | Hologram prefab → Motes (Particle System); scene → Environment → Ambient Dust |
| Volumes, 3D-ness, your own sound files | Scene → Systems → CypherAudio |
| Startup timing | Systems → TaskManager → *Startup* |

To edit the prefab: double-click `Assets/Cypher/Generated/Hologram.prefab`, change values, then
click **<** at the top of the Hierarchy to leave prefab mode. Note: re-running **Build Workshop
Scene** regenerates the prefab and the scene, so redo prefab/scene tweaks afterwards (or tell me
your preferred values and I'll make them the defaults in the code).

## Troubleshooting

| Symptom | Fix |
|---|---|
| Holograms never appear | Check the Console. If the Hologram shader failed to compile, select `Shaders/Hologram.shader` to see the error and paste it to me |
| Particles are pink squares | The `GlowParticle` shader failed. Same fix as above with `Shaders/GlowParticle.shader` |
| No sound | Game view toolbar: make sure **Mute Audio** is off. Check that Systems has a CypherAudio component (rebuild the scene if not) |
| Sounds too loud or too quiet | Systems → CypherAudio → Volume sliders |
| Flicker is distracting | Hologram prefab → Flicker Amount 0.03, Glitch Interval (15, 40) |

---

When everything works, ask for **Phase 3**: the edit panel (double-click or pencil opens it; all
fields, date picker, Save/Cancel/Delete).
