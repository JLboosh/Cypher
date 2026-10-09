# Phase 8: The standalone macOS app

**Result:** `~/cypher/Builds/Cypher.app`, a normal Mac app with its own icon. Double-click to
launch; it runs without Unity, keeps reminders and voice working while minimized, and uses the
same tasks, settings and Google connection you set up in the editor.

## What the build step does (`Assets/Cypher/Editor/MacAppBuilder.cs`)

| Step | Why |
|---|---|
| Generates an **app icon** (navy rounded square, glowing cyan "C" ring) | `Assets/Cypher/Generated/AppIcon.png`; replace it with your own 1024×1024 PNG if you like |
| Player settings: windowed 1600×1000, resizable, full-screen allowed (green button), Retina, **Run In Background**, **no splash screen** | Unity 6 Personal lets you turn off the "Made with Unity" splash |
| Mono scripting backend | Fast, reliable builds; plenty fast for this app |
| Adds every runtime-loaded shader to **Always Included Shaders** | The app looks some shaders up by name; without this they'd be stripped (pink/invisible UI) |
| Patches **Info.plist**: microphone permission text, **App Nap disabled** (reminders fire on time while minimized), display name "Cypher", Productivity category | macOS requires a reason for microphone access, and App Nap would otherwise throttle a background app |
| Re-signs the app **ad-hoc** (`codesign --sign -`) | Editing Info.plist breaks Unity's signature; ad-hoc signing is free and fine for your own Mac |

Data stays in `~/Library/Application Support/DefaultCompany/CypherWorkshop/`, the same place
Play mode uses, so the app and the editor share your tasks and config.

## Build it

1. In Unity: **Cypher → Build Workshop Scene → Build** (needed once, so the scene contains the
   new workshop and references all materials).
2. **Cypher → Build macOS App**. The first build takes a few minutes (shader compilation);
   later ones are faster.
3. A dialog says **Cypher is built** → **Show in Finder**. Optional: drag `Cypher.app` into
   **Applications**.

## First launch

1. Double-click **Cypher.app**. (It was built on this Mac, so macOS opens it without the
   "unidentified developer" warning.)
2. **Microphone:** click **Allow** when asked. (If you clicked Don't Allow:
   System Settings → Privacy & Security → Microphone → Cypher: on, then reopen.)
3. **Notifications** still come through *Script Editor* (allowed in Phase 6).
4. Say "Hey Cypher". Close the window or press **⌘Q** to quit; **⌘M** minimizes (still listening
   and reminding).

### Start Cypher when you log in (optional)
System Settings → General → **Login Items** → **+** → choose `Cypher.app`.

## Test

| # | Do this | Expect |
|---|---|---|
| 1 | Launch the app | Window with the workshop; holograms build in; voice emblem says "Say 'Hey Cypher'" |
| 2 | Allow the microphone, say "Hey Cypher, what's on my list?" | Spoken answer (Piper voice) |
| 3 | Edit, import, crumple-and-throw | Same as in the editor; no pink or invisible UI |
| 4 | Set a test reminder (hover a task, **Shift+M**), then **⌘M** | Notification + spoken reminder while minimized |
| 5 | Quit and relaunch | Same tasks as in the editor |
| 6 | Dock / app switcher | Shows the Cypher icon and name |

## Updating the app

Change anything in Unity, then run **Cypher → Build macOS App** again; it overwrites
`Builds/Cypher.app`. (Quit the running app first.)

## Troubleshooting

| Symptom | Fix |
|---|---|
| "Build failed" | Check the Console: usually a compile error or a missing scene. Run *Build Workshop Scene* first |
| Pink or invisible panels in the app only | A shader failed to compile in Unity. Check the Console for "Shader error", then rebuild |
| No voice reaction in the app | Microphone permission (see First launch). The Console isn't visible in the app; its log is at `~/Library/Logs/DefaultCompany/CypherWorkshop/Player.log` |
| Cypher's voice is the macOS one | Piper is found via `~/cypher/tools/...`; keep that folder where it is (or update the paths in the config) |
| "Cypher is damaged and can't be opened" | Only happens if the app was downloaded or copied with a quarantine flag. Fix: `xattr -cr ~/cypher/Builds/Cypher.app` |
