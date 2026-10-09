# Phase 5: The crumple-and-throw removal

**Result:** whenever a task is removed (by voice, the edit panel's Delete, or Shift+Backspace),
two holographic hands appear, crush the hologram like a sheet of paper in three squeezes
(crinkle, sparks, crinkle, sparks...) into a glowing ball, wind up, and toss it in a spinning
arc into the trash can. A glowing trail traces the arc, lingers, and fades away. The can
flashes, ripples, and goes *swish*.

## How it works

| Piece | File | How |
|---|---|---|
| Choreography + all timings | `Removal/CrumpleThrowDirector.cs` | One coroutine runs the 6 steps; no randomness in motion, fixed seed for sparks, so it's identical every time |
| The paper | `Removal/CrumpleSheet.cs` + `Shaders/HologramCrumple.shader` | Photographs the hologram (text included) into a texture with a temporary camera, swaps it for a 40×26-quad sheet, and a **vertex-displacement shader** driven by `_Crumple` 0→1 folds creases in one after another, squeezes unevenly (wrinkles), then wraps it onto a lumpy glowing sphere |
| The hands | `Removal/HoloHand.cs` + `Shaders/HoloHand.shader` | Built from capsules: palm, 4 three-joint fingers, 2-joint thumb. Rim-glow shader with a noisy dissolve for appearing/disappearing. Fingers curl as they squeeze |
| Trail | in the director | TrailRenderer that never expires on its own; after landing it holds, then fades to nothing |
| Landing | `Environment/TrashCan.cs` | Flash (material + light), two ripples spreading over the rim |
| Sounds | `Audio/ProceduralSfx.cs` | 3 different paper crinkles (one per squeeze, same order every time), throw whoosh, landing swish (net swish + soft thump + small chime) |

The task is deleted from your save file the moment the animation starts (so quitting
mid-throw can't bring it back). The other holograms glide into the gap as the ball is released.

### Deviation from the plan
There are no free, redistributable hand models, so the hands are procedural (built from capsule
shapes) rather than imported. They read as stylized holographic hands. If you later find a CC0 hand
model you like, the director only needs a `SetPose` / `SetCurl` / `SetMaterialize` equivalent.

## Setup

1. Switch to Unity and wait for it to compile. Check the Console for red errors.
2. Press **Play**. The removal system creates itself (no rebuild needed).
3. Optional, recommended before Phase 8: stop Play and run **Cypher → Build Workshop Scene**
   so the scene references the new shaders/materials (the standalone build needs that).

## Test

| # | Do this | Expect |
|---|---|---|
| 1 | Press **N** to add a test task; hover it; **Shift+Backspace** | Hands dissolve in at the sides → 3 squeezes with crinkles and sparks → glowing ball → wind-up → spinning arc with trail → flash, ripples, swish at the can |
| 2 | Watch the trail | Stays fully visible briefly after landing, then fades out completely over ~2.5 s |
| 3 | Watch the text during the crumple | The task's text crumples with the paper |
| 4 | Repeat test 1 a few times | Same folds, same timing, same sounds every time |
| 5 | Turn the camera right to watch the can during a throw | Ball lands inside the mouth |
| 6 | Double-click a task → **Delete** → **Confirm?** | Panel flies back, then the crumple plays from the hologram's spot |
| 7 | "Hey Cypher, remove buy milk" | Cypher says "Removing Buy milk." while the animation plays |
| 8 | Remove a task in the back ring (far away) | Still lands in the can (the arc adapts) |
| 9 | Stop Play, Play | Removed tasks stay removed |

## Tuning (Inspector)

During Play, select **Cypher Removal** (or, after a scene rebuild, the same object in the
scene) → **CrumpleThrowDirector**. Values changed during Play reset when you stop; note the
ones you like and set them outside Play (or tell me and I'll make them the defaults).

| Section | Settings |
|---|---|
| 1. Hands appear | Hands In Seconds, Hands Approach Distance |
| 2-3. Crumple | **Crumple Seconds** (speed), **Crumple Curve** (click it to edit: flat parts are the pauses between squeezes), Squeeze Moments (when crinkles/sparks happen), Ball Radius, Ball Brightness, Sparks Per Squeeze, Hold Ball Seconds |
| 4. Throw | Windup Seconds, **Flight Seconds** (minimum), **Throw Speed** (long throws), **Arc Height** + **Arc Height Per Meter**, Max Arc Top Y (stays under the ceiling), Spin Degrees Per Second |
| 5. Trail | Trail Width, **Trail Hold Seconds**, **Trail Fade Seconds**, Trail Color |
| 6. Landing | Landing Sparks; plus on **Trash Can → TrashCan**: Flash Brightness/Seconds, Light Flash Intensity, Ripple Seconds/Max Radius/Count/Color |

The crumple *shape* (crease count, fold depth, lumpiness) lives in
`Shaders/HologramCrumple.shader` → `Displace()`; ask me if you want it crisper or rounder.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Hologram just vanishes / dematerializes the old way | No Trash Can in the scene, or a shader failed to compile. Check the Console; rebuild the scene |
| The ball is a blank glowing blob (no text) | The snapshot camera didn't render. Paste me any Console warnings mentioning "RenderRequest" |
| Hands are pink | `Shaders/HoloHand.shader` failed to compile; send me the error from its Inspector |
| Ball misses the can | Increase Flight Seconds slightly, or tell me where it lands |

---

When everything works, ask for **Phase 6**: reminders (spoken + macOS notification, hologram
pulse, works minimized) and Cypher's smart ranking (top tasks in front, re-ranking as
deadlines approach, and the "keep my placement?" question).
