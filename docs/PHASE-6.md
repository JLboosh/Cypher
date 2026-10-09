# Phase 6: Reminders and smart ranking

**Result:** Cypher keeps your most important tasks right in front of you and re-ranks them as
things change and deadlines approach. Reminders arrive three ways: Cypher says them out loud,
the hologram pulses, and macOS shows a notification. You can answer "snooze", "in 20 minutes" or
"mark it done".

## Smart ranking (`Core/TaskRanker.cs`)

1. **Urgency** grows as a deadline gets closer. Harder tasks get more lead time: a
   difficulty-5 task due in 3 days counts as urgent as an easy task due tomorrow. Overdue tasks
   are the most urgent; tasks without a deadline sit low.
2. **Priority class:** tasks you set to Low/Medium/High keep that class (**your setting always
   wins**). "Let Cypher decide" tasks get High (urgency ≥ 0.5), Medium (≥ 0.2) or Low. The hologram
   shows what Cypher decided, e.g. **AUTO · HIGH**.
3. **Order:** by class, then urgency, then oldest first. Done tasks go to the back.

Example (tested): an overdue task leads; your manual-High task comes next; an easy
"call the dentist" due tomorrow and a hard thesis chapter due in 3 days are both auto-High; and a task
you set to Low stays in the Low group even when it's due in 2 hours, because manual priority wins.

**Layout:** the top 5 float in front of the desk (3 at eye level, 2 below); the rest form a ring
further back and to the sides, scaled up to stay readable. Holograms glide to their new places.

**Re-ranking happens:** when you add, edit or remove a task, and every 30 seconds as time passes
(labels like "DUE TOMORROW" → "DUE TODAY" update too).

**Holograms you dragged:** the first time a re-rank happens while one is hand-placed, Cypher asks
once, out loud and with two buttons: **Keep my placement** or **Let Cypher arrange**. You can
answer by voice ("keep" / "arrange them"). The answer is saved. Later, "Hey Cypher, arrange my
holograms" switches to Cypher arranging; Shift+R resets dragged ones once.

## Reminders (`Reminders/ReminderService.cs`, `MacNotifier.cs`)

- Checked every second. When one comes due: the hologram pulses for 10 s, a macOS notification
  appears (with the "Glass" sound), and Cypher speaks: *"Reminder: Finish lab report, due Friday."*
- Then for ~8 seconds you can reply without the wake word:
  - "snooze" / "later" → again in 10 minutes
  - "in 20 minutes" / "at 6pm" / "tomorrow" → again then
  - "mark it done" / "I finished it" → marks it done
  - "okay" / "thanks" → nothing more
- **Missed reminders:** if the app was closed when a reminder came due, Cypher announces them
  together a few seconds after launch ("While you were away...").
- **While minimized:** the app keeps running (*Run In Background*), so reminders still speak and
  notify. In the **editor**, Unity slows down a bit when it isn't the focused app, so a reminder
  may arrive a second or two late; the built app (Phase 8) doesn't have this.

### Also fixed in this phase
A **time-zone bug** from Phase 1: dates made from a calendar day (like the date picker's) were
saved without a time zone and read back as UTC, so in New York **a 9:00 AM reminder would have
fired at 5:00 AM**. Dates are now always stored and read as local time; dates already in your
save file read back as the times you intended. Also fixed: voice understanding of "Add, call
Sherlock." (comma after the verb), "add a..." no longer creates a task named "A", saying the wake
word twice works, and the Console no longer fills with "Curl error 7" when Ollama isn't installed.

## Setup

1. Switch to Unity and let it compile. Check the Console for red errors.
2. Press **Play**.
3. **Notifications:** the first time one is sent, macOS may ask to allow notifications for
   **Script Editor** (that's the built-in tool that displays them). If you never see one:
   **System Settings → Notifications → Script Editor → Allow Notifications**.

## Test

| # | Do this | Expect |
|---|---|---|
| 1 | Look at the holograms | Each "Let Cypher decide" task shows **AUTO · HIGH/MED/LOW**. Front row = most important |
| 2 | Edit a back-row task: set priority **High** and a due date of tomorrow, Save | Shimmer, then it glides to the front and others shift back |
| 3 | Hover a task, press **Shift+M**, wait 10 s | Hologram pulses, notification appears top-right, Cypher says "Reminder: ..." |
| 4 | Right after (no wake word): "snooze" | "Okay, I'll remind you again at ..." (10 minutes later) |
| 5 | Shift+M again, then answer "mark it done" | Task dims with a strikethrough and moves to the back |
| 6 | Shift+M, then **minimize Unity** (⌘M) and wait | Notification + voice still arrive |
| 7 | Shift+M, stop Play within 10 s, wait 15 s, press Play | After ~4 s: "While you were away, a reminder came due: ..." |
| 8 | Drag a hologram somewhere, then press **N** (adds a task, which re-ranks) | Question appears + Cypher asks: keep or arrange? Answer by clicking or voice |
| 9 | Choose **Let Cypher arrange**, drag one again, press **N** | No question; the dragged one returns to its ranked slot |
| 10 | "Hey Cypher, what's most important?" | Names the front-center task; it flashes |

## Tuning

| What | Where |
|---|---|
| How fast urgency rises, the High/Medium thresholds | `Core/TaskRanker.cs` (`leadHours`, `HighThreshold`, `MediumThreshold`) |
| Re-rank interval | Systems → TaskManager → *Rerank Interval Seconds* |
| Slot positions (front row, back ring) | `Holograms/HologramLayout.cs` |
| Reminder pulse length, startup delay | Systems → ReminderService |
| Notification on/off, sound | `cypher-config.json`: `macNotifications`, `notificationSound` (Glass, Ping, Pop, Hero, Submarine...) |
| Ask the placement question again | Remove `"placementMode"` from `tasks.json` (Cypher → Open Save Folder) |

---

When everything works, ask for **Phase 7**: importing from **Google Calendar / Google Tasks** and
**Notion**, with duplicate protection and optional two-way sync. That phase needs a free Google
Cloud project and a Notion integration; I'll walk you through both.
