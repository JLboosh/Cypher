# Phase 1: Project setup, desk scene, camera, draggable holograms, save/load

**Time:** about 30–45 minutes, mostly waiting for installs.
**Result:** you sit at a glowing curved desk in a dark lab. Six example task holograms float in front of
you. You can turn left/right, hover holograms to make them brighten, drag them anywhere in 3D,
and they're still where you left them after you quit and reopen.

---

## 1. Install Unity 6 LTS

You currently have Unity **2022.3**, which is too old for this project. Install Unity 6 alongside it;
the two versions don't conflict.

1. Open **Unity Hub** → **Installs** → **Install Editor**.
2. Choose the newest **Unity 6** release labeled **LTS** (version numbers look like `6000.x.y`).
3. On the modules screen, tick:
   - **Mac Build Support (IL2CPP)** (used in Phase 8)
   - **Documentation** (optional, offline docs)
4. Install. If Hub asks for a license: **Preferences → Licenses → Add → Get a free personal license**.

## 2. Create the project

1. Hub → **Projects** → **New project**.
2. Editor version: the Unity 6 LTS you just installed.
3. Template: **Universal 3D** (this is URP; download it if Hub asks).
4. Project name: `CypherWorkshop`
   Location: `/Users/jackyliu/cypher`
5. **Create project** and wait for the editor to open.

You now have `/Users/jackyliu/cypher/CypherWorkshop/`.

## 3. Add the Cypher scripts

Move the `Cypher` folder into the project's `Assets` folder. In Terminal:

```bash
mv ~/cypher/UnityAssets/Cypher ~/cypher/CypherWorkshop/Assets/
```

(Or drag `UnityAssets/Cypher` into the **Project** window in Unity. Alternatively, tell Claude and it
will move the folder for you. Later phases are written straight into the project.)

Switch back to Unity. It imports and compiles the scripts for a few seconds. Open
**Window → General → Console** and confirm there are **no red errors**.

You should see this in the Project window:

```
Assets/Cypher/
  Editor/WorkshopSceneBuilder.cs
  Scripts/Core/        InputLock.cs, TaskManager.cs
  Scripts/Data/        TaskItem.cs, TaskRepository.cs
  Scripts/Environment/ CurvedDeskMesh.cs, TrashCan.cs
  Scripts/Holograms/   HologramLayout.cs, HologramPanel.cs, HologramPencil.cs
  Scripts/Interaction/ CameraRigController.cs, DebugTaskHotkeys.cs, InteractionController.cs
  Shaders/             Hologram.shader
```

> **Why the `Editor` folder matters:** Unity treats any folder named `Editor` as editor-only code.
> It never ships in your built app. The scene builder lives there.

## 4. Check packages and settings (one-time)

1. **Window → Package Manager → In Project**. Confirm these are listed (the Universal 3D template
   includes them):
   - **Universal RP**
   - **Input System**
   - **Unity UI** (in Unity 6, TextMeshPro ships inside this package)

   If **Input System** is missing: **Unity Registry** tab → Input System → **Install**. When Unity
   asks to enable the new input backend, click **Yes**. The editor restarts.
2. **Edit → Project Settings → Player → Other Settings → Active Input Handling** must be
   **Input System Package (New)** or **Both**.
3. **Edit → Project Settings → Player → Resolution and Presentation → Run In Background**: tick it
   (needed for reminders later; the script also sets it at runtime).
4. Import TextMeshPro resources: **Window → TextMeshPro → Import TMP Essential Resources** → **Import**.
   This adds `Assets/TextMesh Pro/`.

Phases 2 and 4 add VFX Graph, whisper.unity and others. You don't need them yet.

## 5. Build the scene (one click)

Menu bar → **Cypher → Build Workshop Scene** → **Build**.

The builder creates and wires everything, so there's no manual Inspector hookup in this phase:

| Created | Where |
|---|---|
| Scene `Workshop.unity`, added to Build Settings | `Assets/Cypher/Scenes/` |
| Hologram prefab | `Assets/Cypher/Generated/Hologram.prefab` |
| Materials (desk, glow strips, floor, hologram, text glow) | `Assets/Cypher/Generated/Materials/` |
| Post-processing profile (Bloom, Vignette, Chromatic Aberration, ACES tonemapping) | `Assets/Cypher/Generated/WorkshopPostProcessing.asset` |

Re-running the builder regenerates the scene and the prefab. It **keeps** existing materials and the
post-processing profile, so color tweaks you make there survive a rebuild.

**Tip:** set the **Game** view's aspect dropdown to **16:10** to match the MacBook Air screen.

## 6. What's in the scene (and what to tweak)

Click each object in the **Hierarchy** to see its components in the **Inspector**.

| Object | Component(s) | Useful settings |
|---|---|---|
| **CameraRig** | `CameraRigController` | Min/Max Yaw (±90°), Max Look Up (45°) / Max Look Down (35°), Mouse + Vertical Sensitivity, *Drag Moves World* (flip if rotation feels backwards), Keyboard Speed, Smooth Time. Controls: drag empty space or ←→↑↓ to look around; **Home** or double-click empty space = back to the default view |
| CameraRig → **Main Camera** | Camera (eye height 1.2 m, tilted 10° down) | Post Processing is on under *Rendering* |
| **Systems** | `TaskManager` | Hologram Prefab, Hologram Root, *Seed Sample Tasks If Empty* |
|  | `InteractionController` | Drag Threshold (px), Double Click Seconds, Scroll Depth Speed |
|  | `DebugTaskHotkeys` | Phase 1 test keys (remove later) |
| **Environment → Desk** | `CurvedDeskMesh` on each piece | Radii, arc, thickness. Edits rebuild the mesh live |
| **Trash Can** | `TrashCan` | Idle glow brightness, pulse amplitude and speed |
| **Lighting** | 1 directional, 1 spot, 3 points | Only the desk spot casts shadows (keeps the GPU cool) |
| **Post Processing (Global Volume)** | Volume | Click the profile to adjust Bloom intensity, etc. |
| **Holograms** | (empty) | Holograms are spawned here at runtime |

To adjust the hologram look, select `Generated/Materials/Hologram.mat`: Fill Color, Edge Color,
Fill Alpha, Scanline density/speed/strength. Colors marked **HDR** with intensity above 1 are what
makes Bloom glow.

## 7. Test it

Press **Play** (▶ at the top). Then check each item:

| # | Do this | Expect |
|---|---|---|
| 1 | Just look | Dark lab, glowing curved desk edge, 6 cyan holograms (3 at eye level, 2 lower, 1 off to the left further back), trash can glowing softly to the right |
| 2 | Hold the mouse on empty space (or the desk) and drag sideways | View turns smoothly and stops at 90° left/right |
| 3 | Hold **← / →** | Same smooth turning |
| 4 | Hover a hologram | It brightens and grows slightly |
| 5 | Drag a hologram | It follows the cursor in 3D, keeps facing you, and **the camera does not turn** |
| 6 | While dragging, scroll | The hologram moves farther or closer |
| 7 | Double-click a hologram | It flashes; the Console logs `Edit requested for "…"`. The real editor comes in Phase 3 |
| 8 | Drag a hologram, then immediately click it once | **No** edit request: a drag cancels a pending double-click |
| 9 | Click the small pencil (top-right of a panel) | Edit request logged |
| 10 | Press **N** a few times | New "Test task N" holograms appear in the next free slots |
| 11 | Hover one, press **Shift+Backspace** | It disappears and the others glide to fill the gap |
| 12 | Drag 2 holograms somewhere odd, **stop Play**, press Play again | They're exactly where you left them; the rest are in their slots |
| 13 | **Shift+R** | Dragged holograms glide back into the automatic layout |
| 14 | **Cypher → Open Save Folder** | Finder shows `tasks.json` (readable JSON) and `tasks.json.bak` |

To start over with fresh sample tasks: **Cypher → Delete Saved Tasks** (keeps a `.bak`), then Play.

**Performance check:** in the Game view, click **Stats**. On an M-series Air you should be well
above 60 FPS in the editor.

## 8. How the scripts fit together

```
TaskRepository (plain C#)  ←  owns the list, reads/writes tasks.json safely (temp file + .bak)
        ↑
TaskManager (Systems)      ←  spawns one HologramPanel per task, lays them out, saves on change
        ↑                     exposes AddTask / RemoveTask / NotifyTaskChanged / EditRequested
InteractionController      ←  the only script that reads the mouse: hover, click, double-click,
        │                     drag, or camera rotation (one state at a time, so they never collide)
        ├─→ HologramPanel   ←  per-hologram visuals: text, glide, face-the-viewer, hover glow
        └─→ CameraRigController ← clamped, damped yaw from mouse drag + arrow keys
HologramLayout             ←  rank → position (front 3, second row 2, then a ring further back)
InputLock                  ←  later phases "take" the input (typing, voice) so nothing else reacts
```

Things later phases plug into:
- `TaskManager.EditRequested`: the Phase 3 edit panel subscribes to this.
- `TaskItem` already contains every field the spec needs (notes, reminder, difficulty, source,
  external ID, done), so the save format won't need migrating.
- `HologramLayout.GetSlot(rank)`: Phase 6 feeds it the smart ranking instead of list order.
- `TrashCan.MouthPosition`: the target for the Phase 5 throw.

## 9. Troubleshooting

| Symptom | Fix |
|---|---|
| Menu **Cypher** doesn't appear | There's a compile error. Fix the red errors in the Console first. |
| "TextMeshPro not set up" dialog | Do step 4.4, then rebuild. |
| Holograms are **pink/magenta** | Shader error. Select `Shaders/Hologram.shader` and read the Inspector error, and check that the project uses URP (template *Universal 3D*). |
| Nothing reacts to the mouse; Console says `Mouse.current` / InputSystem errors | Step 4.2: set Active Input Handling to *Input System Package (New)* or *Both*. |
| Text is missing on holograms | TMP essentials weren't imported before building. Import, then rebuild. |
| No glow at all | Camera → *Rendering → Post Processing* must be ticked; the Global Volume must have the profile assigned. Also check *Project Settings → Graphics/Quality*: the active URP asset needs **HDR** on (it is in the template). |
| Rotation direction feels backwards | CameraRig → untick *Drag Moves World*. |
| Holograms too far or too close | Edit the numbers in `HologramLayout.cs` (radius/height per row). |

---

When every row in section 7 passes, ask for **Phase 2** (hologram flicker and noise, VFX Graph
particles, the wireframe → solid → glow materialize effect, and procedural ambient sound).
