# Phase 3: The edit panel

**Result:** double-click a hologram (or click its pencil) and it expands and flies toward you,
becoming a brighter holographic edit panel. Change any field with your real keyboard and mouse,
then Save (it shrinks back into place with a "data sync" shimmer), Cancel/Esc (discard), or Delete.

## What changed

| File | Change |
|---|---|
| `Scripts/UI/EditPanelController.cs` | **New.** The edit panel: builds its own UI, open/close animation, all fields, save/cancel/delete, the "update the original?" question, voice-ready API |
| `Scripts/UI/HoloUI.cs` | **New.** Small factory for holographic buttons, toggles, segmented choices and text boxes |
| `Scripts/UI/GlowCaret.cs` | **New.** The glowing, blinking text cursor |
| `Scripts/UI/HoloDatePicker.cs` | **New.** Holographic month calendar with Today / Tomorrow / +1 week shortcuts |
| `Shaders/UIGlow.shader` | **New.** Glowing outlines and fills for UI elements |
| `HologramPanel.cs` | Hides while being edited; plays the sync shimmer after a save |
| `TaskItem.cs` | `pendingSync` flag (for imported tasks) and a helper to copy edited fields |
| `TaskManager.cs` | Creates the edit panel automatically if the scene doesn't have one |
| `CypherAudio.cs`, `ProceduralSfx.cs` | Open, close, sync and error sounds |
| `DebugTaskHotkeys.cs` | **Shift+I** fakes an imported task so you can test the sync question |
| `WorkshopSceneBuilder.cs` | Adds the edit panel, an EventSystem and the UI materials to the scene |

## Setup

1. Switch to Unity and wait for it to recompile. The Console should have no red errors.
2. Press **Play**. The edit panel creates itself, so this works right away.
3. Later, when convenient: stop Play and run **Cypher → Build Workshop Scene → Build**.
   This puts the edit panel and its materials into the scene file, which Phase 8 needs for the
   standalone app. Rebuilding regenerates the scene and prefab, but keeps tasks and materials.

## How to use it

| Field | How |
|---|---|
| Title / Notes | Click and type. Arrow keys, Shift-select, ⌘C / ⌘V / ⌘A all work. |
| Due date | Click the date to open the calendar on the right; click a day. **Undetermined** removes the deadline. |
| Reminder | Only shown when there's a due date. Click **Set reminder**, pick a day; it defaults to 9:00 AM. Type a time like `9:30`, `7pm`, `21:15`, or `0930`. **No reminder** clears it. |
| Priority | Low / Medium / High / Let Cypher decide. |
| Difficulty | 1 (trivial) to 5 (hard). |
| Mark as done | The hologram dims and the title gets a strikethrough. Done tasks move to the back. |
| **Save** | Or **⌘+Enter**. |
| **Cancel** | Or **Esc**. Esc first closes the calendar or the sync question if one is open. |
| **Delete** | Click once ("Confirm?"), then again within 3 seconds. |

## Test

| # | Do this | Expect |
|---|---|---|
| 1 | Double-click a hologram | It flies toward you and grows into the edit panel; the original hologram disappears from its spot; whoosh sound. The title field is focused with a glowing blinking cursor at the end |
| 2 | Type in the title, use arrow keys | Cursor is solid while typing, blinks when you pause, follows arrows. The camera does **not** turn |
| 3 | Type "n" in the title | It types "n"; **no** test task gets added |
| 4 | Click the due date → pick a day next week | Calendar appears on the right, today has an amber underline; date updates, calendar closes |
| 5 | Tick **Undetermined** | Due shows "No deadline"; the reminder row is replaced by a hint |
| 6 | Untick it, set a reminder, type `7pm`, click elsewhere | Shows "7:00 PM" |
| 7 | Type `banana` as the time and Save | Time box flashes orange with an error buzz; the panel stays open |
| 8 | Change priority to High, difficulty to 4, **Save** | Panel flies back and shrinks into the hologram's spot, which re-scans with a shimmer and three-note chime. The hologram shows "PRI · HIGH" and the new due date |
| 9 | Open again, change the title, press **Esc** | Panel closes; title unchanged |
| 10 | Open, click **Save** without changing anything | Closes quietly, no shimmer |
| 11 | Open, **Delete** → **Confirm?** → click again | Panel flies back and the hologram dematerializes; others fill the gap |
| 12 | Hover a hologram, **Shift+I** (badge says GCAL), open it, edit, Save | A question appears: "This task came from Google Calendar. Apply this edit to the original too?" Both buttons save. The actual syncing comes in Phase 7 |
| 13 | Click the pencil icon | Same as double-click |
| 14 | Stop Play, Play again | Edits are still there |

## Tuning

On **Edit Panel → EditPanelController** (Inspector, while in the scene after a rebuild, or on the
auto-created object during Play):

| Setting | Effect |
|---|---|
| Distance / Vertical Offset | How close and how high the panel floats |
| Open / Close Seconds | Animation speed |
| Panel Brightness | How much brighter than a resting hologram |
| Delete Confirm Seconds | How long "Confirm?" waits |

UI colors: `Generated/Materials/UIGlowFrame.mat` (outlines; *Intensity*, *Fill Alpha*, *Border
Width*) and `UIGlowFlat.mat` (selected fills and the cursor). These exist after a scene rebuild.

## Ready for voice (Phase 4)

`EditPanelController` exposes `SetTitle`, `SetNotes`, `SetDueDate`, `SetReminder`,
`SetPriority`, `SetDifficulty`, `SetDone`, `Save`, `Cancel` and `Delete`. Voice commands will
call these, and the panel updates live. `EditPanelController.IsTyping` tells the voice system
to stop listening while you type.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Buttons/boxes are invisible or pink | The `UIGlow` shader failed to compile. Select `Shaders/UIGlow.shader` and send me the error |
| Clicking the panel does nothing | Check that the Hierarchy has an **EventSystem** with **Input System UI Input Module** during Play |
| No glowing cursor, but typing works | Send me a screenshot; the cursor position comes from TextMeshPro's layout |
| Panel is cut off at the screen edge | Increase *Distance* (e.g. 0.75) |

---

When everything works, ask for **Phase 4**: the Cypher voice pipeline (wake word →
speech-to-text → AI → text-to-speech), with the add-task and edit-by-voice flows. That phase
needs some free accounts (Picovoice and Google AI Studio), and I'll walk you through each one.
